"""Small test-only RTSP/RTP camera serving synthetic H.264 over TCP."""
import base64
import re
import socketserver
import struct
import threading
import time


def start_camera(h264):
    units = [unit for unit in re.split(b"\x00\x00\x00?\x01", h264) if unit]
    sps = next(unit for unit in units if unit[0] & 31 == 7)
    pps = next(unit for unit in units if unit[0] & 31 == 8)
    parameters = base64.b64encode(sps).decode() + ',' + base64.b64encode(pps).decode()

    class Camera(socketserver.StreamRequestHandler):
        def handle(self):
            lock = threading.Lock()
            stopped = threading.Event()
            started = False

            def send_media():
                sequence, timestamp = 0, 0
                try:
                    while not stopped.is_set():
                        for unit in units:
                            kind = unit[0] & 31
                            if kind == 9:
                                time.sleep(1 / 15)
                                timestamp += 6000
                            if len(unit) <= 1200:
                                payloads = [unit]
                            else:
                                chunks = [unit[i:i + 1198] for i in range(1, len(unit), 1198)]
                                payloads = [bytes([(unit[0] & 0xe0) | 28, kind | (0x80 if i == 0 else 0) | (0x40 if i == len(chunks) - 1 else 0)]) + chunk for i, chunk in enumerate(chunks)]
                            for i, payload in enumerate(payloads):
                                marker = 0x80 if kind in (1, 5) and i == len(payloads) - 1 else 0
                                packet = struct.pack('!BBHII', 0x80, 96 | marker, sequence & 0xffff, timestamp & 0xffffffff, 1234) + payload
                                with lock:
                                    self.request.sendall(b'$\x00' + struct.pack('!H', len(packet)) + packet)
                                sequence += 1
                                if stopped.is_set(): return
                except (ConnectionError, OSError):
                    stopped.set()

            try:
                while True:
                    first = self.rfile.read(1)
                    if not first: break
                    if first == b'$':
                        header = self.rfile.read(3)
                        if len(header) != 3: break
                        self.rfile.read(struct.unpack('!H', header[1:])[0]); continue
                    line = first + self.rfile.readline()
                    method = line.decode().split(' ')[0]
                    headers = {}
                    while True:
                        line = self.rfile.readline()
                        if line in (b'\r\n', b''): break
                        key, value = line.decode().split(':', 1)
                        headers[key.lower()] = value.strip()
                    body, extra = b'', ''
                    if method == 'DESCRIBE':
                        body = ('v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\ns=Synthetic ONVIF camera\r\nt=0 0\r\na=control:*\r\nm=video 0 RTP/AVP 96\r\nc=IN IP4 127.0.0.1\r\na=rtpmap:96 H264/90000\r\na=fmtp:96 packetization-mode=1;sprop-parameter-sets=' + parameters + '\r\na=control:trackID=0\r\n').encode()
                        extra = f'Content-Type: application/sdp\r\nContent-Base: rtsp://127.0.0.1:{self.server.server_address[1]}/live/\r\n'
                    elif method == 'SETUP':
                        extra = 'Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\nSession: fixture;timeout=60\r\n'
                    elif method == 'OPTIONS':
                        extra = 'Public: OPTIONS, DESCRIBE, SETUP, PLAY, GET_PARAMETER, TEARDOWN\r\n'
                    else:
                        extra = 'Session: fixture\r\n'
                    response = f"RTSP/1.0 200 OK\r\nCSeq: {headers.get('cseq', '1')}\r\n{extra}Content-Length: {len(body)}\r\n\r\n".encode() + body
                    with lock: self.request.sendall(response)
                    if method == 'PLAY' and not started:
                        started = True
                        threading.Thread(target=send_media, daemon=True).start()
                    if method == 'TEARDOWN': break
            except (ConnectionError, OSError): pass
            finally: stopped.set()

    server = socketserver.ThreadingTCPServer(('127.0.0.1', 0), Camera)
    server.daemon_threads = True
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


def soap_response(operation, origin, rtsp_port):
    ns = 'http://www.onvif.org/ver10/device/wsdl'
    media = 'http://www.onvif.org/ver10/media/wsdl'
    if operation == 'GetSystemDateAndTime':
        payload = '<d:GetSystemDateAndTimeResponse/>'
    elif operation == 'GetServices':
        payload = f'<d:GetServicesResponse><d:Service><d:Namespace>{media}</d:Namespace><d:XAddr>{origin}/onvif/media</d:XAddr></d:Service></d:GetServicesResponse>'
    elif operation == 'GetProfiles':
        payload = '<m:GetProfilesResponse><m:Profiles token="synthetic"><t:Name>Synthetic H264</t:Name><t:VideoEncoderConfiguration><t:Encoding>H264</t:Encoding></t:VideoEncoderConfiguration></m:Profiles></m:GetProfilesResponse>'
    elif operation == 'GetStreamUri':
        payload = f'<m:GetStreamUriResponse><m:MediaUri><t:Uri>rtsp://127.0.0.1:{rtsp_port}/live</t:Uri></m:MediaUri></m:GetStreamUriResponse>'
    else:
        payload = '<s:Fault/>'
    return f'<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:d="{ns}" xmlns:m="{media}" xmlns:t="http://www.onvif.org/ver10/schema"><s:Body>{payload}</s:Body></s:Envelope>'.encode()

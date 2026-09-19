export type SettingLike = { key?: string; value?: unknown; subgroup?: string; group?: string };
export type Broker = { host: string; port: number; tls: boolean; username: string; password: string };

export function hostName(value: string): string {
  if (!value || value.length > 253 || /[\s/@?#]/.test(value)) throw new Error('Enter a Scrypted LAN hostname or IP address without a port or scheme.');
  const raw = value.replace(/^\[|\]$/g, '');
  const host = raw.includes(':') ? `[${raw}]` : raw;
  let url: URL;
  try { url = new URL(`http://${host}`); } catch { throw new Error('Invalid LAN hostname or IP address.'); }
  if (url.port || ['localhost', '127.0.0.1', '[::1]', '0.0.0.0', '[::]'].includes(url.hostname.toLowerCase()))
    throw new Error('Use the Scrypted address reachable from the RTSPView computer.');
  return url.hostname;
}

export function rebroadcasts(settings: SettingLike[], host: string) {
  const hostname = hostName(host);
  return settings.filter(s => s.key === 'rtspRebroadcastUrl' || s.key?.endsWith(':rtspRebroadcastUrl')).map((s, i) => {
    let url: URL;
    try { url = new URL(String(s.value)); } catch { throw new Error('Scrypted returned an invalid rebroadcast URL.'); }
    if (url.protocol !== 'rtsp:') throw new Error('Scrypted returned an unsupported rebroadcast URL.');
    if (['localhost', '127.0.0.1', '[::1]', '0.0.0.0', '[::]'].includes(url.hostname)) url.hostname = hostname;
    return { label: `${s.subgroup || 'Stream'} (${i + 1})`, url: url.toString() };
  });
}

export function brokerFromSettings(settings: SettingLike[], host: string): Broker {
  const get = (key: string) => settings.find(s => s.key === key)?.value;
  const enabled = get('enableBroker') === true || get('enableBroker') === 'true';
  const address = enabled ? `mqtt://localhost:${get('tcpPort') || 1883}` : String(get('externalBroker') || '');
  return parseBroker(address, String(get('username') || ''), String(get('password') || ''), host);
}

export function parseBroker(address: string, username: string, password: string, host: string): Broker {
  let url: URL;
  try { url = new URL(address); } catch { throw new Error('Configure the Scrypted MQTT broker or enter a broker override.'); }
  if (!['mqtt:', 'mqtts:'].includes(url.protocol)) throw new Error('RTSPView requires MQTT TCP or MQTT TLS, not WebSockets.');
  if (['localhost', '127.0.0.1', '[::1]', '0.0.0.0', '[::]'].includes(url.hostname)) url.hostname = hostName(host);
  return { host: url.hostname.replace(/^\[|\]$/g, ''), port: Number(url.port || (url.protocol === 'mqtts:' ? 8883 : 1883)),
    tls: url.protocol === 'mqtts:', username: username || decodeURIComponent(url.username), password: password || decodeURIComponent(url.password) };
}

export function controllerAddress(address: string) {
  let url: URL;
  try { url = new URL(address); } catch { throw new Error('Enter the full RTSPView address, such as http://viewer-host:5080.'); }
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash || url.pathname !== '/')
    throw new Error('Use an HTTP or HTTPS RTSPView address without credentials, path, or query.');
  return url.origin;
}

export function detectionTopic(instance: string, id: string) {
  return `rtspview/${instance}/${encodeURIComponent(id)}/ObjectDetector`;
}

export function freshDetection(data: unknown, now = Date.now()): boolean {
  const event = data as { timestamp?: number; detections?: unknown[] } | null;
  return !!event && typeof event.timestamp === 'number' && Number.isFinite(event.timestamp) &&
    event.timestamp >= now - 30000 && event.timestamp <= now + 5000 && Array.isArray(event.detections);
}

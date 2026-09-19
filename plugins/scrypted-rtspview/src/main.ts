import sdk, { ScryptedDeviceBase, ScryptedInterface, Setting, Settings, SettingValue, EventListenerRegister } from '@scrypted/sdk';
import { randomUUID } from 'crypto';
import mqtt, { MqttClient } from 'mqtt';
import { Broker, brokerFromSettings, controllerAddress, detectionTopic, freshDetection, parseBroker, rebroadcasts } from './config';

class Connector extends ScryptedDeviceBase implements Settings {
  private client?: MqttClient;
  private listeners: EventListenerRegister[] = [];
  private status = 'Not paired. Create a code in RTSPView → System → Network & security.';
  private queue: Promise<unknown> = Promise.resolve();
  constructor() {
    super();
    if (!this.storage.getItem('instanceId')) this.storage.setItem('instanceId', randomUUID());
    this.queue = this.restore().catch(() => { this.status = 'Event forwarding could not start. Check broker settings and sync again.'; });
    setInterval(() => {
      this.queue = this.queue.then(async () => { if (!this.client) await this.restore(); })
        .catch(() => { this.status = 'MQTT unavailable; retrying the last synced broker.'; });
    }, 30000).unref();
  }
  private get instanceId() { return this.storage.getItem('instanceId')!; }
  private get selected(): string[] { return JSON.parse(this.storage.getItem('cameras') || '[]'); }
  private device(id: string) { return sdk.systemManager.getDeviceById<Settings>(id); }
  private async broker() {
    const host = this.storage.getItem('scryptedHost') || '';
    const override = this.storage.getItem('brokerUrl');
    if (override) return parseBroker(override, this.storage.getItem('brokerUsername') || '', this.storage.getItem('brokerPassword') || '', host);
    const provider = this.device('@scrypted/mqtt');
    if (!provider) throw new Error('Install and configure the Scrypted MQTT plugin, or supply a broker override.');
    return brokerFromSettings(await provider.getSettings(), host);
  }
  async getSettings(): Promise<Setting[]> {
    const settings: Setting[] = [
      { key: 'status', title: 'Status', value: this.status, readonly: true },
      { key: 'address', title: 'RTSPView address', value: this.storage.getItem('address') || '', placeholder: 'http://viewer-host:5080' },
      { key: 'scryptedHost', title: 'Scrypted LAN hostname or IP', value: this.storage.getItem('scryptedHost') || '', description: 'Address reachable from RTSPView; used to replace localhost in rebroadcast and broker URLs.' },
      { key: 'pairCode', title: 'Pairing code', type: 'password', value: '', description: 'Paste the one-time code from RTSPView and save this setting to pair. The code is not stored.' },
      { key: 'cameras', title: 'Cameras to sync', type: 'device', multiple: true, deviceFilter: `interfaces.includes('${ScryptedInterface.VideoCamera}')`, value: this.selected },
      { key: 'sync', title: 'Sync to RTSPView', type: 'button', description: 'Save selected streams and MQTT settings. Existing rules and layouts are preserved. Reload the RTSPView dashboard after syncing.' },
      { key: 'brokerUrl', title: 'Broker override URL', group: 'Advanced', value: this.storage.getItem('brokerUrl') || '', description: 'Optional mqtt:// or mqtts:// URL. Leave empty to read the official Scrypted MQTT plugin settings.' },
      { key: 'brokerUsername', title: 'Broker override username', group: 'Advanced', value: this.storage.getItem('brokerUsername') || '' },
      { key: 'brokerPassword', title: 'Broker override password', group: 'Advanced', type: 'password', value: this.storage.getItem('brokerPassword') || '' },
    ];
    for (const id of this.selected) {
      const camera = this.device(id);
      if (!camera) continue;
      try {
        const streams = rebroadcasts(await camera.getSettings(), this.storage.getItem('scryptedHost') || '');
        settings.push({ key: `stream:${id}`, title: `${camera.name} stream`, group: 'Camera streams',
          choices: streams.map(s => s.label), value: this.storage.getItem(`stream:${id}`) || streams[0]?.label,
          description: 'Choose a Scrypted rebroadcast stream. Enable the camera’s rebroadcast/Stream Management extension if no streams are available.' });
      } catch { /* Sync reports missing camera/stream settings with an actionable error. */ }
    }
    return settings;
  }
  async putSetting(key: string, value: SettingValue): Promise<void> {
    const task = this.queue.then(async () => {
      if (key === 'pairCode') {
        const result = await this.request('/connector/v1/pair', {code: String(value).trim(), instanceId: this.instanceId}, false);
        if (typeof result.token !== 'string') throw new Error('RTSPView did not return a pairing token.');
        this.storage.setItem('token', result.token);
        this.status = 'Paired. Select cameras and click Sync to RTSPView.';
      } else if (key === 'sync') await this.sync();
      else {
        if (!['address', 'scryptedHost', 'cameras', 'brokerUrl', 'brokerUsername', 'brokerPassword'].includes(key) && !key.startsWith('stream:'))
          throw new Error('Unknown connector setting.');
        if (key === 'address') {
          value = controllerAddress(String(value));
          if (value !== this.storage.getItem(key)) { this.storage.removeItem('token'); this.stop(); this.storage.removeItem('active'); }
        }
        if (key === 'cameras' && (!Array.isArray(value) || value.length > 16 || value.some(id => typeof id !== 'string')))
          throw new Error('Select up to 16 cameras.');
        this.storage.setItem(key, key === 'cameras' ? JSON.stringify(value) : String(value));
        this.status = 'Settings saved. Pair if needed, then sync to apply changes.';
      }
      await this.onDeviceEvent(ScryptedInterface.Settings, undefined);
    });
    this.queue = task.catch(() => {});
    return task;
  }
  private async request(path: string, body: object, authorized = true) {
    const token = this.storage.getItem('token');
    if (authorized && !token) throw new Error('Pair this connector with RTSPView first.');
    let response: Response;
    try {
      response = await fetch(controllerAddress(this.storage.getItem('address') || '') + path, {
        method: 'POST', headers: { 'Content-Type': 'application/json', ...(authorized ? {Authorization: `Bearer ${token}`} : {}) },
        body: JSON.stringify(body), signal: AbortSignal.timeout(15000), redirect: 'error',
      });
    } catch { throw new Error('RTSPView is unreachable. Check its address, LAN access, and HTTPS certificate if used.'); }
    if (response.status === 401) throw new Error('Pairing expired or access was revoked. Generate a fresh code in RTSPView.');
    if (!response.ok) {
      // RTSPView errors are deliberately sanitized; never log request bodies or tokens.
      const data = await response.json().catch(() => ({})) as {error?: string};
      throw new Error(data.error || `RTSPView rejected the request (${response.status}).`);
    }
    return await response.json() as {token?: string; imported?: number};
  }
  private async connect(broker: Broker): Promise<MqttClient> {
    const host = broker.host.includes(':') ? `[${broker.host}]` : broker.host;
    const client = mqtt.connect(`${broker.tls ? 'mqtts' : 'mqtt'}://${host}:${broker.port}`, {
      username: broker.username || undefined, password: broker.password || undefined,
      clientId: `rtspview-${randomUUID().slice(0, 18)}`, clean: true, protocolVersion: 4,
      queueQoSZero: false, reconnectPeriod: 2000, connectTimeout: 10000, rejectUnauthorized: true,
    });
    client.on('error', () => { this.status = 'MQTT connection error. Check broker access and sync settings.'; });
    try {
      await new Promise<void>((resolve, reject) => {
        const timeout = setTimeout(() => { cleanup(); reject(new Error('MQTT connection timed out.')); }, 12000);
        const ready = () => { cleanup(); resolve(); };
        const failed = () => { cleanup(); reject(new Error('MQTT connection failed. Check host, credentials and certificate.')); };
        const cleanup = () => { clearTimeout(timeout); client.off('connect', ready); client.off('error', failed); };
        client.once('connect', ready); client.once('error', failed);
      });
      return client;
    } catch (e) { client.end(true); throw e; }
  }
  private stop() {
    for (const listener of this.listeners) listener.removeListener();
    this.listeners = []; this.client?.end(true); this.client = undefined;
  }
  private forward(ids: string[], client: MqttClient) {
    this.stop(); this.client = client;
    client.on('connect', () => { this.status = 'MQTT connected. Forwarding fresh detection events.'; });
    client.on('offline', () => { this.status = 'MQTT offline; events are discarded until reconnected.'; });
    for (const id of ids) {
      const camera = this.device(id);
      if (!camera?.interfaces.includes(ScryptedInterface.ObjectDetector)) continue;
      this.listeners.push(camera.listen(ScryptedInterface.ObjectDetector, (_source, details, event) => {
        if (!client.connected || details.eventInterface !== ScryptedInterface.ObjectDetector || !freshDetection(event)) return;
        const payload = JSON.stringify(event);
        if (Buffer.byteLength(payload) > 65536) return;
        client.publish(detectionTopic(this.instanceId, id), payload, {qos: 0, retain: false}, () => {});
      }));
    }
  }
  private async sync() {
    if (!this.storage.getItem('token')) throw new Error('Pair this connector with RTSPView first.');
    const ids = [...new Set(this.selected)];
    if (!ids.length) throw new Error('Select at least one camera.');
    const cameras = [];
    for (const id of ids) {
      const camera = this.device(id);
      if (!camera) throw new Error('A selected camera is no longer available. Reselect cameras.');
      const streams = rebroadcasts(await camera.getSettings(), this.storage.getItem('scryptedHost') || '');
      const selection = this.storage.getItem(`stream:${id}`);
      const stream = selection ? streams.find(s => s.label === selection) : streams[0];
      if (!stream) throw new Error('A selected camera has no matching rebroadcast stream. Check Stream Management and the stream selector.');
      cameras.push({id, name: camera.name, rtspUrl: stream.url,
        topic: camera.interfaces.includes(ScryptedInterface.ObjectDetector) ? detectionTopic(this.instanceId, id) : ''});
    }
    const broker = await this.broker();
    const client = await this.connect(broker);
    try {
      await this.request('/connector/v1/sync', {version: 1, instanceId: this.instanceId, cameras, broker});
      this.storage.setItem('active', JSON.stringify({ids, broker}));
      this.forward(ids, client);
      this.status = `Synced ${cameras.length} cameras. MQTT connected. Reload RTSPView to configure automation actions.`;
    } catch (e) { client.end(true); throw e; }
  }
  private async restore() {
    const active = this.storage.getItem('active');
    if (!active || !this.storage.getItem('token')) return;
    const {ids, broker} = JSON.parse(active) as {ids: string[]; broker: Broker};
    this.forward(ids, await this.connect(broker));
    this.status = 'MQTT connected. Forwarding fresh detection events for the last synced cameras.';
  }
}
export default Connector;

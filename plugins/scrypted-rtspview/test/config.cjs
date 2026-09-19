const {test} = require('node:test');
const assert = require('node:assert/strict');
const {rebroadcasts, brokerFromSettings, parseBroker, controllerAddress, detectionTopic, freshDetection} = require('../test-dist/config');
test('rebroadcast discovery uses only unmuted stream settings and translates loopback', () => {
  const streams = rebroadcasts([
    {key:'prebuffer:rtspRebroadcastUrl',subgroup:'Main',value:'rtsp://localhost:34197/secret-stream'},
    {key:'prebuffer:rtspRebroadcastMutedUrl',value:'rtsp://localhost:34197/muted'},
    {key:'prebuffer:rtspRebroadcastUrl',subgroup:'Sub',value:'rtsp://remote-host:3000/sub'},
  ], 'scrypted-host');
  assert.equal(streams.length, 2);
  assert.equal(streams[0].url, 'rtsp://scrypted-host:34197/secret-stream');
  assert.equal(streams[1].url, 'rtsp://remote-host:3000/sub');
  assert.throws(() => rebroadcasts([], 'localhost'));
  assert.throws(() => rebroadcasts([{key:'rtspRebroadcastUrl',value:'credential-string'}], 'scrypted-host'), /invalid rebroadcast URL/);
});
test('built-in and external brokers preserve authentication and transport', () => {
  const settings = [{key:'enableBroker',value:'true'}, {key:'tcpPort',value:'1884'}, {key:'username',value:'viewer'}, {key:'password',value:'test-only'}];
  assert.deepEqual(brokerFromSettings(settings,'scrypted-host'),{host:'scrypted-host',port:1884,tls:false,username:'viewer',password:'test-only'});
  assert.deepEqual(parseBroker('mqtts://user:p%40ss@broker-host/events','','','scrypted-host'),{host:'broker-host',port:8883,tls:true,username:'user',password:'p@ss'});
  assert.throws(() => parseBroker('wss://broker-host','','','scrypted-host'));
});
test('controller URL does not allow embedded credentials or path injection', () => {
  assert.equal(controllerAddress('https://viewer-host:5443'), 'https://viewer-host:5443');
  for(const value of ['file:///tmp','https://user:password@host','https://host/api','https://host/?secret=x']) assert.throws(() => controllerAddress(value));
});
test('topics isolate instances and encode MQTT wildcard characters', () => {
  assert.equal(detectionTopic('instance','a/+#'),'rtspview/instance/a%2F%2B%23/ObjectDetector');
});
test('stale, malformed and future detection events are not forwarded', () => {
  const now = Date.now();
  assert.equal(freshDetection({timestamp:now,detections:[{className:'person'}]},now),true);
  for(const event of [null, {}, {timestamp:now-31000,detections:[]}, {timestamp:now+6000,detections:[]}, {timestamp:NaN,detections:[]}])
    assert.equal(freshDetection(event,now),false);
});

const {test} = require('node:test');
const assert = require('node:assert/strict');
const Module = require('node:module');
const {EventEmitter} = require('node:events');

test('paired sync, publishing, stale-event rejection, failed sync and restart', async () => {
  const saved = new Map(), requests = [], clients = [], callbacks = new Set();
  const storage = {getItem:k=>saved.get(k) ?? null,setItem:(k,v)=>saved.set(k,v),removeItem:k=>saved.delete(k)};
  class Base { constructor(){ this.storage=storage; } async onDeviceEvent(){} }
  const camera = {name:'Driveway', interfaces:['VideoCamera','ObjectDetector'],
    getSettings:async()=>[{key:'prebuffer:rtspRebroadcastUrl',value:'rtsp://localhost:34197/rebroadcast'}],
    listen:(_type,callback)=>{callbacks.add(callback);return {removeListener:()=>callbacks.delete(callback)}}};
  const provider = {getSettings:async()=>[{key:'enableBroker',value:true},{key:'tcpPort',value:1883}]};
  const sdk = {__esModule:true,default:{systemManager:{getDeviceById:id=>id==='@scrypted/mqtt'?provider:camera}},ScryptedDeviceBase:Base,
    ScryptedInterface:{VideoCamera:'VideoCamera',ObjectDetector:'ObjectDetector',Settings:'Settings'}};
  const fakeMqtt={connect:(_url,options)=>{
    const client=new EventEmitter();client.options=options;client.connected=false;client.published=[];
    client.end=()=>{client.connected=false;client.ended=true};
    client.publish=(topic,payload,options,callback)=>{client.published.push({topic,payload,options});callback()};
    clients.push(client);queueMicrotask(()=>{client.connected=true;client.emit('connect')});return client;
  }};
  const original=Module._load, originalFetch=global.fetch;
  let rejectSync=false;
  Module._load=function(name,...args){if(name==='@scrypted/sdk')return sdk;if(name==='mqtt')return fakeMqtt;return original.call(this,name,...args)};
  global.fetch=async(url,options)=>{
    requests.push({url,options,body:JSON.parse(options.body)});
    return {ok:!rejectSync,status:rejectSync?409:200,json:async()=>rejectSync?{error:'No free slots'}:{token:'test-token',imported:1}};
  };
  try {
    const Connector=require('../test-dist/main').default;
    const connector=new Connector();
    await connector.putSetting('address','http://viewer-host:5080');
    await connector.putSetting('scryptedHost','scrypted-host');
    await connector.putSetting('cameras',['camera-1']);
    await assert.rejects(connector.putSetting('sync',true),/Pair/);
    await connector.putSetting('pairCode','one-time-test-code');
    assert.equal(saved.has('pairCode'),false);
    await connector.putSetting('sync',true);
    const sync=requests.at(-1);
    assert.equal(sync.body.cameras[0].rtspUrl,'rtsp://scrypted-host:34197/rebroadcast');
    assert.equal(sync.body.broker.host,'scrypted-host');
    assert.equal(sync.options.headers.Authorization,'Bearer test-token');
    assert.equal(sync.options.redirect,'error');
    assert.equal(clients[0].options.rejectUnauthorized,true);
    assert.equal(clients[0].options.queueQoSZero,false);
    const emit=event=>[...callbacks].forEach(callback=>callback(camera,{eventInterface:'ObjectDetector'},event));
    const fresh={timestamp:Date.now(),detections:[{className:'person'}]};
    emit(fresh);assert.equal(clients[0].published.length,1);
    assert.deepEqual(clients[0].published[0].options,{qos:0,retain:false});
    emit({...fresh,timestamp:Date.now()-60000});assert.equal(clients[0].published.length,1);
    clients[0].connected=false;emit(fresh);assert.equal(clients[0].published.length,1);clients[0].connected=true;
    rejectSync=true;await assert.rejects(connector.putSetting('sync',true),/No free slots/);
    assert.equal(clients[1].ended,true);assert.equal(clients[0].ended,undefined);
    emit(fresh);assert.equal(clients[0].published.length,2);
    connector.stop();const restored=new Connector();await restored.queue;
    emit(fresh);assert.equal(clients[2].published.length,1);
    restored.stop();
  } finally { Module._load=original;global.fetch=originalFetch; }
});

const assert = require('node:assert/strict'), fs = require('node:fs'), vm = require('node:vm');
const context = vm.createContext({});
vm.runInContext(fs.readFileSync('src/RTSPView.Controller/wwwroot/viewer-controls.js', 'utf8') + '\nthis.controls = viewerControls;', context);
const state = context.controls.state;
for (const t of [null, undefined]) {
  const s = state(t); for (const key of ['start','restart','streams','fullscreen']) assert.equal(s[key], false);
}
const offline = state({viewerRunning:false, viewerStarting:false, viewerConnected:true, viewer:{isFullScreen:true}});
assert.equal(offline.start,true); assert.equal(offline.restart,false); assert.equal(offline.fullscreen,false,'Stale telemetry cannot enable offline commands');
const starting = state({viewerRunning:true,viewerStarting:true,viewerConnected:true,viewer:{isFullScreen:false}});
for (const key of ['start','restart','streams','fullscreen']) assert.equal(starting[key],false);
for (const full of [false,true]) {
  const s=state({viewerRunning:true,viewerConnected:true,viewer:{isFullScreen:full}});
  assert.equal(s.start,false); assert.equal(s.restart,true); assert.equal(s.streams,true); assert.equal(s.fullscreen,true);
  assert.equal(s.action,full?'exit-fullscreen':'enter-fullscreen'); assert.equal(s.label,full?'Exit full screen':'Enter full screen');
}
assert.equal(state({viewerRunning:true,viewerConnected:true,viewer:{}}).fullscreen,false,'Older viewer has unknown mode');
assert.equal(state({viewerRunning:true,viewerConnected:false,viewer:{isFullScreen:true}}).fullscreen,false);
console.log('PASS viewer controls: offline, starting, unknown, stale, windowed and fullscreen states.');

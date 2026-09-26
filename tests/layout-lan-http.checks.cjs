const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict');
const { webcrypto } = require('node:crypto');
const source = fs.readFileSync('src/RTSPView.Controller/wwwroot/wall-designer.js', 'utf8');
// Ordinary HTTP exposes getRandomValues, but not randomUUID. Match that API surface.
const context = vm.createContext({crypto: {getRandomValues: value => webcrypto.getRandomValues(value)}, window: {addEventListener() {}}});
vm.runInContext(source, context);
const ids = new Set(Array.from({length: 1000}, () => vm.runInContext('layoutItemId()', context)));
assert.equal(ids.size, 1000);
for (const id of ids) assert.match(id, /^[a-f0-9]{32}$/);
assert(!source.includes('crypto.randomUUID('), 'layout actions must work on LAN HTTP');


const widgets=fs.readFileSync('src/RTSPView.Controller/wwwroot/layout-widgets.js','utf8');
context.weatherUi={defaults:()=>({location:'Test'}),editor:(options,save,placement)=>save(options,placement)};
context.aircraftUi={...context.weatherUi,balancedTextSizes:()=>({owner:24})};
vm.runInContext(widgets,context);
for(const full of [true,false])for(const kind of ['weather','aircraft']){
 context.layout={tiles:full?Array.from({length:9},(_,i)=>({cameraSlot:i+1})):[]};context.changes=0;context.kind=kind;
 context.changed=()=>context.changes++;
 vm.runInContext('layoutWidgetsUi.add(kind,layout,[],changed)',context);
 assert.equal(context.layout.tiles.length,full?9:0);
 assert.equal(context.layout.widgets.length,1);
 assert.equal(context.layout.widgets[0].hostCameraSlot,0);
 assert.equal(context.changes,1);
}
console.log('PASS LAN HTTP: independent weather/aircraft creation on full and empty layouts; unique IDs.');

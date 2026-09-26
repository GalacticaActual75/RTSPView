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

context.widget={margin:0,contentScale:1};context.box={left:100,top:100,width:400,height:200};
const resized=vm.runInContext("layoutWidgetsUi.resized(widget,box,1920,1080,200,100,'se')",context);
assert.equal(resized.contentScale,1);assert.equal(resized.widthPercent,600/1920*100);assert.equal(resized.heightPercent,300/1080*100);
assert(Math.abs((1920-600)*resized.x/100-100)<.0001);
const bounded=vm.runInContext("layoutWidgetsUi.resized(widget,box,1920,1080,9999,9999,'nw')",context);
assert(bounded.x>=0&&bounded.y>=0&&bounded.widthPercent<=100&&bounded.heightPercent<=100);
console.log('PASS widget resizing: proportional content scaling, opposite anchor and layout bounds.');

const wide=vm.runInContext("layoutWidgetsUi.resized(widget,box,1920,1080,200,0,'se')",context);
assert.equal(wide.widthPercent,600/1920*100);assert.equal(wide.heightPercent,200/1080*100);
const tall=vm.runInContext("layoutWidgetsUi.resized(widget,box,1920,1080,200,100,'s')",context);
assert.equal(tall.widthPercent,400/1920*100);assert.equal(tall.heightPercent,300/1080*100);
console.log('PASS unlocked corner resize and independent edge resizing.');

context.weatherUi.overlayBounds=()=>({height:200});context.scaled={kind:'weather',heightPercent:50,contentScale:.05};
assert.equal(vm.runInContext('layoutWidgetsUi.contentScale(scaled,{width:640,height:400})',context),2);
context.scaled.contentScale=10;assert.equal(vm.runInContext('layoutWidgetsUi.contentScale(scaled,{width:640,height:400})',context),2);
console.log('PASS content fitting depends on current dimensions, never resize history.');

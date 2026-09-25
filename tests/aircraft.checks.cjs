const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const context=vm.createContext({Date,Math,Number,Map,structuredClone,setInterval(){},ResizeObserver:class{observe(){}},weatherUi:{defaults:()=>({fontSize:24})}});
vm.runInContext(fs.readFileSync('src/RTSPView.Controller/wwwroot/aircraft.js','utf8'),context);
const ui=vm.runInContext('aircraftUi',context),now=Date.now(),options={...ui.defaults(),latitude:0,longitude:0,radiusMiles:10,minimumAltitudeFeet:null,maximumAltitudeFeet:null,units:'imperial'};
const track={hex:'a12345',latitude:.01,longitude:.01,positionAt:new Date(now).toISOString(),altitudeFeet:12000,speedKnots:100,trackDegrees:0,verticalRate:null};
const snapshot={fetchedAt:new Date(now).toISOString(),aircraft:[track]};
assert.equal(ui.nearby(options,snapshot).length,1);
assert.equal(ui.nearby({...options,minimumAltitudeFeet:13000},snapshot).length,0);
assert.equal(ui.nearby({...options,minimumAltitudeFeet:1},{...snapshot,aircraft:[{...track,altitudeFeet:null}]}).length,0);
assert.equal(ui.nearby(options,{...snapshot,aircraft:[{...track,positionAt:new Date(now-61000).toISOString()}]}).length,0);
assert.equal(ui.nearby(options,{...snapshot,aircraft:[{...track,positionAt:new Date(now+60000).toISOString()}]}).length,0);
assert.equal(ui.nearby(options,{...snapshot,fetchedAt:new Date(now-91000).toISOString()}).length,0);
assert.equal(ui.metric('speed',track,{...options,units:'metric'}),'SPD 185 km/h');
assert.equal(ui.metric('verticalRate',track,options),'V/S — ft/min');
assert.equal(ui.metric('track',track,options),'TRK 0° N');
assert.equal(ui.metric('type',{...track,type:'',registration:''},options),'');
for(const width of [120,320,640,1920])for(const fontSize of [12,24,64]){
 const overlay={widthPercent:40,x:100,y:100,margin:12,aircraft:{...options,fontSize,padding:14,iconSize:36,preset:'featured'}};
 const bounds=ui.overlayBounds(overlay,width,width*9/16);
 assert(bounds.width>=0&&bounds.height>=0&&bounds.left+bounds.width<=width+.001&&bounds.top+bounds.height<=width*9/16+.001);
}
console.log('PASS aircraft browser selection: radius, altitude, missing values, old/future positions, outage expiry, units and bounded placement.');
for(const state of ['fresh','stale','unavailable'])for(const count of [0,1])assert.equal(ui.shouldHide(options,state,count,false,true),state!=='fresh'||count===0,'camera takeover visibility follows fresh matching traffic');
assert.equal(ui.shouldHide({...options,hideWhenEmpty:true},'fresh',0,true,false),true);
assert.equal(ui.shouldHide({...options,hideWhenEmpty:true},'unavailable',0,true,false),false);
console.log('PASS camera replacement restores camera on empty, stale and unavailable traffic; overlay outage remains visible.');

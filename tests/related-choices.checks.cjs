const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
class Option {constructor(text,value){this.textContent=text;this.value=String(value)}}
class Select {constructor(value=''){this.value=value;this.options=[]}replaceChildren(...items){this.options=items}add(option){this.options.push(option)}}
const focus=new Select('1'),second=new Select('0'),overlay=new Select('10');
const form={querySelector:()=>({children:[]}),querySelectorAll:selector=>({'.sensor-focus':[focus],'.sensor-second':[second],'.sensor-overlay':[overlay]})[selector]||[]};
const app=fs.readFileSync('src/RTSPView.Controller/wwwroot/app.js','utf8');
const inventory=app.slice(app.indexOf('function layoutStreamInventory('),app.indexOf('function overlaySourceCard('));
const source=fs.readFileSync('src/RTSPView.Controller/wwwroot/tapo.js','utf8').replace('return {init, load, refresh, updateTargets, updateCamera, updateOverlay}', 'return {updateTargets, updateCamera, updateOverlay, seed(f,c){form=f;config=c}}');
const ctx=vm.createContext({Option,console,pluginsUi:{enabled:()=>true}});vm.runInContext(inventory+'\n'+source+'\nthis.ui=tapoUi;',ctx);
const camera=(slot,name='Stream '+slot)=>({slot,name,enabled:true,rtspUrl:'rtsp://camera.example.invalid/live'});
ctx.ui.seed(form,{cameraCount:1,cameras:[camera(1),camera(2,'Unused placeholder')],doorbellOverlay:{camera:camera(10)},garageOverlay:{camera:{...camera(11),rtspUrl:''}},deletedCameraSlots:[26]});
ctx.ui.updateCamera(camera(26,'New stream'));assert(focus.options.some(o=>o.value==='26'),'new stream beyond old cameraCount appears');assert(!focus.options.some(o=>o.value==='2'),'inactive placeholders stay excluded');assert.equal(focus.value,'1');
ctx.ui.updateCamera(camera(1,'Renamed'));assert.equal(focus.options.find(o=>o.value==='1').textContent,'Renamed');assert.equal(focus.options.filter(o=>o.value==='1').length,1);
ctx.ui.updateCamera({...camera(1),enabled:false});assert.equal(focus.value,'1');assert.equal(focus.options.find(o=>o.value==='1').textContent,'Unavailable selection');
ctx.ui.updateOverlay(camera(12,'New overlay'));assert(overlay.options.some(o=>o.value==='12'));assert(focus.options.some(o=>o.value==='35'));assert.equal(overlay.value,'10');assert.equal(second.value,'0');
ctx.ui.updateTargets({cameras:[],cameraCount:1,deletedCameraSlots:[26]});assert(!focus.options.some(o=>o.value==='26'));assert.equal(focus.value,'1');
console.log('PASS related choices: new streams beyond cameraCount, placeholders, rename, disabled/deleted targets, overlay sources, preserved selections.');

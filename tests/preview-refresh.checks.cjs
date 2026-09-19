const assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
let now=100000, calls=[],timers=[],fail=false,release;
const document={hidden:false,head:{append(){}},addEventListener(){},createElement(){return {classList:{contains:()=>true,remove(){},toggle(){}},textContent:''}}};
const context={document,innerHeight:800,innerWidth:1200,Date:class extends Date {static now(){return now;}},WeakMap,WeakSet,Map,URL:{createObjectURL:()=> 'blob:preview',revokeObjectURL(){}},setInterval(fn,ms){timers.push([fn,ms]);},api:async path=>{calls.push(path);if(release)await new Promise(r=>release=r);if(fail)throw Error('offline');},fetch:async()=>({ok:true,blob:async()=>({}),headers:{get:()=>new Date(now).toUTCString()}})};
vm.createContext(context);vm.runInContext(fs.readFileSync('src/RTSPView.Controller/wwwroot/dashboard-ux.js','utf8')+';globalThis.ux=dashboardUX;',context);
function image(onScreen=true){const label={isConnected:true,classList:{contains:()=>true,remove(){},toggle(){}},getClientRects:()=>onScreen?[{}]:[],getBoundingClientRect:()=>({top:0,left:0,bottom:100,right:100})};return {dataset:{},isConnected:true,_ageLabel:label};}
(async()=>{
const a=image(),duplicate=image(),b=image(),off=image(false);for(const [img,slot] of [[a,1],[duplicate,1],[b,2],[off,3]])await context.ux.snapshot(img,slot);
const tick=timers.find(t=>t[1]===750)[0];
await tick();assert.equal(calls.length,1);assert.match(calls[0],/\/1\//);
await tick();assert.equal(calls.length,2);assert.match(calls[1],/\/2\//);
await tick();assert.equal(calls.length,2,'deduplicates and throttles');
document.hidden=true;now+=16000;await tick();assert.equal(calls.length,2,'hidden tab pauses');
document.hidden=false;fail=true;await tick();assert.match(a._ageLabel.textContent,/Preview unavailable · showing last image/);assert.equal(a.src,'blob:preview');
fail=false;now+=16000;await tick();await tick();assert.match(a._ageLabel.textContent,/Preview · updated/);assert.doesNotMatch(context.ux.age(a),/Stale/);
assert(!calls.some(p=>p.includes('/3/')),'offscreen previews do not capture');
now+=16000;release=true;const pending=tick();const count=calls.length;await tick();assert.equal(calls.length,count,'captures never overlap');release();release=null;await pending;
console.log('PASS preview refresh: visible-only, deduplicated, staggered, paused hidden tab, retained failed image, recovery, no overlap');
})().catch(e=>{console.error(e);process.exitCode=1;});

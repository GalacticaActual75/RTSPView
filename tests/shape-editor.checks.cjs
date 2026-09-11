const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const context=vm.createContext({});
vm.runInContext(fs.readFileSync(path.join(__dirname,'../src/RTSPView.Controller/wwwroot/shape-editor.js'),'utf8')+'\nthis.outline=viewportShapeEditor.outline;',context);
const stroke=Array.from({length:300},(_,i)=>{const angle=i/300*Math.PI*2,r=300+(i%2?12:-12);return{x:500+r*Math.cos(angle),y:500+r*Math.sin(angle)}});
for(const amount of [0,40,100]){
 const result=context.outline(stroke,amount);
 assert.ok(result.startsWith('M')&&result.endsWith(' Z'));
 assert.ok(result.length<65536);
 assert.ok(!/NaN|Infinity/.test(result));
 const numbers=result.match(/\d+(?:\.\d+)?/g).map(Number);
 assert.ok(numbers.every(n=>n>=0&&n<=1000),'Smoothing stays inside the drawing surface');
 assert.ok((result.match(/[MLQZ]/g)||[]).length<4096);
}
assert.notEqual(context.outline(stroke,0),context.outline(stroke,100));
const radiusDeviation=amount=>{
 const numbers=context.outline(stroke,amount).match(/\d+(?:\.\d+)?/g).map(Number);
 const radii=[];for(let i=0;i<numbers.length;i+=2)radii.push(Math.hypot(numbers[i]-500,numbers[i+1]-500));
 const mean=radii.reduce((a,b)=>a+b,0)/radii.length;return radii.reduce((a,r)=>a+(r-mean)**2,0)/radii.length;
};
assert.ok(radiusDeviation(100)<radiusDeviation(0),'Smoothing reduces mouse wobble');
assert.equal(context.outline([],40),'');
assert.equal(context.outline([{x:1,y:1},{x:1,y:1},{x:1,y:1}],100),'');
assert.ok(!/NaN/.test(context.outline([{x:0,y:0},{x:10,y:0},{x:0,y:10}],100)));
const extreme=Array.from({length:2000},(_,i)=>({x:i%2?1000:0,y:i%3?1000:0}));
assert.ok(context.outline(extreme,100).length<65536,'Long strokes remain within mask storage limits');
console.log('Shape editor checks passed: smoothing, closed paths, bounds, degenerate strokes, storage limits.');
vm.runInContext('this.placement=viewportShapeEditor.placement;',context);
for(const raw of [[{x:610,y:420},{x:860,y:730}],[{x:0,y:0},{x:30,y:30}],[{x:970,y:970},{x:1000,y:1000}]]){
 const p=context.placement(raw);assert.ok(p);
 for(const point of raw){
  const localX=(point.x-p.x)/(p.width*10),localY=(point.y-p.y)/(p.height*10);
  assert.ok(localX>=0&&localX<=1&&localY>=0&&localY<=1);
  const wallX=(1000-p.width*10)*p.horizontal/100+localX*p.width*10;
  const wallY=(1000-p.height*10)*p.vertical/100+localY*p.height*10;
  assert.ok(Math.abs(wallX-point.x)<.001&&Math.abs(wallY-point.y)<.001,'Saved mask stays at the drawn background location');
 }
}
assert.equal(context.placement([{x:0,y:0},{x:1000,y:1000}]),null);
console.log('Background drawing placement checks passed.');
vm.runInContext('this.pointOutline=viewportShapeEditor.pointOutline;',context);
const triangle=[{x:100,y:100},{x:500,y:100},{x:300,y:600}];
assert.equal(context.pointOutline(triangle,0),'M100.00 100.00 L500.00 100.00 L300.00 600.00 Z','Point mode retains exact corners without smoothing');
assert.equal(context.pointOutline(triangle.slice(0,2),0),'');
assert.ok(context.pointOutline(triangle,40).includes('Q'),'Point outlines support optional smoothing');
console.log('Point outline checks passed.');

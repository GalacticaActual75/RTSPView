const assert=require('node:assert/strict'),balance=require('../src/RTSPView.Controller/wwwroot/wall-proportions.js'),presets=require('../src/RTSPView.Controller/wwwroot/wall-layout-presets.js');
const standard=balance(presets.get('3'));
assert.deepEqual(standard.rows,[1/3,1/3,1/3]);assert.deepEqual(standard.columns,[1/3,1/3,1/3]);
const layout={rows:4,columns:3,tiles:[{row:0,column:0,rowSpan:3,columnSpan:2},...[0,1,2,3].map(row=>({row,column:2,rowSpan:1,columnSpan:1})),...[0,1].map(column=>({row:3,column,rowSpan:1,columnSpan:1}))]};
const p=balance(layout);assert.deepEqual(p.rows,[.25,.25,.25,.25]);assert.deepEqual(p.columns,[1/3,1/3,1/3]);
const mixed={rows:4,columns:4,tiles:[{row:0,column:0,rowSpan:3,columnSpan:2},{row:0,column:2,rowSpan:2,columnSpan:2},...[2,3].map(column=>({row:2,column,rowSpan:1,columnSpan:1})),...[0,1,2,3].map(column=>({row:3,column,rowSpan:1,columnSpan:1}))]};
const mixedBounds=balance(mixed);assert(Math.abs(mixedBounds.rows[3]-.27)<1e-10,'native parity');
for(const t of mixed.tiles.filter(t=>t.rowSpan===1&&t.columnSpan===1)){const r=mixedBounds.bounds(t);assert(Math.abs(r.width-.25)<1e-10);assert(Math.abs(r.height-.27)<1e-10);}
for(const preset of presets.catalog)for(const format of ['16:9','9:16']){
 const l=presets.get(preset.id,format);l.aspectRatio=format;const before=JSON.stringify(l),p=balance(l);
 for(const tracks of [p.rows,p.columns]){assert(Math.abs(tracks.reduce((a,b)=>a+b,0)-1)<1e-10);assert(tracks.every(v=>v>0&&Number.isFinite(v)));}
 for(const tile of l.tiles){const b=p.bounds(tile);assert(b.left>=-1e-10&&b.top>=-1e-10&&b.left+b.width<=1+1e-10&&b.top+b.height<=1+1e-10);assert.equal(p.cell(p.columns,b.left+b.width/100),tile.column);}
 const small=l.tiles.filter(t=>t.rowSpan===1&&t.columnSpan===1).map(p.bounds);for(const b of small){assert(Math.abs(b.width-small[0].width)<1e-10);assert(Math.abs(b.height-small[0].height)<1e-10);}
 assert.equal(JSON.stringify(l),before);
}
console.log('PASS preview proportions: unchanged 3x3, native parity, landscape/portrait bounds and pointer cells');

const eight=balance(presets.get('focus-eight'));assert.equal(presets.get('focus-eight').tiles.length,8);for(const t of presets.get('focus-eight').tiles){const b=eight.bounds(t);assert(Math.abs(b.width-b.height)<1e-10,'focus plus seven must have no 16:9 letterboxing');}

const custom={...mixed,rowWeights:[.2,.2,.3,.3],columnWeights:[.25,.25,.25,.25]};
assert.deepEqual(balance(custom).rows,custom.rowWeights,'saved proportions used verbatim');
const adjusted=balance.adjust(custom,'rows',2,.305);assert(adjusted);assert.equal(adjusted.rowWeights[2],adjusted.rowWeights[3]);assert(Math.abs(adjusted.rowWeights.reduce((a,b)=>a+b,0)-1)<1e-10);
assert.equal(balance.adjust(presets.get('3'),'rows',0,.4),null,'all linked rows cannot break equal tiles');
assert.equal(balance.adjust(custom,'rows',2,.6),null,'oversized group rejected');
assert.deepEqual(presets.transpose(presets.transpose(custom)),custom,'custom sizing survives orientation roundtrip');
const fitLayout={rows:2,columns:2,aspectRatio:'16:9',tiles:[{cameraSlot:1,row:0,column:0,rowSpan:2,columnSpan:1},{cameraSlot:2,row:0,column:1,rowSpan:2,columnSpan:1}]};
const ratios={1:4/3,2:16/9},fit=balance.fit(fitLayout,ratios),initial=balance(fitLayout);
const waste=p=>fitLayout.tiles.reduce((n,t)=>{const r=p.bounds(t),a=16/9*r.width/r.height;return n+r.width*r.height*(1-Math.min(a/ratios[t.cameraSlot],ratios[t.cameraSlot]/a));},0);
assert(waste(fit)<=waste(initial)+1e-8,'fit must not increase total letterboxing');
console.log('PASS optional sizing, linked fine adjustment, fit safety, and transpose');

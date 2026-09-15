const assert=require('node:assert/strict'),balance=require('../src/RTSPView.Controller/wwwroot/wall-proportions.js'),presets=require('../src/RTSPView.Controller/wwwroot/wall-layout-presets.js');
const standard=balance(presets.get('3'));
assert.deepEqual(standard.rows,[1/3,1/3,1/3]);assert.deepEqual(standard.columns,[1/3,1/3,1/3]);
const layout={rows:4,columns:3,tiles:[{row:0,column:0,rowSpan:3,columnSpan:2},...[0,1,2,3].map(row=>({row,column:2,rowSpan:1,columnSpan:1})),...[0,1].map(column=>({row:3,column,rowSpan:1,columnSpan:1}))]};
const p=balance(layout);assert(Math.abs(p.rows[3]-.3)<1e-10);assert(Math.abs(p.columns[2]-.2653333333333333)<1e-10);
for(const preset of presets.catalog)for(const format of ['16:9','9:16']){
 const l=presets.get(preset.id,format);l.aspectRatio=format;const before=JSON.stringify(l),p=balance(l);
 for(const tracks of [p.rows,p.columns]){assert(Math.abs(tracks.reduce((a,b)=>a+b,0)-1)<1e-10);assert(tracks.every(v=>v>0&&Number.isFinite(v)));}
 for(const tile of l.tiles){const b=p.bounds(tile);assert(b.left>=-1e-10&&b.top>=-1e-10&&b.left+b.width<=1+1e-10&&b.top+b.height<=1+1e-10);assert.equal(p.cell(p.columns,b.left+b.width/100),tile.column);}
 assert.equal(JSON.stringify(l),before);
}
console.log('PASS preview proportions: unchanged 3x3, native parity, landscape/portrait bounds and pointer cells');

const eight=balance(presets.get('focus-eight'));assert.equal(presets.get('focus-eight').tiles.length,8);for(const t of presets.get('focus-eight').tiles){const b=eight.bounds(t);assert(Math.abs(b.width-b.height)<1e-10,'focus plus seven must have no 16:9 letterboxing');}

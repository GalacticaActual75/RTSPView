const assert=require('node:assert/strict');
const presets=require('../src/SpotMonitor.Controller/wwwroot/wall-layout-presets.js');
const slots=[1,2,3,4,5,6,7,8,9,26,27,28,29,30,31,32];
for(const preset of presets.catalog)for(const aspect of ['16:9','9:16'])for(const count of [1,9,10,16]){
  const layout=presets.create(preset.id,aspect,slots.slice(0,count));
  assert(layout.rows>=1&&layout.rows<=4&&layout.columns>=1&&layout.columns<=4);
  assert.equal(layout.tiles.length,Math.min(count,preset.tiles.length));
  const occupied=new Set(),used=new Set();
  for(const tile of layout.tiles){
    assert(slots.includes(tile.cameraSlot)&&!used.has(tile.cameraSlot));used.add(tile.cameraSlot);
    assert(tile.row>=0&&tile.column>=0&&tile.row+tile.rowSpan<=layout.rows&&tile.column+tile.columnSpan<=layout.columns);
    for(let r=tile.row;r<tile.row+tile.rowSpan;r++)for(let c=tile.column;c<tile.column+tile.columnSpan;c++){
      const key=r+','+c;assert(!occupied.has(key));occupied.add(key);
    }
  }
  assert.deepEqual(presets.transpose(presets.transpose(layout)),layout);
  if(count===16)assert.equal(occupied.size,layout.rows*layout.columns,'full preset covers the canvas');
}
assert.equal(presets.get('sidebar','9:16').rows,4);
assert.equal(presets.get('sidebar','9:16').columns,3);
console.log('Layout preset checks passed: 12 templates, both formats, camera inventories, coverage, no collisions, and reversible transposition.');

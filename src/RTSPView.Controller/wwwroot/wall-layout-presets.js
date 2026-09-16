const wallLayoutPresets = (() => {
  const tile = (row,column,rowSpan=1,columnSpan=1) => ({row,column,rowSpan,columnSpan});
  const grid = (rows,columns) => Array.from({length:rows*columns},(_,i)=>tile(Math.floor(i/columns),i%columns));
  const catalog = [
    {id:'1',name:'Single',rows:1,columns:1,tiles:grid(1,1)},
    {id:'split',name:'Split',rows:1,columns:2,tiles:grid(1,2)},
    {id:'2',name:'Quad',rows:2,columns:2,tiles:grid(2,2)},
    {id:'six',name:'Six',rows:2,columns:3,tiles:grid(2,3)},
    {id:'3',name:'Nine',rows:3,columns:3,tiles:grid(3,3)},
    {id:'4',name:'Sixteen',rows:4,columns:4,tiles:grid(4,4)},
    {id:'featured',name:'Featured',rows:3,columns:3,tiles:[tile(0,0,2,2),tile(0,2),tile(1,2),tile(2,0),tile(2,1),tile(2,2)]},
    {id:'focus-eight',name:'Focus + seven',rows:4,columns:4,tiles:[tile(0,0,3,3),tile(0,3),tile(1,3),tile(2,3),...grid(1,4).map(t=>({...t,row:3}))]},
    {id:'sidebar',name:'Sidebar',rows:3,columns:4,tiles:[tile(0,0,3,3),tile(0,3),tile(1,3),tile(2,3)]},
    {id:'cinema',name:'Cinema',rows:3,columns:4,tiles:[tile(0,0,2,4),...grid(1,4).map(t=>({...t,row:2}))]},
    {id:'dual',name:'Dual focus',rows:3,columns:4,tiles:[tile(0,0,2,2),tile(0,2,2,2),...grid(1,4).map(t=>({...t,row:2}))]},
    {id:'center',name:'Center stage',rows:4,columns:4,tiles:[tile(1,1,2,2),...grid(4,4).filter(t=>t.row===0||t.row===3||t.column===0||t.column===3)]},
    {id:'strip',name:'Strip',rows:1,columns:4,tiles:grid(1,4)}
  ];
  function transpose(layout) {
    return {...layout,...(layout.rowWeights?{rowWeights:layout.columnWeights,columnWeights:layout.rowWeights}:{}),rows:layout.columns,columns:layout.rows,tiles:layout.tiles.map(t=>({...t,row:t.column,column:t.row,rowSpan:t.columnSpan,columnSpan:t.rowSpan}))};
  }
  function get(id,aspectRatio='16:9') {
    const item=catalog.find(p=>p.id===id);
    if(!item)throw new Error('Unknown layout preset.');
    const layout={...item,tiles:item.tiles.map(t=>({...t}))};
    return aspectRatio==='9:16'?transpose(layout):layout;
  }
  function create(id,aspectRatio,slots) {
    const layout=get(id,aspectRatio);
    return {rows:layout.rows,columns:layout.columns,tiles:layout.tiles.slice(0,slots.length).map((t,i)=>({...t,cameraSlot:slots[i]}))};
  }
  return {catalog,get,create,transpose};
})();
if(typeof module!=='undefined')module.exports=wallLayoutPresets;

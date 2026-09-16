const wallProportions = (layout, targets = null) => {
  const custom=layout.rowWeights?.length===layout.rows&&layout.columnWeights?.length===layout.columns;
  const rows=custom?[...layout.rowWeights]:Array(layout.rows).fill(1/layout.rows),columns=custom?[...layout.columnWeights]:Array(layout.columns).fill(1/layout.columns);
  const aspect=layout.outputWidth&&layout.outputHeight?layout.outputWidth/layout.outputHeight:layout.aspectRatio==='9:16'?9/16:16/9;
  const small=layout.tiles.filter(t=>t.rowSpan===1&&t.columnSpan===1);
  const groups=(count,linked)=>{const shared=[...new Set(linked)].sort((a,b)=>a-b);return [...(shared.length?[shared]:[]),...Array.from({length:count},(_,i)=>i).filter(i=>!shared.includes(i)).map(i=>[i])];};
  const rowGroups=groups(layout.rows,small.map(t=>t.row)),columnGroups=groups(layout.columns,small.map(t=>t.column));
  const sum=(tracks,start,count)=>tracks.slice(start,start+count).reduce((a,b)=>a+b,0);
  const score=()=>layout.tiles.reduce((total,t)=>{
    const error=Math.log(aspect*sum(columns,t.column,t.columnSpan)/sum(rows,t.row,t.rowSpan)/(targets?.[t.cameraSlot]||16/9));
    return total+t.rowSpan*t.columnSpan*error*error;
  },0);
  let best=score();
  if(!custom||targets)for(const step of [.08,.025,.008,.002])for(let pass=0;pass<12;pass++){
    let improved=false;
    for(const [tracks,sets] of [[rows,rowGroups],[columns,columnGroups]])for(let a=0;a<sets.length;a++)for(let b=0;b<sets.length;b++){
      const add=step/sets[a].length,subtract=step/sets[b].length;
      if(a===b||sets[b].some(i=>tracks[i]-subtract<.35/tracks.length)||sets[a].some(i=>tracks[i]+add>2/tracks.length))continue;
      for(const i of sets[a])tracks[i]+=add;for(const i of sets[b])tracks[i]-=subtract;const next=score();
      if(next<best-1e-10){best=next;improved=true;}else{for(const i of sets[a])tracks[i]-=add;for(const i of sets[b])tracks[i]+=subtract;}
    }
    if(!improved)break;
  }
  const bounds=t=>({left:sum(columns,0,t.column),top:sum(rows,0,t.row),width:sum(columns,t.column,t.columnSpan),height:sum(rows,t.row,t.rowSpan)});
  const cell=(tracks,value)=>{let end=0;for(let i=0;i<tracks.length;i++){end+=tracks[i];if(value<end)return i;}return tracks.length-1;};
  return {rows,columns,bounds,cell};
};
wallProportions.fit = (layout, targets) => {
  const baseline=wallProportions(layout);
  const fitted=wallProportions({...layout,rowWeights:baseline.rows,columnWeights:baseline.columns},targets);
  const aspect=layout.outputWidth&&layout.outputHeight?layout.outputWidth/layout.outputHeight:layout.aspectRatio==='9:16'?9/16:16/9;
  const waste=p=>layout.tiles.reduce((n,t)=>{const b=p.bounds(t),ratio=aspect*b.width/b.height,target=targets[t.cameraSlot];return n+b.width*b.height*(1-Math.min(ratio/target,target/ratio));},0);
  return waste(fitted)<waste(baseline)-1e-8?fitted:baseline;
};
// Change a shared track group in half-percent steps without overlaps or unequal small tiles.
wallProportions.adjust = (layout, axis, index, value) => {
  const base=wallProportions(layout),tracks=[...base[axis]],small=layout.tiles.filter(t=>t.rowSpan===1&&t.columnSpan===1);
  const linked=[...new Set(small.map(t=>axis==='rows'?t.row:t.column))];
  const group=linked.includes(index)?linked:[index],others=tracks.map((_,i)=>i).filter(i=>!group.includes(i));
  if(!others.length||!Number.isFinite(value)||value<.02||value*group.length>=1)return null;
  const remainder=1-value*group.length,previous=others.reduce((n,i)=>n+tracks[i],0);
  for(const i of group)tracks[i]=value;
  for(const i of others)tracks[i]=tracks[i]*remainder/previous;
  if(tracks.some(w=>w<.02))return null;
  return {rowWeights:axis==='rows'?tracks:base.rows,columnWeights:axis==='columns'?tracks:base.columns};
};
if(typeof module!=='undefined')module.exports=wallProportions;

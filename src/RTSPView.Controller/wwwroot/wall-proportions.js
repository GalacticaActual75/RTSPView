const wallProportions = layout => {
  const rows=Array(layout.rows).fill(1/layout.rows),columns=Array(layout.columns).fill(1/layout.columns);
  const aspect=layout.aspectRatio==='9:16'?9/16:16/9;
  const small=layout.tiles.filter(t=>t.rowSpan===1&&t.columnSpan===1);
  const groups=(count,linked)=>{const shared=[...new Set(linked)].sort((a,b)=>a-b);return [...(shared.length?[shared]:[]),...Array.from({length:count},(_,i)=>i).filter(i=>!shared.includes(i)).map(i=>[i])];};
  const rowGroups=groups(layout.rows,small.map(t=>t.row)),columnGroups=groups(layout.columns,small.map(t=>t.column));
  const sum=(tracks,start,count)=>tracks.slice(start,start+count).reduce((a,b)=>a+b,0);
  const score=()=>layout.tiles.reduce((total,t)=>{
    const error=Math.log(aspect*sum(columns,t.column,t.columnSpan)/sum(rows,t.row,t.rowSpan)/(16/9));
    return total+t.rowSpan*t.columnSpan*error*error;
  },0);
  let best=score();
  for(const step of [.08,.025,.008,.002])for(let pass=0;pass<12;pass++){
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
if(typeof module!=='undefined')module.exports=wallProportions;

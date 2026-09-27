// Shared sizing and priority contract; parity checked against WidgetViewport in Core.
const widgetViewport = (() => {
  const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
  function measure(width,height,density='auto') {
    width=Math.max(1,width);height=Math.max(1,height);
    const padding=clamp(Math.min(width,height)*.045,4,24),font=clamp(Math.min(width/22,height/13),12,32);
    let detailLevel=width<160||height<150?0:width<420||height<260?1:2;
    if(density==='minimal')detailLevel=0;if(density==='standard')detailLevel=Math.min(1,detailLevel);
    if(density==='detailed'&&width>=160&&height>=150)detailLevel=2;
    return {width,height,padding,font,heading:font*1.35,reading:clamp(Math.min(width*.22,height*.28),24,160),detailLevel,wide:width>height*2.2};
  }
  function weatherMeasure(width,height,density='auto') {
    const v=measure(width,height,density);v.wide=width>=600&&height>=260&&width>=height*1.3;
    if(v.wide){v.font=clamp(Math.min(width/24,height/12),12,72);v.heading=v.font*1.35;v.reading=clamp(Math.min(width*.15,height*.4),24,320);}
    return v;
  }
  function apply(node,options,weather=false) {
    const v=(weather?weatherMeasure:measure)(node.clientWidth,node.clientHeight,options.density);
    node.dataset.responsive='true';node.dataset.density=String(v.detailLevel);node.style.padding=v.padding+'px';
    node.style.setProperty('--weather-font',v.font+'px');node.style.setProperty('--weather-icon',v.heading+'px');
    return v;
  }
  function fitRows(parent,height,rank) {
    const nodes=[...parent.children].filter(n=>!n.hidden&&getComputedStyle(n).display!=='none');
    const gap=parseFloat(getComputedStyle(parent).rowGap)||3;
    const total=()=>nodes.reduce((sum,n)=>sum+n.getBoundingClientRect().height/(parent.getBoundingClientRect().width/Math.max(1,parent.offsetWidth)||1),0)+Math.max(0,nodes.length-1)*gap;
    while(nodes.length&&total()>height+.5){const n=nodes.reduce((a,b)=>rank(a)<=rank(b)?a:b);n.hidden=true;n.dataset.responsiveHidden='';nodes.splice(nodes.indexOf(n),1);}
  }
  function reset(node){for(const n of node.querySelectorAll('[data-responsive-hidden]')){n.hidden=false;delete n.dataset.responsiveHidden;}}
  function aircraftPhoto(width,height,emphasis=false){
    const columns=width>=280&&height>=90,visible=columns||width>=160&&height>=220,fraction=emphasis?.5:.44;
    const photoWidth=columns?(width-8)*fraction:width,photoHeight=columns?height:(height-8)*fraction;
    return {visible,columns,textWidth:columns?width-8-photoWidth:width,textHeight:columns?height:height-8-photoHeight,photoWidth,photoHeight};
  }
  return {measure,weatherMeasure,aircraftPhoto,apply,fitRows,reset};
})();

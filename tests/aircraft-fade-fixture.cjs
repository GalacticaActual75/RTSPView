// Local browser regression fixture using the production renderer and animation engine.
const http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../src/RTSPView.Controller/wwwroot');
const html=`<!doctype html><meta charset="utf-8"><link rel="stylesheet" href="/weather.css"><link rel="stylesheet" href="/aircraft.css">
<style>body{background:#263640;color:white;font-family:Segoe UI}#card{width:800px;height:450px}</style>
<h1>Aircraft fade checks</h1><button id="run">Run transitions</button><output id="result">Ready</output><div id="card"></div>
<script>let ids=['a'];async function api(){return [{key:'0.0000,0.0000,10.0',fetchedAt:new Date().toISOString(),aircraft:ids.map(hex=>({hex,callsign:hex.toUpperCase(),latitude:.01,longitude:.01,altitudeFeet:6000,positionAt:new Date().toISOString()}))}]}</script>
<script src="/widget-responsive.js"></script><script src="/weather.js"></script><script src="/aircraft.js"></script>
<script>
const card=aircraftUi.preview({...aircraftUi.defaults(),latitude:0,longitude:0,cardDesign:'board',showPhoto:false,fadeEnabled:true,fadeInMilliseconds:300,fadeOutMilliseconds:500},false,false,true);document.querySelector('#card').append(card);aircraftUi.refresh();
const wait=ms=>new Promise(r=>setTimeout(r,ms)),names=()=>[...card.querySelectorAll('.weather-current strong')].map(n=>n.textContent).join(',');
document.querySelector('#run').onclick=async()=>{const result=document.querySelector('#result');try{
 const check=(ok,message)=>{if(!ok)throw Error(message);};await wait(900);
 for(const next of [['a','b'],['c','d'],['c']]){
  const old=names();ids=next;await aircraftUi.refresh();check(names()===old,'Old content replaced before fade');
  const outgoing=card.getAnimations().at(-1);check(!!outgoing,'Missing outgoing animation');outgoing.pause();outgoing.currentTime=250;const opacity=Number(getComputedStyle(card).opacity);check(opacity>0&&opacity<1,'No outgoing fade: '+opacity);
  await aircraftUi.refresh();check(names()===old,'Refresh replaced outgoing card');const completed=new Promise(resolve=>outgoing.addEventListener('finish',()=>{const incoming=card.getAnimations().at(-1);if(incoming&&incoming!==outgoing){incoming.pause();incoming.currentTime=150;}resolve(incoming);},{once:true}));outgoing.finish();const incoming=await completed;check(incoming&&incoming!==outgoing,'Missing incoming animation');check(Number(getComputedStyle(card).opacity)>0&&Number(getComputedStyle(card).opacity)<1,'No incoming fade');incoming.finish();await wait(100);
  check(names()===next.join(',').toUpperCase(),'Wrong final aircraft');check(Number(getComputedStyle(card).opacity)===1,'Fade did not settle');
 }
 ids=['e','f'];await aircraftUi.refresh();ids=[];await aircraftUi.refresh();const hiding=card.getAnimations().at(-1);if(hiding)await new Promise(resolve=>{hiding.addEventListener('finish',resolve,{once:true});hiding.finish();});await wait(100);
 check(getComputedStyle(card).visibility==='hidden','Interrupted swap resurfaced');
 result.textContent='PASS: one-to-two, pair replacement, two-to-one, real opacity animation, refresh stability and hide cancellation';
}catch(e){result.textContent='FAIL: '+e.message;}};
</script>`;
http.createServer((req,res)=>{res.setHeader('Cache-Control','no-store');if(req.url==='/'){res.setHeader('Content-Type','text/html');return res.end(html);}const name=req.url.slice(1);if(!['widget-responsive.js','weather.css','weather.js','aircraft.css','aircraft.js'].includes(name)){res.writeHead(404);return res.end();}res.setHeader('Content-Type',name.endsWith('.css')?'text/css':'text/javascript');res.end(fs.readFileSync(path.join(root,name)));}).listen(5216,'127.0.0.1');

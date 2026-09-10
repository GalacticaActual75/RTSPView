// Isolated browser fixture; never starts the Controller or touches installed settings.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../src/SpotMonitor.Controller/wwwroot');
const slots = [1,2,3,4,5,6,7,8,9,26,27,28,29,30,31,32];
const camera = (slot,name) => ({slot,name,enabled:true,rtspUrl:'',transport:1,networkCacheMilliseconds:1000,startupTimeoutSeconds:20,watchdogTimeoutSeconds:20,maximumReconnectBackoffSeconds:30,lowLatency:false,decodeAudio:false});
const overlay = (slot,name,hostCameraSlot) => ({hostCameraSlot,camera:camera(slot,name),viewportShape:0,viewportWidthPercent:50,viewportHeightPercent:50,viewportHorizontalPositionPercent:0,viewportVerticalPositionPercent:100,viewportOpacityPercent:100,zoomPercent:100,imageHorizontalPositionPercent:50,imageVerticalPositionPercent:50,customViewportPathData:'',customViewportSourceName:'',customViewportViewBoxX:0,customViewportViewBoxY:0,customViewportViewBoxWidth:1,customViewportViewBoxHeight:1,customViewportRotationDegrees:0});
let config = {schemaVersion:15,cameras:slots.map((slot,i)=>camera(slot,'Camera '+(i+1))),doorbellOverlay:overlay(10,'Doorbell',2),garageOverlay:overlay(11,'Garage',3),additionalOverlays:[overlay(12,'Patio',1)],layouts:[{id:'default',name:'Default',rows:3,columns:3,tiles:slots.slice(0,9).map((cameraSlot,i)=>({cameraSlot,row:Math.floor(i/3),column:i%3,rowSpan:1,columnSpan:1}))}],activeLayoutId:'default',startFullScreen:true,preferredMonitor:0,hideMouseCursor:true,mouseCursorHideSeconds:3,showCameraNames:true,showCameraStats:true,keepViewerAlwaysOnTop:true};
http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://localhost');res.setHeader('Cache-Control','no-store');
  const json=value=>{res.setHeader('Content-Type','application/json');res.end(JSON.stringify(value));};
  if(url.pathname==='/api/session')return json({authenticated:true,csrfToken:'fixture'});
  if(url.pathname==='/api/status')return json({hostname:'Isolated layout preview',lanAddresses:['localhost'],version:'1.0.30',currentTime:new Date().toISOString()});
  if(url.pathname==='/api/config')return json(config);
  if(url.pathname==='/api/layouts'&&req.method==='PUT'){
    let body='';for await(const chunk of req)body+=chunk;
    const request=JSON.parse(body);config={...config,...request};return json(request);
  }
  if(url.pathname==='/api/telemetry')return json({system:{},viewer:null,viewerConnected:false});
  if(url.pathname==='/api/logs')return json({lines:['Isolated fixture — no live camera connections.']});
  if(url.pathname==='/api/update')return json({installedVersion:'1.0.30',installedChannel:'stable',selectedChannel:'stable',updateAvailable:false});
  if(url.pathname.endsWith('/thumbnail')){res.setHeader('Content-Type','image/svg+xml');return res.end('<svg xmlns="http://www.w3.org/2000/svg" width="640" height="360"><rect width="640" height="360" fill="#162638"/><path d="M0 310L170 140L260 230L430 70L640 290V360H0Z" fill="#284c61"/><circle cx="520" cy="75" r="30" fill="#558da2"/></svg>');}
  const file=path.resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
  if(!file.startsWith(root+path.sep)||!fs.existsSync(file)){res.statusCode=404;return res.end();}
  res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.css':'text/css','.png':'image/png','.ico':'image/x-icon'})[path.extname(file)]||'application/octet-stream');fs.createReadStream(file).pipe(res);
}).listen(5097,'127.0.0.1',()=>console.log('Layout fixture: http://127.0.0.1:5097'));

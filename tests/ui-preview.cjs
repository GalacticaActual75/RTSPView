// Disposable UI fixture: serves shipped assets with in-memory API responses.
// Never starts the viewer, changes host settings, or contacts cameras.
const http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../src/RTSPView.Controller/wwwroot');
const names=['Front entrance','Driveway','Back garden','Side gate','Patio','Workshop','North fence','Parking','Office'];
const camera=(slot,name)=>({slot,name,enabled:true,rtspUrl:'rtsp://camera.example/stream',transport:1,networkCacheMilliseconds:1000,startupTimeoutSeconds:20,watchdogTimeoutSeconds:15,maximumReconnectBackoffSeconds:60,compositeStream:false,lowLatency:false,decodeAudio:false});
const overlay=(slot,name,host)=>({camera:camera(slot,name),hostCameraSlot:host,viewportWidthPercent:40,viewportHeightPercent:40,viewportHorizontalPositionPercent:0,viewportVerticalPositionPercent:100,viewportOpacityPercent:100,viewportShape:0,customViewportSourceName:'',customViewportPathData:'',customViewportViewBoxX:0,customViewportViewBoxY:0,customViewportViewBoxWidth:1,customViewportViewBoxHeight:1,customViewportRotationDegrees:0,zoomPercent:100,imageHorizontalPositionPercent:50,imageVerticalPositionPercent:50});
const config={cameras:names.map((name,i)=>camera(i+1,name)),cameraCount:9,doorbellOverlay:overlay(10,'Doorbell',1),garageOverlay:overlay(11,'Garage',3),additionalOverlays:[],startFullScreen:true,preferredMonitor:0,hideMouseCursor:true,mouseCursorHideSeconds:3,showCameraNames:false,showCameraStats:false,keepViewerAlwaysOnTop:true,activeLayoutId:'default',layouts:[{id:'default',name:'Main wall',rows:3,columns:3,aspectRatio:'16:9',tiles:names.map((_,i)=>({cameraSlot:i+1,row:Math.floor(i/3),column:i%3,rowSpan:1,columnSpan:1}))}]};
let channel='beta';
const update=()=>({installedVersion:'1.0.40-beta.1',installedChannel:'beta',selectedChannel:channel,latestVersion:channel==='beta'?'1.0.40-beta.1':'1.0.39',channelAvailable:true,updateAvailable:channel!=='beta',message:channel==='beta'?'RTSPView is up to date on this channel.':'RTSPView 1.0.39 is ready to install.'});
const network={enabled:true,managed:true,message:'LAN access enabled on private networks.',addresses:['http://192.0.2.10:5080','http://192.0.2.11:5080']};
const mutations=[];
http.createServer(async(req,res)=>{
 const url=new URL(req.url,'http://127.0.0.1'),route=url.pathname;
 const json=value=>{res.setHeader('Content-Type','application/json');res.end(JSON.stringify(value))};
 if(req.method!=='GET'){let body='';for await(const chunk of req)body+=chunk;mutations.push({route,method:req.method,body});console.log(req.method,route,body);if(route==='/api/update/channel'){channel=JSON.parse(body).channel;return json(update())}if(route==='/api/display'){Object.assign(config,JSON.parse(body));return json(config)}if(route==='/api/network')Object.assign(network,JSON.parse(body));if(route==='/api/layouts'){Object.assign(config,JSON.parse(body));return json(JSON.parse(body))}if(route==='/api/doorbell')return json(JSON.parse(body));if(route==='/api/garage')return json(JSON.parse(body));if(/^\/api\/cameras\/\d+$/.test(route))return json(JSON.parse(body));return json({message:'Fixture action recorded.'})}
 if(route==='/fixture/mutations')return json(mutations);
 if(route==='/api/session')return json({authenticated:true,passwordChangeRequired:false,csrfToken:'fixture-only'});
 if(route==='/api/status')return json({hostname:'RTSPVIEW-DEMO',lanAddresses:['192.0.2.10'],version:'1.0.40-beta.1',currentTime:'2026-09-12T22:15:00Z'});
 if(route==='/api/config')return json(config);
 if(route==='/api/update')return json(update());
 if(route==='/api/network')return json(network);
 if(route==='/api/logs')return json({lines:['[Info] Viewer connected.','[Info] Camera streams initialized.']});
 if(route==='/api/telemetry')return json({viewerConnected:true,system:{cpuPercent:12,ramUsedGb:5.4,ramTotalGb:16,gpuPercent:8,gpuVideoDecodePercent:18,networkReceiveMbps:24,networkSendMbps:1},viewer:{viewerMemoryMb:480,cameras:[...config.cameras,config.doorbellOverlay.camera,config.garageOverlay.camera].map((c,i)=>({...c,state:i===4?'Reconnecting':i===5?'StreamError':i===8?'Disabled':'Live',fps:15,width:1280,height:720,codec:'H264',bitrateKbps:246,reconnectCount:i===4?2:0,decoder:'Hardware active (D3D11VA)',frameWarning:i===4?'Last frame received 23 seconds ago':null,lastError:i===5?'Decoder unavailable. Retrying the stream connection.':null}))}});
 if(route.endsWith('/thumbnail')){res.setHeader('Content-Type','image/svg+xml');return res.end('<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720"><rect width="1280" height="720" fill="#243139"/><path d="M0 340L280 180L600 370L980 160L1280 330V720H0Z" fill="#364b43"/><path d="M200 720L640 390L920 720" fill="#7b8280"/><rect x="650" y="230" width="360" height="230" fill="#9b968a"/><path d="M600 240L830 100L1060 240" fill="#3d4751"/><rect x="720" y="280" width="80" height="180" fill="#3f5158"/><text x="35" y="680" fill="#e4e9e7" font-family="sans-serif" font-size="24">UI preview · synthetic camera image</text></svg>')}
 const file=path.resolve(root,'.'+(route==='/'?'/index.html':route));
 if(!file.startsWith(root+path.sep)||!fs.existsSync(file)){res.statusCode=404;return res.end('Not found')}
 res.setHeader('Cache-Control','no-store');res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.css':'text/css','.png':'image/png','.ico':'image/x-icon'})[path.extname(file)]||'application/octet-stream');fs.createReadStream(file).pipe(res);
}).listen(5099,'127.0.0.1',()=>console.log('UI fixture: http://127.0.0.1:5099'));

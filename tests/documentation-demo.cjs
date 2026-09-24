// Documentation-only data for the existing loopback UI fixture.
// No configuration files, cameras, credentials, OS details or external APIs are read.
module.exports = ({config,automation,tapo}) => {
  const now = () => new Date().toISOString();
  const names = ['Demo entrance','Demo driveway','Demo garden','Demo gate','Demo patio','Demo workshop','Demo courtyard','Demo parking','Demo office'];
  config.schemaVersion=16;
  config.showTileBorders=true;
  config.snapshots={enabled:false,intervalHours:1};
  config.cameras.forEach((c,i)=>Object.assign(c,{name:names[i],rtspUrl:`rtsp://camera-${i+1}.example/stream`,sourceMode:0,maximumHeight:720,watchdogTimeoutSeconds:12,maximumReconnectBackoffSeconds:30}));
  for(const layout of [...config.layouts,...config.automationViewLayouts]) {
    Object.assign(layout,{borderColor:'#24272B',backgroundColor:'#000000',rowWeights:[],columnWeights:[],outputWidth:1920,outputHeight:1080});
    layout.tiles.forEach(t=>Object.assign(t,{kind:'camera',sizing:'fit',zoomPercent:100,horizontalPositionPercent:50,verticalPositionPercent:50}));
  }
  const weather={location:'Demo location',latitude:0,longitude:0,timeZone:'UTC',preset:'compact',units:'imperial',theme:'dark',accent:'#F2C75C',backgroundOpacity:80,fontSize:24,iconSize:36,padding:14,cornerRadius:10,alignment:'left',fields:['location','temperature','condition','highLow']};
  config.weatherOverlays=[{hostCameraSlot:3,enabled:true,widthPercent:55,x:0,y:100,margin:12,weather}];
  Object.assign(config.doorbellOverlay.camera,{name:'Demo doorbell',enabled:false,sourceMode:0,maximumHeight:720});
  Object.assign(config.garageOverlay.camera,{name:'Demo garage',enabled:false,sourceMode:0,maximumHeight:720});
  config.doorbellOverlay.viewportShape=3;
  const rule=(id,name,action,cameraSlot,priority)=>({id,name,enabled:true,sources:[{cameraSlot,topic:`demo/camera-${cameraSlot}/ObjectDetector`,requiredZone:''}],overlaySlot:10,action,layoutId:action===2?'automation-default':'',cameraSlot,secondCameraSlot:0,allowNewerDetection:true,anyConfiguredSource:false,clearMinutes:2,priority});
  Object.assign(automation.settings,{enabled:true,host:'broker.example',clientId:'rtspview-demo',rules:[rule('10000000-0000-4000-8000-000000000001','Entrance person → doorbell',0,1,1),rule('10000000-0000-4000-8000-000000000002','Driveway person → focus layout',2,2,2)]});
  Object.assign(tapo.settings,{enabled:false,username:'',hubs:[{id:'20000000-0000-4000-8000-000000000001',name:'Demo hub',host:'hub.example'}],rules:[{id:'30000000-0000-4000-8000-000000000001',name:'Demo door opens → garage view',enabled:true,hubId:'20000000-0000-4000-8000-000000000001',deviceId:'fixture-door-0',match:2,action:1,clearAction:2,unavailableAction:0,overlaySlot:11,layoutId:'',clearLayoutId:'',priority:3,focusCameraSlot:0,secondFocusCameraSlot:0}]});
  const svg=slot=>{
    const palettes=[['#1d3544','#405b62','#7a9a97'],['#273447','#536780','#a3acb6'],['#293e39','#526f59','#a3b59a']];
    const [sky,trees,ground]=palettes[(slot-1)%palettes.length];
    return `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720"><rect width="1280" height="720" fill="${sky}"/><circle cx="1040" cy="145" r="54" fill="#eadab3"/><path d="M0 340L200 220L370 320L620 185L860 320L1060 200L1280 330V720H0Z" fill="${trees}"/><path d="M0 470L1280 380V720H0Z" fill="${ground}"/><path d="M420 720L680 400L820 400L960 720" fill="#d3d5ce"/><rect x="180" y="260" width="350" height="240" rx="4" fill="#c1c6bd"/><path d="M135 265L350 120L575 265" fill="#536171"/><rect x="245" y="315" width="80" height="90" fill="#435a67"/><rect x="370" y="325" width="95" height="175" fill="#435a67"/><rect x="30" y="625" width="555" height="62" rx="10" fill="#10202a" fill-opacity=".9"/><text x="52" y="665" fill="#f1f5f7" font-family="sans-serif" font-size="28">DEMO ${String(slot).padStart(2,'0')} · SYNTHETIC IMAGE</text></svg>`;
  };
  return {handle(req,res,route,json){
    // Keep all automatic page requests inside this disposable preview.
    res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; font-src 'self' data:");
    if(route==='/api/status'){json({hostname:'RTSPVIEW-DEMO',lanAddresses:['192.0.2.10'],version:'1.0.46',currentTime:now()});return true;}
    if(route==='/api/update'){json({showWallNotifications:true,lastChecked:now(),nextCheck:new Date(Date.now()+86400000).toISOString(),installedVersion:'1.0.46',installedChannel:'stable',selectedChannel:'stable',latestVersion:'1.0.46',channelAvailable:true,updateAvailable:false,message:'RTSPView is up to date on this channel.'});return true;}
    if(route==='/api/streaming-update'){json({state:{lastCheck:now(),message:'Demo component status · no downloads are performed.'},versions:{}});return true;}
    if(route==='/api/connector'){json({paired:false});return true;}
    if(route==='/api/dependencies/pawnio'){json({available:true,pawnInstalled:true,state:'ready',message:'Demo sensor service ready.'});return true;}
    if(route==='/api/weather'){json([{key:'0.0000,0.0000',timeZone:'UTC',fetchedAt:now(),validAt:now(),refreshFailed:false,temperature:22,feelsLike:23,humidity:48,wind:2.7,code:2,isDay:true,hourly:[],daily:[{date:now().slice(0,10),high:25,low:15,code:2}]}]);return true;}
    if(route==='/api/weather/search'){json({results:[{name:'Demo location',country:'Synthetic example',latitude:0,longitude:0,timezone:'UTC'}]});return true;}
    if(route==='/api/telemetry'){json({viewerConnected:true,viewerRunning:true,viewerStarting:false,viewerPaused:false,system:{cpuPercent:12,ramUsedGb:5.4,ramTotalGb:16,cpuTemperatureC:62.5,gpuTemperatureC:58.2,gpuPercent:8,gpuVideoDecodePercent:18,networkReceiveMbps:24,networkSendMbps:1},viewer:{displays:[{index:0,deviceName:'demo-display-1',label:'Display 1 — Demo wall — 1920 × 1080 — Primary',primary:true,width:1920,height:1080}],isFullScreen:true,viewerMemoryMb:480,cameras:[...config.cameras,config.doorbellOverlay.camera,config.garageOverlay.camera].map(c=>({...c,state:'Live',fps:15,width:1280,height:720,codec:'H264',bitrateKbps:246,reconnectCount:0,decoder:'Hardware active (D3D11VA)',lastError:null}))}});return true;}
    if(route==='/api/automation/status'){json({connection:'Connected',lastResult:'Demo broker connected · waiting for person events',lastMessage:null,lastPerson:null,rules:automation.settings.rules,activity:[],droppedEvents:0});return true;}
    if(route.endsWith('/thumbnail/refresh')){json({message:'Synthetic snapshot refreshed.'});return true;}
    if(route.endsWith('/thumbnail')){res.setHeader('Content-Type','image/svg+xml');res.setHeader('Last-Modified',new Date().toUTCString());res.end(svg(Number(route.split('/')[3])||1));return true;}
    return false;
  }};
};

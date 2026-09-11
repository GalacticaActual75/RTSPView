// Real HTTP integration checks against an isolated Controller and data directory.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),assert=require('node:assert/strict'),net=require('node:net');
(async()=>{
 const root=path.resolve(__dirname,'..'),data=path.join(root,'artifacts','security','http-'+crypto.randomUUID());fs.mkdirSync(data,{recursive:true});
 const port=await new Promise(resolve=>{const s=net.createServer();s.listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>resolve(p))})});
 const dotnet=process.env.DOTNET_HOST_PATH||path.join(root,'.toolchain','dotnet','dotnet.exe');
 const child=cp.spawn(dotnet,[path.join(root,'src/SpotMonitor.Controller/bin/Release/net8.0-windows/win-x64/SpotMonitor.Controller.dll')],{cwd:root,windowsHide:true,env:{...process.env,DOTNET_ROOT:path.dirname(dotnet),ASPNETCORE_ENVIRONMENT:'Testing',ASPNETCORE_URLS:'http://127.0.0.1:'+port,RTSPVIEW_DATA_DIR:data},stdio:'ignore'});
 const client=()=>({cookies:new Map(),csrf:''});const a=client(),b=client();
 async function request(c,url,method='GET',body){const r=await fetch('http://127.0.0.1:'+port+url,{method,headers:{Cookie:[...c.cookies].map(([k,v])=>k+'='+v).join('; '),'Content-Type':'application/json','X-CSRF-Token':c.csrf},body:body===undefined?undefined:JSON.stringify(body)});for(const cookie of r.headers.getSetCookie()){const pair=cookie.split(';')[0],i=pair.indexOf('=');c.cookies.set(pair.slice(0,i),pair.slice(i+1));}const text=await r.text();let json;try{json=JSON.parse(text)}catch{}return{status:r.status,json,text};}
 const session=async c=>{const r=await request(c,'/api/session');c.csrf=r.json.csrfToken;return r.json};
 try{
  let ready=false;for(let i=0;i<100;i++){try{await session(a);ready=true;break}catch{await new Promise(r=>setTimeout(r,100))}}assert(ready,'Controller starts');
  const hostileHostStatus=await new Promise((resolve,reject)=>{require('node:http').get({hostname:'127.0.0.1',port,path:'/api/session',headers:{Host:'untrusted.example'}},r=>{r.resume();resolve(r.statusCode)}).on('error',reject)});
  assert.equal(hostileHostStatus,400,'untrusted host rejected');
  const program=fs.readFileSync(path.join(root,'src/SpotMonitor.Controller/Program.cs'),'utf8');
  const routes=[...program.matchAll(/app\.Map(Get|Post|Put|Delete)\("([^"\n]+)"/g)].map(m=>[m[1].toUpperCase(),m[2].replace(/\{slot:int\}/g,'1')]).filter(([,p])=>!['/api/session','/api/auth/login'].includes(p));
  for(const[method,url]of routes)assert.equal((await request(a,url,method,method==='GET'?undefined:{})).status,401,'anonymous blocked: '+url);
  assert.equal((await request(a,'/api/auth/login','POST',{password:'admin'})).json.passwordChangeRequired,true);
  assert.equal((await session(a)).passwordChangeRequired,true);
  for(const[method,url]of routes.filter(([,p])=>!['/api/auth/password','/api/auth/logout'].includes(p)))assert.equal((await request(a,url,method,method==='GET'?undefined:{})).status,403,'setup gate: '+url);
  await session(b);assert.equal((await request(b,'/api/auth/login','POST',{password:'admin'})).status,200);
  assert.equal((await request(a,'/api/auth/password','POST',{currentPassword:'admin',newPassword:'short'})).status,400);
  const password='Test-only-'+crypto.randomUUID();assert.equal((await request(a,'/api/auth/password','POST',{currentPassword:'admin',newPassword:password})).status,200);
  assert.equal((await request(b,'/api/config')).status,401,'other session invalidated');
  assert.equal((await request(a,'/api/auth/login','POST',{password:'admin'})).status,401,'default revoked');
  assert.equal((await request(a,'/api/auth/login','POST',{password})).status,200);
  assert.equal((await session(a)).passwordChangeRequired,false);
  const config=(await request(a,'/api/config')).json;assert(config,'configuration loads');
  for(const camera of [config.camera,...config.cameras,config.doorbellOverlay.camera,config.garageOverlay.camera,...config.additionalOverlays.map(o=>o.camera)])assert.equal(camera.rtspUrl,'','fresh camera URL empty');
  const state=fs.readFileSync(path.join(data,'web-security.json'),'utf8');assert(!state.includes(password));assert(!state.includes('admin'));assert.equal(JSON.parse(state).PasswordChangeRequired,false);assert(!fs.existsSync(path.join(data,'initial-admin-password.txt')));
  const url='rtsp://test-user:test-pass@camera.example/live?token=test-query';const camera={...config.cameras[0],rtspUrl:url};assert.equal((await request(a,'/api/cameras/1','PUT',camera)).status,200);
  const exported=await request(a,'/api/config/export');assert(!exported.text.includes('test-pass'));assert(!exported.text.includes('test-query'));
  a.csrf='';assert.equal((await request(a,'/api/display','PUT',{})).status,400,'CSRF required');await session(a);
  const logs=fs.readdirSync(path.join(data,'logs')).map(p=>fs.readFileSync(path.join(data,'logs',p),'utf8')).join('');assert(!logs.includes(password));assert(!logs.includes('test-pass'));assert(!logs.includes('test-query'));
  for(let i=0;i<5;i++)await request(b,'/api/auth/login','POST',{password:'wrong'});assert.equal((await request(b,'/api/auth/login','POST',{password:'wrong'})).status,429);
  console.log('Admin security checks passed: all protected routes, setup gate, blank URLs, password rotation, session invalidation, CSRF, sanitized export/logs and throttling.');
 }finally{child.kill();await new Promise(resolve=>{if(child.exitCode!==null)resolve();else child.once('exit',resolve)});}
})().catch(error=>{console.error('Admin security checks FAILED: '+error.message);process.exitCode=1});

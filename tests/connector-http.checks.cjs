// Run after building Controller: node tests/connector-http.checks.cjs <dotnet path>
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const {spawn} = require('node:child_process');
const {randomUUID} = require('node:crypto');

(async()=>{
  const root=path.resolve(__dirname,'..');
  const directory=fs.mkdtempSync(path.join(os.tmpdir(),'rtspview-connector-http-'));
  const socket=net.createServer();await new Promise(resolve=>socket.listen(0,'127.0.0.1',resolve));
  const port=socket.address().port;await new Promise(resolve=>socket.close(resolve));
  const base=`http://127.0.0.1:${port}`;
  const controllerProject=fs.readFileSync(path.join(root,'src/RTSPView.Controller/RTSPView.Controller.csproj'),'utf8');
  const assembly=controllerProject.match(/<AssemblyName>([^<]+)<\/AssemblyName>/)[1];
  const child=spawn(process.argv[2]||'dotnet',[path.join(root,'src/RTSPView.Controller/bin/Release/net8.0-windows/win-x64',assembly+'.dll')],{
    cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,RTSPVIEW_DATA_DIR:directory,ASPNETCORE_ENVIRONMENT:'Testing',ASPNETCORE_URLS:base,AllowedHosts:'127.0.0.1'}
  });
  let csrf, cookies=new Map();
  async function request(route,body,{admin=false,token,origin,method=body===undefined?'GET':'POST',verify=true}={}){
    const headers={'Content-Type':'application/json'};
    if(admin){headers.Cookie=[...cookies].map(([k,v])=>`${k}=${v}`).join('; ');if(verify&&csrf)headers['X-CSRF-Token']=csrf;}
    if(token)headers.Authorization=`Bearer ${token}`;
    if(origin)headers.Origin=origin;
    const response=await fetch(base+route,{method,headers,body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(10000)});
    if(admin)for(const cookie of response.headers.getSetCookie()){const first=cookie.split(';')[0],i=first.indexOf('=');cookies.set(first.slice(0,i),first.slice(i+1));}
    const text=await response.text();return {status:response.status,data:text?JSON.parse(text):null};
  }
  try {
    let ready=false;
    for(let i=0;i<100;i++){try{const response=await request('/api/session',undefined,{admin:true});csrf=response.data.csrfToken;ready=true;break;}catch{if(child.exitCode!==null)throw new Error('Test Controller exited');await new Promise(r=>setTimeout(r,100));}}
    assert(ready,'Test Controller failed to start');
    assert.equal((await request('/api/control/viewer/start',{}, {admin:true})).status,401,'Unauthenticated viewer start');
    const instanceId=randomUUID();
    assert.equal((await request('/connector/v1/pair',{code:'invalid',instanceId})).status,403,'First-run setup bypass');
    assert.equal((await request('/api/connector/code',{}, {admin:true})).status,401,'Unauthenticated code creation');
    assert.equal((await request('/api/auth/login',{password:'admin'},{admin:true})).status,200);
    assert.equal((await request('/api/connector/code',{}, {admin:true})).status,403,'Password-change bypass');
    assert.equal((await request('/api/auth/password',{currentPassword:'admin',newPassword:'connector-http-test-only'},{admin:true})).status,200);
    assert.equal((await request('/api/auth/login',{password:'connector-http-test-only'},{admin:true})).status,200);
    assert.equal((await request('/api/connector/code',{}, {admin:true,verify:false})).status,400,'CSRF bypass');
    assert.equal((await request('/api/control/viewer/start',{}, {admin:true,verify:false})).status,400,'Viewer start CSRF bypass');
    fs.writeFileSync(path.join(directory,'viewer-paused'),'test intentional exit');
    assert.equal((await request('/api/telemetry',undefined,{admin:true})).data.viewerPaused,true,'Controller lost intentional-stop state');
    assert.equal((await request('/api/control/viewer/restart',{}, {admin:true})).status,409,'Restart bypassed intentional stop');
    const display=await request('/api/display',{showHoverExitButton:true},{admin:true,method:'PUT'});
    assert.equal(display.status,200);assert.equal(display.data.showHoverExitButton,true);
    assert.equal((await request('/api/config',undefined,{admin:true})).data.showHoverExitButton,true,'Hover exit preference was not saved');
    const code=(await request('/api/connector/code',{}, {admin:true})).data.code;
    assert.equal((await request('/connector/v1/pair',{code,instanceId},{origin:base})).status,403,'Browser-origin pairing');
    const pairing=await request('/connector/v1/pair',{code,instanceId});assert.equal(pairing.status,200);
    const token=pairing.data.token;
    assert.equal((await request('/connector/v1/pair',{code,instanceId})).status,401,'Replayed code');
    const camera={id:'driveway',name:'Driveway',rtspUrl:'rtsp://scrypted-host:34197/private-stream',topic:`rtspview/${instanceId}/driveway/ObjectDetector`};
    const sync={version:1,instanceId,cameras:[camera],broker:{host:'broker-host',port:1883,tls:false,username:'viewer',password:'test-mqtt-secret'}};
    assert.equal((await request('/connector/v1/sync',sync,{token:'invalid'})).status,401);
    assert.equal((await request('/connector/v1/sync',sync,{token,origin:'https://other-host'})).status,403);
    assert.equal((await request('/connector/v1/sync',sync,{token})).status,200);
    assert.equal((await request('/connector/v1/sync',sync,{token})).status,200);
    const config=JSON.parse(fs.readFileSync(path.join(directory,'settings.json'),'utf8'));
    assert.equal(config.Cameras.filter(c=>c.RtspUrl).length,1,'Duplicate sync');
    assert.equal(config.Cameras[0].ScryptedTopic,camera.topic);
    const automation=fs.readFileSync(path.join(directory,'automation.json'),'utf8');
    assert(!automation.includes('test-mqtt-secret'),'Plaintext MQTT password');
    assert.equal(JSON.parse(automation).Settings.Enabled,false,'Unexpected automation activation');
    const edit={slot:1,name:'Custom name',rtspUrl:camera.rtspUrl,enabled:true};
    assert.equal((await request('/api/cameras/1',edit,{admin:true,method:'PUT'})).status,200);
    assert.equal(JSON.parse(fs.readFileSync(path.join(directory,'settings.json'),'utf8')).Cameras[0].ScryptedId,`${instanceId}:driveway`,'Camera edit discarded identity');
    const before=fs.readFileSync(path.join(directory,'settings.json'),'utf8');
    assert.equal((await request('/connector/v1/sync',{...sync,cameras:[camera,{...camera,id:'bad',rtspUrl:'http://bad'}]},{token})).status,400);
    assert.equal(fs.readFileSync(path.join(directory,'settings.json'),'utf8'),before,'Invalid import partially saved');
    const failedWrite=path.join(directory,'automation.json.tmp');fs.mkdirSync(failedWrite);
    try {
      const failed=await request('/connector/v1/sync',{...sync,cameras:[{...camera,name:'Should roll back'}]},{token});assert.equal(failed.status,500);assert.match(failed.text||JSON.stringify(failed),/Reference [a-f0-9]{8}/);
      assert.equal(fs.readFileSync(path.join(directory,'settings.json'),'utf8'),before,'Broker save failure did not roll back cameras');
    } finally {fs.rmdirSync(failedWrite);}
    assert.equal((await request('/connector/v1/sync',{...sync,padding:'x'.repeat(66000)},{token})).status,400,'Unbounded request');
    assert.equal((await request('/api/connector',{}, {admin:true,method:'DELETE'})).status,200);
    assert.equal((await request('/connector/v1/sync',sync,{token})).status,401,'Revoked token');
    console.log('PASS: real Controller HTTP pairing, login/setup/CSRF/origin protections, token replay/revocation, idempotent import, protected MQTT password, camera edits, atomic validation and request bounds.');
  } finally { child.kill();await new Promise(resolve=>child.exitCode!==null?resolve():child.once('exit',resolve)); }
})().catch(error=>{console.error(error);process.exitCode=1;});

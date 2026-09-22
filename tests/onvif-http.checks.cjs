// Real authenticated Controller requests against a simulated ONVIF camera.
const assert=require('node:assert/strict'),http=require('node:http'),fs=require('node:fs'),path=require('node:path'),os=require('node:os');
const {spawn}=require('node:child_process'),{createHash}=require('node:crypto');
const root=path.resolve(__dirname,'..'),dotnet=process.argv[2]||'dotnet';
const S='http://www.onvif.org/ver10/schema',D='http://www.onvif.org/ver10/device/wsdl',M='http://www.onvif.org/ver10/media/wsdl',M2='http://www.onvif.org/ver20/media/wsdl';
const password='camera:@& test',username='viewer';
const unescape=s=>(s||'').replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"');
const value=(xml,name)=>unescape(xml.match(new RegExp('<(?:[\\w]+:)?'+name+'(?:\\s[^>]*)?>([^<]*)</'))?.[1]);
const md5=s=>createHash('md5').update(s).digest('hex');
let cameraPort,requests=0,digests=0;
const camera=http.createServer(async(req,res)=>{
  let body='';for await(const chunk of req)body+=chunk;
  requests++;
  const op=req.headers['content-type']?.match(/action="[^"]*\/([^/"]+)"/)?.[1];
  const family=req.url.split('/')[1],media2=family==='media2';
  const ns=op==='GetProfiles'||op==='GetStreamUri'?(media2?M2:M):D;
  const reply=(inner,status=200)=>{res.writeHead(status,{'Content-Type':'application/soap+xml'});res.end('<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:t="'+S+'" xmlns:d="'+D+'" xmlns:m="'+ns+'"><s:Body>'+inner+'</s:Body></s:Envelope>');};
  const fault=code=>reply('<s:Fault><s:Code><s:Value>'+code+'</s:Value></s:Code></s:Fault>',500);
  if(family==='invalid'){res.end('<html>camera login</html>');return;}
  if(family==='redirect'){res.writeHead(302,{Location:'http://example.invalid/credentials'});res.end();return;}
  if(op==='GetSystemDateAndTime'){
    const date=new Date(Date.now()-3600000);
    reply(`<d:GetSystemDateAndTimeResponse><d:SystemDateAndTime><t:UTCDateTime><t:Time><t:Hour>${date.getUTCHours()}</t:Hour><t:Minute>${date.getUTCMinutes()}</t:Minute><t:Second>${date.getUTCSeconds()}</t:Second></t:Time><t:Date><t:Year>${date.getUTCFullYear()}</t:Year><t:Month>${date.getUTCMonth()+1}</t:Month><t:Day>${date.getUTCDate()}</t:Day></t:Date></t:UTCDateTime></d:SystemDateAndTime></d:GetSystemDateAndTimeResponse>`);return;
  }
  if(family==='digest'){
    const fields=Object.fromEntries([...String(req.headers.authorization||'').matchAll(/(\w+)="([^"]*)"|(nc)=([\da-f]+)/g)].map(m=>[m[1]||m[3],m[2]||m[4]]));
    if(!fields.response){res.writeHead(401,{'WWW-Authenticate':'Digest realm="camera", nonce="fixture-nonce", qop="auth", algorithm=MD5'});res.end();return;}
    const expected=md5(`${md5(username+':camera:'+password)}:fixture-nonce:${fields.nc}:${fields.cnonce}:auth:${md5(req.method+':'+fields.uri)}`);
    if(fields.response!==expected){res.writeHead(403);res.end();return;}digests++;
  }
  const created=value(body,'Created'),nonce=Buffer.from(value(body,'Nonce'),'base64');
  const digest=createHash('sha1').update(Buffer.concat([nonce,Buffer.from(created+password)])).digest('base64');
  if(value(body,'Username')!==username||value(body,'Password')!==digest||Math.abs(Date.parse(created)-(Date.now()-3600000))>10000){fault('ter:NotAuthorized');return;}
  if(op==='GetServices'){
    if(family==='legacy'){fault('ter:ActionNotSupported');return;}
    const target=family==='foreign'?'http://example.invalid/media':`http://127.0.0.1:${cameraPort}/${family}/media`;
    reply(`<d:GetServicesResponse><d:Service><d:Namespace>${media2?M2:M}</d:Namespace><d:XAddr>${target}</d:XAddr></d:Service></d:GetServicesResponse>`);return;
  }
  if(op==='GetCapabilities'){
    reply(`<d:GetCapabilitiesResponse><d:Capabilities><t:Media><t:XAddr>${family==='foreign'?'http://example.invalid/media':`http://127.0.0.1:${cameraPort}/${family}/media`}</t:XAddr></t:Media></d:Capabilities></d:GetCapabilitiesResponse>`);return;
  }
  if(op==='GetProfiles'){
    const encoding='<t:Encoding>H264</t:Encoding><t:Resolution><t:Width>640</t:Width><t:Height>360</t:Height></t:Resolution>';
    const config=media2?`<m:Configurations><t:VideoEncoder>${encoding}</t:VideoEncoder></m:Configurations>`:`<t:VideoEncoderConfiguration>${encoding}</t:VideoEncoderConfiguration>`;
    reply(`<m:GetProfilesResponse><m:Profiles token="sub&amp;1"><t:Name>Substream &amp; test</t:Name>${config}</m:Profiles></m:GetProfilesResponse>`);return;
  }
  if(op==='GetStreamUri'){
    if(value(body,'ProfileToken')!=='sub&1'||value(body,'Protocol')!=='RTSP'||(!media2&&value(body,'Stream')!=='RTP-Unicast')){fault('ter:InvalidArgVal');return;}
    const uri='rtsp://0.0.0.0:8554/live?channel=1&amp;subtype=1';
    reply(`<m:GetStreamUriResponse>${media2?'<m:Uri>'+uri+'</m:Uri>':'<m:MediaUri><t:Uri>'+uri+'</t:Uri></m:MediaUri>'}</m:GetStreamUriResponse>`);return;
  }
  fault('ter:ActionNotSupported');
});
(async()=>{
  const directory=fs.mkdtempSync(path.join(os.tmpdir(),'RTSPView-onvif-'));
  await new Promise(r=>camera.listen(0,'127.0.0.1',r));cameraPort=camera.address().port;
  const reservation=http.createServer();await new Promise(r=>reservation.listen(0,'127.0.0.1',r));const port=reservation.address().port;await new Promise(r=>reservation.close(r));
  const base='http://127.0.0.1:'+port;
  const assembly=fs.readFileSync(path.join(root,'src/RTSPView.Controller/RTSPView.Controller.csproj'),'utf8').match(/<AssemblyName>([^<]+)<\/AssemblyName>/)[1];
  const child=spawn(dotnet,[path.join(root,'src/RTSPView.Controller/bin/Release/net8.0-windows/win-x64',assembly+'.dll')],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,RTSPVIEW_DATA_DIR:directory,ASPNETCORE_ENVIRONMENT:'Testing',ASPNETCORE_URLS:base,AllowedHosts:'127.0.0.1'}});
  let csrf='',cookies=new Map();
  async function request(route,body,csrfOverride){
    const response=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json','Cookie':[...cookies].map(([k,v])=>k+'='+v).join('; '),'X-CSRF-Token':csrfOverride??csrf},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(40000)});
    for(const cookie of response.headers.getSetCookie()){const first=cookie.split(';')[0],i=first.indexOf('=');cookies.set(first.slice(0,i),first.slice(i+1));}
    const text=await response.text();return{status:response.status,data:text?JSON.parse(text):null,headers:response.headers};
  }
  const connection=family=>({address:`http://127.0.0.1:${cameraPort}/${family}/device`,username,password});
  try{
    let ready=false;for(let i=0;i<100;i++){try{csrf=(await request('/api/session')).data.csrfToken;ready=true;break;}catch{await new Promise(r=>setTimeout(r,100));}}assert(ready);
    for(const route of ['profiles','stream','discover'])assert.equal((await request('/api/onvif/'+route,connection('media1'))).status,401);
    await request('/api/auth/login',{password:'admin'});
    assert.equal((await request('/api/onvif/profiles',connection('media1'))).status,403);
    await request('/api/auth/password',{currentPassword:'admin',newPassword:'onvif-test-admin'});await request('/api/auth/login',{password:'onvif-test-admin'});
    for(const route of ['profiles','stream','discover'])assert.equal((await request('/api/onvif/'+route,connection('media1'),'')).status,400);
    for(const family of ['media1','legacy','media2','digest']){
      const result=await request('/api/onvif/profiles',connection(family));assert.equal(result.status,200,JSON.stringify(result.data));
      const profile=result.data.profiles[0];assert.equal(profile.token,'sub&1');assert.equal(profile.width,640);assert.equal(profile.encoding,'H264');assert.equal(profile.mediaVersion,family==='media2'?2:1);
      const stream=await request('/api/onvif/stream',{...connection(family),profileToken:profile.token,mediaVersion:profile.mediaVersion});assert.equal(stream.status,200,JSON.stringify(stream.data));
      const uri=new URL(stream.data.rtspUrl);assert.equal(uri.hostname,'127.0.0.1');assert.equal(decodeURIComponent(uri.password),password);assert.equal(uri.search,'?channel=1&subtype=1');assert.match(stream.headers.get('cache-control'),/no-store/);
    }
    assert(digests>0,'HTTP Digest authentication not exercised');
    const wrong=await request('/api/onvif/profiles',{...connection('media1'),password:'incorrect-secret'});assert.equal(wrong.status,400);assert.match(wrong.data.error,/authentication/i);assert(!JSON.stringify(wrong).includes('incorrect-secret'));
    for(const family of ['foreign','invalid','redirect'])assert.equal((await request('/api/onvif/profiles',connection(family))).status,400,family);
    const before=requests;assert.equal((await request('/api/onvif/profiles',{...connection('media1'),address:'file:///private'})).status,400);assert.equal(requests,before);
    assert.equal((await request('/api/onvif/stream',{...connection('media1'),profileToken:'',mediaVersion:1})).status,400);
    console.log('PASS ONVIF HTTP: authentication/CSRF, Media/Media2, capabilities fallback, WS-Security digest and clock skew, HTTP Digest, profile selection, RTSP credential encoding, redirects and invalid/foreign endpoints.');
  }finally{child.kill();await new Promise(r=>child.exitCode!==null?r():child.once('exit',r));camera.closeAllConnections();await new Promise(r=>camera.close(r));fs.rmSync(directory,{recursive:true,force:true});}
})().catch(error=>{console.error(error);process.exitCode=1;camera.closeAllConnections();camera.close();});

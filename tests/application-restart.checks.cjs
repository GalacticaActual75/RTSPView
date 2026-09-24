const assert=require('node:assert/strict'), fs=require('node:fs'), vm=require('node:vm');
const source=fs.readFileSync('src/RTSPView.Controller/wwwroot/app.js','utf8');
const handler=source.slice(source.indexOf("for (const button of document.querySelectorAll('[data-application]'))"),source.indexOf("for(const button of document.querySelectorAll('[data-system]'))"));
async function scenario({confirm=true,fail=false}={}) {
 const button={disabled:false}, messages=[],busy=[],calls=[];let polls=0,telemetry=0;
 const context={document:{querySelectorAll:()=>[button]},uiDialogs:{ask:async()=>confirm},AbortSignal:{timeout:()=>null},setTimeout:fn=>fn(),viewerControls:{busy:v=>busy.push(v),message:v=>messages.push(v)},updateTelemetry:async()=>telemetry++,api:async(url,options)=>{calls.push(url);if(options.method==='POST'){if(fail)throw Error('Maintenance active');assert.equal(JSON.parse(options.body).confirmed,true);return {instance:'old',message:'Restarting'}}polls++;return {instance:polls===1?'old':'new'};}};
 vm.runInNewContext(handler,context);await button.onclick();return {messages,busy,calls,polls,telemetry};
}
(async()=>{
 const ok=await scenario();assert.equal(ok.polls,2,'old controller cannot be mistaken for successful restart');assert.deepEqual(ok.busy,[true,false]);assert.equal(ok.telemetry,1);assert.match(ok.messages.at(-1),/reconnected/);
 const cancelled=await scenario({confirm:false});assert.equal(cancelled.calls.length,0);
 const failed=await scenario({fail:true});assert.equal(failed.polls,0);assert.deepEqual(failed.busy,[true,false]);assert.equal(failed.messages.at(-1),'Maintenance active');
 console.log('PASS application restart UI: confirmation, distinct instance reconnection, failure and busy cleanup.');
})().catch(error=>{console.error(error);process.exitCode=1;});

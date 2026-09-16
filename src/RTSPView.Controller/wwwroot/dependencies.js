const dependencyUi = (()=>{
  let panel,busy=false,loading=false,revision=0;
  function init(){
    panel=document.createElement('div');panel.className='dependency-controls';
    panel.innerHTML='<div class="actions"><button type="button" class="secondary" data-helper disabled>Enable maintenance helper</button><button type="button" data-install disabled>Install PawnIO on host</button></div><p data-dependency-status role="status">Checking host dependencies…</p><p>Enabling the helper requires Windows administrator approval once on this host. After that, PawnIO and supported RTSPView updates can install with UAC enabled, without another prompt. The viewer stays open; only temperature readings pause during installation. Windows may require a restart.</p>';
    document.querySelector('#temperature-dependencies-title').after(panel);
    panel.querySelector('[data-helper]').onclick=()=>start('enable-helper','Enable the RTSPView maintenance service for this Windows user? Approve Windows setup on the host. The service installs verified PawnIO and RTSPView updates and reads temperature sensors.');
    panel.querySelector('[data-install]').onclick=()=>start('install','Install the official PawnIO driver on the RTSPView host? The live viewer stays open; temperature readings pause during installation. Windows may require a restart.');
  }
  async function start(action,prompt){
    if(busy||!confirm(prompt))return;busy=true;revision++;
    for(const button of panel.querySelectorAll('button'))button.disabled=true;
    try{const result=await api('/api/dependencies/pawnio/'+action,{method:'POST',body:JSON.stringify({confirmed:true})});panel.querySelector('[data-dependency-status]').textContent=result.message;}
    catch(error){panel.querySelector('[data-dependency-status]').textContent=error.message;}
    finally{busy=false;}
  }
  async function refresh(){
    if(busy||loading)return;loading=true;const current=revision;
    try{
      const state=await api('/api/dependencies/pawnio');if(current!==revision)return;
      const active=['starting','enabling','downloading','installing','update-installing'].includes(state.state);
      panel.querySelector('[data-helper]').hidden=!!state.available;
      panel.querySelector('[data-install]').hidden=!!state.pawnInstalled;
      panel.querySelector('.actions').hidden=!!state.available&&!!state.pawnInstalled;
      panel.querySelector('[data-helper]').disabled=active||state.available;
      panel.querySelector('[data-install]').disabled=active||!state.available||state.pawnInstalled;
      panel.querySelector('[data-dependency-status]').textContent=(state.available?'Maintenance helper enabled. ':'')+(state.pawnInstalled?'PawnIO installed. ':'')+state.message;
    }catch{if(current===revision){for(const button of panel.querySelectorAll('button'))button.disabled=true;panel.querySelector('[data-dependency-status]').textContent='Host dependency status unavailable. Reconnect to the Controller.';}}
    finally{loading=false;}
  }
  return{init,refresh};
})();


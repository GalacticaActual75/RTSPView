/* LAN access lives in System; all changes use the existing authenticated/CSRF API helper. */
function createNetworkPanel() {
 const panel=document.createElement('form');panel.id='networkForm';panel.className='panel control-panel';
 panel.innerHTML='<h2>LAN access</h2><p>Allow devices on your trusted private network to open this admin panel. HTTP is unencrypted; no certificate is needed.</p><div class="checks"><label><input name="enabled" type="checkbox"> Enable LAN access</label></div><p>Windows will ask for administrator approval on the RTSPView host when enabling access. Its network connection must be set to Private.</p><div class="actions"><button type="submit">Save LAN access</button></div><p id="networkState" role="status"></p><div id="networkAddresses"></div>';
 document.querySelector('#page-system').prepend(panel);
 function render(state) {
  panel.elements.enabled.checked=state.enabled;panel.elements.enabled.disabled=!state.managed;
  panel.querySelector('button').disabled=!state.managed;
  panel.querySelector('#networkState').textContent=state.message;
  const addresses=panel.querySelector('#networkAddresses');addresses.replaceChildren();
  if(state.managed && state.enabled)for(const url of state.addresses){const row=document.createElement('p'),link=document.createElement('a');link.href=url;link.textContent=url;link.target='_blank';link.rel='noopener';row.append(link);addresses.append(row)}
 }
 panel.onsubmit=async event=>{
  event.preventDefault();const enabled=panel.elements.enabled.checked,button=panel.querySelector('button');button.disabled=true;
  if(!enabled && !['localhost','127.0.0.1','[::1]'].includes(location.hostname) && !confirm('Disable LAN access? This device will lose access. You can re-enable it locally on the RTSPView host.')){panel.elements.enabled.checked=true;button.disabled=false;return}
  const status=panel.querySelector('#networkState');status.textContent=enabled?'Approve the Windows prompt on the RTSPView host. Configuring LAN access…':'Disabling LAN access…';
  try {
   const result=await api('/api/network',{method:'PUT',body:JSON.stringify({enabled})});render(result);
   status.textContent=enabled?'LAN access enabled. Allow a few seconds for the listener to update, then open an address below.':'LAN access disabled. Local administration remains available at http://127.0.0.1:5080 on the host.';
  }catch(error){try{render(await api('/api/network'))}catch{}status.textContent=error.message;button.disabled=false}
 };
 return {load:async()=>{try{render(await api('/api/network'))}catch(error){panel.querySelector('#networkState').textContent=error.message}}};
}


/* LAN access lives in System; all changes use the existing authenticated/CSRF API helper. */
function createNetworkPanel() {
 const panel=document.createElement('form');panel.id='networkForm';panel.className='panel control-panel';
 panel.innerHTML='<h2>LAN Access</h2><p>Allow devices on your trusted private network to open this admin panel.</p><div class="settings-action-row"><div><label class="toggle-control"><input name="enabled" type="checkbox" role="switch" aria-describedby="lanHelp"><span class="toggle-track" aria-hidden="true"></span>Enable LAN access</label><p id="lanHelp">Windows will ask for administrator approval on the RTSPView host when enabling access. Its network connection must be set to Private.</p></div><button type="submit">Apply changes</button></div><p id="networkState" role="status"></p><section class="inset-panel" aria-label="Admin URLs (LAN only)"><h3>Admin URLs (LAN only)</h3><div id="networkAddresses"></div><span id="copyAddressState" role="status"></span></section><p class="network-warning">HTTP is unencrypted; no certificate is needed. Use only on a trusted private network.</p>';
 document.querySelector('#page-system').prepend(panel);
 function render(state) {
  panel.elements.enabled.checked=state.enabled;panel.elements.enabled.disabled=!state.managed;
  panel.querySelector('button').disabled=!state.managed;panel.dataset.dirty='false';panel.dataset.savedEnabled=String(state.enabled);
  panel.querySelector('#networkState').textContent=state.message;
  const addresses=panel.querySelector('#networkAddresses');addresses.replaceChildren();
  if(state.managed && state.enabled)for(const url of state.addresses){
   const row=document.createElement('div'),link=document.createElement('a'),copy=document.createElement('button');row.className='address-row';
   link.href=url;link.textContent=url;link.target='_blank';link.rel='noopener';
   copy.type='button';copy.className='secondary copy-address';copy.textContent='Copy';copy.setAttribute('aria-label','Copy '+url);
   copy.onclick=async()=>{
    const status=panel.querySelector('#copyAddressState');
    try {
     if(navigator.clipboard && window.isSecureContext)await navigator.clipboard.writeText(url);
     else {
      // LAN HTTP does not expose Clipboard API. Keep a selection-based fallback.
      const field=document.createElement('textarea');field.value=url;field.className='clipboard-source';field.setAttribute('aria-label','LAN URL to copy');document.body.append(field);field.select();
      try{if(!document.execCommand('copy'))throw new Error('Copy unavailable');}finally{field.remove();copy.focus();}
     }
     status.textContent='Address copied.';
    }catch{status.textContent='Copy unavailable. Select the address and copy it manually.';}
   };
   row.append(link,copy);addresses.append(row);
  }
  if(!addresses.children.length)addresses.textContent=state.enabled?'No LAN addresses available.':'Enable LAN access to see available addresses.';
 }
 panel.oninput=()=>{panel.dataset.dirty=String(String(panel.elements.enabled.checked)!==panel.dataset.savedEnabled);panel.querySelector('#networkState').textContent=panel.dataset.dirty==='true'?'Unsaved changes — Apply changes updates LAN access':'Applied';};
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


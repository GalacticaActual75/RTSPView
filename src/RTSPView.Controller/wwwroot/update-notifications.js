const updateNotifications = (() => {
  let toggle, timing, loading=false;
  function init() {
    const panel=document.querySelector('#updatePanel'), row=document.createElement('div');row.className='update-notifications';
    row.innerHTML='<label class="toggle-control"><input type="checkbox" role="switch" checked><span class="toggle-track" aria-hidden="true"></span>Show update notifications on the stream wall</label><p>Checks once every 24 hours, even with the browser closed. Turning notifications off hides the wall badge; checks continue.</p><p class="update-check-times" role="status"></p>';
    panel.append(row);toggle=row.querySelector('input');timing=row.querySelector('.update-check-times');
    toggle.onchange=async()=>{toggle.disabled=true;const enabled=toggle.checked;try{renderUpdate(await api('/api/update/notifications',{method:'PUT',body:JSON.stringify({enabled})}));}catch(error){toggle.checked=!enabled;timing.textContent=error.message;}finally{toggle.disabled=false;}};
  }
  function render(status) {
    if(!toggle.disabled)toggle.checked=status.showWallNotifications??true;
    const date=value=>value?new Date(value).toLocaleString():'Pending';
    timing.textContent=`Last checked: ${date(status.lastChecked)} · Next check: ${date(status.nextCheck)}`;
  }
  async function refresh() {
    if(loading || document.querySelector('#updateChannel').disabled || document.querySelector('#installUpdate').disabled)return;
    loading=true;const request=updateRequest;
    try{const status=await api('/api/update');if(request===updateRequest)renderUpdate(status);}catch{}finally{loading=false;}
  }
  return {init,render,refresh};
})();

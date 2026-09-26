function createConnectorPanel() {
  const panel = document.createElement('section');
  panel.id = 'connectorPanel'; panel.className = 'panel control-panel';
  panel.innerHTML = `<h2>RTSPview connector</h2>
    <p>Import selected Scrypted camera streams and available integration settings. Enter this computer’s LAN address and a pairing code in the Scrypted plugin.</p>
    <div class="control-buttons"><button type="button" data-code>Create pairing code</button>
    <button type="button" class="secondary" data-status>Refresh status</button>
    <button type="button" class="danger" data-revoke>Disconnect connector</button></div>
    <label hidden data-code-label>One-time code (expires in five minutes)<input readonly autocomplete="off" aria-label="Connector pairing code"></label>
    <p role="status"></p><p>Pairing permits camera and integration configuration changes. Use HTTP only on a trusted private network, or HTTPS with a trusted certificate.</p>`;
  document.querySelector('#page-system').append(panel);
  const message = panel.querySelector('[role="status"]'), label = panel.querySelector('[data-code-label]');
  let expiry;
  function clearCode() { label.hidden = true; label.querySelector('input').value = ''; clearTimeout(expiry); }
  async function refresh() {
    try { const status = await api('/api/connector'); message.textContent = status.paired ? 'Scrypted is paired. Sync cameras from the plugin; reload RTSPView afterward to see the changes.' : 'No Scrypted connector is paired.'; }
    catch (e) { message.textContent = e.message; }
  }
  panel.querySelector('[data-code]').onclick = async () => {
    try { const result = await api('/api/connector/code', {method:'POST'}); clearCode(); label.hidden = false;
      label.querySelector('input').value = result.code; label.querySelector('input').select();
      message.textContent = 'Paste this code into RTSPview connector in Scrypted. Pairing a new connector replaces the previous connection.';
      expiry = setTimeout(clearCode, Math.max(0, new Date(result.expiresAt).getTime() - Date.now()));
    } catch (e) { message.textContent = e.message; }
  };
  panel.querySelector('[data-status]').onclick = refresh;
  panel.querySelector('[data-revoke]').onclick = async () => {
    if (!await uiDialogs.ask('Revoke connector access? Imported streams and saved settings will remain.')) return;
    try { await api('/api/connector', {method:'DELETE'}); clearCode(); await refresh(); }
    catch (e) { message.textContent = e.message; }
  };
  return { refresh, clearCode };
}

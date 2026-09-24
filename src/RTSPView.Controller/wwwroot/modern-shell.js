/* Management UI only. Move existing controls; keep their event handlers and save scopes. */
(() => {
  const $ = s => document.querySelector(s);
  const paths = {
    overview:'M3 4h18v13H3z M8 21h8 M12 17v4',
    cameras:'m12 3 10 5-10 5L2 8z M2 12l10 5 10-5 M2 16l10 5 10-5',
    layouts:'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
    overlays:'M3 3h18v18H3z M12 12h7v7h-7z',
    automation:'m13 2-9 12h7l-1 8 10-13h-7z',
    system:'M9 3h6l1 3 3 1 2 5-2 5-3 1-1 3H9l-1-3-3-1-2-5 2-5 3-1z M9 12a3 3 0 1 0 6 0 3 3 0 1 0-6 0',
    collapse:'M4 3h16v18H4z M9 3v18 m7-13-3 4 3 4',
    coffee:'M4 4h13v10a6 6 0 0 1-12 0V4 M17 6h2a3 3 0 0 1 0 6h-2 M3 21h17',
    feedback:'M3 3h18v14H9l-6 4z M7 8h10 M7 12h7',
    logout:'M9 3H3v18h6 M8 12h13 m-5-5 5 5-5 5'
  };
  function icon(name) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('fill', 'none'); svg.setAttribute('stroke', 'currentColor'); svg.setAttribute('stroke-width', '1.6'); svg.setAttribute('stroke-linecap','round'); svg.setAttribute('stroke-linejoin','round'); svg.setAttribute('aria-hidden', 'true');
    const path = document.createElementNS(svg.namespaceURI, 'path'); path.setAttribute('d', paths[name]); svg.append(path); return svg;
  }
  function label(control, text, name) {
    control.replaceChildren(icon(name)); const span = document.createElement('span'); span.className = 'sidebar-label'; span.textContent = text; control.append(span);
    control.title = text; control.setAttribute('aria-label', text);
  }
  const app = $('#app'), header = app.querySelector('header'), nav = $('.main-nav');
  const sidebar = document.createElement('aside'); sidebar.className = 'app-sidebar'; sidebar.id = 'appSidebar'; sidebar.setAttribute('aria-label','RTSPView sidebar');
  const brand = header.firstElementChild; brand.className = 'sidebar-brand';
  const logo = document.createElement('img'); logo.src = 'favicon-32.png'; logo.alt = ''; brand.querySelector('.brand').prepend(logo);
  for (const button of nav.children) label(button, button.textContent, button.dataset.page);
  const toggle = document.createElement('button'); toggle.type = 'button'; toggle.className = 'sidebar-toggle'; toggle.setAttribute('aria-controls', sidebar.id);
  let collapsed = false; try { collapsed = localStorage.getItem('rtspview.sidebar.collapsed') === 'true'; } catch { /* Private storage may be unavailable. */ }
  function collapse() { app.classList.toggle('sidebar-collapsed', collapsed); label(toggle, collapsed ? 'Expand sidebar' : 'Collapse sidebar', 'collapse'); toggle.setAttribute('aria-expanded', String(!collapsed)); window.dispatchEvent(new Event('resize')); }
  toggle.onclick = () => { collapsed = !collapsed; try { localStorage.setItem('rtspview.sidebar.collapsed', String(collapsed)); } catch {} collapse(); };
  const footer = document.createElement('div'); footer.className = 'sidebar-footer';
  const support = $('.floating-support'); support.className = 'sidebar-support';
  label(support.querySelector('a'), 'Buy Me a Coffee', 'coffee'); label($('#shareFeedback'), 'Send Feedback', 'feedback');
  const health = document.createElement('div'); health.className = 'sidebar-health'; health.innerHTML = '<span class="health-dot" aria-hidden="true"></span><span class="sidebar-label">Checking streams…</span>';
  health.setAttribute('role','status');
  const syncHealth = () => { const source = $('#monitorHealth'); const text = source?.textContent || 'Checking streams…'; health.querySelector('.sidebar-label').textContent = text; health.title = text; health.dataset.tone = source?.dataset.tone || 'neutral'; };
  new MutationObserver(syncHealth).observe($('#monitorHealth'), {childList:true,characterData:true,attributes:true,subtree:true}); syncHealth();
  const logout = $('#logout'); label(logout, 'Sign out', 'logout'); footer.append(health, support, logout, toggle);
  sidebar.append(brand, nav, footer); app.prepend(sidebar);
  header.prepend($('.hero')); $('.mobile-nav').hidden = true;
  const skip = document.createElement('a'); skip.className = 'skip-link'; skip.href = '#mainWorkspace'; skip.textContent = 'Skip to workspace'; app.prepend(skip); $('main').id = 'mainWorkspace'; $('main').tabIndex = -1;
  collapse();
  // A successful save is announced without interrupting the current editor.
  const saveLabels = {'/api/display':'Display settings applied','/api/layouts':'Layout saved','/api/automation':'MQTT automation saved','/api/tapo':'Tapo automation saved','/api/automation/priorities':'Priority order saved','/api/snapshots/settings':'Snapshot settings applied','/api/temperatures':'Temperature settings applied','/api/restart-schedule':'Restart schedule saved'};
  window.addEventListener('admin:saved', event => {
    const {url, method} = event.detail;
    const message = saveLabels[url] || (method === 'PUT' && /^\/api\/(cameras|overlays)\/\d+$/.test(url) ? 'Stream changes applied' : ['/api/doorbell','/api/garage'].includes(url) ? 'Picture in picture updated' : null);
    if (message) uiDialogs.toast(message);
  });
  // Existing info and icon controls receive native, keyboard-discoverable labels.
  const tooltips = root => { for (const button of root.querySelectorAll('button[aria-label]')) if (!button.title) button.title = button.getAttribute('aria-label'); };
  tooltips(document);
  new MutationObserver(records => { for (const record of records) for (const node of record.addedNodes) if (node.nodeType === 1) { if (node.matches('button[aria-label]') && !node.title) node.title = node.getAttribute('aria-label'); tooltips(node); } }).observe(document.body,{childList:true,subtree:true});
})();

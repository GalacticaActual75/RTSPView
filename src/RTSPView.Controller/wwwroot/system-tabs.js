/* Move existing controls intact: changing tabs never saves or resets a form. */
const systemTabs = (() => {
  function compactViewerOptions() {
    for (const label of document.querySelectorAll('#displayForm .display-toggle-grid > label')) {
      const hint = label.querySelector('.setting-copy small');
      if (!hint) continue;
      const row = document.createElement('div'); row.className = 'viewer-setting';
      label.before(row); row.append(label);
      hint.hidden = true; row.append(hint);
    }
  }
  function addHelp(card, selector, label) {
    if (!card) return;
    const notes = [...card.querySelectorAll(selector)].filter(node => !node.matches('[role="status"]'));
    if (!notes.length) return;
    const heading = card.querySelector('h2,h3');
    if (!heading) return;
    const button = document.createElement('button'); button.type = 'button'; button.className = 'settings-info';
    button.textContent = 'i'; button.setAttribute('aria-label', 'About ' + label); button.setAttribute('aria-expanded', 'false');
    const content = document.createElement('div'); content.className = 'settings-help'; content.hidden = true;
    content.id = 'settings-help-' + label.toLowerCase().replace(/[^a-z0-9]+/g, '-');
    button.setAttribute('aria-controls', content.id);
    content.append(...notes); heading.append(button); heading.after(content);
    const close = () => { content.hidden = true; button.setAttribute('aria-expanded', 'false'); };
    button.onclick = () => {
      const opening = content.hidden;
      adminUi.collapseSections(card.closest('.admin-page') || card);
      content.hidden = !opening; button.setAttribute('aria-expanded', String(opening));
    };
    content.onkeydown = event => { if (event.key === 'Escape') { close(); button.focus(); event.stopPropagation(); } };
    button.onkeydown = event => { if (event.key === 'Escape') { close(); event.stopPropagation(); } };
  }
  function init() {
    const page = document.querySelector('#page-system');
    const groups = [
      ['viewer', 'Display', [document.querySelector('#displayForm'), document.querySelector('#startupPanel'), document.querySelector('#snapshotForm')]],
      ['network', 'Network & security', [document.querySelector('#networkForm'), document.querySelector('#connectorPanel'), document.querySelector('#passwordForm')]],
      ['updates', 'Updates', [document.querySelector('#updatePanel')]],
      ['plugins', 'Plugins', [pluginsUi.panel()]],
      ['backups', 'Backups', [document.querySelector('#configPanel')]],
      ['maintenance', 'Maintenance', [document.querySelector('#restartScheduleForm'), document.querySelector('.viewer-display-panel')]],
      ['logs', 'Diagnostics', [document.querySelector('#stats'),document.querySelector('.performance'),document.querySelector('#temperatureForm'),document.querySelector('#logView').closest('section')]],
      ['about','About',[document.querySelector('.repository-link').closest('section'),document.querySelector('#host'),document.querySelector('#clock'),document.querySelector('#shareFeedback')]]
    ];
    const tablist = document.createElement('div');
    tablist.className = 'system-tabs settings-nav'; tablist.setAttribute('aria-orientation','vertical'); tablist.setAttribute('role', 'tablist'); tablist.setAttribute('aria-label', 'System settings');
    const tabs = [], panels = [];
    function select(index, focus = false) {
      adminUi.collapseSections(page);
      tabs.forEach((tab, i) => {
        tab.setAttribute('aria-selected', String(i === index)); tab.tabIndex = i === index ? 0 : -1;
        panels[i].hidden = i !== index;
      });
      if (focus) tabs[index].focus();
      if(groups[index][0]==='plugins')panels[index].append(pluginsUi.panel());
    }
    for (const [id, label, cards] of groups) {
      const index = tabs.length;
      const tab = document.createElement('button'); tab.type = 'button'; tab.id = 'system-tab-' + id;
      tab.setAttribute('role', 'tab'); tab.setAttribute('aria-controls', 'system-panel-' + id); tab.textContent = label;
      const panel = document.createElement('div'); panel.id = 'system-panel-' + id; panel.className = 'system-tab-panel';
      panel.setAttribute('role', 'tabpanel'); panel.setAttribute('aria-labelledby', tab.id); panel.tabIndex = 0;
      panel.append(...cards.filter(Boolean)); panels.push(panel); tabs.push(tab); tablist.append(tab);
      tab.onclick = () => select(index);
      tab.onkeydown = event => {
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        const next = {ArrowRight:(index + 1) % groups.length, ArrowDown:(index + 1) % groups.length, ArrowUp:(index + groups.length - 1) % groups.length, ArrowLeft:(index + groups.length - 1) % groups.length,
          Home:0, End:groups.length - 1}[event.key];
        if (next === undefined) return;
        event.preventDefault(); select(next, true);
      };
    }
    page.prepend(tablist); page.append(...panels); select(0);
    compactViewerOptions();
    for (const [selector, notes, label] of [
      ['#displayForm', ':scope > p, :scope > .information-note', 'viewer behavior'],
      ['#snapshotForm', ':scope > p:not([id])', 'browser snapshots'],
      ['#temperatureForm', ':scope > p, :scope > fieldset > p', 'temperature monitoring'],
      ['#temperatureForm section[aria-labelledby="temperature-dependencies-title"]', ':scope > p:not(:has(a)), .dependency-controls > p:not([role])', 'sensor requirements'],
      ['#passwordForm', ':scope > div:first-child > p', 'administrator password'],
      ['#restartScheduleForm', ':scope > p', 'scheduled restarts'],
      ['#updatePanel', '.update-notifications > p:not([role])', 'update notifications']
    ]) addHelp(page.querySelector(selector), notes, label);
  }
  return {init};
})();

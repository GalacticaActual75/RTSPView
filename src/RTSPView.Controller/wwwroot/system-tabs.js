/* Move existing controls intact: changing tabs never saves or resets a form. */
const systemTabs = (() => {
  function init() {
    const page = document.querySelector('#page-system');
    const groups = [
      ['viewer', 'Viewer', [document.querySelector('#displayForm'), document.querySelector('#snapshotForm'), document.querySelector('#temperatureForm')]],
      ['network', 'Network & security', [document.querySelector('#networkForm'), document.querySelector('#passwordForm')]],
      ['updates', 'Updates', [document.querySelector('#updatePanel'), document.querySelector('.repository-link').closest('section')]],
      ['backups', 'Backups', [document.querySelector('#configPanel')]],
      ['maintenance', 'Maintenance', [document.querySelector('#restartScheduleForm'), document.querySelector('.viewer-display-panel')]],
      ['logs', 'Logs', [document.querySelector('#logView').closest('section')]]
    ];
    const tablist = document.createElement('div');
    tablist.className = 'system-tabs'; tablist.setAttribute('role', 'tablist'); tablist.setAttribute('aria-label', 'System settings');
    const tabs = [], panels = [];
    function select(index, focus = false) {
      tabs.forEach((tab, i) => {
        tab.setAttribute('aria-selected', String(i === index)); tab.tabIndex = i === index ? 0 : -1;
        panels[i].hidden = i !== index;
      });
      if (focus) tabs[index].focus();
    }
    for (const [id, label, cards] of groups) {
      const index = tabs.length;
      const tab = document.createElement('button'); tab.type = 'button'; tab.id = 'system-tab-' + id;
      tab.setAttribute('role', 'tab'); tab.setAttribute('aria-controls', 'system-panel-' + id); tab.textContent = label;
      const panel = document.createElement('div'); panel.id = 'system-panel-' + id; panel.className = 'system-tab-panel';
      panel.setAttribute('role', 'tabpanel'); panel.setAttribute('aria-labelledby', tab.id); panel.tabIndex = 0;
      panel.append(...cards); panels.push(panel); tabs.push(tab); tablist.append(tab);
      tab.onclick = () => select(index);
      tab.onkeydown = event => {
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        const next = {ArrowRight:(index + 1) % groups.length, ArrowLeft:(index + groups.length - 1) % groups.length,
          Home:0, End:groups.length - 1}[event.key];
        if (next === undefined) return;
        event.preventDefault(); select(next, true);
      };
    }
    page.prepend(tablist); page.append(...panels); select(0);
  }
  return {init};
})();

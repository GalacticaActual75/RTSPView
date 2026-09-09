/* Presentation only: retain the existing forms, API handlers and preview math. */
const adminLayout = (() => {
  let current = 'overview';
  const pages = {};
  function init() {
    const css = document.createElement('link');
    css.rel = 'stylesheet'; css.href = 'layout.css?v=beta4'; document.head.append(css);
    const main = document.querySelector('main');
    const banner = main.previousElementSibling;
    banner.className = 'beta-notice';
    banner.removeAttribute('style');
    banner.textContent = 'Beta channel · Export your configuration before testing a new build.';
    document.querySelector('header .tag').textContent = 'BETA';
    const nav = document.createElement('nav'); nav.className = 'main-nav'; nav.setAttribute('aria-label', 'Administration');
    for (const [id, title] of Object.entries({overview:'Overview', cameras:'Cameras', overlays:'Overlays', system:'System'})) {
      const button = document.createElement('button'); button.type = 'button'; button.textContent = title;
      button.dataset.page = id; button.onclick = () => select(id); nav.append(button);
      const page = document.createElement('section'); page.id = 'page-' + id; page.className = 'admin-page'; page.setAttribute('aria-label', title);
      pages[id] = page;
    }
    document.querySelector('header').after(nav);
    const stats = document.querySelector('#stats');
    const performance = document.createElement('details'); performance.className = 'performance';
    performance.innerHTML = '<summary>Detailed performance</summary>';
    const extra = document.createElement('div'); extra.id = 'extraStats'; extra.className = 'stats'; performance.append(extra);
    const overviewHead = document.createElement('div'); overviewHead.className = 'section-title';
    overviewHead.innerHTML = '<h2>Camera wall</h2><p>Latest snapshots and stream health. Open Cameras to configure a slot.</p>';
    pages.overview.append(stats, performance, overviewHead);
    const cameraGrid = document.querySelector('#cameras');
    pages.cameras.append(cameraGrid.previousElementSibling);
    const overlayNav = document.createElement('nav'); overlayNav.className = 'overlay-nav'; overlayNav.setAttribute('aria-label','Select overlay');
    for (const [id, title] of [['doorbell','Doorbell'],['garage','Garage']]) {
      const button = document.createElement('button'); button.type = 'button'; button.textContent = title;
      button.onclick = () => {
        for (const name of ['doorbell','garage']) document.querySelector('#' + name).hidden = name !== id;
        for (const item of overlayNav.children) item.setAttribute('aria-pressed', String(item === button));
        window.dispatchEvent(new Event('resize'));
      };
      button.setAttribute('aria-pressed', String(id === 'doorbell')); overlayNav.append(button);
      const grid = document.querySelector('#' + id); grid.previousElementSibling.remove(); grid.hidden = id !== 'doorbell'; pages.overlays.append(grid);
    }
    const overlayHead = document.createElement('div'); overlayHead.className = 'section-title';
    overlayHead.innerHTML = '<h2>Overlay editor</h2><p>Adjust the preview, then save to apply changes to the camera wall.</p>';
    pages.overlays.prepend(overlayHead, overlayNav);
    const viewer = document.querySelector('.viewer-display-panel'), display = document.querySelector('#displayForm'), updates = document.querySelector('#updatePanel');
    display.className = 'panel control-panel'; updates.className = 'panel control-panel';
    viewer.querySelector('h2').textContent = 'Maintenance';
    viewer.querySelector('p').textContent = 'These commands take effect immediately on the Windows host.';
    pages.system.append(display, updates, document.querySelector('#configPanel'), viewer, document.querySelector('#passwordForm'), document.querySelector('#logView').closest('section'));
    for (const page of Object.values(pages)) main.append(page);
    document.querySelector('#controlState').setAttribute('role','status');
    // Commands from camera cards must remain visible outside the System page.
    document.body.append(document.querySelector('#controlState'));
    select('overview');
  }
  function select(id) {
    current = id;
    for (const [name, page] of Object.entries(pages)) page.hidden = name !== id;
    for (const button of document.querySelectorAll('[data-page]')) {
      button.setAttribute('aria-current', button.dataset.page === id ? 'page' : 'false');
    }
    if (id === 'overview' || id === 'cameras') pages[id].append(document.querySelector('#cameras'));
    document.querySelector('.hero h1').textContent = {overview:'Overview',cameras:'Cameras',overlays:'Overlays',system:'System'}[id];
    window.dispatchEvent(new Event('resize'));
    window.scrollTo({top:0, behavior:'instant'});
  }
  function metrics() {
    const stats = document.querySelector('#stats');
    document.querySelector('#extraStats').replaceChildren(...[...stats.children].slice(4));
  }
  function card(form, overlayMode) {
    form.querySelector('.switch input').setAttribute('aria-label', 'Enable ' + form.elements.name.value);
    const settings = form.querySelector('.camera-settings');
    if (overlayMode) {
      settings.querySelector('summary').textContent = 'Overlay controls';
      const placement = settings.querySelector('.overlay-placement');
      const connection = document.createElement('details'); connection.innerHTML = '<summary>Connection and recovery</summary>';
      for (const child of [...settings.children]) if (child !== placement && child.tagName !== 'SUMMARY') connection.append(child);
      settings.append(connection);
      connection.append(form.querySelector('.restart-camera'));
      const makeGroup = (title, names) => {
        const group = document.createElement('fieldset'); const legend = document.createElement('legend'); legend.textContent = title; group.append(legend);
        for (const name of names) group.append(form.elements[name].closest('label'));
        placement.append(group); return group;
      };
      for (const title of placement.querySelectorAll('.overlay-group-title')) title.remove();
      makeGroup('Placement', ['hostCameraSlot','viewportWidthPercent','viewportHeightPercent','viewportHorizontalPositionPercent','viewportVerticalPositionPercent']);
      const appearance = makeGroup('Appearance',['viewportShape','viewportOpacityPercent']);
      appearance.append(placement.querySelector('.custom-viewport-upload'));
      makeGroup('Framing',['zoomPercent','imageHorizontalPositionPercent','imageVerticalPositionPercent']);
      placement.append(placement.querySelector('.reset-doorbell-framing'));
      const help = placement.querySelector('.overlay-help'), details = document.createElement('details');
      details.innerHTML = '<summary>Framing help</summary>';
      help.textContent = 'Drag in Wall preview to position the overlay. Use Source framing to choose the visible part of the camera image. The image keeps its proportions. Position 0 is left/top, 50 is centered, and 100 is right/bottom. Overlay software decoding may increase CPU usage.';
      details.append(help); placement.append(details);
    }
    const controls = () => [...form.querySelectorAll('input,select')].filter(el => el.type !== 'file');
    const snapshot = () => controls().map(el => ({el, value:el.value, checked:el.checked}));
    let saved = snapshot();
    const actions = form.querySelector('.actions'), submit = actions.querySelector('[type=submit]'), state = actions.querySelector('.save-state');
    const discard = document.createElement('button'); discard.type = 'button'; discard.className = 'secondary'; discard.textContent = 'Discard'; actions.insertBefore(discard, submit);
    state.setAttribute('role','status');
    const isDirty = () => saved.some(({el,value,checked}) => el.value !== value || el.checked !== checked);
    const update = () => {const dirty = isDirty(); submit.hidden = discard.hidden = !dirty; form.dataset.dirty = String(dirty); state.textContent = dirty ? 'Unsaved changes' : '';};
    form.addEventListener('input', update); form.addEventListener('change', update);
    discard.onclick = () => {
      for (const {el,value,checked} of saved) {el.value = value; el.checked = checked;}
      if (overlayMode) for (const name of ['viewportShape','hostCameraSlot']) form.elements[name].dispatchEvent(new Event('change',{bubbles:true}));
      form.dispatchEvent(new Event('input',{bubbles:true})); update();
    };
    form.markSaved = () => {saved = snapshot(); update(); form.querySelector('.slot').textContent = form.elements.name.value;};
    update();
  }
  window.addEventListener('beforeunload', event => {if(document.querySelector('[data-dirty="true"]')){event.preventDefault();event.returnValue='';}});
  return {init, metrics, card};
})();

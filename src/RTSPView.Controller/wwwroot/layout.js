/* Presentation only: retain the existing forms, API handlers and preview math. */
const adminLayout = (() => {
  let current = 'overview';
  let fitQueued = false;
  let fitWall = true;
  const pages = {};
  function init() {
    const css = document.createElement('link');
    css.rel = 'stylesheet'; css.href = 'layout.css?v=channels1'; css.onload = fitOverview; document.head.append(css);
    const main = document.querySelector('main');
    const banner = main.previousElementSibling;
    banner.remove();
    document.querySelector('header .tag').textContent = '…';
    const nav = document.createElement('nav'); nav.className = 'main-nav'; nav.setAttribute('aria-label', 'Administration');
    for (const [id, title] of Object.entries({overview:'Overview', cameras:'Cameras', layouts:'Layouts',overlays:'Overlays', system:'System'})) {
      const button = document.createElement('button'); button.type = 'button'; button.textContent = title;
      button.dataset.page = id; button.onclick = () => select(id); nav.append(button);
      const page = document.createElement('section'); page.id = 'page-' + id; page.className = 'admin-page'; page.setAttribute('aria-label', title);
      pages[id] = page;
    }
    const header = document.querySelector('header');
    header.insertBefore(nav, document.querySelector('#logout'));
    const mobileNav = document.createElement('select'); mobileNav.className = 'mobile-nav'; mobileNav.setAttribute('aria-label', 'Administration page');
    for (const button of nav.children) mobileNav.add(new Option(button.textContent, button.dataset.page));
    mobileNav.onchange = () => select(mobileNav.value); header.insertBefore(mobileNav, document.querySelector('#logout'));
    new ResizeObserver(() => {
      document.documentElement.style.setProperty('--admin-header-height', header.offsetHeight + 'px'); fitOverview();
    }).observe(header);
    const stats = document.querySelector('#stats');
    const performance = document.createElement('details'); performance.className = 'performance';
    performance.innerHTML = '<summary>Detailed performance</summary>';
    performance.addEventListener('toggle', fitOverview);
    const extra = document.createElement('div'); extra.id = 'extraStats'; extra.className = 'stats'; performance.append(extra);
    const overviewHead = document.createElement('div'); overviewHead.className = 'section-title';
    overviewHead.title = 'Select a camera snapshot to open its settings.';
    const sizeLabel = document.createElement('label'); sizeLabel.className = 'overview-density';
    sizeLabel.innerHTML = 'Preview size<select aria-label="Dashboard preview size"><option value="fit">Fit wall</option><option value="large">Larger previews</option></select>';
    sizeLabel.querySelector('select').onchange = event => {fitWall = event.target.value === 'fit'; fitOverview();};
    overviewHead.append(performance, sizeLabel);
    pages.overview.append(stats, overviewHead);
    const cameraGrid = document.querySelector('#cameras');
    cameraGrid.previousElementSibling.querySelector('h2').remove();
    const cameraHeader = cameraGrid.previousElementSibling;
    cameraHeader.classList.add('camera-page-toolbar');
    const count = document.createElement('span'); count.id='cameraCount';
    const addCameraButton = document.createElement('button'); addCameraButton.type='button'; addCameraButton.id='addCamera'; addCameraButton.textContent='+ Add camera'; addCameraButton.onclick=()=>addCameraEntry();
    cameraHeader.replaceChildren(count,addCameraButton);
    pages.cameras.append(cameraHeader);
    const overlayNav = document.createElement('nav'); overlayNav.className = 'overlay-nav'; overlayNav.setAttribute('aria-label','Select overlay');
    for (const [id, title] of [['doorbell','Doorbell'],['garage','Garage']]) {
      const button = document.createElement('button'); button.type = 'button'; button.textContent = title;
      button.dataset.overlayTarget = id; button.onclick = () => selectOverlay(id);
      button.setAttribute('aria-pressed', String(id === 'doorbell')); overlayNav.append(button);
      const grid = document.querySelector('#' + id); grid.classList.add('overlay-workspace');grid.previousElementSibling.remove(); grid.hidden = id !== 'doorbell'; pages.overlays.append(grid);
    }
    const overlayHead = document.createElement('div'); overlayHead.className = 'section-title';
    pages.overlays.prepend(overlayHead, overlayNav);
    const add = document.createElement('button');add.type='button';add.id='addOverlay';add.textContent='+ Add overlay';add.setAttribute('aria-label','Add overlay');add.title='Add overlay';add.onclick=()=>addOverlay();overlayNav.append(add);
    const addState=document.createElement('p');addState.id='addOverlayState';addState.setAttribute('role','status');overlayHead.append(addState);
    const viewer = document.querySelector('.viewer-display-panel'), display = document.querySelector('#displayForm'), updates = document.querySelector('#updatePanel');
    display.className = 'panel control-panel'; updates.className = 'panel control-panel';
    const channelSettings = document.createElement('div'); channelSettings.className = 'update-channel-settings';
    channelSettings.innerHTML = '<div class="channel-choice"><label>Update channel<select id="updateChannel"><option value="stable">Stable (main)</option><option value="beta">Beta</option></select></label><a href="/api/config/export" download="RTSPView-config.json">Export configuration before installing</a></div><div class="release-summary"><dl class="update-versions"><div><dt>Installed</dt><dd id="installedRelease">Checking…</dd></div><div><dt>Available on selected channel</dt><dd id="availableRelease">Checking…</dd></div></dl></div><p class="channel-help">Changing channels does not install anything. Install the selected release to switch this host.</p>';
    updates.insertBefore(channelSettings, document.querySelector('#updateState'));
    channelSettings.querySelector('.release-summary').append(document.querySelector('#updateState'));
    const installDialog = document.createElement('dialog'); installDialog.id = 'updateConfirm';
    installDialog.innerHTML = '<form method="dialog"><h2>Install selected release?</h2><p id="updateConfirmText"></p><p>The camera wall will restart. Follow progress on the Windows host.</p><a href="/api/config/export" download="RTSPView-config.json">Export configuration for rollback</a><div class="actions"><button value="cancel" class="secondary">Cancel</button><button value="install">Install now</button></div></form>';
    document.body.append(installDialog);
    viewer.querySelector('h2').textContent = 'Maintenance';
    viewer.querySelector('p').textContent = 'These commands take effect immediately on the Windows host.';
    pages.system.append(display, updates, document.querySelector('#configPanel'), viewer, document.querySelector('#passwordForm'), document.querySelector('#logView').closest('section'));
    const project = document.createElement('section'); project.className = 'panel control-panel';
    project.innerHTML = '<h2>RTSPView project</h2><p>Source code, documentation, releases and issue reporting.</p><a class="repository-link" href="https://github.com/GalacticaActual75/RTSPView" target="_blank" rel="noopener noreferrer">Open RTSPView on GitHub</a>';
    pages.system.append(project);
    for (const page of Object.values(pages)) main.append(page);
    document.querySelector('#controlState').setAttribute('role','status');
    // Commands from camera cards must remain visible outside the System page.
    document.body.append(document.querySelector('#controlState'));
    toggles(document.querySelector('#displayForm'));
    adminUi.init();
    window.addEventListener('resize', fitOverview);
    select('overview');
  }
  function select(id) {
    current = id;
    document.querySelector('.mobile-nav').value = id;
    document.querySelector('main').dataset.page = id;
    for (const [name, page] of Object.entries(pages)) page.hidden = name !== id;
    for (const button of document.querySelectorAll('[data-page]')) {
      button.setAttribute('aria-current', button.dataset.page === id ? 'page' : 'false');
    }
    if (id === 'overview' || id === 'cameras') pages[id].append(document.querySelector('#cameras'));
    document.querySelector('.hero h1').textContent = {overview:'Overview',cameras:'Cameras',layouts:'Layouts',overlays:'Overlays',system:'System'}[id];
    adminUi.page(id);
    window.dispatchEvent(new Event('resize'));
    window.scrollTo({top:0, behavior:'instant'});
    fitOverview();
  }
  function metrics() {
    const stats = document.querySelector('#stats');
    stats.firstElementChild.dataset.tone = stats.firstElementChild.querySelector('b').textContent === 'Connected' ? 'healthy' : 'error';
    document.querySelector('#extraStats').replaceChildren(...[...stats.children].slice(4));
    fitOverview();
  }
  function fitOverview() {
    if (fitQueued || current !== 'overview') return;
    fitQueued = true;
    requestAnimationFrame(() => {
      fitQueued = false;
      if (current !== 'overview') return;
      const grid = document.querySelector('#cameras'), card = grid?.querySelector('.camera-card'), image = card?.querySelector('.feed-thumbnail');
      if (!image || !grid.offsetWidth) return;
      const columns = getComputedStyle(grid).gridTemplateColumns.split(' ').length;
      const rows = Math.ceil(grid.children.length / columns);
      const gap = parseFloat(getComputedStyle(grid).rowGap) || 0;
      const top = grid.getBoundingClientRect().top + window.scrollY;
      const overhead = Math.max(...[...grid.children].map(tile => tile.getBoundingClientRect().height - tile.querySelector('.feed-thumbnail').getBoundingClientRect().height));
      const available = (window.innerHeight - top - 24 - gap * (rows - 1)) / rows - overhead;
      // On phones keep normal image proportions; on desktops fit three rows when readable.
      const height = fitWall && columns === 3 ? Math.max(96, Math.min(image.clientWidth * 9 / 16, available)) : image.clientWidth * 9 / 16;
      grid.style.setProperty('--overview-image-height', Math.floor(height) + 'px');
    });
  }
  function toggles(root) {
    for (const input of root.querySelectorAll('input[type=checkbox]')) {
      input.setAttribute('role', 'switch');
      if (input.closest('.switch') || input.closest('.toggle-control')) continue;
      const label = input.closest('label'); label.classList.add('toggle-control');
      const track = document.createElement('span'); track.className = 'toggle-track'; track.setAttribute('aria-hidden', 'true'); input.after(track);
    }
  }
  function card(form, overlayMode) {
    form.querySelector('.switch input').setAttribute('aria-label', 'Enable ' + form.elements.name.value);
    const settings = form.querySelector('.camera-settings');
    if (!overlayMode) {
      const open = document.createElement('button'); open.type = 'button'; open.className = 'overview-open';
      const label = () => open.setAttribute('aria-label', `Configure ${form.elements.name.value}`);
      label(); form.addEventListener('change', label);
      open.onclick = () => {
        select('cameras');
        for (const other of document.querySelectorAll('#cameras .camera-settings')) other.open = other === settings;
        requestAnimationFrame(() => {
          form.scrollIntoView({block:'start', behavior:'instant'});
          settings.querySelector('summary').focus({preventScroll:true});
        });
      };
      form.append(open);
    }
    if (overlayMode) {
      const stream = form.querySelector('.camera-preview');
      const status = document.createElement('details'); status.className = 'inspector-section overlay-status'; status.open = true;
      const heading = document.createElement('summary'); heading.textContent = 'Overlay status'; status.append(heading);
      stream.before(status); status.append(stream);
      settings.querySelector('summary').textContent = 'Overlay controls';
      const placement = settings.querySelector('.overlay-placement');
      const connection = document.createElement('details'); connection.className = 'inspector-section'; connection.innerHTML = '<summary>Connection and recovery</summary>';
      for (const child of [...settings.children]) if (child !== placement && child.tagName !== 'SUMMARY') connection.append(child);
      settings.append(connection);
      connection.append(form.querySelector('.restart-camera'));
      const makeGroup = (title, names) => {
        const section = document.createElement('details'); section.className = 'inspector-section'; section.open = true;
        const heading = document.createElement('summary'); heading.textContent = title; section.append(heading);
        const group = document.createElement('div'); group.className = 'inspector-fields'; section.append(group);
        for (const name of names) group.append(form.elements[name].closest('label'));
        placement.append(section); return group;
      };
      for (const title of placement.querySelectorAll('.overlay-group-title')) title.remove();
      makeGroup('Placement', ['hostCameraSlot','viewportWidthPercent','viewportHeightPercent','viewportHorizontalPositionPercent','viewportVerticalPositionPercent']);
      makeGroup('Appearance',['viewportOpacityPercent']);
      const shape = makeGroup('Shape / Mask',['viewportShape']);
      shape.append(placement.querySelector('.open-shape-editor'),placement.querySelector('.custom-viewport-upload'));
      makeGroup('Framing',['zoomPercent','imageHorizontalPositionPercent','imageVerticalPositionPercent']);
      placement.append(placement.querySelector('.reset-doorbell-framing'));
      const help = placement.querySelector('.overlay-help'), details = document.createElement('details');
      details.innerHTML = '<summary>Framing help</summary>';
      help.textContent = 'Drag in Wall preview to position the overlay. Use Source framing to choose the visible part of the camera image. The image keeps its proportions. Position 0 is left/top, 50 is centered, and 100 is right/bottom. Overlay software decoding may increase CPU usage.';
      details.append(help); placement.append(details);
    }
    toggles(form);
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
  function release(version) {
    const beta = version.includes('-beta.');
    const badge = document.querySelector('header .tag');
    badge.textContent = beta ? 'BETA' : 'STABLE'; badge.title = 'Installed release: ' + version;
    badge.classList.toggle('stable', !beta);
    document.title = beta ? 'RTSPView Beta Admin' : 'RTSPView Admin';
  }
  function selectOverlay(id) {
    for(const grid of pages.overlays.querySelectorAll('.overlay-workspace'))grid.hidden=grid.id!==id;
    for(const button of pages.overlays.querySelectorAll('[data-overlay-target]'))button.setAttribute('aria-pressed',String(button.dataset.overlayTarget===id));
    window.dispatchEvent(new Event('resize'));
  }
  function overlayGrid(id,title) {
    let grid=document.getElementById(id);
    if(!grid){grid=document.createElement('div');grid.id=id;grid.className='camera-grid doorbell-grid overlay-workspace';grid.hidden=true;pages.overlays.append(grid);const button=document.createElement('button');button.type='button';button.dataset.overlayTarget=id;button.onclick=()=>selectOverlay(id);button.setAttribute('aria-pressed','false');document.getElementById('addOverlay').before(button)}
    pages.overlays.querySelector(`[data-overlay-target="${id}"]`).textContent=title;
    return grid;
  }
  function clearExtraOverlays(){for(const grid of pages.overlays.querySelectorAll('.overlay-workspace'))if(!['doorbell','garage'].includes(grid.id))grid.remove();for(const button of pages.overlays.querySelectorAll('[data-overlay-target]'))if(!['doorbell','garage'].includes(button.dataset.overlayTarget))button.remove();selectOverlay('doorbell')}
  return {init, metrics, card, release, overlayGrid, selectOverlay, clearExtraOverlays};
})();

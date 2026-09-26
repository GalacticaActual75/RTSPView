(() => {
  const panel = document.querySelector('#updatePanel');
  if (!panel) return;
  const section = document.createElement('div');section.className='streaming-plugins-status';
  const title = document.createElement('h3');
  title.textContent = 'Automatic streaming components';
  const explanation = document.createElement('p');
  explanation.textContent = 'yt-dlp and Streamlink update daily in the background when a tested package is available. No installer, administrator prompt, or restart is needed. Existing streams continue running.';
  const status = document.createElement('p');
  status.setAttribute('role', 'status');
  section.append(title, explanation, status);
  panel.append(section);
  async function refresh() {
    if (document.querySelector('#app')?.hidden || document.hidden) return;
    section.hidden=!pluginsUi.enabled('ytDlp')&&!pluginsUi.enabled('streamlink');if(section.hidden)return;
    explanation.textContent='Enabled streaming components update daily when a tested package is available.';
    try {
      const response = await fetch('/api/streaming-update', { credentials: 'same-origin', cache: 'no-store' });
      if (!response.ok) return;
      const { state, versions } = await response.json();
      const names = ['yt-dlp', 'streamlink'].filter(name => versions[name]&&pluginsUi.enabled(name==='yt-dlp'?'ytDlp':'streamlink')).map(name => `${name} ${versions[name]}`);
      const checked = state.lastCheck ? `Last checked: ${new Date(state.lastCheck).toLocaleString()}.` : 'Waiting for the first automatic check.';
      status.textContent = `${names.join(' · ')}${names.length ? '. ' : ''}${'Automatic updates enabled.'} ${checked}`;
    } catch { status.textContent = 'Streaming update status is temporarily unavailable.'; }
  }
  refresh();
  setInterval(refresh, 15000);
})();

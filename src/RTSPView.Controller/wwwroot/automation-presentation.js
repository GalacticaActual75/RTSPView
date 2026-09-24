/* Shared presentation for the two trigger models. Configuration stays in their forms. */
const automationPresentation = (() => {
  function status(element, text, tone = 'neutral') {
    if (element.textContent !== text) element.textContent = text; element.dataset.tone = tone;
  }
  function toggles(root) {
    for (const input of root.querySelectorAll('input.automation-toggle')) {
      input.classList.remove('automation-toggle'); input.parentElement.classList.add('toggle-control');
      const track = document.createElement('span'); track.className = 'toggle-track'; track.setAttribute('aria-hidden', 'true'); input.after(track); input.parentElement.prepend(input, track);
    }
  }
  function summary(element, name, values) {
    const title = document.createElement('span'); title.className = 'rule-summary-name'; title.textContent = name || 'Unnamed rule';
    const fields = document.createElement('span'); fields.className = 'automation-rule-summary';
    for (const [label, text] of values) {
      const field = document.createElement('span'), caption = document.createElement('small'), value = document.createElement('span');
      caption.textContent = ({Trigger:'When',Action:'Then',Duration:'For / until',Clear:'Afterward'})[label] || label; value.textContent = text; field.append(caption, value); fields.append(field);
    }
    element.replaceChildren(title, fields);
  }
  function delivery(value) {
    if (!value) return 'No viewer acknowledgement yet';
    return value.success ? 'Viewer acknowledged · ' + new Date(value.attemptedAt).toLocaleTimeString() : 'Viewer acknowledgement failed · ' + value.message;
  }
  function mqttConnection(value, enabled, now = Date.now()) {
    const health = value.viewerConnection;
    const recovered = health?.success === true && new Date(health.attemptedAt) >= new Date(value.delivery?.attemptedAt || 0) && now - new Date(health.attemptedAt) < 10000;
    const problem = value.configurationError || (enabled && value.delivery?.success === false && !recovered ? delivery(value.delivery) + ' Check Live View in Quick actions.' : '');
    const detail = problem || (value.connection === 'Disabled' ? 'Integration disabled · enable MQTT and Save & apply to activate rules' : recovered && value.delivery?.success === false ? 'Viewer connected · previous automation delivery failed; see rule status and Recent activity' : value.lastResult || 'Waiting for person events');
    return {text: 'MQTT: ' + value.connection + ' · ' + detail, tone: problem || value.connection === 'Error' ? 'error' : value.connection === 'Connected' ? 'healthy' : 'neutral'};
  }
  function pending(form, dirty, enabled) {
    let label = form.querySelector('.automation-draft-state');
    if (!label) { label = document.createElement('p'); label.className = 'automation-draft-state'; label.setAttribute('role', 'status'); form.querySelector('h2').after(label); }
    status(label, `Saved: ${enabled ? 'Enabled' : 'Disabled'}${dirty ? ' · Unsaved changes — Save changes to apply' : ''}`, dirty ? 'warning' : 'neutral');
  }
  function visibility(presentation, source, id) {
    if (!presentation || Date.now() - new Date(presentation.at) > 5000) return 'Viewer priority status unavailable';
    const rows = presentation.rules.filter(r => r.source === source && r.ruleId === id);
    return rows.some(r => r.effective) ? 'Wins viewer priority' : rows.length ? [...new Set(rows.map(r => r.reason))].join(' · ') : 'No current viewer override';
  }
  function activity(form, data) {
    let section = form.querySelector('.automation-activity');
    if (!section) {
      section = document.createElement('details'); section.className = 'automation-activity';
      const heading = document.createElement('summary'); heading.textContent = 'Recent activity';
      const note = document.createElement('p'); note.className = 'muted';
      const list = document.createElement('ol'); section.append(heading, note, list);
      const savebar = form.querySelector('.automation-savebar'); if (savebar) savebar.before(section); else form.append(section);
    }
    section.querySelector('p').textContent = (data.activityError || 'Last 200 decisions are kept across restarts. Repeated identical events are grouped. Viewer status reports priority, not decoded video.') + (data.droppedEvents !== undefined ? ` Dropped MQTT events since Controller start: ${data.droppedEvents}.` : '');
    const signature = JSON.stringify(data.activity || []);
    if (section.dataset.signature === signature) return;
    section.dataset.signature = signature;
    section.querySelector('ol').replaceChildren(...(data.activity || []).map(entry => {
      const row = document.createElement('li'); row.textContent = `${new Date(entry.at).toLocaleString()} · ${entry.name} · ${entry.kind}: ${entry.decision}`; return row;
    }));
  }
  return {status, mqttConnection, toggles, summary, delivery, pending, visibility, activity};
})();

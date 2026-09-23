/* Promise-based application dialogs. All message text is treated as text, not HTML. */
const uiDialogs = (() => {
  let queue = Promise.resolve(), sequence = 0;
  function open(message, options = {}) {
    return new Promise(resolve => {
      const previous = document.activeElement, dialog = document.createElement('dialog');
      dialog.className = 'app-dialog';
      const id = 'app-dialog-' + ++sequence;
      const heading = document.createElement('h2'), copy = document.createElement('p');
      heading.id = id; copy.id = id + '-description';
      heading.textContent = options.title || message.split('?')[0] + (message.includes('?') ? '?' : '');
      copy.textContent = options.title ? message : message.includes('?') ? message.slice(message.indexOf('?') + 1).trim() : '';
      dialog.setAttribute('aria-labelledby', heading.id); dialog.setAttribute('aria-describedby', copy.id);
      const symbol = document.createElement('span'); symbol.className = 'dialog-symbol'; symbol.textContent = options.danger === false ? 'i' : '!'; symbol.setAttribute('aria-hidden', 'true');
      dialog.append(symbol, heading, copy);
      let input;
      if (options.input !== undefined) {
        const label = document.createElement('label'); label.textContent = options.label || 'Name';
        input = document.createElement('input'); input.value = options.input; input.maxLength = options.maxLength || 200; label.append(input); dialog.append(label);
        input.addEventListener('keydown', event => { if (event.key === 'Enter' && !event.isComposing) { event.preventDefault(); dialog.close('accept'); } });
      }
      const actions = document.createElement('form'); actions.method = 'dialog'; actions.className = 'dialog-actions';
      const cancel = document.createElement('button'); cancel.value = 'cancel'; cancel.className = 'secondary'; cancel.textContent = 'Cancel';
      const accept = document.createElement('button'); accept.value = 'accept'; accept.className = options.danger === false ? '' : 'danger';
      accept.textContent = options.accept || (/^Delete /i.test(message) ? 'Delete' : /^Sign out/i.test(message) ? 'Sign out' : /^Disable/i.test(message) ? 'Disable access' : /^Reboot/i.test(message) ? 'Reboot host' : /^Replace/i.test(message) ? 'Import configuration' : /^Revoke/i.test(message) ? 'Revoke access' : /^Remove/i.test(message) ? 'Remove' : /^Restart/i.test(message) ? 'Restart' : 'Continue');
      actions.append(cancel, accept); dialog.append(actions); document.body.append(dialog);
      dialog.addEventListener('close', () => {
        const result = dialog.returnValue === 'accept' ? input ? input.value : true : input ? null : false;
        dialog.remove(); if (previous?.isConnected) previous.focus(); resolve(result);
      }, {once:true});
      dialog.showModal(); (input || cancel).focus();
    });
  }
  function ask(message, options) {
    const request = queue.then(() => open(message, options)); queue = request.catch(() => {}); return request;
  }
  function toast(message, tone = 'success') {
    let stack = document.getElementById('toastStack');
    if (!stack) { stack = document.createElement('div'); stack.id = 'toastStack'; stack.className = 'toast-stack'; document.body.append(stack); }
    const item = document.createElement('div'); item.className = 'app-toast'; item.dataset.tone = tone;
    item.setAttribute('role', tone === 'error' ? 'alert' : 'status');
    const text = document.createElement('span'); text.textContent = message;
    const dismiss = document.createElement('button'); dismiss.type = 'button'; dismiss.className = 'quiet'; dismiss.textContent = '×'; dismiss.title = 'Dismiss notification'; dismiss.setAttribute('aria-label', 'Dismiss notification');
    let timer; const clear = () => clearTimeout(timer), start = () => { clear(); if (tone !== 'error') timer = setTimeout(() => item.remove(), 6500); };
    dismiss.onclick = () => { clear(); item.remove(); }; item.onmouseenter = clear; item.onmouseleave = start; item.onfocusin = clear; item.onfocusout = start;
    item.append(text, dismiss); stack.append(item); while (stack.children.length > 4) stack.firstElementChild.remove();
    // Keep save feedback visible above an open stream editor without making it modal.
    if ('showPopover' in stack) { stack.popover = 'manual'; if (stack.matches(':popover-open')) stack.hidePopover(); stack.showPopover(); }
    start();
  }
  return {ask, toast};
})();

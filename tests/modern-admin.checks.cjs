const assert = require('node:assert/strict'), fs = require('node:fs'), vm = require('node:vm');
const root = 'src/RTSPView.Controller/wwwroot/';
for (const file of fs.readdirSync(root).filter(f => /\.(js|html)$/.test(f))) {
  const source = fs.readFileSync(root + file, 'utf8');
  assert(!/\b(?:alert|confirm|prompt)\s*\(/.test(source), file + ' must not use a browser dialog');
  assert(!/beforeunload/.test(source), file + ' must not register a native leave warning');
}
const html = fs.readFileSync(root + 'index.html', 'utf8');
assert(html.indexOf('ui-dialogs.js') < html.indexOf('app.js'), 'Dialogs must load before consumers');
assert(html.indexOf('modern-shell.js') > html.indexOf('feedback.js'), 'Shell moves the existing feedback action after initialization');
let document;
class Element {
  constructor(tag) { this.tagName = tag; this.children = []; this.attributes = {}; this.dataset = {}; this.listeners = {}; this.isConnected = false; }
  setAttribute(key, value) { this.attributes[key] = value; }
  append(...children) { for (const child of children) { child.parent = this; child.isConnected = true; this.children.push(child); } }
  addEventListener(name, callback) { this.listeners[name] = callback; }
  showModal() { this.open = true; }
  close(value = '') { this.returnValue = value; this.open = false; this.listeners.close(); }
  focus() { document.activeElement = this; }
  remove() { this.parent.children = this.parent.children.filter(c => c !== this); this.isConnected = false; }
}
document = {body:new Element('body'),createElement:tag => new Element(tag)};
const trigger = new Element('button'); document.body.append(trigger); trigger.focus();
const context = vm.createContext({document,Promise,setTimeout,clearTimeout});
vm.runInContext(fs.readFileSync(root + 'ui-dialogs.js', 'utf8') + '\nthis.dialogs = uiDialogs;', context);
const flush = () => new Promise(resolve => setImmediate(resolve));
const modal = () => document.body.children.find(e => e.tagName === 'dialog');
(async () => {
  const name = '<img src=x onerror=attack()> & Front Door';
  const cancelled = context.dialogs.ask('Delete ' + name + '? This removes its saved connection.');
  await flush();
  assert.equal(modal().children[1].textContent, 'Delete ' + name + '?', 'Names remain text');
  assert.equal(document.activeElement.textContent, 'Cancel', 'Destructive modal defaults to Cancel');
  assert.equal(modal().attributes['aria-labelledby'], modal().children[1].id);
  modal().close(); assert.equal(await cancelled, false); assert.equal(document.activeElement, trigger);
  const first = context.dialogs.ask('Delete first?'), second = context.dialogs.ask('Delete second?');
  await flush(); assert.equal(document.body.children.filter(e => e.tagName === 'dialog').length, 1);
  modal().close('accept'); assert.equal(await first, true); await flush();
  assert.equal(modal().children[1].textContent, 'Delete second?'); modal().close('cancel'); assert.equal(await second, false);
  const input = context.dialogs.ask('Choose a name', {title:'New layout', input:'Draft', danger:false});
  await flush(); assert.equal(document.activeElement.value, 'Draft'); document.activeElement.value = 'Front wall';
  modal().close('accept'); assert.equal(await input, 'Front wall'); assert.equal(modal(), undefined);
  document.querySelector = selector => selector === '.request-reference'
    ? document.body.children.find(e => e.className === 'request-reference')
    : selector === 'dialog[open]' ? document.body.children.find(e => e.tagName === 'dialog' && e.open) : null;
  const destinations = [];
  context.wallDesigner = {openLayout:(id, automation) => destinations.push([id, automation])};
  const app = fs.readFileSync(root + 'app.js', 'utf8');
  vm.runInContext(app.slice(app.indexOf('function showRequestReference('), app.indexOf("document.addEventListener('invalid'")), context);
  const show = () => context.showRequestReference({error:'Overlay is in use.', related:[{kind:'automationLayout', id:'focus', name}]});
  show();
  let reference = modal();
  assert.equal(reference.parent, document.body, 'Dependency error is outside the sidebar layout');
  assert.equal(reference.open, true, 'Dependency error uses a modal top layer');
  assert.equal(reference.attributes['aria-labelledby'], 'request-reference-title');
  assert.equal(document.activeElement.textContent, 'Dismiss');
  assert.equal(reference.children[3].children[0].textContent, 'Automation layout · ' + name, 'Reference names remain text');
  reference.children[4].children[0].onclick();
  assert.equal(modal(), undefined); assert.equal(document.activeElement, trigger, 'Dismiss restores focus');
  show(); reference = modal(); reference.children[3].children[0].onclick();
  assert.deepEqual(destinations, [['focus', true]], 'Link opens the referenced automation layout');
  assert.equal(modal(), undefined, 'Navigation removes the blocker dialog');
  const parent = new Element('dialog'); document.body.append(parent); parent.showModal();
  parent.listeners.close = () => parent.remove();
  show(); reference = document.querySelector('.request-reference'); reference.children[4].children[0].onclick();
  assert.equal(parent.open, true, 'Dismiss preserves an existing editor dialog');
  show(); document.querySelector('.request-reference').children[3].children[0].onclick();
  assert.equal(modal(), undefined, 'Reference navigation closes the existing editor');
  console.log('PASS Modern Dark admin: no native dialogs, script order, safe names, cancellation, focus return, queue isolation, input dialogs.');
  console.log('PASS Dependency errors: top-layer modal, safe reference names, focus return, layout navigation, nested editor dismissal.');
})().catch(error => { console.error(error); process.exitCode = 1; });

const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict');
const { webcrypto } = require('node:crypto');
const source = fs.readFileSync('src/RTSPView.Controller/wwwroot/wall-designer.js', 'utf8');
// Ordinary HTTP exposes getRandomValues, but not randomUUID. Match that API surface.
const context = vm.createContext({crypto: {getRandomValues: value => webcrypto.getRandomValues(value)}, window: {addEventListener() {}}});
vm.runInContext(source, context);
const ids = new Set(Array.from({length: 1000}, () => vm.runInContext('layoutItemId()', context)));
assert.equal(ids.size, 1000);
for (const id of ids) assert.match(id, /^[a-f0-9]{32}$/);
assert(!source.includes('crypto.randomUUID('), 'layout actions must work on LAN HTTP');

// Execute the actual Add weather handler with both a full and an empty grid.
const action = source.slice(source.indexOf("if(!isAutomation&&pluginsUi.enabled('weather'))button('Add weather'"), source.indexOf("    for(const [id,label] of [['add','Add stream']"));
for (const full of [true, false]) {
  class Element {
    constructor() {this.options = [];this.value = '';this.children = [];}
    append(...items) {this.children.push(...items);}
    add(option) {this.options.push(option);if (!this.value) this.value = option.value;}
    showModal() {context.dialogs++;}
  }
  Object.assign(context, {
    pluginsUi:{enabled:()=>true}, isAutomation: false, selectedTile: -1, actions: {}, drawer: '', dialogs: 0, editors: 0, changes: 0,
    layout: {rows: 3, columns: 3, tiles: full ? Array.from({length:9},(_,i)=>({cameraSlot:i+1})) : []},
    config: {cameras: Array.from({length:9},(_,i)=>({slot:i+1,name:'Camera '+(i+1)}))},
    validTile: () => !full, el: () => new Element(), field() {},
    document: {body: new Element()}, Option: class {constructor(text,value){this.value=String(value);}},
    button(label, handler) {if(label==='Add weather') handler();return {};},
    changed() {context.changes++;},
    weatherUi: {defaults: () => ({}), editor(options, save) {context.editors++;save({location:'Test'});}}
  });
  vm.runInContext(action, context);
  assert.equal(context.dialogs, full ? 1 : 0, 'full grid opens a choice dialog');
  assert.equal(context.editors, full ? 0 : 1, 'empty grid opens the weather editor');
  if (!full) {assert.equal(context.layout.tiles.length,1);assert(context.layout.tiles[0].itemId);assert.equal(context.changes,1);}
}
console.log('PASS LAN HTTP: full-grid Add weather dialog, empty-grid weather creation, unique IDs without randomUUID.');

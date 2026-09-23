const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
function element(){return {dataset:{},textContent:'',children:[],append(item){this.children.push(item)},replaceChildren(){this.children=[]}}}
const context=vm.createContext({document:{createElement:element}});
vm.runInContext(fs.readFileSync('src/RTSPView.Controller/wwwroot/admin-ui.js','utf8')+'\nthis.ui=adminUi;',context);
const label=element(),details=element(),box={dataset:{},querySelector(selector){return selector==='.state'?label:details}};
const base={fps:0,bitrateKbps:0,reconnectCount:0,frameWarning:'No video frames received for 107 seconds'};
context.ui.stream(box,{...base,state:'Resolving'},true);
assert.equal(label.textContent,'Opening website stream');
assert(!details.children.some(item=>item.textContent.includes('107 seconds')));
context.ui.stream(box,{...base,state:'Offline',lastError:'The website refused access (HTTP 403).'},true);
assert.equal(label.textContent,'Stream error');assert.equal(box.dataset.tone,'error');
assert(details.children.at(-1).textContent.startsWith('The website refused access (HTTP 403).'));
context.ui.stream(box,{...base,state:'Disabled',lastError:'Previous refusal'},true);
assert.equal(label.textContent,'Disabled');
context.ui.stream(box,{...base,state:'Live'},true);assert.equal(label.textContent,'Stale video');
console.log('PASS stream presentation: opening, explicit refusal priority, disabled state, and genuinely stale playback.');

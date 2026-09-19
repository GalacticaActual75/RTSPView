const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const Zip = require('adm-zip');
const zip = new Zip('dist/plugin.zip');
const entries = zip.getEntries().map(entry => entry.entryName);
assert(entries.includes('main.nodejs.js') && entries.includes('sdk.json') && entries.includes('README.md'));
assert.match(zip.readAsText('README.md'), /https:\/\/github.com\/GalacticaActual75\/RTSPView/);
for (const entry of zip.getEntries()) {
  const text = entry.getData().toString();
  assert(!text.includes(process.cwd()) && !text.includes(process.cwd().replaceAll('\\','/')), 'Local checkout path in package');
}
const saved = new Map();
const storage = {getItem:key=>saved.get(key)??null,setItem:(key,value)=>saved.set(key,value),removeItem:key=>saved.delete(key)};
const context = vm.createContext({exports:{},require,process,Buffer,URL,AbortSignal,fetch,TextEncoder,TextDecoder,
  setTimeout,clearTimeout,setImmediate,clearImmediate,setInterval:()=>({unref(){}}),clearInterval,
  console:{...console,warn:()=>{}},
  deviceManager:{getDeviceLogger:()=>console,getDeviceStorage:()=>storage},endpointManager:{},mediaManager:{},systemManager:{},pluginHostAPI:{}});
context.global = context;
try { vm.runInContext(zip.readAsText('main.nodejs.js'),context,{timeout:5000}); }
catch (error) { console.error('Packaged plugin load failed: ' + error.message); process.exit(1); }
assert.equal(typeof context.exports.default,'function','Bundle does not export a plugin constructor');
const plugin=new context.exports.default();
plugin.getSettings().then(settings=>{
  assert(settings.some(s=>s.key==='pairCode') && settings.some(s=>s.key==='sync'));
  assert(saved.has('instanceId'),'Packaged plugin failed to initialize');
  console.log('PASS: packaged plugin exports, SDK initialization, settings, README and local-path exclusion.');
}).catch(error=>{console.error(error);process.exitCode=1});

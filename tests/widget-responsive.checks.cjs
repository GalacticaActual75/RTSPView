const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const context=vm.createContext({setInterval(){},ResizeObserver:class{observe(){}},Math,Number,Map});
for(const file of ['widget-responsive.js','weather.js','wall-proportions.js','wall-layout-presets.js','layout-widgets.js'])vm.runInContext(fs.readFileSync('src/RTSPView.Controller/wwwroot/'+file,'utf8'),context);
const view=vm.runInContext('widgetViewport',context),widgets=vm.runInContext('layoutWidgetsUi',context);
for(const record of JSON.parse(fs.readFileSync('artifacts/widgets/viewport-contract.json','utf8'))){
 for(const [key,value] of Object.entries(record.Weather)){const actual=view.weatherMeasure(record.Weather.Width,record.Weather.Height,record.Density)[key[0].toLowerCase()+key.slice(1)];if(typeof value==='number')assert(Math.abs(actual-value)<1e-8,key+' weather parity');else assert.equal(actual,value);}
 for(const emphasis of [false,true])for(const [key,value] of Object.entries(emphasis?record.PhotoEmphasis:record.Photo)){const actual=view.aircraftPhoto(record.Viewport.Width,record.Viewport.Height,emphasis)[key[0].toLowerCase()+key.slice(1)];if(typeof value==='number')assert(Math.abs(actual-value)<1e-8,key+' photo parity');else assert.equal(actual,value);}
 const native=record.Viewport,browser=view.measure(native.Width,native.Height,record.Density);
 for(const [key,value]of Object.entries(native)){const actual=browser[key[0].toLowerCase()+key.slice(1)];if(typeof value==='number')assert(Math.abs(actual-value)<1e-8,key+' parity');else assert.equal(actual,value);}
 assert(browser.font>=12&&browser.font<=32);assert(browser.reading>=24);assert(browser.padding<=24);
}
for(const dimension of [1,2,3]){
 const layout={rows:dimension,columns:dimension,tiles:[],rowWeights:[],columnWeights:[]},widget={cell:{row:dimension-1,column:dimension-1,rowSpan:1,columnSpan:1}};
 const b=widgets.bounds(widget,layout,1920,1080);assert(Math.abs(b.width-1920/dimension)<1e-8);assert(Math.abs(b.height-1080/dimension)<1e-8);assert(b.left+b.width<=1920.001&&b.top+b.height<=1080.001);
}
const mixed={rows:2,columns:3,tiles:[],rowWeights:[.25,.75],columnWeights:[.2,.3,.5]};
assert.deepEqual(JSON.parse(JSON.stringify(widgets.bounds({cell:{row:0,column:1,rowSpan:2,columnSpan:2}},mixed,1920,1080))),{left:384,top:0,width:1536,height:1080});
const transpose=vm.runInContext('wallLayoutPresets.transpose',context),original={...mixed,widgets:[{cell:{row:0,column:1,rowSpan:2,columnSpan:2}}]};
assert.deepEqual(JSON.parse(JSON.stringify(transpose(transpose(original)))),original);
console.log('PASS native/browser responsive policy parity, readable typography, 1x1/2x2/3x3 grid tiles and mixed multi-cell spans.');

assert(view.weatherMeasure(1280,720).wide);assert(view.weatherMeasure(1280,720).font>view.weatherMeasure(640,360).font);assert(view.weatherMeasure(1280,720).reading>view.weatherMeasure(640,360).reading);

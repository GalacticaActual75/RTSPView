const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const context=vm.createContext({setInterval(){},ResizeObserver:class{observe(){}},Intl,Date,Math,Number,Map,Set});
for(const file of ['widget-responsive.js','weather.js','system-widgets.js','layout-widgets.js'])vm.runInContext(fs.readFileSync('src/RTSPView.Controller/wwwroot/'+file,'utf8'),context);
const ui=vm.runInContext('systemWidgetsUi',context),defaults=ui.dateTime.defaults();
const o={...defaults,timeZone:'America/Los_Angeles',use24Hour:true,dateFormat:'iso'};
assert.equal(ui.clock(o,new Date('2026-03-08T09:59:59Z'),'UTC')[0],'01:59:59');
assert.equal(ui.clock(o,new Date('2026-03-08T10:00:00Z'),'UTC')[0],'03:00:00');
assert.equal(ui.clock({...o,use24Hour:false},new Date('2026-03-08T10:00:00Z'),'UTC')[0],'3:00:00 AM');
assert.equal(ui.clock({...o,timeZone:'auto'},new Date('2026-03-08T10:00:00Z'),'America/Los_Angeles')[1],'Sunday · 2026-03-08');
assert.equal(ui.clock({...o,showDate:false,showWeekday:false},new Date(),'UTC')[1],'');
assert.equal(ui.clock({...o,timeZone:'UTC'},new Date('2026-03-08T00:00:00Z'),'UTC')[0],'00:00:00');
for(const kind of ['systemStats','dateTime']){
 const options=ui[kind].defaults();assert.equal(options.appearance.contentOpacity,100);assert.equal(options.appearance.latitude,0);assert.equal(options.appearance.longitude,0);assert(options.appearance.location);
 const b=vm.runInContext('layoutWidgetsUi',context).bounds({kind,[kind]:options,margin:12,widthPercent:40,x:100,y:100}, {},1920,1080);
 assert(b.left+b.width<=1920&&b.top+b.height<=1080&&b.height>0);
}
console.log('PASS browser clock DST, host time zone, midnight, hidden date, opacity defaults and placement.');

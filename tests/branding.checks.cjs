// Prevent old product branding from returning; keep explicit migration identifiers reviewable.
const fs=require('fs'),cp=require('child_process'),assert=require('assert/strict');
const policy=JSON.parse(fs.readFileSync('tests/branding-compatibility.json','utf8'));
const files=cp.execFileSync('git',['ls-files','--cached','--others','--exclude-standard','-z'],{encoding:'utf8'}).split('\0').filter(Boolean);
const oldBrand=/spot[ _-]*monitor/i;const failures=[];let checked=0;
for(const file of new Set(files)){
 if(oldBrand.test(file))failures.push(file+': old source filename');
 if(!fs.existsSync(file)||file==='tests/branding-compatibility.json'||policy.historicalDocuments.includes(file))continue;
 const bytes=fs.readFileSync(file);if(bytes.includes(0))continue;let text=bytes.toString('utf8');
 for(const allowed of [...(policy.files[file]||[])].sort((a,b)=>b.length-a.length))text=text.split(allowed).join('');
 if(oldBrand.test(text))failures.push(file+': unexpected old branding');checked++;
}
assert.deepEqual(failures,[],'Branding regression');
const progress=fs.readFileSync('deployment/Show-UpdateProgress.ps1','utf8');assert(progress.includes("$title.Text = 'Updating RTSPView'"));
console.log(`Branding checks passed: ${checked} source files; only documented migration/historical references remain.`);

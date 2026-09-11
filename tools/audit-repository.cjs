// Read-only audit: reports locations/categories, never matching values.
const fs=require('node:fs'), cp=require('node:child_process');
const git=(...args)=>cp.execFileSync('git',args,{maxBuffer:128*1024*1024});
const patterns={
 'credential-bearing URL':/\b(?:rtsp|https?|smb|ftp):\/\/[^\s/@]+:[^\s/@]+@/i,
 'private key':/-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----/,
 'provider token':/\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[A-Z0-9]{16}|sk-[A-Za-z0-9]{32,}|xox[baprs]-[A-Za-z0-9-]{20,})\b/,
 'assigned secret':/(?:password|passwd|token|secret|api[_-]?key)\s*[=:]\s*["'][^"'\r\n]{6,}["']/i,
 'private network':/\b(?:192\.168\.\d{1,3}\.\d{1,3}|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})\b|\\\\[A-Za-z0-9_-]+\\/i,
 'personal path':/[A-Z]:\\(?:Users|Codex)\\[^\s"'<>]+|\/(?:home|Users|volume1|mnt)\/[^\s"'<>]+/i,
 'email':/\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/i
};
const named=git('rev-list','--objects','--all','--reflog').toString().trim().split('\n');
const names=new Map(named.map(x=>[x.slice(0,40),x.slice(41)]));
const objects=git('cat-file','--batch-all-objects','--batch-check=%(objectname)').toString().trim().split('\n').map(id=>id+' '+(names.get(id)||'[unreachable/unmapped]'));
const report={commits:Number(git('rev-list','--all','--reflog','--count')),objects:objects.length,findings:[],binary:[],current:[],metadata:[]};
for(let base=0;base<objects.length;base+=25){
const group=objects.slice(base,base+25);
const input=group.map(x=>x.split(' ')[0]).join('\n')+'\n';
const batch=cp.spawnSync('git',['cat-file','--batch'],{input,maxBuffer:256*1024*1024});
if(batch.status!==0)throw Error('Unable to read Git objects');
let offset=0;
for(const entry of group){const end=batch.stdout.indexOf(10,offset),header=batch.stdout.subarray(offset,end).toString().split(' '),size=Number(header[2]);const data=batch.stdout.subarray(end+1,end+1+size);offset=end+size+2;const name=entry.slice(41);
 if(header[1]==='commit'||header[1]==='tag'){const text=data.toString();const categories=Object.entries(patterns).filter(([,p])=>p.test(text)).map(([k])=>k);if(categories.length)report.metadata.push({object:header[0],type:header[1],categories});}
 if(header[1]!=='blob')continue;
 if(data.includes(0)){
   const matches=new Set();
   for(let pos=0;pos<data.length;pos+=2048){
     const chunk=data.subarray(pos,pos+4096);
     const decoded=chunk.toString('latin1').replace(/\x00/g,'');
     for(const [category,p]of Object.entries(patterns))if(p.test(decoded))matches.add(category);
   }
   const categories=[...matches];
   report.binary.push({object:header[0],path:name,bytes:size,categories});continue;
 }
 const lines=data.toString().split('\n');const hits=[];for(let i=0;i<lines.length;i++)for(const [category,p]of Object.entries(patterns))if(p.test(lines[i]))hits.push({line:i+1,category});
 if(hits.length)report.findings.push({object:header[0],path:name,hits});
}
}
for(const name of git('ls-files','--cached','--others','--exclude-standard','-z').toString().split('\0').filter(Boolean)){if(!fs.existsSync(name))continue;const data=fs.readFileSync(name);if(data.includes(0))continue;const hits=[];data.toString().split('\n').forEach((line,i)=>{for(const[category,p]of Object.entries(patterns))if(p.test(line))hits.push({line:i+1,category});});if(hits.length)report.current.push({path:name,hits});}
fs.mkdirSync('artifacts/security',{recursive:true});fs.writeFileSync('artifacts/security/repository-scan.json',JSON.stringify(report,null,2));
console.log(JSON.stringify({commits:report.commits,objects:report.objects,historicalCandidateBlobs:report.findings.length,binary:report.binary,current:report.current,metadataObjects:report.metadata.length},null,2));

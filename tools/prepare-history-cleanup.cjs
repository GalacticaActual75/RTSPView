// Prepares private inputs only. Does not rewrite, prune, commit, or push Git data.
const fs=require('node:fs'),cp=require('node:child_process');
const git=(...a)=>cp.execFileSync('git',a,{encoding:'utf8',maxBuffer:32*1024*1024});
const sources=['HANDOFF.md','README.md','src/RTSPView.Controller/UpdateService.cs','src/RTSPView.Core/CameraSettings.cs'];
const replacements=new Map();
for(const file of sources){
 const text=git('show','HEAD:'+file);
 for(const m of text.matchAll(/\\\\[A-Za-z0-9_-]+(?:\\[A-Za-z0-9_ .-]+)+/g))replacements.set(m[0].trim(),'UPDATE_CHANNEL_DIRECTORY');
 for(const m of text.matchAll(/\b192\.168\.\d{1,3}\.\d{1,3}\b/g))replacements.set(m[0],'camera.example');
 for(const m of text.matchAll(/[A-Z]:\\Codex\\[^`\r\n"<>]+/g))replacements.set(m[0].trim(),'REPOSITORY_DIRECTORY');
}
const out='artifacts/security/history-cleanup';fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(out+'/replacements.txt',[...replacements].sort((a,b)=>b[0].length-a[0].length).map(([a,b])=>'literal:'+a+'==>'+b).join('\n')+'\n');
const identities=new Set(git('log','--all','--format=%an <%ae>%n%cn <%ce>').split('\n').filter(Boolean));
fs.writeFileSync(out+'/mailmap', [...identities].map(old=>'RTSPView contributor <maintainer@example.invalid> '+old).join('\n')+'\n');
fs.writeFileSync(out+'/remove-paths.txt','HANDOFF.md\nartifacts/\npublish/\nstage/\ndist/\n.toolchain/\n');
console.log(`Prepared ${replacements.size} private literal replacements and ${identities.size} identity mappings in ignored audit artifacts. Review before using. No history changed.`);

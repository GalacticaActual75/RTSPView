// Execute the shipped firewall helper with mocked Windows commands; never alter real firewall rules.
const fs=require('fs'),path=require('path'),os=require('os'),cp=require('child_process'),assert=require('assert/strict');
const directory=fs.mkdtempSync(path.join(os.tmpdir(),'RTSPView-FirewallChecks-'));
try {
 const helper=path.resolve(__dirname,'../deployment/Enable-LanAccess.ps1');
 for(const scenario of ['private','public','failure']) {
  const log=path.join(directory,scenario+'.jsonl'),script=path.join(directory,scenario+'.ps1');
  const quote=s=>"'"+s.replaceAll("'","''")+"'";
  fs.writeFileSync(script,`
$ErrorActionPreference='Stop'
function Get-NetConnectionProfile { [pscustomobject]@{NetworkCategory=${quote(scenario==='public'?'Public':'Private')}} }
function Get-NetFirewallRule { param($Name,$DisplayName,$ErrorAction) }
function Remove-NetFirewallRule { param($Name,[Parameter(ValueFromPipeline)]$InputObject) process {} }
function New-NetFirewallRule {
 param($Name,$DisplayName,$Direction,$Action,$Protocol,$LocalPort,$RemoteAddress,$Profile,$Enabled)
 if (${quote(scenario)} -eq 'failure') { throw 'simulated failure' }
 $PSBoundParameters | ConvertTo-Json -Compress | Add-Content -LiteralPath ${quote(log)}
}
& ${quote(helper)}
exit $LASTEXITCODE
`);
  const result=cp.spawnSync('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',script],{encoding:'utf8',windowsHide:true});
  assert.equal(result.status,scenario==='private'?0:scenario==='public'?2:1,scenario);
  if(scenario==='private') {
   const rule=JSON.parse(fs.readFileSync(log,'utf8').trim().replace(/^\uFEFF/,''));
   assert.equal(rule.LocalPort,5080);assert.equal(rule.Profile,'Private');assert.equal(rule.RemoteAddress,'LocalSubnet');assert.equal(rule.Direction,'Inbound');assert.equal(rule.Action,'Allow');assert.equal(rule.Protocol,'TCP');
  } else assert(!fs.existsSync(log),'No allow rule on rejected setup');
 }
 console.log('LAN firewall checks passed: private subnet rule, public-profile rejection and setup failure. No real firewall changes.');
} finally {fs.rmSync(directory,{recursive:true,force:true});}

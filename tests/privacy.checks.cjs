const assert = require('node:assert/strict');
const { inspectText, inspectPath, historicalFixture } = require('../tools/check-privacy.cjs');
const fixtureOid = 'bf3f46b1cb010b075c1103b40681d8c3cf48e50d';
const fixturePath = 'plugins/scrypted-rtspview/test/config.cjs';
const fixtureHit = { category: 'credential-bearing URL', line: 19 };
assert(historicalFixture(fixtureOid, fixturePath, fixtureHit));
assert(!historicalFixture('changed', fixturePath, fixtureHit));
assert(!historicalFixture(fixtureOid, 'another-file.cjs', fixtureHit));
assert(!historicalFixture(fixtureOid, fixturePath, { ...fixtureHit, line: 20 }));
assert(!historicalFixture(fixtureOid, fixturePath, { ...fixtureHit, category: 'provider credential' }));
// Construct canaries so the tests themselves never publish complete risky literals.
const ip = ['192', '168', '12', '34'].join('.');
const secret = 'synthetic-canary';
assert(inspectText(ip).some(x => x.category === 'private network address'));
for (const prefix of ['10.23', '172.16', '172.31']) {
  assert(inspectText([prefix, '2', '3'].join('.')).length);
}
for (const value of ['192.0.2.10', '198.51.100.20', '203.0.113.10', '127.0.0.1', '172.32.2.3']) {
  assert.equal(inspectText(value).length, 0);
}
assert(inspectText(['C:', 'Users', 'private-user', 'settings'].join('\\')).length);
assert(inspectText(['', '', 'private-host', 'share'].join('\\')).length);
assert(inspectText(['-----BEGIN ', 'PRIVATE KEY-----'].join('')).length);
assert(inspectText('gh' + 'p_' + 'A'.repeat(36)).length);
assert(inspectText('rtsp://' + 'user:' + secret + '@device.local/live').length);
assert.equal(inspectText('rtsp://' + 'user:' + secret + '@camera.example/live').length, 0);
assert.equal(inspectText('https://example.test/path').length, 0);
for (const name of ['RTSPView-config.json', 'exports/custom.json', 'screenshots/host.png', 'appsettings.Development.json', 'settings.json', 'private/key.xml', '.env.local']) assert(inspectPath(name).length, name);
for (const name of ['.env.example', 'assets/branding/rtspview-icon.png', 'tests/branding-compatibility.json', 'src/RTSPView.Core/AppSettings.cs']) assert.equal(inspectPath(name).length, 0, name);
const result = JSON.stringify(inspectText(ip + '\nrtsp://' + 'user:' + secret + '@device.local/live'));
assert(!result.includes(ip)); assert(!result.includes(secret));
console.log('PASS: privacy canaries, reserved fixtures, private filenames, and redacted output');

// Exercise the CLI against a deleted file, rather than just testing regex helpers.
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const cp = require('node:child_process');
const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'privacy-check-'));
try {
  const git = (...args) => cp.execFileSync('git', args, { cwd: scratch, stdio: 'pipe' });
  git('init');
  git('config', 'user.name', 'Privacy test');
  git('config', 'user.email', 'test@example.invalid');
  fs.writeFileSync(path.join(scratch, 'removed.txt'), ip);
  git('add', 'removed.txt');
  git('commit', '-m', 'Add synthetic fixture');
  git('rm', 'removed.txt');
  git('commit', '-m', 'Remove synthetic fixture');
  const scanner = path.resolve(__dirname, '../tools/check-privacy.cjs');
  const current = cp.spawnSync(process.execPath, [scanner], { cwd: scratch, encoding: 'utf8' });
  assert.equal(current.status, 0, current.stderr);
  const historical = cp.spawnSync(process.execPath, [scanner, '--history'], { cwd: scratch, encoding: 'utf8' });
  assert.equal(historical.status, 1, historical.stderr);
  assert(JSON.parse(historical.stdout).findings.some(x => x.path === 'removed.txt' && x.category === 'private network address'));
  assert(!historical.stdout.includes(ip));
  console.log('PASS: deleted historical content is detected without printing its value');
} finally {
  fs.rmSync(scratch, { recursive: true, force: true });
}

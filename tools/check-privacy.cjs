// A redacted, deterministic privacy gate. Use Gitleaks alongside this check.
const fs = require('node:fs');
const cp = require('node:child_process');
const path = require('node:path');

function inspectText(text) {
  const findings = [];
  const rules = [
    ['private network address', /(?<![\d.])(?:192\.168\.\d{1,3}\.\d{1,3}|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})(?![\d.])/],
    ['personal filesystem path', /[A-Z]:[\\/](?:Users|Codex)[\\/][^\s<>"']+|\/(?:home|Users|volume1)\/[^\s<>"']+/i],
    ['network share', /\\\\[A-Za-z0-9_-]+\\[A-Za-z0-9_$.-]+/],
    ['private key material', /-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED )?PRIVATE KEY-----/],
    ['provider credential', /\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|[A]KIA[A-Z0-9]{16}|xox[baprs]-[A-Za-z0-9-]{20,})\b/],
  ];
  for (const [index, line] of text.split(/\r?\n/).entries()) {
    for (const [category, expression] of rules) {
      if (expression.test(line)) findings.push({ line: index + 1, category });
    }
    for (const match of line.matchAll(/\b(?:rtsp|rtsps|https?|mqtts?|smb):\/\/[^\s"'<>`]+/gi)) {
      // Userinfo is allowed only on reserved example domains, never on a real host.
      try {
        const url = new URL(match[0]);
        const synthetic = /(?:^|\.)(?:example|test|invalid)$/.test(url.hostname) || /^(?:example\.com|example\.net|example\.org)$/.test(url.hostname);
        if ((url.username || url.password) && !synthetic) findings.push({ line: index + 1, category: 'credential-bearing URL' });
      } catch { /* Code fragments are not necessarily URL literals. Gitleaks also runs. */ }
    }
  }
  return findings;
}

function inspectPath(name) {
  const normalized = name.replaceAll('\\', '/');
  const base = path.posix.basename(normalized);
  if (/(?:^|\/)(?:exports|backups|screenshots|debug-captures|local-certificates|private|data-protection|snapshots|logs|artifacts|publish|stage|dist|\.toolchain|bin|obj)\//i.test(normalized) ||
      /^(?:settings\.json(?:\..*)?|web-security\.json.*|automation\.json.*|initial-admin-password\.txt|(?:RTSPView|SpotMonitor)-config.*\.json|update-channel\.json|appsettings\.(?:Local|Development)\.json|appsettings\..*\.local\.json|launchSettings\.json)$/i.test(base) ||
      (base.startsWith('.env') && base !== '.env.example') ||
      /\.(?:key|pem|pfx|p12|db|sqlite\d*|log|bak|backup|dmp|dump|exe|dll|pdb)$/i.test(base)) {
    return [{ line: 0, category: 'private runtime or generated file' }];
  }
  return [];
}

// Reviewed, published connector test fixtures predating this gate. These are
// synthetic URL parser inputs, not credentials. Pin the blob AND exact finding;
// any edit creates a new blob and must pass the normal scanner.
function historicalFixture(oid, name, hit) {
  return oid === 'bf3f46b1cb010b075c1103b40681d8c3cf48e50d' &&
    name === 'plugins/scrypted-rtspview/test/config.cjs' &&
    hit.category === 'credential-bearing URL' && [19, 24].includes(hit.line);
}

function main(args) {
  if (args.some(arg => arg !== '--history')) throw new Error('Usage: node tools/check-privacy.cjs [--history]');
  const git = (...values) => cp.execFileSync('git', values, { maxBuffer: 128 * 1024 * 1024 });
  let findings = [], scanned = 0;
  if (args.includes('--history')) {
    const objects = git('rev-list', '--objects', '--all').toString().trim().split('\n').filter(Boolean);
    // Read in bounded batches, including deleted/renamed versions and merge results.
    for (let start = 0; start < objects.length; start += 25) {
      const group = objects.slice(start, start + 25);
      const result = cp.spawnSync('git', ['cat-file', '--batch'], {
        input: group.map(entry => entry.split(' ')[0]).join('\n') + '\n', maxBuffer: 256 * 1024 * 1024
      });
      if (result.status !== 0) throw new Error('Cannot read Git history');
      let offset = 0;
      for (const entry of group) {
        const end = result.stdout.indexOf(10, offset);
        const [oid, type, length] = result.stdout.subarray(offset, end).toString().split(' ');
        const data = result.stdout.subarray(end + 1, end + 1 + Number(length));
        offset = end + Number(length) + 2;
        if (!['blob', 'commit', 'tag'].includes(type)) continue;
        const name = entry.slice(41) || `[${type}]`;
        const hits = type === 'blob' ? inspectPath(name) : [];
        // Binary assets require visual review; filenames are still checked.
        if (!data.includes(0)) hits.push(...inspectText(data.toString('utf8')));
        findings.push(...hits.filter(hit => !historicalFixture(oid, name, hit)).map(hit => ({ object: oid, path: name, ...hit })));
        scanned++;
      }
    }
  } else {
    for (const name of git('ls-files', '-z').toString().split('\0').filter(Boolean)) {
      if (!fs.existsSync(name) || !fs.statSync(name).isFile()) continue;
      const data = fs.readFileSync(name), hits = inspectPath(name);
      if (!data.includes(0)) hits.push(...inspectText(data.toString('utf8')));
      findings.push(...hits.map(hit => ({ path: name, ...hit })));
      scanned++;
    }
  }
  // Never print matching text, URLs, identities, or secret values in CI output.
  console.log(JSON.stringify({ scanned, findings }, null, 2));
  if (findings.length) process.exitCode = 1;
}

module.exports = { inspectText, inspectPath, historicalFixture };
if (require.main === module) main(process.argv.slice(2));

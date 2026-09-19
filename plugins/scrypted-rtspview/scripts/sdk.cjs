// The SDK command shim relies on argv[1]'s basename, which npm's Windows .cmd
// wrapper replaces with cli.js. Invoke the same SDK entrypoint directly.
const path = require('node:path');
const childProcess = require('node:child_process');
const command = process.argv[2];
if (!['scrypted-webpack', 'scrypted-deploy'].includes(command)) throw new Error('Unsupported SDK command');
const entry = path.join(path.dirname(require.resolve('@scrypted/sdk')), 'bin', command + '.js');
process.env.NODE_ENV = 'production';
// The SDK spawns rollup.cmd directly; modern Node on Windows requires the JS
// entrypoint or a shell. Use Node directly and avoid shell quoting entirely.
const spawn = childProcess.spawn;
childProcess.spawn = function (file, args, options) {
  if (process.platform === 'win32' && path.basename(file) === 'rollup.cmd')
    return spawn(process.execPath, [path.join(path.dirname(require.resolve('rollup')), 'bin/rollup'), ...args], {...options, windowsHide:true});
  return spawn(file, args, options);
};
process.argv = [process.argv[0], entry, ...process.argv.slice(3)];
require(entry);

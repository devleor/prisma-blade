// Full project check: build, simulation unit tests (dotnet test), headless Godot checks.
// Godot comes from the GODOT environment variable (the *_console.exe); the default is 4.7.1 .NET in Downloads.
// Pass --gpu to also render test-results/snapshot.png (needs a window and a graphics card).
import {spawnSync} from 'node:child_process';
import {existsSync} from 'node:fs';
import {homedir} from 'node:os';
import {join, dirname} from 'node:path';
import {fileURLToPath} from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const build = 'Godot_v4.7.1-stable_mono_win64';
const godot = process.env.GODOT ?? join(homedir(), 'Downloads', build, build, `${build}_console.exe`);
const run = (cmd, args, {timeoutMin = 10, mustPrint} = {}) => {
  console.log(`\n> ${cmd} ${args.join(' ')}`);
  const r = spawnSync(cmd, args, {cwd: root, encoding: 'utf8', timeout: timeoutMin * 60_000, maxBuffer: 64 << 20});
  process.stdout.write(r.stdout ?? ''); process.stderr.write(r.stderr ?? '');
  if (r.error || r.signal) { console.error(`failed: ${r.error?.message ?? 'signal ' + r.signal}`); process.exit(1); }
  if (r.status !== 0) { console.error(`failed (exit code ${r.status})`); process.exit(r.status || 1); }
  if (mustPrint && !(r.stdout ?? '').includes(mustPrint)) { console.error(`failed: output lacks "${mustPrint}"`); process.exit(1); }
};
// build the game first: headless Godot loads the game DLL, and a stale one would hide new checks
run('dotnet', ['build', 'PrismaBlade.csproj', '-warnaserror']);
run('dotnet', ['test', 'PrismaBlade.sln', '--logger', 'junit;LogFilePath=TestResults/junit.xml']);
if (!existsSync(godot)) { console.error('Godot not found: set GODOT to the path of the *_console.exe'); process.exit(1); }
run(godot, ['--headless', '--path', '.', '--import'], {timeoutMin: 10});
run(godot, ['--headless', '--path', '.', 'res://scenes/tests/GameCheck.tscn'], {timeoutMin: 5, mustPrint: 'PASSED'});
if (process.argv.includes('--gpu')) run(godot, ['--path', '.', '--resolution', '1280x720', 'res://scenes/tests/Snapshot.tscn'], {timeoutMin: 3, mustPrint: 'saved'});
console.log('\nverification: ok');

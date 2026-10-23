// No browser, WebView, profile or hardware access. Uses existing local TypeScript only.
const fs = require('fs'), path = require('path'), vm = require('vm'), assert = require('assert/strict');
const ts = require('../ui/node_modules/typescript');
const file = path.join(__dirname, '../ui/src/bridge/links.ts');
const result = ts.transpileModule(fs.readFileSync(file, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  reportDiagnostics: true,
});
assert.equal(result.diagnostics.length, 0);
const sandbox = { exports: {} };
vm.runInNewContext(result.outputText, sandbox, { timeout: 1000 });
const { mergeLinks } = sandbox.exports;
const initial = { codex: true, claude: true, dsh: true, signal: true, dotMatrix: true,
  idleOffsetMs: 250, idleRatePercent: 99.9, linkState: 'working', receivedHookEvents: 2,
  lastHookEvent: 'dsh/UserPromptSubmit', sessions: [], statusText: 'LCD', signalText: 'RGB', dotMatrixText: 'Dot' };
const normalize = value => JSON.parse(JSON.stringify(value));
for (const key of ['codex', 'claude', 'dsh', 'signal', 'dotMatrix']) {
  const off = mergeLinks(initial, { [key]: false });
  assert.equal(off[key], false, key + ' explicit false is acknowledged');
  assert.equal(initial[key], true, 'input is not mutated');
  const on = mergeLinks(off, { [key]: true });
  assert.deepEqual(normalize(on), initial, key + ' can re-enable');
}
assert.deepEqual(normalize(mergeLinks(initial, { dsh: undefined })), initial);
const patched = mergeLinks(initial, { codex: false, dsh: false, idleOffsetMs: 0, idleRatePercent: 100 });
assert.equal(patched.dsh, false);
assert.equal(patched.codex, false);
assert.equal(patched.claude, true);
assert.equal(patched.idleOffsetMs, 0);
assert.equal(patched.idleRatePercent, 100);
for (const key of ['sessions', 'linkState', 'lastHookEvent', 'statusText', 'signalText', 'dotMatrixText'])
  assert.equal(patched[key], initial[key], 'status retained: ' + key);
assert.equal(mergeLinks(patched, { signal: false }).dsh, false, 'unrelated queued write does not resurrect DSH');
// Confirm the tested helper is actually wired into the page's successful-write path.
const page = fs.readFileSync(path.join(__dirname, '../ui/src/pages/LinksPage.vue'), 'utf8');
assert(page.includes('info.value = mergeLinks(info.value, args)'));
assert(page.indexOf("await bridge.call('setLinks', args)") < page.indexOf('mergeLocal(args)'));
console.log('PASS UI partial merge: all toggles false/true, undefined, zero, queued DSH off and retained status.');

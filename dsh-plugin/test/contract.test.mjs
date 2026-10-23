import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readAsar, defaultArchive } from './asar.mjs';
import { apply } from '../lib/index.js';

// Execute the *installed* Cordis waterfall method, not the old fake emit helper.
const archive = process.env.DSH_CONTRACT_ASAR || defaultArchive;
const cordis = readAsar(archive, 'dsh/node_modules/@deepseek-ai/cordis/lib/index.js');
const method = cordis.match(/waterfall\(\.\.\.args\) \{[\s\S]*?\n\t\}/)?.[0];
assert.ok(method, 'installed Cordis waterfall method must be audited');
const waterfall = Function('return ({' + method + '}).waterfall')();
const tools = readAsar(archive, 'dsh/node_modules/@deepseek-ai/dsh-tools/lib/index.js');
assert.match(tools, /const gate = await this\.ctx\.waterfall\(carrier, "tools\/pre-execute", exec, \(\) => Promise\.resolve\(\{ kind: "allow" \}\)\);/);
assert.match(tools, /gate\.kind === "ask"/);

function context() {
 const listeners = new Map(), cleanup = [];
 return {
  listeners, agents: { list: () => [] },
  on(name, fn) { listeners.set(name, fn); return () => listeners.delete(name); },
  effect(fn) { cleanup.push(fn()); },
  close() { for (const fn of cleanup) fn(); },
  dispatch(_kind, args) { args.shift(); const name = args.shift(); return listeners.has(name) ? [listeners.get(name)] : []; },
  waterfall(...args) { return waterfall.apply(this, args); },
 };
}

test('real installed waterfall: terminal next executes and gate identity is unchanged', async () => {
 const ctx = context(); apply(ctx, { pipeName: 'gpulcd-contract-nonexistent-' + process.pid });
 try {
  for (const kind of ['allow','deny','ask','cancel']) {
   const decision = Object.freeze({kind, reason: 'sentinel'}); let nextCalls = 0;
   const gate = await ctx.waterfall({}, 'tools/pre-execute', {}, () => { nextCalls++; return Promise.resolve(decision); });
   // Same dereference as dsh-tools; the old plugin throws the exact TypeError here.
   assert.equal(gate.kind, kind);
   assert.equal(nextCalls, 1);
   assert.strictEqual(gate, decision);
   const promised = Promise.resolve(decision);
   assert.strictEqual(ctx.waterfall({}, 'tools/pre-execute', {}, () => promised), promised);
  }
 } finally { ctx.close(); }
});

test('real installed waterfall: downstream sync throws and rejected errors pass through unchanged', async () => {
 const ctx = context(); apply(ctx, { pipeName: 'gpulcd-contract-nonexistent-' + process.pid });
 try {
  for (const name of ['tools/pre-execute','approval/request','user-questions/request']) {
   const error = new Error(name);
   assert.throws(() => ctx.waterfall({}, name, {}, () => { throw error; }), e => e === error);
   await assert.rejects(async () => ctx.waterfall({}, name, {}, () => Promise.reject(error)), e => e === error);
   const value = Object.freeze({ result: name });
   assert.strictEqual(await ctx.waterfall({}, name, {}, () => Promise.resolve(value)), value);
  }
 } finally { ctx.close(); }
});

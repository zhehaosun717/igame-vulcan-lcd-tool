import { test } from 'node:test';
import assert from 'node:assert/strict';
import { getEventListeners } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { attach, createTurnTracker, sessionIdOf, inject } from '../lib/index.js';
const agent = (id = 's', status = 'running') => ({id, status, session:{id}});
const deferred = () => Promise.withResolvers();
function fixture(t, agents = [], send) {
 const listeners = new Map(), events = [], effects = [];
 const ctx = { agents:{list:()=>agents}, on:(name,fn)=>{listeners.set(name,fn);return ()=>listeners.delete(name)}, effect:fn=>effects.push(fn()) };
 let bridgeClosed = false;
 const bridge = {send:send ? (value=>send(typeof value==='function'?value():value)) : (value=>{const event=typeof value==='function'?value():value;if(!event)return Promise.resolve(false);events.push(event);return Promise.resolve(true)}), dispose:()=>{bridgeClosed=true}};
 const close = attach(ctx,bridge,{heartbeatMs:20}); t.after(close);
 const emit = (name,...args) => listeners.get(name)?.(...args);
 const status = (a,value) => { a.status=value;emit('agent/status',{agent:a,status:value}); };
 const event = (a,type,turn=1,kind='completed') => emit('session/event',a.session,{type,data:{turn,reason:{kind}}});
 return {events,listeners,emit,status,event,close,bridgeClosed:()=>bridgeClosed,latest:()=>events.at(-1)?.hook_event_name};
}

test('Cordis injection declares the core agents service as a supported array',()=>{
 assert.ok(Array.isArray(inject), 'Cordis 4.0.5 treats object keys as service names; optional is not supported');
 assert.deepEqual(inject,['agents']);
});

test('tracker reads never create; epoch is unique across reloads',()=>{
 const a=createTurnTracker(),b=createTurnTracker();assert.equal(a.current('s'),undefined);
 const id=a.start('s');assert.notEqual(id,b.start('s'));assert.equal(a.current('s'),id);a.end('s');assert.equal(a.current('s'),undefined);
 assert.equal(sessionIdOf({id:'x'}),'x');assert.equal(sessionIdOf({session:{id:'y'}}),'y');assert.equal(sessionIdOf(null),undefined);
});
test('initialize only already-running agents; idle never falsely completes',t=>{
 const busy=agent('busy'),idle=agent('idle','idle');const f=fixture(t,[busy,idle]);
 assert.deepEqual(f.events.map(e=>e.session_id),['busy']);f.status(idle,'idle');assert.equal(f.events.length,1);
 f.status(busy,'idle');assert.equal(f.latest(),'SessionEnd');assert.equal(f.events.some(e=>e.hook_event_name==='Stop'),false);
});
test('only completed stepped turns report Stop; abort/error/blocked/empty do not',t=>{
 const f=fixture(t);const a=agent();
 for(const kind of ['completed','aborted','error','blocked']) {
  f.status(a,'running');f.event(a,'turn/start');f.event(a,'step/start');f.event(a,'turn/end',1,kind);
  assert.equal(f.latest(),kind==='completed'?'Stop':'SessionEnd');f.status(a,'idle');
 }
 f.status(a,'running');f.event(a,'turn/start');f.event(a,'turn/end');assert.equal(f.latest(),'SessionEnd');
});
test('successive actual turns within one running phase receive distinct IDs',t=>{
 const a=agent();const f=fixture(t,[a]);f.event(a,'turn/start');f.event(a,'step/start');f.event(a,'turn/end');
 f.event(a,'turn/start',2);f.event(a,'step/start',2);f.event(a,'turn/end',2);
 assert.equal(f.events.filter(e=>e.hook_event_name==='Stop').length,2);
 assert.equal(new Set(f.events.filter(e=>e.hook_event_name==='UserPromptSubmit').map(e=>e.turn_id)).size,2);
});
test('concurrent approvals/questions and tool/status/heartbeat preserve waiting',async t=>{
 const a=agent(),b=agent('b');const f=fixture(t,[a,b]);const x=deferred(),y=deferred();
 const px=f.emit('approval/request',{agent:a},()=>x.promise),py=f.emit('user-questions/request',{agent:a},()=>y.promise);
 const decision={kind:'deny'};assert.strictEqual(f.emit('tools/pre-execute',{agent:a},()=>decision),decision);assert.equal(f.latest(),'PermissionRequest');
 f.status(a,'running');assert.equal(f.latest(),'PermissionRequest');
 f.emit('tools/pre-execute',{agent:b},()=>decision);assert.equal(f.latest(),'PreToolUse');
 x.resolve('approved');await px;assert.equal(f.events.filter(e=>e.session_id==='s').at(-1).hook_event_name,'PermissionRequest');
 await delay(30);assert.equal(f.events.filter(e=>e.session_id==='s').at(-1).hook_event_name,'PermissionRequest');
 const answer={answers:[]};y.resolve(answer);assert.strictEqual(await py,answer);assert.equal(f.latest(),'PreToolUse');
});
test('old finally after idle/new turn cannot modify newer waits or resurrect',async t=>{
 const a=agent();const f=fixture(t,[a]);const old=deferred(),fresh=deferred();
 const p=f.emit('approval/request',{agent:a},()=>old.promise);f.status(a,'idle');f.status(a,'running');
 const q=f.emit('user-questions/request',{agent:a},()=>fresh.promise);const count=f.events.length;
 old.resolve('approved');await p;assert.equal(f.events.length,count);assert.equal(f.latest(),'PermissionRequest');
 fresh.resolve({answers:[]});await q;f.status(a,'idle');const end=f.events.length;
 f.emit('tools/pre-execute',{agent:a},()=>({kind:'allow'}));assert.equal(f.events.length,end);
});
test('disposed payload is {agent}; late callbacks cannot revive it; reused id isolated',async t=>{
 const a=agent();const f=fixture(t,[a]);const d=deferred();const p=f.emit('approval/request',{agent:a},()=>d.promise);
 f.emit('agent/disposed',{agent:a});assert.equal(f.latest(),'SessionEnd');const end=f.events.length;
 d.resolve('approved');await p;f.status(a,'running');f.emit('tools/pre-execute',{agent:a},()=>({kind:'allow'}));assert.equal(f.events.length,end);
 const replacement=agent();f.status(replacement,'running');f.event(a,'turn/end');assert.equal(f.latest(),'PreToolUse');
 f.emit('agent/disposed',{agent:a});assert.equal(f.latest(),'PreToolUse');
 f.event(replacement,'turn/start');f.event(replacement,'step/start');f.event(replacement,'turn/end');
 const completed=f.events.length;f.emit('agent/disposed',{agent:a});assert.equal(f.events.length,completed);assert.equal(f.latest(),'Stop');
});
test('abort releases only its wait and detaches listeners even before next settles',async t=>{
 const a=agent();const f=fixture(t,[a]);const abort=new AbortController(),x=deferred(),y=deferred();
 const p=f.emit('approval/request',{agent:a,signal:abort.signal},()=>x.promise);
 const q=f.emit('user-questions/request',{agent:a},()=>y.promise);
 assert.equal(getEventListeners(abort.signal,'abort').length,1);abort.abort();assert.equal(getEventListeners(abort.signal,'abort').length,0);
 assert.equal(f.latest(),'PermissionRequest');y.resolve({});await q;assert.equal(f.latest(),'PreToolUse');const count=f.events.length;
 x.resolve('cancelled');await p;assert.equal(f.events.length,count);
});
test('idle/dispose/plugin cleanup remove abort listeners and stale finally stays inert',async t=>{
 for(const end of ['idle','disposed','plugin']) {
  const a=agent();const f=fixture(t,[a]);const d=deferred(),abort=new AbortController();
  const p=f.emit('approval/request',{agent:a,signal:abort.signal},()=>d.promise);
  if(end==='idle')f.status(a,'idle');else if(end==='disposed')f.emit('agent/disposed',{agent:a});else f.close();
  assert.equal(getEventListeners(abort.signal,'abort').length,0);const count=f.events.length;d.resolve('cancelled');await p;assert.equal(f.events.length,count);
  f.close();assert.equal(f.listeners.size,0);assert.equal(f.bridgeClosed(),true);
 }
});
test('sync throws and rejected telemetry cannot alter next values/errors or leak rejections',async t=>{
 for(const send of [()=>{throw Error('telemetry')},()=>Promise.reject(Error('telemetry')),()=>new Promise(()=>{})]) {
  const a=agent();const f=fixture(t,[a],send);const value=Object.freeze({kind:'cancel'}),error=new Error('downstream');
  assert.strictEqual(f.emit('tools/pre-execute',{agent:a},()=>value),value);
  assert.throws(()=>f.emit('tools/pre-execute',{agent:a},()=>{throw error}),e=>e===error);
  for(const name of ['approval/request','user-questions/request']) {
   assert.strictEqual(await f.emit(name,{agent:a},()=>value),value);
   assert.throws(()=>f.emit(name,{agent:a},()=>{throw error}),e=>e===error);
   await assert.rejects(f.emit(name,{agent:a},()=>Promise.reject(error)),e=>e===error);
  }
 }
 await delay(0);
});
test('heartbeat re-advertises current state after failure, then stops on cleanup',async t=>{
 const a=agent(),received=[];let online=false;
 const f=fixture(t,[a],e=>{if(online)received.push(e);return Promise.resolve(online)});
 const d=deferred();const p=f.emit('approval/request',{agent:a},()=>d.promise);online=true;await delay(35);
 assert.ok(received.length);assert.ok(received.every(e=>e.hook_event_name==='PermissionRequest'||(e.hook_event_name==='UserPromptSubmit'&&e.state==='waiting')));
 assert.equal(received.filter(e=>e.hook_event_name==='UserPromptSubmit').length,1,'one ACKed atomic waiting recovery, not periodic starts');
 f.close();const count=received.length;await delay(35);assert.equal(received.length,count);d.resolve('cancelled');await p;
});

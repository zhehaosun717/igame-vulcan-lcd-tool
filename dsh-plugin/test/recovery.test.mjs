import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {setTimeout as delay} from 'node:timers/promises';
import {attach} from '../lib/index.js';

import {Receiver} from './receiver-model.mjs';
function fixture(t,receiver,{loss='failure',waiting=false}={}){
 const listeners=new Map();const a={id:'s',status:'running',session:{id:'s'}};let startLost=false;let sends=0;
 const bridge={send(source){const e=typeof source==='function'?source():source;if(!e)return Promise.resolve(false);sends++;if(e.hook_event_name==='UserPromptSubmit'&&!startLost){startLost=true;return Promise.resolve(loss==='ack-without-apply');}receiver.apply(e);return Promise.resolve(true)},dispose(){}};
 const ctx={on:(n,fn)=>{listeners.set(n,fn);return()=>listeners.delete(n)},effect:()=>{},agents:{list:()=>[a]}};
 const close=attach(ctx,bridge,{heartbeatMs:10});t.after(close);
 const pending=Promise.withResolvers();const request=waiting?listeners.get('approval/request')({agent:a},()=>pending.promise):null;
 t.after(()=>pending.resolve('cancelled'));
 return {a,listeners,close,request,pending,sends:()=>sends};
}
for(const old of ['closed','working'])for(const waiting of [false,true])for(const loss of ['failure','ttl','overflow']){
 test('recover '+loss+' start over old '+old+' to '+(waiting?'waiting':'working'),async t=>{
  const r=new Receiver();r.entries.set('dsh:s',{Turn:'old',State:old==='closed'?'done':'working',Closed:old==='closed'});
  const f=fixture(t,r,{loss,waiting});await delay(85);
  const p=r.entries.get('dsh:s');assert.notEqual(p?.Turn,'old');assert.equal(p?.State,waiting?'waiting':'working');
  if(waiting)assert.equal(r.history.some(p=>p.State==='working'),false,'resync must not replay working start over waiting');
  assert.equal(r.projections.some(p=>p.state==='idle'),false,'no reset-induced idle projection');
  const recovered=r.projections.findIndex(p=>p.event.hook_event_name==='UserPromptSubmit'&&p.event.turn_id!=='old');
  assert.ok(recovered>=0);const tail=r.projections.slice(recovered);assert.ok(tail.every(p=>p.state===(waiting?'waiting':'working')));
  assert.equal(new Set(tail.map(p=>p.hold)).size,1,'healthy heartbeat preserves client hold');
  assert.ok(f.sends()<24,'bounded cadence, no retry loop');
 });
}
test('receiver restart and disabled-ACK then re-enable recover a waiting snapshot',async t=>{
 const r=new Receiver();r.entries.set('dsh:s',{Turn:'old',State:'done',Closed:true});r.enabled=false;r.entries.clear(); // production toggle ClearClient
 const f=fixture(t,r,{loss:'ack-without-apply',waiting:true});await delay(25);r.enabled=true;await delay(70);
 assert.equal(r.entries.get('dsh:s')?.State,'waiting');r.entries.clear();await delay(70);assert.equal(r.entries.get('dsh:s')?.State,'waiting');
 assert.equal(r.history.some(p=>p.State==='working'),false);
});

test('ACKed start never repeats on healthy heartbeats',async t=>{
 const r=new Receiver();let starts=0;const listeners=new Map();const a={id:'s',status:'running',session:{id:'s'}};
 const ctx={on:(n,fn)=>{listeners.set(n,fn);return()=>listeners.delete(n)},effect:()=>{},agents:{list:()=>[a]}};
 const close=attach(ctx,{send(source){const e=typeof source==='function'?source():source;if(!e)return Promise.resolve(false);if(e.hook_event_name==='UserPromptSubmit')starts++;r.apply(e);return Promise.resolve(true)},dispose(){}},{heartbeatMs:10});t.after(close);
 await delay(85);assert.equal(starts,1);assert.equal(r.projections.some(f=>f.state==='idle'),false);assert.equal(new Set(r.projections.map(f=>f.hold)).size,1);
});
test('one pending start per generation; stale supplier and old ACK cannot revive or confirm newer turn',async t=>{
 const a={id:'s',status:'running',session:{id:'s'}},listeners=new Map(),pending=[];
 const ctx={on:(n,fn)=>{listeners.set(n,fn);return()=>listeners.delete(n)},effect:()=>{},agents:{list:()=>[a]}};
 const close=attach(ctx,{send(source){if(typeof source!=='function'||source()?.hook_event_name!=='UserPromptSubmit')return Promise.resolve(true);const d=Promise.withResolvers();pending.push({source,...d});return d.promise},dispose(){}},{heartbeatMs:10});t.after(close);
 await delay(35);assert.equal(pending.length,1,'no overlapping retries while unresolved');
 listeners.get('agent/status')({agent:a,status:'idle'});listeners.get('agent/status')({agent:a,status:'running'});
 assert.equal(pending[0].source(),undefined);assert.equal(pending.length,2);pending[0].resolve(true);pending[1].resolve(false);await delay(25);
 assert.equal(pending.length,3,'old ACK did not mark new generation confirmed');close();pending[2].resolve(false);await delay(25);assert.equal(pending.length,3);
});

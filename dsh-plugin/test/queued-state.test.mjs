import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createServer,connect,Socket} from 'node:net';
import {randomUUID} from 'node:crypto';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createBridge} from '../lib/bridge.js';
import {attach} from '../lib/index.js';
import {projectWithRealReceiver} from './real-receiver.mjs';
async function fixture(t){
 const name='gpulcd-isolated-late-'+randomUUID(),path=process.platform==='win32'?'\\\\.\\pipe\\'+name:join(tmpdir(),name+'.sock');
 const events=[],sockets=new Set(),drained=Promise.withResolvers();let held,target,first=true,b;
 const server=createServer(s=>{sockets.add(s);s.once('close',()=>sockets.delete(s));s.on('error',()=>{});let text='';s.on('data',data=>{text+=data;if(!text.includes('\n'))return;events.push(JSON.parse(text.slice(0,text.indexOf('\n'))));s.write('OK\n');});});
 await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(path,resolve)});
 t.after(async()=>{for(const s of sockets)s.destroy();await new Promise(resolve=>server.close(resolve))});
 b=createBridge({pipeName:process.platform==='win32'?name:path,connectTimeoutMs:1000,ackTimeoutMs:1000,connect(path){if(first){first=false;target=path;held=new Socket();return held}return connect(path)},onSent(){if(b.stats.pending===0&&b.stats.active===0)drained.resolve()}});t.after(()=>b.dispose());
 const a={id:'a',status:'running',session:{id:'a'}},listeners=new Map(),enqueued=[];
 const wrapped={send(source){const e=typeof source==='function'?source():source;if(e)enqueued.push(e);return b.send(source)},dispose:()=>b.dispose()};
 const ctx={on:(n,fn)=>{listeners.set(n,fn);return()=>listeners.delete(n)},effect:()=>{},agents:{list:()=>[a]}};
 const close=attach(ctx,wrapped,{heartbeatMs:1000});t.after(close);
 return {a,events,enqueued,listeners,b,emit:(n,...args)=>listeners.get(n)?.(...args),async release(){held.connect(target);await drained.promise;assert.equal(b.stats.dropped,0);},close};
}
test('late first connect: earlier working refresh cannot lower a newly pending wait',async t=>{
 const f=await fixture(t),pending=Promise.withResolvers();const deny=Object.freeze({kind:'deny'});
 assert.strictEqual(f.emit('tools/pre-execute',{agent:f.a},()=>deny),deny);
 const result=f.emit('approval/request',{agent:f.a},()=>pending.promise);
 await f.release();const {frames,sha256}=projectWithRealReceiver(f.events);console.log('late work->wait '+frames.map(e=>e.state).join(' -> ')+' sha256='+sha256);
 assert.ok(frames.every(e=>e.state==='waiting'),'no working after atomic waiting start');
 pending.resolve('approved');await result;
});
test('late first connect: earlier waiting refresh cannot reenter waiting after answer',async t=>{
 const f=await fixture(t),pending=Promise.withResolvers();const result=f.emit('user-questions/request',{agent:f.a},()=>pending.promise);
 pending.resolve({answers:[]});await result;await f.release();const {frames}=projectWithRealReceiver(f.events);console.log('late wait->work '+frames.map(e=>e.state).join(' -> '));
 assert.ok(frames.every(e=>e.state==='working'),'answered wait must not reappear at delivery');
});
test('queued active snapshots cannot revive an ended generation; final cleanup remains ordered',async t=>{
 const f=await fixture(t);f.emit('tools/pre-execute',{agent:f.a},()=>({kind:'allow'}));f.emit('agent/status',{agent:f.a,status:'idle'});
 await f.release();assert.deepEqual(f.events.map(e=>e.hook_event_name),['SessionEnd']);
 const {frames}=projectWithRealReceiver(f.events);assert.equal(frames.at(-1).state,'idle');assert.equal(frames.at(-1).snapshot.length,0);
});
test('queued replaced generation drops old refreshes and preserves final-before-new-start order',async t=>{
 const f=await fixture(t);const old=f.enqueued[0].turn_id;f.emit('tools/pre-execute',{agent:f.a},()=>({kind:'allow'}));
 f.emit('agent/status',{agent:f.a,status:'idle'});f.emit('agent/status',{agent:f.a,status:'running'});await f.release();
 assert.ok(f.events.filter(e=>e.turn_id===old).every(e=>e.hook_event_name==='SessionEnd'));
 const end=f.events.findIndex(e=>e.hook_event_name==='SessionEnd'),start=f.events.findIndex(e=>e.hook_event_name==='UserPromptSubmit');assert.ok(end>=0&&start>end);
 const {frames}=projectWithRealReceiver(f.events);assert.equal(frames.at(-1).state,'working');assert.notEqual(frames.at(-1).snapshot[0].turn,old);
});
test('queued snapshots read each sessions own waits without cross-session contamination',async t=>{
 const f=await fixture(t),pending=Promise.withResolvers();f.emit('tools/pre-execute',{agent:f.a},()=>({kind:'allow'}));
 const result=f.emit('approval/request',{agent:f.a},()=>pending.promise);const other={id:'b',status:'running',session:{id:'b'}};
 f.emit('agent/status',{agent:other,status:'running'});f.emit('tools/pre-execute',{agent:other},()=>({kind:'cancel'}));await f.release();
 const {frames}=projectWithRealReceiver(f.events);assert.ok(frames.every(e=>e.state==='waiting'));
 assert.equal(frames.at(-1).snapshot.find(e=>e.session==='dsh:a').state,'waiting');assert.equal(frames.at(-1).snapshot.find(e=>e.session==='dsh:b').state,'working');
 pending.resolve('approved');await result;
});

test('completed final Stop remains FIFO and closed while queued active snapshots are discarded',async t=>{
 const f=await fixture(t);f.emit('tools/pre-execute',{agent:f.a},()=>({kind:'allow'}));
 f.emit('session/event',f.a.session,{type:'turn/start',data:{turn:1}});f.emit('session/event',f.a.session,{type:'step/start',data:{turn:1}});
 f.emit('session/event',f.a.session,{type:'turn/end',data:{turn:1,reason:{kind:'completed'}}});f.emit('agent/status',{agent:f.a,status:'idle'});
 await f.release();assert.deepEqual(f.events.map(e=>e.hook_event_name),['Stop']);
 const {frames}=projectWithRealReceiver(f.events);assert.equal(frames.at(-1).state,'done');assert.equal(frames.at(-1).snapshot[0].closed,true);
});

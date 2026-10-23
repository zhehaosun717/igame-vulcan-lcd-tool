import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:net';
import {randomUUID} from 'node:crypto';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import {createBridge} from '../lib/bridge.js';
import {attach} from '../lib/index.js';
import {Receiver} from './receiver-model.mjs';
import {projectWithRealReceiver} from './real-receiver.mjs';
async function pipe(t,receiver){
 const name='gpulcd-isolated-recovery-'+randomUUID(),path=process.platform==='win32'?'\\\\.\\pipe\\'+name:join(tmpdir(),name+'.sock');
 const sockets=new Set(),events=[];const blocked=Promise.withResolvers();let release;
 const server=createServer(s=>{sockets.add(s);s.once('close',()=>sockets.delete(s));s.on('error',()=>{});let buf='';s.on('data',d=>{buf+=d;if(!buf.includes('\n'))return;const e=JSON.parse(buf.slice(0,buf.indexOf('\n')));events.push(e);receiver.apply(e);if(e.session_id==='block'){release=()=>s.write('OK\n');blocked.resolve();}else s.write('OK\n');});});
 await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(path,resolve)});
 t.after(async()=>{for(const s of sockets)s.destroy();await new Promise(r=>server.close(r))});
 return {name:process.platform==='win32'?name:path,events,blocked:blocked.promise,release:()=>release()};
}
for(const oldClosed of [true,false])for(const mode of ['overflow','ttl'])for(const waiting of [false,true])test('real transport '+mode+' over old '+(oldClosed?'closed':'working')+' then recovers '+(waiting?'waiting':'working'),{timeout:4000},async t=>{
 const receiver=new Receiver(),p=await pipe(t,receiver);const drained=Promise.withResolvers();let b;
 b=createBridge({pipeName:p.name,maxQueueAgeMs:mode==='ttl'?30:5000,ackTimeoutMs:1000,onSent:()=>{if(mode==='overflow'&&b.stats.sent===(oldClosed?67:66))drained.resolve()}});t.after(()=>b.dispose());
 const input=(name,id='s',turn='old')=>({hook_event_name:name,session_id:id,turn_id:turn});
 assert.equal(await b.send(input('UserPromptSubmit')),true);if(oldClosed)assert.equal(await b.send(input('Stop')),true);
 const blocked=b.send(input('PreToolUse','block','block'));await p.blocked;
 const a={id:'s',status:'running',session:{id:'s'}},listeners=new Map();
 const ctx={on:(n,fn)=>{listeners.set(n,fn);return()=>listeners.delete(n)},effect:()=>{},agents:{list:()=>[a]}};
 const close=attach(ctx,b,{heartbeatMs:250});t.after(close);
 if(mode==='overflow'){
  const deny=Object.freeze({kind:'deny'});for(let i=0;i<70;i++)assert.strictEqual(listeners.get('tools/pre-execute')({agent:a},()=>deny),deny);
  assert.equal(b.stats.pending,64);assert.equal(b.stats.dropped,7,'start plus six oldest pre events were dropped');
 }else await delay(50);
 p.release();await blocked;if(mode==='overflow')await drained.promise;else await delay(10);
 const observed=p.events.filter(e=>e.session_id==='s'&&e.turn_id!=='old');assert.equal(observed.some(e=>e.hook_event_name==='UserPromptSubmit'),false);
 assert.equal(receiver.entries.get('dsh:s').Turn,'old','receiver still gated before controlled resync');
 const marker=receiver.history.length,pending=Promise.withResolvers();let result;
 if(waiting)result=listeners.get('approval/request')({agent:a},()=>pending.promise);
 await delay(320);const state=receiver.entries.get('dsh:s');assert.notEqual(state?.Turn,'old');assert.equal(state?.State,waiting?'waiting':'working');
 if(waiting)assert.equal(receiver.history.slice(marker).some(e=>e.State==='working'),false);
 assert.ok(b.stats.pending<=64);close();pending.resolve('cancelled');if(result)await result;
 const {frames,sha256,exitCode}=projectWithRealReceiver([...p.events,{client:'claude',hook_event_name:waiting?'PermissionRequest':'PreToolUse',session_id:'competing',turn_id:'competing'}]);assert.equal(exitCode,0);
 assert.equal(frames.some(f=>f.state==='idle'),false,'every actual receiver Apply/Current must avoid artificial idle');
 const recovered=p.events.findIndex(e=>e.session_id==='s'&&e.turn_id!=='old'&&e.hook_event_name==='UserPromptSubmit');
 assert.ok(recovered>=0);assert.ok(frames.slice(recovered).every(f=>f.state===(waiting?'waiting':'working')),'atomic recovery has no false intermediate Current');
 assert.ok(frames.slice(recovered).every(f=>f.client==='dsh'),'atomic recovery preserves client hold against a newer same-priority client');
 assert.equal(new Set(frames.slice(recovered).map(f=>f.hold)).size,1,'no hidden reset of actual private clientSince');
 console.log('real LinkState.cs sha256='+sha256+' frames='+frames.length+' exit='+exitCode);
});

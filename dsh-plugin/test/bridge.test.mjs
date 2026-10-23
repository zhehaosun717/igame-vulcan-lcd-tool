import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:net';
import { EventEmitter } from 'node:events';
import { randomUUID } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { createBridge } from '../lib/bridge.js';
const event = n => ({hook_event_name:'PreToolUse',session_id:'test',turn_id:String(n)});
async function panel(t,reply=socket=>socket.write('OK\n')) {
 const name='gpulcd-isolated-'+process.pid+'-'+randomUUID();
 const path=process.platform==='win32'?'\\\\.\\pipe\\'+name:join(tmpdir(),name+'.sock');
 const sockets=new Set(),lines=[];let count=0;
 const server=createServer(socket=>{
  sockets.add(socket);count++;socket.on('error',()=>{});socket.once('close',()=>sockets.delete(socket));
  let text='';socket.on('data',data=>{text+=data.toString();if(text.includes('\n')) {lines.push(JSON.parse(text.slice(0,text.indexOf('\n'))));reply(socket,lines.at(-1));}});
 });
 await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(path,resolve)});
 t.after(async()=>{for(const socket of sockets)socket.destroy();await new Promise(resolve=>server.close(resolve));});
 return {name:process.platform==='win32'?name:path,lines,sockets,count:()=>count};
}
function bridge(t,options) {const b=createBridge(options);t.after(()=>b.dispose());return b;}
const clean=b=>{assert.equal(b.stats.active,0);assert.equal(b.stats.pending,0);assert.equal(b.stats.timers,0)};
test('fake pipe: success waits for split full OK CRLF acknowledgement',async t=>{
 let ack;const p=await panel(t,s=>{ack=()=>{s.write('O');s.write('K\r\n')}});const b=bridge(t,{pipeName:p.name,ackTimeoutMs:500});
 let done=false;const sent=b.send(event(1)).then(ok=>{done=true;return ok});await delay(20);assert.equal(done,false);assert.equal(b.stats.sent,0);
 ack();assert.equal(await sent,true);assert.equal(b.stats.sent,1);clean(b);assert.equal(p.lines[0].client,'dsh');
});
test('fake pipe: reject, close, partial OK, oversized ACK and timeout fail',async t=>{
 for(const [reply,expected] of [[s=>s.end('ERR rejected\n'),'receiver rejected'],[s=>s.destroy(),'before OK|ECONNRESET'],[s=>s.write('OK',()=>s.destroy()),'before OK|ECONNRESET'],[s=>s.write('x'.repeat(1025)),'oversized'],[()=>{},'ack timeout']]) {
  const p=await panel(t,reply);const b=bridge(t,{pipeName:p.name,ackTimeoutMs:30});assert.equal(await b.send(event(1)),false);assert.match(b.stats.lastError,new RegExp(expected));clean(b);
 }
});
test('fake pipe: concurrent FIFO sends open next connection only after ACK',async t=>{
 let release;const p=await panel(t,(s,e)=>{if(e.turn_id==='0')release=()=>s.write('OK\n');else s.write('OK\n')});
 const b=bridge(t,{pipeName:p.name,ackTimeoutMs:500});const pending=Array.from({length:10},(_,i)=>b.send(event(i)));
 await delay(20);assert.equal(p.count(),1);assert.equal(p.lines.length,1);assert.equal(b.stats.pending,9);
 release();assert.deepEqual(await Promise.all(pending),Array(10).fill(true));assert.deepEqual(p.lines.map(e=>e.turn_id),Array.from({length:10},(_,i)=>String(i)));clean(b);
});
test('queue overflow drops oldest pending and retains bounded FIFO',async t=>{
 let release;const p=await panel(t,(s,e)=>{if(e.turn_id==='0')release=()=>s.write('OK\n');else s.write('OK\n')});
 const b=bridge(t,{pipeName:p.name,maxPending:2,ackTimeoutMs:500});const sends=[0,1,2,3].map(n=>b.send(event(n)));
 assert.equal(b.stats.pending,2);assert.equal(await sends[1],false);assert.equal(b.stats.dropped,1);await delay(20);release();
 assert.deepEqual(await Promise.all(sends),[true,false,true,true]);assert.deepEqual(p.lines.map(e=>e.turn_id),['0','2','3']);clean(b);
});
test('queue age expires without retry; fresh send recovers after timeout',async t=>{
 const p=await panel(t,(s,e)=>{if(e.turn_id!=='0')s.write('OK\n')});const b=bridge(t,{pipeName:p.name,ackTimeoutMs:35,maxQueueAgeMs:10});
 const first=b.send(event(0)),expired=b.send(event(1));assert.equal(await first,false);assert.equal(await expired,false);assert.equal(p.count(),1);assert.equal(b.stats.dropped,1);
 assert.equal(await b.send(event(2)),true);assert.equal(p.count(),2);clean(b);
});
test('dispose destroys socket/timer and resolves active/queued/future sends false',async t=>{
 const p=await panel(t,()=>{});const b=bridge(t,{pipeName:p.name,ackTimeoutMs:1000});const sends=[b.send(event(0)),b.send(event(1))];
 await delay(20);b.dispose();b.dispose();assert.deepEqual(await Promise.all(sends),[false,false]);assert.equal(await b.send(event(2)),false);clean(b);await delay(20);assert.equal(p.sockets.size,0);
});
test('connection refusal and throwing/rejecting onSent are contained',async t=>{
 for(const callback of [()=>{throw Error('callback')},()=>Promise.reject(Error('callback'))]) {
  const b=bridge(t,{pipeName:process.platform==='win32'?'gpulcd-isolated-missing-'+randomUUID():join(tmpdir(),randomUUID()+'.sock'),onSent:callback});
  assert.equal(await b.send(event(1)),false);assert.equal(b.stats.failed,1);clean(b);
 }await delay(0);
});
class FakeSocket extends EventEmitter {destroyed=false;write(){return true}destroy(){this.destroyed=true;return this}}
test('connect timeout, late error, sync connect/write failure clean resources',async t=>{
 const socket=new FakeSocket();const b=bridge(t,{connect:()=>socket,connectTimeoutMs:10});
 const keepAlive=delay(30);assert.equal(await b.send(event(0)),false);await keepAlive;assert.equal(socket.destroyed,true);clean(b);socket.emit('error',Error('late'));
 const throwing=bridge(t,{connect:()=>{throw Error('connect throw')}});assert.equal(await throwing.send(event(1)),false);clean(throwing);
 const writer=new FakeSocket();writer.write=()=>{throw Error('write throw')};const badWrite=bridge(t,{connect:()=>writer});const pending=badWrite.send(event(1));writer.emit('connect');assert.equal(await pending,false);clean(badWrite);
});
test('serialization and oversize events fail before opening sockets',async t=>{
 let connections=0;const b=bridge(t,{connect:()=>{connections++;throw Error('must not connect')}});
 assert.equal(await b.send({hook_event_name:1n,session_id:'test'}),false);assert.equal(await b.send(event('x'.repeat(9000))),false);assert.equal(connections,0);clean(b);
});

test('lazy start snapshots serialize latest state only after connect and skip stale generation',async t=>{
 const socket=new FakeSocket();let written,calls=0;let state='working';
 socket.write=(line)=>{written=JSON.parse(line);socket.emit('data',Buffer.from('OK\n'));return true};
 const b=bridge(t,{connect:()=>socket});const sent=b.send(()=>{calls++;return {...event(1),hook_event_name:'UserPromptSubmit',state}});
 assert.equal(calls,0);state='waiting';socket.emit('connect');assert.equal(await sent,true);assert.equal(calls,1);assert.equal(written.state,'waiting');clean(b);
 const stale=new FakeSocket();let writes=0;stale.write=()=>{writes++;return true};const old=bridge(t,{connect:()=>stale});const result=old.send(()=>undefined);stale.emit('connect');assert.equal(await result,false);assert.equal(writes,0);clean(old);
});

import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
// Production Apply gates, with coordinator-approved atomic DSH start field.
const source=readFileSync(new URL('../../integration/LinkState.cs',import.meta.url),'utf8');
assert.ok(source.includes('if (p != null && e.turn_id != p.Turn && !start) return;'));
assert.ok(source.includes('if (p != null && p.Closed && e.turn_id == p.Turn && !start) return;'));
export class Receiver {
 entries=new Map();enabled=true;history=[];projections=[];clock=0;currentClient='codex';clientSince=null;
 current(){
  for(const state of ['waiting','working','done']){
   const candidates=[...this.entries.values()].filter(p=>p.State===state).sort((a,b)=>(b.Updated??0)-(a.Updated??0));
   if(!candidates.length)continue;const client=candidates[0].Client??'dsh';
   const held=client!==this.currentClient&&this.clientSince!==null&&this.clock-this.clientSince<15000&&candidates.some(p=>p.Client===this.currentClient);
   if(!held&&(client!==this.currentClient||this.clientSince===null)){this.currentClient=client;this.clientSince=this.clock;}
   return state;
  }this.clientSince=null;return 'idle';
 }
 apply(e){
  this.clock++;
  try {
   if(!this.enabled)return;
   const client=e.client??'dsh',key=client+':'+e.session_id;let p=this.entries.get(key);const evt=e.hook_event_name;
   if(evt==='SessionEnd'||evt==='Interrupt'){if(p&&(!e.turn_id||e.turn_id===p.Turn))this.entries.delete(key);return;}
   const start=evt==='UserPromptSubmit';
   if(!['PreToolUse','PostToolUse','PermissionRequest','Stop'].includes(evt)&&!start)return;
   if(client==='dsh'&&start&&e.state&&!['working','waiting'].includes(e.state))return;
   if(p&&e.turn_id!==p.Turn&&!start)return;
   if(p&&p.Closed&&e.turn_id===p.Turn&&!start)return;
   if(!p||start)p={Turn:e.turn_id};
   p.State=evt==='PermissionRequest'||(client==='dsh'&&start&&e.state==='waiting')?'waiting':evt==='Stop'?'done':'working';p.Closed=evt==='Stop';p.Client=client;p.Updated=this.clock;
   this.entries.set(key,p);this.history.push({...p});
  } finally {this.projections.push({event:e,state:this.current(),client:this.currentClient,hold:this.clientSince});}
 }
}

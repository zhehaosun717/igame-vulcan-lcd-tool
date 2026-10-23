// Compile an exact verbatim copy of production LinkState.cs in a fresh TEMP.
// This process is a pure state-machine executable: no UI, pipe, profile or hardware.
import {mkdtempSync,readFileSync,writeFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {spawnSync} from 'node:child_process';
import {createHash} from 'node:crypto';
export function projectWithRealReceiver(events){
 const dir=mkdtempSync(join(tmpdir(),'gpulcd-real-receiver-'));
 const original=readFileSync(new URL('../../integration/LinkState.cs',import.meta.url),'utf8');
 try {
  writeFileSync(join(dir,'LinkState.cs'),original,'utf8');
  writeFileSync(join(dir,'Probe.cs'),
  'using System;using System.Web.Script.Serialization;using GpuLcd;class Probe{static int Main(){var j=new JavaScriptSerializer();var s=new LinkState();string line;int i=0;while((line=Console.ReadLine())!=null){var e=j.Deserialize<LinkEvent>(line);var now=new DateTime(2026,1,1).AddMilliseconds(i++);s.Apply(e,now);Console.WriteLine(j.Serialize(new{state=s.Current(now),client=s.CurrentClient,hold=((DateTime)typeof(LinkState).GetField("clientSince",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(s)).Ticks,snapshot=s.Snapshot()}));}return 0;}}','utf8');
  const exe=join(dir,'Probe.exe');
  const compile=spawnSync('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe',['/nologo','/r:System.Web.Extensions.dll','/out:'+exe,join(dir,'LinkState.cs'),join(dir,'Probe.cs')],{encoding:'utf8',cwd:dir});
  if(compile.status!==0)throw Error('Real receiver compile failed: '+compile.stdout+compile.stderr);
  const run=spawnSync(exe,[],{input:events.map(e=>JSON.stringify({...e,client:e.client??'dsh'})).join('\n')+'\n',encoding:'utf8',cwd:dir});
  if(run.status!==0)throw Error('Real receiver exited '+run.status+': '+run.stderr);
  const frames=run.stdout.trim().split(/\r?\n/).map(line=>JSON.parse(line));
  return {frames,sha256:createHash('sha256').update(original).digest('hex'),exitCode:run.status};
 } finally {rmSync(dir,{recursive:true,force:true})}
}

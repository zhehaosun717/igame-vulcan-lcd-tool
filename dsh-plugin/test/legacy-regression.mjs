// Intentionally exits 1 for the broken committed plugin. Never installs it.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { readAsar, defaultArchive } from './asar.mjs';
const root=fileURLToPath(new URL('../../',import.meta.url));
const temporary=mkdtempSync(join(tmpdir(),'gpulcd-legacy-regression-'));
const cordis=readAsar(process.env.DSH_CONTRACT_ASAR||defaultArchive,'dsh/node_modules/@deepseek-ai/cordis/lib/index.js');
const method=cordis.match(/waterfall\(\.\.\.args\) \{[\s\S]*?\n\t\}/)?.[0];assert.ok(method);
const waterfall=Function('return ({'+method+'}).waterfall')();
try {
 writeFileSync(join(temporary,'package.json'),'{"type":"module"}');
 for(const name of ['index','bridge']){
  const result=spawnSync('git',['show','HEAD:dsh-plugin/lib/'+name+'.js'],{cwd:root,encoding:'utf8'});
  assert.equal(result.status,0,result.stderr);writeFileSync(join(temporary,name+'.js'),result.stdout,'utf8');
 }
 const {apply}=await import(pathToFileURL(join(temporary,'index.js')));const listeners=new Map(),cleanups=[];
 const ctx={on:(name,fn)=>{listeners.set(name,fn);return ()=>listeners.delete(name)},effect:fn=>cleanups.push(fn()),dispatch:(_kind,args)=>{args.shift();const name=args.shift();return listeners.has(name)?[listeners.get(name)]:[]}};
 apply(ctx,{pipeName:'gpulcd-isolated-unused-'+process.pid});let nextCalls=0;
 try {
  const gate=await waterfall.call(ctx,{},'tools/pre-execute',{},()=>{nextCalls++;return Promise.resolve({kind:'allow'})});
  console.log('legacy terminal nextCalls='+nextCalls);
  console.log(gate.kind); // exact installed dsh-tools dereference: intentionally throws
 } finally {for(const cleanup of cleanups)cleanup()}
} finally {rmSync(temporary,{recursive:true,force:true})}

// Read-only proof: print exact installed call sites and module hashes.
import { createHash } from 'node:crypto';
import { readAsar, defaultArchive } from './asar.mjs';
const archive=process.env.DSH_CONTRACT_ASAR||defaultArchive;
for (const [pkg,pattern] of [
 ['cordis',/waterfall\(\.\.\.args/],
 ['dsh-tools',/const gate =/],
 ['dsh-user-approval',/const answer = Promise/],
 ['dsh-user-questions',/return await \(agent ===/],
 ['dsh-agent',/const fused =|emitDisposed\(entry\)|^\s*list\(\)/],
 ['dsh-agent-loop',/get status\(\)|this\.dispatch\.emit\("agent\/status|this\.session\.append\("turn\/end/],
 ['dsh-session',/const callbackArgs = \[this, event\]/]
]) {
 const path='dsh/node_modules/@deepseek-ai/'+pkg+'/lib/index.js';const source=readAsar(archive,path),lines=source.split('\n');
 const version=JSON.parse(readAsar(archive,'dsh/node_modules/@deepseek-ai/'+pkg+'/package.json')).version;
 console.log(pkg+'@'+version+' sha256='+createHash('sha256').update(source).digest('hex'));
 for(let i=0;i<lines.length;i++)if(pattern.test(lines[i]))console.log(path+':'+(i+1)+'\n'+lines.slice(i,i+10).join('\n'));
}

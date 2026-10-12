const fs=require('fs'),vm=require('vm'),assert=require('assert');
const html=fs.readFileSync(__dirname+'/CodexLCD.template.html','utf8');
const sourcePalette=JSON.parse(fs.readFileSync(__dirname+'/nyan-palette.json','utf8'));
const code=html.match(/<script>([\s\S]*?)<\/script>/)[1],logs=[];
const ctx={fillRect(){},createLinearGradient(){return {addColorStop(){}}}};
const s={document:{getElementById(){return {getContext(){return ctx}}}},window:{requestAnimationFrame(){}},
 console:{log(t){logs.push(t)}},Date,JSON,Math,previewState:'Auto',intensity:55};
vm.createContext(s);vm.runInContext(code,s);
s.onCanvasApiEvent({sender:'other',event:'working'});assert.equal(s.state,'idle');
s.onCanvasApiEvent({sender:'GpuLcdTool',event:'working'});assert.equal(s.state,'working');
assert(logs[0]==='GpuLcdState=working;seq=1');
s.onCanvasApiEvent({sender:'GpuLcdTool',event:'unknown'});assert.equal(s.state,'working');
s.previewState='Waiting';s.poll(Date.now());assert.equal(s.manualState,'waiting');assert(s.manualUntil>Date.now());
s.update();assert(typeof ctx.fillStyle==='string');
assert(!html.includes('XMLHttpRequest'));assert(!html.includes('19347'));
console.log('PASS effect sender/state validation, numbered event acknowledgement, manual preview, and rendering; no loopback requests.');

// Sample actual rendered Done frames over a full breathing cycle.
let clock=0;const doneFrames=[];
const doneCtx={fillRect(){doneFrames.push(this.fillStyle)}};
const doneSandbox={document:{getElementById(){return {getContext(){return doneCtx}}}},
 window:{requestAnimationFrame(){}},console:{log(){}},Date:{now:()=>clock},Math,
 previewState:'Auto',intensity:100};
vm.createContext(doneSandbox);vm.runInContext(code,doneSandbox);
doneSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'done'});
for(clock=0;clock<=3200;clock+=50)doneSandbox.update();
const pixels=doneFrames.map(c=>c.match(/\d+/g).map(Number));
const greens=pixels.map(c=>c[1]);
assert(Math.max(...greens)>=206,'Done must reach full configured brightness');
assert(Math.min(...greens)>=103 && Math.min(...greens)<=105,'Done must breathe down to half brightness');
assert(new Set(doneFrames).size>20,'Done frames must visibly change over time');
assert(pixels.every(c=>Math.abs(c[0]/c[1]-34/207)<.012 && Math.abs(c[2]/c[1]-92/207)<.012),'Done hue must remain emerald throughout the pulse');
console.log('PASS Done rendered brightness range, temporal animation, and preserved emerald hue.');
// Idle follows the GIF palette timeline; heartbeat events must not restart it.
let idleTime=0;const idlePixels=[];
function idleRendered(){return JSON.stringify(idlePixels.slice(-64));}
const idleCtx={fillRect(x,y,w,h){idlePixels.push([x,y,w,h,this.fillStyle]);},createLinearGradient(){return {addColorStop(){}}}};
const idleSandbox={document:{getElementById(){return {getContext(){return idleCtx}}}},window:{requestAnimationFrame(){}},console:{log(){}},Date:{now:()=>idleTime},Math,previewState:'Auto',intensity:100};
vm.createContext(idleSandbox);vm.runInContext(code,idleSandbox);
idleSandbox.update();const first=idleRendered();
const sampled=[];
for(idleTime=0;idleTime<idleSandbox.idleDuration;idleTime+=idleSandbox.idleDuration/24){idleSandbox.update();sampled.push(idleRendered());}
assert(new Set(sampled).size>1,'Idle must follow changing animation colors');
assert.equal(idleSandbox.idleDuration,sourcePalette.duration_ms,'Loop must match the configured rainbow period');
assert.equal(JSON.stringify(idleSandbox.idlePalette),JSON.stringify(sourcePalette.frames),'Rainbow stops must match extracted tail colors');
idleTime=sourcePalette.duration_ms;idleSandbox.update();assert.equal(idleRendered(),first,'Rainbow loop must wrap seamlessly');
const epoch=idleSandbox.idleEpoch;
idleTime=10000;idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle'});idleSandbox.update();assert.equal(idleSandbox.idleEpoch,epoch,'Idle heartbeat must not restart the palette');
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'working'});idleSandbox.update();idleTime+=100;
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle'});idleSandbox.update();assert.equal(idleSandbox.idleEpoch,idleTime,'Returning from a task restarts the idle palette');
console.log('PASS animated idle palette, SAVI-duration loop, heartbeat stability, and task-return restart.');
// Receiving the event late must still use the LCD command clock.
idleTime=1791356400000+2000;
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle@1791356400000@0'});idleSandbox.update();
assert.equal(idleSandbox.idleEpoch,1791356400000,'Transport delay must not shift the start');
const delayedColors=JSON.stringify(idleSandbox.idleColors(idleTime));
idleTime+=idleSandbox.idleDuration;
assert.equal(JSON.stringify(idleSandbox.idleColors(idleTime)),delayedColors,'Clock-based phase must loop');
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle@1791356400000@250'});idleSandbox.update();
assert.equal(idleSandbox.idleEpoch,1791356399750,'Positive offset advances lighting');
idleSandbox.onCanvasApiEvent({sender:'other',event:'idle@1791356400000@0'});
assert.equal(idleSandbox.idleEpoch,1791356399750,'Untrusted sender cannot change phase');
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle@1791356400000@9999'});
assert.equal(idleSandbox.idleEpoch,1791356399750,'Out-of-range phase correction is rejected');
console.log('PASS shared LCD epoch, delayed delivery, loop phase, offset and sender validation.');
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle%401791356400000%40-150'});
assert.equal(idleSandbox.idleEpoch,1791356400150,'SignalRGB passes URL-escaped event text without decoding');
console.log('PASS SignalRGB URL-escaped event compatibility.');
// A 0.1% speed correction changes ten-minute phase by 600 ms; offset stays independent.
const rateEpoch=1791356400000;
const paletteAt=t=>{t=((t%sourcePalette.duration_ms)+sourcePalette.duration_ms)%sourcePalette.duration_ms;const i=Math.floor(t/900),a=sourcePalette.colors[i],b=sourcePalette.colors[(i+1)%6],u=t/900-i;return a.map((v,j)=>v*(1-u)+b[j]*u);};
const closeColor=(a,b)=>assert(Array.from(a).every((v,i)=>Math.abs(v-b[i])<.001));
idleTime=rateEpoch+600000;
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle@'+rateEpoch+'@-600@99.90'});
assert(Math.abs(idleSandbox.idleRate-.999)<1e-12);
closeColor(idleSandbox.idleColors(idleTime),paletteAt(4800));
const ratePhase=JSON.stringify(idleSandbox.idleColors(idleTime));
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle@'+rateEpoch+'@-600@99.90'});
assert.equal(JSON.stringify(idleSandbox.idleColors(idleTime)),ratePhase,'Heartbeat must preserve corrected phase');
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle@'+rateEpoch+'@-600@100.10'});
closeColor(idleSandbox.idleColors(idleTime),paletteAt(6000));
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle@'+rateEpoch+'@0@110.00'});
assert(Math.abs(idleSandbox.idleRate-1.001)<1e-12,'Invalid speed must be ignored');
idleSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'idle@'+rateEpoch+'@0'});
assert.equal(idleSandbox.idleRate,1,'Legacy messages must retain original speed');
console.log('PASS long-run playback rate, independent offset, heartbeat stability and legacy compatibility.');
// The tail's six colors form a continuous spatial rainbow with a smooth wrap.
assert.equal(sourcePalette.screen_duration_ms,900);
assert.equal(sourcePalette.colors.length,6);
for(const row of sourcePalette.frames){
 idleTime=idleSandbox.idleEpoch+row[0];idlePixels.length=0;idleSandbox.update();
 assert.equal(idlePixels.length,65);
 const stripes=idlePixels.slice(1);
 assert(stripes.every((p,i)=>p[0]===i*5 && p[1]===0 && p[2]===5 && p[3]===200));
 assert(new Set(stripes.map(p=>p[4])).size>50,'Rainbow must cover different device positions');
 assert.equal(stripes[0][4],'rgb('+idleSandbox.idleSaturation(row.slice(1)).map(v=>Math.round(v*.45)).join(',')+')');
}
const before=idleSandbox.idleColors(idleSandbox.idleEpoch+idleSandbox.idleDuration-.01);
const after=idleSandbox.idleColors(idleSandbox.idleEpoch+idleSandbox.idleDuration+.01);
assert(Array.from(before).every((v,i)=>Math.abs(v-after[i])<.01),'Loop seam must not flash');
assert(Array.from(idleSandbox.idleSaturation([100,150,200])).every((v,i)=>Math.abs(v-[85,142.5,200][i])<1e-9));
for(const rgb of [[0,0,0],[255,255,255],[128,128,128],[245,250,249],[255,0,50]])assert.deepEqual(Array.from(idleSandbox.idleSaturation(rgb)),rgb);
console.log('PASS Nyan tail colors, full spatial coverage, smooth interpolation and loop seam.');

// Sleep is latched: task events, manual previews, idle timeout, and reload cannot wake it.
let nightTime=100000;const store={};let nightFrames=[];
const nightCtx={fillRect(){nightFrames.push(this.fillStyle)},createLinearGradient(){return {addColorStop(){}}}};
const nightSandbox={document:{getElementById(){return {getContext(){return nightCtx}}}},window:{requestAnimationFrame(){}},console:{log(){}},Date:{now:()=>nightTime},Math,previewState:'Auto',intensity:100,localStorage:{getItem:k=>store[k],setItem:(k,v)=>store[k]=v}};
vm.createContext(nightSandbox);vm.runInContext(code,nightSandbox);
nightSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'sleep'});
for(const ev of ['working','waiting','done','idle','claude@working','claude@waiting','claude@done']){
 nightSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:ev});nightSandbox.previewState='Working';nightTime+=60000;
 nightFrames=[];nightSandbox.update();assert.deepEqual(nightFrames,['rgb(0,0,0)']);
}
assert.equal(store.GpuLcdNightMode,'1');
vm.runInContext(code,nightSandbox);nightFrames=[];nightSandbox.update();assert.deepEqual(nightFrames,['rgb(0,0,0)']);
nightSandbox.onCanvasApiEvent({sender:'other',event:'wake'});assert.equal(nightSandbox.blackout,true);
nightSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'wake'});nightSandbox.onCanvasApiEvent({sender:'GpuLcdTool',event:'done'});
nightFrames=[];nightSandbox.update();assert(nightFrames.some(c=>c!=='rgb(0,0,0)'));assert.equal(store.GpuLcdNightMode,'0');
console.log('PASS blackout survives task/preview/timeout/reload and only explicit wake restores light.');

// Claude follows its LCD assets; client changes must propagate even when state is unchanged.
let ccTime=10000,ccFrames=[],ccStops=[];
const ccCtx={fillRect(){ccFrames.push(this.fillStyle)},createLinearGradient(){return {addColorStop(p,c){ccStops.push(c)}}}};
const cc={document:{getElementById(){return {getContext(){return ccCtx}}}},window:{requestAnimationFrame(){}},console:{log(){}},Date:{now:()=>ccTime},Math,previewState:'Auto',intensity:100};
vm.createContext(cc);vm.runInContext(code,cc);
const sendCC=event=>cc.onCanvasApiEvent({sender:'GpuLcdTool',event});
const baseCC=()=>{ccFrames=[];cc.update();return ccFrames[0].match(/\d+/g).map(Number);};
sendCC('working');sendCC('claude%40working');
assert.equal(cc.taskClient,'claude');assert.deepEqual(baseCC(),[163,74,46]);
assert(ccStops.every(c=>c.includes('217,99,61')),'Claude working glow must stay warm');
sendCC('unknown@done');assert.equal(cc.state,'working');
sendCC('claude@waiting');const amber=[];
for(let i=0;i<=1920;i+=80){ccTime=10000+i;amber.push(baseCC());}
assert(Math.max(...amber.map(c=>c[0]))>=239 && Math.min(...amber.map(c=>c[0]))<=86,'Claude waiting must breathe in amber');
sendCC('claude@done');assert.deepEqual(baseCC(),[53,110,53]);ccTime+=1000;
assert.deepEqual(baseCC(),[96,200,96]);ccTime+=20000;sendCC('claude@done');
assert.deepEqual(baseCC(),[96,200,96],'Done heartbeat must not restart fade-in');
sendCC('done');assert.equal(cc.taskClient,'codex');assert.notDeepEqual(baseCC(),[96,200,96]);
console.log('PASS Claude terracotta/amber/soft green, source switching, warm glow and stable Done.');

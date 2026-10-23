using System;
namespace GpuLcd {
 static class LinkTests {
  static int count;
  static void Equal(string expected,string actual){if(expected!=actual)throw new Exception("Expected "+expected+", got "+actual);count++;}
  static LinkEvent E(string eventName,string session,string turn="1"){return new LinkEvent{hook_event_name=eventName,session_id=session,turn_id=turn};}
  // A failing assertion exits with code 1 instead of an unhandled exception (which can hang on an error dialog).
  static int Main(){
   try{Run();return 0;}catch(Exception ex){Console.WriteLine("FAIL: "+ex.Message);return 1;}
  }
  // DeepSeek Harness: own client name, own state, does not disturb the Codex/Claude cases above.
  static void DeepSeek(){
   var t=new DateTime(2026,10,7);
// DeepSeek Harness reports under its own client name and stays separate from Codex with the same session id.
   var ds=new LinkState();var de=E("UserPromptSubmit","same");de.client="dsh";ds.Apply(de,t);
   Equal("working",ds.Current(t));Equal("dsh",ds.CurrentClient);
   var dsStop=E("Stop","same");dsStop.client="dsh";ds.Apply(dsStop,t.AddSeconds(1));
   Equal("done",ds.Current(t.AddSeconds(1)));
   Equal("done",ds.Current(t.AddSeconds(29)));Equal("idle",ds.Current(t.AddSeconds(31)));
   var dsAsk=E("PermissionRequest","b");dsAsk.client="dsh";ds.Apply(dsAsk,t.AddSeconds(60));
   Equal("waiting",ds.Current(t.AddSeconds(60)));Equal("dsh",ds.CurrentClient);
  }
  static void ThreeClientsAndProtocol(){
   var t=new DateTime(2026,10,7);
   Func<string,string,LinkEvent> ev=(client,name)=>{var e=E(name,"same");e.client=client;return e;};
   // Every source can be the first displayed client and receives the same 15 s hold.
   foreach(var first in new[]{"codex","claude","dsh"}){
    var s=new LinkState();s.Apply(ev(first,"UserPromptSubmit"),t);Equal("working",s.Current(t));Equal(first,s.CurrentClient);
    foreach(var other in new[]{"codex","claude","dsh"})if(other!=first)s.Apply(ev(other,"UserPromptSubmit"),t.AddSeconds(2));
    s.Current(t.AddSeconds(14));Equal(first,s.CurrentClient);
    var latest=first=="dsh"?"claude":"dsh";
    s.Apply(ev(latest,"PostToolUse"),t.AddSeconds(15));s.Current(t.AddSeconds(15));Equal(latest,s.CurrentClient);
    s.Apply(ev(first,"PermissionRequest"),t.AddSeconds(16));Equal("waiting",s.Current(t.AddSeconds(16)));Equal(first,s.CurrentClient);
    s.ClearClient(first);Equal("working",s.Current(t.AddSeconds(17)));
   }
   // A DSH off/on sequence discards its old state, without clearing other clients.
   var toggle=new LinkState();toggle.Apply(ev("dsh","UserPromptSubmit"),t);toggle.Apply(ev("codex","UserPromptSubmit"),t);
   toggle.ClearClient("dsh");Equal("working",toggle.Current(t));Equal("codex",toggle.CurrentClient);
   Equal("False",LinkState.ClientEnabled("dsh",true,true,false).ToString());
   if(LinkState.ClientEnabled("dsh",true,true,false))toggle.Apply(ev("dsh","PermissionRequest"),t);
   Equal("working",toggle.Current(t));Equal("True",LinkState.ClientEnabled("dsh",false,false,true).ToString());
   toggle.ClearClient("codex");Equal("idle",toggle.Current(t)); // re-enable alone cannot resurrect old DSH work
   toggle.Apply(ev("dsh","UserPromptSubmit"),t.AddSeconds(1));Equal("working",toggle.Current(t.AddSeconds(1)));Equal("dsh",toggle.CurrentClient);
   toggle.Apply(ev("dsh","Stop"),t.AddSeconds(2));Equal("done",toggle.Current(t.AddSeconds(2)));
   toggle.Apply(ev("dsh","Stop"),t.AddSeconds(25));Equal("idle",toggle.Current(t.AddSeconds(32))); // duplicate Stop does not extend Done
   foreach(var invalid in new[]{"unknown","DSH","Codex","",null}){
    var s=new LinkState();s.Apply(ev(invalid,"UserPromptSubmit"),t);Equal("idle",s.Current(t));
    Equal("False",LinkState.ClientEnabled(invalid,true,true,true).ToString());
    Equal(null,SignalProtocol.TaskEvent("working",invalid));
   }
   var empty=new LinkState();empty.Apply(null,t);empty.Apply(ev("dsh","Unexpected"),t);Equal("idle",empty.Current(t));
   var legacy=E("UserPromptSubmit","legacy");Equal("codex",legacy.client);
   foreach(var client in new[]{"codex","claude","dsh"}){
    Equal("True",LinkState.ClientEnabled(client,true,true,true).ToString());
    foreach(var state in new[]{"working","waiting","done"}){
     string wire=client=="codex"?state:client+"@"+state;
     Equal(wire,SignalProtocol.TaskEvent(state,client));
     Equal(wire,SignalProtocol.Event(state,client,false,false,1791356400000,250,100m));
     Equal("sleep",SignalProtocol.Event(state,client,true,true,0,0,100m));
     Equal("wake",SignalProtocol.Event(state,client,false,true,0,0,100m));
    }
    Equal("idle",SignalProtocol.TaskEvent("idle",client));
   }
   Equal("idle@1791356400000@250@99.90",SignalProtocol.Event("idle","dsh",false,false,1791356400000,250,99.9m));
   Equal("idle@1791356400000@-250@105.00",SignalProtocol.Event("idle","dsh",false,false,1791356400000,-250,110m));
   Equal("idle",SignalProtocol.Event("idle","dsh",false,false,0,0,100m));
   Equal(null,SignalProtocol.TaskEvent("bad","dsh"));
   string marker,stateName,source,firstMarker;
   Equal("True",SignalProtocol.LatestAck("GpuLcdState=done;seq=7",out marker,out stateName,out source).ToString());Equal("codex",source);firstMarker=marker;
   Equal("True",SignalProtocol.LatestAck("GpuLcdState=done;seq=7;client=dsh",out marker,out stateName,out source).ToString());Equal("dsh",source);Equal("done",stateName);Equal("False",(firstMarker==marker).ToString());
   foreach(var client in new[]{"claude","codex","dsh"}){
    Equal("True",SignalProtocol.LatestAck("GpuLcdState=working;seq=1;client="+client,out marker,out stateName,out source).ToString());Equal(client,source);
   }
   Equal("True",SignalProtocol.LatestAck("GpuLcdState=working;seq=2;client=claude\nGpuLcdState=waiting;seq=3;client=dsh",out marker,out stateName,out source).ToString());Equal("dsh",source);Equal("waiting",stateName);
   Equal("False",SignalProtocol.LatestAck("GpuLcdState=done;seq=4;client=unknown",out marker,out stateName,out source).ToString());
   Equal("False",SignalProtocol.LatestAck("GpuLcdState=unknown;seq=4;client=dsh",out marker,out stateName,out source).ToString());
   foreach(var suffix in new[]{";client=", ";client=DSH", ";client=dsh!", ";client=null", "x;client=dsh"})
    Equal("False",SignalProtocol.LatestAck("GpuLcdState=done;seq=4"+suffix,out marker,out stateName,out source).ToString());
   Equal("False",SignalProtocol.LatestAck(null,out marker,out stateName,out source).ToString());
  }
  static LinkEvent DshEvent(string name,string turn,string initial=""){
   return new LinkEvent{client="dsh",session_id="recover",turn_id=turn,hook_event_name=name,state=initial};
  }
  static void AtomicWaitingStart(){
   var t=new DateTime(2026,10,7);
   Equal("",new LinkEvent().state); // Omitted extension preserves legacy start behavior.
   // No two-event working -> waiting sequence: one Apply replaces an old working/closed turn directly.
   foreach(var old in new[]{"working","closed"}){
    var s=new LinkState();s.Apply(DshEvent("UserPromptSubmit","old"),t);
    if(old=="closed")s.Apply(DshEvent("Stop","old"),t.AddSeconds(1));
    s.Apply(DshEvent("UserPromptSubmit","new","waiting"),t.AddSeconds(2));
    Equal("waiting",s.Current(t.AddSeconds(2)));Equal("dsh",s.CurrentClient);Equal("1",s.Snapshot().Length.ToString());
    // A stale old-turn completion/tool event must not close or overwrite the recovered waiting turn.
    foreach(var stale in new[]{"Stop","PostToolUse","PermissionRequest","Interrupt","SessionEnd"}){
     s.Apply(DshEvent(stale,"old","working"),t.AddSeconds(3));Equal("waiting",s.Current(t.AddSeconds(3)));
    }
    s.Apply(DshEvent("UserPromptSubmit","new","waiting"),t.AddSeconds(4));
    Equal("waiting",s.Current(t.AddSeconds(4)));Equal("1",s.Snapshot().Length.ToString());
    s.Apply(DshEvent("Stop","new","waiting"),t.AddSeconds(5));Equal("done",s.Current(t.AddSeconds(5)));
    foreach(var late in new[]{"PermissionRequest","PostToolUse","PreToolUse","Stop"}){
     s.Apply(DshEvent(late,"new","waiting"),t.AddSeconds(20));Equal("done",s.Current(t.AddSeconds(20)));
    }
    Equal("idle",s.Current(t.AddSeconds(35))); // Duplicate Stop does not extend the 30 s done window.
   }
   foreach(var initial in new[]{"",null,"working"}){
    var s=new LinkState();s.Apply(DshEvent("UserPromptSubmit","old"),t);s.Apply(DshEvent("Stop","old"),t);
    s.Apply(DshEvent("UserPromptSubmit","new",initial),t.AddSeconds(1));Equal("working",s.Current(t.AddSeconds(1)));
    s.Apply(DshEvent("Stop","new"),t.AddSeconds(2));Equal("done",s.Current(t.AddSeconds(2)));
   }
   foreach(var invalid in new[]{"done","idle","Waiting","WAITING"," ","unknown"}){
    var empty=new LinkState();empty.Apply(DshEvent("UserPromptSubmit","new",invalid),t);Equal("idle",empty.Current(t));Equal("0",empty.Snapshot().Length.ToString());
    var busy=new LinkState();busy.Apply(DshEvent("UserPromptSubmit","old","waiting"),t);
    busy.Apply(DshEvent("UserPromptSubmit","new",invalid),t.AddSeconds(1));Equal("waiting",busy.Current(t.AddSeconds(1)));
    busy.Apply(DshEvent("Stop","old"),t.AddSeconds(2));Equal("done",busy.Current(t.AddSeconds(2))); // Invalid start did not replace old turn.
   }
   foreach(var client in new[]{"codex","claude"}){
    foreach(var initial in new[]{"waiting","invalid"}){
     var s=new LinkState();var e=DshEvent("UserPromptSubmit","new",initial);e.client=client;s.Apply(e,t);
     Equal("working",s.Current(t));Equal(client,s.CurrentClient);
    }
   }
   // The extension is ignored for ordinary events, even when its value is invalid.
   var ordinary=new LinkState();ordinary.Apply(DshEvent("UserPromptSubmit","new","waiting"),t);
   ordinary.Apply(DshEvent("PostToolUse","new","invalid"),t.AddSeconds(1));Equal("working",ordinary.Current(t.AddSeconds(1)));
   ordinary.Apply(DshEvent("PermissionRequest","new","invalid"),t.AddSeconds(2));Equal("waiting",ordinary.Current(t.AddSeconds(2)));
   // Existing waiting source holds 15 s; repeated DSH starts cannot bypass or reset that clock.
   var h=new LinkState();var cc=E("PermissionRequest","cc");cc.client="claude";h.Apply(cc,t);Equal("waiting",h.Current(t));Equal("claude",h.CurrentClient);
   h.Apply(DshEvent("UserPromptSubmit","new","waiting"),t.AddSeconds(2));Equal("waiting",h.Current(t.AddSeconds(2)));Equal("claude",h.CurrentClient);
   h.Apply(DshEvent("UserPromptSubmit","new","waiting"),t.AddSeconds(10));h.Current(t.AddSeconds(14));Equal("claude",h.CurrentClient);
   h.Current(t.AddSeconds(15));Equal("dsh",h.CurrentClient);
   h.Apply(E("PermissionRequest","cx"),t.AddSeconds(16));h.Current(t.AddSeconds(29));Equal("dsh",h.CurrentClient);
   h.Current(t.AddSeconds(30));Equal("codex",h.CurrentClient);
   // New waiting starts still outrank another client's working state immediately.
   var priority=new LinkState();priority.Apply(E("UserPromptSubmit","cx"),t);priority.Current(t);
   priority.Apply(DshEvent("UserPromptSubmit","new","waiting"),t.AddSeconds(1));Equal("waiting",priority.Current(t.AddSeconds(1)));Equal("dsh",priority.CurrentClient);
  }
  static void Run(){
   var s=new LinkState();var t=new DateTime(2026,10,7);
   Equal("idle",s.Current(t));
   s.Apply(E("UserPromptSubmit","a"),t);Equal("working",s.Current(t));
   s.Apply(E("PermissionRequest","a"),t);Equal("waiting",s.Current(t));
   s.Apply(E("UserPromptSubmit","b"),t);Equal("waiting",s.Current(t));
   s.Apply(E("PostToolUse","a"),t);Equal("working",s.Current(t));
   s.Apply(E("Stop","a"),t);Equal("working",s.Current(t));
   s.Apply(E("PreToolUse","a"),t);Equal("working",s.Current(t)); // Closed A must not resurrect.
   s.Apply(E("Stop","b"),t);Equal("done",s.Current(t));
   Equal("done",s.Current(t.AddSeconds(29)));Equal("idle",s.Current(t.AddSeconds(30)));
   s.Apply(E("UserPromptSubmit","a","2"),t.AddSeconds(40));
   s.Apply(E("Stop","a","1"),t.AddSeconds(41));Equal("working",s.Current(t.AddSeconds(41)));
   s.Apply(E("PermissionRequest","a","2"),t.AddSeconds(42));Equal("waiting",s.Current(t.AddSeconds(42)));
   s.Apply(E("Interrupt","a","2"),t.AddSeconds(43));Equal("idle",s.Current(t.AddSeconds(43)));
   s.Apply(E("UserPromptSubmit","c"),t);Equal("idle",s.Current(t.AddMinutes(31)));
   s.Apply(E("UserPromptSubmit","d"),t);s.Apply(E("SessionEnd","d"),t);Equal("idle",s.Current(t));
   DeepSeek();
   ThreeClientsAndProtocol();
   AtomicWaitingStart();
   // Claude's notifications: prompts wait for the user, the idle reminder ends the turn, others are ignored.
   Equal("PermissionRequest",LinkState.ClaudeEventName("Notification",""));
   Equal("PermissionRequest",LinkState.ClaudeEventName("Notification","permission_prompt"));
   Equal("PermissionRequest",LinkState.ClaudeEventName("Notification","elicitation_dialog"));
   Equal("Stop",LinkState.ClaudeEventName("Notification","idle_prompt"));
   Equal("Notification",LinkState.ClaudeEventName("Notification","auth_success"));
   Equal("PostToolUse",LinkState.ClaudeEventName("PostToolUse","idle_prompt"));
   // A lost Stop: the idle reminder a minute later closes the turn instead of showing "waiting" for 30 minutes.
   var lost=new LinkState();
   var lw=E("UserPromptSubmit","lost","");lw.client="claude";lost.Apply(lw,t);
   var reminder=E(LinkState.ClaudeEventName("Notification","idle_prompt"),"lost","");reminder.client="claude";lost.Apply(reminder,t.AddSeconds(60));
   Equal("done",lost.Current(t.AddSeconds(60)));Equal("idle",lost.Current(t.AddSeconds(200)));
   s.Clear();
   var ce=E("UserPromptSubmit","same");ce.client="claude";s.Apply(ce,t);
   s.Apply(E("UserPromptSubmit","same"),t.AddSeconds(1));
   Equal("working",s.Current(t.AddSeconds(1)));Equal("codex",s.CurrentClient);
   var cw=E("PermissionRequest","same");cw.client="claude";s.Apply(cw,t.AddSeconds(2));
   Equal("waiting",s.Current(t.AddSeconds(2)));Equal("claude",s.CurrentClient);
   s.ClearClient("claude");Equal("working",s.Current(t.AddSeconds(3)));
   s.Clear();
   var cd=E("Stop","c");cd.client="claude";s.Apply(cd,t);
   Equal("done",s.Current(t.AddSeconds(119)));Equal("idle",s.Current(t.AddSeconds(121)));
   // Two clients working at once: the shown client holds for 15 s instead of flipping on every tool event.
   Func<string,string,string,LinkEvent> CE=(evt,session,client)=>{var e=E(evt,session);e.client=client;return e;};
   var h=new LinkState();
   h.Apply(CE("UserPromptSubmit","cc","claude"),t);Equal("working",h.Current(t));Equal("claude",h.CurrentClient);
   h.Apply(CE("UserPromptSubmit","cx","codex"),t.AddSeconds(2));Equal("working",h.Current(t.AddSeconds(2)));Equal("claude",h.CurrentClient);
   h.Apply(CE("PostToolUse","cc","claude"),t.AddSeconds(3));h.Apply(CE("PostToolUse","cx","codex"),t.AddSeconds(5));
   h.Current(t.AddSeconds(5));Equal("claude",h.CurrentClient);
   h.Current(t.AddSeconds(14));Equal("claude",h.CurrentClient);
   h.Current(t.AddSeconds(16));Equal("codex",h.CurrentClient); // hold over: the most recent one shows
   h.Apply(CE("PostToolUse","cc","claude"),t.AddSeconds(17));h.Current(t.AddSeconds(17));Equal("codex",h.CurrentClient);
   // A more urgent state is never held back.
   h.Apply(CE("PermissionRequest","cc","claude"),t.AddSeconds(18));Equal("waiting",h.Current(t.AddSeconds(18)));Equal("claude",h.CurrentClient);
   // The held client leaving the state lets the other one show at once.
   h.Apply(CE("PostToolUse","cc","claude"),t.AddSeconds(19));h.Current(t.AddSeconds(19));Equal("claude",h.CurrentClient);
   h.Apply(CE("Stop","cc","claude"),t.AddSeconds(20));Equal("working",h.Current(t.AddSeconds(20)));Equal("codex",h.CurrentClient);
   Console.WriteLine(count+" state-machine assertions passed.");
  }
 }
}

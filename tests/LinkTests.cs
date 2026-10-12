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

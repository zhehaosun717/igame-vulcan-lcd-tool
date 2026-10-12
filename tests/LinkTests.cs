using System;
namespace GpuLcd {
 static class LinkTests {
  static int count;
  static void Equal(string expected,string actual){if(expected!=actual)throw new Exception("Expected "+expected+", got "+actual);count++;}
  static LinkEvent E(string eventName,string session,string turn="1"){return new LinkEvent{hook_event_name=eventName,session_id=session,turn_id=turn};}
  static void Main(){
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
   Console.WriteLine(count+" state-machine assertions passed.");
  }
 }
}

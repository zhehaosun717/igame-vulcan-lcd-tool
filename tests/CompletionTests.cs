using System;
using System.IO;
namespace GpuLcd {
    static class CompletionTests {
        static int count;
        static void Check(bool value){if(!value)throw new Exception("Completion test "+(count+1)+" failed");count++;}
        static string Record(string turn){return "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\""+turn+"\"}}";}
        static void Main(){
            var t=DateTime.UtcNow;var s=new LinkState();
            var done=CompletionReader.Parse(Record("1"),"test");Check(done!=null && done.hook_event_name=="Stop");
            Check(!s.ApplyCompletion(done,t)); // Historical records cannot invent active tasks.
            s.Apply(new LinkEvent{session_id="test",turn_id="1",hook_event_name="UserPromptSubmit"},t);
            Check(s.Current(t)=="working");Check(s.ApplyCompletion(done,t));Check(s.Current(t)=="done");
            Check(!s.ApplyCompletion(done,t.AddSeconds(29)));Check(s.Current(t.AddSeconds(29))=="done");Check(s.Current(t.AddSeconds(30))=="idle");
            s.Apply(new LinkEvent{session_id="test",turn_id="2",hook_event_name="UserPromptSubmit"},t);
            Check(!s.ApplyCompletion(done,t));Check(s.Current(t)=="working");
            Check(CompletionReader.Parse("{\"type\":\"response_item\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"2\"}}","test")==null);
            Check(CompletionReader.Parse("{bad}","test")==null);
            var root=Environment.GetEnvironmentVariable("CODEX_HOME");
            if(string.IsNullOrEmpty(root))root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            var path=Path.Combine(root,"sessions","gpulcd-test-"+Guid.NewGuid().ToString("N")+".jsonl");
            try {
                File.WriteAllText(path,Record("1")+"\n");var r=new CompletionReader();
                var unknown=new LinkEvent{session_id="untracked",turn_id="x",hook_event_name="UserPromptSubmit"};
                Check(!r.Track(unknown));
                Check(!r.Track(new LinkEvent{session_id="untracked",transcript_path=Path.Combine(Path.GetTempPath(),"outside.jsonl")}));
                Check(r.Track(new LinkEvent{session_id="test",transcript_path=path}));
                Check(r.Track(new LinkEvent{session_id="test",turn_id="2",hook_event_name="Stop"}));
                Check(r.Poll().Count==1);Check(r.Poll().Count==0);
                var line=Record("2");File.AppendAllText(path,line.Substring(0,20));Check(r.Poll().Count==0);
                File.AppendAllText(path,line.Substring(20)+"\n");var events=r.Poll();Check(events.Count==1 && events[0].turn_id=="2");
                Check(s.ApplyCompletion(events[0],t));Check(s.Current(t)=="done");
                // An untrackable second session must not mask the tracked task's completion.
                if(r.Track(unknown))s.Apply(unknown,t);
                Check(s.Current(t)=="done");Check(s.Current(t.AddSeconds(30))=="idle");
                Check(!r.Track(new LinkEvent{client="asst",session_id="asst"}));
            }finally{if(File.Exists(path))File.Delete(path);}
            Console.WriteLine(count+" completion recovery assertions passed.");
        }
    }
}

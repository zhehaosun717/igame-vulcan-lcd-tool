using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace GpuLcd {
    static class Hook {
        // Notification only. Never authorize, reject, or add model context.
        static int Main(string[] args) {
            bool stop=false;
            try {
                string raw = Console.In.ReadToEnd();
                if (raw.Length > 1024 * 1024) return 0;
                var j = new JavaScriptSerializer();
                var input = j.DeserializeObject(raw) as System.Collections.Generic.Dictionary<string, object>;
                if (input == null) return 0;
                Func<string,string> get = k => input.ContainsKey(k) ? Convert.ToString(input[k]) : "";
                stop=get("hook_event_name")=="Stop";
                try {
                    var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"GpuLcdTool");
                    Directory.CreateDirectory(dir);
                    var log=Path.Combine(dir,"hook-events.jsonl");
                    if(File.Exists(log) && new FileInfo(log).Length>1024*1024)File.WriteAllText(log,"");
                    File.AppendAllText(log,j.Serialize(new {utc=DateTime.UtcNow.ToString("o"),evt=get("hook_event_name"),session=get("session_id"),turn=get("turn_id"),transcript=get("transcript_path")})+Environment.NewLine);
                } catch { }
                var e = new LinkEvent { hook_event_name = get("hook_event_name"),
                    session_id = get("session_id"), turn_id = get("turn_id"),
                    transcript_path = get("transcript_path") };
                e.client = args.Length>0 && args[0]=="claude" ? "claude" : "codex";
                if(e.client=="claude")e.hook_event_name=LinkState.ClaudeEventName(e.hook_event_name,get("notification_type"));
                if (e.session_id.Length > 200 || e.turn_id.Length > 200) return 0;
                using (var p = new NamedPipeClientStream(".", "GpuLcdEvents", PipeDirection.InOut)) {
                    p.Connect(400);
                    var w = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true };
                    var r = new StreamReader(p, Encoding.UTF8);
                    w.WriteLine(j.Serialize(e));
                    var read = r.ReadLineAsync();
                    read.Wait(400);
                }
            } catch { } // A missing screen must never block Codex.
            if(stop)Console.WriteLine("{}");
            return 0;
        }
    }
}

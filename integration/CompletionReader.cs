using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace GpuLcd {
    // Read only completion metadata from transcripts explicitly supplied by local hooks.
    public sealed class CompletionReader {
        sealed class Watch { public string Session, Path, Pending=""; public long Offset=-1; public DateTime Seen; }
        readonly Dictionary<string,Watch> watches=new Dictionary<string,Watch>();
        readonly object gate=new object();
        public bool Track(LinkEvent e) {
            if(e.client!="codex" || string.IsNullOrEmpty(e.session_id))return false;
            // Some internal sessions have neither a transcript nor an end hook.
            // Admit missing-path events only after the session has been registered.
            if(string.IsNullOrEmpty(e.transcript_path)) {
                lock(gate) {
                    Watch known;if(!watches.TryGetValue(e.session_id,out known))return false;
                    known.Seen=DateTime.UtcNow;return true;
                }
            }
            string path=e.transcript_path;
            if(path.StartsWith(@"\\?\"))path=path.Substring(4);
            try {
                path=System.IO.Path.GetFullPath(path);
                var home=Environment.GetEnvironmentVariable("CODEX_HOME");
                if(string.IsNullOrEmpty(home))home=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
                var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(home,"sessions"))+System.IO.Path.DirectorySeparatorChar;
                if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase))return false;
            }catch{return false;}
            lock(gate) {
                Watch w;
                if(!watches.TryGetValue(e.session_id,out w) || w.Path!=path)watches[e.session_id]=w=new Watch{Session=e.session_id,Path=path};
                w.Seen=DateTime.UtcNow;
                return true;
            }
        }
        public static LinkEvent Parse(string line,string session) {
            // Do not deserialize conversation or tool payloads.
            if(!line.Contains("\"event_msg\"") || (!line.Contains("\"task_complete\"") && !line.Contains("\"turn_aborted\"")))return null;
            try {
                var j=new JavaScriptSerializer{MaxJsonLength=2*1024*1024};
                var o=j.DeserializeObject(line) as Dictionary<string,object>;
                if(o==null || !o.ContainsKey("type") || Convert.ToString(o["type"])!="event_msg")return null;
                object value;if(!o.TryGetValue("payload",out value))return null;
                var p=value as Dictionary<string,object>;
                if(p==null || !p.ContainsKey("type") || !p.ContainsKey("turn_id"))return null;
                var type=Convert.ToString(p["type"]);var turn=Convert.ToString(p["turn_id"]);
                if((type!="task_complete" && type!="turn_aborted") || string.IsNullOrEmpty(turn))return null;
                return new LinkEvent{client="codex",session_id=session,turn_id=turn,hook_event_name=type=="task_complete"?"Stop":"Interrupt"};
            }catch{return null;}
        }
        public List<LinkEvent> Poll() {
            var result=new List<LinkEvent>();
            lock(gate) {
                var expired=new List<string>();
                foreach(var pair in watches) {
                    var w=pair.Value;
                    if((DateTime.UtcNow-w.Seen).TotalMinutes>31){expired.Add(pair.Key);continue;}
                    try {
                        using(var f=new FileStream(w.Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                            bool skip=false;
                            if(w.Offset<0 || w.Offset>f.Length){w.Offset=Math.Max(0,f.Length-262144);w.Pending="";skip=w.Offset>0;}
                            f.Position=w.Offset;
                            // Bound each read so long tool output cannot stall the UI or allocate without limit.
                            var bytes=new byte[(int)Math.Min(262144,f.Length-w.Offset)];
                            int n=f.Read(bytes,0,bytes.Length);w.Offset+=n;
                            var text=w.Pending+Encoding.UTF8.GetString(bytes,0,n);
                            var lines=text.Split('\n');
                            for(int i=skip?1:0;i<lines.Length-1;i++) {
                                var e=Parse(lines[i],w.Session);if(e!=null)result.Add(e);
                            }
                            w.Pending=lines[lines.Length-1];
                            if(w.Pending.Length>2*1024*1024)w.Pending="";
                        }
                    }catch(IOException){}catch(UnauthorizedAccessException){}
                }
                foreach(var key in expired)watches.Remove(key);
            }
            return result;
        }
    }
}

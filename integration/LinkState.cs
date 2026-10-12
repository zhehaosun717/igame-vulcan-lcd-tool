using System;
using System.Collections.Generic;
using System.Linq;

namespace GpuLcd {
    // Pure state machine: no device I/O, UI or user prompt storage.
    public sealed class LinkEvent {
        public string hook_event_name = "", session_id = "", turn_id = "";
        public string client = "codex";
        public string transcript_path = "";
    }
    public sealed class LinkState {
        sealed class Entry {
            public string Turn, State, Client;
            public DateTime Updated;
            public bool Closed;
        }
        readonly Dictionary<string, Entry> sessions = new Dictionary<string, Entry>();
        public void Clear() { sessions.Clear(); }
        public void ClearClient(string client) {
            foreach(var k in sessions.Where(kv=>kv.Value.Client==client).Select(kv=>kv.Key).ToArray()) sessions.Remove(k);
        }
        public string CurrentClient = "codex";
        // When two clients are in the same state (both working), the one shown stays at least this long, so
        // alternating tool events do not flip the LCD animation and the lights every few seconds.
        public const int ClientHoldSeconds = 15;
        DateTime clientSince = DateTime.MinValue;
        public object[] Snapshot() {
            return sessions.Select(kv => (object)new {session=kv.Key,turn=kv.Value.Turn,state=kv.Value.State,closed=kv.Value.Closed}).ToArray();
        }
        public bool ApplyCompletion(LinkEvent e,DateTime now) {
            Entry p;
            if(e.client!="codex" || !sessions.TryGetValue("codex:"+e.session_id,out p) ||
                p.Closed || p.Turn!=e.turn_id || string.IsNullOrEmpty(e.turn_id))return false;
            Apply(e,now);return true;
        }
        public void Apply(LinkEvent e, DateTime now) {
            if (string.IsNullOrEmpty(e.session_id)) return;
            string client = e.client == "claude" ? "claude" : "codex";
            string key = client + ":" + e.session_id;
            Entry p;
            sessions.TryGetValue(key, out p);
            string evt = e.hook_event_name;
            if (evt == "SessionEnd" || evt == "Interrupt") {
                if (p != null && (string.IsNullOrEmpty(e.turn_id) || e.turn_id == p.Turn))
                    sessions.Remove(key);
                return;
            }
            bool start = evt == "UserPromptSubmit";
            if (evt != "PreToolUse" && evt != "PostToolUse" &&
                evt != "PermissionRequest" && evt != "Stop" && !start) return;
            if (p != null && e.turn_id != p.Turn && !start) return;
            if (p != null && p.Closed && e.turn_id == p.Turn && !start) return;
            if (p == null || start) p = new Entry { Turn = e.turn_id };
            p.Client = client;
            p.State = evt == "PermissionRequest" ? "waiting" : evt == "Stop" ? "done" : "working";
            p.Closed = evt == "Stop";
            p.Updated = now;
            sessions[key] = p;
        }
        public string Current(DateTime now) {
            foreach (var k in sessions.Where(kv => (now - kv.Value.Updated).TotalMinutes > 30).Select(kv => kv.Key).ToArray())
                sessions.Remove(k);
            foreach(string state in new[]{"waiting","working","done"}) {
                var candidates = sessions.Values.Where(x => x.State == state &&
                    (state != "done" || (now - x.Updated).TotalSeconds < (x.Client=="claude" ? 120 : 30)))
                    .OrderByDescending(x=>x.Updated).ToList();
                if(candidates.Count==0)continue;
                string client = candidates[0].Client;
                bool held = client!=CurrentClient && (now-clientSince).TotalSeconds < ClientHoldSeconds &&
                    candidates.Any(x => x.Client==CurrentClient);
                if(!held && client!=CurrentClient){CurrentClient=client;clientSince=now;}
                return state;
            }
            return "idle";
        }
    }
}

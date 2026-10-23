using System;
using System.Collections.Generic;
using System.Linq;

namespace GpuLcd {
    // Pure state machine: no device I/O, UI or user prompt storage.
    public sealed class LinkEvent {
        public string hook_event_name = "", session_id = "", turn_id = "";
        public string client = "codex";
        public string transcript_path = "";
        public string state = ""; // DSH start-only initial state; legacy events may omit it.
    }
    // Wire mapping and acknowledgements are pure: tests never need a live SignalRGB process.
    public static class SignalProtocol {
        public static string TaskEvent(string state, string client) {
            if (state == "idle") return "idle";
            if (!LinkState.KnownClient(client) || (state != "working" && state != "waiting" && state != "done")) return null;
            return client == "codex" ? state : client + "@" + state;
        }
        public static string Event(string state, string client, bool night, bool wake, long idleEpoch, int offset, decimal rate) {
            if (night) return "sleep";
            if (wake) return "wake";
            if (state == "idle" && idleEpoch > 0) {
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                return "idle@" + idleEpoch.ToString(culture) + "@" + offset.ToString(culture) + "@" +
                    Math.Max(95m, Math.Min(105m, rate)).ToString("0.00", culture);
            }
            return TaskEvent(state, client) ?? "idle";
        }
        public static bool LatestAck(string logs, out string marker, out string state, out string client) {
            marker = state = client = "";
            var matches = System.Text.RegularExpressions.Regex.Matches(logs ?? "",
                @"GpuLcdState=(idle|working|waiting|done);seq=[0-9]+(?:;client=(?>([^\s;,""\\}\]]*)))?(?![A-Za-z0-9_-]|;client=)");
            foreach (System.Text.RegularExpressions.Match match in matches) {
                string source = match.Groups[2].Success ? match.Groups[2].Value : "codex";
                if (!LinkState.KnownClient(source)) continue;
                marker = match.Value; state = match.Groups[1].Value; client = source;
            }
            return marker != "";
        }
    }
    public sealed class LinkState {
        sealed class Entry {
            public string Turn, State, Client;
            public DateTime Updated;
            public bool Closed;
        }
        readonly Dictionary<string, Entry> sessions = new Dictionary<string, Entry>();
        public void Clear() { sessions.Clear(); clientSince = DateTime.MinValue; }
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
        // Claude Code's Notification hook is not only for permission prompts: "idle_prompt" fires about a minute
        // after a finished turn. It must not turn that session back into "waiting"; it means the turn is over, which
        // also closes a session whose Stop event was lost. Other types (e.g. auth_success) are ignored.
        // Older versions send no type: every notification was a prompt.
        public static string ClaudeEventName(string hookEventName, string notificationType) {
            if (hookEventName != "Notification") return hookEventName;
            if (string.IsNullOrEmpty(notificationType) || notificationType == "permission_prompt" || notificationType == "elicitation_dialog")
                return "PermissionRequest";
            return notificationType == "idle_prompt" ? "Stop" : hookEventName;
        }
        public static bool KnownClient(string client) { return client == "codex" || client == "claude" || client == "dsh"; }
        public static bool ClientEnabled(string client, bool codex, bool claude, bool dsh) {
            return client == "codex" ? codex : client == "claude" ? claude : client == "dsh" && dsh;
        }
        public void Apply(LinkEvent e, DateTime now) {
            if (e == null || !KnownClient(e.client) || string.IsNullOrEmpty(e.session_id) || e.session_id.Length > 200) return;
            string client = e.client;
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
            bool dshStart = client == "dsh" && start;
            if (dshStart && !string.IsNullOrEmpty(e.state) && e.state != "working" && e.state != "waiting") return;
            bool waitingStart = dshStart && e.state == "waiting";
            if (evt != "PreToolUse" && evt != "PostToolUse" &&
                evt != "PermissionRequest" && evt != "Stop" && !start) return;
            if (p != null && e.turn_id != p.Turn && !start) return;
            if (p != null && p.Closed && e.turn_id == p.Turn && !start) return;
            if (p == null || start) p = new Entry { Turn = e.turn_id };
            p.Client = client;
            p.State = evt == "PermissionRequest" || waitingStart ? "waiting" : evt == "Stop" ? "done" : "working";
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
                if(!held && (client!=CurrentClient || clientSince==DateTime.MinValue)){CurrentClient=client;clientSince=now;}
                return state;
            }
            clientSince = DateTime.MinValue;
            return "idle";
        }
    }
}

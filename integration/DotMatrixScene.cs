using System;
using System.Collections.Generic;

namespace GpuLcd {
    // Pure mapping from the merged assistant state to a Dot Matrix Studio scene: no I/O.
    // Claude Code keeps the colours of its LCD animations and SignalRGB effect; Codex uses the panel's own status colours.
    public static class DotMatrixScene {
        static readonly Dictionary<string, string> ClaudeColors = new Dictionary<string, string> {
            { "working", "#D97757" }, { "waiting", "#F0B43C" }, { "done", "#78C878" }
        };

        // Identifies what is on the panel, so a scene is only sent when it changes.
        public static string Key(string state, string client, bool night) {
            if (night) return "off";
            if (!IsActive(state)) return "idle";
            return state + "/" + (client == "claude" ? "claude" : "codex");
        }

        // The JSON body for POST /api/scene. Optional fields are left out (the panel rejects nulls).
        public static Dictionary<string, object> For(string state, string client, bool night) {
            if (night) return new Dictionary<string, object> {
                { "kind", "pixels" }, { "frames", new[] { new string[0] } }, { "palette", new Dictionary<string, string>() }
            };
            if (!IsActive(state)) return new Dictionary<string, object> { { "kind", "status" }, { "status", "idle" } };
            bool claude = client == "claude";
            var scene = new Dictionary<string, object> {
                { "kind", "status" }, { "status", state }, { "label", claude ? "Claude" : "Codex" }
            };
            if (claude) scene["color"] = ClaudeColors[state];
            return scene;
        }

        // Whether the panel still shows the scene we sent. Colour is not compared: older Dot Matrix Studio
        // versions drop it, which would otherwise look like a mismatch forever.
        public static bool Matches(Dictionary<string, object> sent, Dictionary<string, object> shown) {
            if (shown == null) return false;
            foreach (var field in new[] { "kind", "status", "label" }) {
                object a, b;
                bool hasA = sent.TryGetValue(field, out a), hasB = shown.TryGetValue(field, out b);
                if (hasA != hasB || (hasA && Convert.ToString(a) != Convert.ToString(b))) return false;
            }
            if (Convert.ToString(sent["kind"]) != "pixels") return true;
            // Our only pixels scene is the blank panel; an agent's own pixel art is not it.
            object frames;
            var list = shown.TryGetValue("frames", out frames) ? frames as object[] : null;
            return list != null && list.Length == 1 && list[0] is object[] && ((object[])list[0]).Length == 0;
        }

        static bool IsActive(string state) { return state == "working" || state == "waiting" || state == "done"; }
    }
}

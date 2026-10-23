using System;
using System.Collections.Generic;

namespace GpuLcd {
    // Pure mapping from the merged assistant state to a Dot Matrix Studio scene: no I/O.
    // The panel tells the assistants apart by colour alone (no text label): Claude Code keeps the warm colours of
    // its LCD animations and SignalRGB effect, Codex gets a cool set, so the two differ in every state.
    public static class DotMatrixScene {
        static readonly Dictionary<string, string> ClaudeColors = new Dictionary<string, string> {
            { "working", "#D97757" }, { "waiting", "#F0B43C" }, { "done", "#78C878" }
        };
        static readonly Dictionary<string, string> CodexColors = new Dictionary<string, string> {
            { "working", "#3C82FF" }, { "waiting", "#B478FF" }, { "done", "#3CDCDC" }
        };
        // DeepSeek Harness: deep blue working, amber waiting, teal done. Distinct from both warm Claude and cool Codex.
        static readonly Dictionary<string, string> DshColors = new Dictionary<string, string> {
            { "working", "#4D6BFE" }, { "waiting", "#FF9F45" }, { "done", "#2ED3B7" }
        };

        // Identifies what is on the panel, so a scene is only sent when it changes.
        public static string Key(string state, string client, bool night) {
            if (night) return "off";
            if (!IsActive(state)) return "idle";
            return state + "/" + ClientKey(client);
        }

        // The JSON body for POST /api/scene. Optional fields are left out (the panel rejects nulls).
        public static Dictionary<string, object> For(string state, string client, bool night) {
            if (night) return new Dictionary<string, object> {
                { "kind", "pixels" }, { "frames", new[] { new string[0] } }, { "palette", new Dictionary<string, string>() }
            };
            if (!IsActive(state)) return new Dictionary<string, object> { { "kind", "status" }, { "status", "idle" } };
            var colors = client == "claude" ? ClaudeColors : client == "dsh" ? DshColors : CodexColors;
            return new Dictionary<string, object> { { "kind", "status" }, { "status", state }, { "color", colors[state] } };
        }

        // Whether the panel still shows the scene we sent. A missing colour still matches: older Dot Matrix Studio
        // versions drop it, which would otherwise look like a mismatch forever. A different colour is the other assistant.
        public static bool Matches(Dictionary<string, object> sent, Dictionary<string, object> shown) {
            if (shown == null) return false;
            foreach (var field in new[] { "kind", "status", "label" }) {
                object a, b;
                bool hasA = sent.TryGetValue(field, out a), hasB = shown.TryGetValue(field, out b);
                if (hasA != hasB || (hasA && Convert.ToString(a) != Convert.ToString(b))) return false;
            }
            object sentColor, shownColor;
            if (sent.TryGetValue("color", out sentColor) && shown.TryGetValue("color", out shownColor) &&
                !string.Equals(Convert.ToString(sentColor).TrimStart('#'), Convert.ToString(shownColor).TrimStart('#'), StringComparison.OrdinalIgnoreCase))
                return false;
            if (Convert.ToString(sent["kind"]) != "pixels") return true;
            // Our only pixels scene is the blank panel; an agent's own pixel art is not it.
            object frames;
            var list = shown.TryGetValue("frames", out frames) ? frames as object[] : null;
            return list != null && list.Length == 1 && list[0] is object[] && ((object[])list[0]).Length == 0;
        }

        static string ClientKey(string client) { return client == "claude" ? "claude" : client == "dsh" ? "dsh" : "codex"; }
        static bool IsActive(string state) { return state == "working" || state == "waiting" || state == "done"; }
    }
}

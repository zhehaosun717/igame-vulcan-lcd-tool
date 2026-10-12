using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace GpuLcd {
    // Scene mapping, agent-api.json parsing and the HTTP call against a fake Dot Matrix Studio API.
    static class DotMatrixTests {
        static int count;
        static void Equal(object expected, object actual, string what) {
            if (!Equals(expected, actual)) throw new Exception(what + ": expected " + expected + ", got " + actual);
            count++;
        }
        static void True(bool condition, string what) { if (!condition) throw new Exception(what); count++; }
        static string Json(object value) { return new JavaScriptSerializer().Serialize(value); }

        // One request, one canned response; records what arrived.
        sealed class FakePanel {
            public string RequestLine = "", Body = "";
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int Port;
            readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            readonly Thread thread;
            public FakePanel(int status, string response) {
                listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                thread = new Thread(() => {
                    using (var client = listener.AcceptTcpClient())
                    using (var stream = client.GetStream()) {
                        var reader = new StreamReader(stream, Encoding.UTF8);
                        RequestLine = reader.ReadLine();
                        string line;
                        while (!string.IsNullOrEmpty(line = reader.ReadLine())) {
                            int colon = line.IndexOf(':');
                            Headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                        }
                        string length;
                        var body = new char[Headers.TryGetValue("Content-Length", out length) ? int.Parse(length) : 0];
                        int read = 0; while (read < body.Length) read += reader.Read(body, read, body.Length - read);
                        Body = new string(body);
                        var bytes = Encoding.UTF8.GetBytes(response);
                        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " X\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                        stream.Write(head, 0, head.Length); stream.Write(bytes, 0, bytes.Length);
                    }
                    listener.Stop();
                });
                thread.IsBackground = true; thread.Start();
            }
            public void Wait() { thread.Join(3000); }
        }

        static void Main() {
            // Keys: one per distinct picture, so the bridge only sends on change.
            Equal("off", DotMatrixScene.Key("working", "claude", true), "night wins");
            Equal("idle", DotMatrixScene.Key("idle", "claude", false), "idle ignores client");
            Equal("idle", DotMatrixScene.Key("bogus", "codex", false), "unknown state is idle");
            Equal("working/claude", DotMatrixScene.Key("working", "claude", false), "claude key");
            Equal("done/codex", DotMatrixScene.Key("done", "asst", false), "other clients count as codex");

            // Scenes: JSON the panel's validator accepts (no nulls).
            Equal("{\"kind\":\"pixels\",\"frames\":[[]],\"palette\":{}}", Json(DotMatrixScene.For("working", "claude", true)), "night blanks the panel");
            Equal("{\"kind\":\"status\",\"status\":\"idle\"}", Json(DotMatrixScene.For("idle", "codex", false)), "idle scene");
            Equal("{\"kind\":\"status\",\"status\":\"working\",\"label\":\"Codex\"}", Json(DotMatrixScene.For("working", "codex", false)), "codex keeps panel colours");
            Equal("{\"kind\":\"status\",\"status\":\"waiting\",\"label\":\"Claude\",\"color\":\"#F0B43C\"}", Json(DotMatrixScene.For("waiting", "claude", false)), "claude waiting amber");
            Equal("#D97757", DotMatrixScene.For("working", "claude", false)["color"], "claude working terracotta");
            Equal("#78C878", DotMatrixScene.For("done", "claude", false)["color"], "claude done green");
            foreach (var state in new[] { "idle", "working", "waiting", "done" })
                foreach (var client in new[] { "codex", "claude" })
                    True(!Json(DotMatrixScene.For(state, client, false)).Contains("null"), "no nulls for " + state + "/" + client);

            // agent-api.json
            var ep = DotMatrixClient.ParseEndpoint("{\"port\":47321,\"token\":\"abc\",\"executable\":\"x\",\"args\":[],\"pid\":42}");
            Equal(47321, ep.Port, "port"); Equal("abc", ep.Token, "token"); Equal(42, ep.Pid, "pid");
            Equal("47321|abc|42", ep.Signature, "signature changes when the app restarts");
            Equal(0, DotMatrixClient.ParseEndpoint("{\"port\":1,\"token\":\"t\"}").Pid, "pid is optional");
            True(DotMatrixClient.ParseEndpoint("{\"token\":\"abc\"}") == null, "missing port");
            True(DotMatrixClient.ParseEndpoint("{\"port\":70000,\"token\":\"abc\"}") == null, "port out of range");
            True(DotMatrixClient.ParseEndpoint("{\"port\":47321,\"token\":\"\"}") == null, "empty token");
            True(DotMatrixClient.ParseEndpoint("{\"port\":47321,\"token\":5}") == null, "token must be text");
            True(DotMatrixClient.ParseEndpoint("[]") == null, "not an object");

            // HTTP: token header, hook source, body, shown flag.
            var panel = new FakePanel(200, "{\"success\":true,\"data\":{\"shown\":true,\"kind\":\"status\"},\"error\":null}");
            var scene = DotMatrixScene.For("working", "claude", false);
            True(DotMatrixClient.PostScene(new DotMatrixEndpoint { Port = panel.Port, Token = "secret" }, scene, 1500), "shown");
            panel.Wait();
            Equal("POST /api/scene?source=hook HTTP/1.1", panel.RequestLine, "request line");
            Equal("secret", panel.Headers["x-matrix-token"], "token header");
            Equal("application/json", panel.Headers["Content-Type"], "content type");
            Equal(Json(scene), panel.Body, "body is the scene");

            panel = new FakePanel(200, "{\"success\":true,\"data\":{\"shown\":false,\"kind\":\"status\"},\"error\":null}");
            True(!DotMatrixClient.PostScene(new DotMatrixEndpoint { Port = panel.Port, Token = "t" }, scene, 1500), "held by an agent scene");
            panel.Wait();

            panel = new FakePanel(401, "{\"success\":false,\"data\":null,\"error\":\"missing or invalid token\"}");
            try { DotMatrixClient.PostScene(new DotMatrixEndpoint { Port = panel.Port, Token = "bad" }, scene, 1500); throw new Exception("401 must throw"); }
            catch (WebException) { count++; }
            panel.Wait();

            panel = new FakePanel(200, "{\"success\":true}");
            try { DotMatrixClient.PostScene(new DotMatrixEndpoint { Port = panel.Port, Token = "t" }, scene, 1500); throw new Exception("missing data must throw"); }
            catch (IOException) { count++; }
            panel.Wait();

            // Is the panel still showing ours? (scene as returned by /api/state, parsed the same way)
            var parse = new JavaScriptSerializer();
            Func<string, Dictionary<string, object>> S = text => parse.DeserializeObject(text) as Dictionary<string, object>;
            var claudeWorking = DotMatrixScene.For("working", "claude", false);
            True(DotMatrixScene.Matches(claudeWorking, S(Json(claudeWorking))), "same scene matches");
            True(DotMatrixScene.Matches(claudeWorking, S("{\"kind\":\"status\",\"status\":\"working\",\"label\":\"Claude\"}")), "colour dropped by an older panel app still matches");
            True(!DotMatrixScene.Matches(claudeWorking, S("{\"kind\":\"status\",\"status\":\"thinking\"}")), "another sender's status");
            True(!DotMatrixScene.Matches(claudeWorking, S("{\"kind\":\"status\",\"status\":\"working\",\"label\":\"Codex\"}")), "other client");
            True(!DotMatrixScene.Matches(claudeWorking, S("{\"kind\":\"mood\",\"mood\":\"happy\"}")), "an agent's mood");
            True(!DotMatrixScene.Matches(claudeWorking, null), "nothing shown (app restarted)");
            var idle = DotMatrixScene.For("idle", "codex", false);
            True(DotMatrixScene.Matches(idle, S("{\"kind\":\"status\",\"status\":\"idle\"}")), "idle matches");
            True(!DotMatrixScene.Matches(idle, S("{\"kind\":\"status\",\"status\":\"idle\",\"label\":\"x\"}")), "a label we did not send");
            var off = DotMatrixScene.For("idle", "codex", true);
            True(DotMatrixScene.Matches(off, S(Json(off))), "blank panel matches");
            True(!DotMatrixScene.Matches(off, S("{\"kind\":\"pixels\",\"frames\":[[\"rr\"]],\"palette\":{\"r\":\"#FF0000\"}}")), "an agent's pixel art is not the blank panel");

            panel = new FakePanel(200, "{\"success\":true,\"data\":{\"connected\":true,\"live\":\"agent\",\"scene\":{\"kind\":\"status\",\"status\":\"done\"}},\"error\":null}");
            var current = DotMatrixClient.GetScene(new DotMatrixEndpoint { Port = panel.Port, Token = "secret" }, 1500);
            panel.Wait();
            Equal("GET /api/state HTTP/1.1", panel.RequestLine, "state request line");
            Equal("secret", panel.Headers["x-matrix-token"], "state token header");
            Equal("done", current["status"], "current scene");
            panel = new FakePanel(200, "{\"success\":true,\"data\":{\"connected\":true,\"scene\":null},\"error\":null}");
            True(DotMatrixClient.GetScene(new DotMatrixEndpoint { Port = panel.Port, Token = "t" }, 1500) == null, "no scene yet");
            panel.Wait();

            Console.WriteLine(count + " dot-matrix assertions passed.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GpuLcd {
    partial class MainForm {
        // Third output of the merged assistant state, next to the LCD and SignalRGB: the iDotMatrix panel
        // driven by Dot Matrix Studio. Only HTTP to 127.0.0.1; never touches this tool's serial port.
        // All file, process and network work runs off the UI thread; results come back through BeginInvoke.
        const int DotRetrySeconds = 2, DotInfoSeconds = 5, DotVerifySeconds = 15, DotTimeoutMs = 1500, DotExitTimeoutMs = 800;
        CheckBox chkDotMatrix;
        readonly Label dotStatus = new Label { Text = "点阵屏：等待" };
        string dotApplied = "";
        bool dotBusy;
        DateTime lastDotTry, lastDotInfoRead, lastDotVerify;
        DotMatrixEndpoint dotEndpoint;
        string dotProblem = "Dot Matrix Studio 未运行";

        static string DotMatrixInfoPath {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dot Matrix Studio", "agent-api.json"); }
        }

        // Dot Matrix Studio's endpoint from agent-api.json, or null with the reason in problem. Reads a file: background only.
        // Whether the app really runs shows up when the POST fails; the pid only tells a restarted app apart.
        static DotMatrixEndpoint FindDotMatrix(out string problem) {
            problem = "";
            try {
                if (!File.Exists(DotMatrixInfoPath)) { problem = "没找到 " + DotMatrixInfoPath + "（Dot Matrix Studio 未运行过）"; return null; }
                var endpoint = DotMatrixClient.ParseEndpoint(File.ReadAllText(DotMatrixInfoPath));
                if (endpoint == null) problem = "agent-api.json 内容无效";
                return endpoint;
            } catch (Exception ex) { problem = "读取 agent-api.json 失败：" + ex.Message; return null; }
        }

        static string DescribeDotKey(string key) {
            if (key == "off") return "熄灭";
            if (key == "idle") return "空闲";
            var parts = key.Split('/');
            string state = parts[0] == "working" ? "工作中" : parts[0] == "waiting" ? "等待授权" : "本轮结束";
            return (parts.Length > 1 && parts[1] == "claude" ? "Claude Code / " : "Codex / ") + state;
        }

        void AddDotMatrixControls(FlowLayoutPanel flow) {
            var dot = new CheckBox { Text = "同步到 Dot Matrix Studio 点阵屏", AutoSize = true, Checked = linkCfg.DotMatrix };
            dot.CheckedChanged += (s, e) => {
                linkCfg.DotMatrix = dot.Checked; SaveLinks();
                if (!dot.Checked) ReleaseDotMatrix(false);
                dotApplied = ""; TickLinks();
            };
            chkDotMatrix = dot; flow.Controls.Add(dot);
        }

        // Leaves the panel neutral when we stop driving it (switch turned off, or the tool exits):
        // idle normally, dark during night mode, so it never keeps saying "working".
        void ReleaseDotMatrix(bool waitForIt) {
            var target = dotEndpoint;
            if (target == null || dotApplied == "") return;
            var scene = DotMatrixScene.For("idle", "", linkCfg.NightMode);
            var send = Task.Run(() => { try { DotMatrixClient.PostScene(target, scene, DotExitTimeoutMs); } catch { } });
            if (waitForIt) send.Wait(DotExitTimeoutMs + 200);
        }

        // Called from TickLinks once a second. Sends a scene when what the panel should show changes, again after
        // Dot Matrix Studio restarts (new pid or token), and when a check every 15 s finds the panel showing
        // something else (another sender, or an agent's own scene that has had its hold time).
        void TickDotMatrix(string state, string client, DateTime now) {
            if (!linkCfg.DotMatrix) { dotStatus.Text = "点阵屏：同步未开启"; return; }
            if (dotBusy) return;
            bool night = linkCfg.NightMode;
            string key = DotMatrixScene.Key(state, client, night);
            bool refresh = (now - lastDotInfoRead).TotalSeconds >= DotInfoSeconds;
            bool send = key != dotApplied && (now - lastDotTry).TotalSeconds >= DotRetrySeconds;
            bool verify = key == dotApplied && (now - lastDotVerify).TotalSeconds >= DotVerifySeconds;
            if (!refresh && !send && !verify) return;
            if (!refresh && dotEndpoint == null) { dotStatus.Text = "点阵屏：" + dotProblem; return; }

            dotBusy = true;
            if (refresh) lastDotInfoRead = now;
            if (send) lastDotTry = now;
            if (verify) lastDotVerify = now;
            var known = dotEndpoint;
            string knownSig = known == null ? "" : known.Signature;
            var scene = DotMatrixScene.For(state, client, night);
            Task.Run(() => {
                string problem = "";
                var target = refresh ? FindDotMatrix(out problem) : known;
                string sig = target == null ? "" : target.Signature;
                // A restarted app has a new signature: it shows nothing of ours, so send even if the key is unchanged.
                bool restarted = refresh && sig != knownSig;
                bool shown = false, mismatch = false, sent = false; string result = "";
                if (target != null && (send || restarted)) {
                    sent = true;
                    try { shown = DotMatrixClient.PostScene(target, scene, DotTimeoutMs); result = shown ? "已显示" : "agent 画面占用中，稍后重发"; }
                    catch (Exception ex) { result = "发送失败：" + ex.Message; }
                } else if (target != null && verify) {
                    try { mismatch = !DotMatrixScene.Matches(scene, DotMatrixClient.GetScene(target, DotTimeoutMs)); }
                    catch (Exception ex) { result = "读取状态失败：" + ex.Message; }
                }
                try {
                    BeginInvoke(new Action(() => {
                        dotBusy = false;
                        if (refresh) { dotEndpoint = target; dotProblem = problem; }
                        if (target == null) { dotApplied = ""; dotStatus.Text = "点阵屏：" + dotProblem; return; }
                        if (sent) {
                            dotApplied = shown ? key : "";
                            dotStatus.Text = "点阵屏：" + DescribeDotKey(key) + "　" + result;
                        } else if (mismatch) {
                            dotApplied = ""; lastDotTry = DateTime.MinValue; // shows something else now: send ours again
                        } else if (result != "") dotStatus.Text = "点阵屏：" + DescribeDotKey(key) + "　" + result;
                    }));
                } catch { }
            });
        }
    }
}

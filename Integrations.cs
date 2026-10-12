using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GpuLcd {
    class LinkConfig {
        public bool Codex = true, Claude = true, Signal = false;
        public int IdleOffsetMs = 0;
        public decimal IdleRatePercent = 100m;
        public bool NightMode = false;
    }
    partial class MainForm {
        readonly LinkState linkState = new LinkState();
        readonly CompletionReader completions = new CompletionReader();
        bool completionBusy;
        readonly System.Windows.Forms.Timer linkTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        LinkConfig linkCfg = new LinkConfig();
        CheckBox chkCodex, chkClaude, chkSignal; NumericUpDown nudOffset, nudRate;   // 新界面通过这些控件驱动原有处理逻辑
        Label linkStatus = new Label(), signalStatus = new Label();
        string linkShown = "idle", linkApplied = "idle", linkAppliedClient = "codex", signalApplied = "";
        string demoState = ""; DateTime demoUntil, lastLcdTry, lastSignalTry;
        bool signalBusy;
        long idlePlaybackEpoch;
        int receivedHookEvents; string lastHookEvent="";
        static string LinkPath { get { return Path.Combine(Paths.Data, "integration.json"); } }

        TabPage TabLinks() {
            try { if (File.Exists(LinkPath)) linkCfg = new JavaScriptSerializer().Deserialize<LinkConfig>(File.ReadAllText(LinkPath)); } catch { }
            var tab = new TabPage("联动") { AutoScroll = true };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
            flow.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(500,0),
                Text = "Codex：蓝色工作 / 橙色等待 / 翠绿呼吸完成\r\nClaude Code：陶橙工作 / 琥珀呼吸等待 / 草绿渐亮完成\r\nCodex 结束提示 30 秒，Claude Code 120 秒，然后恢复默认动画。", Margin = new Padding(0,0,0,14) });
            var codex = new CheckBox { Text = "接收 Codex 任务事件", AutoSize = true, Checked = linkCfg.Codex };
            codex.CheckedChanged += (s,e) => { linkCfg.Codex = codex.Checked; if (!codex.Checked) linkState.ClearClient("codex"); SaveLinks(); TickLinks(); };
            chkCodex = codex; flow.Controls.Add(codex);
            var claude = new CheckBox { Text = "接收 Claude Code 任务事件", AutoSize = true, Checked = linkCfg.Claude };
            claude.CheckedChanged += (s,e) => { linkCfg.Claude=claude.Checked; if(!claude.Checked)linkState.ClearClient("claude");SaveLinks();TickLinks(); };
            chkClaude = claude; flow.Controls.Add(claude);
            var signal = new CheckBox { Text = "同步到 SignalRGB 灯光", AutoSize = true, Checked = linkCfg.Signal };
            signal.CheckedChanged += (s,e) => { linkCfg.Signal = signal.Checked; signalApplied = ""; SaveLinks(); TickLinks(); };
            chkSignal = signal; flow.Controls.Add(signal);
            var offsetRow=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=new Padding(0,8,0,0)};
            offsetRow.Controls.Add(new Label {Text="默认灯效时间偏移（毫秒）",AutoSize=true,Margin=new Padding(0,6,8,0)});
            var offset=new NumericUpDown {Minimum=-5000,Maximum=5000,Increment=50,Width=90,Value=Math.Max(-5000,Math.Min(5000,linkCfg.IdleOffsetMs))};
            offset.ValueChanged+=(s,e)=>{linkCfg.IdleOffsetMs=(int)offset.Value;signalApplied="";SaveLinks();TickLinks();};
            nudOffset = offset; offsetRow.Controls.Add(offset);flow.Controls.Add(offsetRow);
            flow.Controls.Add(new Label {Text="灯光慢了填正数，快了填负数；1000 毫秒 = 1 秒。",AutoSize=true,MaximumSize=new Size(500,0)});
            var rateRow=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=new Padding(0,8,0,0)};
            rateRow.Controls.Add(new Label {Text="灯效播放速度（%）",AutoSize=true,Margin=new Padding(0,6,8,0)});
            var rate=new NumericUpDown {Minimum=95,Maximum=105,Increment=.01m,DecimalPlaces=2,Width=100,Value=Math.Max(95m,Math.Min(105m,linkCfg.IdleRatePercent))};
            rate.ValueChanged+=(s,e)=>{linkCfg.IdleRatePercent=rate.Value;signalApplied="";SaveLinks();TickLinks();};
            nudRate = rate; rateRow.Controls.Add(rate);flow.Controls.Add(rateRow);
            flow.Controls.Add(new Label {Text="100% 为原速；越跑越快调低，越跑越慢调高。\r\n每步 0.01%；调整后点下方按钮，重新对齐起点。",AutoSize=true,MaximumSize=new Size(500,0)});
            var sync=new Button {Text="重播默认动画并对齐（预览 30 秒）",AutoSize=true,MinimumSize=new Size(280,34),Margin=new Padding(0,8,0,8)};
            sync.Click+=(s,e)=>Log(PreviewIdleSync());flow.Controls.Add(sync);
            var apply = new Button { Text = "打开 SignalRGB 灯效库", AutoSize = true, MinimumSize = new Size(260,36), Margin = new Padding(0,10,0,8) };
            apply.Click += (s,e) => {
                Task.Run(() => { try { using(var client=new SignalReadClient()) client.Read("openui_installed",new{}); }
                    catch(Exception ex){Log("打开 SignalRGB 灯效库失败："+ex.Message);} });
            };
            flow.Controls.Add(apply);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(510,0), Margin = new Padding(0,8,0,8) };
            string[] states = { "working","waiting","done","idle" }, labels = { "测试工作","测试等待","测试结束","恢复默认" };
            for (int i=0;i<states.Length;i++) {
                string value=states[i];
                var b = new Button { Text = labels[i], AutoSize = true, MinimumSize = new Size(110,34) };
                b.Click += (s,e) => { demoState=value; demoUntil=DateTime.UtcNow.AddSeconds(value=="idle" ? 0 : 8); if(value=="idle"){demoState="";linkState.Clear();} TickLinks(); };
                row.Controls.Add(b);
            }
            flow.Controls.Add(row);
            linkStatus.AutoSize = signalStatus.AutoSize = true;
            linkStatus.MaximumSize = signalStatus.MaximumSize = new Size(500,0);
            linkStatus.Margin = signalStatus.Margin = new Padding(0,8,0,8);
            linkStatus.Text = "任务事件：等待";
            signalStatus.Text = "SignalRGB：未连接";
            flow.Controls.Add(linkStatus); flow.Controls.Add(signalStatus);
            flow.Controls.Add(new Label { AutoSize = true, ForeColor = Color.Gray, MaximumSize = new Size(500,0),
                Text = "保持小工具在托盘运行即可。Codex 钩子首次使用需在 Codex 的 /hooks 中审阅并启用。\r\nSignalRGB 需运行并选中 CodexLCD 灯效；状态回执显示在下方。\r\n多个任务同时运行时：等待授权优先，其次工作中。30 分钟无事件的状态自动过期。\r\nSignalRGB 中的 Preview 可反向触发小屏幕，持续 8 秒。\r\n只传递事件类型与会话标识，不保存或传递对话正文。" });
            var doc = new Button { Text="打开联动说明", AutoSize=true, MinimumSize=new Size(160,34), Margin=new Padding(0,12,0,0) };
            doc.Click += (s,e) => Process.Start("notepad.exe", "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"integration","README.md") + "\"");
            flow.Controls.Add(doc);
            tab.Controls.Add(flow);
            return tab;
        }
        string LegacyClaude(string name) {
            if (!linkCfg.Claude) return "OK Claude Code 联动已关闭";
            string evt=name=="ccwork.savi" ? "UserPromptSubmit" : name=="ccask.savi" ? "PermissionRequest" : "Stop";
            linkState.Apply(new LinkEvent {client="claude",session_id="legacy",turn_id="legacy",hook_event_name=evt},DateTime.UtcNow);
            TickLinks(); return "OK Claude Code 事件已接收";
        }
        void SaveLinks() {
            try { Directory.CreateDirectory(Paths.Data); File.WriteAllText(LinkPath, new JavaScriptSerializer().Serialize(linkCfg)); }
            catch(Exception ex) { Log("保存联动设置失败：" + ex.Message); }
        }
        void MarkMovieStarted(string name) {
            if(name==cfg.Current && !cfg.CurrentIsBg)idlePlaybackEpoch=(long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;
        }
        string PreviewIdleSync() {
            if(linkCfg.NightMode)return "ERR 请先恢复灯光和屏幕";
            if(!connected || widgetOwnsScreen || !btnUpload.Enabled || File.Exists(StorageOps.Flag))return "ERR 当前不能重播默认动画";
            if(string.IsNullOrEmpty(cfg.Current) || cfg.CurrentIsBg)return "ERR 请先选择默认全屏动画";
            playing="";var result=PlayExternal(cfg.Current,0);
            if(result.StartsWith("OK")){demoState="idle";demoUntil=DateTime.UtcNow.AddSeconds(30);linkApplied="idle";signalApplied="";TickLinks();}
            return result;
        }
        void StartLinks() {
            linkTimer.Tick += (s,e) => TickLinks(); linkTimer.Start();
            var th = new Thread(() => {
                while (!IsDisposed) {
                    try {
                        using (var p = new NamedPipeServerStream("GpuLcdEvents", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)) {
                            p.WaitForConnection();
                            var r = new StreamReader(p, Encoding.UTF8);
                            var read = r.ReadLineAsync();
                            if (!read.Wait(1000)) continue;
                            string line = read.Result;
                            if (line == null || line.Length > 4096) continue;
                            var ev = new JavaScriptSerializer().Deserialize<LinkEvent>(line);
                            if (ev == null || ev.session_id == null || ev.session_id.Length > 200) continue;
                            BeginInvoke(new Action(() => {
                                if (ev.client=="claude" ? linkCfg.Claude : linkCfg.Codex) {
                                    receivedHookEvents++;lastHookEvent=ev.client+"/"+ev.hook_event_name;
                                    if(ev.client!="claude" && !completions.Track(ev)) {
                                        if(ev.hook_event_name=="UserPromptSubmit")Log("未纳入屏幕联动：Codex 会话缺少可跟踪的记录路径 session="+ev.session_id);
                                        return;
                                    }
                                    if(ev.hook_event_name=="Stop" || ev.hook_event_name=="UserPromptSubmit")Log("任务事件："+ev.client+"/"+ev.hook_event_name+" session="+ev.session_id+" turn="+ev.turn_id);
                                    linkState.Apply(ev, DateTime.UtcNow); TickLinks();
                                }
                            }));
                            var w = new StreamWriter(p,new UTF8Encoding(false)) { AutoFlush=true };
                            w.WriteLine("OK");
                        }
                    } catch { if (!IsDisposed) Thread.Sleep(200); }
                }
            });
            th.IsBackground=true; th.Start();
        }
        void TickLinks() {
            if(linkCfg.Codex && !completionBusy) {
                completionBusy=true;
                Task.Run(()=>{
                    var events=completions.Poll();
                    try {BeginInvoke(new Action(()=>{
                        completionBusy=false;if(!linkCfg.Codex)return;
                        foreach(var e in events) {
                            if(linkState.ApplyCompletion(e,DateTime.UtcNow))Log("完成记录补收："+e.hook_event_name+" session="+e.session_id+" turn="+e.turn_id);
                        }
                    }));}catch{}
                });
            }
            var now=DateTime.UtcNow;
            string state=now<demoUntil && demoState!="" ? demoState : linkState.Current(now);
            string client=now<demoUntil && demoState!="" ? "codex" : linkState.CurrentClient;
            string text=state=="working" ? "正在工作" : state=="waiting" ? "等待授权" : state=="done" ? "本轮结束" : "空闲";
            if (state!=linkShown) { Log("联动状态：" + text); linkShown=state; }
            if (!linkCfg.NightMode && !widgetOwnsScreen && !uploading && (state!=linkApplied || client!=linkAppliedClient && state!="idle") && (now-lastLcdTry).TotalSeconds>=2) {
                lastLcdTry=now;
                string movie=(client=="claude" ? "cc" : "cx") + (state=="working" ? "work.savi" : state=="waiting" ? "ask.savi" : "done.savi");
                string result;
                try { result=File.Exists(StorageOps.Flag) ? "ERR 屏幕处于存储维护模式" : !connected ? "ERR 屏幕未连接" : state=="idle" ? HandleCommand("revert") : PlayExternal(movie,1800); }
                catch(Exception ex) { result="ERR "+ex.Message; }
                if(result.StartsWith("OK")) { linkApplied=state; linkAppliedClient=client; }
                linkStatus.Text="小屏幕："+(state=="idle" ? "" : client=="claude" ? "Claude Code / " : "Codex / ")+text+"　"+result;
            } else if (state==linkApplied) linkStatus.Text="小屏幕："+(state=="idle" ? "" : client=="claude" ? "Claude Code / " : "Codex / ")+text;
            sharedState=state; signalOn=linkCfg.Signal;
            if(!signalOn && signalBridge!=null){var old=signalBridge;signalBridge=null;old.Dispose();}
            StartSignalBridge();
            if (linkCfg.Signal && (now-signalAckTime).TotalSeconds<4) signalStatus.Text="SignalRGB：灯效脚本已确认 "+signalAckState;
            if (!linkCfg.Signal && !linkCfg.NightMode && !signalWakePending) { signalStatus.Text="SignalRGB：同步未开启"; return; }
            string signalEvent=linkCfg.NightMode?"sleep":signalWakePending?"wake":state=="idle" && playing==cfg.Current && idlePlaybackEpoch>0
                ? "idle@"+idlePlaybackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture)+"@"+linkCfg.IdleOffsetMs.ToString(System.Globalization.CultureInfo.InvariantCulture)+"@"+Math.Max(95m,Math.Min(105m,linkCfg.IdleRatePercent)).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture) : client=="claude" && state!="idle" ? "claude@"+state : state;
            if (signalBusy || (now-lastSignalTry).TotalSeconds < (signalApplied==signalEvent ? 20 : .1)) return;
            signalBusy=true; lastSignalTry=now;
            Task.Run(() => {
                string result; bool ok=false;
                try {
                    var req=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:16034/canvas/event?sender=GpuLcdTool&event="+Uri.EscapeDataString(signalEvent));
                    req.Method="POST"; req.ContentLength=0; req.Timeout=1500; req.ReadWriteTimeout=1500; req.Proxy=null;
                    using(var resp=(HttpWebResponse)req.GetResponse()) { ok=(int)resp.StatusCode>=200 && (int)resp.StatusCode<300; result="HTTP "+(int)resp.StatusCode; }
                } catch(Exception ex) { result=ex.Message; }
                try { BeginInvoke(new Action(() => { signalBusy=false; if(ok){signalApplied=signalEvent;if(signalEvent=="wake")signalWakePending=false;} if(linkCfg.NightMode)nightLabel.Text="睡眠熄灯已开启；任务通知不会点亮设备。\r\n"+nightScreenResult+(ok?" 灯光熄灭指令已发送。":" 灯光接口未连通。"); if((DateTime.UtcNow-signalAckTime).TotalSeconds>=4) signalStatus.Text=ok ? "SignalRGB：事件接口已接收 "+signalEvent+"；请确认已应用 CodexLCD" : "SignalRGB：接口未连通　"+result; })); } catch { }
            });
        }
        void StopLinks() {
            linkTimer.Stop();
            var server=signalBridge; signalBridge=null; if(server!=null)server.Dispose();
            if (connected && !linkCfg.NightMode && linkApplied!="idle") try { HandleCommand("revert"); } catch { }
        }
    }
}

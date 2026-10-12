using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GpuLcd
{
    // 新界面（Vue + WebView2）宿主。设计原则：
    //  1. 前端只能"请求"，所有写屏幕的路径都复用现有的 Guard / uploading 互斥，不新增任何直接写串口的代码。
    //  2. 读类命令（getState/getSettings/listFiles/getLog）只读内存缓存，不触发串口读写，避免再次阻塞 UI 线程。
    //  3. 只信任 https://gpulcd.local/（虚拟主机映射到 ui\dist）或环境变量 GPULCD_UI_URL 指定的开发地址。
    partial class MainForm
    {
        const string WebHost = "gpulcd.local";
        WebUiForm webUi;
        Dictionary<string, int> fileSizes = new Dictionary<string, int>();
        readonly object logLock = new object();
        readonly List<string[]> logRing = new List<string[]>();
        Action<string, string> logHook;

        // ---- 上传（阶段 2）----
        // 安全模型：前端拿不到文件内容，也不能指定路径。文件由 C# 弹系统选择框，只把一次性 token 回给前端；
        // startUpload 只认这个 token，并由 C# 弹原生确认框。上传本身仍走原有的 DoUpload（互斥、转换、核验都在里面）。
        bool spaceKnown; long spaceFree, spaceTotal;
        string uploadToken = "", uploadPath = "";
        bool webBusy;   // 模态框（文件选择/确认）期间拒绝重入，避免前端连点触发两次
        Action<bool, string, string> uploadDoneHook;
        Func<string, bool> confirmHook = null;   // 测试接缝，见 WebStartUpload

        void RaiseUploadDone(bool ok, string name, bool asMovie)
        {
            var h = uploadDoneHook; if (h == null) return;
            string msg = ok ? (asMovie ? "上传完成，已设为当前全屏动画" : "上传完成，已设为背景")
                            : "上传没有完成。如果之后屏幕没有反应、顶部变成橙色，需要完全断电重启一次（关机后关电源开关 30 秒）。详见日志。";
            try { h(ok, name, msg); } catch { }
        }

        static string SuggestName(string path)
        {
            string stem = Path.GetFileNameWithoutExtension(path) ?? "";
            var sb = new StringBuilder();
            foreach (char c in stem) if (c < 128 && char.IsLetterOrDigit(c) && sb.Length < 8) sb.Append(c);
            return (sb.Length == 0 ? "img1" : sb.ToString()) + ".savi";
        }

        static string Kb(long bytes) { return bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0") + " MB" : (bytes / 1024.0).ToString("0") + " KB"; }

        object WebPickUploadFile(out string error)
        {
            error = null;
            if (webBusy) { error = "操作进行中"; return null; }
            string block = WebWriteBlock(); if (block != null) { error = block; return null; }
            webBusy = true;
            try
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "选择要上传到屏幕的图片或动画";
                    dlg.Filter = "图片和动画|*.gif;*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.mp4;*.avi;*.mov;*.mkv;*.webm|所有文件|*.*";
                    dlg.CheckFileExists = true;
                    if (dlg.ShowDialog(webUi) != DialogResult.OK) return new { cancelled = true };
                    string path = dlg.FileName;
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    // 绝不允许直接上传 .savi：已转换/来路不明的 savi 会把屏幕写坏
                    if (ext == ".savi") { error = "不能直接上传 .savi 文件，请选择原始图片或动画"; return null; }
                    long size = new FileInfo(path).Length;
                    if (size <= 0) { error = "文件是空的"; return null; }
                    if (size > 1024L * 1024 * 1024) { error = "文件超过 1 GB，请先裁剪"; return null; }
                    uploadToken = Guid.NewGuid().ToString("N"); uploadPath = path;
                    return new { cancelled = false, token = uploadToken, fileName = Path.GetFileName(path), sizeBytes = size, suggestedName = SuggestName(path) };
                }
            }
            finally { webBusy = false; }
        }

        object WebStartUpload(Dictionary<string, object> args, out string error)
        {
            error = null;
            if (webBusy) { error = "操作进行中"; return null; }
            string block = WebWriteBlock(); if (block != null) { error = block; return null; }
            string token = args.ContainsKey("token") ? args["token"] as string : null;
            string name = args.ContainsKey("name") ? (args["name"] as string ?? "").Trim() : "";
            string fit = args.ContainsKey("fit") ? args["fit"] as string : null;
            string mode = args.ContainsKey("mode") ? args["mode"] as string : null;
            if (string.IsNullOrEmpty(token) || token != uploadToken || string.IsNullOrEmpty(uploadPath)) { error = "文件令牌无效，请重新选择文件"; return null; }
            if (!Savi.ValidName(name)) { error = "屏幕上的文件名只能是 1-8 位字母或数字，加 .savi，例如 img1.savi"; return null; }
            if (fit != "contain" && fit != "cover" && fit != "stretch") { error = "适配方式无效"; return null; }
            if (mode != "movie" && mode != "background") { error = "用途无效"; return null; }
            if (string.Equals(name, cfg.Current, StringComparison.OrdinalIgnoreCase) || string.Equals(name, playing, StringComparison.OrdinalIgnoreCase))
            { error = "不能覆盖当前正在使用的文件，请先把默认动画切换到别的文件"; return null; }
            string path = uploadPath;
            if (!File.Exists(path)) { uploadToken = ""; uploadPath = ""; error = "文件已不存在，请重新选择"; return null; }
            long size = new FileInfo(path).Length;
            bool overwrite = lstFiles.Items.Contains(name);

            var text = new StringBuilder();
            text.AppendLine("即将向屏幕上传：").AppendLine();
            text.AppendLine("文件：" + Path.GetFileName(path) + "（" + Kb(size) + "）");
            text.AppendLine("屏幕文件名：" + name + (overwrite ? "    ⚠ 将覆盖同名文件" : ""));
            text.AppendLine("用途：" + (mode == "movie" ? "全屏动画（上传后立即播放并设为默认）" : "数据页背景"));
            if (spaceKnown) text.AppendLine("屏幕剩余空间：" + Kb(spaceFree) + " / " + Kb(spaceTotal));
            text.AppendLine().AppendLine("上传不可取消。期间请勿关闭程序、断开电源或拔 USB。");
            text.AppendLine("失败时屏幕可能卡住，需要整机断电（关电源开关）30 秒才能恢复。").AppendLine();
            text.Append("确定开始上传吗？");

            webBusy = true; bool yes;
            try
            {
                // confirmHook 仅供自动化测试替换；生产环境恒为 null，走原生确认框（默认按钮是"否"）
                yes = confirmHook != null ? confirmHook(text.ToString())
                    : MessageBox.Show(webUi, text.ToString(), "确认上传到屏幕", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }
            finally { webBusy = false; }
            if (!yes) return new { started = false, reason = "cancelled" };

            // 模态框期间状态可能变了（熄灯、别处发起上传、窗口被占用…），确认后重新检查
            block = WebWriteBlock(); if (block != null) { error = block; return null; }
            if (token != uploadToken) { error = "文件令牌无效，请重新选择文件"; return null; }
            uploadToken = ""; uploadPath = "";   // 令牌一次性
            txtFile.Text = path; txtName.Text = name;
            cmbFit.SelectedIndex = fit == "cover" ? 1 : fit == "stretch" ? 2 : 0;
            rbMovie.Checked = mode == "movie"; rbBg.Checked = mode != "movie";
            Log("新界面发起上传：" + Path.GetFileName(path) + " → " + name);
            DoUpload();   // 内部再次 Guard；成功启动时同步把 uploading 置位
            if (!uploading) { error = "上传没有开始，原因见日志"; return null; }
            return new { started = true };
        }

        // ---- 动画预览（只读本机文件，不碰串口）----
        // 屏幕上的文件读不回来，但上传前转换出的 .savi 副本留在 Paths.Work\out。只有它的大小与屏幕上那份完全一致时才认为是同一个文件。
        // SAVI 结构（从实际文件读出）：+12 fps，+16 帧数，+32 帧表偏移，+40 共用 JPEG 头偏移，+44 头长度；
        // 每帧 = 共用头 + 从帧偏移（FF DA）到 FF D9 的扫描数据。帧在文件里是倒置存放的，前端旋转 180° 显示。
        readonly Dictionary<string, object> previewCache = new Dictionary<string, object>();

        object WebPreview(Dictionary<string, object> args, out string error)
        {
            error = null;
            string name = args.ContainsKey("name") ? args["name"] as string : null;
            if (string.IsNullOrEmpty(name) || !Savi.ValidName(name)) { error = "文件名无效"; return null; }
            // 依次找：① 本机上传副本；② 从屏幕存储读出的副本（screen-copies，以及存储维护的自动备份，新的优先）。
            // ② 只在屏幕上的文件大小已知、且与副本完全一致时才采用，避免把屏幕上早已被替换的旧版本当成现在的画面。
            int screenSize; bool sizeKnown = fileSizes.TryGetValue(name, out screenSize) && screenSize > 0;
            string path = null, source = null; bool mismatch = false;
            string upload = Path.Combine(Paths.Work, "out", name);
            if (File.Exists(upload))
            {
                if (sizeKnown && new FileInfo(upload).Length != screenSize) mismatch = true;
                else { path = upload; source = "本机上传副本"; }
            }
            if (path == null && sizeKnown)
            {
                var cands = new List<string> { Path.Combine(Paths.Data, "screen-copies", "mov", name) };
                try
                {
                    string bb = StorageOps.BackupBase;
                    if (Directory.Exists(bb))
                        foreach (var d in Directory.GetDirectories(bb).OrderByDescending(x => x, StringComparer.Ordinal))
                            cands.Add(Path.Combine(d, "mov", name));
                }
                catch { }
                foreach (var c in cands)
                    if (File.Exists(c) && new FileInfo(c).Length == screenSize) { path = c; source = "从屏幕存储读出的副本"; break; }
            }
            if (path == null)
                return new { available = false, reason = mismatch ? "本机副本和屏幕上的文件不一致（屏幕上的是别处上传的版本）" : "本机没有这个文件的副本，也没有从屏幕存储读出过它" };
            var fi = new FileInfo(path);
            if (fi.Length > 32L * 1024 * 1024) return new { available = false, reason = "文件过大" };
            string key = path + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            object cached; if (previewCache.TryGetValue(key, out cached)) return cached;
            try
            {
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 64 || b[0] != (byte)'S' || b[1] != (byte)'A' || b[2] != (byte)'V' || b[3] != (byte)'I') return new { available = false, reason = "不是可识别的 SAVI 文件" };
                int fps = BitConverter.ToInt32(b, 12), count = BitConverter.ToInt32(b, 16), tab = BitConverter.ToInt32(b, 32), hOff = BitConverter.ToInt32(b, 40), hLen = BitConverter.ToInt32(b, 44);
                if (fps <= 0 || fps > 240 || count <= 0 || count > 20000 || tab < 0 || tab + 4L * count > b.Length || hOff < 0 || hLen <= 0 || hOff + (long)hLen > b.Length)
                    return new { available = false, reason = "SAVI 文件头无法解析" };
                // 连续相同的帧合并成一段（n = 持续帧数）
                var runs = new List<KeyValuePair<int, int>>();
                for (int i = 0; i < count; i++)
                {
                    int o = BitConverter.ToInt32(b, tab + 4 * i);
                    if (runs.Count > 0 && runs[runs.Count - 1].Key == o) runs[runs.Count - 1] = new KeyValuePair<int, int>(o, runs[runs.Count - 1].Value + 1);
                    else runs.Add(new KeyValuePair<int, int>(o, 1));
                }
                // 预览最多保留 90 个画面：画面太多时抽样，把被跳过的时长并进前一个画面，保证总时长不变
                const int maxFrames = 90;
                int step = (runs.Count + maxFrames - 1) / maxFrames;
                var frames = new List<object>(); long total = 0;
                for (int i = 0; i < runs.Count; i += step)
                {
                    int n = 0; for (int j = i; j < Math.Min(runs.Count, i + step); j++) n += runs[j].Value;
                    int o = runs[i].Key; if (o < 0 || o + 2 > b.Length || b[o] != 0xFF || b[o + 1] != 0xDA) return new { available = false, reason = "帧数据损坏" };
                    int e = o; while (e + 1 < b.Length && !(b[e] == 0xFF && b[e + 1] == 0xD9)) e++;
                    if (e + 1 >= b.Length) return new { available = false, reason = "帧数据不完整" };
                    e += 2;
                    var jpg = new byte[hLen + (e - o)];
                    Buffer.BlockCopy(b, hOff, jpg, 0, hLen); Buffer.BlockCopy(b, o, jpg, hLen, e - o);
                    total += jpg.Length;
                    frames.Add(new { src = "data:image/jpeg;base64," + Convert.ToBase64String(jpg), n = n });
                }
                var result = new { available = true, reason = "", source = source, fps = fps, frameCount = count, durationMs = count * 1000.0 / fps, frames = frames, bytes = total };
                if (previewCache.Count > 12) previewCache.Clear();
                previewCache[key] = result;
                return result;
            }
            catch (Exception ex) { return new { available = false, reason = "读取副本失败：" + ex.Message }; }
        }

        // ---- 阶段 3 ----
        // 进入存储维护：与"存储 / 开机"页原按钮完全相同的流程（写维护锁 → 关串口 → 以 --storage 重开 → 退出）。
        void EnterStorageMaintenance()
        {
            try
            {
                Directory.CreateDirectory(Paths.Data); File.WriteAllText(StorageOps.Flag, "Storage manager requested");
                debounce.Stop(); liveTimer.Stop(); revertTimer.Stop(); dev.Close(); connected = false;
                System.Diagnostics.Process.Start(Application.ExecutablePath, "--storage"); reallyExit = true; Close();
            }
            catch (Exception ex) { Log(ex.Message); }
        }

        bool Confirm(string text, string title)
        {
            if (confirmHook != null) return confirmHook(text);
            return MessageBox.Show(webUi, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        static bool AsciiPrintable(string s) { foreach (char c in s) if (c < 0x20 || c > 0x7e) return false; return true; }

        object WebPhase3(string cmd, Dictionary<string, object> args, out string error)
        {
            error = null; string block; int n;
            switch (cmd)
            {
                case "getPreview": return WebPreview(args, out error);
                case "getModes":
                    {
                        var sel = new List<int>(); for (int i = 0; i < clb.Items.Count; i++) if (clb.GetItemChecked(i)) sel.Add(i);
                        return new { available = Dev.ModeNames, selected = sel, interval = (int)numSec.Value, auto = chkAuto.Checked, canUseMovie = !string.IsNullOrEmpty(cfg.Current) && lstFiles.Items.Contains(cfg.Current) };
                    }
                case "setModes":
                    {
                        if ((block = WebWriteBlock()) != null) { error = block; return null; }
                        var raw = args.ContainsKey("selected") ? args["selected"] as System.Collections.IEnumerable : null;
                        var sel = new List<int>();
                        if (raw != null) foreach (var o in raw) { int v; if (!TryInt(o, out v) || v < 0 || v >= Dev.AllModes.Length) { error = "页面编号无效"; return null; } if (!sel.Contains(v)) sel.Add(v); }
                        if (sel.Count == 0) { error = "至少勾选一个页面"; return null; }
                        if (!args.ContainsKey("interval") || !TryInt(args["interval"], out n) || n < 1 || n > 120) { error = "停留时间必须是 1-120 秒"; return null; }
                        if (!args.ContainsKey("auto") || !(args["auto"] is bool)) { error = "auto 必须是布尔值"; return null; }
                        // 播放不存在的文件会让屏幕卡死：这里提前拒绝（原界面是弹模态框）
                        if (sel.Contains(0) && (string.IsNullOrEmpty(cfg.Current) || !lstFiles.Items.Contains(cfg.Current))) { error = "屏幕上还没有可播放的动画，请先在“图片 / 动画”页上传并设为当前"; return null; }
                        for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, sel.Contains(i));
                        numSec.Value = n; chkAuto.Checked = (bool)args["auto"];
                        ApplyModes();
                        var applied = cfg.Modes ?? new int[0];
                        if (applied.Length != sel.Count || sel.Any(i => !applied.Contains((int)Dev.AllModes[i]))) { error = "应用失败，原因见日志"; return null; }
                        return new { };
                    }
                case "getText":
                    return new { text = cfg.Text ?? "", fontSize = cfg.FontSize, color = string.IsNullOrEmpty(cfg.TextColor) ? "FF0000" : cfg.TextColor, show = cfg.ShowText };
                case "setText":
                    {
                        if ((block = WebWriteBlock()) != null) { error = block; return null; }
                        string text = args.ContainsKey("text") ? args["text"] as string : null;
                        if (text == null || text.Length > 64) { error = "文字最多 64 个字符"; return null; }
                        if (!AsciiPrintable(text)) { error = "文字只支持英文、数字和 ASCII 符号"; return null; }
                        if (!args.ContainsKey("fontSize") || !TryInt(args["fontSize"], out n) || n < 0 || n > 9) { error = "字号必须是 0-9"; return null; }
                        string color = args.ContainsKey("color") ? args["color"] as string : null;
                        if (color == null || !System.Text.RegularExpressions.Regex.IsMatch(color, "^[0-9A-Fa-f]{6}$")) { error = "颜色格式必须是 RRGGBB"; return null; }
                        if (!args.ContainsKey("show") || !(args["show"] is bool)) { error = "show 必须是布尔值"; return null; }
                        txtText.Text = text; numFont.Value = n; chkShowText.Checked = (bool)args["show"];
                        textColor = Color.FromArgb(Convert.ToInt32(color.Substring(0, 2), 16), Convert.ToInt32(color.Substring(2, 2), 16), Convert.ToInt32(color.Substring(4, 2), 16));
                        btnColor.BackColor = textColor;
                        ApplyText();
                        if (cfg.Text != text) { error = "应用失败，原因见日志"; return null; }
                        return new { };
                    }
                case "getLive":
                    return new { on = chkLive.Checked, text = lbLive.Text ?? "", autostart = IsAutostart() };
                case "setLive":
                    {
                        if ((block = WebWriteBlock()) != null) { error = block; return null; }
                        if (!args.ContainsKey("on") || !(args["on"] is bool)) { error = "on 必须是布尔值"; return null; }
                        bool on = (bool)args["on"];
                        chkLive.Checked = on;   // 触发原有 SetLive（内部再次 Guard）
                        if (chkLive.Checked != on) { error = "操作失败，原因见日志"; return null; }
                        return new { };
                    }
                case "setAutostart":
                    {
                        if (!args.ContainsKey("on") || !(args["on"] is bool)) { error = "on 必须是布尔值"; return null; }
                        try { SetAutostart((bool)args["on"]); } catch (Exception ex) { error = "写入开机自启失败：" + ex.Message; return null; }
                        loading = true; try { chkAutostart.Checked = IsAutostart(); } finally { loading = false; }
                        return new { };
                    }
                case "getLinks":
                    {
                        var ser = new JavaScriptSerializer();
                        return new
                        {
                            codex = linkCfg.Codex, claude = linkCfg.Claude, signal = linkCfg.Signal, dotMatrix = linkCfg.DotMatrix,
                            idleOffsetMs = linkCfg.IdleOffsetMs, idleRatePercent = (double)linkCfg.IdleRatePercent,
                            linkState = linkShown ?? "idle", receivedHookEvents = receivedHookEvents, lastHookEvent = lastHookEvent ?? "",
                            sessions = ser.DeserializeObject(ser.Serialize(linkState.Snapshot())),
                            statusText = linkStatus.Text ?? "", signalText = signalStatus.Text ?? "", dotMatrixText = dotStatus.Text ?? ""
                        };
                    }
                case "setLinks":
                    {
                        if (uploading) { error = "上传进行中"; return null; }
                        decimal rate = linkCfg.IdleRatePercent; int off = linkCfg.IdleOffsetMs;
                        if (args.ContainsKey("idleOffsetMs")) { if (!TryInt(args["idleOffsetMs"], out off) || off < -5000 || off > 5000) { error = "时间偏移必须在 -5000 到 5000 毫秒之间"; return null; } }
                        if (args.ContainsKey("idleRatePercent")) { try { rate = Convert.ToDecimal(args["idleRatePercent"]); } catch { error = "播放速度必须是数字"; return null; } if (rate < 95m || rate > 105m) { error = "播放速度必须在 95 到 105 之间"; return null; } }
                        foreach (var k in new[] { "codex", "claude", "signal", "dotMatrix" }) if (args.ContainsKey(k) && !(args[k] is bool)) { error = k + " 必须是布尔值"; return null; }
                        // 通过原控件改值，沿用原有处理（清理状态、保存配置、刷新联动）
                        if (args.ContainsKey("codex")) chkCodex.Checked = (bool)args["codex"];
                        if (args.ContainsKey("claude")) chkClaude.Checked = (bool)args["claude"];
                        if (args.ContainsKey("signal")) chkSignal.Checked = (bool)args["signal"];
                        if (args.ContainsKey("dotMatrix")) chkDotMatrix.Checked = (bool)args["dotMatrix"];
                        if (args.ContainsKey("idleOffsetMs")) nudOffset.Value = off;
                        if (args.ContainsKey("idleRatePercent")) nudRate.Value = Math.Round(rate, 2);
                        return new { };
                    }
                case "linkDemo":
                    {
                        if ((block = WebWriteBlock()) != null) { error = block; return null; }
                        string st = args.ContainsKey("state") ? args["state"] as string : null;
                        if (st != "working" && st != "waiting" && st != "done" && st != "idle") { error = "状态无效"; return null; }
                        demoState = st; demoUntil = DateTime.UtcNow.AddSeconds(st == "idle" ? 0 : 8);
                        if (st == "idle") { demoState = ""; linkState.Clear(); }
                        TickLinks();
                        return new { };
                    }
                case "idleSync":
                    {
                        if ((block = WebWriteBlock()) != null) { error = block; return null; }
                        string r = PreviewIdleSync();
                        if (!r.StartsWith("OK")) { error = r.StartsWith("ERR ") ? r.Substring(4) : r; return null; }
                        return new { };
                    }
                case "sendWidget":
                    {
                        if ((block = WebWriteBlock(true)) != null) { error = block; return null; }
                        string r = WidgetCommand(new JavaScriptSerializer().Serialize(args));
                        if (!r.StartsWith("OK")) { error = r.StartsWith("ERR ") ? r.Substring(4) : r; return null; }
                        return new { message = r.Length > 3 ? r.Substring(3) : "OK" };
                    }
                case "getWidgetStatus":
                    {
                        var cur = displayJobs.Current(DateTime.UtcNow);
                        return new { active = widgetOwnsScreen, kind = cur == null ? null : cur.Kind, sent = widgetSent ?? "", error = widgetError ?? "" };
                    }
                case "setNight":
                    {
                        if (!args.ContainsKey("on") || !(args["on"] is bool)) { error = "on 必须是布尔值"; return null; }
                        string r = SetNightMode((bool)args["on"]);   // 内部已检查上传中 / 维护锁
                        if (!r.StartsWith("OK")) { error = r.StartsWith("ERR ") ? r.Substring(4) : r; return null; }
                        return new { message = r.Substring(3) };
                    }
                case "readOpening":
                    {
                        if ((block = WebWriteBlock()) != null) { error = block; return null; }
                        int mode = -1; bool ok = Quiet(() => StorageOps.ReadOpening(dev, out mode));
                        if (!ok) { error = "读取失败"; return null; }
                        return new { mode = mode };
                    }
                case "previewOpening":
                    {
                        if ((block = WebWriteBlock()) != null) { error = block; return null; }
                        if (webBusy) { error = "操作进行中"; return null; }
                        webBusy = true; bool yes;
                        try { yes = Confirm("将在小屏幕上播放原开机动画，并暂停实时刷新和默认动画。\r\n之后需要回到“图片 / 动画”页重新设为当前才能恢复默认动画。\r\n\r\n继续吗？", "播放开机动画"); }
                        finally { webBusy = false; }
                        if (!yes) return new { started = false };
                        if ((block = WebWriteBlock()) != null) { error = block; return null; }
                        try
                        {
                            chkLive.Checked = false; liveTimer.Stop(); revertTimer.Stop(); dev.Stop(); playing = "";
                            bool ok = StorageOps.PreviewOpening(dev);
                            Log(ok ? "开机动画播放指令已发送；返回图片页可恢复默认动画。" : "开机动画指令发送失败");
                            if (!ok) { error = "开机动画指令发送失败"; return null; }
                        }
                        catch (Exception ex) { error = ex.Message; return null; }
                        return new { started = true };
                    }
                case "openStorageManager":
                    {
                        if (uploading) { error = "上传进行中"; return null; }
                        if (webBusy) { error = "操作进行中"; return null; }
                        webBusy = true; bool yes;
                        try { yes = Confirm("程序将退出，并以“存储维护”模式重新打开。\r\n维护期间屏幕暂停普通控制；恢复普通控制需要安全弹出后整机断电约 30 秒。\r\n\r\n确定进入存储维护吗？", "进入存储维护"); }
                        finally { webBusy = false; }
                        if (!yes) return new { started = false };
                        if (uploading) { error = "上传进行中"; return null; }
                        EnterStorageMaintenance();
                        return new { started = true };
                    }
            }
            error = "未知命令 " + cmd; return null;
        }

        void OpenWebUi()
        {
            if (webUi != null && !webUi.IsDisposed) { webUi.Show(); webUi.WindowState = FormWindowState.Normal; webUi.Activate(); return; }
            webUi = new WebUiForm(this);
            webUi.FormClosed += (s, e) => { logHook = null; uploadDoneHook = null; webUi = null; AfterWebUiClosed(); };
            webUi.Show();
        }

        void CloseWebUi() { try { if (webUi != null && !webUi.IsDisposed) webUi.Close(); } catch { } }

        void RecordLog(string time, string text)
        {
            lock (logLock) { logRing.Add(new[] { time, text }); if (logRing.Count > 300) logRing.RemoveRange(0, logRing.Count - 300); }
            var h = logHook; if (h != null) { try { h(time, text); } catch { } }
        }

        // 写屏幕前的前置检查。返回 null 表示可以写；否则是给前端看的中文原因。
        // allowWidgetOwned：挂件（进度/通知）命令本身就是在"挂件占用屏幕"时更新或清除挂件，不能被这条拦住。
        string WebWriteBlock(bool allowWidgetOwned = false)
        {
            if (uploading) return "上传进行中";
            if (File.Exists(StorageOps.Flag)) return "屏幕处于存储维护模式";
            if (linkCfg.NightMode) return "请先恢复灯光和屏幕";
            if (widgetOwnsScreen && !allowWidgetOwned) return "请先恢复原画面（进度 / 通知占用屏幕）";
            if (!connected) return "屏幕未连接";
            return null;
        }

        object WebState()
        {
            int fw = 0; string gpu = "", com = "";
            if (connected) { try { fw = Convert.ToInt32(dev.L3.FWVersion); } catch { } gpu = dev.Gpu ?? ""; com = dev.Com ?? ""; }
            return new
            {
                connected = connected,
                gpu = gpu,
                com = com,
                firmware = fw,
                uploading = uploading,
                nightMode = linkCfg.NightMode,
                maintenance = File.Exists(StorageOps.Flag),
                playing = playing ?? "",
                linkState = linkShown ?? "idle"
            };
        }

        object WebSettings()
        {
            return new { brightness = cfg.Bri, opacity = cfg.Opa, dir = cfg.Dir, enabled = cfg.En, current = cfg.Current ?? "" };
        }

        static int Clamp(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }
        static bool TryInt(object o, out int v) { v = 0; if (o == null) return false; try { v = Convert.ToInt32(o); return true; } catch { return false; } }

        // 在 UI 线程执行。返回 (ok, dataOrError)。
        internal object WebCall(string cmd, Dictionary<string, object> args, out string error)
        {
            error = null;
            if (args == null) args = new Dictionary<string, object>();
            switch (cmd)
            {
                case "getState": return WebState();
                case "getSettings": return WebSettings();
                case "listFiles":
                    {
                        var list = new List<object>();
                        foreach (var it in lstFiles.Items) list.Add(new { name = (string)it, size = fileSizes.ContainsKey((string)it) ? fileSizes[(string)it] : 0 });
                        return list;
                    }
                case "getLog":
                    {
                        int limit = 100; int l; if (args.ContainsKey("limit") && TryInt(args["limit"], out l)) limit = Clamp(l, 1, 300);
                        lock (logLock) return logRing.Skip(Math.Max(0, logRing.Count - limit)).Select(r => (object)new { time = r[0], text = r[1] }).ToList();
                    }
                case "pickUploadFile": return WebPickUploadFile(out error);
                case "startUpload": return WebStartUpload(args, out error);
                case "getSpace": return new { known = spaceKnown, free = spaceFree, total = spaceTotal };
                case "setBasic":
                    {
                        string block = WebWriteBlock(); if (block != null) { error = block; return null; }
                        int b = tbBri.Value, o = tbOpa.Value, d = cmbDir.SelectedIndex; bool en = chkEnable.Checked; int t;
                        if (args.ContainsKey("brightness")) { if (!TryInt(args["brightness"], out t) || t < 0 || t > 10) { error = "亮度必须是 0-10"; return null; } b = t; }
                        if (args.ContainsKey("opacity")) { if (!TryInt(args["opacity"], out t) || t < 0 || t > 100) { error = "不透明度必须是 0-100"; return null; } o = t; }
                        if (args.ContainsKey("dir")) { if (!TryInt(args["dir"], out t) || t < 0 || t > 3) { error = "方向必须是 0-3"; return null; } d = t; }
                        if (args.ContainsKey("enabled")) { if (!(args["enabled"] is bool)) { error = "enabled 必须是布尔值"; return null; } en = (bool)args["enabled"]; }
                        debounce.Stop();
                        loading = true;
                        try { tbBri.Value = b; lbBri.Text = b.ToString(); tbOpa.Value = o; lbOpa.Text = o.ToString(); cmbDir.SelectedIndex = d; chkEnable.Checked = en; }
                        finally { loading = false; }
                        ApplyBasic();   // 内部再次 Guard，并写 settings.json
                        return new { };
                    }
                case "playFile":
                    {
                        string block = WebWriteBlock(); if (block != null) { error = block; return null; }
                        string name = args.ContainsKey("name") ? args["name"] as string : null;
                        if (string.IsNullOrEmpty(name) || !lstFiles.Items.Contains(name)) { error = "屏幕上没有 " + name; return null; }
                        Use(name, true);
                        return new { };
                    }
                default:
                    return WebPhase3(cmd, args, out error);
            }
        }

        class WebUiForm : Form
        {
            readonly MainForm owner;
            readonly WebView2 web = new WebView2();
            readonly Timer pusher = new Timer { Interval = 1000 };
            readonly Timer progressPusher = new Timer { Interval = 250 };
            readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };   // 动画预览一次可能有几 MB
            string lastState = "";
            string allowedOrigin;

            public WebUiForm(MainForm owner)
            {
                this.owner = owner;
                Text = "显卡屏幕控制"; Icon = owner.Icon;
                // 按"逻辑像素 × 显示缩放"定窗口大小：不管系统缩放是 100% 还是 150%，网页的有效尺寸都约 1180×800，
                // 并给一个不会把界面挤坏的最小尺寸（逻辑 900×620）
                float k; using (var g = CreateGraphics()) k = g.DpiX / 96f;
                var wa = Screen.PrimaryScreen.WorkingArea;
                Size = new Size(Math.Min((int)(1180 * k), wa.Width - 40), Math.Min((int)(800 * k), wa.Height - 40));
                MinimumSize = new Size(Math.Min((int)(900 * k), wa.Width), Math.Min((int)(620 * k), wa.Height));
                StartPosition = FormStartPosition.CenterScreen;
                web.Dock = DockStyle.Fill; Controls.Add(web);
                Load += async (s, e) => await Init();
                pusher.Tick += (s, e) => Push();
                progressPusher.Tick += (s, e) => PushProgress();
                FormClosed += (s, e) => { pusher.Stop(); progressPusher.Stop(); };
            }

            // ---- 内置前端资源（build.cmd 把 ui\dist 打成 zip 嵌进 exe）----
            Dictionary<string, byte[]> embedded;
            const string Csp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";

            static Dictionary<string, byte[]> LoadEmbeddedUi()
            {
                try
                {
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("GpuLcd.ui-dist.zip"))
                    {
                        if (s == null) return null;
                        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                        using (var zip = new System.IO.Compression.ZipArchive(s, System.IO.Compression.ZipArchiveMode.Read))
                            foreach (var en in zip.Entries)
                            {
                                if (string.IsNullOrEmpty(en.Name)) continue;   // 目录项
                                using (var es = en.Open()) using (var ms = new MemoryStream()) { es.CopyTo(ms);
                                    string key = en.FullName.Replace('\\', '/');
                                    while (key.StartsWith("./") || key.StartsWith("/")) key = key.Substring(key.StartsWith("./") ? 2 : 1);   // tar 会写成 ./index.html
                                    map[key] = ms.ToArray();
                                }
                            }
                        return map.ContainsKey("index.html") ? map : null;
                    }
                }
                catch { return null; }
            }

            static string Mime(string path)
            {
                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".html": return "text/html; charset=utf-8";
                    case ".js": case ".mjs": return "text/javascript; charset=utf-8";
                    case ".css": return "text/css; charset=utf-8";
                    case ".json": return "application/json; charset=utf-8";
                    case ".svg": return "image/svg+xml";
                    case ".png": return "image/png";
                    case ".ico": return "image/x-icon";
                    case ".woff2": return "font/woff2";
                    case ".woff": return "font/woff";
                    case ".txt": return "text/plain; charset=utf-8";
                    default: return "application/octet-stream";
                }
            }

            void ServeEmbedded(object sender, CoreWebView2WebResourceRequestedEventArgs e)
            {
                try
                {
                    string path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
                    if (path.Length == 0) path = "index.html";
                    byte[] data;
                    var env = web.CoreWebView2.Environment;
                    if (e.Request.Method != "GET" || !embedded.TryGetValue(path, out data))
                    { e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain"); return; }
                    e.Response = env.CreateWebResourceResponse(new MemoryStream(data), 200, "OK",
                        "Content-Type: " + Mime(path) + "\r\nContent-Security-Policy: " + Csp + "\r\nCache-Control: no-store");
                }
                catch { }
            }

            static string DistDir()
            {
                string env = Environment.GetEnvironmentVariable("GPULCD_UI_DIR");
                if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui", "dist");
                return Directory.Exists(p) ? p : null;
            }

            // 初始化失败时不能在 Load/Show 的调用栈里弹模态框，否则会卡住调用 OpenWebUi 的管道线程。
            void Fail(string text)
            {
                owner.Log(text);
                // 新界面起不来（缺 WebView2 运行时等）：自动退回经典界面，保证程序始终可用
                BeginInvoke(new Action(() => { owner.ShowClassic(); MessageBox.Show(text + "\r\n\r\n已自动切换到经典界面。", "新界面"); Close(); }));
            }

            async System.Threading.Tasks.Task Init()
            {
                await System.Threading.Tasks.Task.Yield();   // 先让 Show() 返回，再做可能同步失败的初始化
                try
                {
                    string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuLcdTool", "webview2");
                    Directory.CreateDirectory(data);
                    var env = await CoreWebView2Environment.CreateAsync(null, data);
                    await web.EnsureCoreWebView2Async(env);
                }
                catch (Exception ex)
                {
                    Fail("无法启动 WebView2：" + ex.Message + "\r\n请确认已安装 Microsoft Edge WebView2 运行时。");
                    return;
                }
                var core = web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                string devUrl = Environment.GetEnvironmentVariable("GPULCD_UI_URL");
                string url;
                if (!string.IsNullOrEmpty(devUrl)) { url = devUrl; allowedOrigin = new Uri(devUrl).GetLeftPart(UriPartial.Authority); core.Settings.AreDevToolsEnabled = true; }
                else
                {
                    url = "https://" + WebHost + "/index.html"; allowedOrigin = "https://" + WebHost;
                    core.Settings.AreDevToolsEnabled = false;
                    string overrideDir = Environment.GetEnvironmentVariable("GPULCD_UI_DIR");
                    embedded = string.IsNullOrEmpty(overrideDir) ? LoadEmbeddedUi() : null;
                    if (embedded != null)
                    {
                        // 成品模式：前端打包在 exe 里，不依赖任何散落文件
                        core.AddWebResourceRequestedFilter("https://" + WebHost + "/*", CoreWebView2WebResourceContext.All);
                        core.WebResourceRequested += ServeEmbedded;
                    }
                    else
                    {
                        // 开发 / 回退模式：从文件夹提供（GPULCD_UI_DIR 或 exe 旁的 ui\dist）
                        string dist = DistDir();
                        if (dist == null) { Fail("没有找到界面文件（程序内置资源和 ui\\dist 都不存在）。"); return; }
                        core.SetVirtualHostNameToFolderMapping(WebHost, dist, CoreWebView2HostResourceAccessKind.Allow);
                    }
                }
                // 只允许在受信任的来源内导航，外部链接交给系统浏览器
                core.NavigationStarting += (s, e) =>
                {
                    if (!e.Uri.StartsWith(allowedOrigin, StringComparison.OrdinalIgnoreCase) && !e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
                };
                core.NewWindowRequested += (s, e) => { e.Handled = true; };
                core.WebMessageReceived += OnMessage;
                owner.logHook = (t, m) => { try { if (!IsDisposed) BeginInvoke(new Action(() => Post(new { @event = "log", data = new { time = t, text = m } }))); } catch { } };
                owner.uploadDoneHook = (ok, name, msg) => { if (!IsDisposed) Post(new { @event = "uploadDone", data = new { ok = ok, name = name, message = msg } }); };
                core.Navigate(url);
                pusher.Start(); progressPusher.Start();
            }

            void Post(object o) { if (web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsString(json.Serialize(o)); }

            // 上传期间约每 250ms 推一次进度（只读厂商库里的内存字段，不碰串口）
            void PushProgress()
            {
                if (!owner.uploading) return;
                try { Post(new { @event = "uploadProgress", data = new { percent = Math.Max(0, Math.Min(100, owner.dev.UploadPercent)), result = owner.dev.UploadResult } }); }
                catch { }
            }

            void Push()
            {
                try
                {
                    string s = json.Serialize(owner.WebState());
                    if (s != lastState) { lastState = s; Post(new { @event = "state", data = owner.WebState() }); }
                }
                catch { }
            }

            void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
            {
                // 来源校验：只处理受信任页面发来的消息
                if (!e.Source.StartsWith(allowedOrigin, StringComparison.OrdinalIgnoreCase)) return;
                object id = null;
                try
                {
                    string raw = e.TryGetWebMessageAsString();
                    var msg = json.Deserialize<Dictionary<string, object>>(raw);
                    if (msg == null || !msg.ContainsKey("cmd")) return;
                    id = msg.ContainsKey("id") ? msg["id"] : null;
                    var args = msg.ContainsKey("args") ? msg["args"] as Dictionary<string, object> : null;
                    string err; object data = owner.WebCall(msg["cmd"] as string, args, out err);
                    if (err != null) Post(new { id = id, ok = false, error = err });
                    else Post(new { id = id, ok = true, data = data });
                    if (err == null && (msg["cmd"] as string) == "setBasic") Push();
                }
                catch (Exception ex) { Post(new { id = id, ok = false, error = ex.Message }); }
            }
        }
    }
}

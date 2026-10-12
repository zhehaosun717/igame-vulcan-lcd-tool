// GpuLcd - 直接控制七彩虹 iGame 显卡 LCD 3.0 屏幕，不运行 iGameCenter。
// 优先使用随包 vendor/iGameCenter 的运行库；开发环境可回退到已安装的厂商库。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using iGameAPI.LCD.CSharp;
using iGameAPI.NVAPI.CSharp;
using LcdColor = iGameAPI.Contracts.LCD.Color;
using Mode = iGameAPI.Contracts.LCD.LCD3DisplayMode;
using CpuInfo = iGameCenter.Contracts.Hardware.CPULoadInfo;
using DramInfo = iGameCenter.Contracts.Hardware.DRAMLoadInfo;

namespace GpuLcd
{
    // ---------------------------------------------------------------- 设置文件
    class Cfg
    {
        public int Bri = -1, Opa = -1, Dir = -1;
        public bool En = true, Live = false, Auto = true, Tray = false;
        public int Interval = 10;
        public int[] Modes = null;
        public List<string> Files = new List<string>();
        public string Fit = "contain";
        public string Text = "";
        public int FontSize = 0;
        public string TextColor = "FF0000";
        public bool ShowText = false;
        public string Current = "";
        public bool CurrentIsBg = false;

        static string PathOf { get { return Path.Combine(Paths.Data, "settings.json"); } }
        public static Cfg Load()
        {
            try { if (File.Exists(PathOf)) return new JavaScriptSerializer().Deserialize<Cfg>(File.ReadAllText(PathOf)); } catch { }
            return new Cfg();
        }
        public bool Exists { get { return File.Exists(PathOf); } }
        public void Save()
        {
            try { Directory.CreateDirectory(Paths.Data); File.WriteAllText(PathOf, new JavaScriptSerializer().Serialize(this)); } catch { }
        }
    }

    static class Paths
    {
        public static string Data { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GpuLcdTool"); } }
        public static string Work { get { return Path.Combine(Data, "savi"); } }
    }

    // ---------------------------------------------------------------- 屏幕设备
    partial class Dev
    {
        public ILCD3 L3;
        public ILCD Core;
        public string Gpu = "", Com = "";
        LogCallbackFun cb;

        public static readonly Mode[] AllModes = {
            Mode.Movie, Mode.CpuFrequency, Mode.CpuUsage, Mode.CpuTemperature, Mode.GpuFrequency, Mode.GpuUsage,
            Mode.GpuTemperature, Mode.GpuFanSpeed, Mode.VRAMFrequency, Mode.VRAMUsage, Mode.RAMFrequency,
            Mode.RAMUsage, Mode.RAMTemperature, Mode.Time };
        public static readonly string[] ModeNames = {
            "自定义动画/图片", "CPU 频率", "CPU 占用", "CPU 温度", "GPU 频率", "GPU 占用", "GPU 温度",
            "GPU 风扇转速", "显存频率", "显存占用", "内存频率", "内存占用", "内存温度", "时间" };

        [MethodImpl(MethodImplOptions.NoInlining)]
        public string Connect(Action<string> log)
        {
            try
            {
                if (!NvCard.LoadNVAPI()) return "NVAPI 加载失败（需要 NVIDIA 显卡和驱动）";
                var cards = NvCard.EnumNvCard();
                if (cards == null || cards.Count == 0) return "没有找到显卡";
                cb = new LogCallbackFun(m => log("[库] " + m));
                ILCD lcd = LCD.GetGCardLCD(cards[0].GPUHandle, cb);
                if (lcd == null) return "这张显卡没有检测到 LCD 屏（USB 线接好了吗？）";
                L3 = lcd as ILCD3;
                if (L3 == null) return "检测到的不是 LCD 3.0 屏幕";
                Core = lcd;
                Gpu = cards[0].Name;
                Com = L3.COMName;
                return null;
            }
            catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
        }

        public string Status()
        {
            return string.Format("{0} | {1} | 固件 {2} | 启用={3} 亮度={4} 背景不透明度={5} 方向={6} 轮播={7}ms 自动={8}",
                Gpu, Com, L3.FWVersion, L3.Enabled, L3.Brightness, L3.BackgroundOpacity, L3.Dir, L3.SwitchTime, L3.AutoSwitching);
        }

        public void ApplyBasic(int opacity, int brightness, bool enabled, int dir)
        {
            L3.Dir = dir;
            L3.SetLCD3Setting(opacity, brightness, enabled);
        }

        public void ApplyModes(IEnumerable<Mode> modes, int seconds, bool auto)
        {
            L3.IsDoubleDisplay = false;
            L3.SingleDisplayModes = modes.ToList();
            L3.SwitchTime = Math.Max(1, seconds) * 1000;
            L3.AutoSwitching = auto;
            L3.RefeshDisplayMode();
        }

        public void SetText(int size, System.Drawing.Color c, string text, bool show)
        {
            var lc = new LcdColor { R = c.R, G = c.G, B = c.B };
            L3.FontColor = lc;
            L3.SetCusTextInfo(size, lc, text);
            L3.IsDisplayCustomText = show;
            L3.RefeshDisplayMode();
        }

        public int UploadPercent { get { return L3.UploadPercent; } }
        public int UploadResult { get { return L3.UploadResult; } }
        public bool Started;

        // 厂家库 LCD3Core.LCD3_UploadFile 的返回码（从库的 IL 读出来的）
        public static string UploadError(int code)
        {
            switch (code)
            {
                case 1: return "本地文件不存在";
                case 2: return "屏幕上的文件名为空";
                case 3: return "屏幕没有进入写文件模式（开始上传的指令没有得到应答）";
                case 4: return "文件超过 20 MB";
                case 7: return "屏幕存储空间不够";
                case 9: return "上一次上传还没结束";
                case 10: return "屏幕串口没打开";
                case 11: return "厂家库上传线程发生异常";
                case 12: return "通过串口写入数据失败";
                case 20: return "通过 I2C 写入数据失败";
                default: return "未知错误码 " + code;
            }
        }

        // 上传在库内部的后台线程里进行，UploadImage 返回时并没有传完，所以要轮询进度。
        // 调用前必须保证没有别的东西在用串口（实时刷新要先停掉）。返回 true 表示传输完成。
        public bool UploadWait(string lcdName, string localFile, Action<string> log, int timeoutSec)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var busyField = L3.GetType().GetField("IsUploading", flags);
            if (busyField == null) throw new NotSupportedException("厂家库缺少上传状态字段");
            if ((bool)busyField.GetValue(L3)) throw new InvalidOperationException("上一次上传尚未结束");
            // UploadImage resets these in a background worker. Clear stale 100% before starting it.
            L3.GetType().GetProperty("UploadPercent", flags).GetSetMethod(true).Invoke(L3, new object[] { 0 });
            L3.GetType().GetProperty("UploadResult", flags).GetSetMethod(true).Invoke(L3, new object[] { -1 });
            L3.UploadImage(lcdName, localFile);
            var sw = Stopwatch.StartNew(); int lastPct = -99, lastRes = -99; int stalled = 0;
            while (sw.Elapsed.TotalSeconds < timeoutSec)
            {
                int pct = L3.UploadPercent, res = L3.UploadResult;
                if (pct != lastPct || res != lastRes) { log(string.Format("  上传进度 {0}%  结果码 {1}", pct, res)); lastPct = pct; lastRes = res; stalled = 0; }
                else stalled++;
                if (res > 0) { log("上传失败：" + UploadError(res)); return false; }
                if (pct >= 100 && res == 0 && !(bool)busyField.GetValue(L3))
                {
                    var files = ListFiles();
                    bool verified = files != null && files.Any(f => f.Key == lcdName && f.Value == new FileInfo(localFile).Length);
                    log(verified ? "上传线程已结束；文件尺寸核验通过" : "上传报告完成，但文件尺寸核验失败");
                    return verified;
                }
                if (stalled > 240) { log("进度 60 秒没有变化，放弃等待"); return false; }
                Thread.Sleep(250);
            }
            log("上传超时"); return false;
        }

        // 只读：从屏幕读取文件列表和存储空间（库里是非公开方法，用反射调用）
        object CoreObj()
        {
            var f = L3.GetType().GetField("LCD3Core", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return f == null ? null : f.GetValue(L3);
        }
        public List<KeyValuePair<string, int>> ListFiles()
        {
            var res = new List<KeyValuePair<string, int>>();
            object c = CoreObj(); if (c == null) return null;
            var m = c.GetType().GetMethod("LCD3_GetMovList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var list = m.Invoke(c, null) as System.Collections.IEnumerable;
            if (list == null) return null;
            foreach (object o in list) res.Add(new KeyValuePair<string, int>((string)o.GetType().GetProperty("FileName").GetValue(o, null), (int)o.GetType().GetProperty("FileSize").GetValue(o, null)));
            return res;
        }
        public bool Space(out long free, out long total)
        {
            free = total = 0;
            object c = CoreObj(); if (c == null) return false;
            var spT = c.GetType().Assembly.GetType("iGameAPI.LCD.CSharp.LCD3SDSpace");
            object[] a = { Activator.CreateInstance(spT) };
            bool ok = (bool)c.GetType().GetMethod("LCD3_GetSDSpace", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Invoke(c, a);
            if (!ok) return false;
            free = Convert.ToInt64(spT.GetProperty("FreeSpace").GetValue(a[0], null));
            total = Convert.ToInt64(spT.GetProperty("TotalSpace").GetValue(a[0], null));
            return true;
        }
        public void Delete(string lcdName) { L3.DeleteMov(lcdName); }
        public void SetBackground(string lcdName) { L3.SetBackground(lcdName); }
        // 只设 MovName 不会发任何东西（要等库的定时器发）。这里直接发一次“显示动画”指令，
        // 和定时器在动画页发的是同一条；屏幕收到后自己循环播放，不需要一直开着实时刷新。
        public bool PlayMovie(string lcdName)
        {
            L3.MovName = lcdName;
            object c = CoreObj();
            var m = c.GetType().GetMethod("LCD3_SingleDisplay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return (bool)m.Invoke(c, new object[] { Mode.Movie });
        }

        public void Start() { Core.Start(); Started = true; }
        public void Stop() { try { Core.Stop(); } catch { } Started = false; }
        public void PushCpu(CpuData c, RamData r)
        {
            L3.SetCpuInfo(new CpuInfo { Name = c.Name, Load = c.Load, CoreTemp = c.Temp, CoreClockValue = c.Clock, CoreClockMax = c.ClockMax });
            L3.SetDramInfo(new DramInfo { TotalMemory = r.TotalGB, UsedMemory = r.UsedGB, AvailableMemory = r.FreeGB });
        }
        public void Close()
        {
            try { Core.Stop(); } catch { }
            try { Core.Close(); } catch { }
            try { NvCard.UnloadNVAPI(); } catch { }
        }
    }

    // ---------------------------------------------------------------- CPU / 内存读数
    class CpuData { public string Name = ""; public double Load, Temp, Clock, ClockMax; }
    class RamData { public double TotalGB, UsedGB, FreeGB; }

    static class Sensors
    {
        static PerformanceCounter load, freq;
        static string cpuName = ""; static double clockMax = 0;

        [StructLayout(LayoutKind.Sequential)]
        class MemStatus
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MemStatus));
            public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MemStatus s);

        public static CpuData Cpu()
        {
            if (load == null)
            {
                load = new PerformanceCounter("Processor", "% Processor Time", "_Total"); load.NextValue();
                try { freq = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total"); freq.NextValue(); } catch { }
                try
                {
                    var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                    cpuName = ((string)k.GetValue("ProcessorNameString") ?? "CPU").Trim();
                    clockMax = Convert.ToDouble(k.GetValue("~MHz") ?? 0);
                }
                catch { }
            }
            var d = new CpuData { Name = cpuName, Load = Math.Round(load.NextValue(), 1), ClockMax = clockMax };
            try { d.Clock = freq != null ? freq.NextValue() : clockMax; } catch { d.Clock = clockMax; }
            d.Temp = HwInfoTemp();
            return d;
        }

        public static RamData Ram()
        {
            var m = new MemStatus(); GlobalMemoryStatusEx(m);
            double gb = 1024.0 * 1024 * 1024;
            return new RamData { TotalGB = m.ullTotalPhys / gb, FreeGB = m.ullAvailPhys / gb, UsedGB = (m.ullTotalPhys - m.ullAvailPhys) / gb };
        }

        // 可选：读 HWiNFO 共享内存里的 "CPU Package" 温度。没开共享内存就返回 0。
        static double HwInfoTemp()
        {
            try
            {
                using (var mmf = MemoryMappedFile.OpenExisting("Global\\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read))
                using (var a = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    uint off = a.ReadUInt32(32), size = a.ReadUInt32(36), num = a.ReadUInt32(40);
                    double best = 0;
                    for (uint i = 0; i < num; i++)
                    {
                        long p = off + (long)i * size;
                        uint type = a.ReadUInt32(p);
                        if (type != 1) continue; // 温度
                        byte[] lab = new byte[128]; a.ReadArray(p + 12, lab, 0, 128);
                        string label = Encoding.Default.GetString(lab).TrimEnd('\0');
                        if (label.IndexOf("CPU Package", StringComparison.OrdinalIgnoreCase) >= 0 || label.StartsWith("CPU (Tctl", StringComparison.OrdinalIgnoreCase))
                        { best = a.ReadDouble(p + 284); break; }
                    }
                    return Math.Round(best, 1);
                }
            }
            catch { return 0; }
        }
    }

    // ---------------------------------------------------------------- 图片 -> .savi
    static class Savi
    {
        public static string Prepare()
        {
            string work = Paths.Work;
            string exe = Path.Combine(work, "tools", "SAVI.exe");
            if (!File.Exists(exe))
            {
                string src = Path.Combine(Vendor.Root, "SAVIConverter");
                CopyDir(Path.Combine(src, "tools"), Path.Combine(work, "tools"));
                CopyDir(Path.Combine(src, "configs"), Path.Combine(work, "configs"));
            }
            return work;
        }
        static void CopyDir(string s, string d)
        {
            Directory.CreateDirectory(d);
            foreach (string f in Directory.GetFiles(s)) File.Copy(f, Path.Combine(d, Path.GetFileName(f)), true);
            foreach (string sub in Directory.GetDirectories(s)) CopyDir(sub, Path.Combine(d, Path.GetFileName(sub)));
        }

        static string Run(string exe, string args, string cwd, int timeoutMs)
        {
            var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi))
            {
                var err = new StringBuilder();
                p.OutputDataReceived += (s, e) => { };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) err.AppendLine(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } throw new TimeoutException(Path.GetFileName(exe) + " 超时"); }
                p.WaitForExit();
                return err.ToString();
            }
        }

        // fit: contain(保持比例加黑边) / cover(裁切填满) / stretch(拉伸)
        public static string Convert(string input, string fit, string lcdName, Action<string> log)
        {
            string work = Prepare();
            string ext = Path.GetExtension(input).ToLowerInvariant();
            bool still = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp";
            string scale = fit == "cover" ? "scale=480:128:force_original_aspect_ratio=increase,crop=480:128"
                         : fit == "stretch" ? "scale=480:128"
                         : "scale=480:128:force_original_aspect_ratio=decrease,pad=480:128:(ow-iw)/2:(oh-ih)/2:black";
            string pre = still ? "-loop 1 -t 1 " : "";
            string rate = still ? "10" : "30";
            string gif = Path.Combine(work, "input.gif");
            if (File.Exists(gif)) File.Delete(gif);
            string ff = Path.Combine(work, "tools", "ffmpeg.exe");
            string vf = string.Format("fps={0},{1},split[a][b];[a]palettegen[p];[b][p]paletteuse", rate, scale);
            string args = string.Format("-y {0}-i \"{1}\" -t 30 -vf \"{2}\" \"{3}\"", pre, input, vf, gif);
            log("ffmpeg 预处理 …");
            Run(ff, args, work, 120000);
            if (!File.Exists(gif) || new FileInfo(gif).Length == 0) throw new Exception("ffmpeg 没有生成 gif，输入文件可能不是支持的图片或视频。");

            string bin = Path.Combine(work, "Bin");
            string outSavi = Path.Combine(bin, "input.savi");
            if (File.Exists(outSavi)) File.Delete(outSavi);
            log("SAVI 转换 …");
            Run(Path.Combine(work, "tools", "SAVI.exe"), "-input:\".\\input.avi\" -cfg:\".\\configs\\giftosavi_singledisplay.xml\"", work, 180000);
            string obj = Path.Combine(work, "Obj");
            try { if (Directory.Exists(obj)) Directory.Delete(obj, true); } catch { }
            if (!File.Exists(outSavi)) throw new Exception("SAVI 转换失败，没有生成 .savi 文件。");
            string dest = Path.Combine(work, "out"); Directory.CreateDirectory(dest);
            string final = Path.Combine(dest, lcdName);
            File.Copy(outSavi, final, true);
            log(string.Format("转换完成: {0} KB", new FileInfo(final).Length / 1024));
            return final;
        }

        public static bool ValidName(string n) { return Regex.IsMatch(n, @"^[A-Za-z0-9_]{1,8}\.savi$"); }
    }

    // ---------------------------------------------------------------- 命令行
    static class Cli
    {
        public static int Run(string[] a)
        {
            var dev = new Dev();
            string err = dev.Connect(m => Console.WriteLine(m));
            if (err != null) { Console.WriteLine("连接失败: " + err); return 2; }
            try
            {
                string cmd = a[0].ToLowerInvariant();
                var cfg = Cfg.Load();
                switch (cmd)
                {
                    case "status": Console.WriteLine(dev.Status()); break;
                    case "brightness": dev.ApplyBasic(dev.L3.BackgroundOpacity, int.Parse(a[1]), dev.L3.Enabled, dev.L3.Dir); break;
                    case "opacity": dev.ApplyBasic(int.Parse(a[1]), dev.L3.Brightness, dev.L3.Enabled, dev.L3.Dir); break;
                    case "enable": dev.ApplyBasic(dev.L3.BackgroundOpacity, dev.L3.Brightness, a[1] == "on" || a[1] == "1", dev.L3.Dir); break;
                    case "dir": dev.ApplyBasic(dev.L3.BackgroundOpacity, dev.L3.Brightness, dev.L3.Enabled, int.Parse(a[1])); break;
                    case "modes":
                        {
                            var list = a[1].Split(',').Select(x => (Mode)int.Parse(x)).ToList();
                            dev.ApplyModes(list, a.Length > 2 ? int.Parse(a[2]) : dev.L3.SwitchTime / 1000, true);
                            break;
                        }
                    case "text":
                        {
                            var c = a.Length > 3 ? ParseColor(a[3]) : System.Drawing.Color.Red;
                            dev.SetText(a.Length > 2 ? int.Parse(a[2]) : 0, c, a[1], true); break;
                        }
                    case "notext": dev.SetText(0, System.Drawing.Color.Red, "", false); break;
                    case "upload":
                        {
                            string file = a[1], name = a.Length > 2 ? a[2] : "img1.savi", fit = a.Length > 3 ? a[3] : "contain";
                            if (!Savi.ValidName(name)) { Console.WriteLine("屏幕上的文件名只能是 1-8 位字母数字加 .savi"); return 1; }
                            string savi = file.EndsWith(".savi", StringComparison.OrdinalIgnoreCase) ? file : Savi.Convert(file, fit, name, Console.WriteLine);
                            Console.WriteLine(string.Format("文件 {0:N0} 字节", new FileInfo(savi).Length));
                            Console.WriteLine("上传中 …");
                            bool ok = dev.UploadWait(name, savi, Console.WriteLine, 180);
                            Console.WriteLine(ok ? "上传完成" : "上传没有完成");
                            break;
                        }
                    case "files":
                        {
                            var fl = dev.ListFiles();
                            if (fl == null) { Console.WriteLine("读取文件列表失败"); return 1; }
                            Console.WriteLine("屏幕上的文件: " + fl.Count);
                            foreach (var kv in fl) Console.WriteLine(string.Format("  {0}  {1:N0} 字节", kv.Key, kv.Value));
                            long fr, tot;
                            if (dev.Space(out fr, out tot)) Console.WriteLine(string.Format("空间: 剩余 {0:N0} / 总共 {1:N0}（单位按库原样）", fr, tot));
                            else Console.WriteLine("读取剩余空间失败");
                            return 0;
                        }
                    case "revert":
                        if (string.IsNullOrEmpty(cfg.Current)) { Console.WriteLine("没有设定默认动画"); return 0; }
                        a = new[] { "play", cfg.Current };
                        goto case "play";
                    case "flash":   // 窗口程序没运行时，无法定时恢复，只播放
                    case "play":
                        {
                            var fl = dev.ListFiles();
                            if (fl == null || !fl.Any(k => k.Key == a[1])) { Console.WriteLine("屏幕上没有 " + a[1] + "，不发送"); return 1; }
                            Console.WriteLine("发送播放指令: " + (dev.PlayMovie(a[1]) ? "成功" : "失败"));
                            break;
                        }
                    case "bg": dev.SetBackground(a[1]); break;
                    case "delete": dev.Delete(a[1]); break;
                    default: Console.WriteLine("未知命令"); return 1;
                }
                if (cmd != "status") Console.WriteLine("OK  " + dev.Status());
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("出错: " + ex); return 1; }
            finally { dev.Close(); }
        }
        static System.Drawing.Color ParseColor(string s)
        {
            s = s.TrimStart('#');
            return System.Drawing.Color.FromArgb(Convert.ToInt32(s.Substring(0, 2), 16), Convert.ToInt32(s.Substring(2, 2), 16), Convert.ToInt32(s.Substring(4, 2), 16));
        }
    }

    // ---------------------------------------------------------------- 主窗口
    partial class MainForm : Form
    {
        Dev dev = new Dev();
        Cfg cfg = Cfg.Load();
        bool connected, loading;
        // 上传互斥：为 true 期间，任何路径（联动/挂件定时器、管道指令、界面按钮、重新连接、关闭窗口）都不得写串口。
        // 只在 UI 线程读写；DoUpload 开始时置位，上传线程结束后在 UI 线程里清除。
        volatile bool uploading;
        Label status = new Label();
        TextBox logBox = new TextBox();
        // 基本
        CheckBox chkEnable = new CheckBox();
        TrackBar tbBri = new TrackBar(), tbOpa = new TrackBar();
        Label lbBri = new Label(), lbOpa = new Label();
        ComboBox cmbDir = new ComboBox();
        System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 350 };
        // 页面
        CheckedListBox clb = new CheckedListBox();
        NumericUpDown numSec = new NumericUpDown();
        CheckBox chkAuto = new CheckBox();
        // 图片
        TextBox txtFile = new TextBox(), txtName = new TextBox();
        ComboBox cmbFit = new ComboBox();
        RadioButton rbMovie = new RadioButton(), rbBg = new RadioButton();
        ProgressBar pb = new ProgressBar();
        ListBox lstFiles = new ListBox();
        Label lbSpace = new Label();
        Button btnUpload = new Button();
        System.Windows.Forms.Timer upTimer = new System.Windows.Forms.Timer { Interval = 250 };
        // 文字
        TextBox txtText = new TextBox();
        NumericUpDown numFont = new NumericUpDown();
        Button btnColor = new Button();
        CheckBox chkShowText = new CheckBox();
        System.Drawing.Color textColor = System.Drawing.Color.Red;
        // 实时
        CheckBox chkLive = new CheckBox(), chkAutostart = new CheckBox();
        Label lbLive = new Label();
        System.Windows.Forms.Timer liveTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        NotifyIcon tray = new NotifyIcon();
        bool reallyExit;
        bool startInTray;
        // 新界面（WebView2）是主窗口；经典 WinForms 窗口只作为备用（--classic 启动，或托盘菜单调出，或 WebView2 起不来时自动回退）。
        bool classicUi, shuttingDown;
        static Icon AppIcon() { try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; } catch { return SystemIcons.Application; } }
        public bool Preview;
        TabControl tabs = new TabControl();
        System.Windows.Forms.Timer revertTimer = new System.Windows.Forms.Timer();
        string playing = "";

        // ---------------- 外部指令通道（给 Claude Code 钩子等脚本用）
        // 指令：play <文件> / flash <文件> <秒> / revert / status
        void StartPipe()
        {
            var th = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var ps = new NamedPipeServerStream(Program.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte))
                        {
                            ps.WaitForConnection();
                            var rd = new StreamReader(ps, new UTF8Encoding(false));
                            var wr = new StreamWriter(ps, new UTF8Encoding(false)) { AutoFlush = true };
                            string line = rd.ReadLine() ?? "";
                            string resp;
                            try { resp = (string)Invoke(new Func<string>(() => HandleCommand(line))); }
                            catch (Exception ex) { resp = "ERR " + ex.Message; }
                            wr.WriteLine(resp);
                            try { ps.WaitForPipeDrain(); } catch { }
                        }
                    }
                    catch (Exception ex) { Log("指令通道出错: " + ex.Message); Thread.Sleep(500); }
                }
            });
            th.IsBackground = true; th.Start();
        }

        string HandleCommand(string line)
        {
            var a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return "ERR 空指令";
            // 上传期间只放行不碰串口的指令；其余（play/flash/revert/widget/health/night*/idle-sync/Claude 旧指令等）一律拒绝
            if (uploading && a[0]!="status" && a[0]!="widget-status" && a[0]!="link-status" && a[0]!="quit") return "ERR 上传进行中";
            if(a[0]=="night-status")return NightStatus();
            if(a[0]=="night" && a.Length==2 && (a[1]=="on" || a[1]=="off"))return SetNightMode(a[1]=="on");
            if (a.Length>1 && (a[0]=="play" || a[0]=="flash") &&
                (a[1]=="ccwork.savi" || a[1]=="ccask.savi" || a[1]=="ccdone.savi")) return LegacyClaude(a[1]);
            if (a[0]=="webui") { OpenWebUi(); return "OK 新界面已打开"; }
            if (a[0]=="idle-sync") return PreviewIdleSync();
            if (a[0]=="quit") {
                if(!btnUpload.Enabled)return "ERR 上传进行中";
                reallyExit=true;BeginInvoke(new Action(Close));return "OK 正在退出";
            }
            if (a[0]=="health") {
                if(!connected || File.Exists(StorageOps.Flag))return "ERR 未连接或维护中";
                int mode=-1;bool ok=Quiet(()=>StorageOps.ReadOpening(dev,out mode));
                return ok ? "OK LCD settings read; opening="+mode : "ERR LCD read failed";
            }
            if (line.StartsWith("widget ")) return WidgetCommand(line.Substring(7));
            if (a[0]=="widget-status") return "OK "+new JavaScriptSerializer().Serialize(new {active=widgetOwnsScreen,kind=displayJobs.Current(DateTime.UtcNow)?.Kind,sent=widgetSent,error=widgetError});
            if (a[0]=="link-status") return "OK "+new JavaScriptSerializer().Serialize(new {state=linkShown,receivedHookEvents=receivedHookEvents,lastHookEvent=lastHookEvent,sessions=linkState.Snapshot(),signalAck=signalAckState,ackAgeSeconds=(DateTime.UtcNow-signalAckTime).TotalSeconds});
            if (File.Exists(StorageOps.Flag)) return "ERR 屏幕处于存储维护模式";
            if (!connected) return "ERR 屏幕未连接";
            int secs;
            switch (a[0].ToLowerInvariant())
            {
                case "status": return "OK " + dev.Status() + " | 正在播放=" + playing + " 默认=" + cfg.Current;
                case "play": if(widgetOwnsScreen){displayJobs.Apply(new DisplayRequest{action="clear"},DateTime.UtcNow);RestoreWidget();} return a.Length > 1 ? PlayExternal(a[1], 0) : "ERR 缺少文件名";
                case "flash": return a.Length > 1 ? PlayExternal(a[1], a.Length > 2 && int.TryParse(a[2], out secs) ? secs : 60) : "ERR 缺少文件名";
                case "revert": revertTimer.Stop(); return string.IsNullOrEmpty(cfg.Current) ? "OK 没有默认动画" : PlayExternal(cfg.Current, 0);
                default: return "ERR 未知指令 " + a[0];
            }
        }

        // 外部指令播放：不改用户的默认动画；同一个文件已经在播就不再发，避免频繁写屏幕
        string PlayExternal(string name, int revertAfterSec)
        {
            if(uploading)return "ERR 上传进行中";
            if(linkCfg.NightMode)return "ERR 请先恢复灯光和屏幕";
            if (!lstFiles.Items.Contains(name)) return "ERR 屏幕上没有 " + name;
            revertTimer.Stop();
            if (revertAfterSec > 0) { revertTimer.Interval = revertAfterSec * 1000; revertTimer.Start(); }
            if (name == playing) return "OK 已在播放";
            bool ok = Quiet(() => dev.PlayMovie(name));
            if (ok) { playing = name; MarkMovieStarted(name); }
            Log("外部指令：播放 " + name + (revertAfterSec > 0 ? "，" + revertAfterSec + " 秒后恢复默认" : ""));
            return ok ? "OK" : "ERR 发送失败";
        }

        // 开发用：把每个页签按真实屏幕截图存成 png，方便检查排版（会在屏幕上闪一下）。
        public void Shoot(string dir)
        {
            Directory.CreateDirectory(dir);
            TopMost = true; Activate();
            for (int i = 0; i < tabs.TabPages.Count; i++)
            {
                tabs.SelectedIndex = i; Application.DoEvents();
                if (tabs.TabPages[i].Text == "实时数据")
                {
                    loading = true; chkLive.Checked = true; loading = false;
                    for (int k = 0; k < 3; k++) { lbLive.Text = string.Format("CPU {0}%  2808 MHz  0°C    内存 22.2 / 63.9 GB", k + 1); Application.DoEvents(); Thread.Sleep(400); }
                }
                Thread.Sleep(300); Application.DoEvents();
                using (var bmp = new Bitmap(Width, Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(Left, Top, 0, 0, Size);
                    bmp.Save(Path.Combine(dir, string.Format("tab{0}_{1}.png", i, tabs.TabPages[i].Text.Replace(" / ", "_"))), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }

        public MainForm(bool inTray)
        {
            startInTray = inTray;
            var cmdArgs = Environment.GetCommandLineArgs();
            classicUi = Array.IndexOf(cmdArgs, "--classic") >= 0;
            // 启动时不让经典窗口闪现：先全透明、不进任务栏，Shown 里再隐藏
            if (!classicUi && Array.IndexOf(cmdArgs, "--shot") < 0) { Opacity = 0; ShowInTaskbar = false; }
            Icon = AppIcon();
            SuspendLayout();
            Text = "显卡屏幕控制";
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 640);
            MinimumSize = new Size(560, 600);
            StartPosition = FormStartPosition.CenterScreen;

            status.Dock = DockStyle.Top; status.Height = 34; status.Padding = new Padding(10, 9, 0, 0); status.Text = "连接中 …";
            var btnRe = new Button { Text = "重新连接", Dock = DockStyle.Right, Width = 90 };
            btnRe.Click += (s, e) => Connect();
            var top = new Panel { Dock = DockStyle.Top, Height = 34 };
            top.Controls.Add(status); top.Controls.Add(btnRe);

            logBox.Multiline = true; logBox.ReadOnly = true; logBox.ScrollBars = ScrollBars.Vertical; logBox.Dock = DockStyle.Bottom; logBox.Height = 110;
            logBox.Font = new Font("Consolas", 8.5f);

            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(TabBasic()); tabs.TabPages.Add(TabModes()); tabs.TabPages.Add(TabImage()); tabs.TabPages.Add(TabText()); tabs.TabPages.Add(TabLive()); tabs.TabPages.Add(TabLinks()); tabs.TabPages.Add(TabWidgets()); tabs.TabPages.Add(TabStorage());
            UpdateNightUi();
            Controls.Add(tabs); Controls.Add(logBox); Controls.Add(top);

            debounce.Tick += (s, e) => { debounce.Stop(); ApplyBasic(); };
            upTimer.Tick += (s, e) => { try { pb.Value = Math.Max(0, Math.Min(100, dev.UploadPercent)); } catch { } };
            liveTimer.Tick += (s, e) => LiveTick();

            tray.Icon = Icon; tray.Text = "显卡屏幕控制";
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示窗口", null, (s, e) => ShowMe());
            nightMenu=new ToolStripMenuItem();nightMenu.Click+=(s,e)=>Log(SetNightMode(!linkCfg.NightMode));menu.Items.Add(nightMenu);UpdateNightUi();
            menu.Items.Add("经典界面（备用）", null, (s, e) => ShowClassic());
            menu.Items.Add("退出", null, (s, e) => { reallyExit = true; Close(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += (s, e) => ShowMe();

            ResumeLayout(false);
            PerformAutoScale();

            Shown += (s, e) =>
            {
                if (Preview) return;
                // 第二次启动程序时，Program 会触发这个事件，把这里的窗口调出来
                var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
                var waiter = new Thread(() => { while (true) { showEvt.WaitOne(); try { BeginInvoke(new Action(ShowMe)); } catch { return; } } });
                waiter.IsBackground = true; waiter.Start();
                revertTimer.Tick += (s2, e2) =>
                {
                    revertTimer.Stop();
                    if (uploading) { revertTimer.Interval = 3000; revertTimer.Start(); return; }   // 上传中不写串口，传完再恢复
                    if (!linkCfg.NightMode && connected && !string.IsNullOrEmpty(cfg.Current) && lstFiles.Items.Contains(cfg.Current) && playing != cfg.Current)
                    {
                        if (Quiet(() => dev.PlayMovie(cfg.Current))) { playing = cfg.Current; MarkMovieStarted(cfg.Current); Log("恢复默认动画 " + cfg.Current); }
                    }
                };
                StartPipe();
                Connect();
                StartLinks();
                StartWidgets();
                tray.Visible = true;   // 托盘图标常驻（窗口开着时也在），随时能找到程序、右键退出
                if (classicUi) { if (startInTray) Hide(); }
                else
                {
                    Hide(); Opacity = 1;   // 经典窗口退到后台；新界面是主窗口
                    if (!startInTray) OpenWebUi();
                }
            };
            FormClosing += OnClosing;
        }

        // ---------------- 页面搭建
        TabPage TabBasic()
        {
            var t = new TabPage("基本");
            var scroll = new Panel {Dock=DockStyle.Fill,AutoScroll=true};
            var grid = new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=3,RowCount=7,Padding=new Padding(16)};
            grid.SuspendLayout();
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for(int row=0;row<7;row++)grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddNightControls(grid);
            chkEnable.Text = "启用屏幕"; chkEnable.AutoSize = true; chkEnable.Margin=new Padding(0,10,0,16); chkEnable.CheckedChanged += (s, e) => Changed();
            grid.Controls.Add(chkEnable,0,2);grid.SetColumnSpan(chkEnable,3);
            AddSlider(grid, "亮度", tbBri, lbBri, 0, 10, 3);
            AddSlider(grid, "背景不透明度（%）", tbOpa, lbOpa, 0, 100, 4);
            var l = new Label { Text = "屏幕方向", AutoSize = true,Anchor=AnchorStyles.Left,Margin=new Padding(0,0,20,0) };
            cmbDir.DropDownStyle = ComboBoxStyle.DropDownList; cmbDir.Items.AddRange(new object[] { "0", "1", "2", "3" });
            cmbDir.Width = 80;cmbDir.MinimumSize=new Size(80,0); cmbDir.Margin=new Padding(0,0,0,12); cmbDir.SelectedIndexChanged += (s, e) => Changed();
            grid.Controls.Add(l,0,5);grid.Controls.Add(cmbDir,1,5);
            var note = new Label { Text = "拖动滑块后约 0.3 秒自动生效。", AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,12,0,0), ForeColor = System.Drawing.Color.Gray };
            grid.Controls.Add(note,0,6);grid.SetColumnSpan(note,3);
            scroll.Controls.Add(grid);t.Controls.Add(scroll);grid.ResumeLayout(true);
            return t;
        }
        TabPage TabStorage()
        {
            var tab = new TabPage("存储 / 开机") { AutoScroll = true };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
            var note = new Label { AutoSize = true, MaximumSize = new Size(510, 0), Text = "屏幕支持 USB 存储维护。进入后暂停播放和外部指令，可以浏览文件、备份、替换或恢复开机动画。\r\n\r\n固件 1.22 的开机方案 0 使用 op.savi / oploop.savi；方案 1 使用 op1.savi / op1loop.savi。普通图片动画在 mov 文件夹。\r\n\r\n目前恢复普通控制需要安全弹出，再整机断电约 30 秒后开机。", Margin = new Padding(0, 0, 0, 18) };
            flow.Controls.Add(note);
            var info = new Label { AutoSize = true, MaximumSize = new Size(510, 0), Text = "开机方案尚未读取" };
            var read = new Button { Text = "读取当前开机方案", AutoSize = true, MinimumSize = new Size(200, 36) };
            read.Click += (sender, ev) => { if (!connected || uploading) return; try { int mode = -1; bool ok = Quiet(() => StorageOps.ReadOpening(dev, out mode)); info.Text = ok ? "当前开机方案：" + mode : "读取失败"; } catch (Exception ex) { Log(ex.Message); } };
            flow.Controls.Add(read); flow.Controls.Add(info);
            var preview = new Button { Text = "在小屏幕播放原开机动画", AutoSize = true, MinimumSize = new Size(230, 36) };
            preview.Click += (sender, ev) => { if (!connected || uploading) return; try { chkLive.Checked = false; liveTimer.Stop(); revertTimer.Stop(); dev.Stop(); playing = ""; Log(StorageOps.PreviewOpening(dev) ? "开机动画播放指令已发送；返回图片页可恢复默认动画。" : "开机动画指令发送失败"); } catch (Exception ex) { Log(ex.Message); } };
            flow.Controls.Add(preview);
            var manage = new Button { Text = "打开存储维护…", AutoSize = true, MinimumSize = new Size(230, 36) };
            manage.Click += (sender, ev) => {
                if (uploading) { Log("上传进行中，请等待上传结束再打开存储维护"); return; }
                EnterStorageMaintenance();
            };
            flow.Controls.Add(manage); tab.Controls.Add(flow); return tab;
        }
        void AddSlider(TableLayoutPanel t, string name, TrackBar tb, Label val, int min, int max, int row)
        {
            t.Controls.Add(new Label { Text = name, AutoSize = true,Anchor=AnchorStyles.Left,Margin=new Padding(0,0,20,12) },0,row);
            tb.Minimum = min; tb.Maximum = max; tb.TickFrequency = Math.Max(1, (max - min) / 10);
            tb.Dock=DockStyle.Fill;tb.Margin=new Padding(0,0,12,12);tb.Scroll += (s, e) => { val.Text = tb.Value.ToString(); Changed(); };
            val.AutoSize = true;val.MinimumSize=new Size(34,0);val.Anchor=AnchorStyles.Left;val.Margin=new Padding(0,0,0,12);
            t.Controls.Add(tb,1,row);t.Controls.Add(val,2,row);
        }

        TabPage TabModes()
        {
            var t = new TabPage("轮播页面");
            t.Controls.Add(new Label { Text = "勾选要在屏幕上轮流显示的页面：", Location = new Point(16, 14), AutoSize = true });
            clb.Location = new Point(16, 40); clb.Size = new Size(250, 300); clb.CheckOnClick = true;
            for (int i = 0; i < Dev.ModeNames.Length; i++) clb.Items.Add(Dev.ModeNames[i]);
            t.Controls.Add(clb);
            t.Controls.Add(new Label { Text = "每页停留（秒）", Location = new Point(290, 44), AutoSize = true });
            numSec.Minimum = 1; numSec.Maximum = 120; numSec.Value = 10; numSec.Location = new Point(400, 40); numSec.Width = 70; t.Controls.Add(numSec);
            chkAuto.Text = "自动轮播"; chkAuto.Checked = true; chkAuto.Location = new Point(290, 80); chkAuto.AutoSize = true; t.Controls.Add(chkAuto);
            var b = new Button { Text = "应用", Location = new Point(290, 120), Width = 100, Height = 32 };
            b.Click += (s, e) => ApplyModes(); t.Controls.Add(b);
            t.Controls.Add(new Label { Text = "CPU / 内存页的数据需要在“实时数据”里\r\n打开推送；GPU 页由显卡库自己更新。", Location = new Point(290, 170), Size = new Size(280, 60), ForeColor = System.Drawing.Color.Gray });
            return t;
        }

        TabPage TabImage()
        {
            var t = new TabPage("图片 / 动画");
            int y = 14;
            t.Controls.Add(new Label { Text = "文件（png jpg gif mp4 …）", Location = new Point(16, y), AutoSize = true }); y += 22;
            txtFile.Location = new Point(16, y); txtFile.Width = 430;
            var br = new Button { Text = "浏览…", Location = new Point(456, y - 2), Width = 90 };
            br.Click += (s, e) =>
            {
                var d = new OpenFileDialog { Filter = "图片/动画/视频|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.mp4;*.avi;*.mov;*.mkv;*.webm|所有文件|*.*" };
                if (d.ShowDialog() == DialogResult.OK) txtFile.Text = d.FileName;
            };
            t.Controls.Add(txtFile); t.Controls.Add(br); y += 38;
            t.Controls.Add(new Label { Text = "适配方式", Location = new Point(16, y + 4), AutoSize = true });
            cmbFit.DropDownStyle = ComboBoxStyle.DropDownList; cmbFit.Items.AddRange(new object[] { "保持比例，加黑边", "裁切填满", "拉伸" });
            cmbFit.SelectedIndex = 0; cmbFit.Location = new Point(110, y); cmbFit.Width = 160; t.Controls.Add(cmbFit);
            t.Controls.Add(new Label { Text = "屏幕上的文件名", Location = new Point(290, y + 4), AutoSize = true });
            txtName.Text = "img1.savi"; txtName.Location = new Point(400, y); txtName.Width = 146; t.Controls.Add(txtName); y += 38;
            rbMovie.Text = "作为全屏动画（“自定义动画”页）"; rbMovie.Checked = true; rbMovie.Location = new Point(16, y); rbMovie.AutoSize = true;
            rbBg.Text = "作为数据页的背景"; rbBg.Location = new Point(300, y); rbBg.AutoSize = true; t.Controls.Add(rbMovie); t.Controls.Add(rbBg); y += 34;
            btnUpload.Text = "转换并上传"; btnUpload.Location = new Point(16, y); btnUpload.Width = 130; btnUpload.Height = 32; btnUpload.Click += (s, e) => DoUpload();
            t.Controls.Add(btnUpload);
            pb.Location = new Point(160, y + 4); pb.Width = 386; pb.Height = 24; t.Controls.Add(pb); y += 46;
            t.Controls.Add(new Label { Text = "屏幕上现有的文件：", Location = new Point(16, y), AutoSize = true });
            lbSpace.Location = new Point(160, y); lbSpace.AutoSize = true; lbSpace.ForeColor = System.Drawing.Color.Gray; t.Controls.Add(lbSpace); y += 22;
            lstFiles.Location = new Point(16, y); lstFiles.Size = new Size(250, 100); t.Controls.Add(lstFiles);
            var bUse = new Button { Text = "设为当前", Location = new Point(280, y), Width = 110 };
            bUse.Click += (s, e) => UseSelected();
            var bDel = new Button { Text = "从屏幕删除", Location = new Point(280, y + 38), Width = 110 };
            bDel.Click += (s, e) => DeleteSelected();
            var bRef = new Button { Text = "刷新列表", Location = new Point(280, y + 76), Width = 110 };
            bRef.Click += (s, e) => RefreshFiles();
            t.Controls.Add(bUse); t.Controls.Add(bDel); t.Controls.Add(bRef);
            return t;
        }

        TabPage TabText()
        {
            var t = new TabPage("文字");
            t.Controls.Add(new Label { Text = "显示的文字", Location = new Point(16, 16), AutoSize = true });
            txtText.Location = new Point(16, 40); txtText.Width = 520; t.Controls.Add(txtText);
            t.Controls.Add(new Label { Text = "字号（从 0 起）", Location = new Point(16, 84), AutoSize = true });
            numFont.Minimum = 0; numFont.Maximum = 9; numFont.Location = new Point(130, 80); numFont.Width = 60; t.Controls.Add(numFont);
            btnColor.Text = "颜色"; btnColor.Location = new Point(220, 78); btnColor.Width = 80; btnColor.BackColor = textColor;
            btnColor.Click += (s, e) => { var d = new ColorDialog { Color = textColor }; if (d.ShowDialog() == DialogResult.OK) { textColor = d.Color; btnColor.BackColor = textColor; } };
            t.Controls.Add(btnColor);
            chkShowText.Text = "在屏幕上显示这段文字"; chkShowText.Location = new Point(16, 124); chkShowText.AutoSize = true; t.Controls.Add(chkShowText);
            var b = new Button { Text = "应用", Location = new Point(16, 160), Width = 100, Height = 32 }; b.Click += (s, e) => ApplyText(); t.Controls.Add(b);
            return t;
        }

        TabPage TabLive()
        {
            var t = new TabPage("实时数据");
            // 用自动排列，控件高度跟着字体走，不会互相遮挡
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 12, 12, 12) };
            chkLive.Text = "【实验性】启动屏幕刷新：轮播数据页并推送 CPU / 内存数据"; chkLive.AutoSize = true; chkLive.Margin = new Padding(3, 3, 3, 6);
            chkLive.CheckedChanged += (s, e) => SetLive(chkLive.Checked);
            lbLive.AutoSize = true; lbLive.ForeColor = System.Drawing.Color.Gray; lbLive.Margin = new Padding(22, 0, 3, 24); lbLive.Text = " ";
            chkAutostart.Text = "开机自动启动（最小化到托盘，并恢复上次的设置）"; chkAutostart.AutoSize = true; chkAutostart.Margin = new Padding(3, 3, 3, 6);
            chkAutostart.Checked = IsAutostart();
            chkAutostart.CheckedChanged += (s, e) => { if (!loading) SetAutostart(chkAutostart.Checked); };
            var note = new Label
            {
                AutoSize = true, ForeColor = System.Drawing.Color.Gray, Margin = new Padding(3, 12, 3, 3),
                Text = "· 关闭窗口时，如果开着实时数据，程序会留在右下角托盘继续刷新屏幕；\r\n  右键托盘图标选“退出”才会真正关闭。再次双击程序会把托盘里的窗口调出来。\r\n" +
                       "· CPU 温度来自 HWiNFO 的共享内存（HWiNFO 设置里要打开 Shared Memory Support），\r\n  没开就显示 0。CPU 占用和内存不依赖任何软件。"
            };
            flow.Controls.Add(chkLive); flow.Controls.Add(lbLive); flow.Controls.Add(chkAutostart); flow.Controls.Add(note);
            t.Controls.Add(flow);
            return t;
        }

        // ---------------- 逻辑
        void Log(string m)
        {
            string line = DateTime.Now.ToString("HH:mm:ss ") + m + "\r\n";
            RecordLog(DateTime.Now.ToString("HH:mm:ss"), m);
            try { Directory.CreateDirectory(Paths.Data); File.AppendAllText(Path.Combine(Paths.Data, "log.txt"), DateTime.Now.ToString("MM-dd ") + line); } catch { }
            if (!IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => logBox.AppendText(line))); return; }
            logBox.AppendText(line);
        }

        void Connect()
        {
            if (uploading) { Log("上传进行中，请等待上传结束再重新连接"); return; }
            if(widgetOwnsScreen)StopWidgets();
            if (connected) { liveTimer.Stop(); dev.Close(); connected = false; dev = new Dev(); }
            bool maintenance = File.Exists(StorageOps.Flag);
            // Keep tabs selectable, but prevent their controls from touching a mounted disk.
            for (int i = 0; i < tabs.TabCount - 1; i++)
                foreach (Control control in tabs.TabPages[i].Controls) control.Enabled = !maintenance;
            if (maintenance)
            {
                loading = true;
                chkEnable.Checked = cfg.En;
                tbBri.Value = Math.Max(0, Math.Min(10, cfg.Bri)); lbBri.Text = tbBri.Value.ToString();
                tbOpa.Value = Math.Max(0, Math.Min(100, cfg.Opa)); lbOpa.Text = tbOpa.Value.ToString();
                cmbDir.SelectedIndex = Math.Max(0, Math.Min(3, cfg.Dir));
                for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, cfg.Modes != null && cfg.Modes.Contains((int)Dev.AllModes[i]));
                numSec.Value = Math.Max(1, Math.Min(120, cfg.Interval)); chkAuto.Checked = cfg.Auto;
                txtText.Text = cfg.Text; numFont.Value = Math.Max(0, Math.Min(9, cfg.FontSize)); chkShowText.Checked = cfg.ShowText;
                cmbFit.SelectedIndex = cfg.Fit == "cover" ? 1 : cfg.Fit == "stretch" ? 2 : 0;
                loading = false;
                status.Text = "维护模式：原功能已保留，恢复屏幕连接后可用。";
                status.ForeColor = System.Drawing.Color.DarkOrange;
                Log("全部原有页面已显示。屏幕仍被维护锁保护；请从“存储 / 开机”打开维护窗口并恢复普通控制。现有设置未修改。");
                return;
            }
            Log("连接屏幕 …");
            string err = dev.Connect(Log);
            if (err != null) { status.Text = "未连接：" + err; status.ForeColor = System.Drawing.Color.Firebrick; Log(err); return; }
            if (string.IsNullOrEmpty(dev.Com))
            {
                // 屏幕不回应串口握手时不要再往里写任何东西
                status.Text = "屏幕没有回应：关掉 iGameCenter 再点“重新连接”；还不行就完全断电重启一次（关机后关电源开关 30 秒）";
                status.ForeColor = System.Drawing.Color.DarkOrange;
                Log("找到显卡和 LCD，但屏幕串口握手失败，未连接");
                dev.Close(); dev = new Dev();
                return;
            }
            connected = true;
            status.Text = string.Format("已连接  {0} · {1} · 固件 {2}", dev.Gpu, dev.Com, dev.L3.FWVersion); status.ForeColor = System.Drawing.Color.SeaGreen;
            Log(dev.Status());
            // 屏幕被要求播放一个不存在的动画文件时固件会卡死，所以先确认文件在不在
            List<string> onScreen = null;
            try { var fl = dev.ListFiles(); if (fl != null) onScreen = fl.Select(k => k.Key).ToList(); } catch { }
            if (cfg.Modes != null && cfg.Modes.Contains((int)Mode.Movie) && (onScreen == null || !onScreen.Contains(cfg.Current)))
            {
                cfg.Modes = cfg.Modes.Where(m => m != (int)Mode.Movie).ToArray();
                if (cfg.Modes.Length == 0) cfg.Modes = new[] { (int)Mode.GpuTemperature, (int)Mode.Time };
                Log(string.Format("屏幕上没有找到动画文件“{0}”，已取消“自定义动画/图片”页（播放不存在的文件会让屏幕卡死）", cfg.Current));
                cfg.Save();
            }
            loading = true;
            if (cfg.Bri < 0) cfg.Bri = Math.Max(0, Math.Min(10, dev.L3.Brightness));
            if (cfg.Opa < 0) cfg.Opa = Math.Max(0, Math.Min(100, dev.L3.BackgroundOpacity));
            if (cfg.Dir < 0) cfg.Dir = Math.Max(0, Math.Min(3, dev.L3.Dir));
            if (cfg.Modes == null) cfg.Modes = dev.L3.SingleDisplayModes.Select(m => (int)m).ToArray();
            chkEnable.Checked = cfg.En; tbBri.Value = cfg.Bri; lbBri.Text = cfg.Bri.ToString(); tbOpa.Value = cfg.Opa; lbOpa.Text = cfg.Opa.ToString();
            cmbDir.SelectedIndex = cfg.Dir;
            for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, cfg.Modes.Contains((int)Dev.AllModes[i]));
            numSec.Value = Math.Max(1, Math.Min(120, cfg.Interval)); chkAuto.Checked = cfg.Auto;
            txtText.Text = cfg.Text; numFont.Value = Math.Max(0, Math.Min(9, cfg.FontSize)); chkShowText.Checked = cfg.ShowText;
            try { textColor = ColorTranslator.FromHtml("#" + cfg.TextColor); btnColor.BackColor = textColor; } catch { }
            cmbFit.SelectedIndex = cfg.Fit == "cover" ? 1 : cfg.Fit == "stretch" ? 2 : 0;
            chkLive.Checked = false;
            loading = false;
            RefreshFiles();   // 必须在 ApplyModes 之前，它要用屏幕上的文件列表做检查
            if(linkCfg.NightMode){dev.Stop();dev.ApplyNightScreen(cfg.Opa,cfg.Bri,false,cfg.Dir);nightScreenResult="屏幕已熄灭。";UpdateNightUi();return;}
            if (cfg.Exists || startInTray) { ApplyBasic(); ApplyModes(); }
            if (cfg.Live) chkLive.Checked = true;
            else if (!string.IsNullOrEmpty(cfg.Current) && !cfg.CurrentIsBg && onScreen != null && onScreen.Contains(cfg.Current))
            {
                // 不开实时刷新时，发一次播放指令，屏幕自己循环播放
                try
                {
                    bool ok = dev.PlayMovie(cfg.Current);
                    if (ok) { playing = cfg.Current; MarkMovieStarted(cfg.Current); }
                    Log(ok ? "已让屏幕播放 " + cfg.Current : "播放指令发送失败");
                }
                catch (Exception ex) { Log("播放出错: " + ex.Message); }
            }
        }

        // 需要屏幕应答的操作（读列表、删除、上传）不能和实时刷新同时用串口，否则应答会被打乱，
        // 屏幕可能卡在半截状态。这里先暂停刷新，做完再恢复。
        T Quiet<T>(Func<T> f)
        {
            bool was = dev.Started;
            if (was) { liveTimer.Stop(); dev.Stop(); Thread.Sleep(1200); }
            try { return f(); }
            finally { if (was) { try { dev.Start(); liveTimer.Start(); } catch { } } }
        }

        void RefreshFiles()
        {
            if (!connected || uploading) return;
            try
            {
                var fl = Quiet(() => dev.ListFiles());
                lstFiles.Items.Clear();
                if (fl == null) { lbSpace.Text = "（读取失败）"; return; }
                foreach (var kv in fl) lstFiles.Items.Add(kv.Key);
                fileSizes = fl.ToDictionary(kv => kv.Key, kv => kv.Value);
                long fr = 0, tot = 0;
                bool ok = Quiet(() => dev.Space(out fr, out tot));
                spaceKnown = ok; spaceFree = fr; spaceTotal = tot;
                lbSpace.Text = ok ? string.Format("{0} 个文件，剩余空间 {1:N0} / {2:N0}", fl.Count, fr, tot) : fl.Count + " 个文件";
            }
            catch (Exception ex) { Log("读取屏幕文件列表出错: " + ex.Message); }
        }

        void Changed() { if (!loading && connected) { debounce.Stop(); debounce.Start(); } }

        bool Guard() { if(uploading){Log("上传进行中，请等待上传结束");return false;} if(linkCfg.NightMode){Log("请先恢复灯光和屏幕");return false;} if(widgetOwnsScreen){Log("请先在进度 / 通知页恢复原画面");return false;} if (!connected) { Log("屏幕未连接"); return false; } return true; }

        void ApplyBasic()
        {
            if (!Guard()) return;
            try
            {
                dev.ApplyBasic(tbOpa.Value, tbBri.Value, chkEnable.Checked, cmbDir.SelectedIndex);
                cfg.En = chkEnable.Checked; cfg.Bri = tbBri.Value; cfg.Opa = tbOpa.Value; cfg.Dir = cmbDir.SelectedIndex; cfg.Save();
                Log(string.Format("已应用：启用={0} 亮度={1} 不透明度={2} 方向={3}", chkEnable.Checked, tbBri.Value, tbOpa.Value, cmbDir.SelectedIndex));
            }
            catch (Exception ex) { Log("出错: " + ex.Message); }
        }

        void ApplyModes()
        {
            if (!Guard()) return;
            try
            {
                var modes = new List<Mode>();
                for (int i = 0; i < clb.Items.Count; i++) if (clb.GetItemChecked(i)) modes.Add(Dev.AllModes[i]);
                if (modes.Count == 0) { Log("至少勾选一个页面"); return; }
                if (modes.Contains(Mode.Movie) && (string.IsNullOrEmpty(cfg.Current) || !lstFiles.Items.Contains(cfg.Current)))
                {
                    MessageBox.Show("屏幕上还没有可播放的动画（或者当前动画已被删除）。\r\n播放不存在的文件会让屏幕卡死，请先在“图片 / 动画”页上传并设为当前。", "不能勾选“自定义动画/图片”");
                    return;
                }
                dev.ApplyModes(modes, (int)numSec.Value, chkAuto.Checked);
                cfg.Modes = modes.Select(m => (int)m).ToArray(); cfg.Interval = (int)numSec.Value; cfg.Auto = chkAuto.Checked; cfg.Save();
                Log("已应用轮播：" + string.Join("、", modes.Select(m => Dev.ModeNames[Array.IndexOf(Dev.AllModes, m)])));
            }
            catch (Exception ex) { Log("出错: " + ex.Message); }
        }

        void ApplyText()
        {
            if (!Guard()) return;
            try
            {
                dev.SetText((int)numFont.Value, textColor, txtText.Text, chkShowText.Checked);
                cfg.Text = txtText.Text; cfg.FontSize = (int)numFont.Value; cfg.ShowText = chkShowText.Checked;
                cfg.TextColor = string.Format("{0:X2}{1:X2}{2:X2}", textColor.R, textColor.G, textColor.B); cfg.Save();
                Log("已应用文字");
            }
            catch (Exception ex) { Log("出错: " + ex.Message); }
        }

        void DoUpload()
        {
            if (!Guard()) return;
            string file = txtFile.Text.Trim('"', ' '), name = txtName.Text.Trim();
            if (!File.Exists(file)) { Log("文件不存在"); return; }
            if (!Savi.ValidName(name)) { Log("屏幕上的文件名只能是 1-8 位字母或数字，加 .savi，例如 img1.savi"); return; }
            string fit = cmbFit.SelectedIndex == 1 ? "cover" : cmbFit.SelectedIndex == 2 ? "stretch" : "contain";
            bool asMovie = rbMovie.Checked;
            uploading = true; btnUpload.Enabled = false; pb.Value = 0;
            debounce.Stop(); revertTimer.Stop();   // 挂起的亮度写入 / 定时恢复都不能在上传中触发
            // 上传全程串口必须空闲：先停掉实时刷新，传完再恢复
            bool wasLive = dev.Started;
            if (wasLive) { liveTimer.Stop(); dev.Stop(); Log("上传期间暂停实时刷新"); }
            var th = new Thread(() =>
            {
                bool ok = false;
                try
                {
                    string savi = Savi.Convert(file, fit, name, Log);
                    if (wasLive) Thread.Sleep(1500);
                    Log("上传到屏幕 …（不要关闭程序）");
                    BeginInvoke(new Action(() => upTimer.Start()));
                    ok = dev.UploadWait(name, savi, Log, 300);
                }
                catch (Exception ex) { Log("出错: " + ex.Message); }
                finally
                {
                    BeginInvoke(new Action(() =>
                    {
                        upTimer.Stop(); uploading = false; btnUpload.Enabled = true;
                        if (ok) { pb.Value = 100; cfg.Fit = fit; cfg.Save(); }
                        if (wasLive) { try { dev.Start(); liveTimer.Start(); Log("已恢复实时刷新"); } catch (Exception ex) { Log("恢复实时刷新失败: " + ex.Message); } }
                        if (ok) { RefreshFiles(); Use(name, asMovie); }
                        else Log("上传没有完成。如果之后屏幕没有反应、顶部变成橙色，需要完全断电重启一次。");
                        RaiseUploadDone(ok, name, asMovie);
                    }));
                }
            });
            th.IsBackground = true; th.Start();
        }

        void Use(string name, bool asMovie)
        {
            if(linkCfg.NightMode){Log("请先恢复灯光和屏幕");return;}
            try
            {
                if (asMovie)
                {
                    cfg.Current = name; cfg.CurrentIsBg = false;
                    bool ok = Quiet(() => dev.PlayMovie(name));
                    if (ok) { playing = name; MarkMovieStarted(name); }
                    revertTimer.Stop();
                    if (!clb.GetItemChecked(0)) { clb.SetItemChecked(0, true); ApplyModes(); }
                    Log(ok ? "已设为全屏动画：" + name : "播放指令发送失败：" + name);
                }
                else { Quiet(() => { dev.SetBackground(name); return 0; }); Log("已设为背景：" + name); }
                cfg.Current = name; cfg.CurrentIsBg = !asMovie; cfg.Save();
            }
            catch (Exception ex) { Log("出错: " + ex.Message); }
        }

        void UseSelected()
        {
            if (!Guard() || lstFiles.SelectedItem == null) return;
            Use((string)lstFiles.SelectedItem, rbMovie.Checked);
        }

        void DeleteSelected()
        {
            if (!Guard() || lstFiles.SelectedItem == null) return;
            string n = (string)lstFiles.SelectedItem;
            if (n == cfg.Current)
            {
                MessageBox.Show(n + " 正在屏幕上使用。删除正在播放的文件可能让屏幕卡死。\r\n请先把别的文件设为当前，或在“轮播页面”里取消“自定义动画/图片”，再删除。", "不能删除");
                return;
            }
            if (MessageBox.Show("从屏幕上删除 " + n + " ？", "确认", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { Quiet(() => { dev.Delete(n); return 0; }); Log("已删除 " + n); RefreshFiles(); }
            catch (Exception ex) { Log("出错: " + ex.Message); }
        }

        void SetLive(bool on)
        {
            if (loading) return;
            if (!Guard()) { loading = true; chkLive.Checked = false; loading = false; return; }
            try
            {
                if (on) { dev.Start(); liveTimer.Start(); LiveTick(); Log("实时刷新已开启"); }
                else { liveTimer.Stop(); dev.Stop(); lbLive.Text = ""; Log("实时刷新已关闭"); }
                cfg.Live = on; cfg.Save();
            }
            catch (Exception ex) { Log("出错: " + ex.Message); }
        }

        void LiveTick()
        {
            if(linkCfg.NightMode)return;
            try
            {
                var c = Sensors.Cpu(); var r = Sensors.Ram();
                dev.PushCpu(c, r);
                lbLive.Text = string.Format("CPU {0:0}%  {1:0} MHz  {2:0}°C    内存 {3:0.0} / {4:0.0} GB", c.Load, c.Clock, c.Temp, r.UsedGB, r.TotalGB);
            }
            catch (Exception ex) { lbLive.Text = "推送出错: " + ex.Message; }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static bool IsAutostart() { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("GpuLcdTool") != null; }
        static void SetAutostart(bool on)
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (on) k.SetValue("GpuLcdTool", "\"" + Application.ExecutablePath + "\" --tray");
                else k.DeleteValue("GpuLcdTool", false);
            }
        }

        void ShowMe() { if (classicUi) ShowClassic(); else OpenWebUi(); }

        void ShowClassic() { ShowInTaskbar = true; Opacity = 1; Show(); WindowState = FormWindowState.Normal; Activate(); }

        // 关窗口后是否应该留在托盘继续工作（联动 / 熄灯 / 实时刷新在跑，且屏幕可用；
        // 显卡屏没连上但还在给点阵屏转发助手状态时，也要留着）
        bool StayInTray(CloseReason reason)
        {
            return !reallyExit && reason == CloseReason.UserClosing
                && (linkCfg.NightMode || cfg.Live || linkCfg.Codex || linkCfg.Claude || linkCfg.Signal)
                && (connected || linkCfg.NightMode || (linkCfg.DotMatrix && (linkCfg.Codex || linkCfg.Claude)));
        }

        // 新界面窗口被用户关掉之后：留托盘或退出（与原经典窗口的规则一致）。上传中绝不退出。
        void AfterWebUiClosed()
        {
            if (shuttingDown || reallyExit || IsDisposed || classicUi || Visible) return;
            if (uploading || StayInTray(CloseReason.UserClosing))
            {
                tray.Visible = true;
                tray.ShowBalloonTip(2000, "显卡屏幕控制", "屏幕控制与联动仍在运行，右键托盘图标可退出。", ToolTipIcon.Info);
            }
            else { reallyExit = true; Close(); }
        }

        void OnClosing(object s, FormClosingEventArgs e)
        {
            // 上传中关闭窗口会关掉串口，把屏幕写坏；一律拒绝，传完再退出
            if (uploading) { e.Cancel = true; Log("上传进行中，暂不能退出；请等上传结束"); return; }
            // 新界面开着时关经典窗口，只是把它藏起来
            if (!classicUi && !reallyExit && e.CloseReason == CloseReason.UserClosing && webUi != null && !webUi.IsDisposed) { e.Cancel = true; Hide(); return; }
            if (StayInTray(e.CloseReason))
            {
                e.Cancel = true; Hide(); tray.Visible = true;
                tray.ShowBalloonTip(2000, "显卡屏幕控制", "屏幕控制与联动仍在运行，右键托盘图标可退出。", ToolTipIcon.Info);
                return;
            }
            shuttingDown = true; CloseWebUi(); StopWidgets(); StopLinks(); liveTimer.Stop(); tray.Visible = false;
            if (connected) dev.Close();
        }
    }

    // ---------------------------------------------------------------- 入口
    static class Program
    {
        public const string ShowEventName = "Local\\GpuLcdTool_Show";
        public const string PipeName = "GpuLcdTool";
        static Mutex single;
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            try { Vendor.Setup(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "显卡屏幕控制"); return 3; }
            return Run(args);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Run(string[] args)
        {
            if (args.Length > 1 && args[0] == "--shot")
            {
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var f = new MainForm(false) { Preview = true };
                f.Show(); Application.DoEvents();
                f.Shoot(args[1]);
                f.Close();
                return 0;
            }
            if (args.Length > 0 && args[0] == "--storage") return StorageUI.Run();
            // Keep the complete GUI visible; only device commands are blocked during maintenance.
            bool gui = args.Length == 0 || args[0] == "--tray";
            if (!gui && File.Exists(StorageOps.Flag))
            {
                AttachConsole(-1);
                const string message = "屏幕处于存储维护模式。请使用存储维护工具完成备份并恢复连接。";
                Console.WriteLine("ERR " + message);
                return 5;
            }
            // 屏幕串口同一时间只能被一个进程打开，所以只允许一个实例
            bool first;
            single = new Mutex(true, "Local\\GpuLcdTool_Single", out first);
            if (!first)
            {
                if (gui)
                {
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                    return 0;
                }
                AttachConsole(-1);
                // 窗口程序在运行：简单指令转给它执行
                string c0 = args[0].ToLowerInvariant();
                if (c0 == "play" || c0 == "flash" || c0 == "revert" || c0 == "status")
                {
                    try
                    {
                        using (var cl = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                        {
                            cl.Connect(3000);
                            var w = new StreamWriter(cl, new UTF8Encoding(false)) { AutoFlush = true };
                            var r = new StreamReader(cl, new UTF8Encoding(false));
                            w.WriteLine(string.Join(" ", args));
                            string resp = r.ReadLine() ?? "ERR 没有回应";
                            Console.WriteLine(resp);
                            return resp.StartsWith("OK") ? 0 : 1;
                        }
                    }
                    catch (Exception ex) { Console.WriteLine("ERR 无法连接正在运行的程序: " + ex.Message); return 1; }
                }
                Console.WriteLine("显卡屏幕控制的窗口或托盘程序正在运行，它占着屏幕串口。上传等操作请在窗口里做，或先从托盘退出它。");
                return 4;
            }
            if (!gui)
            {
                AttachConsole(-1);
                return Cli.Run(args);
            }
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Length > 0 && args[0] == "--tray"));
            return 0;
        }
    }
}



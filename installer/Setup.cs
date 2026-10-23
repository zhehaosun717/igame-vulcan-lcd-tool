using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("显卡屏幕控制 安装程序")]
[assembly: AssemblyProduct("GpuLcd")]
[assembly: AssemblyVersion("1.0.4.0")]
[assembly: AssemblyFileVersion("1.0.4.0")]

// 安装 / 卸载一体的小安装器（每用户安装，不需要管理员权限）。
//   Setup.exe                       图形界面安装
//   Setup.exe /S [/dir=PATH] [/autostart] [/desktop] [/norun]   静默安装
//   Uninstall.exe /uninstall [/S] [/purge]                       卸载（/purge 同时删除用户设置）
static class Setup
{
    const string AppName = "显卡屏幕控制";
    const string Version = "1.0.4";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\GpuLcdTool";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string PipeName = "GpuLcdTool";

    static bool silent;
    static string Arg(string[] a, string name) { foreach (var s in a) if (s.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return s.Substring(name.Length + 1).Trim('"'); return null; }
    static bool Has(string[] a, string name) { foreach (var s in a) if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase)) return true; return false; }
    static string DefaultDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GpuLcd"); } }
    static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); } }
    static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); } }

    [STAThread]
    static int Main(string[] args)
    {
        silent = Has(args, "/S");
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (Has(args, "/uninstall")) return Uninstall(args);
            return Install(args);
        }
        catch (Exception ex) { Say("操作失败：" + ex.Message, true); return 1; }
    }

    static void Say(string text, bool error = false)
    {
        if (silent) { Console.Error.WriteLine(text); return; }
        MessageBox.Show(text, AppName + " 安装程序", MessageBoxButtons.OK, error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
    }

    // ---- 运行中的程序 ----
    static bool AppRunning(string exeDir)
    {
        foreach (var p in Process.GetProcessesByName("GpuLcd"))
        {
            try { if (exeDir == null || string.Equals(Path.GetDirectoryName(p.MainModule.FileName), exeDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true; }
            catch { return true; }
        }
        return false;
    }

    // 用程序自己的管道请它正常退出（它会收尾：停联动、关串口）；不强行结束进程，避免打断屏幕操作。
    static bool StopApp()
    {
        if (Process.GetProcessesByName("GpuLcd").Length == 0) return true;
        try
        {
            using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
            {
                c.Connect(2000);
                var w = new StreamWriter(c, new UTF8Encoding(false)) { AutoFlush = true };
                var r = new StreamReader(c, new UTF8Encoding(false));
                w.WriteLine("quit"); string resp = r.ReadLine() ?? "";
                if (resp.StartsWith("ERR")) return false;   // 例如“上传进行中”：绝不打断上传
            }
        }
        catch { }
        for (int i = 0; i < 40; i++) { if (Process.GetProcessesByName("GpuLcd").Length == 0) return true; Thread.Sleep(250); }
        return false;
    }

    // ---- 检测依赖（只提醒，不阻止）----
    static string Prereqs()
    {
        var sb = new StringBuilder();
        bool wv2 = false;
        foreach (var path in new[] { @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" })
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                using (var k = hive.OpenSubKey(path)) if (k != null && !string.IsNullOrEmpty(k.GetValue("pv") as string)) wv2 = true;
        }
        if (!wv2) sb.AppendLine("· 未检测到 Microsoft Edge WebView2 运行时：程序会自动改用经典界面。可从 Microsoft 官网安装 WebView2 后获得新界面。");
        return sb.ToString().Trim();
    }

    // ---- 快捷方式（WScript.Shell，晚绑定，不需要额外引用）----
    static void MakeLink(string linkPath, string target, string args, string workDir, string icon)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(t);
        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
        Type lt = lnk.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
        lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { args });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { icon });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { AppName });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }

    // ---- 安装 ----
    class Options { public string Dir; public bool Desktop, Autostart, Run; }

    static int Install(string[] args)
    {
        var o = new Options { Dir = Arg(args, "/dir") ?? DefaultDir, Desktop = Has(args, "/desktop"), Autostart = Has(args, "/autostart"), Run = !Has(args, "/norun") };
        // 已有开机自启时，向导里默认勾上（安装后会把它指向新位置）；静默安装只在显式 /autostart 时才写
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) { if (!silent && k != null && k.GetValue("GpuLcdTool") != null) o.Autostart = true; }
        if (!silent)
        {
            if (!ShowWizard(o)) return 3;
        }
        string pre = Prereqs();
        if (silent && pre.Length > 0) Console.Error.WriteLine(pre);

        if (!StopApp()) { Say("检测到 " + AppName + " 正在运行且没有退出（可能正在上传文件）。\r\n请先从托盘退出程序，等上传结束后再安装。", true); return 2; }
        Directory.CreateDirectory(o.Dir);
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        {
            if (s == null) { Say("安装包已损坏：缺少 payload.zip。", true); return 1; }
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                foreach (var en in zip.Entries)
                {
                    if (string.IsNullOrEmpty(en.Name)) continue;
                    string rel = en.FullName.Replace('/', '\\');
                    while (rel.StartsWith(".\\") || rel.StartsWith("\\")) rel = rel.Substring(rel.StartsWith(".\\") ? 2 : 1);
                    if (rel.Contains("..")) continue;   // 防路径穿越
                    string dest = Path.GetFullPath(Path.Combine(o.Dir, rel));
                    if (!dest.StartsWith(Path.GetFullPath(o.Dir), StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    en.ExtractToFile(dest, true);
                }
        }
        string exe = Path.Combine(o.Dir, "GpuLcd.exe");
        string un = Path.Combine(o.Dir, "Uninstall.exe");
        File.Copy(Application.ExecutablePath, un, true);

        MakeLink(StartMenuLink, exe, "", o.Dir, exe + ",0");
        if (o.Desktop) MakeLink(DesktopLink, exe, "", o.Dir, exe + ",0");
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppName); k.SetValue("DisplayVersion", Version); k.SetValue("Publisher", "GpuLcd");
            k.SetValue("InstallLocation", o.Dir); k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("UninstallString", "\"" + un + "\" /uninstall"); k.SetValue("QuietUninstallString", "\"" + un + "\" /uninstall /S");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            long size = 0; foreach (var f in new DirectoryInfo(o.Dir).GetFiles("*", SearchOption.AllDirectories)) size += f.Length;
            k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
        }
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (o.Autostart) k.SetValue("GpuLcdTool", "\"" + exe + "\" --tray");
            else
            {
                // 不勾选：只清除“指向本安装目录”的旧自启项；指向别处的（如你自己的工作目录）不碰
                string cur = k.GetValue("GpuLcdTool") as string;
                if (cur != null && cur.IndexOf(o.Dir, StringComparison.OrdinalIgnoreCase) >= 0) k.DeleteValue("GpuLcdTool", false);
            }
        }
        if (!silent)
        {
            string msg = "安装完成。\r\n位置：" + o.Dir + (pre.Length > 0 ? "\r\n\r\n注意：\r\n" + pre : "");
            MessageBox.Show(msg, AppName + " 安装程序", MessageBoxButtons.OK, pre.Length > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        if (o.Run) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = o.Dir, UseShellExecute = false });
        return 0;
    }

    static bool ShowWizard(Options o)
    {
        var f = new Form { Text = AppName + " 安装程序", StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(520, 330), Font = new Font("Microsoft YaHei UI", 9f) };
        try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        f.Controls.Add(new Label { Text = AppName + " " + Version, Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold), Location = new Point(20, 16), AutoSize = true });
        f.Controls.Add(new Label { Text = "七彩虹 iGame 火神 LCD 3.0 侧屏的独立控制工具，已附带屏幕运行库，无需安装 iGameCenter。", Location = new Point(20, 52), Size = new Size(480, 20), ForeColor = Color.Gray });
        f.Controls.Add(new Label { Text = "安装位置", Location = new Point(20, 90), AutoSize = true });
        var path = new TextBox { Text = o.Dir, Location = new Point(20, 112), Width = 390 };
        var browse = new Button { Text = "浏览…", Location = new Point(420, 110), Width = 80 };
        browse.Click += (s, e) => { using (var d = new FolderBrowserDialog { SelectedPath = path.Text, Description = "选择安装位置" }) if (d.ShowDialog() == DialogResult.OK) path.Text = Path.Combine(d.SelectedPath, d.SelectedPath.EndsWith("GpuLcd", StringComparison.OrdinalIgnoreCase) ? "" : "GpuLcd"); };
        var desktop = new CheckBox { Text = "创建桌面快捷方式", Location = new Point(20, 152), AutoSize = true, Checked = o.Desktop };
        var auto = new CheckBox { Text = "开机自动启动（最小化到托盘并恢复上次设置）", Location = new Point(20, 180), AutoSize = true, Checked = o.Autostart };
        var run = new CheckBox { Text = "安装完成后运行", Location = new Point(20, 208), AutoSize = true, Checked = o.Run };
        f.Controls.AddRange(new Control[] { path, browse, desktop, auto, run });
        string pre = Prereqs();
        if (pre.Length > 0) f.Controls.Add(new Label { Text = pre, Location = new Point(20, 238), Size = new Size(480, 48), ForeColor = Color.DarkOrange });
        var ok = new Button { Text = "安装", Location = new Point(320, 292), Width = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Location = new Point(420, 292), Width = 80, DialogResult = DialogResult.Cancel };
        f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog() != DialogResult.OK) return false;
        o.Dir = path.Text.Trim(); o.Desktop = desktop.Checked; o.Autostart = auto.Checked; o.Run = run.Checked;
        if (string.IsNullOrEmpty(o.Dir)) return false;
        return true;
    }

    // ---- 卸载 ----
    static int Uninstall(string[] args)
    {
        string dir = null;
        using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey)) if (k != null) dir = k.GetValue("InstallLocation") as string;
        if (string.IsNullOrEmpty(dir)) dir = Path.GetDirectoryName(Application.ExecutablePath);
        bool purge = Has(args, "/purge");
        if (!silent)
        {
            var r = MessageBox.Show("确定要卸载 " + AppName + " 吗？", AppName + " 卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return 3;
            purge = MessageBox.Show("是否同时删除你的设置和日志？\r\n（%APPDATA%\\GpuLcdTool，包含默认动画、联动设置等）\r\n\r\n选“否”会保留，以后重装可以接着用。", AppName + " 卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        if (!StopApp()) { Say("程序正在运行且没有退出（可能正在上传文件）。请先从托盘退出再卸载。", true); return 2; }

        foreach (var l in new[] { StartMenuLink, DesktopLink }) { try { if (File.Exists(l)) File.Delete(l); } catch { } }
        // 只删指向本安装目录的自启项，不动别处（如开发目录）的
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            string cur = k == null ? null : k.GetValue("GpuLcdTool") as string;
            if (cur != null && cur.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0) k.DeleteValue("GpuLcdTool", false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        if (purge)
        {
            foreach (var d in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GpuLcdTool"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuLcdTool") })
                try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        }
        // 自己正在运行，不能删自己：交给一个延迟的 cmd 把整个目录删掉
        string self = Application.ExecutablePath;
        foreach (var f in Directory.GetFiles(dir)) { if (string.Equals(f, self, StringComparison.OrdinalIgnoreCase)) continue; try { File.Delete(f); } catch { } }
        foreach (var d in Directory.GetDirectories(dir)) { try { Directory.Delete(d, true); } catch { } }
        var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + dir + "\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        Process.Start(psi);
        if (!silent) MessageBox.Show("已卸载。" + (purge ? "" : "\r\n你的设置仍保留在 %APPDATA%\\GpuLcdTool。"), AppName + " 卸载", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }
}

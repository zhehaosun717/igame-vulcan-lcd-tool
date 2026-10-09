using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace GpuLcd
{
    static class StorageOps
    {
        public static string Flag { get { return Path.Combine(Paths.Data, "storage-maintenance.flag"); } }
        public static string BackupBase { get { return Path.Combine(Paths.Data, "storage-backups"); } }
        public static readonly string[] BootNames = { "op.savi", "oploop.savi", "op1.savi", "op1loop.savi" };
        public static string NewBackup()
        {
            string p = Path.Combine(BackupBase, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(p); return p;
        }
        public static string Hash(string path)
        {
            using (var s = File.OpenRead(path)) using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
        }
        public static void CopyVerified(string source, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(source, dest, false);
            if (Hash(source) != Hash(dest)) throw new IOException("复制校验失败：" + Path.GetFileName(source));
        }
        public static string FindRoot()
        {
            var roots = new List<string>();
            using (var q = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
            using (var disks = q.Get()) foreach (ManagementObject disk in disks)
            {
                string id = Convert.ToString(disk["PNPDeviceID"]);
                string serial = Convert.ToString(disk["SerialNumber"]).Trim();
                if (!id.StartsWith(@"USBSTOR\DISK&VEN_SONIX&PROD_USB_MSC", StringComparison.OrdinalIgnoreCase)
                    || serial != "100000000000" || Convert.ToUInt64(disk["Size"]) > 536870912UL) continue;
                using (var parts = disk.GetRelated("Win32_DiskPartition")) foreach (ManagementObject part in parts)
                using (var vols = part.GetRelated("Win32_LogicalDisk")) foreach (ManagementObject vol in vols)
                {
                    string root = Convert.ToString(vol["DeviceID"]) + @"\";
                    if (root.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(Path.Combine(root, "op.savi")) && Directory.Exists(Path.Combine(root, "mov"))) roots.Add(root);
                }
            }
            roots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (roots.Count > 1) throw new IOException("发现多个同名屏幕磁盘，不能自动选择。");
            return roots.Count == 1 ? roots[0] : null;
        }
        public static string StorageDeviceStatus()
        {
            using (var search = new ManagementObjectSearcher("SELECT DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Present=TRUE"))
            using (var devices = search.Get()) foreach (ManagementObject item in devices)
            {
                if (!Convert.ToString(item["DeviceID"]).StartsWith(@"USB\VID_0C45&PID_6480\", StringComparison.OrdinalIgnoreCase)) continue;
                int code = Convert.ToInt32(item["ConfigManagerErrorCode"]);
                return code == 47
                    ? "屏幕 USB 已安全弹出（代码 47），尚未恢复串口。先拔插显卡外部 USB-C 线；若仍为 U 盘模式，再整机断电。"
                    : "屏幕仍使用 USB 存储模式（设备状态码 " + code + "）。重新连接 USB 不一定能退出此模式；尚未确认软件复位方法。";
            }
            return null;
        }
        public static string RequireRoot()
        {
            string p = FindRoot();
            if (p == null) throw new IOException("没有找到已挂载的屏幕磁盘。先进入 U 盘模式并刷新。");
            return p;
        }
        public static string Backup(string root, Action<string> log)
        {
            string dest = NewBackup(); var rows = new List<object>();
            BackupDir(root, root, dest, rows, log);
            File.WriteAllText(Path.Combine(dest, "manifest.json"), new JavaScriptSerializer().Serialize(rows));
            File.WriteAllText(Path.Combine(dest, "backup-complete.txt"), "Verified file backup; Windows System Volume Information excluded.\r\n" + DateTime.Now.ToString("O"));
            log("已完成并校验 " + rows.Count + " 个文件：" + dest); return dest;
        }
        static void BackupDir(string root, string dir, string dest, List<object> rows, Action<string> log)
        {
            foreach (string f in Directory.GetFiles(dir))
            {
                if ((File.GetAttributes(f) & FileAttributes.ReparsePoint) != 0) throw new IOException("不支持链接文件。");
                string rel = f.Substring(root.Length); string target = Path.Combine(dest, rel);
                CopyVerified(f, target); rows.Add(new { path = rel, bytes = new FileInfo(f).Length, sha256 = Hash(target) }); log("已备份 " + rel);
            }
            foreach (string sub in Directory.GetDirectories(dir))
            {
                if (Path.GetFileName(sub).Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) throw new IOException("不支持链接目录。");
                BackupDir(root, sub, dest, rows, log);
            }
        }
        public static Bitmap FirstFrame(string file)
        {
            var fi = new FileInfo(file);
            if (fi.Length < 64 || fi.Length > 64L * 1024 * 1024) throw new IOException("SAVI 大小不符合本工具支持范围（64 B–64 MB）。");
            byte[] b = File.ReadAllBytes(file);
            if (b[0] != 'S' || b[1] != 'A' || b[2] != 'V' || b[3] != 'I') throw new IOException("文件不是 SAVI 格式。");
            for (int i = 48; i < b.Length - 2; i++) if (b[i] == 255 && b[i+1] == 216 && b[i+2] == 255)
            {
                using (var ms = new MemoryStream(b, i, b.Length-i)) using (var img = Image.FromStream(ms))
                {
                    if (img.Width != 480 || img.Height != 128) throw new IOException("动画画面必须是 480×128。");
                    return new Bitmap(img);
                }
            }
            throw new IOException("找不到可解码的 JPEG 首帧；没有写入屏幕。");
        }
        // The UI revalidates hardware identity before invoking this file-only operation.
        public static string ReplaceBoot(string root, string name, string input)
        {
            if (!BootNames.Contains(name)) throw new ArgumentException("只允许替换四个已确认的开机资源。");
            using (var check = FirstFrame(input)) { }
            string target = Path.Combine(root, name);
            if (!File.Exists(target)) throw new IOException("原文件不存在；请先检查存储。");
            string backup = NewBackup(); CopyVerified(target, Path.Combine(backup, name));
            CopyVerified(input, Path.Combine(backup, "replacement.savi"));
            string temp = Path.Combine(root, "lcdnew.tmp"), old = Path.Combine(root, "lcdold.tmp");
            if (File.Exists(temp) || File.Exists(old)) throw new IOException("发现上次未完成的替换文件；先恢复，不覆盖。");
            CopyVerified(Path.Combine(backup, "replacement.savi"), temp);
            File.Move(target, old);
            try
            {
                File.Move(temp, target);
                if (Hash(target) != Hash(input)) throw new IOException("写入后校验失败。");
            }
            catch
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(old, target); throw;
            }
            File.Delete(old);
            File.WriteAllText(Path.Combine(backup, "replacement.json"), new JavaScriptSerializer().Serialize(new { target = name, original = Hash(Path.Combine(backup, name)), replacement = Hash(target), time = DateTime.Now.ToString("O") }));
            return backup;
        }
        public static object Core(Dev d)
        {
            return typeof(Dev).GetMethod("CoreObj", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(d, null);
        }
        public static bool ReadOpening(Dev d, out int mode)
        {
            object c = Core(d); var bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var t = c.GetType().Assembly.GetType("iGameAPI.LCD.CSharp.LCD3Setting");
            object[] a = { Activator.CreateInstance(t) };
            bool ok = (bool)c.GetType().GetMethod("LCD3_GetSetting", bf).Invoke(c, a);
            mode = ok ? (int)t.GetProperty("OpeningMode").GetValue(a[0], null) : -1; return ok;
        }
        public static bool PreviewOpening(Dev d)
        {
            object c = Core(d);
            return (bool)c.GetType().GetMethod("LCD3_PlayOpening", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, null);
        }
    }

    class StorageForm : Form
    {
        public bool Preview = false;
        Label state = new Label(); TextBox log = new TextBox(); ListBox files = new ListBox();
        ComboBox boot = new ComboBox(); PictureBox preview = new PictureBox();
        FlowLayoutPanel actions = new FlowLayoutPanel(); string backupPath = StorageOps.BackupBase; bool busy;
        static string LastBackupFile { get { return Path.Combine(Paths.Data, "storage-last-backup.txt"); } }
        void RememberBackup(string path) { backupPath = path; File.WriteAllText(LastBackupFile, path); }
        public StorageForm()
        {
            SuspendLayout();
            if (File.Exists(LastBackupFile)) { string p = File.ReadAllText(LastBackupFile).Trim(); if (Directory.Exists(p)) backupPath = p; }
            Text = "显卡屏幕 · 存储与开机动画"; Font = new Font("Microsoft YaHei UI", 9);
            AutoScaleDimensions = new SizeF(96,96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720,740); MinimumSize = new Size(640,640); StartPosition = FormStartPosition.CenterScreen;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(14) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,138));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,178));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,130));
            state.Dock=DockStyle.Fill; state.Text="正在识别屏幕磁盘…"; layout.Controls.Add(state,0,0);
            actions.Dock=DockStyle.Fill; actions.AutoScroll=true; layout.Controls.Add(actions,0,1);
            Button("进入 U 盘模式", EnterStorage); Button("刷新磁盘", RefreshDisk); Button("打开屏幕文件夹", () => Process.Start("explorer.exe", StorageOps.RequireRoot()));
            Button("备份全部屏幕文件", () => RememberBackup(StorageOps.Backup(StorageOps.RequireRoot(), Log)));
            Button("打开备份文件夹", () => { Directory.CreateDirectory(backupPath); Process.Start("explorer.exe", backupPath); });
            Button("检查并恢复普通控制", ResumeControl);
            Button("返回主界面", () => UI(() => { Tag = "main"; busy = false; Close(); }));
            files.Dock=DockStyle.Fill; files.HorizontalScrollbar=true; layout.Controls.Add(files,0,2);
            var group=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=2}; group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); group.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,212)); group.RowStyles.Add(new RowStyle(SizeType.Absolute,36)); group.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            boot.DropDownStyle=ComboBoxStyle.DropDownList; boot.Dock=DockStyle.Fill; boot.Items.AddRange(StorageOps.BootNames); boot.SelectedIndex=0;
            group.Controls.Add(boot,0,0); preview.Dock=DockStyle.Fill; preview.SizeMode=PictureBoxSizeMode.Zoom; preview.BackColor=Color.Black; group.Controls.Add(preview,0,1);
            var ops=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown}; group.Controls.Add(ops,1,0); group.SetRowSpan(ops,2);
            LocalButton(ops,"预览所选文件首帧", PreviewFile); LocalButton(ops,"选择图片 / 动画替换…", ReplaceFile); LocalButton(ops,"从备份恢复此文件…", RestoreFile);
            layout.Controls.Add(group,0,3);
            layout.Controls.Add(new Label {Dock=DockStyle.Fill,Text="方案 0：op / oploop；方案 1：op1 / op1loop。替换只修改下拉框选中的文件。\r\n完成后关闭文件夹，使用 Windows“安全删除硬件”，再关机、关闭电源开关约 30 秒后开机。\r\n替换后的实际播放效果需断电后确认；重新开机后点击“检查并恢复普通控制”。", AutoEllipsis=false},0,4);
            log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill; layout.Controls.Add(log,0,5); Controls.Add(layout); ResumeLayout(false); PerformAutoScale();
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
            Shown+=(s,e)=>{if(!Preview)Run(()=>{RefreshDisk();if(StorageOps.FindRoot()!=null)PreviewFile();});}; FormClosing+=(s,e)=> { if(busy) {e.Cancel=true;return;} if(File.Exists(StorageOps.Flag)) Log("维护状态保留；重新打开小工具可继续。"); };
        }
        void Button(string title, Action action) { LocalButton(actions,title,action); }
        void LocalButton(FlowLayoutPanel panel,string title,Action action)
        {
            var b=new Button {Text=title,AutoSize=true,MinimumSize=new Size(170,34),Margin=new Padding(3)};
            b.Click+=(s,e)=>Run(action); panel.Controls.Add(b);
        }
        async void Run(Action action)
        {
            if(busy)return; busy=true; actions.Enabled=false;
            try { await Task.Run(action); }
            catch(Exception ex) {Log("错误："+(ex.InnerException??ex).Message);}
            finally {busy=false;if(!IsDisposed)actions.Enabled=true;}
        }
        void UI(Action a) {if(InvokeRequired)Invoke(a);else a();}
        void Log(string s) { UI(()=>{ string line=DateTime.Now.ToString("HH:mm:ss")+" "+s+Environment.NewLine; log.AppendText(line); try { Directory.CreateDirectory(Paths.Data); File.AppendAllText(Path.Combine(Paths.Data,"storage.log"),line); } catch { } }); }
        string Selected() {string s=null;UI(()=>s=(string)boot.SelectedItem);return s;}
        string Choose(string title,string filter,string initial=null)
        {
            string p=null;UI(()=>{using(var d=new OpenFileDialog {Title=title,Filter=filter,InitialDirectory=initial??""})if(d.ShowDialog(this)==DialogResult.OK)p=d.FileName;});return p;
        }
        void RefreshDisk()
        {
            string root=StorageOps.FindRoot(); UI(()=>files.Items.Clear());
            if(root==null){string message=StorageOps.StorageDeviceStatus() ?? "未检测到屏幕磁盘。若 USB 已重新连接，点击“检查并恢复普通控制”。"; UI(()=>state.Text=message);Log(message);return;}
            var drive=new DriveInfo(root);
            UI(()=>state.Text=string.Format("屏幕磁盘：{0}  ·  SONiX USB MSC\r\n总计 {1:N1} MiB，剩余 {2:N1} MiB。此模式下屏幕暂停，助手B 外部指令已暂停。",root,drive.TotalSize/1048576.0,drive.AvailableFreeSpace/1048576.0));
            foreach(string f in Directory.GetFiles(root,"*",SearchOption.TopDirectoryOnly)) UI(()=>files.Items.Add(Path.GetFileName(f)+"   "+new FileInfo(f).Length.ToString("N0")+" B"));
            foreach(string f in Directory.GetFiles(Path.Combine(root,"mov"))) UI(()=>files.Items.Add("mov\\"+Path.GetFileName(f)+"   "+new FileInfo(f).Length.ToString("N0")+" B"));
            Directory.CreateDirectory(Paths.Data);if(!File.Exists(StorageOps.Flag))File.WriteAllText(StorageOps.Flag,"USB storage active");
            Log("已确认屏幕盘 "+root);
        }
        void EnterStorage()
        {
            if(StorageOps.FindRoot()!=null){RefreshDisk();return;}
            Directory.CreateDirectory(Paths.Data); File.WriteAllText(StorageOps.Flag,"Storage transition requested "+DateTime.Now.ToString("O"));
            var d=new Dev();
            try
            {
                string err=d.Connect(Log); if(err!=null)throw new IOException(err);
                if(Convert.ToString(d.L3.FWVersion)!="122" || string.IsNullOrEmpty(d.Com))throw new IOException("仅验证了固件 122 和 USB 串口连接；未发送切换指令。");
                int mode; if(!StorageOps.ReadOpening(d,out mode))throw new IOException("屏幕没有回应设置读取。");
                Log("当前开机方案 "+mode+"；进入存储模式。"); d.Stop();
                object c=StorageOps.Core(d); bool sent=(bool)c.GetType().GetMethod("WriteDataByCOM",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,new object[]{new byte[]{0xf6,0x5a,0xd0,0,0}});
                if(!sent)throw new IOException("串口发送失败。");
                Thread.Sleep(5000);
            }
            finally{d.Close();}
            for(int i=0;i<8 && StorageOps.FindRoot()==null;i++)Thread.Sleep(1000);
            RefreshDisk(); if(StorageOps.FindRoot()==null)throw new IOException("没有检测到新盘；发送成功不代表切换成功。可检查普通连接。");
        }
        void PreviewFile()
        {
            string f=Path.Combine(StorageOps.RequireRoot(),Selected()); var img=StorageOps.FirstFrame(f);
            UI(()=>{var old=preview.Image;preview.Image=img;if(old!=null)old.Dispose();}); Log("显示首帧："+Path.GetFileName(f));
        }
        void ReplaceFile()
        {
            string target=Selected(); string file=Choose("选择要转换并替换到 "+target+" 的素材","图片/视频/SAVI|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.mp4;*.avi;*.mov;*.mkv;*.webm;*.savi");
            if(file==null)return;
            string prepared=file.EndsWith(".savi",StringComparison.OrdinalIgnoreCase)?file:Savi.Convert(file,"contain","bootnew.savi",Log);
            ConfirmReplace(target,prepared);
        }
        void RestoreFile()
        {
            string target=Selected();string file=Choose("选择原来的 "+target+" 备份","SAVI 动画|*.savi",backupPath);if(file!=null)ConfirmReplace(target,file);
        }
        void ConfirmReplace(string target,string file)
        {
            using(var img=StorageOps.FirstFrame(file)) UI(()=>{var old=preview.Image;preview.Image=new Bitmap(img);if(old!=null)old.Dispose();});
            bool yes=false;UI(()=>yes=MessageBox.Show(this,"将把所选素材写入屏幕的 "+target+"。\r\n原文件会先备份；其他开机资源保持原样。\r\n确定替换？","写入开机动画",MessageBoxButtons.OKCancel)==DialogResult.OK);
            if(!yes)return;
            RememberBackup(StorageOps.ReplaceBoot(StorageOps.RequireRoot(),target,file));Log("已替换并校验 "+target+"；原件备份："+backupPath);RefreshDisk();
        }
        void ResumeControl()
        {
            if(StorageOps.FindRoot()!=null)throw new IOException("屏幕仍是 U 盘。请先安全弹出，然后整机断电约 30 秒再开机。");
            string pending=StorageOps.StorageDeviceStatus(); if(pending!=null)throw new IOException(pending);
            var d=new Dev();try {string err=d.Connect(Log);if(err!=null)throw new IOException(err);int mode;if(string.IsNullOrEmpty(d.Com)||!StorageOps.ReadOpening(d,out mode))throw new IOException("普通屏幕连接尚未恢复，维护锁仍保留。");Log("普通连接已验证，开机方案 "+mode);}
            finally {d.Close();}
            if(File.Exists(StorageOps.Flag))File.Delete(StorageOps.Flag);
            // Release this process's mutex before the normal UI starts.
            UI(()=>{Tag="resume";});
            UI(()=>{busy=false;Close();});
        }
    }
    static class StorageUI
    {
        [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
        public static int Run()
        {
            using(var mutex=new Mutex(false,@"Local\GpuLcdTool_Single"))
            {
                bool held=false;bool resume=false;
                try
                {
                    try {held=mutex.WaitOne(5000);}catch(AbandonedMutexException){held=true;}
                    if(!held){MessageBox.Show("另一个屏幕工具窗口正在运行，请从那个窗口打开存储维护。");return 4;}
                    SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    using(var f=new StorageForm()){Application.Run(f);resume=(string)f.Tag=="resume" || (string)f.Tag=="main";}
                }
                finally {if(held)mutex.ReleaseMutex();}
                if(resume)Process.Start(Application.ExecutablePath);
            }
            return 0;
        }
    }
}





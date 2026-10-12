using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GpuLcd
{
    // Prefer the private runtime shipped beside the application. Never mix a partial
    // bundled runtime with another version installed on the machine.
    static class Vendor
    {
        internal const string InstalledRoot = @"C:\Program Files\iGameCenter";
        public static string Root { get; private set; }
        static bool ready;
        internal static readonly string[] Required = {
            "iGameAPI.LCD.CSharp.dll", "iGameAPI.NVAPI.CSharp.dll",
            "iGameAPI.Contracts.dll", "iGameCenter.Contracts.dll",
            @"iGameAPI\iGameAPI.LCD.dll", @"iGameAPI\iGameAPI.NVAPI.dll"
        };
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetDllDirectory(string path);

        internal static string ResolveRoot(string applicationDirectory, string installedDirectory)
        {
            string bundled = Path.Combine(applicationDirectory, "vendor", "iGameCenter");
            string selected = Directory.Exists(bundled) ? bundled : installedDirectory;
            foreach (string relative in Required)
                if (!File.Exists(Path.Combine(selected, relative)))
                    throw new FileNotFoundException("屏幕运行库不完整，请重新解压完整便携包或重新安装：" + relative,
                        Path.Combine(selected, relative));
            return selected;
        }

        public static void Setup()
        {
            if (ready) return;
            Root = ResolveRoot(AppDomain.CurrentDomain.BaseDirectory, InstalledRoot);
            string api = Path.Combine(Root, "iGameAPI");
            if (!SetDllDirectory(api)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Environment.SetEnvironmentVariable("PATH", api + ";" + Root + ";" + Environment.GetEnvironmentVariable("PATH"));
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                string name = new AssemblyName(args.Name).Name;
                foreach (string dir in new[] { Root, api }) {
                    string path = Path.Combine(dir, name + ".dll");
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            ready = true;
        }
    }
}

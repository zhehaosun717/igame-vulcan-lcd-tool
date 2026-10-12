using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

static class PackageProbe {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
    static extern int GetModuleFileName(IntPtr module, StringBuilder path, int size);
    static int Main(string[] args) {
        try {
            if (args.Length == 2) {
                var installer = Assembly.LoadFile(Path.GetFullPath(args[0]));
                using (var input = installer.GetManifestResourceStream("payload.zip"))
                using (var output = File.Create(args[1])) input.CopyTo(output);
                Console.WriteLine("PASS installer payload extracted without running installer");
                return 0;
            }
            string app = AppDomain.CurrentDomain.BaseDirectory;
            var program = Assembly.LoadFrom(Path.Combine(app, "GpuLcd.exe"));
            var vendor = program.GetType("GpuLcd.Vendor", true);
            vendor.GetMethod("Setup").Invoke(null, null);
            string root = (string)vendor.GetProperty("Root").GetValue(null, null);
            string bundled = Path.Combine(app, "vendor", "iGameCenter");
            if (!string.Equals(root, bundled, StringComparison.OrdinalIgnoreCase)) throw new Exception("Not using bundled runtime: " + root);
            foreach (string name in new[] { "iGameAPI.Contracts", "iGameCenter.Contracts", "iGameAPI.NVAPI.CSharp", "iGameAPI.LCD.CSharp" }) {
                var loaded = Assembly.Load(name);
                if (!loaded.Location.StartsWith(bundled + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("External assembly: " + loaded.Location);
                loaded.GetTypes();
                Console.WriteLine("PASS managed " + name);
            }
            foreach (string name in new[] { "iGameAPI.NVAPI.dll", "iGameAPI.LCD.dll" }) {
                string path = Path.Combine(bundled, "iGameAPI", name);
                // Match the relative DllImport used by the vendor wrappers.
                IntPtr handle = LoadLibrary(@".\iGameAPI\" + name);
                if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var actual = new StringBuilder(4096); GetModuleFileName(handle, actual, actual.Capacity);
                if (!string.Equals(actual.ToString(), path, StringComparison.OrdinalIgnoreCase)) throw new Exception("External native library");
                Console.WriteLine("PASS native " + name);
            }
            program.GetTypes(); // Resolve all host field/method type references, but do not construct a device or form.
            Console.WriteLine("PASS host types and bundled runtime; no LCD commands issued");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

using System;
using System.IO;
namespace GpuLcd {
    static class VendorRuntimeTests {
        static int count;
        static void Check(bool ok) { if (!ok) throw new Exception("Runtime test failed: " + count); count++; }
        static void Fill(string root) {
            foreach (string name in Vendor.Required) {
                string path = Path.Combine(root, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "fixture");
            }
        }
        static void Main() {
            string root = Path.Combine(Path.GetTempPath(), "GpuLcdRuntime-" + Guid.NewGuid().ToString("N"));
            try {
                string installed = Path.Combine(root, "installed"); Fill(installed);
                Check(Vendor.ResolveRoot(root, installed) == installed);
                string bundled = Path.Combine(root, "vendor", "iGameCenter"); Fill(bundled);
                Check(Vendor.ResolveRoot(root, installed) == bundled);
                Check(Vendor.ResolveRoot(root, Path.Combine(root, "missing")) == bundled);
                File.Delete(Path.Combine(bundled, Vendor.Required[0]));
                try { Vendor.ResolveRoot(root, installed); throw new Exception("Partial bundle accepted"); }
                catch (FileNotFoundException) { count++; }
                Directory.Delete(bundled, true);
                try { Vendor.ResolveRoot(root, Path.Combine(root, "missing")); throw new Exception("Missing runtime accepted"); }
                catch (FileNotFoundException) { count++; }
                Console.WriteLine(count + " runtime selection assertions passed.");
            } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}

// Packaging-only probe. Never invokes a host/installer method or entry point.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
static class ReleaseResourceProbe {
    static int Main(string[] args) {
        try {
            if (args.Length != 4) throw new ArgumentException("assembly resource output version");
            string path = Path.GetFullPath(args[0]), version = args[3];
            var assembly = Assembly.LoadFile(path);
            string expected = version + ".0";
            if (assembly.GetName().Version.ToString() != expected) throw new Exception("Assembly version mismatch");
            if (FileVersionInfo.GetVersionInfo(path).FileVersion != expected) throw new Exception("File version mismatch");
            if (args[1] == "payload.zip") {
                var field = assembly.GetType("Setup", true).GetField("Version", BindingFlags.Static | BindingFlags.NonPublic);
                if (field == null || !field.IsLiteral || (string)field.GetRawConstantValue() != version) throw new Exception("Installer display version mismatch");
            } else {
                var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (info == null || info.InformationalVersion != version) throw new Exception("Informational version mismatch");
            }
            using (var input = assembly.GetManifestResourceStream(args[1])) {
                if (input == null) throw new Exception("Missing embedded resource: " + args[1]);
                using (var output = File.Create(args[2])) input.CopyTo(output);
            }
            Console.WriteLine("PASS metadata " + Path.GetFileName(path) + " " + expected + "; extracted " + args[1] + "; no entry point invoked");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

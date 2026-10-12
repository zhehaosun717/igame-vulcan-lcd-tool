using System;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace GpuLcd {
    // The hook event pipe. Hooks often run at low integrity (a sandboxed assistant, or GpuLcdHook.exe in a folder that
    // carries a Low mandatory label), and a default pipe from a medium-integrity GpuLcd refuses their writes, so every
    // event was silently lost. This pipe carries a Low label and only lets the current user (plus SYSTEM and admins) in.
    static class EventPipe {
        const uint PIPE_ACCESS_DUPLEX = 0x3, FILE_FLAG_OVERLAPPED = 0x40000000;
        const uint PIPE_TYPE_BYTE = 0x0, PIPE_REJECT_REMOTE_CLIENTS = 0x8;

        [StructLayout(LayoutKind.Sequential)]
        struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances,
            uint outBuffer, uint inBuffer, uint defaultTimeout, ref SecurityAttributes attributes);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, IntPtr size);
        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr memory);

        // D: SYSTEM, admins and the current user get full access; S: Low mandatory label, no-write-up, so low-integrity
        // processes of this user may write.
        public static string Sddl(string userSid) {
            return "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;" + userSid + ")S:(ML;;NW;;;LW)";
        }

        public static NamedPipeServerStream Create(string name) {
            IntPtr descriptor;
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(Sddl(WindowsIdentity.GetCurrent().User.Value), 1, out descriptor, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                var attributes = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor = descriptor };
                var handle = CreateNamedPipe(@"\\.\pipe\" + name, PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
                    PIPE_TYPE_BYTE | PIPE_REJECT_REMOTE_CLIENTS, 1, 4096, 4096, 0, ref attributes);
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
            } finally { LocalFree(descriptor); }
        }
    }
}

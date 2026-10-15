using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace KanjiPop
{
    /// <summary>
    /// Thin P/Invoke layer over the macOS frameworks (Objective-C runtime, CoreGraphics, CoreFoundation).
    /// Every public member is safe to call on other platforms: it just returns a neutral value.
    /// </summary>
    static class Mac
    {
        const string ObjC = "/usr/lib/libobjc.A.dylib";
        const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        public static bool IsMac => OperatingSystem.IsMacOS();

        // ------------------------------------------------------------ Objective-C runtime
        [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr recv, IntPtr sel);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr recv, IntPtr sel, IntPtr a);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern long MsgLong(IntPtr recv, IntPtr sel);

        static IntPtr Cls(string n) => objc_getClass(n);
        static IntPtr Sel(string n) => sel_registerName(n);

        static string? Str(IntPtr nsString)
        {
            if (nsString == IntPtr.Zero) return null;
            var p = Msg(nsString, Sel("UTF8String"));
            return p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
        }

        static IntPtr NSString(string s)
        {
            var utf8 = Marshal.StringToCoTaskMemUTF8(s);
            try { return Msg(Cls("NSString"), Sel("stringWithUTF8String:"), utf8); }
            finally { Marshal.FreeCoTaskMem(utf8); }
        }

        static IntPtr GeneralPasteboard() => Msg(Cls("NSPasteboard"), Sel("generalPasteboard"));

        /// <summary>NSPasteboard.changeCount - increases on every copy. -1 when unavailable.</summary>
        public static long ChangeCount()
        {
            if (!IsMac) return -1;
            try { return MsgLong(GeneralPasteboard(), Sel("changeCount")); }
            catch { return -1; }
        }

        public static string? PasteboardText()
        {
            if (!IsMac) return null;
            try { return Str(Msg(GeneralPasteboard(), Sel("stringForType:"), NSString("public.utf8-plain-text"))); }
            catch { return null; }
        }

        /// <summary>Localized name of the app that currently has keyboard focus (e.g. "Safari").</summary>
        public static string? FrontAppName()
        {
            if (!IsMac) return null;
            try
            {
                var ws = Msg(Cls("NSWorkspace"), Sel("sharedWorkspace"));
                var app = Msg(ws, Sel("frontmostApplication"));
                return app == IntPtr.Zero ? null : Str(Msg(app, Sel("localizedName")));
            }
            catch { return null; }
        }

        /// <summary>Names of regular (Dock) apps that are running.</summary>
        public static List<string> RunningAppNames()
        {
            var list = new List<string>();
            if (!IsMac) return list;
            try
            {
                var ws = Msg(Cls("NSWorkspace"), Sel("sharedWorkspace"));
                var apps = Msg(ws, Sel("runningApplications"));
                long n = MsgLong(apps, Sel("count"));
                for (long i = 0; i < n; i++)
                {
                    var app = Msg(apps, Sel("objectAtIndex:"), (IntPtr)i);
                    if (MsgLong(app, Sel("activationPolicy")) != 0) continue; // 0 = NSApplicationActivationPolicyRegular
                    var name = Str(Msg(app, Sel("localizedName")));
                    if (!string.IsNullOrEmpty(name) && !list.Contains(name)) list.Add(name);
                }
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        // ------------------------------------------------------------ CoreGraphics events
        [StructLayout(LayoutKind.Sequential)]
        public struct CGPoint { public double X, Y; }

        public delegate IntPtr TapCallback(IntPtr proxy, uint type, IntPtr ev, IntPtr info);

        [DllImport(CG)] static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong mask, TapCallback cb, IntPtr info);
        [DllImport(CG)] static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.U1)] bool enable);
        [DllImport(CG)] static extern long CGEventGetIntegerValueField(IntPtr ev, uint field);
        [DllImport(CG)] static extern void CGEventSetIntegerValueField(IntPtr ev, uint field, long value);
        [DllImport(CG)] static extern ulong CGEventGetFlags(IntPtr ev);
        [DllImport(CG)] static extern void CGEventSetFlags(IntPtr ev, ulong flags);
        [DllImport(CG)] static extern CGPoint CGEventGetLocation(IntPtr ev);
        [DllImport(CG)] static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(CG)] static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort key, [MarshalAs(UnmanagedType.U1)] bool down);
        [DllImport(CG)] static extern void CGEventPost(uint tap, IntPtr ev);
        [DllImport(CG)][return: MarshalAs(UnmanagedType.U1)] static extern bool CGPreflightListenEventAccess();
        [DllImport(CG)][return: MarshalAs(UnmanagedType.U1)] static extern bool CGRequestListenEventAccess();

        [DllImport(CF)] static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr alloc, IntPtr port, long order);
        [DllImport(CF)] static extern IntPtr CFRunLoopGetMain();
        [DllImport(CF)] static extern void CFRunLoopAddSource(IntPtr rl, IntPtr src, IntPtr mode);
        [DllImport(CF)] static extern void CFRelease(IntPtr obj);

        public const uint EvLeftDown = 1, EvRightDown = 3, EvKeyDown = 10, EvOtherDown = 25;
        public const uint EvTapTimeout = 0xFFFFFFFE, EvTapUserDisabled = 0xFFFFFFFF;
        public const ulong FlagShift = 0x20000, FlagCtrl = 0x40000, FlagAlt = 0x80000, FlagCmd = 0x100000;
        const uint FieldKeycode = 9, FieldAutorepeat = 8, FieldUserData = 42;
        /// <summary>Marks key events we post ourselves so the tap ignores them.</summary>
        public const long OwnMagic = 0x4B4A5031; // "KJP1"

        public static long KeyCode(IntPtr ev) => CGEventGetIntegerValueField(ev, FieldKeycode);
        public static bool IsAutorepeat(IntPtr ev) => CGEventGetIntegerValueField(ev, FieldAutorepeat) != 0;
        public static bool IsOwn(IntPtr ev) => CGEventGetIntegerValueField(ev, FieldUserData) == OwnMagic;
        public static ulong Flags(IntPtr ev) => CGEventGetFlags(ev);
        public static CGPoint Location(IntPtr ev) => CGEventGetLocation(ev);

        /// <summary>Mouse position in points (origin top-left of the main display).</summary>
        public static (double X, double Y) CursorPoints()
        {
            if (!IsMac) return (200, 200);
            try
            {
                var ev = CGEventCreate(IntPtr.Zero);
                if (ev == IntPtr.Zero) return (200, 200);
                var p = CGEventGetLocation(ev);
                CFRelease(ev);
                return (p.X, p.Y);
            }
            catch { return (200, 200); }
        }

        public static bool HasListenAccess()
        {
            if (!IsMac) return true;
            try { return CGPreflightListenEventAccess(); } catch { return true; }
        }

        public static void RequestListenAccess()
        {
            if (!IsMac) return;
            try { CGRequestListenEventAccess(); } catch { }
        }

        public static void OpenInputMonitoringSettings()
        {
            if (!IsMac) return;
            try
            {
                Process.Start(new ProcessStartInfo("open",
                    "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent") { UseShellExecute = false });
            }
            catch { }
        }

        /// <summary>Create a listen-only session event tap on the main run loop. Returns IntPtr.Zero if macOS refuses.</summary>
        public static IntPtr CreateTap(ulong mask, TapCallback cb)
        {
            if (!IsMac) return IntPtr.Zero;
            // kCGSessionEventTap = 1, kCGHeadInsertEventTap = 0, kCGEventTapOptionListenOnly = 1
            var tap = CGEventTapCreate(1, 0, 1, mask, cb, IntPtr.Zero);
            if (tap == IntPtr.Zero) return IntPtr.Zero;
            var src = CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, 0);
            var lib = NativeLibrary.Load(CF);
            var modeSym = NativeLibrary.GetExport(lib, "kCFRunLoopCommonModes");
            var commonModes = Marshal.ReadIntPtr(modeSym);
            CFRunLoopAddSource(CFRunLoopGetMain(), src, commonModes);
            CFRelease(src);
            CGEventTapEnable(tap, true);
            return tap;
        }

        public static void EnableTap(IntPtr tap) { if (IsMac && tap != IntPtr.Zero) CGEventTapEnable(tap, true); }

        /// <summary>Post Cmd+C to whatever app has focus (used when the hotkey is not Cmd+C itself).</summary>
        public static void PostCopy()
        {
            if (!IsMac) return;
            try
            {
                foreach (bool down in new[] { true, false })
                {
                    var ev = CGEventCreateKeyboardEvent(IntPtr.Zero, 8 /* C */, down);
                    if (ev == IntPtr.Zero) continue;
                    CGEventSetFlags(ev, FlagCmd);
                    CGEventSetIntegerValueField(ev, FieldUserData, OwnMagic);
                    CGEventPost(0 /* kCGHIDEventTap */, ev);
                    CFRelease(ev);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------ login item
        static string LaunchAgentPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", "com.kanjipop.app.plist");

        public static bool GetAutostart() => IsMac && File.Exists(LaunchAgentPath);

        public static void SetAutostart(bool on)
        {
            if (!IsMac) return;
            try
            {
                if (!on) { if (File.Exists(LaunchAgentPath)) File.Delete(LaunchAgentPath); return; }
                var exe = Environment.ProcessPath ?? "";
                Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentPath)!);
                File.WriteAllText(LaunchAgentPath,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                    "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                    "<plist version=\"1.0\"><dict>\n" +
                    "<key>Label</key><string>com.kanjipop.app</string>\n" +
                    "<key>ProgramArguments</key><array><string>" + System.Security.SecurityElement.Escape(exe) + "</string></array>\n" +
                    "<key>RunAtLoad</key><true/>\n" +
                    "</dict></plist>\n");
            }
            catch { }
        }
    }
}

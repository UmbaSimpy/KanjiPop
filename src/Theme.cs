using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace KanjiPop
{
    public static class Theme
    {
        public static Brush Frozen(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            b.Freeze();
            return b;
        }

        public static readonly Brush Bg = Frozen("#F2202127");
        public static readonly Brush Solid = Frozen("#202127");
        public static readonly Brush Field = Frozen("#2D2F39");
        public static readonly Brush Hover = Frozen("#2A2C35");
        public static readonly Brush Border_ = Frozen("#3A3C46");
        public static readonly Brush Fg = Frozen("#ECECF0");
        public static readonly Brush Dim = Frozen("#9A9CA8");
        public static readonly Brush Accent = Frozen("#7CC4FF");
        public static readonly Brush PosBrush = Frozen("#E8B96A");
        public static readonly Brush Gold = Frozen("#FFC94D");
        public static readonly FontFamily Font = new("Yu Gothic UI, Meiryo UI, Segoe UI");
        public static readonly FontFamily Symbols = new("Segoe UI Symbol, Segoe UI");

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(System.IntPtr h, int attr, ref int v, int size);

        /// <summary>Dark title bar for normal windows (Windows 10 20H1+/11).</summary>
        public static void DarkTitleBar(Window w)
        {
            w.SourceInitialized += (_, _) =>
            {
                int on = 1;
                DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, 20, ref on, 4);
            };
        }
    }
}

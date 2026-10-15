using Avalonia.Media;

namespace KanjiPop
{
    public static class Theme
    {
        static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex)).ToImmutable();

        public static readonly IBrush Bg = B("#F2202127");
        public static readonly IBrush Solid = B("#202127");
        public static readonly IBrush Field = B("#2D2F39");
        public static readonly IBrush Hover = B("#2A2C35");
        public static readonly IBrush Border_ = B("#3A3C46");
        public static readonly IBrush Fg = B("#ECECF0");
        public static readonly IBrush Dim = B("#9A9CA8");
        public static readonly IBrush Accent = B("#7CC4FF");
        public static readonly IBrush PosBrush = B("#E8B96A");
        public static readonly IBrush Gold = B("#FFC94D");
        public static readonly IBrush Blue = B("#2E78C8");
        public static readonly IBrush Clear = Brushes.Transparent;

        // Unknown family names are skipped, so this list works on both macOS and Windows.
        public static readonly FontFamily Font = new("Hiragino Sans, Hiragino Kaku Gothic ProN, Yu Gothic UI, Meiryo UI, Segoe UI");
        public static readonly FontFamily Symbols = new("Apple Symbols, Segoe UI Symbol, Hiragino Sans, Segoe UI");
    }
}

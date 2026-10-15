using System;
using System.IO;
using System.Text;
using System.Threading;
using Avalonia;

namespace KanjiPop
{
    static class Program
    {
        public static string[] Args = Array.Empty<string>();
        public static string DbPath = "";

        [STAThread]
        static int Main(string[] args)
        {
            Args = args;
            var exeDir = AppContext.BaseDirectory;
            DbPath = Path.Combine(exeDir, "data", "dict.db");
            if (!File.Exists(DbPath)) DbPath = Path.Combine(exeDir, "dict.db");

            // --dump "text|text" [outfile] : print the analysis and exit (smoke test, no UI)
            if (args.Length >= 2 && args[0] == "--dump")
            {
                using var d = new Dict(DbPath);
                var text = DumpText(args[1], new Analyzer(d, Config.Load()));
                if (args.Length >= 3) File.WriteAllText(args[2], text);
                else { Console.OutputEncoding = Encoding.UTF8; Console.Write(text); }
                return 0;
            }

            if (!File.Exists(DbPath))
            {
                Console.Error.WriteLine("Dictionary file not found: " + DbPath);
                return 1;
            }

            using var mutex = new Mutex(true, "Local\\KanjiPop.Avalonia.SingleInstance", out bool first);
            if (!first) return 0;

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new MacOSPlatformOptions { ShowInDock = false })
                .LogToTrace();

        static string DumpText(string input, Analyzer an)
        {
            var sb = new StringBuilder();
            foreach (var line in input.Split('|'))
            {
                var a = an.Analyze(line);
                sb.AppendLine($"== {line}");
                foreach (var t in a.Tokens)
                {
                    sb.Append($"  {t.Surface} [{t.Reading}]");
                    if (t.Entries.Count > 0)
                    {
                        var e = t.Entries[0];
                        sb.Append(e.Direct ? "" : $" <- {e.DictForm} ({e.Reasons})");
                        sb.Append(" : " + string.Join("; ", e.Senses.Count > 0 ? e.Senses[0].Gloss : new()));
                        sb.Append($"  [{t.Entries.Count} entries]");
                    }
                    sb.AppendLine();
                }
                foreach (var k in a.Kanji)
                    sb.AppendLine($"  K {k.Literal} on={string.Join(",", k.On)} kun={string.Join(",", k.Kun)} = {string.Join(",", k.Meanings)} jlpt={k.Jlpt} strokes={k.Strokes}");
            }
            return sb.ToString();
        }
    }
}

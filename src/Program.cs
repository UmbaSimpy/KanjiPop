using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KanjiPop
{
    static class Program
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        [STAThread]
        static int Main(string[] args)
        {
            var exeDir = AppContext.BaseDirectory;
            var dbPath = Path.Combine(exeDir, "data", "dict.db");
            if (!File.Exists(dbPath)) dbPath = Path.Combine(exeDir, "dict.db");

            // --dump "text" [outfile] : print analysis (for testing), no UI
            if (args.Length >= 2 && args[0] == "--dump")
            {
                var cfg0 = Config.Load();
                using var d = new Dict(dbPath);
                var an = new Analyzer(d, cfg0);
                var text = DumpText(args[1], an);
                if (args.Length >= 3) File.WriteAllText(args[2], text);
                else { Native.AttachConsole(-1); Console.OutputEncoding = System.Text.Encoding.UTF8; Console.Write(text); }
                return 0;
            }

            using var mutex = new Mutex(true, "Local\\KanjiPop.SingleInstance", out bool first);
            if (!first) return 0;

            if (!File.Exists(dbPath))
            {
                System.Windows.MessageBox.Show("Dictionary file not found:\n" + dbPath, "KanjiPop");
                return 1;
            }

            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var host = new Host(dbPath);
            host.Start();
            // --show "text" : popup immediately (for testing/screenshots)
            if (args.Length >= 2 && args[0] == "--show") host.ShowText(args[1]);
            if (args.Length >= 2 && args[0] == "--open") host.OpenWindow(args[1]);
            return app.Run();
        }

        static string DumpText(string input, Analyzer an)
        {
            var sb = new System.Text.StringBuilder();
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

    /// <summary>Owns tray icon, hooks, dictionary, popup and the settings/library windows.</summary>
    sealed class Host
    {
        Config _cfg = Config.Load();
        readonly Dict _dict;
        Analyzer _analyzer;
        readonly AppState _state = AppState.Load();
        readonly ItemStore _history = new("history.json");
        readonly ItemStore _favorites = new("favorites.json");
        Analysis? _last;
        PopupWindow _popup;
        InputHooks? _hooks;
        NotifyIcon? _tray;
        SettingsWindow? _settings;
        bool _pausedAll;
        LibraryWindow? _library;

        public Host(string dbPath)
        {
            _dict = new Dict(dbPath);
            _analyzer = new Analyzer(_dict, _cfg);
            _popup = NewPopup();
        }

        PopupWindow NewPopup() => new(_cfg, _state,
            () => _last != null && _favorites.Contains(_last.Text),
            ToggleFavorite);

        bool ToggleFavorite()
        {
            if (_last == null) return false;
            if (_favorites.Contains(_last.Text)) { _favorites.Remove(_last.Text); return false; }
            _favorites.Add(HistoryItem.From(_last), 0);
            return true;
        }

        readonly System.Windows.Threading.DispatcherTimer _trim = new() { Interval = TimeSpan.FromSeconds(4) };

        public void Start()
        {
            BuildTray();
            InstallHooks();
            _trim.Tick += (_, _) => { _trim.Stop(); if (!_popup.IsShown) Native.TrimMemory(); };
            ScheduleTrim();
        }

        void ScheduleTrim() { _trim.Stop(); _trim.Start(); }

        void InstallHooks()
        {
            _hooks?.Dispose();
            try
            {
                _hooks = new InputHooks(_cfg) { PopupVisible = () => _popup.IsShown, Paused = IsPaused };
                _hooks.Triggered += t => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(async () => await OnTriggered(t)));
                _hooks.EscapePressed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => { _popup.HidePopup(); ScheduleTrim(); }));
                _hooks.MouseClicked += (x, y) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_popup.IsShown && !_popup.ContainsPx(x, y)) { _popup.HidePopup(); ScheduleTrim(); }
                }));
                if (_tray != null) _tray.Text = $"KanjiPop — {_cfg.Hotkey}{(_cfg.DoubleTap ? " ×2" : "")}";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Cannot use hotkey '" + _cfg.Hotkey + "':\n" + ex.Message, "KanjiPop");
            }
        }

        bool IsPaused()
        {
            if (_pausedAll) return true;
            if (_cfg.PausedApps.Count == 0) return false;
            var fg = Native.ForegroundProcessName();
            return fg.Length > 0 && _cfg.PausedApps.Any(a =>
                string.Equals(System.IO.Path.GetFileNameWithoutExtension(a.Trim()), fg, StringComparison.OrdinalIgnoreCase));
        }

        async Task OnTriggered(TriggerInfo t)
        {
            if (t.InjectCopy) InputHooks.InjectCopy(t.Key);
            // wait for the source app to actually change the clipboard (so we never show stale text)
            for (int i = 0; i < 20 && Native.GetClipboardSequenceNumber() == t.SeqBefore; i++)
                await Task.Delay(25);
            if (Native.GetClipboardSequenceNumber() == t.SeqBefore) return;
            await Task.Delay(40);
            var text = ReadClipboard();
            if (!string.IsNullOrWhiteSpace(text)) ShowText(text);
        }

        static string? ReadClipboard()
        {
            for (int i = 0; i < 8; i++)
            {
                try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null; }
                catch { Thread.Sleep(25); }
            }
            return null;
        }

        public void ShowText(string text)
        {
            if (!Kana.HasJapanese(text)) return;
            Native.GetCursorPos(out var p);
            var a = _analyzer.Analyze(text);
            _last = a;
            if (_cfg.MaxHistory > 0 && a.Tokens.Count > 0) _history.Add(HistoryItem.From(a), _cfg.MaxHistory);
            _popup.Present(a, p.X, p.Y);
        }

        void BuildTray()
        {
            _tray = new NotifyIcon { Icon = MakeIcon(), Visible = true, Text = "KanjiPop" };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Look up clipboard", null, (_, _) => { var t = ReadClipboard(); if (t != null) ShowText(t); });
            menu.Items.Add("History…", null, (_, _) => OpenLibrary(false));
            menu.Items.Add("Favorites…", null, (_, _) => OpenLibrary(true));
            menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
            var pause = new ToolStripMenuItem("Pause everywhere") { CheckOnClick = true };
            pause.CheckedChanged += (_, _) =>
            {
                _pausedAll = pause.Checked;
                if (_tray != null) _tray.Text = _pausedAll ? "KanjiPop — paused" : $"KanjiPop — {_cfg.Hotkey}{(_cfg.DoubleTap ? " ×2" : "")}";
            };
            menu.Items.Add(pause);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, (_, _) => Quit());
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) OpenLibrary(false);
            };
        }

        public void OpenWindow(string name)
        {
            if (name == "settings") OpenSettings(); else OpenLibrary(name == "favorites");
        }

        void OpenLibrary(bool favorites)
        {
            if (_library == null)
            {
                _library = new LibraryWindow(_history, _favorites, ShowText);
                _library.Closed += (_, _) => _library = null;
                _library.Show();
            }
            _library.ShowTab(favorites);
            if (_library.WindowState == System.Windows.WindowState.Minimized) _library.WindowState = System.Windows.WindowState.Normal;
            _library.Activate();
        }

        void OpenSettings()
        {
            if (_settings != null) { _settings.Activate(); return; }
            _hooks?.SetEnabled(false); // don't trigger lookups while recording a new shortcut
            _settings = new SettingsWindow(_cfg, IsAutostart(), (cfg, autostart) =>
            {
                cfg.Save();
                SetAutostart(autostart);
                Reload();
            });
            _settings.Closed += (_, _) => { _settings = null; _hooks?.SetEnabled(true); };
            _settings.Show();
            _settings.Activate();
        }

        void Reload()
        {
            _cfg = Config.Load();
            _analyzer = new Analyzer(_dict, _cfg);
            _popup.HidePopup();
            _popup.Close();
            _popup = NewPopup();
            InstallHooks();
            _hooks?.SetEnabled(_settings == null);
        }

        void Quit()
        {
            _hooks?.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _dict.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        static bool IsAutostart()
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return k?.GetValue("KanjiPop") != null;
        }

        static void SetAutostart(bool on)
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k == null) return;
            if (on) k.SetValue("KanjiPop", "\"" + Environment.ProcessPath + "\"");
            else k.DeleteValue("KanjiPop", false);
        }

        static Icon MakeIcon()
        {
            using var st = typeof(Host).Assembly.GetManifestResourceStream("app.ico")!;
            return new Icon(st, SystemInformation.SmallIconSize);
        }
    }
}

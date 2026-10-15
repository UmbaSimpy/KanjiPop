using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace KanjiPop
{
    /// <summary>Owns menu-bar icon, hotkey listener, dictionary, popup and the settings/library windows.</summary>
    sealed class Host
    {
        Config _cfg = Config.Load();
        readonly Dict _dict;
        readonly IClassicDesktopStyleApplicationLifetime _lifetime;
        Analyzer _analyzer;
        readonly AppState _state = AppState.Load();
        readonly ItemStore _history = new("history.json");
        readonly ItemStore _favorites = new("favorites.json");
        Analysis? _last;
        PopupWindow _popup;
        HotkeyService? _hot;
        TrayIcon? _tray;
        NativeMenuItem? _pauseItem, _statusItem;
        SettingsWindow? _settings;
        LibraryWindow? _library;
        bool _pausedAll;
        readonly DispatcherTimer _retry = new() { Interval = TimeSpan.FromSeconds(3) };
        bool _permissionAsked;

        public Host(string dbPath, IClassicDesktopStyleApplicationLifetime lifetime)
        {
            _lifetime = lifetime;
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

        public void Start()
        {
            BuildTray();
            StartHotkeys();
        }

        // ------------------------------------------------------------ hotkey

        void StartHotkeys()
        {
            try
            {
                _hot = new HotkeyService(_cfg) { PopupVisible = () => _popup.IsShown, Paused = IsPaused };
            }
            catch (FormatException ex)
            {
                _ = Dialogs.Ask(null, ex.Message + "\n\nUsing the default hotkey instead.", "OK");
                _cfg.Hotkey = new Config().Hotkey;
                _cfg.Save();
                _hot = new HotkeyService(_cfg) { PopupVisible = () => _popup.IsShown, Paused = IsPaused };
            }
            _hot.Triggered += t => Dispatcher.UIThread.Post(() => _ = OnTriggered(t));
            _hot.EscapePressed += () => Dispatcher.UIThread.Post(() => _popup.HidePopup());
            _hot.MouseClicked += (x, y) => Dispatcher.UIThread.Post(() =>
            {
                if (_popup.IsShown && !_popup.ContainsPoint(x, y)) _popup.HidePopup();
            });
            UpdateStatusText();

            if (_hot.Start()) return;
            // macOS refused the event tap: the Input Monitoring permission is missing
            Mac.RequestListenAccess();
            _retry.Tick += (_, _) => { if (_hot!.Start()) _retry.Stop(); };
            _retry.Start();
            if (!_permissionAsked)
            {
                _permissionAsked = true;
                _ = AskForPermission();
            }
        }

        async Task AskForPermission()
        {
            var r = await Dialogs.Ask(null,
                "KanjiPop needs the “Input Monitoring” permission to notice your copy shortcut.\n\n" +
                "Open System Settings → Privacy & Security → Input Monitoring, switch KanjiPop on, then quit and reopen KanjiPop.\n\n" +
                "Nothing you type is stored or sent anywhere; the app only looks for the shortcut.",
                "Open System Settings", "Later");
            if (r == 0) Mac.OpenInputMonitoringSettings();
        }

        bool IsPaused()
        {
            if (_pausedAll) return true;
            if (_cfg.PausedApps.Count == 0) return false;
            var front = Mac.FrontAppName();
            if (front == null) return false;
            return _cfg.PausedApps.Any(a => string.Equals(a, front, StringComparison.OrdinalIgnoreCase));
        }

        void UpdateStatusText()
        {
            var text = $"KanjiPop — {_cfg.Hotkey}{(_cfg.DoubleTap ? " ×2" : "")}{(_pausedAll ? " (paused)" : "")}";
            if (_tray != null) _tray.ToolTipText = text;
            if (_statusItem != null) _statusItem.Header = text;
        }

        async Task OnTriggered(TriggerInfo t)
        {
            if (t.InjectCopy) Mac.PostCopy();
            long now = Mac.ChangeCount();
            if (now >= 0)
            {
                // wait for the source app to actually change the clipboard (so we never show stale text)
                for (int i = 0; i < 20 && Mac.ChangeCount() == t.SeqBefore; i++)
                    await Task.Delay(25);
                if (Mac.ChangeCount() == t.SeqBefore) return;
                await Task.Delay(40);
            }
            else await Task.Delay(200);
            var text = await ReadClipboard();
            if (!string.IsNullOrWhiteSpace(text)) ShowText(text);
        }

        async Task<string?> ReadClipboard()
        {
            if (Mac.IsMac)
            {
                var t = Mac.PasteboardText();
                if (t != null) return t;
            }
            try
            {
                var cb = TopLevel.GetTopLevel(_popup)?.Clipboard;
                return cb == null ? null : await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(cb);
            }
            catch { return null; }
        }

        public void ShowText(string text)
        {
            if (!Kana.HasJapanese(text)) return;
            var (cx, cy) = Mac.CursorPoints();
            var a = _analyzer.Analyze(text);
            _last = a;
            if (_cfg.MaxHistory > 0 && a.Tokens.Count > 0) _history.Add(HistoryItem.From(a), _cfg.MaxHistory);
            _popup.Present(a, cx, cy);
        }

        // ------------------------------------------------------------ menu bar icon

        void BuildTray()
        {
            var menu = new NativeMenu();
            _statusItem = new NativeMenuItem("KanjiPop") { IsEnabled = false };
            menu.Add(_statusItem);
            menu.Add(new NativeMenuItemSeparator());
            Add(menu, "Look up clipboard", async () => { var t = await ReadClipboard(); if (t != null) ShowText(t); });
            Add(menu, "History…", () => OpenLibrary(false));
            Add(menu, "Favorites…", () => OpenLibrary(true));
            Add(menu, "Settings…", OpenSettings);
            menu.Add(new NativeMenuItemSeparator());
            _pauseItem = new NativeMenuItem("Pause everywhere") { ToggleType = NativeMenuItemToggleType.CheckBox };
            _pauseItem.Click += (_, _) => { _pausedAll = _pauseItem.IsChecked; UpdateStatusText(); };
            menu.Add(_pauseItem);
            menu.Add(new NativeMenuItemSeparator());
            Add(menu, "Quit KanjiPop", Quit);

            using var icon = AssetLoader.Open(new Uri("avares://KanjiPop/Assets/tray.png"));
            _tray = new TrayIcon { Icon = new WindowIcon(icon), Menu = menu, IsVisible = true };
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
            UpdateStatusText();
        }

        static void Add(NativeMenu menu, string header, Action act)
        {
            var item = new NativeMenuItem(header);
            item.Click += (_, _) => act();
            menu.Add(item);
        }

        static void Add(NativeMenu menu, string header, Func<Task> act)
        {
            var item = new NativeMenuItem(header);
            item.Click += async (_, _) => await act();
            menu.Add(item);
        }

        public void OpenWindow(string which)
        {
            switch (which)
            {
                case "settings": OpenSettings(); break;
                case "history": OpenLibrary(false); break;
                case "favorites": OpenLibrary(true); break;
            }
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
            if (_library.WindowState == WindowState.Minimized) _library.WindowState = WindowState.Normal;
            _library.Activate();
        }

        void OpenSettings()
        {
            if (_settings != null) { _settings.Activate(); return; }
            _hot?.SetEnabled(false); // don't trigger lookups while recording a new shortcut
            _settings = new SettingsWindow(_cfg, Mac.GetAutostart(), (cfg, autostart) =>
            {
                cfg.Save();
                Mac.SetAutostart(autostart);
                Reload();
            });
            _settings.Closed += (_, _) => { _settings = null; _hot?.SetEnabled(true); };
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
            try { _hot?.Reconfigure(_cfg); }
            catch (FormatException ex) { _ = Dialogs.Ask(null, ex.Message, "OK"); }
            UpdateStatusText();
        }

        void Quit()
        {
            _retry.Stop();
            _hot?.Dispose();
            if (_tray != null) _tray.IsVisible = false;
            _dict.Dispose();
            _lifetime.Shutdown();
        }
    }
}

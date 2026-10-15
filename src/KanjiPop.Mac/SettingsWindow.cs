using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using static KanjiPop.Theme;

namespace KanjiPop
{
    /// <summary>Settings page (opened from the menu-bar icon). Works on a copy of the config; Save applies it.</summary>
    public sealed class SettingsWindow : Window
    {
        readonly Config _cfg;
        readonly Action<Config, bool> _onSave;
        readonly TextBox _hotkey = new();
        readonly CheckBox _double = new(), _romaji = new(), _kana = new(), _autostart = new();
        readonly Dictionary<string, TextBox> _nums = new();
        readonly TextBlock _status = new();
        readonly ObservableCollection<string> _pausedItems = new();
        readonly ListBox _paused = new();
        readonly TextBox _appName = new();
        string _hotkeyValue;

        public SettingsWindow(Config current, bool autostart, Action<Config, bool> onSave)
        {
            _cfg = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(current)) ?? new Config();
            _onSave = onSave;
            _hotkeyValue = _cfg.Hotkey;

            Title = "KanjiPop settings";
            Width = 480; SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Solid; Foreground = Fg; FontFamily = Font; FontSize = 14;

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            var wa = Screens.Primary?.WorkingArea.Height ?? 900;
            MaxHeight = Math.Max(400, wa / (Screens.Primary?.Scaling ?? 1.0) - 40);
            Content = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = root };

            root.Children.Add(Heading("Hotkey"));
            _hotkey.IsReadOnly = true;
            _hotkey.Cursor = new Cursor(StandardCursorType.Hand);
            Style(_hotkey);
            _hotkey.Text = _hotkeyValue;
            ToolTip.SetTip(_hotkey, "Click, then press the key combination you want");
            _hotkey.GotFocus += (_, _) => _status.Text = "Press the new key combination (with Cmd, Ctrl or Option)…";
            _hotkey.LostFocus += (_, _) => _status.Text = "";
            _hotkey.AddHandler(KeyDownEvent, OnHotkeyKey, RoutingStrategies.Tunnel);
            root.Children.Add(Row("Shortcut (click, then press keys)", _hotkey));
            _double.Content = "Press it twice (double-tap)";
            _double.IsChecked = _cfg.DoubleTap;
            root.Children.Add(Check(_double));
            root.Children.Add(Row("Double-tap window (ms)", Num("DoubleTapMs", _cfg.DoubleTapMs)));
            _status.Foreground = Accent; _status.FontSize = 12; _status.Margin = new Thickness(0, 2, 0, 0);
            _status.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(_status);

            root.Children.Add(Heading("Pause in these apps"));
            root.Children.Add(new TextBlock
            {
                Text = "The hotkey is ignored while one of these apps has focus (for example games).",
                Foreground = Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
            });
            _paused.Height = 84; _paused.Background = Field; _paused.Foreground = Fg; _paused.BorderBrush = Border_;
            _paused.BorderThickness = new Thickness(1);
            foreach (var a in _cfg.PausedApps) _pausedItems.Add(a);
            _paused.ItemsSource = _pausedItems;
            root.Children.Add(_paused);
            Style(_appName); _appName.Watermark = "App name, e.g. Steam";
            var addRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = true };
            var remove = Dialogs.Btn("Remove");
            remove.Click += (_, _) => { if (_paused.SelectedItem is string s) _pausedItems.Remove(s); };
            var running = Dialogs.Btn("Running apps ▾"); running.Click += (_, _) => ShowRunning(running);
            var add = Dialogs.Btn("Add"); add.Click += (_, _) => AddApp(_appName.Text ?? "");
            foreach (var b in new[] { remove, running, add }) { DockPanel.SetDock(b, Dock.Right); addRow.Children.Add(b); }
            addRow.Children.Add(_appName);
            root.Children.Add(addRow);

            root.Children.Add(Heading("Popup"));
            _romaji.Content = "Show romaji"; _romaji.IsChecked = _cfg.ShowRomaji; root.Children.Add(Check(_romaji));
            _kana.Content = "List kana-only words in multi-word selections"; _kana.IsChecked = _cfg.ShowKanaWords; root.Children.Add(Check(_kana));
            root.Children.Add(Row("Font size", Num("FontSize", (int)_cfg.FontSize)));
            root.Children.Add(Row("Auto-close after (seconds, 0 = never)", Num("AutoCloseSeconds", _cfg.AutoCloseSeconds)));
            root.Children.Add(Row("Max entries per word", Num("MaxEntries", _cfg.MaxEntries)));
            root.Children.Add(Row("Max meanings per entry", Num("MaxSensesPerEntry", _cfg.MaxSensesPerEntry)));
            root.Children.Add(Row("Max kanji shown", Num("MaxKanji", _cfg.MaxKanji)));
            root.Children.Add(Row("Max characters looked up", Num("MaxChars", _cfg.MaxChars)));

            root.Children.Add(Heading("General"));
            root.Children.Add(Row("History size (0 = off)", Num("MaxHistory", _cfg.MaxHistory)));
            _autostart.Content = "Start when I log in"; _autostart.IsChecked = autostart; root.Children.Add(Check(_autostart));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var defaults = Dialogs.Btn("Defaults");
            defaults.Click += (_, _) => LoadInto(new Config(), _autostart.IsChecked == true);
            var cancel = Dialogs.Btn("Cancel"); cancel.Click += (_, _) => Close();
            var save = Dialogs.Btn("Save", true);
            save.Click += (_, _) => Save();
            buttons.Children.Add(defaults); buttons.Children.Add(cancel); buttons.Children.Add(save);
            root.Children.Add(buttons);
        }

        void LoadInto(Config c, bool autostart)
        {
            _hotkeyValue = c.Hotkey; _hotkey.Text = c.Hotkey;
            _double.IsChecked = c.DoubleTap; _romaji.IsChecked = c.ShowRomaji; _kana.IsChecked = c.ShowKanaWords;
            _autostart.IsChecked = autostart;
            _pausedItems.Clear();
            _nums["DoubleTapMs"].Text = c.DoubleTapMs.ToString();
            _nums["FontSize"].Text = ((int)c.FontSize).ToString();
            _nums["AutoCloseSeconds"].Text = c.AutoCloseSeconds.ToString();
            _nums["MaxEntries"].Text = c.MaxEntries.ToString();
            _nums["MaxSensesPerEntry"].Text = c.MaxSensesPerEntry.ToString();
            _nums["MaxKanji"].Text = c.MaxKanji.ToString();
            _nums["MaxChars"].Text = c.MaxChars.ToString();
            _nums["MaxHistory"].Text = c.MaxHistory.ToString();
        }

        int N(string key, int min, int max, int fallback)
        {
            if (!int.TryParse((_nums[key].Text ?? "").Trim(), out var v)) v = fallback;
            return Math.Max(min, Math.Min(max, v));
        }

        void Save()
        {
            try { Hotkey.Parse(_hotkeyValue); }
            catch (Exception ex) { _status.Text = ex.Message.StartsWith("A hotkey") ? ex.Message : "That hotkey is not valid."; return; }
            _cfg.Hotkey = _hotkeyValue;
            _cfg.DoubleTap = _double.IsChecked == true;
            _cfg.DoubleTapMs = N("DoubleTapMs", 150, 2000, 500);
            _cfg.ShowRomaji = _romaji.IsChecked == true;
            _cfg.ShowKanaWords = _kana.IsChecked == true;
            _cfg.FontSize = N("FontSize", 9, 32, 14);
            _cfg.AutoCloseSeconds = N("AutoCloseSeconds", 0, 600, 0);
            _cfg.MaxEntries = N("MaxEntries", 1, 10, 3);
            _cfg.MaxSensesPerEntry = N("MaxSensesPerEntry", 1, 20, 5);
            _cfg.MaxKanji = N("MaxKanji", 0, 40, 10);
            _cfg.MaxChars = N("MaxChars", 1, 1000, 120);
            _cfg.MaxHistory = N("MaxHistory", 0, 1000, 30);
            _cfg.PausedApps = _pausedItems.ToList();
            _onSave(_cfg, _autostart.IsChecked == true);
            Close();
        }

        void AddApp(string raw)
        {
            var name = raw.Trim();
            if (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.Length > 0 && !_pausedItems.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                _pausedItems.Add(name);
            _appName.Text = "";
        }

        void ShowRunning(Button anchor)
        {
            var menu = new ContextMenu { MaxHeight = 400 };
            var names = Mac.RunningAppNames();
            foreach (var n in names)
            {
                var item = new MenuItem { Header = n };
                var name = n;
                item.Click += (_, _) => AddApp(name);
                menu.Items.Add(item);
            }
            if (names.Count == 0) menu.Items.Add(new MenuItem { Header = "(none found)", IsEnabled = false });
            menu.Open(anchor);
        }

        void OnHotkeyKey(object? sender, KeyEventArgs e)
        {
            e.Handled = true;
            string? name = e.Key switch
            {
                >= Key.A and <= Key.Z => e.Key.ToString(),
                >= Key.D0 and <= Key.D9 => ((int)e.Key - (int)Key.D0).ToString(),
                Key.Space => "Space",
                _ => null,
            };
            if (name == null) return; // modifier alone, or a key we don't support
            var parts = new List<string>();
            var m = e.KeyModifiers;
            if (m.HasFlag(KeyModifiers.Meta)) parts.Add("Cmd");
            if (m.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (m.HasFlag(KeyModifiers.Alt)) parts.Add("Option");
            if (m.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
            if (parts.Count == 0) { _status.Text = "Use at least one of Cmd, Ctrl or Option."; return; }
            parts.Add(name);
            _hotkeyValue = string.Join("+", parts);
            _hotkey.Text = _hotkeyValue;
            _status.Text = "";
        }

        // ---- small UI helpers ----
        static TextBlock Heading(string t) => new()
        {
            Text = t, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Accent,
            Margin = new Thickness(0, 14, 0, 6),
        };

        static void Style(TextBox t)
        {
            t.Background = Field; t.Foreground = Fg; t.BorderBrush = Border_; t.CaretBrush = Fg;
            t.Padding = new Thickness(6, 4, 6, 4);
        }

        TextBox Num(string key, int value)
        {
            var t = new TextBox { Text = value.ToString(), Width = 80, HorizontalContentAlignment = HorizontalAlignment.Right };
            Style(t);
            _nums[key] = t;
            return t;
        }

        static Control Row(string label, Control ctl)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Fg });
            Grid.SetColumn(ctl, 1);
            if (ctl is TextBox tb && tb.IsReadOnly) ctl.Width = 150;
            g.Children.Add(ctl);
            return g;
        }

        static Control Check(CheckBox c)
        {
            c.Foreground = Fg; c.Margin = new Thickness(0, 4, 0, 4);
            return c;
        }
    }
}

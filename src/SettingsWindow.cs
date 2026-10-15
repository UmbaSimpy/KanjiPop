using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static KanjiPop.Theme;

namespace KanjiPop
{
    /// <summary>Settings page (opened from the tray). Works on a copy of the config; Save applies it.</summary>
    public sealed class SettingsWindow : Window
    {
        readonly Config _cfg;
        readonly Action<Config, bool> _onSave;
        readonly TextBox _hotkey = new();
        readonly CheckBox _double = new(), _romaji = new(), _kana = new(), _autostart = new();
        readonly Dictionary<string, TextBox> _nums = new();
        readonly TextBlock _status = new();
        string _hotkeyValue;

        public SettingsWindow(Config current, bool autostart, Action<Config, bool> onSave)
        {
            _cfg = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(current)) ?? new Config();
            _onSave = onSave;
            _hotkeyValue = _cfg.Hotkey;

            Title = "KanjiPop settings";
            Width = 460; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Solid; Foreground = Fg; FontFamily = Font; FontSize = 14;
            DarkTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            Content = root;

            root.Children.Add(Heading("Hotkey"));
            _hotkey.IsReadOnly = true;
            _hotkey.Cursor = Cursors.Hand;
            Style(_hotkey);
            _hotkey.Text = _hotkeyValue;
            _hotkey.ToolTip = "Click, then press the key combination you want";
            _hotkey.GotKeyboardFocus += (_, _) => _status.Text = "Press the new key combination…";
            _hotkey.LostKeyboardFocus += (_, _) => _status.Text = "";
            _hotkey.PreviewKeyDown += OnHotkeyKey;
            root.Children.Add(Row("Shortcut (click, then press keys)", _hotkey));
            _double.Content = "Press it twice (double-tap)";
            _double.IsChecked = _cfg.DoubleTap;
            root.Children.Add(Check(_double));
            root.Children.Add(Row("Double-tap window (ms)", Num("DoubleTapMs", _cfg.DoubleTapMs)));
            _status.Foreground = Accent; _status.FontSize = 12; _status.Margin = new Thickness(0, 2, 0, 0);
            root.Children.Add(_status);

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
            _autostart.Content = "Start with Windows"; _autostart.IsChecked = autostart; root.Children.Add(Check(_autostart));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var defaults = Btn("Defaults");
            defaults.Click += (_, _) => LoadInto(new Config(), _autostart.IsChecked == true);
            var cancel = Btn("Cancel"); cancel.Click += (_, _) => Close();
            var save = Btn("Save"); save.Background = Frozen("#2E78C8"); save.Foreground = Brushes.White;
            save.Click += (_, _) => Save();
            buttons.Children.Add(defaults); buttons.Children.Add(cancel); buttons.Children.Add(save);
            root.Children.Add(buttons);
        }

        void LoadInto(Config c, bool autostart)
        {
            _hotkeyValue = c.Hotkey; _hotkey.Text = c.Hotkey;
            _double.IsChecked = c.DoubleTap; _romaji.IsChecked = c.ShowRomaji; _kana.IsChecked = c.ShowKanaWords;
            _autostart.IsChecked = autostart;
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
            if (!int.TryParse(_nums[key].Text.Trim(), out var v)) v = fallback;
            return Math.Max(min, Math.Min(max, v));
        }

        void Save()
        {
            try { Hotkey.Parse(_hotkeyValue); }
            catch { _status.Text = "That hotkey is not valid."; return; }
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
            _onSave(_cfg, _autostart.IsChecked == true);
            Close();
        }

        void OnHotkeyKey(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin or Key.Tab or Key.Escape) return;
            var vk = KeyInterop.VirtualKeyFromKey(key);
            var parts = new List<string>();
            var m = Keyboard.Modifiers;
            if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(((System.Windows.Forms.Keys)vk).ToString());
            _hotkeyValue = string.Join("+", parts);
            _hotkey.Text = _hotkeyValue;
            _status.Text = "";
        }

        // ---- small UI helpers ----
        static TextBlock Heading(string t) => new()
        {
            Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Accent,
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

        static UIElement Row(string label, FrameworkElement ctl)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Fg });
            Grid.SetColumn(ctl, 1);
            if (ctl is TextBox tb && tb.IsReadOnly) ctl.Width = 150;
            g.Children.Add(ctl);
            return g;
        }

        static UIElement Check(CheckBox c)
        {
            c.Foreground = Fg; c.Margin = new Thickness(0, 4, 0, 4);
            return c;
        }

        static Button Btn(string text)
        {
            var b = new Button
            {
                Content = text, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0),
                Background = Field, Foreground = Fg, BorderBrush = Border_, Cursor = Cursors.Hand,
            };
            return b;
        }
    }
}

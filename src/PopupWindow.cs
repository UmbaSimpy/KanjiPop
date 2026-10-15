using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace KanjiPop
{
    /// <summary>Borderless, non-activating popup that shows an Analysis near the cursor.</summary>
    public sealed class PopupWindow : Window
    {
        static readonly Brush Bg = Frozen("#F2202127");
        static readonly Brush Border_ = Frozen("#3A3C46");
        static readonly Brush Fg = Frozen("#ECECF0");
        static readonly Brush Dim = Frozen("#9A9CA8");
        static readonly Brush Accent = Frozen("#7CC4FF");
        static readonly Brush PosBrush = Frozen("#E8B96A");
        static readonly FontFamily Font = new("Yu Gothic UI, Meiryo UI, Segoe UI");

        readonly Config _cfg;
        readonly Border _card;
        readonly ScrollViewer _scroll;
        readonly DispatcherTimer _autoClose = new();
        Native.RECT _rect;

        static Brush Frozen(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            b.Freeze();
            return b;
        }

        public PopupWindow(Config cfg)
        {
            _cfg = cfg;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = Font;
            FontSize = cfg.FontSize;
            UseLayoutRounding = true;

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 560,
            };
            _scroll.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = (Style)System.Windows.Markup.XamlReader.Parse(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='ScrollBar'><Setter Property='Width' Value='7'/><Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='ScrollBar'><Border Background='Transparent'><Track Name='PART_Track' IsDirectionReversed='True'>" +
                "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#5A5D6B' CornerRadius='3' Margin='1,0'/>" +
                "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Border></ControlTemplate></Setter.Value></Setter></Style>");
            _card = new Border
            {
                Margin = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                Background = Bg,
                BorderBrush = Border_,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 6, 12),
                MaxWidth = 500,
                MinWidth = 220,
                Child = _scroll,
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.45 },
            };
            Content = _card;
            _autoClose.Tick += (_, _) => HidePopup();
            SourceInitialized += (_, _) =>
                Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
        }

        public bool IsShown => IsVisible && Opacity > 0;

        public void HidePopup()
        {
            _autoClose.Stop();
            Opacity = 0;
            Hide();
        }

        public bool ContainsPx(int x, int y) =>
            IsShown && x >= _rect.Left && x <= _rect.Right && y >= _rect.Top && y <= _rect.Bottom;

        public void Present(Analysis a, int cursorX, int cursorY)
        {
            _scroll.Content = Build(a);
            _scroll.ScrollToTop();
            Opacity = 0;
            if (!IsVisible) Show();
            UpdateLayout();
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.GetWindowRect(hwnd, out var r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursorX, cursorY)).WorkingArea;
            int margin = 14; // shadow margin of the card
            int x = cursorX + 8 - margin, y = cursorY + 20 - margin;
            if (x + w > wa.Right) x = wa.Right - w;
            if (x < wa.Left) x = wa.Left;
            if (y + h > wa.Bottom) y = cursorY - h + margin - 14;
            if (y < wa.Top) y = wa.Top;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            Native.GetWindowRect(hwnd, out _rect);
            _rect.Left += margin; _rect.Top += margin; _rect.Right -= margin; _rect.Bottom -= margin;
            Opacity = 1;
            if (_cfg.AutoCloseSeconds > 0)
            {
                _autoClose.Interval = TimeSpan.FromSeconds(_cfg.AutoCloseSeconds);
                _autoClose.Stop(); _autoClose.Start();
            }
        }

        // ---------------------------------------------------------------- content

        static TextBlock T(string text, double size, Brush fg, FontWeight? weight = null, bool wrap = true)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = fg,
                FontWeight = weight ?? FontWeights.Normal,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            };
        }

        UIElement Build(Analysis a)
        {
            var root = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            var withEntries = a.Tokens.Where(t => t.Entries.Count > 0).ToList();
            bool detail = withEntries.Count == 1 && a.Tokens.Count(t => t.HasKanji || t.Entries.Count > 0) == 1;

            if (a.Tokens.Count == 0)
            {
                root.Children.Add(T("No dictionary match.", _cfg.FontSize, Dim));
                return root;
            }

            if (detail) BuildDetail(root, withEntries[0]);
            else BuildSentence(root, a);

            if (a.Truncated)
                root.Children.Add(Pad(T($"(text cut to {_cfg.MaxChars} characters)", 11, Dim), 6));

            if (a.Kanji.Count > 0)
            {
                root.Children.Add(Divider());
                foreach (var k in a.Kanji) root.Children.Add(KanjiRow(k));
            }
            return root;
        }

        static FrameworkElement Pad(FrameworkElement e, double top) { e.Margin = new Thickness(0, top, 0, 0); return e; }

        static UIElement Divider() => new Border
        {
            Height = 1, Background = Border_, Margin = new Thickness(0, 10, 8, 8),
        };

        void BuildDetail(StackPanel root, Token tok)
        {
            int shown = 0;
            foreach (var e in tok.Entries)
            {
                if (shown >= _cfg.MaxEntries) break;
                if (shown > 0) root.Children.Add(Divider());
                shown++;

                var head = new WrapPanel { Orientation = Orientation.Horizontal };
                head.Children.Add(Pad(T(tok.Surface == e.Headword || e.Direct ? e.Headword : tok.Surface, 26, Fg, FontWeights.SemiBold, false), 0));
                var rd = e.Direct ? string.Join(" / ", e.Readings.Select(Kana.ToHiragana).Distinct()) : e.SurfaceReading;
                var reading = T(rd, 19, Accent, null, false);
                reading.Margin = new Thickness(12, 7, 0, 0);
                head.Children.Add(reading);
                if (e.Common)
                {
                    var c = T("common", 10, PosBrush, null, false);
                    c.Margin = new Thickness(10, 12, 0, 0);
                    head.Children.Add(c);
                }
                root.Children.Add(head);

                if (_cfg.ShowRomaji)
                    root.Children.Add(T(Kana.ToRomaji(e.Direct ? e.Readings[0] : e.SurfaceReading), 12, Dim));
                if (!e.Direct)
                    root.Children.Add(Pad(T($"← {e.DictForm}  ({e.Reasons})", 12, Dim), 2));
                else if (e.Written.Count > 0)
                    root.Children.Add(Pad(T("written: " + string.Join("  ", e.Written), 12, Dim), 2));

                AddSenses(root, e, _cfg.MaxSensesPerEntry);
            }
        }

        void BuildSentence(StackPanel root, Analysis a)
        {
            // ruby line: reading above each word that contains kanji
            var ruby = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var t in a.Tokens)
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 2, 4) };
                var rt = T(t.HasKanji ? t.Reading : " ", 11, Accent, null, false);
                rt.HorizontalAlignment = HorizontalAlignment.Center;
                col.Children.Add(rt);
                col.Children.Add(T(t.Surface, 20, Fg, null, false));
                ruby.Children.Add(col);
            }
            root.Children.Add(ruby);
            root.Children.Add(Divider());

            int n = 0;
            foreach (var t in a.Tokens)
            {
                if (!t.HasKanji && !_cfg.ShowKanaWords) continue;
                if (++n > 30) break;
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
                var line = new WrapPanel();
                line.Children.Add(T(t.Surface, 16, Fg, FontWeights.SemiBold, false));
                var rd = T(t.Entries.Count > 0 ? "  " + t.Reading : "", 14, Accent, null, false);
                line.Children.Add(rd);
                if (_cfg.ShowRomaji && t.Entries.Count > 0)
                    line.Children.Add(T("  " + Kana.ToRomaji(t.Reading), 11, Dim, null, false));
                if (t.Entries.Count > 0 && !t.Entries[0].Direct)
                    line.Children.Add(T($"   ← {t.Entries[0].DictForm}", 11, Dim, null, false));
                row.Children.Add(line);
                if (t.Entries.Count > 0)
                {
                    var gl = string.Join("; ", t.Entries[0].Senses.Take(2).Select(s => string.Join(", ", s.Gloss.Take(3))));
                    row.Children.Add(T(gl, 13, Fg));
                }
                root.Children.Add(row);
            }
        }

        void AddSenses(StackPanel root, EntryView e, int max)
        {
            int i = 0;
            string lastPos = "";
            foreach (var s in e.Senses)
            {
                if (++i > max) break;
                var pos = PosLabel(s.Pos);
                if (pos != lastPos && pos.Length > 0)
                {
                    root.Children.Add(Pad(T(pos, 11, PosBrush), 5));
                    lastPos = pos;
                }
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
                tb.Inlines.Add(new System.Windows.Documents.Run($"{i}. ") { Foreground = Dim });
                tb.Inlines.Add(new System.Windows.Documents.Run(string.Join("; ", s.Gloss)) { Foreground = Fg });
                if (s.Misc.Count > 0)
                    tb.Inlines.Add(new System.Windows.Documents.Run("  (" + string.Join(", ", s.Misc.Select(m => m.ToLowerInvariant())) + ")")
                    { Foreground = Dim, FontSize = 11 });
                root.Children.Add(tb);
            }
            if (e.Senses.Count > max)
                root.Children.Add(Pad(T($"+{e.Senses.Count - max} more meanings", 11, Dim), 2));
        }

        static string PosLabel(List<string> pos)
        {
            var o = new List<string>();
            foreach (var p in pos)
            {
                string l = p switch
                {
                    "noun" => "noun",
                    "adjective" => "i-adjective",
                    "adjectival nouns or quasi-adjectives" => "na-adjective",
                    "transitive verb" => "transitive",
                    "intransitive verb" => "intransitive",
                    "noun or participle which takes the aux. verb suru" => "suru-verb",
                    _ when p.StartsWith("Godan verb") => "godan verb",
                    _ when p.StartsWith("Ichidan verb") => "ichidan verb",
                    _ when p.StartsWith("Suru verb") => "suru verb",
                    _ when p.StartsWith("Kuru verb") => "kuru verb",
                    _ => p.ToLowerInvariant(),
                };
                if (!o.Contains(l)) o.Add(l);
            }
            return string.Join(", ", o.Take(4));
        }

        UIElement KanjiRow(KanjiInfo k)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var big = T(k.Literal, 28, Fg, null, false);
            big.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(big);

            var col = new StackPanel();
            Grid.SetColumn(col, 1);
            var rd = new TextBlock { TextWrapping = TextWrapping.Wrap };
            if (k.On.Count > 0) rd.Inlines.Add(new System.Windows.Documents.Run(string.Join("、", k.On)) { Foreground = Accent });
            if (k.On.Count > 0 && k.Kun.Count > 0) rd.Inlines.Add(new System.Windows.Documents.Run("   "));
            if (k.Kun.Count > 0) rd.Inlines.Add(new System.Windows.Documents.Run(string.Join("、", k.Kun)) { Foreground = Fg });
            col.Children.Add(rd);
            col.Children.Add(T(string.Join(", ", k.Meanings.Take(5)), 12, Fg));
            var meta = new List<string>();
            if (k.Jlpt is int j) meta.Add("JLPT " + (j switch { 4 => "N5", 3 => "N4", 2 => "N3/N2", _ => "N1" }));
            if (k.Grade is int gr) meta.Add(gr <= 6 ? $"grade {gr}" : gr == 8 ? "jōyō (secondary)" : "jinmeiyō");
            if (k.Strokes is int s) meta.Add($"{s} strokes");
            if (meta.Count > 0) col.Children.Add(T(string.Join(" · ", meta), 11, Dim));
            g.Children.Add(col);
            return g;
        }
    }
}

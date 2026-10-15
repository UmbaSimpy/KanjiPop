using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using static KanjiPop.Theme;

namespace KanjiPop
{
    /// <summary>Borderless popup that shows an Analysis near the cursor. Draggable; remembers where you put it.</summary>
    public sealed class PopupWindow : Window
    {
        const int Margin_ = 14; // transparent margin around the card (room for the shadow)

        readonly Config _cfg;
        readonly AppState _state;
        readonly Func<bool> _isFav;
        readonly Func<bool> _toggleFav;
        readonly Border _card;
        readonly ScrollViewer _scroll;
        readonly TextBlock _star, _reset;
        readonly DispatcherTimer _autoClose = new();
        readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
        bool _placing;
        double _cursorX, _cursorY; // points

        public PopupWindow(Config cfg, AppState state, Func<bool> isFav, Func<bool> toggleFav)
        {
            _cfg = cfg; _state = state; _isFav = isFav; _toggleFav = toggleFav;
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = Font;
            FontSize = cfg.FontSize;

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 540,
            };

            _star = IconButton("☆", "Add to favorites");
            _star.PointerReleased += (_, _) => { _star.Text = _toggleFav() ? "★" : "☆"; _star.Foreground = _star.Text == "★" ? Gold : Dim; };
            _reset = IconButton("⟲", "Reset position (show next to the text again)");
            _reset.PointerReleased += (_, _) => ResetPosition();

            var grip = new TextBlock
            {
                Text = "⠿ drag to move", FontSize = 10, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7, FontFamily = Symbols,
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(_reset);
            buttons.Children.Add(_star);
            var bar = new Grid { Margin = new Thickness(0, 0, 8, 4) };
            bar.Children.Add(grip);
            bar.Children.Add(buttons);

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(_scroll);

            _card = new Border
            {
                Margin = new Thickness(Margin_),
                CornerRadius = new CornerRadius(10),
                Background = Bg,
                BorderBrush = Border_,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 6, 12),
                MaxWidth = 500,
                MinWidth = 240,
                Child = dock,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 3, Color = Color.FromArgb(115, 0, 0, 0) }),
            };
            _card.PointerPressed += OnCardPressed;
            Content = _card;

            _autoClose.Tick += (_, _) => HidePopup();
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _state.Save(); };
            PositionChanged += OnPositionChanged;
            KeyDown += (_, e) => { if (e.Key == Key.Escape) HidePopup(); };
        }

        static TextBlock IconButton(string glyph, string tip)
        {
            var t = new TextBlock
            {
                Text = glyph, FontSize = 17, Foreground = Dim, FontFamily = Symbols, Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(8, -2, 0, 0), Background = Brushes.Transparent,
            };
            ToolTip.SetTip(t, tip);
            t.PointerEntered += (_, _) => t.Foreground = Accent;
            t.PointerExited += (_, _) => t.Foreground = t.Text == "★" ? Gold : Dim;
            t.PointerPressed += (_, e) => e.Handled = true; // don't start a drag from a button
            return t;
        }

        public bool IsShown => IsVisible;

        public void HidePopup()
        {
            _autoClose.Stop();
            if (IsVisible) Hide();
        }

        double Scaling => Screens.Primary?.Scaling ?? 1.0;

        /// <summary>Is the point (in points, as reported by the event tap) inside the card?</summary>
        public bool ContainsPoint(double x, double y)
        {
            if (!IsVisible) return false;
            double sc = Scaling;
            double left = Position.X / sc + Margin_, top = Position.Y / sc + Margin_;
            double w = Math.Max(0, Bounds.Width - 2 * Margin_), h = Math.Max(0, Bounds.Height - 2 * Margin_);
            return x >= left && x <= left + w && y >= top && y <= top + h;
        }

        void OnCardPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_card).Properties.IsLeftButtonPressed) return;
            try { BeginMoveDrag(e); } catch { }
        }

        void OnPositionChanged(object? sender, PixelPointEventArgs e)
        {
            if (_placing || !IsVisible) return;
            _state.HasPos = true; _state.X = e.Point.X; _state.Y = e.Point.Y;
            _reset.IsVisible = true;
            _saveTimer.Stop(); _saveTimer.Start();
        }

        void ResetPosition()
        {
            _state.HasPos = false;
            _state.Save();
            _reset.IsVisible = false;
            Place(false);
        }

        /// <summary>Move to the remembered spot (if useSaved and one exists) or next to the cursor.</summary>
        void Place(bool useSaved)
        {
            double sc = Scaling;
            _card.Measure(new Size(2000, 2000));
            int w = (int)Math.Ceiling(_card.DesiredSize.Width * sc), h = (int)Math.Ceiling(_card.DesiredSize.Height * sc);
            int x, y;
            if (useSaved && _state.HasPos)
            {
                x = _state.X; y = _state.Y;
                var wa = (Screens.ScreenFromPoint(new PixelPoint(x + w / 2, y + h / 2)) ?? Screens.Primary)?.WorkingArea
                         ?? new PixelRect(0, 0, 1920, 1080);
                x = Math.Max(wa.X, Math.Min(x, wa.Right - w));
                y = Math.Max(wa.Y, Math.Min(y, wa.Bottom - h));
            }
            else
            {
                int cx = (int)(_cursorX * sc), cy = (int)(_cursorY * sc);
                int m = (int)(Margin_ * sc);
                var wa = (Screens.ScreenFromPoint(new PixelPoint(cx, cy)) ?? Screens.Primary)?.WorkingArea
                         ?? new PixelRect(0, 0, 1920, 1080);
                x = cx + (int)(8 * sc) - m; y = cy + (int)(20 * sc) - m;
                if (x + w > wa.Right) x = wa.Right - w;
                if (x < wa.X) x = wa.X;
                if (y + h > wa.Bottom) y = cy - h + m - (int)(14 * sc);
                if (y < wa.Y) y = wa.Y;
            }
            _placing = true;
            Position = new PixelPoint(x, y);
            _placing = false;
        }

        /// <param name="cursorX">Cursor position in points (macOS / CG coordinates).</param>
        public void Present(Analysis a, double cursorX, double cursorY)
        {
            _cursorX = cursorX; _cursorY = cursorY;
            FontSize = _cfg.FontSize;
            _scroll.Content = Build(a);
            _scroll.Offset = default;
            bool fav = _isFav();
            _star.Text = fav ? "★" : "☆";
            _star.Foreground = fav ? Gold : Dim;
            _reset.IsVisible = _state.HasPos;
            Place(true);
            if (!IsVisible) Show();
            Place(true); // size is exact now
            if (_cfg.AutoCloseSeconds > 0)
            {
                _autoClose.Interval = TimeSpan.FromSeconds(_cfg.AutoCloseSeconds);
                _autoClose.Stop(); _autoClose.Start();
            }
        }

        // ---------------------------------------------------------------- content

        static TextBlock T(string text, double size, IBrush fg, FontWeight? weight = null, bool wrap = true)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = fg,
                FontWeight = weight ?? FontWeight.Normal,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            };
        }

        Control Build(Analysis a)
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

        static Control Pad(Control e, double top) { e.Margin = new Thickness(0, top, 0, 0); return e; }

        static Control Divider() => new Border
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
                head.Children.Add(T(tok.Surface == e.Headword || e.Direct ? e.Headword : tok.Surface, 26, Fg, FontWeight.SemiBold, false));
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
                line.Children.Add(T(t.Surface, 16, Fg, FontWeight.SemiBold, false));
                line.Children.Add(T(t.Entries.Count > 0 ? "  " + t.Reading : "", 14, Accent, null, false));
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
                tb.Inlines!.Add(new Run($"{i}. ") { Foreground = Dim });
                tb.Inlines.Add(new Run(string.Join("; ", s.Gloss)) { Foreground = Fg });
                if (s.Misc.Count > 0)
                    tb.Inlines.Add(new Run("  (" + string.Join(", ", s.Misc.Select(m => m.ToLowerInvariant())) + ")")
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

        Control KanjiRow(KanjiInfo k)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition(40, GridUnitType.Pixel));
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var big = T(k.Literal, 28, Fg, null, false);
            big.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(big);

            var col = new StackPanel();
            Grid.SetColumn(col, 1);
            var rd = new TextBlock { TextWrapping = TextWrapping.Wrap };
            if (k.On.Count > 0) rd.Inlines!.Add(new Run(string.Join("、", k.On)) { Foreground = Accent });
            if (k.On.Count > 0 && k.Kun.Count > 0) rd.Inlines!.Add(new Run("   "));
            if (k.Kun.Count > 0) rd.Inlines!.Add(new Run(string.Join("、", k.Kun)) { Foreground = Fg });
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

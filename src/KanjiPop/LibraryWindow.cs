using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static KanjiPop.Theme;

namespace KanjiPop
{
    /// <summary>Dedicated page for lookup history and favorites.</summary>
    public sealed class LibraryWindow : Window
    {
        readonly ItemStore _history, _favorites;
        readonly Action<string> _lookup;
        readonly TextBox _search = new();
        readonly StackPanel _list = new();
        readonly TextBlock _count = new();
        Button _tabHistory = null!, _tabFavs = null!;
        bool _showFavs;

        public LibraryWindow(ItemStore history, ItemStore favorites, Action<string> lookup)
        {
            _history = history; _favorites = favorites; _lookup = lookup;
            Title = "KanjiPop – History & Favorites";
            Width = 560; Height = 620; MinWidth = 420; MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Solid; Foreground = Fg; FontFamily = Font; FontSize = 14;
            DarkTitleBar(this);

            var grid = new Grid { Margin = new Thickness(16, 14, 16, 12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = grid;

            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            _tabHistory = TabButton("History", () => { _showFavs = false; Refresh(); });
            _tabFavs = TabButton("★ Favorites", () => { _showFavs = true; Refresh(); });
            tabs.Children.Add(_tabHistory); tabs.Children.Add(_tabFavs);
            grid.Children.Add(tabs);

            _search.Margin = new Thickness(0, 10, 0, 8);
            _search.Padding = new Thickness(8, 6, 8, 6);
            _search.Background = Field; _search.Foreground = Fg; _search.BorderBrush = Border_; _search.CaretBrush = Fg;
            _search.ToolTip = "Filter by word, reading or meaning";
            _search.TextChanged += (_, _) => Refresh();
            Grid.SetRow(_search, 1);
            grid.Children.Add(_search);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
            Grid.SetRow(scroll, 2);
            grid.Children.Add(scroll);

            var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var clear = new Button
            {
                Content = "Clear all", Padding = new Thickness(12, 4, 12, 4), Background = Field, Foreground = Fg,
                BorderBrush = Border_, Cursor = Cursors.Hand,
            };
            clear.Click += (_, _) => ClearAll();
            DockPanel.SetDock(clear, Dock.Right);
            foot.Children.Add(clear);
            _count.Foreground = Dim; _count.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(_count);
            Grid.SetRow(foot, 3);
            grid.Children.Add(foot);

            Action changed = () => Dispatcher.BeginInvoke(new Action(Refresh));
            _history.Changed += changed; _favorites.Changed += changed;
            Closed += (_, _) => { _history.Changed -= changed; _favorites.Changed -= changed; };
            Refresh();
        }

        public void ShowTab(bool favorites)
        {
            _showFavs = favorites;
            Refresh();
        }

        Button TabButton(string text, Action click)
        {
            var b = new Button
            {
                Content = text, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0),
                BorderBrush = Border_, Cursor = Cursors.Hand, Foreground = Fg,
            };
            b.Click += (_, _) => click();
            return b;
        }

        void ClearAll()
        {
            var store = _showFavs ? _favorites : _history;
            if (store.Items.Count == 0) return;
            var what = _showFavs ? "all favorites" : "the whole history";
            if (MessageBox.Show(this, $"Remove {what}?", "KanjiPop", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                store.Clear();
        }

        void Refresh()
        {
            var store = _showFavs ? _favorites : _history;
            _tabHistory.Background = _showFavs ? Field : Frozen("#2E78C8");
            _tabFavs.Background = _showFavs ? Frozen("#2E78C8") : Field;

            var q = _search.Text.Trim();
            var items = store.Items.Where(i => q.Length == 0 ||
                i.Text.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Reading.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Meaning.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

            _list.Children.Clear();
            foreach (var it in items) _list.Children.Add(Row(it, store));
            if (items.Count == 0)
                _list.Children.Add(new TextBlock
                {
                    Text = q.Length > 0 ? "No matches." : _showFavs ? "No favorites yet. Click the ☆ in the popup to add one." : "Nothing here yet.",
                    Foreground = Dim, Margin = new Thickness(4, 12, 0, 0), TextWrapping = TextWrapping.Wrap,
                });
            _count.Text = $"{items.Count} of {store.Items.Count}";
        }

        UIElement Row(HistoryItem it, ItemStore store)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Click to look up again" };
            var line = new WrapPanel();
            line.Children.Add(new TextBlock { Text = it.Text.Replace('\n', ' '), FontSize = 18, Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340 });
            if (it.Reading.Length > 0)
                line.Children.Add(new TextBlock { Text = "  " + it.Reading, FontSize = 14, Foreground = Accent, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) });
            left.Children.Add(line);
            if (it.Meaning.Length > 0)
                left.Children.Add(new TextBlock { Text = it.Meaning, Foreground = Dim, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            left.MouseLeftButtonUp += (_, _) => _lookup(it.Text);
            g.Children.Add(left);

            bool fav = _favorites.Contains(it.Text);
            var star = Glyph(fav ? "★" : "☆", fav ? Gold : Dim, fav ? "Remove from favorites" : "Add to favorites");
            star.MouseLeftButtonUp += (_, _) =>
            {
                if (_favorites.Contains(it.Text)) _favorites.Remove(it.Text);
                else _favorites.Add(it, 0);
            };
            Grid.SetColumn(star, 1);
            g.Children.Add(star);

            var del = Glyph("✕", Dim, "Remove from this list");
            del.MouseLeftButtonUp += (_, _) => store.Remove(it.Text);
            Grid.SetColumn(del, 2);
            g.Children.Add(del);

            var b = new Border
            {
                Child = g, Padding = new Thickness(10, 6, 8, 6), CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 2), Background = Brushes.Transparent,
            };
            b.MouseEnter += (_, _) => b.Background = Hover;
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            return b;
        }

        static TextBlock Glyph(string g, Brush color, string tip) => new()
        {
            Text = g, FontSize = 18, Foreground = color, FontFamily = Symbols, Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip,
            Background = Brushes.Transparent,
        };
    }
}

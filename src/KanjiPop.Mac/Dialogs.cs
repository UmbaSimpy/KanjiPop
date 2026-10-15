using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static KanjiPop.Theme;

namespace KanjiPop
{
    /// <summary>Tiny modal message boxes (Avalonia has none built in).</summary>
    static class Dialogs
    {
        public static Button Btn(string text, bool primary = false) => new()
        {
            Content = text, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0),
            Background = primary ? Blue : Field, Foreground = primary ? Brushes.White : Fg, BorderBrush = Border_,
        };

        /// <summary>Shows <paramref name="text"/> with the given buttons; returns the index of the clicked one (-1 if closed).</summary>
        public static async Task<int> Ask(Window? owner, string text, params string[] buttons)
        {
            int result = -1;
            var win = new Window
            {
                Title = "KanjiPop", Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
                Background = Solid, Foreground = Fg, FontFamily = Font, FontSize = 14,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
            };
            var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16), Spacing = 16 };
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Fg });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                var b = Btn(buttons[i], i == 0);
                b.Click += (_, _) => { result = idx; win.Close(); };
                row.Children.Add(b);
            }
            panel.Children.Add(row);
            win.Content = panel;
            if (owner != null) await win.ShowDialog(owner);
            else
            {
                var tcs = new TaskCompletionSource();
                win.Closed += (_, _) => tcs.TrySetResult();
                win.Show();
                win.Activate();
                await tcs.Task;
            }
            return result;
        }
    }
}

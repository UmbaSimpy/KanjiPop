using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace KanjiPop
{
    public sealed class App : Application
    {
        Host? _host;

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _host = new Host(Program.DbPath, desktop);
                // start once the run loop is going (the event tap attaches to it)
                Dispatcher.UIThread.Post(() =>
                {
                    _host.Start();
                    var a = Program.Args;
                    if (a.Length >= 2 && a[0] == "--show") _host.ShowText(a[1]);
                    if (a.Length >= 2 && a[0] == "--open") _host.OpenWindow(a[1]);
                });
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}

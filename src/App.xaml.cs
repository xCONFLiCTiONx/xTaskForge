using System.Linq;
using System.Windows;

namespace xTaskForge;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a =>
            a.Equals("--native-messaging", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)))
        {
            NativeMessagingHost.RunConsoleLoop();
            Shutdown();
            return;
        }

        if (SilentElevationHelper.IsRunningAsAdmin())
        {
            System.Windows.MessageBox.Show(
                "xTaskForge is not allowed to run as Administrator.\n\nPlease launch xTaskForge normally as your standard user account. Individual scripts and tray items can still be elevated using 'Run as Administrator' or 'Silent Elevate (Task Scheduler)'.",
                "xTaskForge — Elevation Not Allowed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        bool forceStartInTray = e.Args.Any(a =>
            a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-tray", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/tray", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        var mainWindow = new MainWindow(forceStartInTray);
        MainWindow = mainWindow;

        if (!mainWindow.ShouldStartHidden)
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            PipProvider.RestoreAllWindowsAndShutdown();
        }
        catch { }
        base.OnExit(e);
    }
}

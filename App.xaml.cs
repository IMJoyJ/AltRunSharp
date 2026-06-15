using System;
using System.IO;
using System.Windows;

namespace AltRunSharp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Handle privileged admin operations (called when re-launched as admin)
        if (AdminHelper.HandleAdminArgs(e.Args))
        {
            Shutdown(0);
            return;
        }

        // Handle --add-launch <path> (called from context menu)
        if (e.Args.Length >= 2 && e.Args[0] == "--add-launch")
        {
            // Will be handled after MainWindow loads; store for later
            PendingAddLaunchPath = e.Args[1];
        }

        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            LogException(ev.ExceptionObject as Exception);
            Environment.Exit(1);
        };

        this.DispatcherUnhandledException += (s, ev) =>
        {
            LogException(ev.Exception);
            ev.Handled = true;
            Environment.Exit(1);
        };

        base.OnStartup(e);
    }

    public static string? PendingAddLaunchPath { get; private set; }

    private static void LogException(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "logs");
            Directory.CreateDirectory(dir);
            string logPath = Path.Combine(dir, "app_crash.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now}] {ex.Message}\r\nStack: {ex.StackTrace}\r\n\r\n");
        }
        catch { }
    }
}

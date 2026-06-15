using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;

namespace AltRunSharp;

public partial class App : Application
{
    private const string MutexName = "Global\\AltRunSharp_SingleInstance";
    private const string PipeName  = "AltRunSharp_IPC";

    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Handle privileged admin operations (called when re-launched as admin)
        if (AdminHelper.HandleAdminArgs(e.Args))
        {
            Shutdown(0);
            return;
        }

        // ── Single-instance check ────────────────────────────────────────────
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // Forward args to the running instance via named pipe, then exit.
            if (e.Args.Length >= 2 && e.Args[0] == "--add-launch")
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName,
                        PipeDirection.Out, PipeOptions.None);
                    client.Connect(timeout: 2000); // ms
                    string msg = string.Join("\x01", e.Args); // \x01-delimited
                    byte[] buf = Encoding.UTF8.GetBytes(msg);
                    client.Write(buf, 0, buf.Length);
                }
                catch { /* pipe not ready — ignore */ }
            }

            Shutdown(0);
            return;
        }

        // First instance: start IPC listener
        PipeName_StartListener(e.Args);

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

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ── Named-pipe IPC server (background thread) ────────────────────────────

    /// <summary>
    /// One-time args that arrived via the original command line (first launch).
    /// </summary>
    public static string[]? StartupArgs { get; private set; }

    private void PipeName_StartListener(string[] startupArgs)
    {
        StartupArgs = startupArgs;

        Thread t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName,
                        PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.None);
                    server.WaitForConnection();

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string raw = reader.ReadToEnd();
                    string[] parts = raw.Split('\x01');

                    // Dispatch to UI thread
                    Current.Dispatcher.BeginInvoke(() =>
                    {
                        if (Current.MainWindow is MainWindow mw)
                            mw.HandleIpcArgs(parts);
                    });
                }
                catch { /* pipe broken — restart listener */ }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    // ── Crash logging ────────────────────────────────────────────────────────

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

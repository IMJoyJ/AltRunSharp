using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AltRunSharp;

public static class UpdateService
{
    private const string ApiUrl =
        "https://api.github.com/repos/IMJoyJ/AltRunSharp/releases/latest";

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "AltRunSharp-Updater" } }
    };

    public record UpdateInfo(string Version, string DownloadUrl, string HtmlUrl);

    public static string CurrentVersion
    {
        get
        {
            var v = typeof(UpdateService).Assembly.GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    // ── Variant detection ────────────────────────────────────────────────────

    /// <summary>Determine which release asset name matches this system.</summary>
    private static string GetAssetName()
    {
        string arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        bool hasFx = HasDesktopRuntime10();
        return hasFx
            ? $"AltRunSharp-{arch}.exe"
            : $"AltRunSharp-{arch}-standalone.exe";
    }

    /// <summary>Check if .NET 10 Windows Desktop Runtime is installed.</summary>
    private static bool HasDesktopRuntime10()
    {
        try
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string dir = Path.Combine(pf, "dotnet", "shared",
                "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(dir)) return false;
            foreach (string d in Directory.GetDirectories(dir))
                if (Path.GetFileName(d).StartsWith("10.")) return true;
        }
        catch { /* ignore */ }
        return false;
    }

    // ── Check for update ─────────────────────────────────────────────────────

    /// <summary>
    /// Query GitHub Releases API for the latest version.
    /// Returns null if already up-to-date or on any error.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string json = await Http.GetStringAsync(ApiUrl, cts.Token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString()!.TrimStart('v');
        if (new Version(tag) <= new Version(CurrentVersion))
            return null;

        string htmlUrl = root.GetProperty("html_url").GetString() ?? "";
        string assetName = GetAssetName();

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == assetName)
            {
                string url = asset.GetProperty("browser_download_url").GetString()!;
                return new UpdateInfo(tag, url, htmlUrl);
            }
        }
        return null; // matching asset not found
    }

    // ── Download & apply ─────────────────────────────────────────────────────

    /// <summary>
    /// Download the new exe, write an update.bat, launch it, then exit the app.
    /// The bat waits for the process to die, replaces the exe, and restarts.
    /// </summary>
    public static async Task ApplyAsync(UpdateInfo info, Action<int>? onProgress = null)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AltRunSharp_Update");
        Directory.CreateDirectory(tempDir);
        string newExe = Path.Combine(tempDir, "AltRunSharp_new.exe");

        // ── Download with progress ───────────────────────────────────────
        using var resp = await Http.GetAsync(info.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;

        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var dst = new FileStream(newExe, FileMode.Create))
        {
            byte[] buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) onProgress?.Invoke((int)(done * 100 / total));
            }
        }
        onProgress?.Invoke(100);

        // ── Write update bat (outside tempDir so rd can clean it) ─────────
        string currentExe = Environment.ProcessPath!;
        string batPath = Path.Combine(Path.GetTempPath(), "altrunsharp_update.bat");
        string bat = string.Join("\r\n",
            "@echo off",
            "timeout /t 2 /nobreak >nul",
            $"taskkill /f /pid {Environment.ProcessId} >nul 2>&1",
            "timeout /t 1 /nobreak >nul",
            $"del \"{currentExe}\" >nul 2>&1",
            $"move /y \"{newExe}\" \"{currentExe}\"",
            $"rd /s /q \"{tempDir}\" >nul 2>&1",
            $"start \"\" \"{currentExe}\"",
            "del \"%~f0\"");
        await File.WriteAllTextAsync(batPath, bat);

        // ── Launch bat & exit ────────────────────────────────────────────
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });

        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    // ── Cleanup ──────────────────────────────────────────────────────────────

    /// <summary>Remove leftover temp files from a previous update.</summary>
    public static void Cleanup()
    {
        try
        {
            string d = Path.Combine(Path.GetTempPath(), "AltRunSharp_Update");
            if (Directory.Exists(d)) Directory.Delete(d, true);
        }
        catch { /* ignore */ }
        try
        {
            string f = Path.Combine(Path.GetTempPath(), "altrunsharp_update.bat");
            if (File.Exists(f)) File.Delete(f);
        }
        catch { /* ignore */ }
    }
}

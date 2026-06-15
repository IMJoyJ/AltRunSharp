using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

namespace AltRunSharp
{
    public static class AdminHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "AltRunSharp";

        // ── Registry key for exe context menu (HKLM, requires admin) ────────
        private const string ContextMenuKeyPath = @"SOFTWARE\Classes\exefile\shell\AddToAltRun";

        public static bool IsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Re-launch this executable as administrator with the given argument.
        /// Returns true if UAC dialog was shown (process started), false on failure.
        /// </summary>
        public static bool RelaunchAsAdmin(string argument)
        {
            string? exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (exe == null) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = argument,
                    Verb = "runas",
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User declined UAC
                return false;
            }
        }

        // ── Startup (HKCU — no admin needed) ────────────────────────────────

        public static void EnableStartup()
        {
            string? exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (exe == null) return;
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.SetValue(AppName, $"\"{exe}\"");
        }

        public static void DisableStartup()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        public static bool IsStartupEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) != null;
        }

        // ── Context menu (HKLM — requires admin) ────────────────────────────

        public static void EnableContextMenu()
        {
            string? exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (exe == null) return;

            using var key = Registry.LocalMachine.CreateSubKey(ContextMenuKeyPath + @"\command");
            key.SetValue("", $"\"{exe}\" --add-launch \"%1\"");

            using var parentKey = Registry.LocalMachine.CreateSubKey(ContextMenuKeyPath);
            parentKey.SetValue("", "添加到 AltRun");
            parentKey.SetValue("Icon", $"\"{exe}\"");
        }

        public static void DisableContextMenu()
        {
            Registry.LocalMachine.DeleteSubKeyTree(ContextMenuKeyPath, throwOnMissingSubKey: false);
        }

        public static bool IsContextMenuEnabled()
        {
            using var key = Registry.LocalMachine.OpenSubKey(ContextMenuKeyPath);
            return key != null;
        }

        // ── Handle admin command-line arguments ──────────────────────────────

        /// <summary>
        /// Call at startup. If a privileged arg is present, execute it and exit.
        /// Returns true if the app should exit immediately.
        /// </summary>
        public static bool HandleAdminArgs(string[] args)
        {
            if (args.Length == 0) return false;

            switch (args[0])
            {
                case "--reg-context-menu":
                    EnableContextMenu();
                    return true;
                case "--unreg-context-menu":
                    DisableContextMenu();
                    return true;
                default:
                    return false;
            }
        }
    }
}

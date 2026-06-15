using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AltRunSharp
{
    /// <summary>
    /// Handles one-shot launching: programs, one-time scripts, and workflows.
    /// Service-mode scripts are routed to ServiceManager instead.
    /// </summary>
    public class RunnerService
    {
        private readonly string _dataDir;
        private static readonly string _appBaseDir = AppDomain.CurrentDomain.BaseDirectory;

        public RunnerService(string dataDir)
        {
            _dataDir = dataDir;
        }

        // ── Launch item (exe) ─────────────────────────────────────────────────

        public void RunLaunchItem(LaunchItem item)
        {
            string workDir = Path.GetDirectoryName(item.Path) ?? _appBaseDir;
            var psi = new ProcessStartInfo
            {
                FileName = item.Path,
                Arguments = item.Args ?? string.Empty,
                WorkingDirectory = workDir,
                UseShellExecute = true
            };
            try { Process.Start(psi); }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败：{ex.Message}", "AltRunSharp",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Script item ───────────────────────────────────────────────────────

        /// <summary>
        /// Run a script item with extra args from user input.
        /// For "service" LaunchMode: caller should route to ServiceManager instead.
        /// For "workflow": resolves steps from config and runs sequentially.
        /// </summary>
        public void RunScriptItem(ScriptItem item, string[] extraArgs, AppConfig? config = null)
        {
            if (item.ScriptType.Equals("workflow", StringComparison.OrdinalIgnoreCase))
            {
                RunWorkflow(item, extraArgs, config);
                return;
            }

            // js / cs / bat / exe — one-time only (service mode is handled by ServiceManager)
            bool isExe = item.ScriptType.Equals("exe", StringComparison.OrdinalIgnoreCase);
            bool isBat = item.ScriptType.Equals("bat", StringComparison.OrdinalIgnoreCase);

            string exe, baseArgs, workDir;
            if (item.ScriptType.Equals("js", StringComparison.OrdinalIgnoreCase))
            {
                string scriptPath = Path.Combine(_dataDir, "scripts", item.ScriptFileName);
                if (!File.Exists(scriptPath))
                {
                    MessageBox.Show($"脚本文件不存在：{scriptPath}", "AltRunSharp",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                exe = "node";
                baseArgs = BuildArgString($"\"{scriptPath}\"", extraArgs);
                workDir = _appBaseDir;
            }
            else if (item.ScriptType.Equals("cs", StringComparison.OrdinalIgnoreCase))
            {
                string scriptPath = Path.Combine(_dataDir, "scripts", item.ScriptFileName);
                if (!File.Exists(scriptPath))
                {
                    MessageBox.Show($"脚本文件不存在：{scriptPath}", "AltRunSharp",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                exe = "dotnet";
                baseArgs = BuildArgString($"run \"{scriptPath}\"", extraArgs);
                workDir = _appBaseDir;
            }
            else if (isBat)
            {
                string scriptPath = Path.Combine(_dataDir, "scripts", item.ScriptFileName);
                if (!File.Exists(scriptPath))
                {
                    MessageBox.Show($"批处理文件不存在：{scriptPath}", "AltRunSharp",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                exe = "cmd.exe";
                baseArgs = BuildArgString($"/c \"{scriptPath}\"", extraArgs);
                workDir = _appBaseDir;
            }
            else if (isExe)
            {
                if (!File.Exists(item.ScriptFileName))
                {
                    MessageBox.Show($"程序不存在：{item.ScriptFileName}", "AltRunSharp",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                exe = item.ScriptFileName;
                baseArgs = BuildArgString(string.Empty, extraArgs).Trim();
                workDir = Path.GetDirectoryName(item.ScriptFileName) ?? _appBaseDir;
            }
            else
            {
                MessageBox.Show($"不支持的脚本类型：{item.ScriptType}", "AltRunSharp",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (item.Silent)
                RunSilent(exe, baseArgs, workDir);
            else
                RunWithOutputWindow(exe, baseArgs, item.Name, workDir);
        }

        // ── Workflow ──────────────────────────────────────────────────────────

        private void RunWorkflow(ScriptItem workflow, string[] extraArgs, AppConfig? config)
        {
            if (config == null)
            {
                MessageBox.Show("无法获取配置，工作流无法执行。", "AltRunSharp",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Resolve and validate step references
            var steps = new List<(string exe, string args, string label, string workDir)>();
            var deadRefs = new List<string>();

            foreach (var stepName in workflow.WorkflowSteps)
            {
                var stepItem = config.ScriptItems.Find(s =>
                    string.Equals(s.Name, stepName, StringComparison.OrdinalIgnoreCase));

                if (stepItem == null || stepItem.ScriptType == "workflow")
                {
                    deadRefs.Add(stepName);
                    continue;
                }

                string stepExt = stepItem.ScriptType.ToLowerInvariant();
                bool stepIsExe = stepExt == "exe";

                string scriptPath = stepIsExe
                    ? stepItem.ScriptFileName
                    : Path.Combine(_dataDir, "scripts", stepItem.ScriptFileName);

                if (!File.Exists(scriptPath))
                {
                    deadRefs.Add(stepName);
                    continue;
                }

                string stepExe, stepBaseArgs, stepWorkDir;
                if (stepExt == "js")
                {
                    stepExe = "node";
                    stepBaseArgs = BuildArgString($"\"{scriptPath}\"", extraArgs);
                    stepWorkDir = _appBaseDir;
                }
                else if (stepExt == "bat")
                {
                    stepExe = "cmd.exe";
                    stepBaseArgs = BuildArgString($"/c \"{scriptPath}\"", extraArgs);
                    stepWorkDir = _appBaseDir;
                }
                else if (stepIsExe)
                {
                    stepExe = scriptPath;
                    stepBaseArgs = BuildArgString(string.Empty, extraArgs).Trim();
                    stepWorkDir = Path.GetDirectoryName(scriptPath) ?? _appBaseDir;
                }
                else // cs
                {
                    stepExe = "dotnet";
                    stepBaseArgs = BuildArgString($"run \"{scriptPath}\"", extraArgs);
                    stepWorkDir = _appBaseDir;
                }

                steps.Add((stepExe, stepBaseArgs, stepItem.Name, stepWorkDir));
            }

            // Auto-remove dead references
            if (deadRefs.Count > 0)
            {
                foreach (var dead in deadRefs)
                    workflow.WorkflowSteps.Remove(dead);
            }

            if (steps.Count == 0)
            {
                MessageBox.Show("工作流没有有效的步骤。", "AltRunSharp",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var win = new OutputWindow(steps, $"工作流: {workflow.Name}");
            win.Show();
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private static string BuildArgString(string baseArgs, string[] extraArgs)
        {
            if (extraArgs == null || extraArgs.Length == 0) return baseArgs;
            var sb = new System.Text.StringBuilder(baseArgs);
            foreach (var arg in extraArgs)
            {
                sb.Append(' ');
                sb.Append(QuoteArg(arg));
            }
            return sb.ToString();
        }

        private static string QuoteArg(string arg)
        {
            if (arg.Contains(' ') || arg.Contains('"'))
                return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            return arg;
        }

        private void RunSilent(string exe, string args, string workDir)
        {
            string logsDir = Path.Combine(_dataDir, "logs");
            Directory.CreateDirectory(logsDir);
            string logFile = Path.Combine(logsDir, DateTime.Now.ToString("yyyyMMdd") + ".log");

            var psi = new ProcessStartInfo
            {
                FileName = exe, Arguments = args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            try
            {
                var proc = new Process { StartInfo = psi };
                proc.Start();
                string header = $"\r\n=== [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exe} {args} ===\r\n";
                File.AppendAllText(logFile, header);

                proc.OutputDataReceived += (_, e) => { if (e.Data != null) File.AppendAllText(logFile, e.Data + "\r\n"); };
                proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) File.AppendAllText(logFile, "[ERR] " + e.Data + "\r\n"); };
                proc.EnableRaisingEvents = true;
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.Exited += (_, _) =>
                {
                    File.AppendAllText(logFile, $"=== 退出码: {proc.ExitCode} ===\r\n");
                    proc.Dispose();
                };
            }
            catch (Exception ex)
            {
                File.AppendAllText(logFile, $"[LAUNCH ERROR] {ex.Message}\r\n");
            }
        }

        private static void RunWithOutputWindow(string exe, string args, string title, string workDir)
        {
            var win = new OutputWindow(exe, args, title, workDir);
            win.Show();
        }
    }
}

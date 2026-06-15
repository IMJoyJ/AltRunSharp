using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AltRunSharp
{
    // ── Per-task runtime state ────────────────────────────────────────────────

    public class TaskRunState
    {
        public string TaskName { get; }
        public bool IsRunning { get; set; }
        public Process? CurrentProcess { get; set; }
        public bool KillRequested { get; set; }          // for "kill" conflict resolution
        public DateTime? LastFiredAt { get; set; }
        public string LastFiredDate { get; set; } = string.Empty;
        public HashSet<string> FiredTimesToday { get; } = new HashSet<string>();

        public TaskRunState(string name) { TaskName = name; }

        public string NextFireText(ScheduledTask task)
        {
            if (!task.Enabled) return "已禁用";
            if (IsRunning) return "执行中";
            if (task.TriggerType == "interval")
            {
                if (LastFiredAt == null) return "下次触发: 立即";
                var next = LastFiredAt.Value.AddSeconds(task.IntervalSeconds);
                var diff = next - DateTime.Now;
                if (diff.TotalSeconds <= 0) return "下次触发: 立即";
                if (diff.TotalHours >= 1) return $"下次触发: {diff.Hours}h {diff.Minutes}m 后";
                if (diff.TotalMinutes >= 1) return $"下次触发: {(int)diff.TotalMinutes}m {diff.Seconds}s 后";
                return $"下次触发: {(int)diff.TotalSeconds}s 后";
            }
            else
            {
                // Find next scheduled time today or tomorrow
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                string nowHHmm = DateTime.Now.ToString("HH:mm");
                var todayRemaining = task.DailyTimes
                    .Where(t => string.Compare(t, nowHHmm, StringComparison.Ordinal) > 0 &&
                                !(FiredTimesToday.Contains(t) && LastFiredDate == today))
                    .OrderBy(t => t).FirstOrDefault();
                if (todayRemaining != null) return $"下次触发: 今天 {todayRemaining}";
                var first = task.DailyTimes.OrderBy(t => t).FirstOrDefault();
                return first != null ? $"下次触发: 明天 {first}" : "无触发时间配置";
            }
        }
    }

    // ── ScheduleManager ───────────────────────────────────────────────────────

    public class ScheduleManager : IDisposable
    {
        private readonly string _dataDir;
        private readonly Dispatcher _dispatcher;
        private AppConfig _config;

        private readonly Dictionary<string, TaskRunState> _states = new();
        private readonly System.Threading.Timer _checkTimer;
        private readonly object _lock = new();

        public event Action? StateChanged;

        public ScheduleManager(string dataDir, Dispatcher dispatcher, AppConfig config)
        {
            _dataDir = dataDir;
            _dispatcher = dispatcher;
            _config = config;

            // Check triggers every 5 seconds
            _checkTimer = new System.Threading.Timer(CheckTriggers, null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void UpdateConfig(AppConfig config)
        {
            lock (_lock) { _config = config; }
        }

        public void StartBootTasks()
        {
            foreach (var task in _config.ScheduledTasks.Where(t => t.BootStart && t.Enabled))
                EnsureState(task.Name);
            // The timer will handle first fires; boot tasks are just enabled.
        }

        public IReadOnlyList<(ScheduledTask Task, TaskRunState State)> GetTaskStates()
        {
            lock (_lock)
            {
                return _config.ScheduledTasks
                    .Select(t => (t, EnsureState(t.Name)))
                    .ToList();
            }
        }

        public void FireNow(string taskName)
        {
            lock (_lock)
            {
                var task = _config.ScheduledTasks.FirstOrDefault(t => t.Name == taskName);
                if (task == null) return;
                var state = EnsureState(taskName);
                Fire(task, state);
            }
        }

        public void StopTask(string taskName)
        {
            lock (_lock)
            {
                if (_states.TryGetValue(taskName, out var s))
                {
                    s.KillRequested = true;
                    try { s.CurrentProcess?.Kill(entireProcessTree: true); } catch { }
                }
            }
        }

        public void Dispose()
        {
            _checkTimer.Dispose();
            lock (_lock)
            {
                foreach (var s in _states.Values)
                {
                    s.KillRequested = true;
                    try { s.CurrentProcess?.Kill(entireProcessTree: true); } catch { }
                }
            }
        }

        // ── Trigger check loop ─────────────────────────────────────────────

        private void CheckTriggers(object? _)
        {
            List<(ScheduledTask, TaskRunState)> toFire = new();
            lock (_lock)
            {
                foreach (var task in _config.ScheduledTasks.Where(t => t.Enabled))
                {
                    var state = EnsureState(task.Name);
                    if (ShouldFire(task, state))
                        toFire.Add((task, state));
                }
            }

            foreach (var (task, state) in toFire)
                Fire(task, state);

            if (toFire.Count > 0 || _states.Values.Any(s => s.IsRunning))
                NotifyChanged();
            else
                NotifyChanged(); // always refresh countdown text
        }

        private bool ShouldFire(ScheduledTask task, TaskRunState state)
        {
            DateTime now = DateTime.Now;

            if (task.TriggerType == "interval")
            {
                if (state.LastFiredAt == null) return task.BootStart; // only auto-fire on boot if BootStart=true
                return (now - state.LastFiredAt.Value).TotalSeconds >= task.IntervalSeconds;
            }
            else // daily
            {
                string today = now.ToString("yyyy-MM-dd");
                string nowHHmm = now.ToString("HH:mm");

                if (state.LastFiredDate != today)
                {
                    state.FiredTimesToday.Clear();
                    state.LastFiredDate = today;
                }

                return task.DailyTimes.Any(t => t == nowHHmm && !state.FiredTimesToday.Contains(t));
            }
        }

        private void Fire(ScheduledTask task, TaskRunState state)
        {
            // Conflict resolution
            if (state.IsRunning)
            {
                switch (task.ConflictResolution)
                {
                    case "skip":    return;
                    case "kill":
                        state.KillRequested = true;
                        try { state.CurrentProcess?.Kill(entireProcessTree: true); } catch { }
                        // Wait briefly for the old run to notice KillRequested
                        Thread.Sleep(200);
                        state.KillRequested = false;
                        break;
                    case "parallel": break; // fall through and start new
                }
            }

            // Record fire time
            DateTime now = DateTime.Now;
            state.LastFiredAt = now;
            if (task.TriggerType == "daily")
            {
                state.LastFiredDate = now.ToString("yyyy-MM-dd");
                state.FiredTimesToday.Add(now.ToString("HH:mm"));
            }

            // Run on a background thread
            Task.Run(() => Execute(task, state));
        }

        // ── Execution ──────────────────────────────────────────────────────

        private void Execute(ScheduledTask task, TaskRunState state)
        {
            state.IsRunning = true;
            state.KillRequested = false;
            NotifyChanged();

            string logDir = Path.Combine(_dataDir, "logs", "schedule");
            Directory.CreateDirectory(logDir);
            string safeName = string.Concat(task.Name.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string logFile = Path.Combine(logDir, safeName + ".log");

            AppendLog(logFile, $"\n=== [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 计划任务触发: {task.Name} ===");

            try
            {
                // 1. Get runtime args from optional args script
                string[] extraArgs = Array.Empty<string>();
                if (!string.IsNullOrEmpty(task.ArgsScriptName))
                {
                    extraArgs = RunArgsScript(task.ArgsScriptName, logFile);
                    AppendLog(logFile, $"[ARGS] 解析得到 {extraArgs.Length} 个参数");
                }

                // 2. Resolve workflow
                ScriptItem? workflow;
                AppConfig cfg;
                lock (_lock) { cfg = _config; }

                workflow = cfg.ScriptItems.FirstOrDefault(s =>
                    s.Name == task.WorkflowName && s.ScriptType == "workflow");

                if (workflow == null)
                {
                    AppendLog(logFile, $"[ERROR] 工作流不存在或类型不对: {task.WorkflowName}");
                    return;
                }

                // 3. Validate & run steps sequentially
                var steps = workflow.WorkflowSteps
                    .Select(name => cfg.ScriptItems.FirstOrDefault(s =>
                        s.Name == name && s.ScriptType != "workflow"))
                    .Where(s => s != null)
                    .ToList();

                if (steps.Count == 0)
                {
                    AppendLog(logFile, "[ERROR] 工作流没有有效步骤");
                    return;
                }

                int idx = 0;
                foreach (var step in steps)
                {
                    if (state.KillRequested) { AppendLog(logFile, "=== 执行被终止 ==="); break; }
                    idx++;
                    AppendLog(logFile, $"\n── 步骤 {idx}/{steps.Count}: {step!.Name} ──");
                    RunStepSilent(step, extraArgs, logFile, state);
                }

                AppendLog(logFile, $"\n=== 任务完成，退出 ===");
            }
            catch (Exception ex)
            {
                AppendLog(logFile, $"[FATAL] {ex.Message}");
            }
            finally
            {
                state.IsRunning = false;
                state.CurrentProcess = null;
                NotifyChanged();
            }
        }

        private void RunStepSilent(ScriptItem step, string[] extraArgs, string logFile, TaskRunState state)
        {
            string scriptPath = Path.Combine(_dataDir, "scripts", step.ScriptFileName);
            if (!File.Exists(scriptPath))
            {
                AppendLog(logFile, $"[SKIP] 脚本文件不存在: {scriptPath}");
                return;
            }

            string exe, baseArgs;
            if (step.ScriptType.Equals("js", StringComparison.OrdinalIgnoreCase))
            {
                exe = "node";
                baseArgs = $"\"{scriptPath}\"";
            }
            else
            {
                exe = "dotnet";
                baseArgs = $"run \"{scriptPath}\"";
            }

            string fullArgs = BuildArgString(baseArgs, extraArgs);

            var psi = new ProcessStartInfo
            {
                FileName = exe, Arguments = fullArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                var proc = new Process { StartInfo = psi };
                state.CurrentProcess = proc;
                proc.Start();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendLog(logFile, e.Data); };
                proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) AppendLog(logFile, "[ERR] " + e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                AppendLog(logFile, $"退出码: {proc.ExitCode}");
                proc.Dispose();
            }
            catch (Exception ex)
            {
                AppendLog(logFile, $"[LAUNCH ERROR] {ex.Message}");
            }
            finally
            {
                state.CurrentProcess = null;
            }
        }

        private string[] RunArgsScript(string scriptName, string logFile)
        {
            AppConfig cfg;
            lock (_lock) { cfg = _config; }

            var script = cfg.ScriptItems.FirstOrDefault(s =>
                s.Name == scriptName && s.ScriptType != "workflow");
            if (script == null) return Array.Empty<string>();

            string scriptPath = Path.Combine(_dataDir, "scripts", script.ScriptFileName);
            if (!File.Exists(scriptPath)) return Array.Empty<string>();

            string exe = script.ScriptType.Equals("js", StringComparison.OrdinalIgnoreCase) ? "node" : "dotnet";
            string args = script.ScriptType.Equals("js", StringComparison.OrdinalIgnoreCase)
                ? $"\"{scriptPath}\""
                : $"run \"{scriptPath}\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe, Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                var proc = Process.Start(psi)!;
                string stdout = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(8000);
                proc.Dispose();
                AppendLog(logFile, $"[ARGS SCRIPT] stdout: {stdout}");
                return ScriptArgsParser.Parse(stdout);
            }
            catch (Exception ex)
            {
                AppendLog(logFile, $"[ARGS SCRIPT ERROR] {ex.Message}");
                return Array.Empty<string>();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private TaskRunState EnsureState(string name)
        {
            if (!_states.TryGetValue(name, out var s))
                _states[name] = s = new TaskRunState(name);
            return s;
        }

        private static string BuildArgString(string baseArgs, string[] extraArgs)
        {
            if (extraArgs.Length == 0) return baseArgs;
            var sb = new System.Text.StringBuilder(baseArgs);
            foreach (var arg in extraArgs)
            {
                sb.Append(' ');
                sb.Append(arg.Contains(' ') || arg.Contains('"')
                    ? "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
                    : arg);
            }
            return sb.ToString();
        }

        private static void AppendLog(string file, string text)
        {
            try { File.AppendAllText(file, text + "\r\n"); } catch { }
        }

        private void NotifyChanged()
            => _dispatcher.BeginInvoke(new Action(() => StateChanged?.Invoke()),
                DispatcherPriority.Background);
    }
}

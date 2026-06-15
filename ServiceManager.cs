using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace AltRunSharp
{
    // ── ServiceEntry ─────────────────────────────────────────────────────────

    public class ServiceEntry
    {
        public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..8];
        public ScriptItem Item { get; }
        public string ExtraArgs { get; }   // extra args string entered at launch
        public Process? Process { get; set; }
        public bool IsRunning { get; set; }
        public bool IsWaitingRestart { get; set; }
        public bool StopRequested { get; set; }
        public DateTime? RestartAt { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(ExtraArgs)
            ? Item.Name
            : $"{Item.Name}  [{ExtraArgs}]";

        public string StatusText
        {
            get
            {
                if (IsRunning) return "运行中";
                if (IsWaitingRestart && RestartAt.HasValue)
                {
                    int secs = Math.Max(0, (int)(RestartAt.Value - DateTime.UtcNow).TotalSeconds);
                    return $"等待重启 ({secs}s)";
                }
                return "已停止";
            }
        }

        public ServiceEntry(ScriptItem item, string extraArgs = "")
        {
            Item = item;
            ExtraArgs = extraArgs ?? string.Empty;
        }
    }

    // ── ServiceManager ────────────────────────────────────────────────────────

    public class ServiceManager : IDisposable
    {
        private readonly string _dataDir;
        private readonly Dispatcher _dispatcher;
        private readonly ConcurrentDictionary<string, ServiceEntry> _services = new();

        // Job Object: ensures all child processes die when host exits
        private static readonly IntPtr _jobHandle = CreateKillOnCloseJobObject();

        public event Action? ServicesChanged;

        public ServiceManager(string dataDir, Dispatcher dispatcher)
        {
            _dataDir = dataDir;
            _dispatcher = dispatcher;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Start boot services (called at app startup).</summary>
        public void StartBootServices(IEnumerable<ScriptItem> allScripts)
        {
            foreach (var s in allScripts.Where(s =>
                s.LaunchMode == "service" && s.BootStart && s.ScriptType != "workflow"))
            {
                StartService(s);
            }
        }

        /// <summary>Start a new instance of the service. Multiple calls = multiple instances.</summary>
        public string StartService(ScriptItem item, string extraArgs = "")
        {
            var entry = new ServiceEntry(item, extraArgs);
            _services[entry.InstanceId] = entry;
            System.Threading.Tasks.Task.Run(() => RunLoop(entry));
            return entry.InstanceId;
        }

        /// <summary>Stop a specific instance by InstanceId.</summary>
        public void StopService(string instanceId)
        {
            if (!_services.TryGetValue(instanceId, out var entry)) return;
            entry.StopRequested = true;
            try { entry.Process?.Kill(entireProcessTree: true); } catch { }
            entry.IsRunning = false;
            entry.IsWaitingRestart = false;
            _services.TryRemove(instanceId, out _);
            NotifyChanged();
        }

        /// <summary>Stop all running instances.</summary>
        public void StopAllServices()
        {
            foreach (var id in _services.Keys.ToArray())
                StopService(id);
        }

        /// <summary>All active service instances, sorted by display name.</summary>
        public IReadOnlyList<ServiceEntry> GetServices()
            => _services.Values.OrderBy(e => e.DisplayName).ToList();

        /// <summary>True if any instance of the named script is currently active.</summary>
        public bool IsAnyInstanceActive(string scriptName)
            => _services.Values.Any(e => e.Item.Name == scriptName &&
               (e.IsRunning || e.IsWaitingRestart) && !e.StopRequested);

        public void Dispose() => StopAllServices();

        // ── Service loop (runs on background thread) ──────────────────────────

        private void RunLoop(ServiceEntry entry)
        {
            string logDir = Path.Combine(_dataDir, "logs", "services");
            Directory.CreateDirectory(logDir);
            // Log file: safe filename from script name + instance ID
            string safeName = string.Concat(entry.Item.Name.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string logFile = Path.Combine(logDir, $"{safeName}_{entry.InstanceId}.log");

            while (!entry.StopRequested)
            {
                string scriptPath = Path.Combine(_dataDir, "scripts", entry.Item.ScriptFileName);
                if (!File.Exists(scriptPath))
                {
                    AppendLog(logFile, $"[ERROR] 脚本文件不存在: {scriptPath}");
                    break;
                }

                string exe, baseArgs;
                if (entry.Item.ScriptType.Equals("js", StringComparison.OrdinalIgnoreCase))
                {
                    exe = "node";
                    baseArgs = $"\"{scriptPath}\"";
                }
                else if (entry.Item.ScriptType.Equals("cs", StringComparison.OrdinalIgnoreCase))
                {
                    exe = "dotnet";
                    baseArgs = $"run \"{scriptPath}\"";
                }
                else break;

                string fullArgs = string.IsNullOrWhiteSpace(entry.ExtraArgs)
                    ? baseArgs
                    : $"{baseArgs} {entry.ExtraArgs}";

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = fullArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                AppendLog(logFile,
                    $"\r\n=== [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 实例 {entry.InstanceId} 启动 ===");

                try
                {
                    var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    entry.Process = proc;
                    entry.IsRunning = true;
                    entry.IsWaitingRestart = false;
                    NotifyChanged();

                    proc.Start();

                    // Attach to Job Object so it dies with the host
                    AssignToJobObject(proc);

                    proc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendLog(logFile, e.Data); };
                    proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) AppendLog(logFile, "[ERR] " + e.Data); };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();

                    AppendLog(logFile, $"=== 进程退出，退出码: {proc.ExitCode} ===");
                    proc.Dispose();
                    entry.Process = null;
                }
                catch (Exception ex)
                {
                    AppendLog(logFile, $"[LAUNCH ERROR] {ex.Message}");
                }

                entry.IsRunning = false;
                if (entry.StopRequested) break;

                // ── 10-second restart countdown ───────────────────────────────
                entry.IsWaitingRestart = true;
                entry.RestartAt = DateTime.UtcNow.AddSeconds(10);
                AppendLog(logFile, "=== 10秒后自动重启 ===");

                for (int i = 0; i < 100; i++)
                {
                    if (entry.StopRequested) break;
                    Thread.Sleep(100);
                    if (i % 10 == 0) NotifyChanged();
                }

                entry.IsWaitingRestart = false;
                entry.RestartAt = null;
            }

            entry.IsRunning = false;
            entry.IsWaitingRestart = false;
            _services.TryRemove(entry.InstanceId, out _);
            NotifyChanged();
        }

        private static void AppendLog(string file, string text)
        {
            try { File.AppendAllText(file, text + "\r\n"); } catch { }
        }

        private void NotifyChanged()
            => _dispatcher.BeginInvoke(new Action(() => ServicesChanged?.Invoke()),
                DispatcherPriority.Background);

        // ── Win32 Job Object ──────────────────────────────────────────────────

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass,
            IntPtr lpInfo, int cbInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        private static IntPtr CreateKillOnCloseJobObject()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, ptr, size);
            }
            finally { Marshal.FreeHGlobal(ptr); }

            return job;
        }

        private static void AssignToJobObject(Process proc)
        {
            if (_jobHandle == IntPtr.Zero) return;
            try { AssignProcessToJobObject(_jobHandle, proc.Handle); } catch { }
        }
    }
}

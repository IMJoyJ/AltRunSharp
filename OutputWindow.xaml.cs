using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace AltRunSharp
{
    public partial class OutputWindow : Window
    {
        private Process? _process;
        private bool _killed = false;

        // ── Single-command constructor (existing behavior) ────────────────────

        public OutputWindow(string exe, string args, string title, string workingDir = "")
        {
            InitializeComponent();
            TitleLabel.Text = title;

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += (_, e) => { if (e.Data != null) AppendLine(e.Data, false); };
            _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) AppendLine(e.Data, true); };
            _process.Exited += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    int code = _process!.ExitCode;
                    SetStatus(code == 0);
                    AppendLine($"\n--- 进程已退出，退出码: {code} ---", code != 0);
                });
                _process!.Dispose();
                _process = null;
            };

            Closed += (_, _) => KillProcess();

            try
            {
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                AppendLine($"[启动失败] {ex.Message}", true);
                SetStatus(false);
            }
        }

        // ── Multi-command constructor (workflow) ─────────────────────────────
        // Runs each (exe, args, stepLabel) in sequence on a background task.

        public OutputWindow(IReadOnlyList<(string exe, string args, string label, string workDir)> steps, string title)
        {
            InitializeComponent();
            TitleLabel.Text = title;
            Closed += (_, _) => { _killed = true; KillProcess(); };

            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)); // yellow = running
            StatusLabel.Text = "工作流执行中...";

            Task.Run(async () =>
            {
                int stepIndex = 0;
                foreach (var (exe, args, label, workDir) in steps)
                {
                    if (_killed) break;
                    stepIndex++;
                    AppendLine($"\n{'═'.ToString().PadRight(50, '═')}", false);
                    AppendLine($"  步骤 {stepIndex}/{steps.Count}: {label}", false);
                    AppendLine($"{'═'.ToString().PadRight(50, '═')}", false);

                    var tcs = new TaskCompletionSource<int>();
                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments = args,
                            WorkingDirectory = workDir,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        },
                        EnableRaisingEvents = true
                    };
                    _process = proc;
                    proc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendLine(e.Data, false); };
                    proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) AppendLine(e.Data, true); };
                    proc.Exited += (_, _) => tcs.TrySetResult(proc.ExitCode);

                    try
                    {
                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        int code = await tcs.Task;
                        AppendLine($"--- 步骤退出，退出码: {code} ---", code != 0);
                        proc.Dispose();
                    }
                    catch (Exception ex)
                    {
                        AppendLine($"[步骤启动失败] {ex.Message}", true);
                        proc.Dispose();
                    }

                    _process = null;
                    if (_killed) break;
                }

                if (!_killed)
                {
                    AppendLine("\n━━━ 工作流完成 ━━━", false);
                    Dispatcher.Invoke(() => SetStatus(true));
                }
            });
        }

        // ── Shared helpers ────────────────────────────────────────────────────

        private void AppendLine(string text, bool isError)
        {
            Dispatcher.Invoke(() =>
            {
                var run = new System.Windows.Documents.Run(text + "\n")
                {
                    Foreground = isError
                        ? new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8))
                        : new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4))
                };
                OutputText.Inlines.Add(run);
                OutputScroll.ScrollToEnd();
            });
        }

        // Keep old name for compatibility
        private void AppendText(string text, bool isError) => AppendLine(text, isError);

        private void SetStatus(bool success)
        {
            StatusDot.Fill = success
                ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))
                : new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
            StatusLabel.Text = success ? "已完成" : "失败";
        }

        private void KillProcess()
        {
            try { if (_process is { } p && !p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        }
    }
}

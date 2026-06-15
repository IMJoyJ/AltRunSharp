using System.Collections.Generic;

namespace AltRunSharp
{
    public class AppConfig
    {
        public string Hotkey { get; set; } = "Alt+R";
        /// <summary>"single" | "double" | "triple"</summary>
        public string ClickMode { get; set; } = "single";
        public bool StartupEnabled { get; set; } = false;
        public bool ContextMenuEnabled { get; set; } = false;
        public List<LaunchItem> LaunchItems { get; set; } = new List<LaunchItem>();
        public List<ScriptItem> ScriptItems { get; set; } = new List<ScriptItem>();
        public List<ScheduledTask> ScheduledTasks { get; set; } = new List<ScheduledTask>();
    }

    public class LaunchItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Args { get; set; } = string.Empty;
        /// <summary>Additional search aliases. Matching triggers autocomplete back to Name.</summary>
        public List<string> Aliases { get; set; } = new List<string>();
    }

    public class ScriptItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        /// <summary>"js" | "cs" | "workflow"</summary>
        public string ScriptType { get; set; } = "js";
        /// <summary>"once" | "service" — only applies to js/cs types</summary>
        public string LaunchMode { get; set; } = "once";
        /// <summary>true = silent/log, false = output window — only for LaunchMode "once"</summary>
        public bool Silent { get; set; } = false;
        /// <summary>Filename under data/scripts/, e.g. "test_args.js" — only for js/cs</summary>
        public string ScriptFileName { get; set; } = string.Empty;
        /// <summary>Service: auto-start when host app launches (host manages this, no registry write)</summary>
        public bool BootStart { get; set; } = false;
        /// <summary>Workflow: ordered list of script Names to execute sequentially</summary>
        public List<string> WorkflowSteps { get; set; } = new List<string>();
        /// <summary>Additional search aliases. Matching triggers autocomplete back to /Name.</summary>
        public List<string> Aliases { get; set; } = new List<string>();
    }

    public class ScheduledTask
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;

        /// <summary>Reference to a workflow ScriptItem name to execute when triggered</summary>
        public string WorkflowName { get; set; } = string.Empty;

        /// <summary>Optional js/cs script whose stdout is parsed as args for the workflow</summary>
        public string ArgsScriptName { get; set; } = string.Empty;

        /// <summary>"interval" | "daily"</summary>
        public string TriggerType { get; set; } = "interval";

        /// <summary>Interval trigger: total seconds between executions</summary>
        public int IntervalSeconds { get; set; } = 3600;

        /// <summary>Daily trigger: fire times in "HH:mm" format (can have multiple)</summary>
        public List<string> DailyTimes { get; set; } = new List<string>();

        /// <summary>Auto-start tracking when host launches (no registry write)</summary>
        public bool BootStart { get; set; } = false;

        /// <summary>"parallel" | "kill" | "skip"</summary>
        public string ConflictResolution { get; set; } = "skip";
    }
}

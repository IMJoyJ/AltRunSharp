using System;
using System.Collections.Generic;
using System.Linq;

namespace AltRunSharp
{
    /// <summary>
    /// Unified search result entry shown in the launcher list.
    /// </summary>
    public class SearchResult
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        /// <summary>"launch" | "script" | "builtin"</summary>
        public string Kind { get; set; } = "launch";
        /// <summary>Built-in command id when Kind == "builtin" (e.g. "panel").</summary>
        public string? BuiltinCommand { get; set; }
        public LaunchItem? LaunchItem { get; set; }
        public ScriptItem? ScriptItem { get; set; }

        public string KindLabel => Kind == "builtin" ? "内置" : (Kind == "script" ? "脚本" : "程序");
        public string DisplayText => string.IsNullOrWhiteSpace(Description) ? Name : $"{Name}  —  {Description}";
    }

    public class LauncherViewModel
    {
        /// <summary>Built-in "/" commands (not backed by ScriptItem).</summary>
        public static readonly IReadOnlyList<(string Name, string Description)> BuiltinCommands =
            new (string, string)[] { ("panel", "打开设置面板") };

        private AppConfig _config = new AppConfig();

        public void UpdateConfig(AppConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Search for matching items by fuzzy name/description/path matching.
        /// Returns all items if query is empty.
        /// </summary>
        public List<SearchResult> Search(string query)
        {
            var results = new List<SearchResult>();

            query = query.Trim();

            // Build all results
            foreach (var li in _config.LaunchItems)
            {
                results.Add(new SearchResult
                {
                    Name = li.Name,
                    Description = li.Description,
                    Kind = "launch",
                    LaunchItem = li
                });
            }
            foreach (var si in _config.ScriptItems)
            {
                if (si.ExcludeFromSearch) continue;
                results.Add(new SearchResult
                {
                    Name = "/" + si.Name,
                    Description = si.Description,
                    Kind = "script",
                    ScriptItem = si
                });
            }

            if (string.IsNullOrEmpty(query))
                return new List<SearchResult>();

            var builtinResults = new List<SearchResult>();
            if (query.StartsWith("/"))
            {
                string sub = query.Substring(1);
                foreach (var (name, desc) in BuiltinCommands)
                {
                    if (string.IsNullOrEmpty(sub) || ContainsIgnoreCase(name, sub))
                    {
                        builtinResults.Add(new SearchResult
                        {
                            Name = "/" + name,
                            Description = desc,
                            Kind = "builtin",
                            BuiltinCommand = name
                        });
                    }
                }
            }

            // Filter: check if query is a substring of Name, Description, Path, or any Alias (case-insensitive)
            var filtered = results
                .Where(r =>
                    ContainsIgnoreCase(r.Name, query) ||
                    ContainsIgnoreCase(r.Description, query) ||
                    (r.LaunchItem != null && ContainsIgnoreCase(r.LaunchItem.Path, query)) ||
                    (r.LaunchItem != null && r.LaunchItem.Aliases.Any(a => ContainsIgnoreCase(a, query))) ||
                    (r.ScriptItem != null && r.ScriptItem.Aliases.Any(a => ContainsIgnoreCase(a, query))))
                .ToList();

            if (builtinResults.Count > 0)
            {
                int scriptIdx = filtered.FindIndex(r => r.Kind == "script");
                if (scriptIdx >= 0)
                    filtered.InsertRange(scriptIdx, builtinResults);
                else
                    filtered.AddRange(builtinResults);
            }

            return filtered;
        }

        /// <summary>
        /// Find a script item by exact command name (used when user types "/name args...").
        /// </summary>
        public SearchResult? FindScriptByName(string cmdName)
        {
            foreach (var (name, desc) in BuiltinCommands)
            {
                if (string.Equals(name, cmdName, StringComparison.OrdinalIgnoreCase))
                {
                    return new SearchResult
                    {
                        Name = "/" + name,
                        Description = desc,
                        Kind = "builtin",
                        BuiltinCommand = name
                    };
                }
            }

            foreach (var si in _config.ScriptItems)
            {
                if (string.Equals(si.Name, cmdName, StringComparison.OrdinalIgnoreCase) ||
                    si.Aliases.Any(a => string.Equals(a, cmdName, StringComparison.OrdinalIgnoreCase)))
                {
                    return new SearchResult
                    {
                        Name = "/" + si.Name,
                        Description = si.Description,
                        Kind = "script",
                        ScriptItem = si
                    };
                }
            }
            return null;
        }

        private static bool ContainsIgnoreCase(string source, string value)
            => source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

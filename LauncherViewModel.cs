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
        /// <summary>"launch" or "script"</summary>
        public string Kind { get; set; } = "launch";
        public LaunchItem? LaunchItem { get; set; }
        public ScriptItem? ScriptItem { get; set; }

        public string KindLabel => Kind == "script" ? "脚本" : "程序";
        public string DisplayText => string.IsNullOrWhiteSpace(Description) ? Name : $"{Name}  —  {Description}";
    }

    public class LauncherViewModel
    {
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
                results.Add(new SearchResult
                {
                    Name = "/" + si.Name,
                    Description = si.Description,
                    Kind = "script",
                    ScriptItem = si
                });
            }

            if (string.IsNullOrEmpty(query))
                return results;

            // Filter: check if query is a substring of Name, Description, Path, or any Alias (case-insensitive)
            return results
                .Where(r =>
                    ContainsIgnoreCase(r.Name, query) ||
                    ContainsIgnoreCase(r.Description, query) ||
                    (r.LaunchItem != null && ContainsIgnoreCase(r.LaunchItem.Path, query)) ||
                    (r.LaunchItem != null && r.LaunchItem.Aliases.Any(a => ContainsIgnoreCase(a, query))) ||
                    (r.ScriptItem != null && r.ScriptItem.Aliases.Any(a => ContainsIgnoreCase(a, query))))
                .ToList();
        }

        /// <summary>
        /// Find a script item by exact command name (used when user types "/name args...").
        /// </summary>
        public SearchResult? FindScriptByName(string cmdName)
        {
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

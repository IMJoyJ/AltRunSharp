using System;
using System.Collections.Generic;

namespace AltRunSharp
{
    /// <summary>
    /// Parses a shell-like argument string into individual string arguments.
    /// Supports quoted args with escape sequences, unquoted args, and spaces.
    /// Examples:
    ///   /cmd "hello world" "\"escaped\"" simple  =>  ["hello world", "\"escaped\"", "simple"]
    /// </summary>
    public static class ScriptArgsParser
    {
        public static string[] Parse(string input)
        {
            var args = new List<string>();
            int i = 0;
            int len = input.Length;

            while (i < len)
            {
                // Skip whitespace
                while (i < len && char.IsWhiteSpace(input[i])) i++;
                if (i >= len) break;

                if (input[i] == '"')
                {
                    // Quoted argument
                    i++; // skip opening quote
                    var sb = new System.Text.StringBuilder();
                    while (i < len)
                    {
                        char c = input[i];
                        if (c == '\\' && i + 1 < len)
                        {
                            // Escape sequence
                            char next = input[i + 1];
                            if (next == '"' || next == '\\')
                            {
                                sb.Append(next);
                                i += 2;
                            }
                            else
                            {
                                sb.Append(c);
                                i++;
                            }
                        }
                        else if (c == '"')
                        {
                            i++; // skip closing quote
                            break;
                        }
                        else
                        {
                            sb.Append(c);
                            i++;
                        }
                    }
                    args.Add(sb.ToString());
                }
                else
                {
                    // Unquoted argument (ends at whitespace)
                    int start = i;
                    while (i < len && !char.IsWhiteSpace(input[i])) i++;
                    args.Add(input.Substring(start, i - start));
                }
            }

            return args.ToArray();
        }
    }
}

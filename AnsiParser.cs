using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;

namespace AltRunSharp
{
    public class AnsiSegment
    {
        public string Text { get; set; } = string.Empty;
        public Color? Foreground { get; set; }   // null = 使用调用方默认色
        public Color? Background { get; set; }   // null = 透明
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
    }

    public sealed class AnsiParser
    {
        // ── Catppuccin Mocha Color Palette ───────────────────────────────────

        private static readonly Color[] StandardColors =
        [
            Color.FromRgb(0x45, 0x47, 0x5A), // 0: Black   #45475A
            Color.FromRgb(0xF3, 0x8B, 0xA8), // 1: Red     #F38BA8
            Color.FromRgb(0xA6, 0xE3, 0xA1), // 2: Green   #A6E3A1
            Color.FromRgb(0xF9, 0xE2, 0xAF), // 3: Yellow  #F9E2AF
            Color.FromRgb(0x89, 0xB4, 0xFA), // 4: Blue    #89B4FA
            Color.FromRgb(0xCB, 0xA6, 0xF7), // 5: Magenta #CBA6F7
            Color.FromRgb(0x94, 0xE2, 0xD5), // 6: Cyan    #94E2D5
            Color.FromRgb(0xBA, 0xC2, 0xDE)  // 7: White   #BAC2DE
        ];

        private static readonly Color[] BrightColors =
        [
            Color.FromRgb(0x58, 0x5B, 0x70), // 8: Bright Black    #585B70
            Color.FromRgb(0xF5, 0xA3, 0xB7), // 9: Bright Red      #F5A3B7
            Color.FromRgb(0xB5, 0xE8, 0xB0), // 10: Bright Green   #B5E8B0
            Color.FromRgb(0xFA, 0xE7, 0xC3), // 11: Bright Yellow  #FAE7C3
            Color.FromRgb(0xA5, 0xC5, 0xFB), // 12: Bright Blue    #A5C5FB
            Color.FromRgb(0xD8, 0xBF, 0xF9), // 13: Bright Magenta #D8BFF9
            Color.FromRgb(0xAB, 0xE9, 0xDE), // 14: Bright Cyan    #ABE9DE
            Color.FromRgb(0xCD, 0xD6, 0xF4)  // 15: Bright White   #CDD6F4
        ];

        private static readonly byte[] CubeSteps = [0, 95, 135, 175, 215, 255];

        // ── Persistent SGR State ─────────────────────────────────────────────

        private Color? _foreground;
        private Color? _background;
        private bool _bold;
        private bool _dim;
        private bool _italic;
        private bool _underline;

        public AnsiParser()
        {
            Reset();
        }

        public void Reset()
        {
            _foreground = null;
            _background = null;
            _bold = false;
            _dim = false;
            _italic = false;
            _underline = false;
        }

        private Color? GetEffectiveForeground()
        {
            if (!_foreground.HasValue) return null;
            if (!_dim) return _foreground.Value;
            var c = _foreground.Value;
            return Color.FromRgb((byte)(c.R * 2 / 3), (byte)(c.G * 2 / 3), (byte)(c.B * 2 / 3));
        }

        public List<AnsiSegment> ParseLine(string line)
        {
            var segments = new List<AnsiSegment>();
            if (string.IsNullOrEmpty(line))
            {
                return segments;
            }

            var currentText = new StringBuilder();

            void Flush()
            {
                if (currentText.Length > 0)
                {
                    segments.Add(new AnsiSegment
                    {
                        Text = currentText.ToString(),
                        Foreground = GetEffectiveForeground(),
                        Background = _background,
                        Bold = _bold,
                        Italic = _italic,
                        Underline = _underline
                    });
                    currentText.Clear();
                }
            }

            int i = 0;
            int len = line.Length;

            while (i < len)
            {
                char ch = line[i];

                if (ch == '\x1b')
                {
                    // Escape sequence start
                    if (i + 1 >= len)
                    {
                        // Incomplete ESC at line end -> swallow
                        break;
                    }

                    char next = line[i + 1];
                    if (next == '[')
                    {
                        // CSI sequence: ESC [ ... <final_byte>
                        int j = i + 2;
                        // Parameter bytes (0x30–0x3F) and intermediate bytes (0x20–0x2F)
                        while (j < len && line[j] >= 0x20 && line[j] <= 0x3F)
                        {
                            j++;
                        }

                        if (j < len && line[j] >= 0x40 && line[j] <= 0x7E)
                        {
                            char finalChar = line[j];
                            string paramStr = line.Substring(i + 2, j - (i + 2));

                            if (finalChar == 'm')
                            {
                                Flush();
                                ApplySgr(paramStr);
                            }
                            // Non-'m' CSI sequences (e.g. 'K', 'H', 'J', etc.) are swallowed

                            i = j + 1;
                        }
                        else
                        {
                            // Incomplete CSI sequence at line end -> swallow remainder
                            break;
                        }
                    }
                    else if (next == ']')
                    {
                        // OSC sequence: ESC ] ... (BEL | ESC \)
                        int j = i + 2;
                        while (j < len)
                        {
                            if (line[j] == '\x07')
                            {
                                j++;
                                break;
                            }
                            if (line[j] == '\x1b' && j + 1 < len && line[j + 1] == '\\')
                            {
                                j += 2;
                                break;
                            }
                            j++;
                        }
                        i = j; // Swallowed OSC sequence
                    }
                    else if (next == '(' || next == ')' || next == '*' || next == '+')
                    {
                        // Character set designation: ESC ( C -> swallow up to 3 chars
                        i = (i + 2 < len) ? i + 3 : len;
                    }
                    else
                    {
                        // Other 2-char escape sequence (e.g. ESC =, ESC >, ESC N, etc.) -> swallow 2 chars
                        i += 2;
                    }
                }
                else if (ch == '\r' || ch == '\x07' || ch == '\x08' || ch == '\0' || ch == 0x7f || (ch < 32 && ch != '\t' && ch != '\n'))
                {
                    // Control characters -> swallow
                    i++;
                }
                else
                {
                    currentText.Append(ch);
                    i++;
                }
            }

            Flush();
            return segments;
        }

        private void ApplySgr(string paramStr)
        {
            if (string.IsNullOrEmpty(paramStr))
            {
                Reset();
                return;
            }

            var parts = paramStr.Split(';', ':');
            var tokens = new List<int>(parts.Length);
            foreach (var p in parts)
            {
                if (string.IsNullOrEmpty(p))
                {
                    tokens.Add(0);
                }
                else if (int.TryParse(p, out int val))
                {
                    tokens.Add(val);
                }
                else
                {
                    tokens.Add(0);
                }
            }

            if (tokens.Count == 0)
            {
                Reset();
                return;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                int code = tokens[i];
                switch (code)
                {
                    case 0:
                        Reset();
                        break;
                    case 1:
                        _bold = true;
                        break;
                    case 2:
                        _dim = true;
                        break;
                    case 3:
                        _italic = true;
                        break;
                    case 4:
                        _underline = true;
                        break;
                    case 22:
                        _bold = false;
                        _dim = false;
                        break;
                    case 23:
                        _italic = false;
                        break;
                    case 24:
                        _underline = false;
                        break;
                    case >= 30 and <= 37:
                        _foreground = StandardColors[code - 30];
                        break;
                    case 38:
                        // Extended foreground: 38;5;n or 38;2;r;g;b
                        if (i + 1 < tokens.Count)
                        {
                            int mode = tokens[i + 1];
                            if (mode == 5) // 256 color
                            {
                                if (i + 2 < tokens.Count)
                                {
                                    _foreground = Get256Color(tokens[i + 2]);
                                    i += 2;
                                }
                                else
                                {
                                    i += 1;
                                }
                            }
                            else if (mode == 2) // 24-bit RGB
                            {
                                if (i + 4 < tokens.Count)
                                {
                                    int r = tokens[i + 2];
                                    int g = tokens[i + 3];
                                    int b = tokens[i + 4];
                                    if (r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
                                    {
                                        _foreground = Color.FromRgb((byte)r, (byte)g, (byte)b);
                                    }
                                    i += 4;
                                }
                                else
                                {
                                    i = tokens.Count;
                                }
                            }
                        }
                        break;
                    case 39:
                        _foreground = null;
                        break;
                    case >= 40 and <= 47:
                        _background = StandardColors[code - 40];
                        break;
                    case 48:
                        // Extended background: 48;5;n or 48;2;r;g;b
                        if (i + 1 < tokens.Count)
                        {
                            int mode = tokens[i + 1];
                            if (mode == 5) // 256 color
                            {
                                if (i + 2 < tokens.Count)
                                {
                                    _background = Get256Color(tokens[i + 2]);
                                    i += 2;
                                }
                                else
                                {
                                    i += 1;
                                }
                            }
                            else if (mode == 2) // 24-bit RGB
                            {
                                if (i + 4 < tokens.Count)
                                {
                                    int r = tokens[i + 2];
                                    int g = tokens[i + 3];
                                    int b = tokens[i + 4];
                                    if (r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
                                    {
                                        _background = Color.FromRgb((byte)r, (byte)g, (byte)b);
                                    }
                                    i += 4;
                                }
                                else
                                {
                                    i = tokens.Count;
                                }
                            }
                        }
                        break;
                    case 49:
                        _background = null;
                        break;
                    case >= 90 and <= 97:
                        _foreground = BrightColors[code - 90];
                        break;
                    case >= 100 and <= 107:
                        _background = BrightColors[code - 100];
                        break;
                    default:
                        // Unknown SGR code: ignore safely
                        break;
                }
            }
        }

        public static Color Get256Color(int n)
        {
            if (n < 0 || n > 255) return Color.FromRgb(0xCD, 0xD6, 0xF4);
            if (n < 8) return StandardColors[n];
            if (n < 16) return BrightColors[n - 8];
            if (n <= 231)
            {
                int index = n - 16;
                int rIndex = index / 36;
                int gIndex = (index % 36) / 6;
                int bIndex = index % 6;
                return Color.FromRgb(CubeSteps[rIndex], CubeSteps[gIndex], CubeSteps[bIndex]);
            }
            // 232..255 grayscale
            byte gray = (byte)(8 + 10 * (n - 232));
            return Color.FromRgb(gray, gray, gray);
        }
    }
}

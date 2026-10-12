using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace CM3ResolutionPatcher
{
    // Puts cm3text.dll (embedded in this program) and its settings file next to cm3.exe. The patched cm3.exe loads the
    // DLL when it starts drawing text; if the DLL is missing the game simply draws its own text as it always did.
    public static class TextOverlayInstaller
    {
        public const string DllName = "cm3text.dll";
        public const string IniName = "cm3text.ini";
        public const string DefaultFont = "Segoe UI";
        public const int DefaultHighlightFrom = 13;
        public static readonly Color DefaultKeyColour = Color.FromArgb(187, 68, 68);
        public static readonly Color DefaultBorderColour = Color.FromArgb(235, 205, 90);
        public static readonly Color DefaultSecondaryColour = Color.FromArgb(55, 185, 40);
        public static readonly Color DefaultOtherColour = Color.FromArgb(102, 102, 102);

        // fontFace: the font for all text. highlightAttributes: colour player attributes by value and put a bar behind the
        // best ones (those at or above highlightFrom).
        public static void Install(string gameFolder, string fontFace, bool highlightAttributes, int highlightFrom, bool byValue, bool border, Color keyColour, Color secondaryColour, Color otherColour, Color borderColour)
        {
            string dllPath = Path.Combine(gameFolder, DllName);

            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(DllName))
            {
                if (resource == null)
                    throw new Exception("cm3text.dll isn't embedded in this build of the patcher");

                byte[] bytes = new byte[resource.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = resource.Read(bytes, read, bytes.Length - read);
                    if (count <= 0)
                        break;
                    read += count;
                }

                // Don't rewrite an identical file (it may be in use by a running game)
                if (!File.Exists(dllPath) || !SameBytes(File.ReadAllBytes(dllPath), bytes))
                    File.WriteAllBytes(dllPath, bytes);
            }

            if (string.IsNullOrEmpty(fontFace))
                fontFace = DefaultFont;

            string iniPath = Path.Combine(gameFolder, IniName);
            SetValue(iniPath, "text", "font", fontFace);
            SetValue(iniPath, "text", "titlefont", fontFace);
            SetValue(iniPath, "attributes", "enabled", highlightAttributes ? "1" : "0");
            SetValue(iniPath, "attributes", "highlightfrom", highlightFrom.ToString());
            SetValue(iniPath, "attributes", "mode", byValue ? "value" : "position");
            SetValue(iniPath, "attributes", "border", border ? "1" : "0");
            SetValue(iniPath, "attributes", "keycolour", ToHex(keyColour));
            SetValue(iniPath, "attributes", "secondarycolour", ToHex(secondaryColour));
            SetValue(iniPath, "attributes", "othercolour", ToHex(otherColour));
            SetValue(iniPath, "attributes", "bordercolour", ToHex(borderColour));
        }

        // Removes the DLL (the settings file is kept in case the overlay is switched back on later)
        public static void Remove(string gameFolder)
        {
            string dllPath = Path.Combine(gameFolder, DllName);
            if (File.Exists(dllPath))
            {
                File.SetAttributes(dllPath, FileAttributes.Normal);
                File.Delete(dllPath);
            }
        }

        public static bool IsInstalled(string gameFolder)
        {
            return File.Exists(Path.Combine(gameFolder, DllName));
        }

        // The font face used for all text (the two big title fonts get a bold version of the same face)
        public static string ReadFont(string gameFolder)
        {
            string value = GetValue(Path.Combine(gameFolder, IniName), "text", "font");
            return string.IsNullOrEmpty(value) ? DefaultFont : value;
        }

        public static bool ReadHighlightAttributes(string gameFolder)
        {
            string value = GetValue(Path.Combine(gameFolder, IniName), "attributes", "enabled");
            return value == null || value != "0";
        }

        // True if highlights are coloured by value, false if by the player's position
        public static bool ReadByValue(string gameFolder)
        {
            string value = GetValue(Path.Combine(gameFolder, IniName), "attributes", "mode");
            return value == null || value.Trim().ToLowerInvariant() != "position";
        }

        public static bool ReadBorder(string gameFolder)
        {
            string value = GetValue(Path.Combine(gameFolder, IniName), "attributes", "border");
            return value == "1";
        }

        public static int ReadHighlightFrom(string gameFolder)
        {
            int result;
            string value = GetValue(Path.Combine(gameFolder, IniName), "attributes", "highlightfrom");
            return (value != null && int.TryParse(value, out result) && result >= 1 && result <= 20) ? result : DefaultHighlightFrom;
        }

        static string ToHex(Color c)
        {
            return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        static Color ReadColour(string gameFolder, string key, Color fallback)
        {
            int rgb;
            string value = GetValue(Path.Combine(gameFolder, IniName), "attributes", key);
            if (value != null && value.Length == 6 && int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out rgb))
                return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return fallback;
        }

        public static Color ReadKeyColour(string gameFolder) { return ReadColour(gameFolder, "keycolour", DefaultKeyColour); }

        public static Color ReadOtherColour(string gameFolder) { return ReadColour(gameFolder, "othercolour", DefaultOtherColour); }

        public static Color ReadBorderColour(string gameFolder) { return ReadColour(gameFolder, "bordercolour", DefaultBorderColour); }

        public static Color ReadSecondaryColour(string gameFolder) { return ReadColour(gameFolder, "secondarycolour", DefaultSecondaryColour); }

        // ---- a tiny ini reader/writer that keeps everything else in the file (other settings, comments) as it was ----

        static string GetValue(string iniPath, string section, string key)
        {
            if (!File.Exists(iniPath))
                return null;

            bool inSection = false;
            foreach (string line in File.ReadAllLines(iniPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("["))
                {
                    inSection = trimmed.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (inSection && trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(key.Length + 1).Trim();
            }
            return null;
        }

        static void SetValue(string iniPath, string section, string key, string value)
        {
            List<string> lines = new List<string>();
            if (File.Exists(iniPath))
                lines.AddRange(File.ReadAllLines(iniPath));

            int sectionStart = -1, sectionEnd = lines.Count, keyLine = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("["))
                {
                    if (sectionStart >= 0 && sectionEnd == lines.Count)
                        sectionEnd = i;
                    if (trimmed.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase))
                        sectionStart = i;
                }
                else if (sectionStart >= 0 && sectionEnd == lines.Count && trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    keyLine = i;
            }

            string newLine = key + "=" + value;
            if (keyLine >= 0)
                lines[keyLine] = newLine;
            else if (sectionStart >= 0)
            {
                // Insert after the last non-blank line of the section
                int at = sectionEnd;
                while (at > sectionStart + 1 && lines[at - 1].Trim().Length == 0)
                    at--;
                lines.Insert(at, newLine);
            }
            else
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
                    lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(newLine);
            }

            File.WriteAllText(iniPath, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine, Encoding.Unicode);
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}

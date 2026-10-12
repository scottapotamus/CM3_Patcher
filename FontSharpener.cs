using System;
using System.IO;

namespace CM3ResolutionPatcher
{
    // CM3's fonts (Data\arial_*.fnt, Data\trade_cond_*.fnt) are pre-drawn glyph bitmaps that are already anti-aliased
    // for 800x600. Enlarging them blurs the soft edges a second time, so this tightens the edges of every glyph
    // (a contrast curve on its intensities) while keeping every width, offset and advance, so the layout is unchanged.
    //
    // File format (little endian):
    //   int   height                       rows per glyph
    //   for each character 0x20..0xFF:
    //     int advance, int width, int field2, int field3     (width is in bytes)
    //     byte[width * height] pixels                        4 bits per pixel, two pixels per byte, high nibble on the left
    //
    // The original files are kept as <name>.fnt.orig; the sharpened ones are always made from those, so the
    // strength can be changed any time and Restore() puts everything back.
    public static class FontSharpener
    {
        public enum Strength
        {
            None = 0,
            Medium = 1,
            Strong = 2,
        }

        static readonly string[] FontFiles = {
            "arial_14.fnt", "arial_16.fnt", "arial_18.fnt",
            "arial_narrow_10.fnt", "arial_narrow_11.fnt",
            "trade_cond_24_bold.fnt", "trade_cond_28_bold.fnt",
        };

        const string BackupExtension = ".orig";

        public static bool HasFonts(string dataFolder)
        {
            foreach (string font in FontFiles)
                if (!File.Exists(Path.Combine(dataFolder, font)))
                    return false;
            return true;
        }

        // Which strength is currently installed isn't recorded, only whether the originals are backed up (i.e. sharpened)
        public static bool IsSharpened(string dataFolder)
        {
            foreach (string font in FontFiles)
                if (File.Exists(Path.Combine(dataFolder, font + BackupExtension)))
                    return true;
            return false;
        }

        public static void Apply(string dataFolder, Strength strength)
        {
            if (strength == Strength.None)
            {
                Restore(dataFolder);
                return;
            }

            if (!HasFonts(dataFolder))
                throw new FileNotFoundException("Couldn't find the CM3 font files in " + dataFolder);

            byte[] table = BuildTable(strength);

            // Convert everything in memory first so a bad file can't leave a half-converted set behind
            byte[][] converted = new byte[FontFiles.Length][];
            for (int i = 0; i < FontFiles.Length; i++)
            {
                string path = Path.Combine(dataFolder, FontFiles[i]);
                string backup = path + BackupExtension;
                byte[] original = File.ReadAllBytes(File.Exists(backup) ? backup : path);
                converted[i] = Convert(original, table, FontFiles[i]);
            }

            for (int i = 0; i < FontFiles.Length; i++)
            {
                string path = Path.Combine(dataFolder, FontFiles[i]);
                string backup = path + BackupExtension;
                if (!File.Exists(backup))
                    File.Copy(path, backup);
                WriteFile(path, converted[i]);
            }
        }

        public static void Restore(string dataFolder)
        {
            foreach (string font in FontFiles)
            {
                string path = Path.Combine(dataFolder, font);
                string backup = path + BackupExtension;
                if (!File.Exists(backup))
                    continue;

                WriteFile(path, File.ReadAllBytes(backup));
                File.SetAttributes(backup, FileAttributes.Normal);
                File.Delete(backup);
            }
        }

        // Maps a 4 bit intensity to a 4 bit intensity: everything at or below Low goes to 0, at or above High goes to 15,
        // linear in between. A narrower range gives harder edges.
        static byte[] BuildTable(Strength strength)
        {
            double low = strength == Strength.Strong ? 0.35 : 0.20;
            double high = strength == Strength.Strong ? 0.65 : 0.75;

            byte[] table = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                double t = (i / 15.0 - low) / (high - low);
                t = Math.Min(1.0, Math.Max(0.0, t));
                table[i] = (byte)Math.Round(t * 15.0);
            }
            return table;
        }

        static byte[] Convert(byte[] original, byte[] table, string name)
        {
            byte[] data = (byte[])original.Clone();
            int height = BitConverter.ToInt32(data, 0);
            int position = 4;

            for (int c = 0x20; c < 0x100; c++)
            {
                if (position + 16 > data.Length)
                    throw new InvalidDataException(name + " isn't in the expected font format");

                int width = BitConverter.ToInt32(data, position + 4);
                position += 16;

                long size = (long)width * height;
                if (width < 0 || height <= 0 || position + size > data.Length)
                    throw new InvalidDataException(name + " isn't in the expected font format");

                for (int i = position; i < position + size; i++)
                    data[i] = (byte)((table[data[i] >> 4] << 4) | table[data[i] & 15]);
                position += (int)size;
            }

            if (position != data.Length)
                throw new InvalidDataException(name + " isn't in the expected font format");
            return data;
        }

        // The game's files are read-only
        static void WriteFile(string path, byte[] data)
        {
            if (File.Exists(path))
                File.SetAttributes(path, FileAttributes.Normal);
            File.WriteAllBytes(path, data);
        }
    }
}

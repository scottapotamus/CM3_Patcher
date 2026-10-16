using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CM3SaveGameEditor
{
    // A league or cup of the game as it is in the game's folder right now
    public class GameCompetition
    {
        public int Id;
        public bool National;           // from nation_comp.dat rather than club_comp.dat
        public string Name;             // what Data\club_comp.dat / nation_comp.dat say now
        public string Short;
        public string OriginalName;     // before the first rename (from the .orig copy of the table), the same as Name if never renamed
        public string OriginalShort;
        public int ExeOffset = -1;      // where the name is in cm3.exe (-1 = it isn't there, so the competition can't be renamed)
        public string ExeName = "";     // the name that is in cm3.exe there (the same as Name unless a rename only got half done)
        public int Capacity;            // the longest name that fits there

        public bool Renameable { get { return ExeOffset >= 0; } }

        // cm3.exe and the tables agree about the name (when they don't the game stops when it loads, so it needs finishing)
        public bool InSync { get { return ExeName == Name; } }
    }

    // The new name of a competition
    public class GameCompetitionChange
    {
        public GameCompetition Competition;
        public string Name;
        public string Short;
    }

    // Renames the leagues and cups of the game itself (new games).
    //
    // cm3.exe has a table of names (nations, leagues, cups) and compares every name in a game it loads with it; a competition that
    // isn't called what the exe expects makes the game stop with "v3.04e index.cpp 1635" (the number depends on which one). So a
    // competition can only be renamed where the game gets its names from as well: the string in cm3.exe (a name that doesn't fit in the
    // room of the old one goes in a new section of the exe and the one place that uses it is pointed at it), the table in Data\club_comp.dat / nation_comp.dat (a new game starts from it), the language
    // files (Data\*.lng, a table of 52 byte name + 24 byte short name pairs the game shows names from) and the picture list
    // (Pictures\pics.cfg, which names the pictures of a competition by its name). Saved games have their own copies (see
    // SaveGameFile.ReplaceCompetitions).
    public static class CompetitionRenamer
    {
        const int RecordSize = 107;
        const int NameOffset = 4;
        const int NameLength = 52;
        const int ShortOffset = 56;
        const int ShortLength = 24;

        public const int MaxName = 50;
        public const int MaxShort = 24;

        static readonly Encoding Latin = Encoding.GetEncoding(1252);

        static string DataFolder(string exe)
        {
            return Path.Combine(Path.GetDirectoryName(exe), "Data");
        }

        // The competitions in the game's tables with where their names are in cm3.exe
        public static List<GameCompetition> Read(string exe)
        {
            List<GameCompetition> list = new List<GameCompetition>();
            byte[] exeBytes = File.ReadAllBytes(exe);
            string data = DataFolder(exe);
            foreach (string table in new string[] { "club_comp.dat", "nation_comp.dat" })
            {
                string file = Path.Combine(data, table);
                if (!File.Exists(file))
                    continue;
                byte[] bytes = File.ReadAllBytes(file);
                string orig = file + ".orig";
                byte[] origBytes = File.Exists(orig) ? File.ReadAllBytes(orig) : null;
                if (bytes.Length % RecordSize != 0)
                    throw new InvalidDataException(table + " isn't in the format this tool knows.");
                for (int at = 0; at < bytes.Length; at += RecordSize)
                {
                    GameCompetition c = new GameCompetition();
                    c.Id = BitConverter.ToInt32(bytes, at);
                    c.National = table == "nation_comp.dat";
                    c.Name = ReadString(bytes, at + NameOffset, NameLength);
                    c.Short = ReadString(bytes, at + ShortOffset, ShortLength);
                    c.OriginalName = c.Name;
                    c.OriginalShort = c.Short;
                    if (origBytes != null && origBytes.Length == bytes.Length && BitConverter.ToInt32(origBytes, at) == c.Id)
                    {
                        c.OriginalName = ReadString(origBytes, at + NameOffset, NameLength);
                        c.OriginalShort = ReadString(origBytes, at + ShortOffset, ShortLength);
                    }
                    if (c.Name.Length == 0)
                        continue;
                    FindInExe(exeBytes, c, c.Name);
                    if (!c.Renameable && c.OriginalName != c.Name)
                        FindInExe(exeBytes, c, c.OriginalName);         // the tables were renamed but cm3.exe wasn't (a rename that stopped half way)
                    list.Add(c);
                }
            }
            return list;
        }

        // The name must be in the exe once, with a zero before and after it: then it is a string of its own that can be changed
        static void FindInExe(byte[] exe, GameCompetition c, string nameInExe)
        {
            byte[] name = Latin.GetBytes(nameInExe);
            List<int> found = new List<int>();
            for (int at = 1; at + name.Length < exe.Length; at++)
            {
                if (exe[at - 1] != 0 || exe[at] != name[0] || exe[at + name.Length] != 0)
                    continue;
                if (Matches(exe, at, name))
                    found.Add(at);
            }
            if (found.Count > 1)
            {
                // the game's own copy of a name we have moved is still there: the one in our section is the one in use
                Section own = FindSection(exe, StringSection);
                List<int> ours = new List<int>();
                if (own != null)
                    foreach (int at in found)
                        if (at >= own.RawPointer && at < own.RawPointer + own.RawSize)
                            ours.Add(at);
                if (ours.Count != 1)
                    return;                         // not sure which one the game uses
                found = ours;
            }
            if (found.Count != 1)
                return;
            int zeros = 0;
            while (found[0] + name.Length + zeros < exe.Length && exe[found[0] + name.Length + zeros] == 0)
                zeros++;
            c.ExeOffset = found[0];
            c.ExeName = nameInExe;
            c.Capacity = Math.Min(MaxName, name.Length + zeros - 1);       // keep one zero to end it
        }

        // ---- the sections of cm3.exe: a name that is longer than the room of the old one is put in a section of its own
        const string StringSection = ".cm3str";
        const int StringSectionSize = 0x4000;

        class Section
        {
            public string Name;
            public int Rva, VirtualSize, RawPointer, RawSize, Header;
        }

        static List<Section> Sections(byte[] exe)
        {
            int pe = BitConverter.ToInt32(exe, 0x3C);
            int count = BitConverter.ToInt16(exe, pe + 6);
            int first = pe + 24 + BitConverter.ToInt16(exe, pe + 20);
            List<Section> list = new List<Section>();
            for (int i = 0; i < count; i++)
            {
                int header = first + i * 40;
                Section section = new Section();
                int end = 0;
                while (end < 8 && exe[header + end] != 0)
                    end++;
                section.Name = Encoding.ASCII.GetString(exe, header, end);
                section.VirtualSize = BitConverter.ToInt32(exe, header + 8);
                section.Rva = BitConverter.ToInt32(exe, header + 12);
                section.RawSize = BitConverter.ToInt32(exe, header + 16);
                section.RawPointer = BitConverter.ToInt32(exe, header + 20);
                section.Header = header;
                list.Add(section);
            }
            return list;
        }

        static Section FindSection(byte[] exe, string name)
        {
            foreach (Section section in Sections(exe))
                if (section.Name == name)
                    return section;
            return null;
        }

        static int ImageBase(byte[] exe)
        {
            return BitConverter.ToInt32(exe, BitConverter.ToInt32(exe, 0x3C) + 24 + 28);
        }

        static int OffsetToVa(byte[] exe, int offset)
        {
            foreach (Section section in Sections(exe))
                if (offset >= section.RawPointer && offset < section.RawPointer + section.RawSize)
                    return ImageBase(exe) + section.Rva + (offset - section.RawPointer);
            return -1;
        }

        static void WriteInt(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)value;
            buffer[at + 1] = (byte)(value >> 8);
            buffer[at + 2] = (byte)(value >> 16);
            buffer[at + 3] = (byte)(value >> 24);
        }

        static int Align(int value, int alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }

        // The section for the moved names (read only data), added at the end of the exe the first time
        static Section EnsureStringSection(ref byte[] exe)
        {
            Section have = FindSection(exe, StringSection);
            if (have != null)
                return have;

            int pe = BitConverter.ToInt32(exe, 0x3C);
            int count = BitConverter.ToInt16(exe, pe + 6);
            int optional = pe + 24;
            int first = optional + BitConverter.ToInt16(exe, pe + 20);
            int sectionAlignment = BitConverter.ToInt32(exe, optional + 32);
            int fileAlignment = BitConverter.ToInt32(exe, optional + 36);
            List<Section> sections = Sections(exe);
            Section last = sections[sections.Count - 1];
            if (exe.Length > last.RawPointer + last.RawSize)
                throw new InvalidDataException("cm3.exe has extra data after its last section, so a longer name can't be added.");

            int header = first + count * 40;
            if (header + 40 > BitConverter.ToInt32(exe, optional + 60))
                throw new InvalidDataException("There is no room for another section header in cm3.exe, so a longer name can't be added.");
            for (int i = 0; i < 40; i++)
                if (exe[header + i] != 0)
                    throw new InvalidDataException("There is no room for another section header in cm3.exe, so a longer name can't be added.");

            int rva = Align(last.Rva + Math.Max(last.VirtualSize, last.RawSize), sectionAlignment);
            int raw = Align(exe.Length, fileAlignment);
            int rawSize = Align(StringSectionSize, fileAlignment);
            byte[] result = new byte[raw + rawSize];
            Array.Copy(exe, result, exe.Length);
            for (int i = 0; i < StringSection.Length; i++)
                result[header + i] = (byte)StringSection[i];
            WriteInt(result, header + 8, StringSectionSize);        // VirtualSize
            WriteInt(result, header + 12, rva);                     // VirtualAddress
            WriteInt(result, header + 16, rawSize);                 // SizeOfRawData
            WriteInt(result, header + 20, raw);                     // PointerToRawData
            WriteInt(result, header + 36, 0x40000040);              // initialised data | read
            result[pe + 6] = (byte)(count + 1);
            result[pe + 7] = 0;
            WriteInt(result, optional + 56, rva + Align(StringSectionSize, sectionAlignment));      // SizeOfImage
            exe = result;
            return FindSection(exe, StringSection);
        }

        // Puts the name in the exe's own section and points the place that uses the old one at it
        static void Relocate(ref byte[] exe, GameCompetition c, byte[] name)
        {
            int oldVa = OffsetToVa(exe, c.ExeOffset);
            if (oldVa < 0)
                throw new InvalidDataException("Couldn't work out where \"" + c.ExeName + "\" is in cm3.exe.");

            int headers = BitConverter.ToInt32(exe, BitConverter.ToInt32(exe, 0x3C) + 24 + 60);
            List<int> uses = new List<int>();
            for (int at = headers; at + 4 <= exe.Length; at++)
                if (BitConverter.ToInt32(exe, at) == oldVa)
                    uses.Add(at);
            if (uses.Count == 0 || uses.Count > 4)
                throw new InvalidDataException("Couldn't find where cm3.exe uses \"" + c.ExeName + "\" (found " + uses.Count + " places), so it can't be given a longer name.");

            Section section = EnsureStringSection(ref exe);
            int used = section.RawSize;
            while (used > 0 && exe[section.RawPointer + used - 1] == 0)
                used--;
            int at4 = Align(used, 4);
            if (at4 + name.Length + 1 > section.RawSize)
                throw new InvalidDataException("There is no room left in cm3.exe for more longer names.");

            // the old name goes if it was one of ours (so there is only one copy of a name)
            if (c.ExeOffset >= section.RawPointer && c.ExeOffset < section.RawPointer + section.RawSize)
                Array.Clear(exe, c.ExeOffset, Latin.GetBytes(c.ExeName).Length + 1);

            Array.Copy(name, 0, exe, section.RawPointer + at4, name.Length);
            exe[section.RawPointer + at4 + name.Length] = 0;
            int newVa = ImageBase(exe) + section.Rva + at4;
            foreach (int use in uses)
                WriteInt(exe, use, newVa);
        }

        static bool Matches(byte[] buffer, int at, byte[] part)
        {
            for (int i = 0; i < part.Length; i++)
                if (buffer[at + i] != part[i])
                    return false;
            return true;
        }

        static string ReadString(byte[] buffer, int at, int length)
        {
            int end = at;
            while (end < at + length && buffer[end] != 0)
                end++;
            return Latin.GetString(buffer, at, end - at);
        }

        static void WriteString(byte[] buffer, int at, int length, string text)
        {
            byte[] bytes = Latin.GetBytes(text);
            Array.Clear(buffer, at, length);
            Array.Copy(bytes, 0, buffer, at, bytes.Length);
        }

        // Why a name can't be used, or null when it can
        public static string Problem(GameCompetition c, string name, string shortName, IEnumerable<GameCompetition> all, Dictionary<int, GameCompetitionChange> pending)
        {
            name = name.Trim();
            shortName = shortName.Trim();
            if (!c.Renameable)
                return "This competition can't be renamed (the game doesn't keep its name in a place this tool can change).";
            if (name.Length == 0 || shortName.Length == 0)
                return "The names can't be empty.";
            if (name.Length > MaxName)
                return "The name can be at most " + MaxName + " characters.";
            if (shortName.Length > MaxShort)
                return "The short name can be at most " + MaxShort + " characters.";
            if (!string.Equals(Latin.GetString(Latin.GetBytes(name)), name, StringComparison.Ordinal) || name.IndexOf('\0') >= 0 ||
                !string.Equals(Latin.GetString(Latin.GetBytes(shortName)), shortName, StringComparison.Ordinal) || shortName.IndexOf('\0') >= 0)
                return "Use only letters the game can show (Western European characters).";
            foreach (GameCompetition other in all)
            {
                if (other.Id == c.Id)
                    continue;
                GameCompetitionChange change;
                string otherName = pending.TryGetValue(other.Id, out change) ? change.Name : other.Name;
                if (string.Equals(otherName, name, StringComparison.OrdinalIgnoreCase))
                    return "Another competition is already called \"" + name + "\".";
            }
            return null;
        }

        // Changes cm3.exe, the tables and the language files and the picture list in the game's folder. Returns what was done.
        public static string Apply(string exe, List<GameCompetitionChange> changes)
        {
            string folder = Path.GetDirectoryName(exe);
            string data = DataFolder(exe);
            StringBuilder report = new StringBuilder();

            // cm3.exe
            byte[] exeBytes = File.ReadAllBytes(exe);
            foreach (GameCompetitionChange change in changes)
            {
                GameCompetition c = change.Competition;
                if (!c.Renameable)
                    throw new InvalidDataException("\"" + c.Name + "\" can't be renamed.");
                byte[] oldName = Latin.GetBytes(c.ExeName);
                if (!Matches(exeBytes, c.ExeOffset, oldName) || exeBytes[c.ExeOffset + oldName.Length] != 0)
                    throw new InvalidDataException("cm3.exe has changed since the competitions were read; please try again.");
                string why = Problem(c, change.Name, change.Short, new GameCompetition[0], new Dictionary<int, GameCompetitionChange>());
                if (why != null)
                    throw new ArgumentException(why);
                int room = oldName.Length;
                while (c.ExeOffset + room < exeBytes.Length && exeBytes[c.ExeOffset + room] == 0)
                    room++;
                byte[] name = Latin.GetBytes(change.Name.Trim());
                if (name.Length >= room)
                    Relocate(ref exeBytes, c, name);        // doesn't fit where the old one is
                else
                {
                    Array.Clear(exeBytes, c.ExeOffset, room);
                    Array.Copy(name, 0, exeBytes, c.ExeOffset, name.Length);
                }
            }

            // the exe first: a game that is running has it locked, and then nothing else has been changed yet
            WriteGameFile(exe, exeBytes);

            foreach (string table in new string[] { "club_comp.dat", "nation_comp.dat" })
            {
                string file = Path.Combine(data, table);
                if (!File.Exists(file))
                    continue;
                byte[] bytes = File.ReadAllBytes(file);
                bool changed = false;
                for (int at = 0; at + RecordSize <= bytes.Length; at += RecordSize)
                {
                    int id = BitConverter.ToInt32(bytes, at);
                    foreach (GameCompetitionChange change in changes)
                    {
                        if (change.Competition.Id != id || change.Competition.National != (table == "nation_comp.dat"))
                            continue;
                        WriteString(bytes, at + NameOffset, NameLength, change.Name.Trim());
                        WriteString(bytes, at + ShortOffset, ShortLength, change.Short.Trim());
                        changed = true;
                    }
                }
                if (changed)
                    WriteGameFile(file, bytes);
            }
            report.Append("Updated the competition tables");

            int languages = 0;
            if (Directory.Exists(data))
            {
                foreach (string file in Directory.GetFiles(data, "*.lng"))
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    bool changed = false;
                    foreach (GameCompetitionChange change in changes)
                        changed |= PatchPair(bytes, change.Competition.Name, change.Competition.Short, change.Name.Trim(), change.Short.Trim());
                    if (changed)
                    {
                        WriteGameFile(file, bytes);
                        languages++;
                    }
                }
            }
            report.Append(languages > 0 ? ", " + languages + " language file" + (languages == 1 ? "" : "s") : "");

            string pictures = Path.Combine(Path.Combine(folder, "Pictures"), "pics.cfg");
            if (File.Exists(pictures))
            {
                string text = Latin.GetString(File.ReadAllBytes(pictures));
                bool changed = false;
                foreach (GameCompetitionChange change in changes)
                {
                    string before = "\n" + change.Competition.Name + ":";
                    string after = "\n" + change.Name.Trim() + ":";
                    string padded = "\n" + text;
                    int at = padded.IndexOf(before, StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        padded = padded.Substring(0, at) + after + padded.Substring(at + before.Length);
                        text = padded.Substring(1);
                        changed = true;
                    }
                }
                if (changed)
                {
                    WriteGameFile(pictures, Latin.GetBytes(text));
                    report.Append(", the picture list");
                }
            }

            report.Append(" and cm3.exe.");
            return report.ToString();
        }

        // Writes a file of the game; the first time a copy of the file is kept as <name>.orig
        static void WriteGameFile(string file, byte[] bytes)
        {
            FileAttributes attributes = File.GetAttributes(file);
            string orig = file + ".orig";
            if (!file.EndsWith("cm3.exe", StringComparison.OrdinalIgnoreCase) && !File.Exists(orig))
                File.Copy(file, orig);
            File.SetAttributes(file, FileAttributes.Normal);
            File.WriteAllBytes(file, bytes);
            File.SetAttributes(file, attributes);
        }

        // The language files (and the saved games' copies) have the competitions' names as a table of 52 byte name + 24 byte short name
        // pairs. Finds the pair of a competition and changes it, when it is there exactly once. Returns whether it changed.
        public static bool PatchPair(byte[] data, string oldName, string oldShort, string newName, string newShort)
        {
            byte[] pattern = new byte[NameLength + ShortLength];
            WriteString(pattern, 0, NameLength, oldName);
            WriteString(pattern, NameLength, ShortLength, oldShort);
            int found = -1;
            for (int at = 0; at + pattern.Length <= data.Length; at++)
            {
                if (data[at] != pattern[0] || !Matches(data, at, pattern))
                    continue;
                if (found >= 0)
                    return false;                   // the same pair twice (like the Friendly cups): can't tell which one
                found = at;
            }
            if (found < 0)
                return false;
            WriteString(data, found, NameLength, newName);
            WriteString(data, found + NameLength, ShortLength, newShort);
            return true;
        }
    }
}

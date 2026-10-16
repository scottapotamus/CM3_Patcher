using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CM3SaveGameEditor
{
    // A competition of the save: a league or a cup (of clubs) or a national team competition
    public class Competition
    {
        public int Id;
        public bool National;           // from nation_comp.dat (the national teams' competitions) rather than club_comp.dat
        public string Name;
        public string ShortName;
        public string Nation;           // the nation it belongs to, if any
        public bool HasFile;            // there is a "<name>_<id>.tmp" sub-file for it in the save
    }

    // The things of a competition the editor can change
    public class CompetitionEdit
    {
        public string Name;
        public string ShortName;

        public bool SameAs(Competition competition)
        {
            return Name == competition.Name && ShortName == competition.ShortName;
        }
    }

    public partial class SaveGameFile
    {
        // club_comp.dat (the clubs' competitions) and nation_comp.dat (the national teams') have 107 byte records: +0 id, +4 name
        // (52 bytes), +56 short name (24 bytes), +93 the nation it belongs to (-1 = none). Each competition has a sub-file in the
        // save called "<name>_<id>.tmp" (the game builds that name when it opens it), so renaming a competition renames that
        // sub-file as well. Where the prize money of a competition comes from isn't known: it isn't stored in the save.
        const int CompetitionRecordSize = 107;
        const int CompetitionNameOffset = 4;
        const int CompetitionNameLength = 52;
        const int CompetitionShortOffset = 56;
        const int CompetitionShortLength = 24;
        const int CompetitionNationOffset = 93;

        public const int MaxCompetitionName = 50;
        public const int MaxCompetitionShortName = 24;

        // The sub-files that are renamed when the file is written (old entry -> new name)
        Dictionary<Entry, string> pendingRenames = new Dictionary<Entry, string>();

        static string TmpName(string name, int id)
        {
            return name + "_" + id + ".tmp";
        }

        Entry FindOrNull(string name)
        {
            foreach (Entry entry in entries)
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            return null;
        }

        public List<Competition> Competitions()
        {
            byte[] nations = Decode(Find("nation.dat"));
            Dictionary<int, string> nationNames = NationNames(nations);
            List<Competition> list = new List<Competition>();
            foreach (string table in new string[] { "club_comp.dat", "nation_comp.dat" })
            {
                byte[] data = Decode(Find(table));
                if (data.Length % CompetitionRecordSize != 0)
                    throw new InvalidDataException(table + " isn't in the format this editor knows.");
                for (int at = 0; at < data.Length; at += CompetitionRecordSize)
                {
                    Competition competition = new Competition();
                    competition.Id = BitConverter.ToInt32(data, at);
                    competition.National = table == "nation_comp.dat";
                    competition.Name = ReadString(data, at + CompetitionNameOffset, CompetitionNameLength);
                    competition.ShortName = ReadString(data, at + CompetitionShortOffset, CompetitionShortLength);
                    string nation;
                    competition.Nation = nationNames.TryGetValue(BitConverter.ToInt32(data, at + CompetitionNationOffset), out nation) ? nation : "";
                    competition.HasFile = FindOrNull(TmpName(competition.Name, competition.Id)) != null;
                    list.Add(competition);
                }
            }
            return list;
        }

        // Renames competitions (by id). The changed sub-files go into "replaced", the renamed sub-files are renamed when the
        // file is written. Returns how many competitions were changed.

        public int ReplaceCompetitions(Dictionary<int, CompetitionEdit> edits, Dictionary<Entry, byte[]> replaced)
        {
            int changed = 0;
            foreach (string table in new string[] { "club_comp.dat", "nation_comp.dat" })
            {
                Entry entry = Find(table);
                byte[] data = Edit(entry, replaced);
                if (data.Length % CompetitionRecordSize != 0)
                    throw new InvalidDataException(table + " isn't in the format this editor knows.");
                bool tableChanged = false;
                for (int at = 0; at < data.Length; at += CompetitionRecordSize)
                {
                    int id = BitConverter.ToInt32(data, at);
                    CompetitionEdit edit;
                    if (!edits.TryGetValue(id, out edit))
                        continue;
                    if (edit.Name.Trim().Length == 0 || edit.ShortName.Trim().Length == 0)
                        throw new ArgumentException("A competition needs a name and a short name.");
                    if (edit.Name.Length > MaxCompetitionName || edit.ShortName.Length > MaxCompetitionShortName)
                        throw new ArgumentException("\"" + edit.Name + "\" is too long.");

                    string oldName = ReadString(data, at + CompetitionNameOffset, CompetitionNameLength);
                    string oldShort = ReadString(data, at + CompetitionShortOffset, CompetitionShortLength);
                    if (oldName == edit.Name && oldShort == edit.ShortName)
                        continue;

                    if (oldName != edit.Name)
                    {
                        Entry tmp = FindOrNull(TmpName(oldName, id));
                        if (tmp != null)
                        {
                            if (FindOrNull(TmpName(edit.Name, id)) != null)
                                throw new InvalidDataException("There is already a competition called \"" + edit.Name + "\" with that number.");
                            pendingRenames[tmp] = TmpName(edit.Name, id);
                            RenameInsideSubFile(tmp, TmpName(oldName, id), TmpName(edit.Name, id), replaced);
                        }
                        WriteString(data, at + CompetitionNameOffset, CompetitionNameLength, edit.Name);
                    }
                    RenameInLanguageFiles(oldName, oldShort, edit, replaced);
                    if (oldShort != edit.ShortName)
                        WriteString(data, at + CompetitionShortOffset, CompetitionShortLength, edit.ShortName);
                    tableChanged = true;
                    changed++;
                }
                if (tableChanged)
                    replaced[entry] = RunLength.Encode(data);
            }
            return changed;
        }

        // The saved game has the language files as well (a table of name and short name pairs the game shows the names from)
        void RenameInLanguageFiles(string oldName, string oldShort, CompetitionEdit edit, Dictionary<Entry, byte[]> replaced)
        {
            foreach (Entry entry in entries)
            {
                if (!entry.Name.EndsWith(".lng", StringComparison.OrdinalIgnoreCase))
                    continue;
                byte[] language = Edit(entry, replaced);
                if (CompetitionRenamer.PatchPair(language, oldName, oldShort, edit.Name, edit.ShortName))
                    replaced[entry] = RunLength.Encode(language);
            }
        }

        // A competition's sub-file starts with an 80 byte block that is the competition object's file name buffer (the name,
        // a zero, then whatever else was in the buffer), which the game reads back as it is and which has to match the name it
        // opened the file by. The data follows after exactly those 80 bytes, so the block keeps its size: only the name in it
        // changes. (Changing the length of the block moves all the data and the game stops with "comp_stats.CPP 5382".)
        const int SubFileNameBlock = 80;

        void RenameInsideSubFile(Entry entry, string oldName, string newName, Dictionary<Entry, byte[]> replaced)
        {
            Encoding latin = Encoding.GetEncoding(1252);
            byte[] body = Edit(entry, replaced);
            byte[] old = latin.GetBytes(oldName);
            byte[] name = latin.GetBytes(newName);
            bool matches = body.Length > SubFileNameBlock && old.Length < SubFileNameBlock && body[old.Length] == 0;
            for (int i = 0; matches && i < old.Length; i++)
                matches = body[i] == old[i];
            if (!matches)
                throw new InvalidDataException("The competition's data (" + oldName + ") doesn't start with its own name, so it can't be renamed.");
            if (name.Length >= SubFileNameBlock)
                throw new InvalidDataException("The name of the competition's sub-file (" + newName + ") is too long.");

            byte[] renamed = (byte[])body.Clone();
            Array.Clear(renamed, 0, old.Length + 1);                    // the old name and its zero
            Array.Copy(name, 0, renamed, 0, name.Length);               // the new name, then a zero (cleared above, or left over from the buffer)
            renamed[name.Length] = 0;
            replaced[entry] = RunLength.Encode(renamed);
        }

        // Writes the new names of renamed sub-files into the directory of a file that was just built
        void ApplyRenames(byte[] result)
        {
            foreach (KeyValuePair<Entry, string> rename in pendingRenames)
            {
                byte[] bytes = Encoding.GetEncoding(1252).GetBytes(rename.Value);
                if (bytes.Length >= NameLength)
                    throw new InvalidDataException("The name of a sub-file is too long.");
                int at = rename.Key.DirectoryPosition + NameOffset;
                Array.Clear(result, at, NameLength);
                Array.Copy(bytes, 0, result, at, bytes.Length);
            }
            pendingRenames.Clear();
        }

        // The order the sub-files are written in (and listed in the directory). The game finds the .tmp sub-files by searching
        // for their names in the directory, which is kept in alphabetical order (ignoring case: index.cpp), so after a rename
        // they have to be put back in order or the game stops with "index.cpp 1635" when it loads the save. The data of the
        // sub-files follows the directory's order (the end of one is the start of the next), so the data moves with them.
        // The .dat entries before the .tmp ones stay where they are.
        List<Entry> OutputOrder()
        {
            List<Entry> order = new List<Entry>(entries);
            if (pendingRenames.Count == 0)
                return order;
            int first = -1;
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    first = i;
                    break;
                }
            }
            if (first < 0)
                return order;

            List<KeyValuePair<string, Entry>> tail = new List<KeyValuePair<string, Entry>>();
            for (int i = first; i < order.Count; i++)
            {
                string name;
                if (!pendingRenames.TryGetValue(order[i], out name))
                    name = order[i].Name;
                tail.Add(new KeyValuePair<string, Entry>(name.ToLowerInvariant(), order[i]));
            }
            // a stable sort: equal names keep their order
            Dictionary<Entry, int> position = new Dictionary<Entry, int>();
            for (int i = 0; i < tail.Count; i++)
                position[tail[i].Value] = i;
            tail.Sort(delegate(KeyValuePair<string, Entry> x, KeyValuePair<string, Entry> y)
            {
                int c = string.CompareOrdinal(x.Key, y.Key);
                return c != 0 ? c : position[x.Value].CompareTo(position[y.Value]);
            });
            for (int i = 0; i < tail.Count; i++)
                order[first + i] = tail[i].Value;
            return order;
        }
    }
}

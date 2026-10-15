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
                        }
                        WriteString(data, at + CompetitionNameOffset, CompetitionNameLength, edit.Name);
                    }
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
    }
}

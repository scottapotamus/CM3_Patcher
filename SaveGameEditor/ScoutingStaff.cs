using System;
using System.Collections.Generic;
using System.IO;

namespace CM3SaveGameEditor
{
    // Where the numbers about the staff (managers, coaches, scouts, physios, directors...) come from.
    //
    // nonplayer.dat has a 68 byte record per member of staff: +0 its number (the record number), +4 current ability (u16),
    // +6 potential ability (u16), then the staff attributes as plain bytes (1-20). The staff.dat record points to it with the
    // number at +105; +61 of the staff record is the job. The rest (adaptability, determination, the hidden personality
    // numbers, date of birth, nationality, club, wage) is in the staff record, as for players.
    public static class StaffFields
    {
        class Source
        {
            public string Name;
            public bool InStaffRecord;
            public int Offset;
            public Source(string name, bool inStaffRecord, int offset) { Name = name; InStaffRecord = inStaffRecord; Offset = offset; }
        }

        // The game's order (function at 0x4CA9C0 in cm3.exe)
        static readonly Source[] Visible = {
            new Source("Adaptability", true, 86), new Source("Coaching Goalkeepers", false, 17),
            new Source("Coaching Outfield Players", false, 16), new Source("Determination", true, 88),
            new Source("Judging Player Ability", false, 23), new Source("Judging Player Potential", false, 24),
            new Source("Level of Discipline", false, 20), new Source("Man Management", false, 25),
            new Source("Motivating", false, 27), new Source("Physiotherapy", false, 30),
            new Source("Tactical Knowledge", false, 33), new Source("Working With Youngsters", false, 34)
        };

        static readonly Source[] HiddenSources = {
            new Source("Ambition", true, 87), new Source("Loyalty", true, 89), new Source("Pressure", true, 90),
            new Source("Professionalism", true, 91), new Source("Sportsmanship", true, 92), new Source("Temperament", true, 93)
        };

        // staff record +61 (function at 0x4C3B40 in cm3.exe)
        public static readonly string[] Jobs = {
            "Unemployed", "Chairman", "Managing Director", "General Manager", "Director of Football", "Manager",
            "Assistant Manager", "Reserve Manager", "Coach", "Scout", "Physio", "Player", "Player/Manager",
            "Player/Assistant Manager", "?", "Player/Coach"
        };

        public static readonly ScoutFieldSet Set;

        static StaffFields()
        {
            List<ScoutField> list = new List<ScoutField>();
            list.Add(new ScoutField { Name = "Current ability", Min = 0, Max = 200, Hidden = true });
            list.Add(new ScoutField { Name = "Potential ability", Min = 0, Max = 200, Hidden = true });
            list.Add(new ScoutField { Name = "Age", Min = 14, Max = 90 });
            list.Add(new ScoutField { Name = "Wages per month", Min = 0, Max = 2000000000 });
            foreach (Source source in Visible)
                list.Add(new ScoutField { Name = source.Name, Min = 1, Max = 20 });
            int firstHidden = list.Count;
            foreach (Source source in HiddenSources)
                list.Add(new ScoutField { Name = source.Name, Min = 0, Max = 20, Hidden = true });
            Set = new ScoutFieldSet
            {
                All = list.ToArray(), FirstVisible = 4, VisibleCount = Visible.Length, FirstHidden = firstHidden,
                HiddenCount = HiddenSources.Length, FirstPosition = list.Count, PositionCount = 0
            };
        }

        // Writes the numbers of a member of staff into his records; returns whether anything was different
        public static bool Write(int[] values, byte[] nonPlayer, int nonPlayerAt, byte[] staff, int staffAt)
        {
            int[] now = new int[Set.All.Length];
            Read(now, nonPlayer, nonPlayerAt, staff, staffAt);
            bool changed = false;
            for (int f = 0; f < 2; f++)
            {
                if (values[f] < 0 || values[f] > 200)
                    throw new ArgumentException("The abilities must be between 0 and 200.");
                if (values[f] != now[f])
                {
                    nonPlayer[nonPlayerAt + 4 + 2 * f] = (byte)values[f];
                    nonPlayer[nonPlayerAt + 5 + 2 * f] = (byte)(values[f] >> 8);
                    changed = true;
                }
            }
            for (int i = 0; i < Visible.Length + HiddenSources.Length; i++)
            {
                int f = Set.FirstVisible + i;
                Source source = i < Visible.Length ? Visible[i] : HiddenSources[i - Visible.Length];
                if (values[f] < (i < Visible.Length ? 1 : 0) || values[f] > 20)
                    throw new ArgumentException(source.Name + " must be between " + (i < Visible.Length ? 1 : 0) + " and 20.");
                if (values[f] == now[f])
                    continue;
                if (source.InStaffRecord)
                    staff[staffAt + source.Offset] = (byte)values[f];
                else
                    nonPlayer[nonPlayerAt + source.Offset] = (byte)values[f];
                changed = true;
            }
            return changed;
        }

        public static void Read(int[] values, byte[] nonPlayer, int nonPlayerAt, byte[] staff, int staffAt)
        {
            values[ScoutFieldSet.CurrentAbility] = BitConverter.ToUInt16(nonPlayer, nonPlayerAt + 4);
            values[ScoutFieldSet.PotentialAbility] = BitConverter.ToUInt16(nonPlayer, nonPlayerAt + 6);
            for (int i = 0; i < Visible.Length; i++)
                values[Set.FirstVisible + i] = Visible[i].InStaffRecord ? staff[staffAt + Visible[i].Offset] : nonPlayer[nonPlayerAt + Visible[i].Offset];
            for (int i = 0; i < HiddenSources.Length; i++)
                values[Set.FirstHidden + i] = staff[staffAt + HiddenSources[i].Offset];
        }
    }

    public partial class SaveGameFile
    {
        const int NonPlayerRecordSize = 68;
        const int StaffNonPlayerOffset = 105;
        const int StaffJobOffset = 61;

        // Changes the numbers of players and of staff (by staff id; the values are indexed like the field sets). The changed
        // sub-files (compressed) go into "replaced". Returns how many people were changed.
        public int ReplaceAttributes(Dictionary<int, int[]> playerEdits, Dictionary<int, int[]> staffEdits, Dictionary<Entry, byte[]> replaced)
        {
            Entry staffEntry = Find("staff.dat");
            byte[] staff = Edit(staffEntry, replaced);
            if (staff.Length % StaffRecordSize != 0)
                throw new InvalidDataException("staff.dat isn't in the format this editor knows.");
            int changed = 0;
            bool staffChanged = false;

            if (playerEdits.Count > 0)
            {
                Entry entry = Find("player.dat");
                byte[] players = Edit(entry, replaced);
                if (players.Length % PlayerRecordSize != 0)
                    throw new InvalidDataException("player.dat isn't in the format this editor knows.");
                bool playersChanged = false;
                foreach (KeyValuePair<int, int[]> pair in playerEdits)
                {
                    int staffAt = pair.Key * StaffRecordSize;
                    if (pair.Key < 0 || staffAt + StaffRecordSize > staff.Length)
                        throw new InvalidDataException("There is no staff number " + pair.Key + ".");
                    int playerId = BitConverter.ToInt32(staff, staffAt + StaffPlayerOffset);
                    if (playerId < 0 || (playerId + 1) * PlayerRecordSize > players.Length)
                        throw new InvalidDataException("Staff number " + pair.Key + " isn't a player.");
                    if (pair.Value.Length != ScoutFields.Count)
                        throw new ArgumentException("Bad edit.");
                    if (ScoutFields.Write(pair.Value, players, playerId * PlayerRecordSize, staff, staffAt))
                    {
                        changed++;
                        playersChanged = staffChanged = true;
                    }
                }
                if (playersChanged)
                    replaced[entry] = RunLength.Encode(players);
            }

            if (staffEdits.Count > 0)
            {
                Entry entry = Find("nonplayer.dat");
                byte[] nonPlayers = Edit(entry, replaced);
                if (nonPlayers.Length % NonPlayerRecordSize != 0)
                    throw new InvalidDataException("nonplayer.dat isn't in the format this editor knows.");
                bool nonPlayersChanged = false;
                foreach (KeyValuePair<int, int[]> pair in staffEdits)
                {
                    int staffAt = pair.Key * StaffRecordSize;
                    if (pair.Key < 0 || staffAt + StaffRecordSize > staff.Length)
                        throw new InvalidDataException("There is no staff number " + pair.Key + ".");
                    int record = BitConverter.ToInt32(staff, staffAt + StaffNonPlayerOffset);
                    if (record < 0 || (record + 1) * NonPlayerRecordSize > nonPlayers.Length)
                        throw new InvalidDataException("Staff number " + pair.Key + " has no staff attributes.");
                    if (pair.Value.Length != StaffFields.Set.All.Length)
                        throw new ArgumentException("Bad edit.");
                    if (StaffFields.Write(pair.Value, nonPlayers, record * NonPlayerRecordSize, staff, staffAt))
                    {
                        changed++;
                        nonPlayersChanged = staffChanged = true;
                    }
                }
                if (nonPlayersChanged)
                    replaced[entry] = RunLength.Encode(nonPlayers);
            }

            if (staffChanged)
                replaced[staffEntry] = RunLength.Encode(staff);
            return changed;
        }

        // Every member of staff of the save that has staff attributes (managers, coaches, scouts, physios, directors...)
        public List<ScoutPlayer> StaffMembers()
        {
            byte[] staff = Decode(Find("staff.dat"));
            byte[] nonPlayers = Decode(Find("nonplayer.dat"));
            byte[] firstNames = Decode(Find("first_names.dat"));
            byte[] secondNames = Decode(Find("second_names.dat"));
            byte[] commonNames = Decode(Find("common_names.dat"));
            byte[] nations = Decode(Find("nation.dat"));
            if (staff.Length % StaffRecordSize != 0 || nonPlayers.Length % NonPlayerRecordSize != 0 || nations.Length % NationRecordSize != 0)
                throw new InvalidDataException("staff.dat, nonplayer.dat or nation.dat isn't in the format this editor knows.");

            DateTime today = GameDate();
            Dictionary<int, string> clubNames = new Dictionary<int, string>();
            foreach (Club club in Clubs())
                clubNames[club.Id] = club.Name;
            Dictionary<int, string> nationNames = NationNames(nations);

            int count = nonPlayers.Length / NonPlayerRecordSize;
            List<ScoutPlayer> list = new List<ScoutPlayer>();
            for (int i = 0; i < staff.Length / StaffRecordSize; i++)
            {
                int staffAt = i * StaffRecordSize;
                int record = BitConverter.ToInt32(staff, staffAt + StaffNonPlayerOffset);
                if (record < 0 || record >= count || BitConverter.ToInt32(nonPlayers, record * NonPlayerRecordSize) != record || staff[staffAt + StaffJobOffset] == StaffRetiredJob)
                    continue;

                ScoutPlayer member = new ScoutPlayer();
                member.StaffId = BitConverter.ToInt32(staff, staffAt);
                member.Name = StaffName(staff, staffAt, firstNames, secondNames, commonNames);
                member.Club = ClubName(clubNames, BitConverter.ToInt32(staff, staffAt + StaffClubOffset));
                string nation;
                member.Nation = nationNames.TryGetValue(BitConverter.ToUInt16(staff, staffAt + StaffNationOffset), out nation) ? nation : "";
                int job = staff[staffAt + StaffJobOffset];
                member.Positions = job < StaffFields.Jobs.Length ? StaffFields.Jobs[job] : "job " + job;

                member.Values = new int[StaffFields.Set.All.Length];
                StaffFields.Read(member.Values, nonPlayers, record * NonPlayerRecordSize, staff, staffAt);
                member.Values[ScoutFieldSet.Age] = AgeOf(staff, staffAt, today);
                member.Values[ScoutFieldSet.Wage] = BitConverter.ToInt32(staff, staffAt + StaffWageOffset) * 4;
                list.Add(member);
            }
            return list;
        }
    }
}

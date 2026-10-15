using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CM3SaveGameEditor
{
    // A new player or member of staff to put into a saved game
    public class NewPerson
    {
        public bool IsStaff;
        public string FirstName;
        public string Surname;
        public int NationId;
        public string NationName;
        public DateTime Born;
        public int Job = 11;                // 11 = player; for staff the number of the job (see StaffFields.Jobs)
        public int ClubId = -1;             // -1 = no club (a free agent)
        public string ClubName = "";
        public int Wage;                    // per month, when he has a club
        public DateTime Expires;            // when his contract ends
        public int CurrentAbility = 100;
        public int PotentialAbility = 120;
        public int Position;                // a player's main position: an index into SaveGameFile.PositionPresets
        public int Attribute = 10;          // what every attribute starts as (1-20), when Values isn't given
        public int[] Values;                // all his numbers (indexed like ScoutFields / StaffFields.Set); if given, they are used

        public string Name { get { return FirstName + " " + Surname; } }
    }

    public partial class SaveGameFile
    {
        const int StaffRetiredJob = 16;

        public class PositionPreset
        {
            public string Name;
            public int[] Ratings;           // goalkeeper, sweeper, defender, defensive midfielder, midfielder, attacking midfielder, striker, wing back, right, left, centre, free role
            public PositionPreset(string name, int[] ratings) { Name = name; Ratings = ratings; }
        }

        public static readonly PositionPreset[] PositionPresets = {
            new PositionPreset("Goalkeeper",            new int[] { 20, 1, 1, 1, 1, 1, 1, 1, 1, 1, 20, 1 }),
            new PositionPreset("Centre back",           new int[] { 1, 10, 20, 10, 1, 1, 1, 1, 1, 1, 20, 1 }),
            new PositionPreset("Right back",            new int[] { 1, 1, 20, 8, 1, 1, 1, 18, 20, 1, 15, 1 }),
            new PositionPreset("Left back",             new int[] { 1, 1, 20, 8, 1, 1, 1, 18, 1, 20, 15, 1 }),
            new PositionPreset("Defensive midfielder",  new int[] { 1, 1, 10, 20, 15, 5, 1, 1, 1, 1, 20, 1 }),
            new PositionPreset("Central midfielder",    new int[] { 1, 1, 1, 12, 20, 12, 1, 1, 1, 1, 20, 1 }),
            new PositionPreset("Right midfielder",      new int[] { 1, 1, 1, 1, 20, 15, 1, 5, 20, 1, 15, 1 }),
            new PositionPreset("Left midfielder",       new int[] { 1, 1, 1, 1, 20, 15, 1, 5, 1, 20, 15, 1 }),
            new PositionPreset("Attacking midfielder",  new int[] { 1, 1, 1, 1, 15, 20, 10, 1, 1, 1, 20, 15 }),
            new PositionPreset("Striker",               new int[] { 1, 1, 1, 1, 1, 10, 20, 1, 1, 1, 20, 10 })
        };

        // Staff jobs that can be given to a new member of staff
        public static readonly int[] StaffJobChoices = { 5, 6, 8, 9, 10 };

        // The numbers a new player starts with when none are given
        public static int[] DefaultPlayerValues(NewPerson person)
        {
            int[] values = new int[ScoutFields.Count];
            values[ScoutFieldSet.CurrentAbility] = person.CurrentAbility;
            values[ScoutFieldSet.PotentialAbility] = person.PotentialAbility;
            PositionPreset preset = PositionPresets[Math.Max(0, Math.Min(PositionPresets.Length - 1, person.Position))];
            for (int i = 0; i < preset.Ratings.Length; i++)
                values[ScoutFields.FirstPositionField + i] = preset.Ratings[i];
            for (int f = ScoutFields.FirstVisible; f < ScoutFields.FirstVisible + ScoutFields.VisibleCount; f++)
                values[f] = person.Attribute;
            for (int f = ScoutFields.FirstHidden; f < ScoutFields.FirstHidden + ScoutFields.HiddenCount; f++)
                values[f] = person.Attribute;
            return values;
        }

        public static int[] DefaultStaffValues(NewPerson person)
        {
            int[] values = new int[StaffFields.Set.All.Length];
            values[ScoutFieldSet.CurrentAbility] = person.CurrentAbility;
            values[ScoutFieldSet.PotentialAbility] = person.PotentialAbility;
            for (int f = StaffFields.Set.FirstVisible; f < values.Length; f++)
                values[f] = person.Attribute;
            return values;
        }

        static void Append(ref byte[] data, byte[] record)
        {
            byte[] bigger = new byte[data.Length + record.Length];
            Array.Copy(data, bigger, data.Length);
            Array.Copy(record, 0, bigger, data.Length, record.Length);
            data = bigger;
        }

        static int NonZero(byte[] data, int at, int length)
        {
            int count = 0;
            for (int i = 0; i < length; i++)
                if (data[at + i] != 0)
                    count++;
            return count;
        }

        // The id of a first name or surname in a name table (60 byte records: +0 the name (51 bytes), +51 the record number,
        // +55 and +59 two more numbers); a name that isn't there yet is added
        static int NameId(ref byte[] names, string name)
        {
            name = name.Trim();
            if (name.Length == 0 || Encoding.GetEncoding(1252).GetByteCount(name) > 50)
                throw new ArgumentException("A name must be between 1 and 50 characters.");
            for (int i = 0; i < names.Length / NameRecordSize; i++)
                if (string.Equals(ReadString(names, i * NameRecordSize, 51), name, StringComparison.OrdinalIgnoreCase))
                    return i;

            int id = names.Length / NameRecordSize;
            byte[] record = new byte[NameRecordSize];
            WriteString(record, 0, 51, name);
            WriteInt(record, 51, id);
            WriteInt(record, 55, -1);
            record[59] = 1;
            Append(ref names, record);
            return id;
        }

        static void WriteDate(byte[] buffer, int at, DateTime date)
        {
            WriteShort(buffer, at, (date - new DateTime(date.Year, 1, 1)).Days);
            WriteShort(buffer, at + 2, date.Year);
        }

        // Puts new players and staff into the save: records in staff.dat and player.dat (or nonplayer.dat), their names, a contract
        // and a place in the lists of their club if they have one. The changed sub-files (compressed) go into "replaced".
        // Returns how many people were added.
        public int AddPeople(List<NewPerson> people, Dictionary<Entry, byte[]> replaced)
        {
            Entry staffEntry = Find("staff.dat"), playerEntry = Find("player.dat"), nonPlayerEntry = Find("nonplayer.dat");
            Entry firstEntry = Find("first_names.dat"), secondEntry = Find("second_names.dat");
            Entry contractEntry = Find("contract.dat"), clubEntry = Find("club.dat");

            byte[] staff = Edit(staffEntry, replaced);
            byte[] players = Edit(playerEntry, replaced);
            byte[] nonPlayers = Edit(nonPlayerEntry, replaced);
            byte[] firstNames = Edit(firstEntry, replaced);
            byte[] secondNames = Edit(secondEntry, replaced);
            byte[] contracts = Edit(contractEntry, replaced);
            byte[] clubData = Edit(clubEntry, replaced);
            if (staff.Length % StaffRecordSize != 0 || players.Length % PlayerRecordSize != 0 || nonPlayers.Length % NonPlayerRecordSize != 0 ||
                firstNames.Length % NameRecordSize != 0 || secondNames.Length % NameRecordSize != 0 || clubData.Length % ClubRecordSize != 0)
                throw new InvalidDataException("A data file isn't in the format this editor knows.");

            DateTime today = GameDate();
            int staffCount = staff.Length / StaffRecordSize;

            // the plainest free agent of each kind is the pattern for the new records
            int playerTemplate = -1, staffTemplate = -1;
            int playerBest = int.MaxValue, staffBest = int.MaxValue;
            for (int i = 0; i < staffCount; i++)
            {
                int at = i * StaffRecordSize;
                if (BitConverter.ToInt32(staff, at + StaffClubOffset) >= 0)
                    continue;
                int job = staff[at + StaffJobOffset];
                int playerId = BitConverter.ToInt32(staff, at + StaffPlayerOffset);
                int nonPlayerId = BitConverter.ToInt32(staff, at + StaffNonPlayerOffset);
                int plainness = NonZero(staff, at, StaffRecordSize);
                if (job == 11 && playerId >= 0 && (playerId + 1) * PlayerRecordSize <= players.Length && BitConverter.ToInt32(players, playerId * PlayerRecordSize) == playerId && plainness < playerBest)
                {
                    playerBest = plainness;
                    playerTemplate = i;
                }
                if (job < 11 && playerId < 0 && nonPlayerId >= 0 && (nonPlayerId + 1) * NonPlayerRecordSize <= nonPlayers.Length && BitConverter.ToInt32(nonPlayers, nonPlayerId * NonPlayerRecordSize) == nonPlayerId && plainness < staffBest)
                {
                    staffBest = plainness;
                    staffTemplate = i;
                }
            }

            // a pattern for the contracts: a plain full time one
            int contractCount = ContractCount(contracts);
            int contractTemplate = -1;
            for (int i = 0; i < contractCount && contractTemplate < 0; i++)
            {
                int at = 4 + i * ContractRecordSize;
                bool plain = BitConverter.ToInt32(contracts, at) >= 0 && BitConverter.ToInt32(contracts, at + ContractFeeOffset) == 0 &&
                             contracts[at + ContractTypeOffset] == 2;
                for (int t = 0; t < ClauseTypes && plain; t++)
                    if (contracts[at + ContractFlagsOffset + t] != 0)
                        plain = false;
                for (int t = 0; t < BonusTypes && plain; t++)
                    if (BitConverter.ToInt32(contracts, at + ContractBonusOffset + 4 * t) != -1)
                        plain = false;
                if (plain)
                    contractTemplate = i;
            }

            bool contractsChanged = false, clubsChanged = false;
            int added = 0;
            foreach (NewPerson person in people)
            {
                if (person.Born >= today)
                    throw new ArgumentException(person.Name + " has to be born before the game's date (" + today.ToShortDateString() + ").");
                if (person.CurrentAbility < 0 || person.CurrentAbility > 200 || person.PotentialAbility < 0 || person.PotentialAbility > 200)
                    throw new ArgumentException("The abilities must be between 0 and 200.");
                if (person.Attribute < 1 || person.Attribute > 20)
                    throw new ArgumentException("The attributes must be between 1 and 20.");
                int template = person.IsStaff ? staffTemplate : playerTemplate;
                if (template < 0)
                    throw new InvalidDataException("This saved game has no free " + (person.IsStaff ? "member of staff" : "player") + " to use as a pattern.");
                if (person.IsStaff && (person.Job < 1 || person.Job > 10))
                    throw new ArgumentException("A member of staff needs a staff job.");
                if (person.ClubId >= 0)
                {
                    if ((person.ClubId + 1) * ClubRecordSize > clubData.Length || BitConverter.ToInt32(clubData, person.ClubId * ClubRecordSize) != person.ClubId)
                        throw new InvalidDataException("There is no club " + person.ClubId + ".");
                    if (person.Expires <= today)
                        throw new ArgumentException(person.Name + "'s contract has to end after the game's date.");
                }

                int job = person.IsStaff ? person.Job : 11;
                int staffId = staff.Length / StaffRecordSize;
                byte[] record = new byte[StaffRecordSize];
                Array.Copy(staff, template * StaffRecordSize, record, 0, StaffRecordSize);

                WriteInt(record, 0, staffId);
                WriteInt(record, 4, NameId(ref firstNames, person.FirstName));
                WriteInt(record, 8, NameId(ref secondNames, person.Surname));
                WriteInt(record, 12, 0);
                WriteDate(record, StaffBirthOffset, person.Born);
                WriteShort(record, StaffNationOffset, person.NationId);
                record[StaffJobOffset] = (byte)job;
                WriteInt(record, StaffClubOffset, person.ClubId);
                WriteInt(record, 101, staffId);

                // the hidden personality numbers start at what the attribute is
                for (int offset = 86; offset <= 93; offset++)
                    record[offset] = (byte)person.Attribute;

                if (person.ClubId >= 0)
                {
                    WriteDate(record, StaffExpiryOffset, person.Expires);
                    WriteInt(record, StaffWageOffset, (int)Math.Round(person.Wage / 4.0));
                }
                else
                {
                    WriteDate(record, StaffExpiryOffset, today.AddDays(-1));
                    WriteInt(record, StaffWageOffset, 0);
                }

                // his numbers
                if (!person.IsStaff)
                {
                    int playerId = players.Length / PlayerRecordSize;
                    byte[] playerRecord = new byte[PlayerRecordSize];
                    int templatePlayerId = BitConverter.ToInt32(staff, template * StaffRecordSize + StaffPlayerOffset);
                    Array.Copy(players, templatePlayerId * PlayerRecordSize, playerRecord, 0, PlayerRecordSize);
                    WriteInt(playerRecord, 0, playerId);
                    WriteInt(record, StaffPlayerOffset, playerId);
                    WriteInt(record, StaffNonPlayerOffset, -1);

                    int[] values = person.Values != null ? (int[])person.Values.Clone() : DefaultPlayerValues(person);
                    if (values.Length != ScoutFields.Count)
                        throw new ArgumentException("Bad numbers for " + person.Name + ".");
                    ScoutFields.Write(values, playerRecord, 0, record, 0);

                    Append(ref players, playerRecord);
                }
                else
                {
                    int nonPlayerId = nonPlayers.Length / NonPlayerRecordSize;
                    byte[] nonPlayerRecord = new byte[NonPlayerRecordSize];
                    int templateNonPlayerId = BitConverter.ToInt32(staff, template * StaffRecordSize + StaffNonPlayerOffset);
                    Array.Copy(nonPlayers, templateNonPlayerId * NonPlayerRecordSize, nonPlayerRecord, 0, NonPlayerRecordSize);
                    WriteInt(nonPlayerRecord, 0, nonPlayerId);
                    WriteInt(record, StaffPlayerOffset, -1);
                    WriteInt(record, StaffNonPlayerOffset, nonPlayerId);

                    int[] values = person.Values != null ? (int[])person.Values.Clone() : DefaultStaffValues(person);
                    if (values.Length != StaffFields.Set.All.Length)
                        throw new ArgumentException("Bad numbers for " + person.Name + ".");
                    StaffFields.Write(values, nonPlayerRecord, 0, record, 0);

                    Append(ref nonPlayers, nonPlayerRecord);
                }
                Append(ref staff, record);

                // a contract and a place in the club's lists
                if (person.ClubId >= 0)
                {
                    byte[] contract = new byte[ContractRecordSize];
                    int contractAt = 4 + (contractTemplate >= 0 ? contractTemplate : 0) * ContractRecordSize;
                    Array.Copy(contracts, contractAt, contract, 0, ContractRecordSize);
                    WriteInt(contract, 0, staffId);
                    WriteInt(contract, ContractClubOffset, person.ClubId);
                    WriteInt(contract, ContractWageOffset, (int)Math.Round(person.Wage / 4.0));
                    for (int t = 0; t < BonusTypes; t++)
                        WriteInt(contract, ContractBonusOffset + 4 * t, -1);
                    for (int t = 0; t < ClauseTypes; t++)
                        contract[ContractFlagsOffset + t] = 0;
                    WriteInt(contract, ContractFeeOffset, 0);
                    WriteDate(contract, 37, today);
                    WriteDate(contract, ContractExpiryOffset, person.Expires);
                    contract[ContractTypeOffset] = 2;

                    int count = ContractCount(contracts);
                    int split = 4 + count * ContractRecordSize;
                    byte[] merged = new byte[contracts.Length + ContractRecordSize];
                    Array.Copy(contracts, 0, merged, 0, split);
                    Array.Copy(contract, 0, merged, split, ContractRecordSize);
                    Array.Copy(contracts, split, merged, split + ContractRecordSize, contracts.Length - split);
                    WriteInt(merged, 0, count + 1);
                    contracts = merged;
                    contractsChanged = true;

                    MoveToClub(clubData, staffId, job, -1, person.ClubId, person.Name);
                    clubsChanged = true;
                }
                added++;
            }

            replaced[staffEntry] = RunLength.Encode(staff);
            replaced[playerEntry] = RunLength.Encode(players);
            replaced[nonPlayerEntry] = RunLength.Encode(nonPlayers);
            replaced[firstEntry] = RunLength.Encode(firstNames);
            replaced[secondEntry] = RunLength.Encode(secondNames);
            if (contractsChanged)
                replaced[contractEntry] = RunLength.Encode(contracts);
            if (clubsChanged)
                replaced[clubEntry] = RunLength.Encode(clubData);
            return added;
        }

        // Takes players and staff out of the game the way the game retires them: they leave their club (and its lists), their
        // contracts end, and their job becomes "retired" (they are still in the files, but are in no list of the game).
        // Returns how many were removed.
        public int RemovePeople(List<int> staffIds, Dictionary<Entry, byte[]> replaced)
        {
            Entry staffEntry = Find("staff.dat"), contractEntry = Find("contract.dat"), clubEntry = Find("club.dat");
            byte[] staff = Edit(staffEntry, replaced);
            byte[] contracts = Edit(contractEntry, replaced);
            byte[] clubData = Edit(clubEntry, replaced);
            if (staff.Length % StaffRecordSize != 0 || clubData.Length % ClubRecordSize != 0)
                throw new InvalidDataException("staff.dat or club.dat isn't in the format this editor knows.");
            int contractCount = ContractCount(contracts);

            int removed = 0;
            bool contractsChanged = false;
            foreach (int id in staffIds)
            {
                int at = id * StaffRecordSize;
                if (id < 0 || at + StaffRecordSize > staff.Length)
                    throw new InvalidDataException("There is no staff number " + id + ".");

                int club = BitConverter.ToInt32(staff, at + StaffClubOffset);
                if (club >= 0 && (club + 1) * ClubRecordSize <= clubData.Length)
                    for (int place = RosterStart; place <= RosterEnd; place += 4)
                        if (BitConverter.ToInt32(clubData, club * ClubRecordSize + place) == id)
                            WriteInt(clubData, club * ClubRecordSize + place, -1);

                for (int i = 0; i < contractCount; i++)
                {
                    int contractAt = 4 + i * ContractRecordSize;
                    if (BitConverter.ToInt32(contracts, contractAt) == id)
                    {
                        WriteInt(contracts, contractAt, -1);
                        contractsChanged = true;
                    }
                }

                WriteInt(staff, at + StaffClubOffset, -1);
                staff[at + StaffJobOffset] = StaffRetiredJob;
                removed++;
            }

            replaced[staffEntry] = RunLength.Encode(staff);
            replaced[clubEntry] = RunLength.Encode(clubData);
            if (contractsChanged)
                replaced[contractEntry] = RunLength.Encode(contracts);
            return removed;
        }

        // The nations a new person can be from
        public List<KeyValuePair<int, string>> NationList()
        {
            Dictionary<int, string> names = NationNames(Decode(Find("nation.dat")));
            List<KeyValuePair<int, string>> list = new List<KeyValuePair<int, string>>(names);
            list.Sort(delegate(KeyValuePair<int, string> a, KeyValuePair<int, string> b) { return string.Compare(a.Value, b.Value, StringComparison.CurrentCultureIgnoreCase); });
            return list;
        }
    }
}

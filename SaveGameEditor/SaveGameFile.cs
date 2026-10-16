using System;
using System.Collections.Generic;
using System.IO;

namespace CM3SaveGameEditor
{
    // A Championship Manager 3 saved game (.sav): a directory of sub-files (the .dat files and the per-competition /
    // fixture .tmp files) followed by their run-length compressed contents.
    //
    //   12 bytes  header: version (4), ?, number of entries
    //   entries   268 bytes each: offset (4), uncompressed size (4), name (256, zero padded), 4 more bytes
    //   ...       (some more data between the directory and the first sub-file, kept as it is)
    //   contents  each entry's data runs from its offset up to the next entry's offset (the last one to the end of the file)
    //
    // Compression: a byte with the top bit set is a run (low 7 bits = how many, the next byte = what to repeat); 0x80 on its
    // own is a literal 0x80; any other byte is a literal. Encoding the decoded data again gives the original bytes back.
    //
    // Fixture lists (fixtures_<year>.tmp) are a 4 byte count followed by 82 byte records. The last two bytes of a record
    // are the match's rules word: bits 0-3 = substitutes named, bits 4-7 = substitutes that may be used, plus flag bits.
    public partial class SaveGameFile
    {
        const int HeaderSize = 12;
        const int EntrySize = 268;
        const int NameOffset = 8;
        const int NameLength = 256;

        const int FixtureHeaderSize = 4;
        const int FixtureRecordSize = 82;
        const int FixtureRulesOffset = 0x50;

        public const int MaxSubstitutes = 9;

        public class Entry
        {
            public string Name;
            public int Offset;          // where its compressed data starts
            public int Size;            // uncompressed size
            public int End;             // where its compressed data ends
            public int DirectoryPosition;
        }

        byte[] data;
        List<Entry> entries = new List<Entry>();

        public string FileName;
        public List<Entry> Entries { get { return entries; } }

        public static SaveGameFile Load(string fileName)
        {
            SaveGameFile save = new SaveGameFile();
            save.FileName = fileName;
            save.data = File.ReadAllBytes(fileName);
            save.ParseDirectory();
            return save;
        }

        void ParseDirectory()
        {
            if (data.Length < HeaderSize)
                throw new InvalidDataException("This isn't a Championship Manager 3 saved game (file too small).");

            int version = BitConverter.ToInt32(data, 0);
            int count = BitConverter.ToInt32(data, 8);
            if (version != 4 || count < 1 || count > 2000 || HeaderSize + (long)count * EntrySize > data.Length)
                throw new InvalidDataException("This isn't a Championship Manager 3 saved game (unexpected header).");

            for (int i = 0; i < count; i++)
            {
                int position = HeaderSize + i * EntrySize;
                Entry entry = new Entry();
                entry.DirectoryPosition = position;
                entry.Offset = BitConverter.ToInt32(data, position);
                entry.Size = BitConverter.ToInt32(data, position + 4);

                int length = 0;
                while (length < NameLength && data[position + NameOffset + length] != 0)
                    length++;
                entry.Name = System.Text.Encoding.GetEncoding(1252).GetString(data, position + NameOffset, length);
                entries.Add(entry);
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                entry.End = i + 1 < entries.Count ? entries[i + 1].Offset : data.Length;
                if (entry.Offset < HeaderSize + count * EntrySize || entry.End < entry.Offset || entry.End > data.Length)
                    throw new InvalidDataException("The saved game's directory doesn't make sense (" + entry.Name + ").");
            }
        }

        public List<Entry> FixtureLists()
        {
            List<Entry> lists = new List<Entry>();
            foreach (Entry entry in entries)
                if (entry.Name.StartsWith("fixtures_", StringComparison.OrdinalIgnoreCase) && entry.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    lists.Add(entry);
            return lists;
        }

        byte[] Compressed(Entry entry)
        {
            byte[] blob = new byte[entry.End - entry.Offset];
            Array.Copy(data, entry.Offset, blob, 0, blob.Length);
            return blob;
        }

        // The sub-file as it is now: with the changes that are waiting in "replaced" if there are any
        byte[] Edit(Entry entry, Dictionary<Entry, byte[]> replaced)
        {
            byte[] blob;
            return replaced.TryGetValue(entry, out blob) ? RunLength.Decode(blob, 0) : Decode(entry);
        }

        byte[] Decode(Entry entry)
        {
            byte[] decoded = RunLength.Decode(Compressed(entry), entry.Size);
            if (decoded.Length != entry.Size)
                throw new InvalidDataException(entry.Name + " didn't decompress to the size the saved game says it should be.");
            return decoded;
        }

        static int FixtureCount(Entry entry, byte[] fixtures)
        {
            if (fixtures.Length < FixtureHeaderSize || (fixtures.Length - FixtureHeaderSize) % FixtureRecordSize != 0)
                throw new InvalidDataException(entry.Name + " isn't in the format this editor knows.");
            int count = (fixtures.Length - FixtureHeaderSize) / FixtureRecordSize;
            if (BitConverter.ToInt32(fixtures, 0) != count)
                throw new InvalidDataException(entry.Name + " isn't in the format this editor knows.");
            return count;
        }

        // How many fixtures there are for each (named, used) rule, across all the fixture lists
        public SortedDictionary<int, int> RulesSummary(out int fixtures)
        {
            SortedDictionary<int, int> summary = new SortedDictionary<int, int>();
            fixtures = 0;
            foreach (Entry entry in FixtureLists())
            {
                byte[] list = Decode(entry);
                int count = FixtureCount(entry, list);
                for (int i = 0; i < count; i++)
                {
                    int rules = list[FixtureHeaderSize + i * FixtureRecordSize + FixtureRulesOffset];
                    int key = rules;
                    int existing;
                    summary.TryGetValue(key, out existing);
                    summary[key] = existing + 1;
                }
                fixtures += count;
            }
            return summary;
        }

        public static int Named(int rules) { return rules & 0xF; }
        public static int Used(int rules) { return (rules >> 4) & 0xF; }

        // Gives every fixture (except ones with no substitutes at all) the same rules. The changed sub-files (compressed) go
        // into "replaced". Returns how many fixtures were changed.
        public int ReplaceSubstitutes(int named, int used, Dictionary<Entry, byte[]> replaced, out int fixtures)
        {
            if (named < 1 || named > MaxSubstitutes || used < 1 || used > MaxSubstitutes)
                throw new ArgumentException("The substitutes must be between 1 and " + MaxSubstitutes);

            byte rules = (byte)((used << 4) | named);
            int changed = 0;
            fixtures = 0;

            foreach (Entry entry in FixtureLists())
            {
                byte[] list = Decode(entry);
                int count = FixtureCount(entry, list);
                int changedHere = 0;
                for (int i = 0; i < count; i++)
                {
                    int at = FixtureHeaderSize + i * FixtureRecordSize + FixtureRulesOffset;
                    if (list[at] != 0 && list[at] != rules)
                    {
                        list[at] = rules;
                        changedHere++;
                    }
                }
                fixtures += count;
                if (changedHere > 0)
                {
                    replaced[entry] = RunLength.Encode(list);
                    changed += changedHere;
                }
            }
            return changed;
        }

        public int WriteWithSubstitutes(string fileName, int named, int used, out int fixtures)
        {
            Dictionary<Entry, byte[]> replaced = new Dictionary<Entry, byte[]>();
            int changed = ReplaceSubstitutes(named, used, replaced, out fixtures);
            WriteFile(fileName, replaced);
            return changed;
        }

        // ---- clubs. club.dat has 581 byte records: +0 id, +4 name (52 bytes), +56 short name (24 bytes), +105 the id of the club's
        // stadium (-1 = none) and +128 the club's reputation (signed 16 bit, 0-10000: the function at 0x5009D0 in cm3.exe uses
        // it for the transfer funds). The bank balance of each club is a signed 64 bit number at offset 4 of its record in
        // finance.dat (355 byte records: id, balance, ...). stadium.dat has 78 byte records: +0 id, +4 name (52 bytes), +56 the
        // city, +60 capacity, +64 seated capacity (the game says "all seated" when it is not below the capacity). ----

        const int ClubRecordSize = 581;
        const int ClubNameOffset = 4;
        const int ClubNameLength = 52;
        const int ClubShortNameOffset = 56;
        const int ClubShortNameLength = 24;
        const int ClubStadiumOffset = 105;
        const int ClubReputationOffset = 128;
        const int FinanceRecordSize = 355;
        const int FinanceBalanceOffset = 4;
        const int StadiumRecordSize = 78;
        const int StadiumNameOffset = 4;
        const int StadiumCapacityOffset = 60;
        const int StadiumSeatedOffset = 64;

        public const int MaxClubName = 50;
        public const int MaxShortName = 24;
        public const int MaxStadiumName = 50;

        public class Club
        {
            public int Id;
            public string Name;
            public string ShortName;
            public int Reputation;
            public long Balance;
            public int StadiumId;           // -1 = the club has no stadium
            public string StadiumName;
            public int Capacity;
            public int Seated;
            public int StadiumClubs;        // how many clubs use this stadium

            public ClubEdit ToEdit()
            {
                ClubEdit edit = new ClubEdit();
                edit.Name = Name;
                edit.ShortName = ShortName;
                edit.Reputation = Reputation;
                edit.Balance = Balance;
                edit.StadiumName = StadiumName;
                edit.Capacity = Capacity;
                edit.Seated = Seated;
                return edit;
            }
        }

        // The things of a club the editor can change
        public class ClubEdit
        {
            public string Name;
            public string ShortName;
            public int Reputation;
            public long Balance;
            public string StadiumName;      // the stadium things are ignored for a club without a stadium
            public int Capacity;
            public int Seated;

            public bool SameAs(Club club)
            {
                if (Name != club.Name || ShortName != club.ShortName || Reputation != club.Reputation || Balance != club.Balance)
                    return false;
                return club.StadiumId < 0 || (StadiumName == club.StadiumName && Capacity == club.Capacity && Seated == club.Seated);
            }
        }

        Entry Find(string name)
        {
            foreach (Entry entry in entries)
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            throw new InvalidDataException("This saved game has no " + name + ".");
        }

        static string ReadString(byte[] buffer, int at, int length)
        {
            int n = 0;
            while (n < length && buffer[at + n] != 0)
                n++;
            return System.Text.Encoding.GetEncoding(1252).GetString(buffer, at, n);
        }

        static void WriteString(byte[] buffer, int at, int length, string text)
        {
            byte[] bytes = System.Text.Encoding.GetEncoding(1252).GetBytes(text);
            if (bytes.Length > length)
                throw new ArgumentException("\"" + text + "\" is too long (at most " + length + " characters).");
            Array.Clear(buffer, at, length);
            Array.Copy(bytes, 0, buffer, at, bytes.Length);
        }

        public List<Club> Clubs()
        {
            byte[] clubData = Decode(Find("club.dat"));
            byte[] finance = Decode(Find("finance.dat"));
            byte[] stadiums = Decode(Find("stadium.dat"));
            if (clubData.Length % ClubRecordSize != 0 || finance.Length % FinanceRecordSize != 0 || stadiums.Length % StadiumRecordSize != 0)
                throw new InvalidDataException("club.dat, finance.dat or stadium.dat isn't in the format this editor knows.");

            int financeCount = finance.Length / FinanceRecordSize;
            int stadiumCount = stadiums.Length / StadiumRecordSize;
            Dictionary<int, int> stadiumUsers = new Dictionary<int, int>();
            List<Club> clubs = new List<Club>();
            for (int i = 0; i < clubData.Length / ClubRecordSize; i++)
            {
                int at = i * ClubRecordSize;
                Club club = new Club();
                club.Id = BitConverter.ToInt32(clubData, at);
                club.Name = ReadString(clubData, at + ClubNameOffset, ClubNameLength);
                club.ShortName = ReadString(clubData, at + ClubShortNameOffset, ClubShortNameLength);
                club.Reputation = BitConverter.ToInt16(clubData, at + ClubReputationOffset);

                int record = FindFinanceRecord(finance, financeCount, club.Id);
                if (record < 0)
                    continue;
                club.Balance = BitConverter.ToInt64(finance, record * FinanceRecordSize + FinanceBalanceOffset);

                club.StadiumId = BitConverter.ToInt32(clubData, at + ClubStadiumOffset);
                if (club.StadiumId >= 0 && (club.StadiumId >= stadiumCount || BitConverter.ToInt32(stadiums, club.StadiumId * StadiumRecordSize) != club.StadiumId))
                    club.StadiumId = -1;
                if (club.StadiumId >= 0)
                {
                    int stadiumAt = club.StadiumId * StadiumRecordSize;
                    club.StadiumName = ReadString(stadiums, stadiumAt + StadiumNameOffset, ClubNameLength);
                    club.Capacity = BitConverter.ToInt32(stadiums, stadiumAt + StadiumCapacityOffset);
                    club.Seated = BitConverter.ToInt32(stadiums, stadiumAt + StadiumSeatedOffset);
                    int users;
                    stadiumUsers.TryGetValue(club.StadiumId, out users);
                    stadiumUsers[club.StadiumId] = users + 1;
                }
                clubs.Add(club);
            }
            foreach (Club club in clubs)
                if (club.StadiumId >= 0)
                    club.StadiumClubs = stadiumUsers[club.StadiumId];
            return clubs;
        }

        // The records are normally in id order with the id as the record number, but don't rely on it
        static int FindFinanceRecord(byte[] finance, int count, int id)
        {
            if (id >= 0 && id < count && BitConverter.ToInt32(finance, id * FinanceRecordSize) == id)
                return id;
            for (int i = 0; i < count; i++)
                if (BitConverter.ToInt32(finance, i * FinanceRecordSize) == id)
                    return i;
            return -1;
        }

        // Changes clubs (by club id): names, reputation, bank balance and the club's stadium (a stadium that several clubs use
        // changes for all of them). The changed sub-files (compressed) go into "replaced". Returns how many clubs were changed.
        public int ReplaceClubs(Dictionary<int, ClubEdit> edits, Dictionary<Entry, byte[]> replaced)
        {
            Entry clubEntry = Find("club.dat");
            Entry financeEntry = Find("finance.dat");
            Entry stadiumEntry = Find("stadium.dat");
            byte[] clubData = Edit(clubEntry, replaced);
            byte[] finance = Edit(financeEntry, replaced);
            byte[] stadiums = Edit(stadiumEntry, replaced);
            if (clubData.Length % ClubRecordSize != 0 || finance.Length % FinanceRecordSize != 0 || stadiums.Length % StadiumRecordSize != 0)
                throw new InvalidDataException("club.dat, finance.dat or stadium.dat isn't in the format this editor knows.");
            int financeCount = finance.Length / FinanceRecordSize;
            int stadiumCount = stadiums.Length / StadiumRecordSize;

            bool clubsChanged = false, financeChanged = false, stadiumsChanged = false;
            int changed = 0;
            foreach (KeyValuePair<int, ClubEdit> pair in edits)
            {
                ClubEdit edit = pair.Value;
                if (pair.Key < 0 || (pair.Key + 1) * ClubRecordSize > clubData.Length || BitConverter.ToInt32(clubData, pair.Key * ClubRecordSize) != pair.Key)
                    throw new InvalidDataException("There is no club " + pair.Key + ".");
                int at = pair.Key * ClubRecordSize;
                if (edit.Name.Trim().Length == 0 || edit.ShortName.Trim().Length == 0)
                    throw new ArgumentException("A club needs a name and a short name.");
                bool any = false;

                if (ReadString(clubData, at + ClubNameOffset, ClubNameLength) != edit.Name)
                {
                    WriteString(clubData, at + ClubNameOffset, ClubNameLength, edit.Name);
                    any = clubsChanged = true;
                }
                if (ReadString(clubData, at + ClubShortNameOffset, ClubShortNameLength) != edit.ShortName)
                {
                    WriteString(clubData, at + ClubShortNameOffset, ClubShortNameLength, edit.ShortName);
                    any = clubsChanged = true;
                }
                if (edit.Reputation < short.MinValue || edit.Reputation > short.MaxValue)
                    throw new ArgumentException("The reputation must be between " + short.MinValue + " and " + short.MaxValue + ".");
                if (BitConverter.ToInt16(clubData, at + ClubReputationOffset) != edit.Reputation)
                {
                    WriteShort(clubData, at + ClubReputationOffset, edit.Reputation);
                    any = clubsChanged = true;
                }

                int record = FindFinanceRecord(finance, financeCount, pair.Key);
                if (record < 0)
                    throw new InvalidDataException("There is no finance record for club " + pair.Key + ".");
                int financeAt = record * FinanceRecordSize + FinanceBalanceOffset;
                if (BitConverter.ToInt64(finance, financeAt) != edit.Balance)
                {
                    WriteLong(finance, financeAt, edit.Balance);
                    any = financeChanged = true;
                }

                int stadiumId = BitConverter.ToInt32(clubData, at + ClubStadiumOffset);
                if (stadiumId >= 0 && stadiumId < stadiumCount && BitConverter.ToInt32(stadiums, stadiumId * StadiumRecordSize) == stadiumId)
                {
                    int stadiumAt = stadiumId * StadiumRecordSize;
                    if (edit.Capacity < 0 || edit.Seated < 0)
                        throw new ArgumentException("The capacity can't be negative.");
                    if (ReadString(stadiums, stadiumAt + StadiumNameOffset, ClubNameLength) != edit.StadiumName)
                    {
                        if (edit.StadiumName.Trim().Length == 0)
                            throw new ArgumentException("A stadium needs a name.");
                        WriteString(stadiums, stadiumAt + StadiumNameOffset, ClubNameLength, edit.StadiumName);
                        any = stadiumsChanged = true;
                    }
                    if (BitConverter.ToInt32(stadiums, stadiumAt + StadiumCapacityOffset) != edit.Capacity)
                    {
                        WriteInt(stadiums, stadiumAt + StadiumCapacityOffset, edit.Capacity);
                        any = stadiumsChanged = true;
                    }
                    if (BitConverter.ToInt32(stadiums, stadiumAt + StadiumSeatedOffset) != edit.Seated)
                    {
                        WriteInt(stadiums, stadiumAt + StadiumSeatedOffset, edit.Seated);
                        any = stadiumsChanged = true;
                    }
                }
                if (any)
                    changed++;
            }

            if (clubsChanged)
                replaced[clubEntry] = RunLength.Encode(clubData);
            if (financeChanged)
                replaced[financeEntry] = RunLength.Encode(finance);
            if (stadiumsChanged)
                replaced[stadiumEntry] = RunLength.Encode(stadiums);
            return changed;
        }

        // ---- contracts: contract.dat is a count (u32) followed by that many 72 byte records. In a record:
        //   +0   staff id (0xFFFFFFFF = unused slot)        +4   club id
        //   +12  weekly wage (the game shows it x4 per month)
        //   +16 / +20 / +24  goal / assist / clean sheet bonus (-1 = none; the game rounds what it shows)
        //   +28..+32  five clause flag bytes, one per clause type (0 = none, 1 = present, 2 = triggered for types 0 and 3)
        //   +33  the clause release fee, one number shared by the clauses
        //   +37  start date, +45 expiry date: day of the year counted from 0 (u16), year (u16), flag (u32)
        //   +53  contract type: bit 7 = part time, bits 0-5 = 1 monthly, 2 normal, 5 trial, 6/7 loan
        //   +71  high nibble = squad status (1 indispensable ... 7 not needed, 0 uncertain)
        // The staff.dat record of the same staff id (110 bytes) mirrors the expiry date (+70) and the wage (+78). The
        // names are in staff.dat (+4 first name id, +8 surname id) and the name files (60 byte records, the name first). ----

        const int ContractRecordSize = 72;
        const int ContractClubOffset = 4;
        const int ContractWageOffset = 12;
        const int ContractBonusOffset = 16;
        const int ContractFlagsOffset = 28;
        const int ContractFeeOffset = 33;
        const int ContractExpiryOffset = 45;
        const int ContractTypeOffset = 53;
        const int ContractStatusOffset = 71;
        const int StaffRecordSize = 110;
        const int StaffExpiryOffset = 70;
        const int StaffWageOffset = 78;
        const int NameRecordSize = 60;
        public const int ClauseTypes = 5;
        public const int BonusTypes = 3;

        public static readonly string[] ClauseNames = {
            "Non Promotion Release", "Minimum Fee Release", "Non Playing Release", "Relegation Release", "Match Highest Player Wage"
        };

        public class Contract
        {
            public int Record;              // index in contract.dat
            public int StaffId;
            public string Name;
            public int ClubId;
            public string Club;
            public int Wage;                // per week
            public int ExpiryDay, ExpiryYear;
            public byte TypeByte;
            public int[] Bonuses = new int[BonusTypes];     // goal, assist, clean sheet; -1 = none
            public byte StatusByte;         // the squad status is its high nibble
            public byte[] Flags = new byte[ClauseTypes];    // one per clause type
            public int Fee;
            public bool IsStaff;            // not a player
            public string Job;              // of a member of staff

            public bool HasClauses
            {
                get
                {
                    foreach (byte flag in Flags)
                        if (flag != 0)
                            return true;
                    return false;
                }
            }

            public ContractEdit ToEdit()
            {
                ContractEdit edit = new ContractEdit();
                edit.Flags = (byte[])Flags.Clone();
                edit.Fee = Fee;
                edit.Wage = Wage;
                edit.ExpiryDay = ExpiryDay;
                edit.ExpiryYear = ExpiryYear;
                edit.TypeByte = TypeByte;
                edit.Bonuses = (int[])Bonuses.Clone();
                edit.StatusByte = StatusByte;
                edit.ClubId = ClubId;
                return edit;
            }
        }

        // The things of a contract the editor can change
        public class ContractEdit
        {
            public byte[] Flags;
            public int Fee;
            public int Wage;
            public int ExpiryDay, ExpiryYear;
            public byte TypeByte;
            public int[] Bonuses;
            public byte StatusByte;
            public int ClubId;

            public bool SameAs(Contract contract)
            {
                if (ClubId != contract.ClubId || Fee != contract.Fee || Wage != contract.Wage || ExpiryDay != contract.ExpiryDay || ExpiryYear != contract.ExpiryYear ||
                    TypeByte != contract.TypeByte || StatusByte != contract.StatusByte)
                    return false;
                for (int i = 0; i < ClauseTypes; i++)
                    if (Flags[i] != contract.Flags[i])
                        return false;
                for (int i = 0; i < BonusTypes; i++)
                    if (Bonuses[i] != contract.Bonuses[i])
                        return false;
                return true;
            }
        }

        static string NameAt(byte[] names, int id)
        {
            int at = id * NameRecordSize;
            if (id < 0 || at + NameRecordSize > names.Length)
                return "?";
            int length = 0;
            while (length < NameRecordSize - 5 && names[at + length] != 0)
                length++;
            return System.Text.Encoding.GetEncoding(1252).GetString(names, at, length);
        }

        static int ContractCount(byte[] contracts)
        {
            if (contracts.Length < 4)
                throw new InvalidDataException("contract.dat isn't in the format this editor knows.");
            int count = BitConverter.ToInt32(contracts, 0);
            if (count < 0 || 4 + (long)count * ContractRecordSize > contracts.Length)
                throw new InvalidDataException("contract.dat isn't in the format this editor knows.");
            return count;
        }

        // Every contract in the save (or only the ones that have clauses)
        public List<Contract> Contracts(bool onlyWithClauses)
        {
            byte[] contracts = Decode(Find("contract.dat"));
            byte[] staff = Decode(Find("staff.dat"));
            byte[] firstNames = Decode(Find("first_names.dat"));
            byte[] secondNames = Decode(Find("second_names.dat"));
            byte[] commonNames = Decode(Find("common_names.dat"));
            int count = ContractCount(contracts);

            Dictionary<int, string> clubNames = new Dictionary<int, string>();
            foreach (Club club in Clubs())
                clubNames[club.Id] = club.Name;

            List<Contract> list = new List<Contract>();
            for (int i = 0; i < count; i++)
            {
                int at = 4 + i * ContractRecordSize;
                int staffId = BitConverter.ToInt32(contracts, at);
                if (staffId < 0)
                    continue;

                Contract contract = new Contract();
                contract.Record = i;
                contract.StaffId = staffId;
                contract.ClubId = BitConverter.ToInt32(contracts, at + 4);
                contract.Wage = BitConverter.ToInt32(contracts, at + ContractWageOffset);
                for (int t = 0; t < BonusTypes; t++)
                    contract.Bonuses[t] = BitConverter.ToInt32(contracts, at + ContractBonusOffset + 4 * t);
                for (int t = 0; t < ClauseTypes; t++)
                    contract.Flags[t] = contracts[at + ContractFlagsOffset + t];
                contract.Fee = BitConverter.ToInt32(contracts, at + ContractFeeOffset);
                contract.ExpiryDay = BitConverter.ToUInt16(contracts, at + ContractExpiryOffset);
                contract.ExpiryYear = BitConverter.ToUInt16(contracts, at + ContractExpiryOffset + 2);
                contract.TypeByte = contracts[at + ContractTypeOffset];
                contract.StatusByte = contracts[at + ContractStatusOffset];
                if (onlyWithClauses && !contract.HasClauses)
                    continue;

                string club;
                contract.Club = clubNames.TryGetValue(contract.ClubId, out club) ? club : "club " + contract.ClubId;

                int staffAt = staffId * StaffRecordSize;
                contract.Job = "";
                if (staffAt + StaffRecordSize <= staff.Length)
                {
                    contract.IsStaff = BitConverter.ToInt32(staff, staffAt + StaffPlayerOffset) < 0;
                    int job = staff[staffAt + StaffJobOffset];
                    contract.Job = job < StaffFields.Jobs.Length ? StaffFields.Jobs[job] : "job " + job;
                }
                if (staffAt + StaffRecordSize <= staff.Length)
                    contract.Name = StaffName(staff, staffAt, firstNames, secondNames, commonNames);
                else
                    contract.Name = "staff " + staffId;
                list.Add(contract);
            }
            return list;
        }

        // Changes contracts (by record number); the changed sub-files (compressed) go into "replaced". A contract left with no
        // clauses gets a fee of 0. The expiry date and wage are also changed in the staff record when it had the same values
        // as the contract. Returns how many contracts were changed.
        public int ReplaceContracts(Dictionary<int, ContractEdit> edits, Dictionary<Entry, byte[]> replaced)
        {
            Entry entry = Find("contract.dat");
            byte[] contracts = Edit(entry, replaced);
            int count = ContractCount(contracts);

            Entry staffEntry = Find("staff.dat");
            byte[] staff = null;
            bool staffChanged = false;
            List<int[]> moves = new List<int[]>();      // staff id, the club he was at, the club he goes to

            int changed = 0;
            foreach (KeyValuePair<int, ContractEdit> pair in edits)
            {
                if (pair.Key < 0 || pair.Key >= count)
                    throw new InvalidDataException("There is no contract number " + pair.Key + ".");
                int at = 4 + pair.Key * ContractRecordSize;
                ContractEdit edit = pair.Value;
                if (edit.Flags.Length != ClauseTypes || edit.Bonuses.Length != BonusTypes)
                    throw new ArgumentException("Bad contract edit.");

                bool any = false;
                foreach (byte flag in edit.Flags)
                    if (flag != 0)
                        any = true;
                int fee = any ? edit.Fee : 0;

                int oldWage = BitConverter.ToInt32(contracts, at + ContractWageOffset);
                int oldExpiryDay = BitConverter.ToUInt16(contracts, at + ContractExpiryOffset);
                int oldExpiryYear = BitConverter.ToUInt16(contracts, at + ContractExpiryOffset + 2);

                // is anything different?
                bool different = BitConverter.ToInt32(contracts, at + ContractClubOffset) != edit.ClubId ||
                                 BitConverter.ToInt32(contracts, at + ContractFeeOffset) != fee ||
                                 oldWage != edit.Wage || oldExpiryDay != edit.ExpiryDay || oldExpiryYear != edit.ExpiryYear ||
                                 contracts[at + ContractTypeOffset] != edit.TypeByte || contracts[at + ContractStatusOffset] != edit.StatusByte;
                for (int t = 0; t < ClauseTypes && !different; t++)
                    if (contracts[at + ContractFlagsOffset + t] != edit.Flags[t])
                        different = true;
                for (int t = 0; t < BonusTypes && !different; t++)
                    if (BitConverter.ToInt32(contracts, at + ContractBonusOffset + 4 * t) != edit.Bonuses[t])
                        different = true;
                if (!different)
                    continue;

                int oldClub = BitConverter.ToInt32(contracts, at + ContractClubOffset);
                if (oldClub != edit.ClubId)
                    moves.Add(new int[] { BitConverter.ToInt32(contracts, at), oldClub, edit.ClubId });
                WriteInt(contracts, at + ContractClubOffset, edit.ClubId);
                for (int t = 0; t < ClauseTypes; t++)
                    contracts[at + ContractFlagsOffset + t] = edit.Flags[t];
                for (int t = 0; t < BonusTypes; t++)
                    WriteInt(contracts, at + ContractBonusOffset + 4 * t, edit.Bonuses[t]);
                WriteInt(contracts, at + ContractFeeOffset, fee);
                WriteInt(contracts, at + ContractWageOffset, edit.Wage);
                WriteShort(contracts, at + ContractExpiryOffset, edit.ExpiryDay);
                WriteShort(contracts, at + ContractExpiryOffset + 2, edit.ExpiryYear);
                contracts[at + ContractTypeOffset] = edit.TypeByte;
                contracts[at + ContractStatusOffset] = edit.StatusByte;
                changed++;

                // the staff record mirrors the wage and the expiry date
                if (oldWage != edit.Wage || oldExpiryDay != edit.ExpiryDay || oldExpiryYear != edit.ExpiryYear)
                {
                    if (staff == null)
                        staff = Edit(staffEntry, replaced);
                    int staffAt = BitConverter.ToInt32(contracts, at) * StaffRecordSize;
                    if (staffAt >= 0 && staffAt + StaffRecordSize <= staff.Length)
                    {
                        if (BitConverter.ToInt32(staff, staffAt + StaffWageOffset) == oldWage)
                        {
                            WriteInt(staff, staffAt + StaffWageOffset, edit.Wage);
                            staffChanged = true;
                        }
                        if (BitConverter.ToUInt16(staff, staffAt + StaffExpiryOffset) == oldExpiryDay &&
                            BitConverter.ToUInt16(staff, staffAt + StaffExpiryOffset + 2) == oldExpiryYear)
                        {
                            WriteShort(staff, staffAt + StaffExpiryOffset, edit.ExpiryDay);
                            WriteShort(staff, staffAt + StaffExpiryOffset + 2, edit.ExpiryYear);
                            staffChanged = true;
                        }
                    }
                }
            }

            // a member of staff who goes to another club: his staff record and the club's lists of staff and players follow
            if (moves.Count > 0)
            {
                if (staff == null)
                    staff = Edit(staffEntry, replaced);
                Entry clubEntry = Find("club.dat");
                byte[] clubData = Edit(clubEntry, replaced);
                if (clubData.Length % ClubRecordSize != 0)
                    throw new InvalidDataException("club.dat isn't in the format this editor knows.");
                byte[] firstNames = Decode(Find("first_names.dat"));
                byte[] secondNames = Decode(Find("second_names.dat"));
                byte[] commonNames = Decode(Find("common_names.dat"));
                bool rostersChanged = false;
                foreach (int[] move in moves)
                {
                    int staffId = move[0], from = move[1], to = move[2];
                    int staffAt = staffId * StaffRecordSize;
                    if (staffId < 0 || staffAt + StaffRecordSize > staff.Length)
                        continue;
                    if (to < 0 || (to + 1) * ClubRecordSize > clubData.Length || BitConverter.ToInt32(clubData, to * ClubRecordSize) != to)
                        throw new InvalidDataException("There is no club " + to + ".");
                    if (BitConverter.ToInt32(staff, staffAt + StaffClubOffset) != from)
                        continue;       // e.g. a loan: his own club stays as it is

                    string who = StaffName(staff, staffAt, firstNames, secondNames, commonNames);
                    MoveToClub(clubData, staffId, staff[staffAt + StaffJobOffset], from, to, who);
                    WriteInt(staff, staffAt + StaffClubOffset, to);
                    staffChanged = rostersChanged = true;
                }
                if (rostersChanged)
                    replaced[clubEntry] = RunLength.Encode(clubData);
            }

            if (changed > 0)
                replaced[entry] = RunLength.Encode(contracts);
            if (staffChanged)
                replaced[staffEntry] = RunLength.Encode(staff);
            return changed;
        }

        // The lists of staff ids in a club.dat record: from +155 on, 4 bytes each, -1 = empty.
        //   +191 chairman   +195 / +199 / +203 managing director, general manager, director of football   +207 manager
        //   +211 assistant manager   +215 .. +411 the players (50)   +415 .. +431 coaches   +435 .. +459 scouts
        //   +463 .. +471 physios
        // (+155 .. +187 is another list that has some of the staff in it; a member of staff who leaves is taken out of it too.)
        const int RosterStart = 155;
        const int RosterEnd = 471;

        static readonly int[][] JobSlots = {
            new int[0],                                                 // 0 unemployed
            new int[] { 191 },                                          // 1 chairman
            new int[] { 195, 199, 203 },                                // 2 managing director
            new int[] { 195, 199, 203 },                                // 3 general manager
            new int[] { 195, 199, 203 },                                // 4 director of football
            new int[] { 207 },                                          // 5 manager
            new int[] { 211 },                                          // 6 assistant manager
            new int[] { 211 },                                          // 7 reserve manager
            new int[] { 415, 419, 423, 427, 431 },                      // 8 coach
            new int[] { 435, 439, 443, 447, 451, 455, 459 },            // 9 scout
            new int[] { 463, 467, 471 },                                // 10 physio
            Range(215, 411),                                            // 11 player
            Range(215, 411),                                            // 12 player / manager
            Range(215, 411),                                            // 13 player / assistant manager
            new int[0],                                                 // 14
            Range(215, 411)                                             // 15 player / coach
        };

        static int[] Range(int first, int last)
        {
            List<int> slots = new List<int>();
            for (int at = first; at <= last; at += 4)
                slots.Add(at);
            return slots.ToArray();
        }

        static int FreeSlot(byte[] clubData, int record, int[] slots)
        {
            foreach (int slot in slots)
                if (BitConverter.ToInt32(clubData, record * ClubRecordSize + slot) == -1)
                    return slot;
            return -1;
        }

        // Takes a member of staff out of the lists of a club and puts him into the lists of another one
        static void MoveToClub(byte[] clubData, int staffId, int job, int from, int to, string name)
        {
            // first check there is room, so nothing is half done
            int[] slots = job < JobSlots.Length ? JobSlots[job] : new int[0];
            int target = slots.Length == 0 ? -1 : FreeSlot(clubData, to, slots);
            string clubName = ReadString(clubData, to * ClubRecordSize + ClubNameOffset, ClubNameLength);
            if (slots.Length > 0 && target < 0)
                throw new InvalidDataException(clubName + " has no free place for " + name + " (" + (job < StaffFields.Jobs.Length ? StaffFields.Jobs[job] : "job " + job) +
                                               "). Move the one who is there somewhere else first.");

            if (from >= 0 && (from + 1) * ClubRecordSize <= clubData.Length)
                for (int at = RosterStart; at <= RosterEnd; at += 4)
                    if (BitConverter.ToInt32(clubData, from * ClubRecordSize + at) == staffId)
                        WriteInt(clubData, from * ClubRecordSize + at, -1);

            if (target >= 0)
                WriteInt(clubData, to * ClubRecordSize + target, staffId);

            // a player who is also a manager, assistant or coach takes that job at the new club if it is free
            int[] extra = job == 12 ? JobSlots[5] : job == 13 ? JobSlots[6] : job == 15 ? JobSlots[8] : null;
            if (extra != null)
            {
                int free = FreeSlot(clubData, to, extra);
                if (free >= 0)
                    WriteInt(clubData, to * ClubRecordSize + free, staffId);
            }
        }

        static void WriteShort(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        // Rebuilds the file: everything before the first sub-file as it was (with the new offsets), then each sub-file
        // (the compressed data of the ones in "replaced" instead of the original)
        public void WriteFile(string fileName, Dictionary<Entry, byte[]> replaced)
        {
            using (MemoryStream output = new MemoryStream(data.Length + 1024))
            {
                int firstOffset = entries[0].Offset;
                output.Write(data, 0, firstOffset);

                List<Entry> order = OutputOrder();
                Dictionary<Entry, int> newOffsets = new Dictionary<Entry, int>();
                foreach (Entry entry in order)
                {
                    newOffsets[entry] = (int)output.Position;
                    byte[] blob;
                    if (!replaced.TryGetValue(entry, out blob))
                    {
                        blob = new byte[entry.End - entry.Offset];
                        Array.Copy(data, entry.Offset, blob, 0, blob.Length);
                    }
                    output.Write(blob, 0, blob.Length);
                }
                byte[] result = output.ToArray();

                foreach (Entry entry in entries)
                    WriteInt(result, entry.DirectoryPosition, newOffsets[entry]);

                // changed sub-files may have a different size now
                foreach (KeyValuePair<Entry, byte[]> pair in replaced)
                {
                    int size = RunLength.Decode(pair.Value, pair.Key.Size).Length;
                    if (size != pair.Key.Size)
                        WriteInt(result, pair.Key.DirectoryPosition + 4, size);
                }

                ApplyRenames(result);

                // the directory lists the sub-files in the order they are written in
                byte[] directory = new byte[entries.Count * EntrySize];
                Array.Copy(result, HeaderSize, directory, 0, directory.Length);
                for (int i = 0; i < order.Count; i++)
                    Array.Copy(directory, order[i].DirectoryPosition - HeaderSize, result, HeaderSize + i * EntrySize, EntrySize);
                File.WriteAllBytes(fileName, result);
            }
        }

        static void WriteLong(byte[] buffer, int offset, long value)
        {
            WriteInt(buffer, offset, (int)value);
            WriteInt(buffer, offset + 4, (int)(value >> 32));
        }

        static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }

    static class RunLength
    {
        public static byte[] Decode(byte[] blob, int expectedSize)
        {
            MemoryStream output = new MemoryStream(expectedSize > 0 ? expectedSize : blob.Length * 2);
            int i = 0;
            while (i < blob.Length)
            {
                byte b = blob[i++];
                if (b == 0x80)
                    output.WriteByte(0x80);
                else if ((b & 0x80) != 0)
                {
                    if (i >= blob.Length)
                        throw new InvalidDataException("Unexpected end of compressed data.");
                    byte value = blob[i++];
                    for (int n = b & 0x7F; n > 0; n--)
                        output.WriteByte(value);
                }
                else
                    output.WriteByte(b);
            }
            return output.ToArray();
        }

        public static byte[] Encode(byte[] data)
        {
            MemoryStream output = new MemoryStream(data.Length / 2 + 16);
            int i = 0;
            while (i < data.Length)
            {
                byte b = data[i];
                int run = 1;
                while (i + run < data.Length && data[i + run] == b && run < 127)
                    run++;
                if (run >= 2)
                {
                    output.WriteByte((byte)(0x80 | run));
                    output.WriteByte(b);
                    i += run;
                }
                else
                {
                    if (b > 0x80)
                        output.WriteByte(0x81);
                    output.WriteByte(b);
                    i++;
                }
            }
            return output.ToArray();
        }
    }
}

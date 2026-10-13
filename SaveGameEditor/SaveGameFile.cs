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
    public class SaveGameFile
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

        // ---- clubs: names are in club.dat (581 byte records: id, name), the bank balance of each club is a signed 64 bit
        // number at offset 4 of its record in finance.dat (355 byte records: id, balance, ...) ----

        const int ClubRecordSize = 581;
        const int ClubNameLength = 52;
        const int FinanceRecordSize = 355;
        const int FinanceBalanceOffset = 4;

        public class Club
        {
            public int Id;
            public string Name;
            public long Balance;
        }

        Entry Find(string name)
        {
            foreach (Entry entry in entries)
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            throw new InvalidDataException("This saved game has no " + name + ".");
        }

        public List<Club> Clubs()
        {
            byte[] clubData = Decode(Find("club.dat"));
            byte[] finance = Decode(Find("finance.dat"));
            if (clubData.Length % ClubRecordSize != 0 || finance.Length % FinanceRecordSize != 0)
                throw new InvalidDataException("club.dat or finance.dat isn't in the format this editor knows.");

            int financeCount = finance.Length / FinanceRecordSize;
            List<Club> clubs = new List<Club>();
            for (int i = 0; i < clubData.Length / ClubRecordSize; i++)
            {
                int at = i * ClubRecordSize;
                Club club = new Club();
                club.Id = BitConverter.ToInt32(clubData, at);
                int length = 0;
                while (length < ClubNameLength && clubData[at + 4 + length] != 0)
                    length++;
                club.Name = System.Text.Encoding.GetEncoding(1252).GetString(clubData, at + 4, length);

                int record = FindFinanceRecord(finance, financeCount, club.Id);
                if (record < 0)
                    continue;
                club.Balance = BitConverter.ToInt64(finance, record * FinanceRecordSize + FinanceBalanceOffset);
                clubs.Add(club);
            }
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

        // Sets the bank balance of clubs (by club id); the changed sub-file (compressed) goes into "replaced".
        // Returns how many were changed.
        public int ReplaceBalances(Dictionary<int, long> balances, Dictionary<Entry, byte[]> replaced)
        {
            Entry entry = Find("finance.dat");
            byte[] finance = Decode(entry);
            if (finance.Length % FinanceRecordSize != 0)
                throw new InvalidDataException("finance.dat isn't in the format this editor knows.");
            int count = finance.Length / FinanceRecordSize;

            int changed = 0;
            foreach (KeyValuePair<int, long> pair in balances)
            {
                int record = FindFinanceRecord(finance, count, pair.Key);
                if (record < 0)
                    throw new InvalidDataException("There is no finance record for club " + pair.Key + ".");
                int at = record * FinanceRecordSize + FinanceBalanceOffset;
                if (BitConverter.ToInt64(finance, at) == pair.Value)
                    continue;
                WriteLong(finance, at, pair.Value);
                changed++;
            }

            if (changed > 0)
                replaced[entry] = RunLength.Encode(finance);
            return changed;
        }

        public int WriteWithBalances(string fileName, Dictionary<int, long> balances)
        {
            Dictionary<Entry, byte[]> replaced = new Dictionary<Entry, byte[]>();
            int changed = ReplaceBalances(balances, replaced);
            WriteFile(fileName, replaced);
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

            public bool SameAs(Contract contract)
            {
                if (Fee != contract.Fee || Wage != contract.Wage || ExpiryDay != contract.ExpiryDay || ExpiryYear != contract.ExpiryYear ||
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
                if (staffAt + StaffRecordSize <= staff.Length)
                    contract.Name = NameAt(firstNames, BitConverter.ToInt32(staff, staffAt + 4)) + " " + NameAt(secondNames, BitConverter.ToInt32(staff, staffAt + 8));
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
            byte[] contracts = Decode(entry);
            int count = ContractCount(contracts);

            Entry staffEntry = Find("staff.dat");
            byte[] staff = null;
            bool staffChanged = false;

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
                bool different = BitConverter.ToInt32(contracts, at + ContractFeeOffset) != fee ||
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
                        staff = Decode(staffEntry);
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

            if (changed > 0)
                replaced[entry] = RunLength.Encode(contracts);
            if (staffChanged)
                replaced[staffEntry] = RunLength.Encode(staff);
            return changed;
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

                List<int> newOffsets = new List<int>();
                foreach (Entry entry in entries)
                {
                    newOffsets.Add((int)output.Position);
                    byte[] blob;
                    if (!replaced.TryGetValue(entry, out blob))
                    {
                        blob = new byte[entry.End - entry.Offset];
                        Array.Copy(data, entry.Offset, blob, 0, blob.Length);
                    }
                    output.Write(blob, 0, blob.Length);
                }
                byte[] result = output.ToArray();

                for (int i = 0; i < entries.Count; i++)
                    WriteInt(result, entries[i].DirectoryPosition, newOffsets[i]);

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

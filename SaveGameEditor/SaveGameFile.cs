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

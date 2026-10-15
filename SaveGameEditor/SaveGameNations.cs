using System;
using System.Collections.Generic;
using System.IO;

namespace CM3SaveGameEditor
{
    // Nationality and second nationality of players and staff (staff.dat +26 and +30; the game works out the work permit from them)
    public partial class SaveGameFile
    {
        static int SecondNation(byte[] staff, int staffAt)
        {
            int id = BitConverter.ToUInt16(staff, staffAt + StaffSecondNationOffset);
            return id == 0xFFFF ? -1 : id;
        }

        // edits: staff id -> { nationality, second nationality (-1 = none) }. Returns how many people changed.
        public int ReplaceNations(Dictionary<int, int[]> edits, Dictionary<Entry, byte[]> replaced)
        {
            Entry entry = Find("staff.dat");
            byte[] staff = Edit(entry, replaced);
            if (staff.Length % StaffRecordSize != 0)
                throw new InvalidDataException("staff.dat isn't in the format this editor knows.");
            Dictionary<int, string> known = NationNames(Decode(Find("nation.dat")));

            int changed = 0;
            foreach (KeyValuePair<int, int[]> pair in edits)
            {
                int at = pair.Key * StaffRecordSize;
                if (pair.Key < 0 || at + StaffRecordSize > staff.Length)
                    throw new InvalidDataException("There is no staff number " + pair.Key + ".");
                int nation = pair.Value[0], second = pair.Value[1];
                if (!known.ContainsKey(nation) || (second >= 0 && !known.ContainsKey(second)))
                    throw new InvalidDataException("Unknown nation for staff number " + pair.Key + ".");
                if (BitConverter.ToUInt16(staff, at + StaffNationOffset) == nation && SecondNation(staff, at) == second)
                    continue;
                WriteShort(staff, at + StaffNationOffset, nation);
                WriteShort(staff, at + StaffSecondNationOffset, second < 0 ? 0xFFFF : second);
                changed++;
            }
            if (changed > 0)
                replaced[entry] = RunLength.Encode(staff);
            return changed;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CM3SaveGameEditor
{
    // One searchable number about a player (an attribute, the current / potential ability, the age...)
    public class ScoutField
    {
        public string Name;
        public int Min, Max;
        public bool Hidden;         // not shown by the game
    }

    // A player or a member of staff of the save, with all the numbers the scouting tab can search on (Values, indexed like
    // the field set's All)
    public class ScoutPlayer
    {
        public int StaffId;
        public string Name;
        public string Club;
        public string Nation;
        public int NationId = -1;       // the ids in nation.dat; the second nationality is -1 when he has none
        public int SecondNationId = -1;
        public string Positions;        // where a player plays; the job of a member of staff
        public int[] Values;
    }

    // The numbers the scouting tab can search on for players or for staff. The first four are always the current ability,
    // the potential ability, the age and the wages (per month), then the shown attributes, the hidden ones and the positions.
    public class ScoutFieldSet
    {
        public ScoutField[] All;
        public int FirstVisible, VisibleCount;
        public int FirstHidden, HiddenCount;
        public int FirstPosition, PositionCount;

        public const int CurrentAbility = 0;
        public const int PotentialAbility = 1;
        public const int Age = 2;
        public const int Wage = 3;
    }

    // Where each number comes from.
    //
    // player.dat has a 70 byte record per player; the player's staff.dat record (110 bytes) points to it with the number at +97.
    // In the player record:
    //   +5   current ability (u16)        +7   potential ability (u16)       (0-200 scale, hidden by the game)
    //   +15..+26   how well he plays in 12 positions (1-20): goalkeeper, sweeper, defender, defensive midfielder, midfielder,
    //              attacking midfielder, striker, wing back, right side, left side, centre, free role
    //   +27..+69   the attributes. Most are the number shown in the game. The ones the game scales with the current ability
    //              are signed bytes: shown = clamp(1, 20, (t + t * t * 0.1) / 3 + 0.5), t = (2 * byte + ability) * 0.05 + 10,
    //              cut down to a whole number (the function at 0x4C9FE0 in cm3.exe). Some of those are always 1 for a goalkeeper
    //              (attackers' skills) and Handling / Reflexes are 1 for everybody who isn't one.
    // In the staff record: adaptability +86, ambition +87, determination +88, loyalty +89, pressure +90, professionalism +91,
    // sportsmanship +92, temperament +93; the date of birth is day of the year (u16) +16 and year (u16) +18; the nationality
    // +26 (u16); the club +57 (-1 = none); the weekly wage +78. The game's date is in general.dat: day of the year (u16) at 13
    // and year (u16) at 15.
    public static class ScoutFields
    {
        public const int CurrentAbility = 0;
        public const int PotentialAbility = 1;
        public const int Age = 2;
        public const int Wage = 3;

        public static readonly ScoutField[] All;

        // The attributes the game shows, in its (alphabetical) order. Kind: P = a byte of the player record, S = a byte of the
        // staff record, N = a byte of the player record scaled with the ability, O = like N but 1 for goalkeepers, G = like N
        // but 1 for players who aren't goalkeepers.
        class Source
        {
            public string Name;
            public char Kind;
            public int Offset;
            public Source(string name, char kind, int offset) { Name = name; Kind = kind; Offset = offset; }
        }

        static readonly Source[] Visible = {
            new Source("Acceleration", 'P', 27), new Source("Adaptability", 'S', 86), new Source("Aggression", 'P', 28),
            new Source("Agility", 'P', 29), new Source("Anticipation", 'N', 30), new Source("Balance", 'P', 31),
            new Source("Bravery", 'P', 32), new Source("Creativity", 'O', 67), new Source("Crossing", 'O', 35),
            new Source("Determination", 'S', 88), new Source("Dribbling", 'O', 38), new Source("Flair", 'N', 40),
            new Source("Handling", 'G', 42), new Source("Heading", 'N', 43), new Source("Influence", 'P', 47),
            new Source("Jumping", 'P', 46), new Source("Marking", 'O', 50), new Source("Off The Ball", 'O', 51),
            new Source("Pace", 'P', 54), new Source("Passing", 'N', 55), new Source("Positioning", 'N', 57),
            new Source("Reflexes", 'G', 58), new Source("Set Pieces", 'P', 41), new Source("Shooting", 'O', 39),
            new Source("Stamina", 'P', 60), new Source("Strength", 'P', 61), new Source("Tackling", 'N', 62),
            new Source("Teamwork", 'P', 63), new Source("Technique", 'P', 64), new Source("Versatility", 'P', 66),
            new Source("Work Rate", 'P', 68)
        };

        static readonly Source[] HiddenSources = {
            new Source("Ambition", 'S', 87), new Source("Loyalty", 'S', 89), new Source("Pressure", 'S', 90),
            new Source("Professionalism", 'S', 91), new Source("Sportsmanship", 'S', 92), new Source("Temperament", 'S', 93)
        };

        // player record +15 .. +26
        static readonly string[] PositionNames = {
            "Goalkeeper", "Sweeper", "Defender", "Defensive Midfielder", "Midfielder", "Attacking Midfielder", "Striker",
            "Wing Back", "Right side", "Left side", "Centre", "Free role"
        };
        static readonly string[] PositionShort = { "GK", "SW", "D", "DM", "M", "AM", "ST", "WB", "R", "L", "C", "FR" };
        const int FirstPosition = 15;
        const int GoalkeeperPosition = 15;
        const int PositionThreshold = 15;       // the game lists a position from this rating on

        public static readonly int FirstVisible = 4;
        public static readonly int FirstHidden;
        public static readonly int FirstPositionField;
        public static readonly ScoutFieldSet Set;

        static ScoutFields()
        {
            List<ScoutField> list = new List<ScoutField>();
            list.Add(new ScoutField { Name = "Current ability", Min = 0, Max = 200, Hidden = true });
            list.Add(new ScoutField { Name = "Potential ability", Min = 0, Max = 200, Hidden = true });
            list.Add(new ScoutField { Name = "Age", Min = 14, Max = 50 });
            list.Add(new ScoutField { Name = "Wages per month", Min = 0, Max = 2000000000 });
            foreach (Source source in Visible)
                list.Add(new ScoutField { Name = source.Name, Min = 1, Max = 20 });
            FirstHidden = list.Count;
            foreach (Source source in HiddenSources)
                list.Add(new ScoutField { Name = source.Name, Min = 0, Max = 20, Hidden = true });
            FirstPositionField = list.Count;
            foreach (string name in PositionNames)
                list.Add(new ScoutField { Name = "Rating as " + name, Min = 0, Max = 20 });
            All = list.ToArray();
            Set = new ScoutFieldSet
            {
                All = All, FirstVisible = FirstVisible, VisibleCount = Visible.Length, FirstHidden = FirstHidden,
                HiddenCount = HiddenSources.Length, FirstPosition = FirstPositionField, PositionCount = PositionNames.Length
            };
        }

        public static int Count { get { return All.Length; } }
        public static int VisibleCount { get { return Visible.Length; } }
        public static int HiddenCount { get { return HiddenSources.Length; } }
        public static int PositionCount { get { return PositionNames.Length; } }

        static int Scaled(byte raw, int ability)
        {
            double t = (2.0 * (sbyte)raw + ability) * 0.05 + 10.0;
            double value = (t + t * t * 0.1) * 0.3333333333333333 + 0.5;
            if (value <= 1.0)
                return 1;
            if (value >= 20.0)
                return 20;
            return (int)value;
        }

        static int Attribute(Source source, byte[] player, int playerAt, byte[] staff, int staffAt, int ability)
        {
            switch (source.Kind)
            {
                case 'S':
                    return staff[staffAt + source.Offset];
                case 'P':
                    return player[playerAt + source.Offset];
                case 'O':
                    return player[playerAt + GoalkeeperPosition] == 20 ? 1 : Scaled(player[playerAt + source.Offset], ability);
                case 'G':
                    return player[playerAt + GoalkeeperPosition] < 15 ? 1 : Scaled(player[playerAt + source.Offset], ability);
                default:
                    return Scaled(player[playerAt + source.Offset], ability);
            }
        }

        // Fills in the attributes, the abilities and the position ratings (not the age and the wage)
        public static void Read(int[] values, byte[] player, int playerAt, byte[] staff, int staffAt)
        {
            int ability = BitConverter.ToInt16(player, playerAt + 5);
            values[CurrentAbility] = ability;
            values[PotentialAbility] = BitConverter.ToInt16(player, playerAt + 7);
            for (int i = 0; i < Visible.Length; i++)
                values[FirstVisible + i] = Attribute(Visible[i], player, playerAt, staff, staffAt, ability);
            for (int i = 0; i < HiddenSources.Length; i++)
                values[FirstHidden + i] = staff[staffAt + HiddenSources[i].Offset];
            for (int i = 0; i < PositionNames.Length; i++)
                values[FirstPositionField + i] = player[playerAt + FirstPosition + i];
        }

        // Can this attribute of the player be changed (the attackers' skills show 1 for a goalkeeper, Handling and Reflexes
        // show 1 for everybody else)? "values" are the player's numbers, indexed like All.
        public static bool Applies(int field, int[] values)
        {
            int i = field - FirstVisible;
            if (i < 0 || i >= Visible.Length)
                return true;
            if (Visible[i].Kind == 'O')
                return values[FirstPositionField + 0] != 20;
            if (Visible[i].Kind == 'G')
                return values[FirstPositionField + 0] >= 15;
            return true;
        }

        // Is this attribute one the game scales with the current ability (the editor can't always hit the number exactly)?
        public static bool IsScaled(int field)
        {
            int i = field - FirstVisible;
            return i >= 0 && i < Visible.Length && "NOG".IndexOf(Visible[i].Kind) >= 0;
        }

        // The byte that makes the game show "value" for a scaled attribute at this ability (the nearest one to what is there)
        static byte RawFor(int value, int ability, byte current)
        {
            int best = (sbyte)current, bestError = Math.Abs(Scaled(current, ability) - value);
            for (int x = -128; x <= 127; x++)
            {
                int error = Math.Abs(Scaled((byte)x, ability) - value);
                if (error < bestError || (error == bestError && Math.Abs(x - (sbyte)current) < Math.Abs(best - (sbyte)current)))
                {
                    best = x;
                    bestError = error;
                }
            }
            return (byte)best;
        }

        static void Check(int value, int min, int max, string what)
        {
            if (value < min || value > max)
                throw new ArgumentException(what + " must be between " + min + " and " + max + ".");
        }

        // Writes the numbers of a player into his records (the abilities, the attributes, the hidden ones, the position
        // ratings; not the age and the wages). Scaled attributes are set so the game shows what "values" says at the new
        // current ability. Returns whether anything was different.
        public static bool Write(int[] values, byte[] player, int playerAt, byte[] staff, int staffAt)
        {
            int[] now = new int[Count];
            Read(now, player, playerAt, staff, staffAt);
            int oldAbility = now[CurrentAbility];
            int ability = values[CurrentAbility];
            Check(ability, 0, 200, "The current ability");
            Check(values[PotentialAbility], 0, 200, "The potential ability");
            bool changed = false;

            if (ability != oldAbility)
            {
                player[playerAt + 5] = (byte)ability;
                player[playerAt + 6] = (byte)(ability >> 8);
                changed = true;
            }
            if (values[PotentialAbility] != now[PotentialAbility])
            {
                player[playerAt + 7] = (byte)values[PotentialAbility];
                player[playerAt + 8] = (byte)(values[PotentialAbility] >> 8);
                changed = true;
            }

            for (int i = 0; i < PositionNames.Length; i++)
            {
                int f = FirstPositionField + i;
                Check(values[f], 0, 20, "A position rating");
                if (values[f] != now[f])
                {
                    player[playerAt + FirstPosition + i] = (byte)values[f];
                    changed = true;
                }
            }

            for (int i = 0; i < HiddenSources.Length; i++)
            {
                int f = FirstHidden + i;
                Check(values[f], 0, 20, HiddenSources[i].Name);
                if (values[f] != now[f])
                {
                    staff[staffAt + HiddenSources[i].Offset] = (byte)values[f];
                    changed = true;
                }
            }

            for (int i = 0; i < Visible.Length; i++)
            {
                int f = FirstVisible + i;
                Source source = Visible[i];
                Check(values[f], 1, 20, source.Name);
                if (source.Kind == 'S')
                {
                    if (values[f] != now[f])
                    {
                        staff[staffAt + source.Offset] = (byte)values[f];
                        changed = true;
                    }
                }
                else if (source.Kind == 'P')
                {
                    if (values[f] != now[f])
                    {
                        player[playerAt + source.Offset] = (byte)values[f];
                        changed = true;
                    }
                }
                else if (Applies(f, values) && (values[f] != now[f] || ability != oldAbility))
                {
                    byte raw = RawFor(values[f], ability, player[playerAt + source.Offset]);
                    if (raw != player[playerAt + source.Offset])
                    {
                        player[playerAt + source.Offset] = raw;
                        changed = true;
                    }
                }
            }
            return changed;
        }

        // e.g. "D/DM (R/L/C)", the way the game lists where a player can play
        public static string PositionText(int[] values)
        {
            StringBuilder roles = new StringBuilder();
            StringBuilder sides = new StringBuilder();
            for (int i = 0; i < PositionNames.Length; i++)
            {
                if (values[FirstPositionField + i] < PositionThreshold)
                    continue;
                StringBuilder target = (i >= 8 && i <= 10) ? sides : roles;
                if (target.Length > 0)
                    target.Append(i >= 8 && i <= 10 ? "" : "/");
                target.Append(PositionShort[i]);
            }
            if (roles.Length == 0 && sides.Length == 0)
                return "";
            return sides.Length == 0 ? roles.ToString() : roles + " (" + sides + ")";
        }
    }

    public partial class SaveGameFile
    {
        const int PlayerRecordSize = 70;
        const int StaffPlayerOffset = 97;
        const int StaffBirthOffset = 16;
        const int StaffNationOffset = 26;
        const int StaffSecondNationOffset = 30;     // u16, 0xFFFF = none
        const int StaffClubOffset = 57;
        const int NationRecordSize = 290;
        const int GeneralDateOffset = 13;

        static DateTime DayOfYear(int day, int year)
        {
            try
            {
                return new DateTime(Math.Max(1, Math.Min(9999, year)), 1, 1).AddDays(day);
            }
            catch (ArgumentException)
            {
                return new DateTime(1900, 1, 1);
            }
        }

        static Dictionary<int, string> NationNames(byte[] nations)
        {
            Dictionary<int, string> names = new Dictionary<int, string>();
            for (int at = 0; at + NationRecordSize <= nations.Length; at += NationRecordSize)
            {
                int length = 0;
                while (length < 50 && nations[at + 4 + length] != 0)
                    length++;
                names[BitConverter.ToInt32(nations, at)] = Encoding.GetEncoding(1252).GetString(nations, at + 4, length);
            }
            return names;
        }

        // The name the game shows: the common name if there is one, else first name and surname
        static string StaffName(byte[] staff, int staffAt, byte[] firstNames, byte[] secondNames, byte[] commonNames)
        {
            int common = BitConverter.ToInt32(staff, staffAt + 12);
            string commonName = common > 0 ? NameAt(commonNames, common) : "";
            if (commonName.Length > 0 && commonName != "?")
                return commonName;
            return (NameAt(firstNames, BitConverter.ToInt32(staff, staffAt + 4)) + " " + NameAt(secondNames, BitConverter.ToInt32(staff, staffAt + 8))).Trim();
        }

        static string ClubName(Dictionary<int, string> clubNames, int clubId)
        {
            string club;
            return clubId < 0 ? "" : (clubNames.TryGetValue(clubId, out club) ? club : "club " + clubId);
        }

        static int AgeOf(byte[] staff, int staffAt, DateTime today)
        {
            DateTime born = DayOfYear(BitConverter.ToUInt16(staff, staffAt + StaffBirthOffset), BitConverter.ToUInt16(staff, staffAt + StaffBirthOffset + 2));
            int age = today.Year - born.Year;
            if (born > today.AddYears(-age))
                age--;
            return age;
        }

        // The date of the game in this save
        public DateTime GameDate()
        {
            byte[] general = Decode(Find("general.dat"));
            if (general.Length < GeneralDateOffset + 4)
                throw new InvalidDataException("general.dat isn't in the format this editor knows.");
            return DayOfYear(BitConverter.ToUInt16(general, GeneralDateOffset), BitConverter.ToUInt16(general, GeneralDateOffset + 2));
        }

        // Every player of the save (staff who have a player record) with the numbers to search on
        public List<ScoutPlayer> Players()
        {
            byte[] staff = Decode(Find("staff.dat"));
            byte[] players = Decode(Find("player.dat"));
            byte[] firstNames = Decode(Find("first_names.dat"));
            byte[] secondNames = Decode(Find("second_names.dat"));
            byte[] commonNames = Decode(Find("common_names.dat"));
            byte[] nations = Decode(Find("nation.dat"));
            if (staff.Length % StaffRecordSize != 0 || players.Length % PlayerRecordSize != 0 || nations.Length % NationRecordSize != 0)
                throw new InvalidDataException("staff.dat, player.dat or nation.dat isn't in the format this editor knows.");

            DateTime today = GameDate();

            Dictionary<int, string> clubNames = new Dictionary<int, string>();
            foreach (Club club in Clubs())
                clubNames[club.Id] = club.Name;

            Dictionary<int, string> nationNames = NationNames(nations);

            int playerCount = players.Length / PlayerRecordSize;
            List<ScoutPlayer> list = new List<ScoutPlayer>();
            for (int i = 0; i < staff.Length / StaffRecordSize; i++)
            {
                int staffAt = i * StaffRecordSize;
                int playerId = BitConverter.ToInt32(staff, staffAt + StaffPlayerOffset);
                if (playerId < 0 || playerId >= playerCount || staff[staffAt + StaffJobOffset] == StaffRetiredJob)
                    continue;
                int playerAt = playerId * PlayerRecordSize;
                if (BitConverter.ToInt32(players, playerAt) != playerId)
                    continue;

                ScoutPlayer player = new ScoutPlayer();
                player.StaffId = BitConverter.ToInt32(staff, staffAt);
                player.Name = StaffName(staff, staffAt, firstNames, secondNames, commonNames);
                player.Club = ClubName(clubNames, BitConverter.ToInt32(staff, staffAt + StaffClubOffset));
                string nation;
                player.Nation = nationNames.TryGetValue(BitConverter.ToUInt16(staff, staffAt + StaffNationOffset), out nation) ? nation : "";
                player.NationId = BitConverter.ToUInt16(staff, staffAt + StaffNationOffset);
                player.SecondNationId = SecondNation(staff, staffAt);

                player.Values = new int[ScoutFields.Count];
                ScoutFields.Read(player.Values, players, playerAt, staff, staffAt);

                player.Values[ScoutFields.Age] = AgeOf(staff, staffAt, today);
                player.Values[ScoutFields.Wage] = BitConverter.ToInt32(staff, staffAt + StaffWageOffset) * 4;
                player.Positions = ScoutFields.PositionText(player.Values);
                list.Add(player);
            }
            return list;
        }
    }
}

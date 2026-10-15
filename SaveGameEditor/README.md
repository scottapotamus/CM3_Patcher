# CM3 Utils: saved game editing and scouting

The Editing and Scouting parts of CM3 Utils (.NET Framework 3.5, WinForms), for Championship Manager 3 saved games (`.sav`).
The Patcher part is described in the README of the parent folder.

Saved games are normally in `C:\Users\<you>\AppData\Local\VirtualStore\Program Files (x86)\Championship Manager 3\`
(Windows redirects the game's writes there when it is installed in Program Files).

## Clubs

The "Clubs" tab lists every club in the save (there is a search box) with its reputation, stadium, capacity and bank
balance. Pick a club and change its name, short name, reputation, bank balance (or use +1m / +10m / +50m), stadium name,
capacity and seated capacity, then press Set; the changed clubs are shown in yellow and written when you press Apply. A
stadium that several clubs use (some lower league clubs share one) is changed for all of them; 1,062 clubs have no stadium.
Balances can be anything from -2,000,000,000 to 2,000,000,000 (the game's amounts are mostly 32 bit).

- `club.dat` has 581 byte records: +0 id, +4 name (52 bytes, up to 50 characters are used), +56 short name (24 bytes), +105
  the id of the club's stadium (-1 = none) and +128 the club's reputation (signed 16 bit; 0-10,000, the biggest clubs are over
  9,000). The reputation is used by the function at 0x5009D0 in cm3.exe, which works out the Transfer Funds.
- `stadium.dat` has 78 byte records: +0 id (= the record number), +4 name (52 bytes), +56 the city, +60 capacity, +64 seated
  capacity (the game says "all seated" when the seated number is not below the capacity). Offset +68 is another number that
  isn't understood (it is below the capacity for most stadiums, so it isn't an expansion limit); it is left alone.
- The bank balance is a signed 64 bit number at offset 4 of the club's 355 byte record in `finance.dat` (the record number is
  the club's id). **Transfer Funds are not stored**: the game works them out from the balance and the club's wages and
  reputation (function at 0x5009D0 in cm3.exe), so give a club a bigger balance to give it more to spend. In a test, Barnet's
  Transfer Funds went from 30,000 to 2,300,000 when its balance went from 297,435 to 5,297,435.
- CM3 has no "training facilities" rating for clubs (there is no such text or setting in cm3.exe or the save; that is a
  Championship Manager 01/02 thing), so there is nothing to edit for it. Average attendance is a season statistic the game
  works out, not a stored number. The club record also has three numbers at +115, +119 and +123 that look like the size of the
  club's support (Manchester United 71,200 / 16,500 / 100,000, Crewe 9,500 / 1,500 / 9,500); what they do isn't known yet.
- Club and stadium names also appear in the game's language files (`eng.lng`...), which this editor doesn't change.

## Contracts

The "Contracts" tab has a Players and a Staff tab inside; the Players one lists the contracts of the players in the save (search by player or club; "Only with clauses" narrows it to the
ones that have clauses). Select one to change: the club (type its name; the list suggests clubs), the contract type (Full/Part Time, Contract, Monthly, Trial or Loan), the
wages per month, the expiry date, the squad status, the goal / assist / clean sheet bonuses (untick one to remove it)
and the clauses with their release fee. Press Set to keep the change; Apply writes everything. Unticking every clause
removes the contract's clauses (its fee is set to 0). Only existing clauses can be edited; clauses can be ticked on for
a contract that has none, but they all share the one release fee.

`contract.dat` is a count (u32) followed by that many 72 byte contract records, then other tables that are copied
through unchanged. In a record:

| Offset | Field |
| --- | --- |
| +0 | staff id (0xFFFFFFFF = unused slot) |
| +4 | club id |
| +12 | weekly wage (the game shows it x4 as "per month") |
| +16, +20, +24 | goal, assist, clean sheet bonus (-1 = none; the game rounds what it shows, e.g. 117 shows as 120) |
| +28..+32 | five clause flag bytes, one per clause type: Non Promotion, Minimum Fee, Non Playing, Relegation, Match Highest Player Wage (0 = none, 1 = present, 2 = triggered, for Non Promotion and Relegation) |
| +33 | the clause release fee, one number shared by all of the contract's clauses |
| +37, +45 | start date, expiry date: day of the year counted from 0 (u16), year (u16), flag (u32) |
| +53 | contract type: bit 7 = part time, bits 0-5 = 1 monthly, 2 normal, 5 trial, 6 or 7 loan |
| +71 | high nibble = squad status: 1 indispensable, 2 important first team player, 3 squad rotation, 4 backup, 5 hot prospect, 6 decent young player, 7 not needed (0 = uncertain) |

Editing changes only those bytes in place, so no lengths or offsets change. The expiry date and the wage are also kept
in the player's `staff.dat` record (+70 and +78), so the editor changes them there too (when they matched the contract's
old values). Other fields (+8, the tail bytes around +54..+70, pending transfer offers' own copies of the clauses) are not
understood and are left alone. Player names come from `staff.dat` (+4 first name id, +8 surname id) and the
`first_names.dat` / `second_names.dat` tables (60 byte records, the name first). A player with a second contract (about
440 of them) shows up once per contract.

## Scouting

The "Scouting" tab searches every player in the save (it only reads the save; Apply ignores it). Filter by name, club,
nation or "Free agents only", then add as many ranges as you like - "Potential ability between 150 and 200", "Age between
14 and 19", "Professionalism between 15 and 20" - each one adds a column to the results. A second range on the same number
replaces the first. Click a column heading to sort (all matches are sorted, the first 1000 are listed). Select a player to see
every number about him.

Searchable numbers: current and potential ability (0-200, hidden by the game), age (from the game's date), wages per month,
the 31 attributes the game shows, the hidden ones (ambition, loyalty, pressure, professionalism, sportsmanship, temperament)
and the 12 position ratings (goalkeeper, sweeper, defender, defensive midfielder, midfielder, attacking midfielder, striker,
wing back, right / left / centre, free role). The "Plays as" column lists the positions rated 15 or more, like the game's profile.

How the numbers are stored (checked against a profile screen in the game; every attribute matched):

- `player.dat` has a 70 byte record per player. His `staff.dat` record points to it: the number at +97 is the player record
  number (the first 4 bytes of a player record are that number too).
- Player record: +5 current ability (u16), +7 potential ability (u16), +15..+26 the position ratings (1-20), +27..+69 the
  attributes.
- Many attributes are the number the game shows. Anticipation, Flair, Heading, Passing, Positioning and Tackling, and the
  attackers' skills (Creativity, Crossing, Dribbling, Marking, Off The Ball, Shooting) and Handling / Reflexes are signed bytes
  the game scales with the current ability: `t = (2 * byte + ability) * 0.05 + 10`, shown = `trunc(clamp(1, 20, (t + 0.1 * t * t) / 3 + 0.5))`
  (the function at 0x4C9FE0 in cm3.exe). That is why a player's attributes grow with his ability. Goalkeepers show 1 in the
  attackers' skills and everybody who isn't one shows 1 in Handling and Reflexes.
- Staff record: adaptability +86, ambition +87, determination +88, loyalty +89, pressure +90, professionalism +91,
  sportsmanship +92, temperament +93; date of birth +16 (day of the year u16, year u16); nationality +26 (u16, the id in
  `nation.dat`, 290 byte records); club +57 (-1 = none); weekly wage +78. The game's date is in `general.dat` at +13 (day of the
  year u16, year u16). A player's common name (`common_names.dat`) is shown when he has one.
- A few bytes in the player record (+33, +34, +36, +37, +44, +45, +48, +49, +52, +53, +56, +59, +65, +69) aren't understood yet,
  and the hidden attributes other than the ones above (consistency, injury proneness...) aren't located.

Hidden attributes (current / potential ability and the hidden personality numbers) are off by default in both scouting tabs:
tick "Show hidden attributes" to search on them and show them in the table; unticking removes them from the dropdown, the
table and any search ranges that use them. The two scouting tabs share the setting.

## Staff scouting

The "Staff scouting" tab is the same search for managers, assistants, coaches, scouts, physios, directors and chairmen
(9,819 in a full save), with a Job filter. Searchable: current / potential ability (0-200, hidden by the game), age, wages
per month, the 12 staff attributes (Adaptability, Coaching Goalkeepers, Coaching Outfield Players, Determination, Judging
Player Ability / Potential, Level of Discipline, Man Management, Motivating, Physiotherapy, Tactical Knowledge, Working With
Youngsters) and the hidden personality numbers. Checked against Klinsmann's entry in Tri Wasano's editor (CA 140, PA 180,
Coaching Outfield Players 10).

- `nonplayer.dat` has a 68 byte record per member of staff: +0 its number (= the record number), +4 current ability (u16), +6
  potential ability (u16), then plain attribute bytes: coaching outfield players +16, coaching goalkeepers +17, level of
  discipline +20, judging player ability +23, judging player potential +24, man management +25, motivating +27,
  physiotherapy +30, tactical knowledge +33, working with youngsters +34. Adaptability and determination are in the staff
  record (+86, +88) as for players.
- The staff record points to its `nonplayer.dat` record with the number at +105 (+101 is the staff id, +97 the player record
  of players; a few players who are also player-managers have both). The job is the byte at +61: 0 unemployed, 1 chairman,
  2 managing director, 3 general manager, 4 director of football, 5 manager, 6 assistant manager, 7 reserve manager, 8 coach,
  9 scout, 10 physio, 11 player, 12 player/manager, 13 player/assistant manager, 15 player/coach.

Changing the club of a contract moves the person too (experimental): his staff record gets the new club and he is taken out of the
old club's lists and put in a free place in the new club's lists in `club.dat` (offsets +155..+471 of the club record, 4 bytes per
place, -1 = empty: +191 chairman, +195/+199/+203 managing director / general manager / director of football, +207 manager, +211
assistant manager, +215..+411 the players, +415..+431 coaches, +435..+459 scouts, +463..+471 physios). If the new club has no free
place for his job (e.g. it already has a manager) Apply stops with a message and writes nothing - move the one who is there
first. The game's other files that mention a person's club (e.g. squad lists in the competition files) aren't touched, so check
the result in the game; Apply writes `<name>_edited.sav` unless you tick Overwrite.

## Attributes

The "Attributes" tab (Players and Staff) edits the numbers of a player or a member of staff on a screen laid out like the game's
profile: the attributes in the game's three alphabetical columns with red / green bars, then the numbers the game hides (current
and potential ability, ambition, loyalty...) and, for players, the 12 position ratings. Pick someone on the left, change the
numbers; changed ones are marked in yellow ("Undo changes" puts someone back) and everything is written by Apply. In the game
Anticipation, Flair, Heading, Passing, Positioning, Tackling and the attackers' skills depend on the current ability (see Scouting):
the editor writes the byte that makes the game show the number you typed at the current ability you typed, so they stay as you
set them even if you change the ability. A goalkeeper shows 1 in the attackers' skills and everybody else shows 1 in Handling and
Reflexes; those boxes are disabled.

## Staff contracts

The Staff tab inside "Contracts" lists the contracts of everybody who is not a player (managers, assistants, coaches, scouts, physios,
directors; the Job column says which) and edits the contract type, the wages per month and the expiry date, and the club (see above), the same way as the
Players tab does for players (which also has squad status, bonuses and clauses). The changes of both tabs are written
together by Apply.

## Competitions

The "Competitions" tab lists the leagues and cups (clubs' and national teams') and renames them (name and short name). The names
are in `club_comp.dat` and `nation_comp.dat` (107 byte records: +0 id, +4 name (52 bytes), +56 short name (24 bytes), +93 the
nation); each competition's data is in a sub-file called `<name>_<id>.tmp`, which is renamed too (the game builds that name
when it opens the file). The prize money of a competition is not stored in the save (no prize table in any sub-file or in
cm3.exe's data), so it can't be edited. Names change in the saved game only.

## People: adding and removing players and staff

The "People" tab adds new players and staff and takes others out of the game; Apply writes them.

- **Add**: first name, surname, nation, date of birth, main position (player) or job (staff), club, wages and contract end,
  and all his numbers on a screen laid out like the Attributes tab (attributes, current / potential ability, hidden numbers, position ratings; the main position fills in the ratings and "Set every attribute to" gives all attributes one value). A new record is made in `staff.dat` and `player.dat` (or
  `nonplayer.dat` for staff) from the plainest free agent in the save (and the names are looked up in `first_names.dat` /
  `second_names.dat`, or added to them); a person with a club also gets a contract in `contract.dat` and a place in the club's
  lists (see Contracts). Without a club the person is a free agent. The numbers can still be changed on the Attributes tab once the saved
  game has been written and opened again.
- **Remove**: tick people on the list. They leave their club and its lists, their contracts end (the contract's staff number
  becomes -1) and their job becomes 16, which is how the game marks retired people (they stay in the files but are in no list
  of the game, and the editor doesn't list them).
- Not every file that mentions a person is updated (stats, history, squad lists in competition files), so check the result in
  the game; keep the original save (Apply writes `<name>_edited.sav` unless you tick Overwrite).

## Substitutes rules

CM3 stores the substitutes rules in every fixture when the fixture is created, so patching `cm3.exe` (the CM3 Patcher's
"Modern substitutes rules") only affects fixtures created afterwards. This tool rewrites the rules of all the fixtures
already in a save: pick the save, choose how many players are named on the bench and how many substitutions are allowed
(1-9 each; 9 is the most the game can hold), tick "Change the substitutes rules" and Apply.

- By default a new file `<name>_edited.sav` is written next to the original. Tick "Overwrite" to replace the save instead
  (the original is first copied to `<name>.sav.bak`, only if that file doesn't exist yet).
- Fixtures that allow no substitutes at all are left alone.
- After writing, the tool reads the new file back and checks it.
- Close the game first. Use the CM3 Patcher's substitutes option as well so later seasons' fixtures get the rules too.

## The file format (as far as it is understood)

```
12 bytes   header: version (4), ?, number of entries (95)
entries    268 bytes each: offset (4), uncompressed size (4), name (256, zero padded), 4 more bytes
...        some more data up to the first sub-file (copied through untouched)
contents   each entry's data runs from its offset to the next entry's offset (the last one to the end of the file)
```

Sub-files are the game's `.dat` files plus `.tmp` files for each competition, `fixtures_<year>.tmp`, and so on. They are
run-length compressed: a byte with the top bit set is a run (low 7 bits = count, next byte = value), `0x80` on its own is
a literal `0x80`, any other byte is a literal. Re-encoding a decoded sub-file reproduces the original bytes exactly.

`fixtures_<year>.tmp` is a 4 byte count followed by 82 byte records. The last two bytes of a record are the rules word:
bits 0-3 = substitutes named, bits 4-7 = substitutes allowed. The editor only changes that one byte (offset 0x50 in the
record), in the fixture lists, and keeps everything else as it was.

## Build

Build `CM3Utils.csproj` in the parent folder (Release) with VS/MSBuild. Output `bin\Release\CM3 Utils.exe`.

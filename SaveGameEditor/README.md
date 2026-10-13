# Scottapotamus CM3 Save Game Editor

A small standalone tool (.NET Framework 3.5, WinForms) for editing Championship Manager 3 saved games (`.sav`).
It shares no code with the CM3 Patcher in the parent folder.

Saved games are normally in `C:\Users\<you>\AppData\Local\VirtualStore\Program Files (x86)\Championship Manager 3\`
(Windows redirects the game's writes there when it is installed in Program Files).

## Club finances

The "Club finances" tab lists every club in the save with its bank balance. Pick a club (there is a search box), type
a new balance and press Set, or use +1m / +10m / +50m. The changes are collected and written when you press Apply.
Balances can be anything from -2,000,000,000 to 2,000,000,000 (the game's amounts are mostly 32 bit).

The balance is a signed 64 bit number at offset 4 of the club's 355 byte record in `finance.dat` (the record number is
the club's id; names come from `club.dat`, 581 byte records). **Transfer Funds are not stored**: the game works them
out from the balance and the club's wages and reputation (function at 0x5009D0 in cm3.exe), so give a club a bigger
balance to give it more to spend. In a test, Barnet's Transfer Funds went from 30,000 to 2,300,000 when its balance went
from 297,435 to 5,297,435.

## Contracts

The "Contracts" tab lists the contracts in the save (search by player or club; "Only with clauses" narrows it to the
ones that have clauses). Select one to change: the contract type (Full/Part Time, Contract, Monthly, Trial or Loan), the
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

Build `SaveGameEditor.csproj` (Release) with VS/MSBuild. Output `bin\Release\Scottapotamus CM3 Save Game Editor.exe`.

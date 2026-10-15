# CM3 Utils by Scottapotamus

One app for Championship Manager 3 (v3.04e): the **Patcher** (this document), the saved game **Editing** and **Scouting**
(see `SaveGameEditor\README.md`). Start it and pick one on the first screen or with the buttons in the red title bar.

The Patcher is a tool that lets Championship Manager 3 (v3.04e, `cm3.exe`) run at resolutions other than 800x600, with
smoother scaling, sharper fonts and an optional modern-text mode.
It is independent of the CM0102 patcher in the parent folder.

## Usage

1. Build `CM3Utils.csproj` (Release) in Visual Studio, then run `bin\Release\CM3 Utils.exe` and choose **Patcher**.
   CM3 is normally installed in Program Files, which needs administrator rights to write to: if the patcher can't write
   there it offers to restart CM3 Utils as administrator (the rest of the app doesn't need them).
2. Select `cm3.exe` (the default install location is detected automatically). The line under it says what is in the game now.
3. The settings are on four tabs, each with its own **Apply** button that changes only the settings of that tab (everything
   else stays as it is in the game):
   - **Graphics**: the resolution (pick one or type `WIDTHxHEIGHT`), the scaling filter, font sharpening, and Modern text with
     its font (the font list is shown in the fonts themselves, with a preview).
   - **Interface**: the + / - and scroll button fix.
   - **Game rules**: modern substitutes rules (new games).
   - **Enhancements**: the attribute highlights and the squad Clear button (these need Modern text to be in the game already).
   The scaling filter and font strength can't be read back from the exe, so they are remembered in `cm3utils.ini` next to it.

## How it works

CM3 renders every screen into a fixed 800x600 DirectDraw surface, so there are no layout values to change like in CM0102.
Instead the patch:

* makes the game window / full-screen display mode the requested size,
* stretches the 800x600 surface onto it (aspect ratio preserved, centred, black bars if the shape differs). You can choose
  the filter: *High quality* (GDI+ bicubic, the default), *Crisp* (a whole-number nearest-neighbour upscale followed by a
  bicubic downscale, slightly sharper), *Smooth* (GDI halftone) or *Sharp* (DirectDraw's blocky nearest-neighbour
  stretch). The GDI+ modes fall back to Smooth automatically if GDI+ isn't available, and
* maps the mouse position back into 800x600 coordinates.

The new code is placed in a small extra executable section (`.cm3res`) appended to `cm3.exe`, and a handful of places in
the original code are redirected to it. See `CM3ResolutionChanger.cs` (generated - see *Rebuilding* below).

## Font sharpening

CM3's fonts (`Data\arial_*.fnt`, `Data\trade_cond_*.fnt`) are pre-drawn 4-bit glyph bitmaps whose edges are already soft,
so no scaling filter can make them look crisp. The patcher can tighten every glyph's edges (a contrast curve on its
intensities) while leaving every width and offset alone, so the layout doesn't change. *Medium* is the recommended
setting, *Strong* is crisper still. The originals are saved next to the fonts as `*.fnt.orig` and choosing *None*
restores them.

## Modern text (experimental)

With **Modern text** ticked, the game's text is drawn with a TrueType font (Segoe UI by default) at the real screen
resolution instead of being enlarged from the 800x600 bitmap letters. Layout is unchanged: positions, widths and
spacing come from the game's own font data.

How it works: the patched `cm3.exe` loads `cm3text.dll` (put next to it by the patcher) and wraps the game's `DrawString`
routine. The game still draws its own soft text into its 800x600 surface, so everything it does with that surface
(saving and restoring screen areas, painting over text, ...) keeps working. Around each `DrawString` the DLL keeps the
pixels under the text before and after. Just before the surface is stretched onto the screen, every text item whose
soft pixels are still intact has its clean background put back; after the stretch the DLL draws the text again with the
TrueType font and puts the soft pixels back, so the game's surface ends up exactly as it was. Text that has been painted
over (or dimmed) no longer matches and keeps the game's own soft rendering. If `cm3text.dll` is missing the game just
draws its own text.

Settings live in `cm3text.ini` next to the exe (the patcher writes the font for you):

```
[text]
; the font for all text
font=Segoe UI
; the two big heading fonts (drawn bold)
titlefont=Segoe UI
; override one of the game's fonts (0-6): its face, weight and letter width in percent (0 = fit automatically)
font5=Arial Narrow
weight5=700
condense5=80
; gray (default) or cleartype
quality=gray
; write cm3text.log next to the exe
debug=1
```

(Comments must be on their own lines.)

Needs one of the GDI+ scaling modes (it is switched on automatically). Not available at 800x600.

### Squad screen: Clear button

The game has no way to unselect the whole team: you have to click each position tag (SB1, GK, DMC...) of the squad screen one
by one. With Modern text on, the overlay adds a **Clear** button next to Sort By on the squad screen. Clicking it clicks every
position tag on the screen for you (synthetic mouse clicks sent to the game's own window, one every ~45 ms, so the game does
the unselecting and redraws itself; it takes a couple of seconds). It is a patcher option ("Squad screen: a Clear button"), saved
as `[squad] clearbutton=1` in `cm3text.ini`. It shows on any squad list (also other clubs', where it does nothing, because there
are no tags). Needs a scaled resolution, like the rest of Modern text.

### Player attribute highlighting

On the player profile screen (and anywhere the game lists the 31 attributes) the overlay puts a highlight behind the value, a
bit like Football Manager's attribute view. The values keep the game's own text colours.

By default every attribute is coloured by its **value**: above 14 in the **key** colour (red by default), 10-14 in the
**secondary** colour (green) and below 10 in the **other** colour (grey). The highlight fills the game's value cell in
proportion to the value (20 = the whole cell, 10 = half), and the key ones can also get an outline round the cell (off by
default). All the colours are configurable in the patcher.

Or choose to colour them by the **player's position** instead: the overlay reads the position label the game draws on
the profile ("Goalkeeper", "Defender (Right)", "Defender/Midfielder (Right/Centre)", ...) and colours every attribute by
what it means for that role: **key** attributes (red by default), **secondary** ones (green) and all the **other** attributes
(grey). All three colours are configurable in the patcher. The highlight fills the game's value cell in proportion to the
value (20 = the whole cell, 10 = half), and key attributes can also get an outline round the cell (off by default,
colour configurable). If the position isn't recognised, only the player's best attributes (at or above a threshold,
13 by default) are highlighted, in the key colour. The tiers are editable in
`cm3text.cpp`, `ROLES` (names are the game's own: "Off The Ball" is movement, "Shooting" covers finishing and long shots,
"Set Pieces" is free kicks):

| Role | Key | Secondary (plus Determination) |
| --- | --- | --- |
| Goalkeeper | Anticipation, Handling | Acceleration, Agility, Jumping, Pace, Passing, Positioning, Reflexes, Strength |
| Sweeper | Marking, Passing, Tackling | Heading, Jumping, Pace, Positioning, Stamina, Strength |
| Defender (centre) | Marking, Tackling | Heading, Jumping, Pace, Positioning, Stamina, Strength |
| Defender (Right/Left) | Acceleration, Agility, Dribbling, Marking, Passing | Heading, Jumping, Pace, Positioning, Stamina, Strength |
| Defensive Midfielder | Marking, Passing, Tackling | Acceleration, Heading, Jumping, Pace, Stamina, Strength, Teamwork |
| Midfielder (centre) | Anticipation, Balance, Dribbling, Flair, Passing, Stamina, Technique | Acceleration, Heading, Jumping, Pace, Strength, Teamwork |
| Midfielder (Right/Left) | Midfielder and Winger combined | Midfielder and Winger combined |
| Attacking Midfielder (any side) | Dribbling, Flair, Shooting, Off The Ball, Passing | Acceleration, Heading, Jumping, Pace, Stamina, Strength, Teamwork |
| Attacker (Right/Left) | Acceleration, Agility, Crossing, Dribbling, Pace | Flair, Heading, Jumping, Stamina, Strength, Teamwork |
| Forward (Right/Left) | Winger and Striker combined | Winger and Striker combined |
| Striker (and Forward/Attacker in the centre) | Shooting, Pace | Crossing, Dribbling, Flair, Set Pieces, Heading, Jumping, Off The Ball, Passing |

A player with several roles or sides (e.g. "Defender/Midfielder (Right/Centre)") gets the union. If the label isn't
recognised the bars simply go on his best attributes. It matches English text only. Settings (the patcher writes them
for you):

```
[attributes]
enabled=1
; position = colour by what matters for the player's position, value = colour by the value (15+ key, 10-14 secondary, below 10 other)
mode=value
; used only when the position isn't known: highlight values at or above this
highlightfrom=13
; bar opacity, 0-255
alpha=130
; highlight colours as RRGGBB hex (the patcher has colour pickers)
keycolour=BB4444
secondarycolour=37B928
othercolour=666666
; 1 = outline the key attributes' cells, 0 = no outline
border=0
; outline colour
bordercolour=EBCD5A
```

## Substitutes rules

CM3 allows 5 players on the bench (some leagues 7) and 3 substitutions, set per competition in the game's code. The
patcher can give **every competition** modern rules instead: up to **9 named** on the bench and up to **9 substitutions**
(the defaults it offers are 9 and 5). It replaces the six places in `cm3.exe` where the rules are read (three code
blocks that build every league's and cup's rules) with fixed numbers; choosing "off" or 800x600 with nothing else
selected puts the original bytes back. 9 is the maximum because the game holds 20 players per team (11 + 9).

The rules are stored in each fixture when it is created, so the change applies to **new games** (and fixtures created
after loading), not to matches already in a saved game's fixture list.

## + / - and scroll buttons repeating too fast

On a modern PC, a short click on a repeating button (the + / - buttons for transfers and contracts, the scroll arrows
in lists) acts like a long hold: one 60 ms tap on a list's scroll arrow jumped about a page. This happens in the
original, unpatched game. The game has no timing of its own for these buttons: its "hold the button to repeat" loops
pump messages once per pass and otherwise spin as fast as the CPU allows (it runs at 100% of a core), and the mouse-up
message is only noticed when the pump runs.

The fix hooks the end of the game's message pump (`0x5A1F27`) and makes it wait a few milliseconds per pass **only while
the left mouse button is down**. Taps now give exactly one step and a long hold still repeats, at a steady rate. Nothing
else is slowed (loading, matches and idle behaviour are untouched). The wait defaults to 60 ms; raise it for slower
repeats. It is independent of the resolution settings, so it works at 800x600 too.

## Notes

* Only the `cm3.exe` v3.04e is supported; the patcher checks the bytes before touching anything.
* CM3 is a 4:3 game, so 4:3 resolutions (1024x768, 1600x1200, ...) fill the screen with no black bars.
* In windowed mode the window must fit on your screen (height plus about 30 pixels for the title bar).
* Full-screen mode needs your graphics driver to offer the chosen size in 16-bit colour. It is implemented but has
  only been tested in windowed mode, as has the modern-text mode.
* Modern text has been tried on the menus, dialogs, league selection and file selection screens, not yet on in-game
  screens such as squads, tactics or the match view.

## Rebuilding

* The patcher: build `CM3Utils.csproj` with Visual Studio / MSBuild (.NET Framework 3.5). It embeds
  `TextOverlay\cm3text.dll`.
* `TextOverlay\cm3text.dll`: run `TextOverlay\build.bat` (needs the Visual Studio Build Tools with the C++ workload). The
  built DLL is checked in, so you only need this if you change `cm3text.cpp`.
* `CM3ResolutionChanger.cs` is generated from `Tools\CM3ResolutionChanger.template.cs` by `Tools\generate_changer.py`
  (needs Python with the `keystone-engine` package):
  `python Tools\generate_changer.py Tools\CM3ResolutionChanger.template.cs CM3ResolutionChanger.cs`

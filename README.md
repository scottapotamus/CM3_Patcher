# Scottapotamus CM3 Patcher

A standalone tool that lets Championship Manager 3 (v3.04e, `cm3.exe`) run at resolutions other than 800x600, with
smoother scaling, sharper fonts and an optional modern-text mode.
It is independent of the CM0102 patcher in the parent folder.

## Usage

1. Build `CM3ResolutionPatcher.csproj` (Release) in Visual Studio, then run `bin\Release\Scottapotamus CM3 Patcher.exe`
   (it asks for administrator rights because CM3 is normally installed in Program Files).
2. Select `cm3.exe` (the default install location is detected automatically).
3. Pick a resolution, or type your own as `WIDTHxHEIGHT`, a scaling filter, and the font options, then click **Apply**.
4. Choose **800x600** at any time to restore the original game.

A copy of the untouched exe is saved as `cm3_original.exe` first (unless you untick the box).

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

### Player attribute highlighting

On the player profile screen (and anywhere the game lists the 31 attributes) the overlay colours each value by how
good it is - dim for poor (1-4), grey (5-8), yellow (9-12), light green (13-15), bright green (16-20) - and puts a bar
behind the row, a bit like Football Manager's attribute view.

By default the bars are **position aware**: the overlay reads the position label the game draws on the profile
("Goalkeeper", "Defender (Right)", "Defender/Midfielder (Right/Centre)", ...) and highlights only the attributes that
matter for that role: **green** if the value is at or above the threshold (13 by default), **blue** if it is a key
attribute but weak. Attributes that don't matter for the position get no bar. Key attributes per role (editable in
`cm3text.cpp`, `ROLES`):

| Role | Key attributes |
| --- | --- |
| Goalkeeper | Handling, Reflexes, Positioning, Agility, Anticipation, Bravery |
| Sweeper | Positioning, Anticipation, Tackling, Marking, Heading, Passing |
| Defender | Tackling, Marking, Positioning, Anticipation, Bravery; centre: Heading, Jumping, Strength; wide (Right/Left): Pace, Acceleration, Stamina, Crossing |
| Defensive Midfielder | Tackling, Positioning, Anticipation, Teamwork, Work Rate, Stamina, Passing, Marking |
| Midfielder | Passing, Technique, Teamwork, Work Rate, Stamina, Creativity, Positioning; wide: Pace, Crossing, Dribbling, Acceleration |
| Attacking Midfielder | Creativity, Passing, Technique, Flair, Dribbling, Off The Ball, Shooting; wide: Pace, Crossing, Acceleration |
| Striker | Shooting, Off The Ball, Heading, Pace, Acceleration, Technique, Strength, Dribbling |

A player with several roles or sides (e.g. "Defender/Midfielder (Right/Centre)") gets the union. If the label isn't
recognised (or `roleaware=0`) the bars simply go on his best attributes. It matches English text only. Settings (the
patcher writes them for you):

```
[attributes]
enabled=1
; 1 = highlight what matters for the position, 0 = just the best attributes
roleaware=1
; a value at or above this is "strong" (green bar)
highlightfrom=13
; bar opacity, 0-255
alpha=130
```

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

* The patcher: build `CM3ResolutionPatcher.csproj` with Visual Studio / MSBuild (.NET Framework 3.5). It embeds
  `TextOverlay\cm3text.dll`.
* `TextOverlay\cm3text.dll`: run `TextOverlay\build.bat` (needs the Visual Studio Build Tools with the C++ workload). The
  built DLL is checked in, so you only need this if you change `cm3text.cpp`.
* `CM3ResolutionChanger.cs` is generated from `Tools\CM3ResolutionChanger.template.cs` by `Tools\generate_changer.py`
  (needs Python with the `keystone-engine` package):
  `python Tools\generate_changer.py Tools\CM3ResolutionChanger.template.cs CM3ResolutionChanger.cs`

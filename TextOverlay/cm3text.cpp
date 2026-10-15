// cm3text.dll - draws Championship Manager 3's text with a TrueType font at the real screen resolution.
//
// cm3.exe is patched (see CM3ResolutionChanger.cs) so that it calls into this DLL:
//   * before and after every call of the game's DrawString routine (Cm3_PreDraw / Cm3_PostDraw);
//   * before the 800x600 game surface is stretched onto the window or screen (Cm3_BeforeStretch);
//   * after the stretch, with the device context it was drawn to (Cm3_AfterStretch);
//   * once the whole present is over (Cm3_Rebake).
//
// The game still draws its own soft bitmap text into its 800x600 surface, so all of its own bookkeeping (saving and
// restoring parts of the screen, painting over text, ...) keeps working untouched. Around every DrawString we keep
//   background : the surface pixels under the text before the game drew it
//   baked      : the same pixels after it drew its soft text
// Before a stretch, every text item whose "baked" pixels are still exactly what is on the surface is visible, so we
// temporarily put its clean "background" back (newest first, which also peels off drop shadows correctly). After the
// stretch we draw the text again with a TrueType font at the real resolution and put the soft pixels back, so the game's
// surface ends up exactly as it was. Text that has been painted over no longer matches and is simply not drawn.
//
// Positions, widths and spacing come from the game's own font data and text routines, so the layout is the original.
//
// The squad screen also gets a "Clear" button (next to Sort By): the game has no way to unselect the whole team except
// clicking every position tag one by one, so the button does exactly that for you, with synthetic mouse clicks sent to
// the game's own window procedure (so the game does all the work and redraws itself).

#include <windows.h>
#include <ddraw.h>
#include <string>
#include <vector>
#include <map>
#include <math.h>
#include <stdio.h>
#include <stdarg.h>
#include <stdlib.h>

#pragma comment(lib, "msimg32.lib")

// ---- addresses inside cm3.exe v3.04e (image base 0x400000, no relocation) ----
static const unsigned FONT_TABLE = 0x83b7f8;        // one block of 0x1404 bytes per font
static const unsigned FONT_STRIDE = 0x1404;
static const int FONT_COUNT = 7;
static IDirectDrawSurface** const GAME_SURFACE = (IDirectDrawSurface**)0x844428;   // the 800x600 off-screen surface
static const int SURFACE_WIDTH = 800;
static const int SURFACE_HEIGHT = 600;

typedef int (__cdecl *KernFn)(int font, int index, const char* text);
static KernFn GameKern = (KernFn)0x535b20;          // gap between letter index-1 and index

static const int MAX_ITEMS = 700;

struct Item
{
    int x, y, font;
    unsigned short colour;
    int underline;
    std::string text;
    RECT bounds;                            // area of the surface the text covers (clipped to the surface)
    std::vector<unsigned short> background; // surface pixels there before the game drew the text
    std::vector<unsigned short> baked;      // ... and after
    bool lifted;                            // currently showing its clean background (between before/after stretch)

    // Player attribute rows ("Pace   15"): set on the number when it is found next to an attribute name
    int attrValue;                          // 1-20, or -1 if this isn't an attribute value
    int barLeft;                            // left edge (game pixels) of the row, where the attribute name starts
    int attrName;                           // index into ATTRIBUTE_NAMES of the row's attribute
};

struct FontInfo
{
    bool ready;
    int cellHeight;         // rows per glyph in the game's bitmap font
    int baselineRow;        // row (0 = top of cell) just below the capital letters
    int capHeight;          // height of capital letters in game pixels
    std::wstring face;
    int weight;
    double condense;        // horizontal stretch of the letters (1 = natural, 0 = fit to the game's letter widths)
};

static CRITICAL_SECTION g_lock;
static bool g_lockReady = false;
static bool g_initialised = false;
static bool g_working = false;
static std::vector<Item*> g_items;
static Item* g_pending = NULL;
static FontInfo g_fonts[FONT_COUNT];
static int g_quality = ANTIALIASED_QUALITY;
static bool g_debug = false;
static wchar_t g_logPath[MAX_PATH];

// Attribute highlighting (settings from the [attributes] section of the ini file)
static bool g_attrEnabled = true;
static int g_attrBarFrom = 13;              // rows with a value at or above this get a highlight bar
static int g_attrBarAlpha = 130;            // 0-255
static COLORREF g_attrKeyColour = RGB(187, 68, 68);       // highlight colour: attributes that are key for the position
static COLORREF g_attrSecondaryColour = RGB(55, 185, 40); // highlight colour: secondary attributes
static COLORREF g_attrOtherColour = RGB(102, 102, 102);   // highlight colour: all the other attributes
static COLORREF g_attrBorderColour = RGB(235, 205, 90);  // outline around the boxes of key attributes
static const int ATTR_CELL_OFFSET = 133;     // game pixels from the start of the attribute name to the left edge of its value cell
static const int ATTR_CELL_WIDTH = 90;       // game pixels: width of the value cell
static bool g_attrBorder = false;             // outline the boxes of key attributes
static bool g_attrByValue = true;           // colour by the attribute's value instead of by what matters for the position
static const int ATTR_KEY_ABOVE = 14;       // by value: above this is "key"
static const int ATTR_SECONDARY_FROM = 10;  // by value: this up to ATTR_KEY_ABOVE is "secondary", below it is "other"

static bool g_clearEnabled = true;          // the squad screen's "Clear" button ([squad] clearbutton in the ini file)

struct CachedFont { HFONT hfont; };
static CachedFont g_hfonts[FONT_COUNT];
static std::map<unsigned, ABC> g_abcCache[FONT_COUNT];
static double g_cachedScale = 0;

static void Log(const char* format, ...)
{
    if (!g_debug)
        return;
    char buffer[512];
    va_list args;
    va_start(args, format);
    vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    HANDLE file = CreateFileW(g_logPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file != INVALID_HANDLE_VALUE)
    {
        DWORD written;
        WriteFile(file, buffer, (DWORD)strlen(buffer), &written, NULL);
        CloseHandle(file);
    }
}

// ---------------------------------------------------------------- the game's 800x600 surface

struct SurfaceLock
{
    unsigned char* bits;
    int pitch;
    bool ok;

    SurfaceLock() : bits(NULL), pitch(0), ok(false)
    {
        IDirectDrawSurface* surface = *GAME_SURFACE;
        if (!surface)
            return;

        DDSURFACEDESC desc;
        ZeroMemory(&desc, sizeof(desc));
        desc.dwSize = sizeof(desc);
        if (surface->Lock(NULL, &desc, DDLOCK_SURFACEMEMORYPTR, NULL) != DD_OK)
            return;
        if (desc.ddpfPixelFormat.dwRGBBitCount != 16)
        {
            surface->Unlock(NULL);
            return;
        }
        bits = (unsigned char*)desc.lpSurface;
        pitch = desc.lPitch;
        ok = true;
    }

    ~SurfaceLock()
    {
        if (ok)
            (*GAME_SURFACE)->Unlock(NULL);
    }

    unsigned short* Row(int y) { return (unsigned short*)(bits + y * pitch); }
};

static void CopyOut(SurfaceLock& s, const RECT& r, std::vector<unsigned short>& out)
{
    int w = r.right - r.left, h = r.bottom - r.top;
    out.resize(w * h);
    for (int y = 0; y < h; y++)
        memcpy(&out[y * w], s.Row(r.top + y) + r.left, w * 2);
}

static void CopyIn(SurfaceLock& s, const RECT& r, const std::vector<unsigned short>& in)
{
    int w = r.right - r.left, h = r.bottom - r.top;
    for (int y = 0; y < h; y++)
        memcpy(s.Row(r.top + y) + r.left, &in[y * w], w * 2);
}

static bool Equal(SurfaceLock& s, const RECT& r, const std::vector<unsigned short>& pixels)
{
    int w = r.right - r.left, h = r.bottom - r.top;
    for (int y = 0; y < h; y++)
        if (memcmp(s.Row(r.top + y) + r.left, &pixels[y * w], w * 2) != 0)
            return false;
    return true;
}

// ---------------------------------------------------------------- the game's bitmap fonts

static unsigned char* GlyphRecord(int font, int c)
{
    return (unsigned char*)(FONT_TABLE + font * FONT_STRIDE + 4 + c * 20);
}

static int GlyphWidth(int font, int c)
{
    return *(int*)GlyphRecord(font, c);
}

// Same maths as the game's own DrawString: the advance of each character (0 for skipped control characters)
static void Advances(int font, const char* text, std::vector<int>& adv)
{
    adv.clear();
    for (int i = 0; text[i]; i++)
    {
        unsigned char c = (unsigned char)text[i];
        if (c == 0x7c)
            c = 0x20;
        if (c < 0x20)
        {
            adv.push_back(0);
            continue;
        }
        adv.push_back(GlyphWidth(font, c) + GameKern(font, i + 1, text));
    }
}

static std::wstring Widen(const char* text)
{
    std::wstring w;
    for (int i = 0; text[i]; i++)
    {
        wchar_t wc = 0;
        unsigned char c = (unsigned char)text[i];
        if (c == 0x7c)
            c = 0x20;
        MultiByteToWideChar(1252, 0, (const char*)&c, 1, &wc, 1);
        w.push_back(wc);
    }
    return w;
}

static bool g_green6 = true;        // 565 (true) or 555 pixel format; the game's colour arguments use the surface's format

static COLORREF ToColorRef(unsigned short c)
{
    int r = (c >> 11) & 31, b = c & 31;
    if (g_green6)
    {
        int g = (c >> 5) & 63;
        return RGB((r * 255 + 15) / 31, (g * 255 + 31) / 63, (b * 255 + 15) / 31);
    }
    r = (c >> 10) & 31;
    int g = (c >> 5) & 31;
    return RGB((r * 255 + 15) / 31, (g * 255 + 15) / 31, (b * 255 + 15) / 31);
}

// Measure the capital letters of a bitmap font (using the letter H) so the TrueType font can be sized to match
static void MeasureFont(int font)
{
    FontInfo& f = g_fonts[font];
    f.ready = false;

    int base = FONT_TABLE + font * FONT_STRIDE;
    f.cellHeight = *(int*)base;
    if (f.cellHeight <= 0 || f.cellHeight > 100)
        return;

    unsigned char* rec = GlyphRecord(font, 'H');
    int widthBytes = *(int*)(rec + 4);
    unsigned char* bits = *(unsigned char**)(rec + 0x10);
    if (!bits || widthBytes <= 0)
        return;

    int top = -1, bottom = -1;
    for (int row = 0; row < f.cellHeight; row++)
    {
        bool any = false;
        for (int b = 0; b < widthBytes; b++)
            if (bits[row * widthBytes + b])
                any = true;
        if (any)
        {
            if (top < 0)
                top = row;
            bottom = row;
        }
    }
    if (top < 0)
        return;

    f.baselineRow = bottom + 1;
    f.capHeight = bottom - top + 1;
    f.ready = true;
}

// Reads a colour setting written as RRGGBB hex; leaves the colour alone if it isn't there
static void ReadColour(const wchar_t* path, const wchar_t* key, COLORREF& colour)
{
    wchar_t text[16];
    GetPrivateProfileStringW(L"attributes", key, L"", text, 16, path);
    if (wcslen(text) == 6)
    {
        unsigned rgb = wcstoul(text, NULL, 16);
        colour = RGB((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }
}

static void LoadConfig()
{
    wchar_t path[MAX_PATH];
    HMODULE self = NULL;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       (LPCWSTR)&LoadConfig, &self);
    GetModuleFileNameW(self, path, MAX_PATH);
    wchar_t* dot = wcsrchr(path, L'.');
    if (dot)
        wcscpy_s(dot, MAX_PATH - (dot - path), L".ini");

    wchar_t value[128];

    g_debug = GetPrivateProfileIntW(L"text", L"debug", 0, path) != 0;
    wcscpy_s(g_logPath, path);
    wchar_t* logDot = wcsrchr(g_logPath, L'.');
    if (logDot)
        wcscpy_s(logDot, MAX_PATH - (logDot - g_logPath), L".log");
    if (g_debug)
        DeleteFileW(g_logPath);

    // Defaults: Segoe UI everywhere (bold for the two big title fonts), letter widths fitted to the game's
    for (int i = 0; i < FONT_COUNT; i++)
    {
        g_fonts[i].face = L"Segoe UI";
        g_fonts[i].weight = (i >= 5) ? FW_BOLD : FW_NORMAL;
        g_fonts[i].condense = 0;        // 0 = fit the letter widths to the game's automatically
    }

    GetPrivateProfileStringW(L"text", L"font", L"", value, 128, path);
    if (value[0])
        for (int i = 0; i < 5; i++)
            g_fonts[i].face = value;

    GetPrivateProfileStringW(L"text", L"titlefont", L"", value, 128, path);
    if (value[0])
        for (int i = 5; i < FONT_COUNT; i++)
            g_fonts[i].face = value;

    wchar_t name[32];
    for (int i = 0; i < FONT_COUNT; i++)
    {
        wsprintfW(name, L"font%d", i);
        GetPrivateProfileStringW(L"text", name, L"", value, 128, path);
        if (value[0])
            g_fonts[i].face = value;

        wsprintfW(name, L"weight%d", i);
        int weight = GetPrivateProfileIntW(L"text", name, 0, path);
        if (weight > 0)
            g_fonts[i].weight = weight;

        wsprintfW(name, L"condense%d", i);
        int percent = GetPrivateProfileIntW(L"text", name, 0, path);
        if (percent > 0)
            g_fonts[i].condense = percent / 100.0;
    }

    g_attrEnabled = GetPrivateProfileIntW(L"attributes", L"enabled", 1, path) != 0;
    g_attrBarFrom = GetPrivateProfileIntW(L"attributes", L"highlightfrom", 13, path);
    g_attrBarAlpha = GetPrivateProfileIntW(L"attributes", L"alpha", 130, path);
    GetPrivateProfileStringW(L"attributes", L"mode", L"value", value, 128, path);
    g_attrByValue = _wcsicmp(value, L"value") == 0;
    g_attrBorder = GetPrivateProfileIntW(L"attributes", L"border", 0, path) != 0;

    // highlight colours as RRGGBB hex
    ReadColour(path, L"keycolour", g_attrKeyColour);
    ReadColour(path, L"secondarycolour", g_attrSecondaryColour);
    ReadColour(path, L"othercolour", g_attrOtherColour);
    ReadColour(path, L"bordercolour", g_attrBorderColour);

    g_clearEnabled = GetPrivateProfileIntW(L"squad", L"clearbutton", 1, path) != 0;

    GetPrivateProfileStringW(L"text", L"quality", L"gray", value, 128, path);
    g_quality = (_wcsicmp(value, L"cleartype") == 0) ? CLEARTYPE_QUALITY : ANTIALIASED_QUALITY;
}

// Height in pixels of a capital H for a font at a given pixel size, measured with the real outline
static double CapRatio(HDC hdc, const std::wstring& face, int weight)
{
    LOGFONTW lf = {};
    lf.lfHeight = -200;
    lf.lfWeight = weight;
    lf.lfCharSet = DEFAULT_CHARSET;
    lf.lfQuality = ANTIALIASED_QUALITY;
    wcscpy_s(lf.lfFaceName, face.c_str());
    HFONT font = CreateFontIndirectW(&lf);
    HGDIOBJ old = SelectObject(hdc, font);

    GLYPHMETRICS gm = {};
    MAT2 identity = { {0, 1}, {0, 0}, {0, 0}, {0, 1} };
    double ratio = 0.7;
    if (GetGlyphOutlineW(hdc, L'H', GGO_METRICS, &gm, 0, NULL, &identity) != GDI_ERROR && gm.gmptGlyphOrigin.y > 0)
        ratio = gm.gmptGlyphOrigin.y / 200.0;

    SelectObject(hdc, old);
    DeleteObject(font);
    return ratio;
}

static HFONT GetFont(HDC hdc, int font, double scale)
{
    if (scale != g_cachedScale)
    {
        for (int i = 0; i < FONT_COUNT; i++)
        {
            if (g_hfonts[i].hfont)
                DeleteObject(g_hfonts[i].hfont);
            g_hfonts[i].hfont = NULL;
            g_abcCache[i].clear();
        }
        g_cachedScale = scale;
    }

    if (g_hfonts[font].hfont)
        return g_hfonts[font].hfont;

    FontInfo& f = g_fonts[font];
    double ratio = CapRatio(hdc, f.face, f.weight);
    int pixelSize = (int)floor(f.capHeight * scale / ratio + 0.5);
    if (pixelSize < 4)
        pixelSize = 4;

    LOGFONTW lf = {};
    lf.lfHeight = -pixelSize;
    lf.lfWeight = f.weight;
    lf.lfCharSet = DEFAULT_CHARSET;
    lf.lfQuality = (BYTE)g_quality;
    wcscpy_s(lf.lfFaceName, f.face.c_str());

    // Fit the letter widths to the game's: measure a sample sentence in the game's bitmap font and in this font, and
    // stretch or squeeze this font's letters by the difference (unless the ini file says otherwise)
    {
        static const char sample[] = "The quick brown fox jumps over the lazy dog 0123456789";
        std::vector<int> adv;
        Advances(font, sample, adv);
        double gameWidth = 0;
        for (size_t i = 0; i < adv.size(); i++)
            gameWidth += adv[i] * scale;

        HFONT natural = CreateFontIndirectW(&lf);
        HGDIOBJ old = SelectObject(hdc, natural);
        TEXTMETRICW tm = {};
        GetTextMetricsW(hdc, &tm);
        SIZE size = {};
        std::wstring wideSample = Widen(sample);
        GetTextExtentPoint32W(hdc, wideSample.c_str(), (int)wideSample.size(), &size);
        SelectObject(hdc, old);
        DeleteObject(natural);

        double condense = f.condense;
        if (condense <= 0 && size.cx > 0)
        {
            condense = gameWidth / size.cx;
            if (condense < 0.6)
                condense = 0.6;
            if (condense > 1.25)
                condense = 1.25;
        }
        Log("font %d: '%S' %dpx weight %d, game %.1f vs %d -> width factor %.3f\n", font, f.face.c_str(), pixelSize, f.weight, gameWidth, (int)size.cx, condense);
        if (condense > 0 && fabs(condense - 1.0) > 0.01)
            lf.lfWidth = (LONG)floor(tm.tmAveCharWidth * condense + 0.5);
    }

    g_hfonts[font].hfont = CreateFontIndirectW(&lf);
    return g_hfonts[font].hfont;
}

static void Initialise()
{
    if (g_initialised)
        return;
    g_initialised = true;

    LoadConfig();

    bool any = false;
    for (int i = 0; i < FONT_COUNT; i++)
    {
        MeasureFont(i);
        if (g_fonts[i].ready)
            any = true;
    }

    // Is the surface 565 or 555? (the game's text colours use the same format)
    IDirectDrawSurface* surface = *GAME_SURFACE;
    if (surface)
    {
        DDSURFACEDESC desc;
        ZeroMemory(&desc, sizeof(desc));
        desc.dwSize = sizeof(desc);
        if (surface->GetSurfaceDesc(&desc) == DD_OK)
            g_green6 = desc.ddpfPixelFormat.dwGBitMask == 0x7E0;
    }
    else
        any = false;

    g_working = any;
    Log("init: working=%d green6=%d\n", any ? 1 : 0, g_green6 ? 1 : 0);
    for (int i = 0; i < FONT_COUNT; i++)
        Log("font %d: ready=%d cell=%d baseline=%d cap=%d\n", i, g_fonts[i].ready, g_fonts[i].cellHeight, g_fonts[i].baselineRow, g_fonts[i].capHeight);
}

static int Overlap(const RECT& a, const RECT& b)
{
    int w = min(a.right, b.right) - max(a.left, b.left);
    int h = min(a.bottom, b.bottom) - max(a.top, b.top);
    return (w > 0 && h > 0) ? w * h : 0;
}

static bool SameText(const Item& a, int x, int y, int font, const char* text)
{
    return a.x == x && a.y == y && a.font == font && a.text == text;
}

// ---------------------------------------------------------------- player attribute rows

static const char* const ATTRIBUTE_NAMES[] = {
    "Acceleration", "Adaptability", "Aggression", "Agility", "Anticipation", "Balance", "Bravery", "Creativity",
    "Crossing", "Determination", "Dribbling", "Flair", "Handling", "Heading", "Influence", "Jumping", "Marking",
    "Off The Ball", "Pace", "Passing", "Positioning", "Reflexes", "Set Pieces", "Shooting", "Stamina", "Strength",
    "Tackling", "Teamwork", "Technique", "Versatility", "Work Rate",
};

static std::string Trim(const std::string& s)
{
    size_t a = 0, b = s.size();
    while (a < b && (s[a] == ' ' || s[a] == '|'))
        a++;
    while (b > a && (s[b - 1] == ' ' || s[b - 1] == '|'))
        b--;
    return s.substr(a, b - a);
}

// Index of an attribute name in ATTRIBUTE_NAMES, or -1
static int AttributeIndex(const std::string& text)
{
    std::string s = Trim(text);
    for (size_t i = 0; i < sizeof(ATTRIBUTE_NAMES) / sizeof(ATTRIBUTE_NAMES[0]); i++)
        if (s == ATTRIBUTE_NAMES[i])
            return (int)i;
    return -1;
}

static bool IsAttributeName(const std::string& text)
{
    return AttributeIndex(text) >= 0;
}

// ---- roles: which attributes matter for a position ----
// The player screen shows a label such as "Goalkeeper", "Defender (Right)" or "Defender/Midfielder (Right/Centre)".
// Each role has two tiers of attributes: key ones (full-strength bar) and secondary ones (fainter bar), separately for
// playing in the centre and playing wide (Right/Left).
// Names are the game's own ("Off The Ball" = movement, "Shooting" = finishing/long shots, "Set Pieces" = free kicks).
struct RoleDef
{
    const char* word;
    const char* keyCentre;
    const char* secCentre;
    const char* keyWide;
    const char* secWide;
};

// ---- the attribute lists, one KEY and one SEC list per kind of player ----
// Edit these to change what gets highlighted. A name in KEY wins over the same name in SEC.
#define DET ",Determination"

// goalkeeper
#define GK_KEY "Anticipation,Handling,Agility,Jumping,Positioning,Reflexes"
#define GK_SEC "Acceleration,Pace,Strength" DET

// sweeper
#define SW_KEY "Marking,Tackling,Heading,Positioning,Strength,Pace"
#define SW_SEC "Jumping,Stamina,Passing,Acceleration" DET

// defender
#define CB_KEY "Heading,Jumping,Pace,Positioning,Stamina,Strength,Marking,Tackling" DET
#define CB_SEC "Acceleration,Agility,Passing"

// defender (wide: full back)
#define FB_KEY "Acceleration,Agility,Marking,Tackling,Stamina,Pace,Positioning"
#define FB_SEC "Heading,Jumping,Dribbling,Strength,Crossing,Passing" DET

// defensive midfielder
#define DM_KEY "Marking,Passing,Tackling,Positioning,Strength,Teamwork,Work Rate" DET
#define DM_SEC "Acceleration,Heading,Jumping,Pace,Stamina,Technique"

// midfielder (centre)
#define MC_KEY "Anticipation,Balance,Dribbling,Flair,Passing,Stamina,Technique"
#define MC_SEC "Acceleration,Heading,Jumping,Pace,Strength,Teamwork" DET

// attacking midfielder
#define AM_KEY "Dribbling,Flair,Shooting,Off The Ball,Passing"
#define AM_SEC DM_SEC

// winger (the wide version of several roles below)
#define WG_KEY "Acceleration,Agility,Crossing,Dribbling,Pace,Flair" DET
#define WG_SEC "Flair,Heading,Jumping,Stamina,Strength,Teamwork"

// striker
#define ST_KEY "Shooting,Pace,Off The Ball,Acceleration,Bravery" DET
#define ST_SEC "Crossing,Dribbling,Flair,Set Pieces,Heading,Jumping,Passing,Technique"

// ---- the roles ----
// Columns: position word, then key and secondary lists when playing in the centre (or with no side shown), then key and
// secondary lists when playing wide (Right/Left). Empty wide lists mean the centre lists are used on every side.
static const RoleDef ROLES[] = {
    //  position                 centre key  centre sec  wide key               wide sec
    { "Goalkeeper",            GK_KEY,     GK_SEC,     "",                    "" },
    { "Sweeper",               SW_KEY,     SW_SEC,     "",                    "" },
    { "Defender",              CB_KEY,     CB_SEC,     FB_KEY,                FB_SEC },              // centre back / full back
    { "Defensive Midfielder",  DM_KEY,     DM_SEC,     "",                    "" },
    { "Midfielder",            MC_KEY,     MC_SEC,     MC_KEY "," WG_KEY,     MC_SEC "," WG_SEC },   // midfielder + winger
    { "Attacking Midfielder",  AM_KEY,     AM_SEC,     "",                    "" },
    { "Striker",               ST_KEY,     ST_SEC,     "",                    "" },
    { "Forward",               ST_KEY,     ST_SEC,     WG_KEY "," ST_KEY,     WG_SEC "," ST_SEC },   // winger + striker
    { "Attacker",              ST_KEY,     ST_SEC,     WG_KEY,                WG_SEC },              // winger
};

static unsigned MaskOf(const char* commaList)
{
    unsigned mask = 0;
    std::string list = commaList;
    size_t start = 0;
    while (start <= list.size())
    {
        size_t comma = list.find(',', start);
        if (comma == std::string::npos)
            comma = list.size();
        std::string name = list.substr(start, comma - start);
        int index = AttributeIndex(name);
        if (index >= 0)
            mask |= 1u << index;
        start = comma + 1;
    }
    return mask;
}

static std::string Lower(std::string s)
{
    for (size_t i = 0; i < s.size(); i++)
        if (s[i] >= 'A' && s[i] <= 'Z')
            s[i] = (char)(s[i] - 'A' + 'a');
    return s;
}

// If text is a position label, returns true and the mask of the attributes that matter for it
static bool ParseRoleLabel(const std::string& text, unsigned& keyMask, unsigned& secMask)
{
    std::string s = Trim(text);
    if (s.empty() || s.size() > 80)
        return false;

    size_t paren = s.find(" (");
    std::string rolesPart = paren == std::string::npos ? s : s.substr(0, paren);
    std::string sides = paren == std::string::npos ? "" : Lower(s.substr(paren + 2));

    bool wide = sides.find("right") != std::string::npos || sides.find("left") != std::string::npos;
    bool centre = sides.find("centre") != std::string::npos || sides.find("center") != std::string::npos || !wide;

    unsigned key = 0, sec = 0;
    size_t start = 0;
    int roles = 0;
    while (start <= rolesPart.size())
    {
        size_t slash = rolesPart.find('/', start);
        if (slash == std::string::npos)
            slash = rolesPart.size();
        std::string word = Lower(Trim(rolesPart.substr(start, slash - start)));

        bool known = false;
        for (size_t i = 0; i < sizeof(ROLES) / sizeof(ROLES[0]); i++)
        {
            if (word == Lower(ROLES[i].word))
            {
                const RoleDef& r = ROLES[i];
                // a role with no wide variant uses its centre attributes whatever the side
                bool useWide = wide && r.keyWide[0];
                if (centre || !useWide)
                {
                    key |= MaskOf(r.keyCentre);
                    sec |= MaskOf(r.secCentre);
                }
                if (useWide)
                {
                    key |= MaskOf(r.keyWide);
                    sec |= MaskOf(r.secWide);
                }
                known = true;
                break;
            }
        }
        if (!known)
            return false;       // not a position label
        roles++;
        start = slash + 1;
    }

    if (roles == 0)
        return false;
    keyMask = key;
    secMask = sec & ~key;
    return true;
}

// The key attributes of the player on screen: from the newest position label the game has drawn
static bool CurrentRoleMask(unsigned& keyMask, unsigned& secMask)
{
    for (int i = (int)g_items.size() - 1; i >= 0 && i >= (int)g_items.size() - 400; i--)
        if (ParseRoleLabel(g_items[i]->text, keyMask, secMask))
            return true;
    return false;
}

static bool ParseAttributeValue(const std::string& text, int& value)
{
    std::string s = Trim(text);
    if (s.empty() || s.size() > 2)
        return false;
    for (size_t i = 0; i < s.size(); i++)
        if (s[i] < '0' || s[i] > '9')
            return false;
    value = atoi(s.c_str());
    return value >= 1 && value <= 20;
}

static bool SameRow(const Item& a, const Item& b)
{
    return abs(a.y - b.y) <= 2;
}

// Remember that a number belongs to an attribute name on the same row (whichever of the two was drawn first)
static void LinkAttributeRow(Item* item)
{
    if (!g_attrEnabled)
        return;

    int start = (int)g_items.size() - 200;
    if (start < 0)
        start = 0;

    int value;
    if (ParseAttributeValue(item->text, value))
    {
        Item* name = NULL;
        for (int i = (int)g_items.size() - 1; i >= start; i--)
        {
            Item* other = g_items[i];
            if (other == item || !SameRow(*other, *item) || other->x >= item->x || item->x - other->x > 280)
                continue;
            if (IsAttributeName(other->text) && (name == NULL || other->x > name->x))
                name = other;
        }
        if (name)
        {
            item->attrValue = value;
            item->attrName = AttributeIndex(name->text);
            item->barLeft = name->bounds.left - 3;
        }
    }
    else if (IsAttributeName(item->text))
    {
        for (int i = (int)g_items.size() - 1; i >= start; i--)
        {
            Item* other = g_items[i];
            if (other == item || !SameRow(*other, *item) || other->x <= item->x || other->x - item->x > 280)
                continue;
            int v;
            if (ParseAttributeValue(other->text, v))
            {
                other->attrValue = v;
                other->attrName = AttributeIndex(item->text);
                other->barLeft = item->bounds.left - 3;
            }
        }
    }
}

// ---------------------------------------------------------------- the squad screen's "Clear" button

static const int CLEAR_LEFT = 372, CLEAR_TOP = 126, CLEAR_RIGHT = 468, CLEAR_BOTTOM = 146;   // game pixels
static const UINT_PTR CLEAR_TIMER = 0xC3A1;
static const UINT CLEAR_INTERVAL = 45;                  // ms between the mouse messages of the clearing

static HWND g_hwnd = NULL;                              // the game's window, once we have subclassed it
static WNDPROC g_oldProc = NULL;
static double g_viewScale = 0;                          // how the game's pixels map onto the window (from the last stretch)
static int g_viewX = 0, g_viewY = 0;
static std::vector<POINT> g_clearQueue;                 // window points to click, in order
static bool g_clearDown = false;                        // the button is down (the next message is the release)
static LPARAM g_clearPos = 0;
static bool g_swallowUp = false;                        // our own press of the button is still down

static bool ItemShown(const Item& it, SurfaceLock& surface)
{
    return it.lifted || (surface.ok && Equal(surface, it.bounds, it.baked));
}

static bool ScreenHasText(SurfaceLock& surface, const char* text)
{
    for (size_t i = 0; i < g_items.size(); i++)
        if (g_items[i]->text == text && ItemShown(*g_items[i], surface))
            return true;
    return false;
}

// "GK", "DMC", "SB7"...: the position tags next to the players of the selected team (two columns: left and right list)
static bool IsTagItem(const Item& it)
{
    const std::string& t = it.text;
    if (t.size() < 2 || t.size() > 4)
        return false;
    for (size_t i = 0; i < t.size(); i++)
        if (!((t[i] >= 'A' && t[i] <= 'Z') || (t[i] >= '0' && t[i] <= '9')))
            return false;
    int cx = (it.bounds.left + it.bounds.right) / 2;
    int cy = (it.bounds.top + it.bounds.bottom) / 2;
    bool column = (cx >= 108 && cx <= 152) || (cx >= 436 && cx <= 492);
    return column && cy >= 218 && cy <= 560;     // (the green position buttons above the list are at about y 204: not tags)
}

// Is a squad list on the screen (View / Sort By / Filter buttons)?
static bool SquadScreenShown(SurfaceLock& surface)
{
    return ScreenHasText(surface, "View") && ScreenHasText(surface, "Sort By") && ScreenHasText(surface, "Filter");
}

static bool OverClearButton(int x, int y)
{
    if (g_viewScale <= 0)
        return false;
    double gx = (x - g_viewX) / g_viewScale, gy = (y - g_viewY) / g_viewScale;
    return gx >= CLEAR_LEFT && gx < CLEAR_RIGHT && gy >= CLEAR_TOP && gy < CLEAR_BOTTOM;
}

static void DrawClearButton(HDC hdc, double scale, int ox, int oy)
{
    int left = (int)floor(ox + CLEAR_LEFT * scale + 0.5), right = (int)floor(ox + CLEAR_RIGHT * scale + 0.5);
    int top = (int)floor(oy + CLEAR_TOP * scale + 0.5), bottom = (int)floor(oy + CLEAR_BOTTOM * scale + 0.5);
    int edge = max(1, (int)(scale / 1.5 + 0.5));

    HBRUSH fill = CreateSolidBrush(RGB(150, 150, 150));
    RECT r = { left, top, right, bottom };
    FillRect(hdc, &r, fill);
    DeleteObject(fill);

    HBRUSH light = CreateSolidBrush(RGB(215, 215, 215)), dark = CreateSolidBrush(RGB(70, 70, 70));
    RECT t = { left, top, right, top + edge }, l = { left, top, left + edge, bottom };
    RECT b = { left, bottom - edge, right, bottom }, rr = { right - edge, top, right, bottom };
    FillRect(hdc, &t, light);
    FillRect(hdc, &l, light);
    FillRect(hdc, &b, dark);
    FillRect(hdc, &rr, dark);
    DeleteObject(light);
    DeleteObject(dark);

    // the font of the other buttons of the screen
    int font = 2;
    for (size_t i = 0; i < g_items.size(); i++)
        if (g_items[i]->text == "Sort By")
            font = g_items[i]->font;
    if (font < 0 || font >= FONT_COUNT || !g_fonts[font].ready)
        return;

    HGDIOBJ old = SelectObject(hdc, GetFont(hdc, font, scale));
    SetTextColor(hdc, RGB(245, 245, 245));
    SetTextAlign(hdc, TA_TOP | TA_LEFT | TA_NOUPDATECP);
    RECT text = { left, top, right, bottom };
    DrawTextW(hdc, L"Clear", -1, &text, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    SetTextAlign(hdc, TA_BASELINE | TA_LEFT | TA_NOUPDATECP);
    SelectObject(hdc, old);
}

// The window points of the tags that are on the screen now
static void CollectTags(SurfaceLock& surface, std::vector<POINT>& points)
{
    std::vector<const Item*> tags;
    for (size_t i = 0; i < g_items.size(); i++)
        if (IsTagItem(*g_items[i]) && ItemShown(*g_items[i], surface))
            tags.push_back(g_items[i]);

    // top to bottom, left list before right list
    for (size_t i = 0; i < tags.size(); i++)
        for (size_t j = i + 1; j < tags.size(); j++)
        {
            int yi = (tags[i]->bounds.top + tags[i]->bounds.bottom) / 2, yj = (tags[j]->bounds.top + tags[j]->bounds.bottom) / 2;
            int xi = tags[i]->bounds.left, xj = tags[j]->bounds.left;
            if (yj < yi - 3 || (abs(yj - yi) <= 3 && xj < xi))
                std::swap(tags[i], tags[j]);
        }

    for (size_t i = 0; i < tags.size(); i++)
    {
        // the same tag can be on the list more than once (text drawn again a pixel away): click each cell once
        bool again = false;
        for (size_t j = 0; j < i && !again; j++)
            if (abs((tags[i]->bounds.left + tags[i]->bounds.right) / 2 - (tags[j]->bounds.left + tags[j]->bounds.right) / 2) <= 14 &&
                abs((tags[i]->bounds.top + tags[i]->bounds.bottom) / 2 - (tags[j]->bounds.top + tags[j]->bounds.bottom) / 2) <= 8)
                again = true;
        if (again)
            continue;
        POINT p;
        p.x = (int)floor(g_viewX + (tags[i]->bounds.left + tags[i]->bounds.right) / 2.0 * g_viewScale + 0.5);
        p.y = (int)floor(g_viewY + (tags[i]->bounds.top + tags[i]->bounds.bottom) / 2.0 * g_viewScale + 0.5);
        points.push_back(p);
    }
}

// One mouse message of the clearing; the game sees them like real ones
static void ClearStep(HWND hwnd)
{
    if (g_clearDown)
    {
        CallWindowProcA(g_oldProc, hwnd, WM_LBUTTONUP, 0, g_clearPos);
        g_clearDown = false;
        return;
    }
    if (g_clearQueue.empty())
    {
        KillTimer(hwnd, CLEAR_TIMER);
        // the mouse goes back to where the real pointer is
        POINT pointer;
        if (GetCursorPos(&pointer) && ScreenToClient(hwnd, &pointer))
            CallWindowProcA(g_oldProc, hwnd, WM_MOUSEMOVE, 0, MAKELPARAM(pointer.x, pointer.y));
        return;
    }
    POINT p = g_clearQueue.front();
    g_clearQueue.erase(g_clearQueue.begin());
    g_clearPos = MAKELPARAM(p.x, p.y);
    Log("clear: click %d,%d\n", p.x, p.y);
    CallWindowProcA(g_oldProc, hwnd, WM_MOUSEMOVE, 0, g_clearPos);
    CallWindowProcA(g_oldProc, hwnd, WM_LBUTTONDOWN, MK_LBUTTON, g_clearPos);
    g_clearDown = true;
}

static LRESULT CALLBACK GameProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (message == WM_TIMER && wParam == CLEAR_TIMER)
    {
        ClearStep(hwnd);
        return 0;
    }

    if (message == WM_LBUTTONDOWN)
        Log("click %d,%d: over button %d (scale %.3f origin %d,%d)\n", (short)LOWORD(lParam), (short)HIWORD(lParam), (int)OverClearButton((short)LOWORD(lParam), (short)HIWORD(lParam)), g_viewScale, g_viewX, g_viewY);

    if (message == WM_LBUTTONDOWN && g_clearEnabled && g_working && g_clearQueue.empty() && !g_clearDown &&
        OverClearButton((short)LOWORD(lParam), (short)HIWORD(lParam)))
    {
        EnterCriticalSection(&g_lock);
        SurfaceLock surface;
        bool shown = SquadScreenShown(surface);
        if (shown)
            CollectTags(surface, g_clearQueue);
        Log("clear: squad shown %d, %d tags\n", (int)shown, (int)g_clearQueue.size());
        LeaveCriticalSection(&g_lock);
        if (shown)
        {
            g_swallowUp = true;
            if (!g_clearQueue.empty())
                SetTimer(hwnd, CLEAR_TIMER, CLEAR_INTERVAL, NULL);
            return 0;
        }
    }
    if (message == WM_LBUTTONUP && g_swallowUp)
    {
        g_swallowUp = false;
        return 0;
    }
    return CallWindowProcA(g_oldProc, hwnd, message, wParam, lParam);
}

static BOOL CALLBACK FindGameWindow(HWND hwnd, LPARAM result)
{
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    RECT r;
    if (pid == GetCurrentProcessId() && IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == NULL &&
        GetClientRect(hwnd, &r) && r.right >= 400 && r.bottom >= 300)
    {
        *(HWND*)result = hwnd;
        return FALSE;
    }
    return TRUE;
}

// Puts our window procedure in front of the game's (once), to see the clicks on the button
static void EnsureSubclass()
{
    if (g_hwnd || !g_clearEnabled)
        return;
    HWND found = NULL;
    EnumWindows(FindGameWindow, (LPARAM)&found);
    if (!found)
        return;
    g_oldProc = (WNDPROC)SetWindowLongPtrA(found, GWLP_WNDPROC, (LONG_PTR)GameProc);
    if (g_oldProc)
        g_hwnd = found;
}

extern "C"
{

// Called once by the patched exe after the DLL is loaded. Returns 1 if the overlay can be used.
int __cdecl Cm3_Init(void)
{
    if (!g_lockReady)
    {
        InitializeCriticalSection(&g_lock);
        g_lockReady = true;
    }
    EnterCriticalSection(&g_lock);
    Initialise();
    int ok = g_working ? 1 : 0;
    LeaveCriticalSection(&g_lock);
    return ok;
}

// Called just before the game's DrawString runs. args: x, y, font, colour (565), text (char*), underline index (-1 = none)
void __cdecl Cm3_PreDraw(const int* args)
{
    g_pending = NULL;
    if (!g_working)
        return;

    int x = args[0], y = args[1], font = (short)args[2];
    unsigned short colour = (unsigned short)args[3];
    const char* text = (const char*)args[4];
    int underline = args[5];

    if (font < 0 || font >= FONT_COUNT || !g_fonts[font].ready || text == NULL || text[0] == 0)
        return;

    EnterCriticalSection(&g_lock);

    Item* item = new Item();
    item->x = x;
    item->y = y;
    item->font = font;
    item->colour = colour;
    item->underline = underline;
    item->text = text;
    item->lifted = false;
    item->attrValue = -1;
    item->attrName = -1;
    item->barLeft = 0;

    std::vector<int> adv;
    Advances(font, text, adv);
    int width = 0;
    for (size_t i = 0; i < adv.size(); i++)
        width += adv[i];

    RECT bounds;
    bounds.left = max(0, x);
    bounds.top = max(0, y);
    bounds.right = min(SURFACE_WIDTH, x + (width > 0 ? width : 1) + 1);
    bounds.bottom = min(SURFACE_HEIGHT, y + g_fonts[font].cellHeight);
    item->bounds = bounds;

    if (bounds.right <= bounds.left || bounds.bottom <= bounds.top)
    {
        delete item;
        LeaveCriticalSection(&g_lock);
        return;
    }

    SurfaceLock surface;
    if (!surface.ok)
    {
        delete item;
        LeaveCriticalSection(&g_lock);
        return;
    }
    CopyOut(surface, bounds, item->background);

    // The same text drawn again on top of itself: keep the original clean background, not the soft text under it
    for (size_t i = 0; i < g_items.size(); i++)
    {
        Item* old = g_items[i];
        if (SameText(*old, x, y, font, text) && old->bounds.left == bounds.left && old->bounds.top == bounds.top &&
            old->bounds.right == bounds.right && old->bounds.bottom == bounds.bottom && Equal(surface, bounds, old->baked))
        {
            item->background = old->background;
            delete old;
            g_items.erase(g_items.begin() + i);
            break;
        }
    }

    g_pending = item;
    LeaveCriticalSection(&g_lock);
}

// Called just after the game's DrawString returned: remember what the soft text looks like
void __cdecl Cm3_PostDraw(void)
{
    Item* item = g_pending;
    g_pending = NULL;
    if (!item)
        return;

    EnterCriticalSection(&g_lock);
    SurfaceLock surface;
    if (!surface.ok)
    {
        delete item;
        LeaveCriticalSection(&g_lock);
        return;
    }
    CopyOut(surface, item->bounds, item->baked);

    if (item->baked == item->background)
    {
        // Nothing was drawn (completely clipped)
        delete item;
    }
    else
    {
        if ((int)g_items.size() >= MAX_ITEMS)
        {
            for (int i = 0; i < MAX_ITEMS / 4; i++)
                delete g_items[i];
            g_items.erase(g_items.begin(), g_items.begin() + MAX_ITEMS / 4);
        }
        Log("text '%s' at %d,%d font %d\n", item->text.c_str(), item->x, item->y, item->font);
        g_items.push_back(item);
        LinkAttributeRow(item);
    }
    LeaveCriticalSection(&g_lock);
}

// The surface rectangle (l,t,r,b, right/bottom exclusive) is about to be stretched onto the screen: put the clean
// background back under every text item that is still visible in it.
void __cdecl Cm3_BeforeStretch(int l, int t, int r, int b)
{
    if (!g_working)
        return;

    EnterCriticalSection(&g_lock);
    RECT area = { l, t, r, b };

    SurfaceLock surface;
    if (surface.ok)
    {
        // Newest first, so overlapping text (drop shadows) is peeled off in the right order
        for (int i = (int)g_items.size() - 1; i >= 0; i--)
        {
            Item* item = g_items[i];
            if (Overlap(item->bounds, area) == 0)
                continue;
            if (!Equal(surface, item->bounds, item->baked))
                continue;
            CopyIn(surface, item->bounds, item->background);
            item->lifted = true;
        }
    }
    LeaveCriticalSection(&g_lock);
}

static void DrawItem(HDC hdc, const Item& it, double scale, int ox, int oy)
{
    FontInfo& f = g_fonts[it.font];
    HFONT hfont = GetFont(hdc, it.font, scale);
    SelectObject(hdc, hfont);
    COLORREF colour = ToColorRef(it.colour);
    SetTextColor(hdc, colour);

    std::vector<int> adv;
    Advances(it.font, it.text.c_str(), adv);
    std::wstring wide = Widen(it.text.c_str());

    int baseline = (int)floor(oy + (it.y + f.baselineRow) * scale + 0.5);
    int pen = 0;
    int underlineFrom = -1, underlineTo = -1;

    for (size_t i = 0; i < wide.size(); i++)
    {
        unsigned char c = (unsigned char)it.text[i];
        if (c == 0x7c)
            c = 0x20;

        int cell = (c >= 0x20) ? GlyphWidth(it.font, c) : 0;
        double cellLeft = ox + (it.x + pen) * scale;
        double cellWidth = cell * scale;

        if ((int)i == it.underline)
        {
            underlineFrom = (int)floor(cellLeft + 0.5);
            underlineTo = (int)floor(cellLeft + cellWidth + 0.5);
        }

        if (c > 0x20)
        {
            unsigned key = wide[i];
            std::map<unsigned, ABC>::iterator found = g_abcCache[it.font].find(key);
            ABC abc;
            if (found == g_abcCache[it.font].end())
            {
                if (!GetCharABCWidthsW(hdc, wide[i], wide[i], &abc))
                {
                    abc.abcA = 0;
                    abc.abcB = (UINT)cellWidth;
                    abc.abcC = 0;
                }
                g_abcCache[it.font][key] = abc;
            }
            else
                abc = found->second;

            // Centre the letter's ink in the game's cell
            double x = cellLeft + (cellWidth - abc.abcB) / 2.0 - abc.abcA;
            ExtTextOutW(hdc, (int)floor(x + 0.5), baseline, 0, NULL, &wide[i], 1, NULL);
        }

        pen += adv[i];
    }

    if (underlineFrom >= 0)
    {
        HPEN pen2 = CreatePen(PS_SOLID, scale >= 1.5 ? (int)(scale + 0.5) : 1, colour);
        HGDIOBJ old = SelectObject(hdc, pen2);
        int y = baseline + (int)(scale * 2);
        MoveToEx(hdc, underlineFrom, y, NULL);
        LineTo(hdc, underlineTo, y);
        SelectObject(hdc, old);
        DeleteObject(pen2);
    }
}

// The surface has been stretched onto hdc: draw the lifted text on top, with the TrueType font.
//   d*  : the rectangle on hdc that was just drawn (we clip to it)
//   scale16 : scale as 16.16 fixed point, ox/oy : where the game's (0,0) is on hdc
void __cdecl Cm3_AfterStretch(HDC hdc, int dl, int dt, int dw, int dh, int scale16, int ox, int oy)
{
    if (!g_working)
        return;

    EnterCriticalSection(&g_lock);

    double scale = scale16 / 65536.0;
    g_viewScale = scale;
    g_viewX = ox;
    g_viewY = oy;
    EnsureSubclass();
    int saved = SaveDC(hdc);
    IntersectClipRect(hdc, dl, dt, dl + dw, dt + dh);
    SetBkMode(hdc, TRANSPARENT);
    SetTextAlign(hdc, TA_BASELINE | TA_LEFT | TA_NOUPDATECP);

    // Highlight bars behind the best attributes (drawn first so the text ends up on top)
    if (g_attrEnabled)
    {
        HDC colourDc = CreateCompatibleDC(hdc);
        HBITMAP colourBitmap = CreateCompatibleBitmap(hdc, 1, 1);
        HGDIOBJ oldBitmap = SelectObject(colourDc, colourBitmap);
        SetPixel(colourDc, 0, 0, g_attrKeyColour);
        BLENDFUNCTION blend = { AC_SRC_OVER, 0, (BYTE)max(0, min(255, g_attrBarAlpha)), 0 };

        unsigned keyMask = 0, secMask = 0;
        bool haveRole = !g_attrByValue && CurrentRoleMask(keyMask, secMask);

        for (size_t i = 0; i < g_items.size(); i++)
        {
            Item* item = g_items[i];
            if (!item->lifted || item->attrValue < 1)
                continue;

            // Which highlight: by value, every attribute is coloured by how good it is; by position, every attribute is
            // coloured by whether it is key, secondary or neither for the player's position (if that isn't known, just the
            // player's best attributes, in the key colour)
            COLORREF barColour;
            bool key = false;
            if (g_attrByValue)
            {
                if (item->attrValue > ATTR_KEY_ABOVE)
                {
                    key = true;
                    barColour = g_attrKeyColour;
                }
                else
                    barColour = item->attrValue >= ATTR_SECONDARY_FROM ? g_attrSecondaryColour : g_attrOtherColour;
            }
            else if (haveRole)
            {
                if (item->attrName < 0)
                    continue;
                unsigned bit = 1u << item->attrName;
                if (keyMask & bit)
                {
                    key = true;
                    barColour = g_attrKeyColour;
                }
                else
                    barColour = (secMask & bit) ? g_attrSecondaryColour : g_attrOtherColour;
            }
            else
            {
                if (item->attrValue < g_attrBarFrom)
                    continue;
                barColour = g_attrKeyColour;
            }
            SetPixel(colourDc, 0, 0, barColour);

            FontInfo& f = g_fonts[item->font];
            // The game's own cell for the value: a fixed distance to the right of the attribute name's start
            int cellLeft = item->barLeft + 3 + ATTR_CELL_OFFSET;
            int left = (int)floor(ox + cellLeft * scale + 0.5);
            int right = (int)floor(ox + (cellLeft + ATTR_CELL_WIDTH) * scale + 0.5);
            int top = (int)floor(oy + (item->y + f.baselineRow - f.capHeight - 2) * scale + 0.5);
            int bottom = (int)floor(oy + (item->y + f.baselineRow + 3) * scale + 0.5);
            // The highlight fills the cell from the left in proportion to the value (20 = the whole cell)
            int value = max(1, min(20, item->attrValue));
            int fill = (int)floor((right - left) * value / 20.0 + 0.5);
            if (fill > 0)
                AlphaBlend(hdc, left, top, fill, bottom - top, colourDc, 0, 0, 1, 1, blend);

            // Key attributes can also get an outline round the whole cell
            if (key && g_attrBorder)
            {
                int penWidth = max(1, (int)(scale + 0.5));
                HPEN borderPen = CreatePen(PS_SOLID, penWidth, g_attrBorderColour);
                HGDIOBJ oldPen = SelectObject(hdc, borderPen);
                HGDIOBJ oldBrush = SelectObject(hdc, GetStockObject(NULL_BRUSH));
                Rectangle(hdc, left, top, right, bottom);
                SelectObject(hdc, oldBrush);
                SelectObject(hdc, oldPen);
                DeleteObject(borderPen);
            }
        }

        SelectObject(colourDc, oldBitmap);
        DeleteObject(colourBitmap);
        DeleteDC(colourDc);
    }

    int drawn = 0;
    for (size_t i = 0; i < g_items.size(); i++)
    {
        Item* item = g_items[i];
        if (!item->lifted)
            continue;
        DrawItem(hdc, *item, scale, ox, oy);
        drawn++;
    }
    Log("after stretch: dest=%d,%d,%d,%d items=%d drawn=%d\n", dl, dt, dw, dh, (int)g_items.size(), drawn);

    if (g_clearEnabled && g_hwnd)
    {
        SurfaceLock surface;
        if (SquadScreenShown(surface))
            DrawClearButton(hdc, scale, ox, oy);
    }

    RestoreDC(hdc, saved);
    LeaveCriticalSection(&g_lock);
}

// ---- double buffering ----
// The stretch and our text are drawn into an off-screen bitmap and copied to the window/screen in one go, so there is
// never a moment where the screen shows the stretched picture without its text (that was a flicker on every redraw).
static HDC g_backDc = NULL;
static HBITMAP g_backBitmap = NULL;
static int g_backWidth = 0, g_backHeight = 0;

// Returns an off-screen DC at least right x bottom pixels big to draw into instead of real, or NULL to draw directly
HDC __cdecl Cm3_BeginBuffer(HDC real, int right, int bottom)
{
    if (!g_working || !real || right <= 0 || bottom <= 0 || right > 16384 || bottom > 16384)
        return NULL;

    if (!g_backDc)
        g_backDc = CreateCompatibleDC(real);
    if (!g_backDc)
        return NULL;

    if (!g_backBitmap || right > g_backWidth || bottom > g_backHeight)
    {
        int width = max(right, g_backWidth), height = max(bottom, g_backHeight);
        HBITMAP bitmap = CreateCompatibleBitmap(real, width, height);
        if (!bitmap)
            return g_backBitmap ? g_backDc : NULL;
        SelectObject(g_backDc, bitmap);         // this also deselects the old bitmap
        if (g_backBitmap)
            DeleteObject(g_backBitmap);
        g_backBitmap = bitmap;
        g_backWidth = width;
        g_backHeight = height;
    }
    return g_backDc;
}

// The finished picture is in the off-screen DC: put it on the real one
void __cdecl Cm3_EndBuffer(HDC real, HDC buffer, int x, int y, int w, int h)
{
    if (real && buffer)
        BitBlt(real, x, y, w, h, buffer, x, y, SRCCOPY);
}

// The present is over: put the game's own soft text back on its surface, exactly as it was.
void __cdecl Cm3_Rebake(void)
{
    if (!g_working)
        return;

    EnterCriticalSection(&g_lock);
    bool any = false;
    for (size_t i = 0; i < g_items.size(); i++)
        if (g_items[i]->lifted)
            any = true;

    if (any)
    {
        SurfaceLock surface;
        for (size_t i = 0; i < g_items.size(); i++)
        {
            Item* item = g_items[i];
            if (!item->lifted)
                continue;
            if (surface.ok)
                CopyIn(surface, item->bounds, item->baked);
            item->lifted = false;
        }
    }
    LeaveCriticalSection(&g_lock);
}

}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID)
{
    return TRUE;
}

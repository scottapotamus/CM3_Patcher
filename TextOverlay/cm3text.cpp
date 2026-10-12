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
static bool g_attrRoleAware = true;         // highlight what matters for the player's position, not just his best stats

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
    g_attrRoleAware = GetPrivateProfileIntW(L"attributes", L"roleaware", 1, path) != 0;

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
// Each role has a base set of key attributes, plus extras for playing out wide (Right/Left) or in the centre.
struct RoleDef
{
    const char* word;
    const char* base;
    const char* centre;
    const char* wide;
};

static const RoleDef ROLES[] = {
    { "Goalkeeper", "Handling,Reflexes,Positioning,Agility,Anticipation,Bravery", "", "" },
    { "Sweeper", "Positioning,Anticipation,Tackling,Marking,Heading,Passing", "", "" },
    { "Defender", "Tackling,Marking,Positioning,Anticipation,Bravery", "Heading,Jumping,Strength", "Pace,Acceleration,Stamina,Crossing" },
    { "Defensive Midfielder", "Tackling,Positioning,Anticipation,Teamwork,Work Rate,Stamina,Passing,Marking", "", "" },
    { "Midfielder", "Passing,Technique,Teamwork,Work Rate,Stamina,Creativity,Positioning", "", "Pace,Crossing,Dribbling,Acceleration" },
    { "Attacking Midfielder", "Creativity,Passing,Technique,Flair,Dribbling,Off The Ball,Shooting", "", "Pace,Crossing,Acceleration" },
    { "Striker", "Shooting,Off The Ball,Heading,Pace,Acceleration,Technique,Strength,Dribbling", "", "" },
    { "Forward", "Shooting,Off The Ball,Heading,Pace,Acceleration,Technique,Strength,Dribbling", "", "" },
    { "Attacker", "Shooting,Off The Ball,Heading,Pace,Acceleration,Technique,Strength,Dribbling", "", "" },
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
static bool ParseRoleLabel(const std::string& text, unsigned& mask)
{
    std::string s = Trim(text);
    if (s.empty() || s.size() > 80)
        return false;

    size_t paren = s.find(" (");
    std::string rolesPart = paren == std::string::npos ? s : s.substr(0, paren);
    std::string sides = paren == std::string::npos ? "" : Lower(s.substr(paren + 2));

    bool wide = sides.find("right") != std::string::npos || sides.find("left") != std::string::npos;
    bool centre = sides.find("centre") != std::string::npos || sides.find("center") != std::string::npos || !wide;

    unsigned result = 0;
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
                result |= MaskOf(ROLES[i].base);
                if (centre)
                    result |= MaskOf(ROLES[i].centre);
                if (wide)
                    result |= MaskOf(ROLES[i].wide);
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
    mask = result;
    return true;
}

// The key attributes of the player on screen: from the newest position label the game has drawn
static bool CurrentRoleMask(unsigned& mask)
{
    for (int i = (int)g_items.size() - 1; i >= 0 && i >= (int)g_items.size() - 400; i--)
        if (ParseRoleLabel(g_items[i]->text, mask))
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

// Colour of an attribute value, from dim (poor) to bright green (excellent)
static COLORREF AttributeColour(int value)
{
    if (value <= 4)
        return RGB(150, 112, 112);
    if (value <= 8)
        return RGB(185, 185, 185);
    if (value <= 12)
        return RGB(255, 236, 130);
    if (value <= 15)
        return RGB(176, 240, 96);
    return RGB(70, 255, 120);
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
    COLORREF colour = (g_attrEnabled && it.attrValue > 0) ? AttributeColour(it.attrValue) : ToColorRef(it.colour);
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
        SetPixel(colourDc, 0, 0, RGB(40, 150, 70));
        BLENDFUNCTION blend = { AC_SRC_OVER, 0, (BYTE)max(0, min(255, g_attrBarAlpha)), 0 };

        unsigned keyMask = 0;
        bool haveRole = g_attrRoleAware && CurrentRoleMask(keyMask);

        for (size_t i = 0; i < g_items.size(); i++)
        {
            Item* item = g_items[i];
            if (!item->lifted || item->attrValue < 1)
                continue;

            // Which bar, if any: for a known position, bars go on the attributes that matter for it (green if strong,
            // blue if weak); otherwise just on the player's best attributes
            bool strong = item->attrValue >= g_attrBarFrom;
            COLORREF barColour;
            if (haveRole)
            {
                if (item->attrName < 0 || !(keyMask & (1u << item->attrName)))
                    continue;
                barColour = strong ? RGB(40, 150, 70) : RGB(50, 90, 175);
            }
            else
            {
                if (!strong)
                    continue;
                barColour = RGB(40, 150, 70);
            }
            SetPixel(colourDc, 0, 0, barColour);

            FontInfo& f = g_fonts[item->font];
            int left = (int)floor(ox + item->barLeft * scale + 0.5);
            int right = (int)floor(ox + (item->bounds.right + 4) * scale + 0.5);
            int top = (int)floor(oy + (item->y + f.baselineRow - f.capHeight - 2) * scale + 0.5);
            int bottom = (int)floor(oy + (item->y + f.baselineRow + 3) * scale + 0.5);
            AlphaBlend(hdc, left, top, right - left, bottom - top, colourDc, 0, 0, 1, 1, blend);
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

    RestoreDC(hdc, saved);
    LeaveCriticalSection(&g_lock);
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

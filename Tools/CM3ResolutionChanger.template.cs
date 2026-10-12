// ---------------------------------------------------------------------------------------------------------------------
// GENERATED FILE - do not edit CM3ResolutionChanger.cs by hand.
// It is produced from Tools\CM3ResolutionChanger.template.cs by Tools\generate_changer.py, which assembles the machine
// code "caves" (needs Python with the 'keystone-engine' package):
//     python Tools\generate_changer.py Tools\CM3ResolutionChanger.template.cs CM3ResolutionChanger.cs
// ---------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;

namespace CM3ResolutionPatcher
{
    // Lets Championship Manager 3 (cm3.exe, v3.04e) run at resolutions other than 800x600.
    //
    // CM3 renders into a fixed 800x600 system-memory DirectDraw surface and every screen is laid out
    // for that size, so unlike CM0102 there are no layout constants to tweak. Instead the game keeps
    // drawing at 800x600 and this patch:
    //   * makes the window / display mode the requested size,
    //   * stretch-blits (aspect preserving, centred) the 800x600 surface onto the larger primary surface, and
    //   * maps the mouse position back into 800x600 coordinates.
    //
    // The new code lives in a small executable section (.cm3res) appended to the exe, and the original
    // code is redirected to it with 5 byte jumps ("hooks"). Choosing 800x600 puts the original bytes back.
    public class CM3ResolutionChanger
    {
        const int ImageBase = 0x400000;
        const int SourceWidth = 800;
        const int SourceHeight = 600;
        const string SectionName = ".cm3res";
        const int SectionSize = 0x2000;
        const int MinWidth = 640;
        const int MinHeight = 480;
        const int MaxSize = 7680;

        // Parameter slots filled into the caves below (the second number in each CaveXParams entry)
        const int ParamOX = 0, ParamOY = 1, ParamScale = 2, ParamInvScale = 3, ParamWidth = 4, ParamHeight = 5, ParamData = 6, ParamSmooth = 7, ParamUpscale = 8, ParamDownscaleFilter = 9, ParamPostDraw = 10, ParamHeldDelay = 11;

        // GDI+ InterpolationMode used for the final downscale in Crisp mode (6 = high quality bilinear, 7 = high quality bicubic)
        const int CrispDownscaleFilter = 7;

        // Scratch data for the smoothing code (function pointers, device contexts and the strings used to look the
        // functions up). The section is writable so the code can fill these in the first time it runs.
        const int DataOffset = 0x1000;
        const int DataStartupInput = 0xC0;      // GdiplusStartupInput: GdiplusVersion = 1, the rest 0
        const int DataStringsOffset = 0x200;
        const int DataStringSlot = 0x20;
        static readonly string[] DataStrings = { /*@STRINGS@*/ };

/*@CAVES@*/

        class Hook
        {
            public int Offset;          // file offset (== RVA for cm3.exe's code)
            public byte[] Original;
            public int Cave;            // index into CaveNames/CaveCode

            public Hook(int offset, string originalHex, string cave)
            {
                Offset = offset;
                Original = FromHex(originalHex);
                Cave = Array.IndexOf(CaveNames, cave);
                if (Cave < 0)
                    throw new ArgumentException("Unknown cave " + cave);
            }
        }

        // Order matters: caves are laid out in this order inside the new section
        static readonly string[] CaveNames = { /*@CAVELIST@*/ };
/*@CAVEARRAYS@*/

        static readonly Hook[] Hooks = {
            new Hook(0x13354D, "A1E4B78300", "Present"),                // Present: mov eax,[83B7E4]  (stretch blit instead of 1:1 blit)
            new Hook(0x1A2173, "8B84245401", "Mouse"),                  // WM_MOUSEMOVE: read lParam
            new Hook(0x132F78, "6A1055538B08", "FsMode"),               // Fullscreen SetDisplayMode(w,h,16)
            new Hook(0x132D42, "8B15CCB78300", "Win1"),                 // Windowed CreateWindow: use real size
            new Hook(0x132DA5, "85C0A35C5C8600", "Win2"),               // ... then go back to 800x600 for the surfaces
            new Hook(0x132F1E, "8B155C5C8600", "TogEnd"),               // Same again for the fullscreen<->windowed switch
            new Hook(0x132C9C, "897C2434897C2438", "Brush"),            // Window class background brush -> black
        };

        // Optional: the game's DrawString routine (sub esp,0D0h) is wrapped so that cm3text.dll sees every string, and
        // draws it again with a TrueType font at the real resolution on top of the stretched picture (see cm3text.cpp)
        // Optional: while the left mouse button is held, the game's message pump waits a little on each pass. The game's
        // "hold the button to repeat" loops (scroll bars, +/- buttons) call the pump once per pass and have no timing of
        // their own, so on a modern PC even a short click repeats hundreds of times.
        static readonly Hook HeldHook = new Hook(0x1A1F27, "E842D61B00", "HeldPump");

        static readonly Hook[] TextHooks = {
            new Hook(0x135590, "81ECD0000000", "DrawText"),
        };

        // Fullscreen<->windowed switch: loads the window size into ebp/ebx (14 bytes, patched inline)
        const int ToggleOffset = 0x132E6F;
        static readonly byte[] ToggleOriginal = FromHex("8BAC24C00000008B9C24BC000000");

        // Optional: the substitutes rules. Every competition's rules word (bits 0-3 = substitutes named, bits 4-7 = substitutes
        // that may be used) is packed by three blocks of code that read the two per-competition bytes [obj+0x33] / [obj+0x34].
        // Replacing those six 3-byte reads (mov r8,[obj+disp]) with "mov r8,imm8 / nop" gives every competition the same rules.
        // The game's team selection and match engine hold 20 players per team (11 + 9), so 9 is the most that can be named.
        public const int MaxSubstitutes = 9;

        class SubSite
        {
            public int Offset;
            public byte[] Original;
            public byte MovOpcode;      // mov al/cl/dl/bl, imm8
            public bool Named;          // true: the number named on the bench, false: the number that may be used

            public SubSite(int offset, string originalHex, byte movOpcode, bool named)
            {
                Offset = offset;
                Original = FromHex(originalHex);
                MovOpcode = movOpcode;
                Named = named;
            }
        }

        static readonly SubSite[] SubSites = {
            new SubSite(0x19963F, "8A4633", 0xB0, true),        // league: mov al,[esi+33h]
            new SubSite(0x199646, "8A5634", 0xB2, false),       //         mov dl,[esi+34h]
            new SubSite(0x0A4B60, "8A4D33", 0xB1, true),        // cup:    mov cl,[ebp+33h]
            new SubSite(0x0A4B72, "8A5D34", 0xB3, false),       //         mov bl,[ebp+34h]
            new SubSite(0x0A57D2, "8A4E33", 0xB1, true),        // cup:    mov cl,[esi+33h]
            new SubSite(0x0A57E8, "8A5634", 0xB2, false),       //         mov dl,[esi+34h]
        };

        // 0 = leave the game's own rules for that number
        static void WriteSubstitutes(byte[] exe, int named, int used)
        {
            foreach (SubSite site in SubSites)
            {
                int value = site.Named ? named : used;
                if (value > 0)
                {
                    exe[site.Offset] = site.MovOpcode;
                    exe[site.Offset + 1] = (byte)value;
                    exe[site.Offset + 2] = 0x90;
                }
                else
                    Array.Copy(site.Original, 0, exe, site.Offset, site.Original.Length);
            }
        }

        // The substitutes rules the exe is patched with (0 = the game's own rules)
        public static void GetSubstitutes(string fileName, out int named, out int used)
        {
            named = used = 0;
            byte[] exe = File.ReadAllBytes(fileName);
            if (!IsSupported(exe))
                return;
            foreach (SubSite site in SubSites)
            {
                if (exe[site.Offset] != site.MovOpcode)
                    continue;
                if (site.Named)
                    named = exe[site.Offset + 1];
                else
                    used = exe[site.Offset + 1];
            }
        }

        public static void GetResolution(string fileName, out int width, out int height)
        {
            width = SourceWidth;
            height = SourceHeight;

            byte[] exe = File.ReadAllBytes(fileName);
            if (!IsPatched(exe))
                return;

            // bd <height> bb <width>
            if (exe[ToggleOffset] == 0xBD && exe[ToggleOffset + 5] == 0xBB)
            {
                height = BitConverter.ToInt32(exe, ToggleOffset + 1);
                width = BitConverter.ToInt32(exe, ToggleOffset + 6);
            }
        }

        public static bool IsCM3Exe(string fileName)
        {
            byte[] exe = File.ReadAllBytes(fileName);
            return IsSupported(exe);
        }

        public enum Smoothing
        {
            None = 0,           // DirectDraw nearest-neighbour stretch: sharp but blocky
            Halftone = 1,       // GDI halftone stretch: smooth
            HighQuality = 2,    // GDI+ bicubic stretch: smoothest (falls back to halftone if GDI+ is unavailable)
            Crisp = 3,          // whole-number nearest-neighbour upscale then a high quality downscale: edges about one screen pixel wide
        }

        public static void SetResolution(string fileName, int width, int height)
        {
            SetResolution(fileName, width, height, Smoothing.HighQuality);
        }

        public static void SetResolution(string fileName, int width, int height, bool smooth)
        {
            SetResolution(fileName, width, height, smooth ? Smoothing.HighQuality : Smoothing.None);
        }

        public static void SetResolution(string fileName, int width, int height, Smoothing smoothing)
        {
            SetResolution(fileName, width, height, smoothing, false);
        }

        // textOverlay: draw the game's text with cm3text.dll (needs a smoothing mode other than None)
        public static void SetResolution(string fileName, int width, int height, Smoothing smoothing, bool textOverlay)
        {
            Apply(fileName, width, height, smoothing, textOverlay, GetButtonRepeatDelay(fileName));
        }

        // The one entry point. Resolution 800x600 means "leave the display alone"; the button repeat fix is independent of it.
        //   textOverlay         : draw the game's text with cm3text.dll (needs a smoothing mode other than None)
        //   buttonRepeatDelayMs : 0 = off, otherwise how long (ms) the game waits per pass while a mouse button is held
        public static void Apply(string fileName, int width, int height, Smoothing smoothing, bool textOverlay, int buttonRepeatDelayMs)
        {
            int named, used;
            GetSubstitutes(fileName, out named, out used);
            Apply(fileName, width, height, smoothing, textOverlay, buttonRepeatDelayMs, named, used);
        }

        //   substitutesNamed / substitutesUsed : 0 = the game's own rules, otherwise 1-9 for every competition
        public static void Apply(string fileName, int width, int height, Smoothing smoothing, bool textOverlay, int buttonRepeatDelayMs, int substitutesNamed, int substitutesUsed)
        {
            if (substitutesNamed < 0 || substitutesNamed > MaxSubstitutes || substitutesUsed < 0 || substitutesUsed > MaxSubstitutes)
                throw new ArgumentException("The substitutes must be between 0 and " + MaxSubstitutes);

            if (buttonRepeatDelayMs < 0 || buttonRepeatDelayMs > 1000)
                throw new ArgumentException("The button repeat delay must be between 0 and 1000 ms");

            if (textOverlay && smoothing == Smoothing.None)
                throw new ArgumentException("The text overlay needs one of the smoothing modes");

            if (width < MinWidth || height < MinHeight || width > MaxSize || height > MaxSize)
                throw new ArgumentException(string.Format("Resolution must be at least {0}x{1} and at most {2}x{2}", MinWidth, MinHeight, MaxSize));

            byte[] exe = File.ReadAllBytes(fileName);
            if (!IsSupported(exe))
                throw new Exception("This doesn't look like the CM3 v3.04e cm3.exe");

            bool changeResolution = !(width == SourceWidth && height == SourceHeight);
            if (!changeResolution)
                textOverlay = false;        // the text overlay is part of the scaled output

            if (!changeResolution && buttonRepeatDelayMs == 0)
            {
                Revert(exe);
                WriteSubstitutes(exe, substitutesNamed, substitutesUsed);
                File.WriteAllBytes(fileName, exe);
                return;
            }

            int caveRva;
            exe = AddSection(exe, out caveRva);
            int sectionFileOffset = GetSectionFileOffset(exe, caveRva);

            // Aspect preserving scale in 16.16 fixed point, centred in the new size
            int scale = (int)(Math.Min((double)width / SourceWidth, (double)height / SourceHeight) * 65536.0);
            int scaledWidth = (int)(((long)SourceWidth * scale) >> 16);
            int scaledHeight = (int)(((long)SourceHeight * scale) >> 16);
            int[] values = new int[12];
            values[ParamOX] = (width - scaledWidth) / 2;
            values[ParamOY] = (height - scaledHeight) / 2;
            values[ParamScale] = scale;
            values[ParamInvScale] = (int)((1L << 32) / scale);
            values[ParamWidth] = width;
            values[ParamHeight] = height;
            values[ParamData] = ImageBase + caveRva + DataOffset;
            values[ParamSmooth] = (int)smoothing;
            values[ParamUpscale] = (int)Math.Floor((double)scale / 65536.0) + 1;       // always a bit bigger than the real scale
            values[ParamDownscaleFilter] = CrispDownscaleFilter;
            values[ParamHeldDelay] = buttonRepeatDelayMs;

            // Lay out the caves
            int[] caveRvas = new int[CaveNames.Length];
            int[] cavePositions = new int[CaveNames.Length];
            int position = 0;
            for (int i = 0; i < CaveNames.Length; i++)
            {
                cavePositions[i] = position;
                caveRvas[i] = caveRva + position;
                position += (CaveCode[i].Length + 5 + 15) & ~15;
            }
            int caveTotal = position;
            values[ParamPostDraw] = ImageBase + caveRvas[Array.IndexOf(CaveNames, "DrawPost")];

            for (int i = 0; i < CaveNames.Length; i++)
            {
                position = cavePositions[i];
                byte[] code = (byte[])CaveCode[i].Clone();
                foreach (int[] p in CaveParams[i])
                    WriteInt(code, p[0], values[p[1]]);

                int caveVa = ImageBase + caveRvas[i];

                Array.Copy(code, 0, exe, sectionFileOffset + position, code.Length);
                // Jump back to the original code
                int jmpAt = sectionFileOffset + position + code.Length;
                exe[jmpAt] = 0xE9;
                WriteInt(exe, jmpAt + 1, (ImageBase + CaveReturn[i]) - (caveVa + code.Length + 5));
            }
            if (caveTotal > DataOffset)
                throw new Exception("Code caves don't fit in the new section");

            // Fresh data page: zeroed pointers/status, the lookup strings and the GDI+ startup input
            Array.Clear(exe, sectionFileOffset + DataOffset, SectionSize - DataOffset);
            for (int i = 0; i < DataStrings.Length; i++)
                for (int c = 0; c < DataStrings[i].Length; c++)
                    exe[sectionFileOffset + DataOffset + DataStringsOffset + i * DataStringSlot + c] = (byte)DataStrings[i][c];
            WriteInt(exe, sectionFileOffset + DataOffset + DataStartupInput, 1);

            MakeSectionWritable(exe, caveRva);

            foreach (Hook hook in Hooks)
            {
                if (changeResolution)
                    WriteHook(exe, hook, caveRvas);
                else
                    Array.Copy(hook.Original, 0, exe, hook.Offset, hook.Original.Length);
            }

            foreach (Hook hook in TextHooks)
            {
                if (textOverlay)
                    WriteHook(exe, hook, caveRvas);
                else
                    Array.Copy(hook.Original, 0, exe, hook.Offset, hook.Original.Length);
            }

            if (buttonRepeatDelayMs > 0)
                WriteHook(exe, HeldHook, caveRvas);
            else
                Array.Copy(HeldHook.Original, 0, exe, HeldHook.Offset, HeldHook.Original.Length);

            if (changeResolution)
            {
                // Inline: mov ebp,<height> / mov ebx,<width>
                exe[ToggleOffset] = 0xBD;
                WriteInt(exe, ToggleOffset + 1, height);
                exe[ToggleOffset + 5] = 0xBB;
                WriteInt(exe, ToggleOffset + 6, width);
                for (int i = 10; i < ToggleOriginal.Length; i++)
                    exe[ToggleOffset + i] = 0x90;
            }
            else
                Array.Copy(ToggleOriginal, 0, exe, ToggleOffset, ToggleOriginal.Length);

            WriteSubstitutes(exe, substitutesNamed, substitutesUsed);
            File.WriteAllBytes(fileName, exe);
        }

        static void WriteHook(byte[] exe, Hook hook, int[] caveRvas)
        {
            exe[hook.Offset] = 0xE9;
            WriteInt(exe, hook.Offset + 1, caveRvas[hook.Cave] - (hook.Offset + 5));
            for (int i = 5; i < hook.Original.Length; i++)
                exe[hook.Offset + i] = 0x90;
        }

        // 0 if the button repeat fix isn't applied, otherwise the delay (ms) it was applied with
        public static int GetButtonRepeatDelay(string fileName)
        {
            byte[] exe = File.ReadAllBytes(fileName);
            if (!IsSupported(exe) || exe[HeldHook.Offset] != 0xE9)
                return 0;

            // Follow the jump into the cave and read the delay out of it
            int sectionRva = FindSectionRva(exe);
            int caveRva = HeldHook.Offset + 5 + BitConverter.ToInt32(exe, HeldHook.Offset + 1);
            int fileOffset = GetSectionFileOffset(exe, sectionRva) + (caveRva - sectionRva);
            int heldCave = Array.IndexOf(CaveNames, "HeldPump");
            foreach (int[] p in CaveParams[heldCave])
                if (p[1] == ParamHeldDelay)
                    return BitConverter.ToInt32(exe, fileOffset + p[0]);
            return 0;
        }

        static int FindSectionRva(byte[] exe)
        {
            int pe = BitConverter.ToInt32(exe, 0x3C);
            int sectionCount = BitConverter.ToInt16(exe, pe + 6);
            int firstSection = pe + 24 + BitConverter.ToInt16(exe, pe + 20);
            for (int i = 0; i < sectionCount; i++)
                if (SectionNameAt(exe, firstSection + i * 40) == SectionName)
                    return BitConverter.ToInt32(exe, firstSection + i * 40 + 12);
            throw new Exception("Couldn't find the .cm3res section");
        }

        // True if the exe hands the game's text to cm3text.dll
        public static bool GetTextOverlay(string fileName)
        {
            byte[] exe = File.ReadAllBytes(fileName);
            return IsPatched(exe) && exe[TextHooks[0].Offset] == 0xE9;
        }

        static void Revert(byte[] exe)
        {
            foreach (Hook hook in Hooks)
                Array.Copy(hook.Original, 0, exe, hook.Offset, hook.Original.Length);
            foreach (Hook hook in TextHooks)
                Array.Copy(hook.Original, 0, exe, hook.Offset, hook.Original.Length);
            Array.Copy(HeldHook.Original, 0, exe, HeldHook.Offset, HeldHook.Original.Length);
            Array.Copy(ToggleOriginal, 0, exe, ToggleOffset, ToggleOriginal.Length);
            WriteSubstitutes(exe, 0, 0);
        }

        // Every patch site must either be untouched or already one of ours
        static bool IsSupported(byte[] exe)
        {
            if (exe.Length < 0x1000 || exe[0] != 'M' || exe[1] != 'Z')
                return false;
            int pe = BitConverter.ToInt32(exe, 0x3C);
            if (pe < 0 || pe + 0x80 > exe.Length || BitConverter.ToInt32(exe, pe) != 0x4550)
                return false;
            if (BitConverter.ToInt32(exe, pe + 24 + 28) != ImageBase)
                return false;

            foreach (Hook hook in Hooks)
            {
                if (hook.Offset + hook.Original.Length > exe.Length)
                    return false;
                if (!(Matches(exe, hook.Offset, hook.Original) || exe[hook.Offset] == 0xE9))
                    return false;
            }
            foreach (Hook hook in TextHooks)
                if (!(Matches(exe, hook.Offset, hook.Original) || exe[hook.Offset] == 0xE9))
                    return false;
            if (!(Matches(exe, HeldHook.Offset, HeldHook.Original) || exe[HeldHook.Offset] == 0xE9))
                return false;
            foreach (SubSite site in SubSites)
                if (!(Matches(exe, site.Offset, site.Original) || (exe[site.Offset] == site.MovOpcode && exe[site.Offset + 2] == 0x90)))
                    return false;
            return Matches(exe, ToggleOffset, ToggleOriginal) || exe[ToggleOffset] == 0xBD;
        }

        static bool IsPatched(byte[] exe)
        {
            return IsSupported(exe) && exe[Hooks[0].Offset] == 0xE9;
        }

        static bool Matches(byte[] data, int offset, byte[] expected)
        {
            for (int i = 0; i < expected.Length; i++)
                if (data[offset + i] != expected[i])
                    return false;
            return true;
        }

        // Appends the (executable) .cm3res section if it isn't there already. Returns its RVA.
        static byte[] AddSection(byte[] exe, out int sectionRva)
        {
            int pe = BitConverter.ToInt32(exe, 0x3C);
            int sectionCount = BitConverter.ToInt16(exe, pe + 6);
            int optionalSize = BitConverter.ToInt16(exe, pe + 20);
            int optionalHeader = pe + 24;
            int firstSection = optionalHeader + optionalSize;

            for (int i = 0; i < sectionCount; i++)
            {
                int header = firstSection + i * 40;
                if (SectionNameAt(exe, header) == SectionName)
                {
                    sectionRva = BitConverter.ToInt32(exe, header + 12);
                    int rawSize = BitConverter.ToInt32(exe, header + 16);
                    if (rawSize >= SectionSize)
                        return exe;

                    // Made by an earlier version with a smaller section: it's the last one, so just make it bigger
                    byte[] grown = new byte[BitConverter.ToInt32(exe, header + 20) + SectionSize];
                    Array.Copy(exe, grown, exe.Length);
                    WriteInt(grown, header + 8, SectionSize);
                    WriteInt(grown, header + 16, SectionSize);
                    WriteInt(grown, optionalHeader + 56, sectionRva + SectionSize);
                    return grown;
                }
            }

            int last = firstSection + (sectionCount - 1) * 40;
            int lastRva = BitConverter.ToInt32(exe, last + 12);
            int lastVirtualSize = BitConverter.ToInt32(exe, last + 8);
            int lastRawEnd = BitConverter.ToInt32(exe, last + 20) + BitConverter.ToInt32(exe, last + 16);
            if (exe.Length > lastRawEnd)
                throw new Exception("cm3.exe has extra data after its last section, can't add a code section");

            int newHeader = firstSection + sectionCount * 40;
            int headersSize = BitConverter.ToInt32(exe, optionalHeader + 60);
            if (newHeader + 40 > headersSize)
                throw new Exception("No room for another section header in cm3.exe");
            for (int i = 0; i < 40; i++)
                if (exe[newHeader + i] != 0)
                    throw new Exception("No room for another section header in cm3.exe");

            sectionRva = (lastRva + lastVirtualSize + 0xFFF) & ~0xFFF;
            int rawPointer = (exe.Length + 0xFFF) & ~0xFFF;

            byte[] result = new byte[rawPointer + SectionSize];
            Array.Copy(exe, result, exe.Length);

            for (int i = 0; i < SectionName.Length; i++)
                result[newHeader + i] = (byte)SectionName[i];
            WriteInt(result, newHeader + 8, SectionSize);       // VirtualSize
            WriteInt(result, newHeader + 12, sectionRva);       // VirtualAddress
            WriteInt(result, newHeader + 16, SectionSize);      // SizeOfRawData
            WriteInt(result, newHeader + 20, rawPointer);       // PointerToRawData
            WriteInt(result, newHeader + 36, unchecked((int)0xE0000020));       // code | execute | read | write
            result[pe + 6] = (byte)(sectionCount + 1);
            result[pe + 7] = 0;
            WriteInt(result, optionalHeader + 56, sectionRva + SectionSize);    // SizeOfImage
            return result;
        }

        static int GetSectionFileOffset(byte[] exe, int sectionRva)
        {
            int pe = BitConverter.ToInt32(exe, 0x3C);
            int sectionCount = BitConverter.ToInt16(exe, pe + 6);
            int firstSection = pe + 24 + BitConverter.ToInt16(exe, pe + 20);
            for (int i = 0; i < sectionCount; i++)
            {
                int header = firstSection + i * 40;
                if (BitConverter.ToInt32(exe, header + 12) == sectionRva)
                    return BitConverter.ToInt32(exe, header + 20);
            }
            throw new Exception("Couldn't find the .cm3res section");
        }

        // The data area needs a read/write/execute section (older versions of this patch made it read/execute only)
        static void MakeSectionWritable(byte[] exe, int sectionRva)
        {
            int pe = BitConverter.ToInt32(exe, 0x3C);
            int sectionCount = BitConverter.ToInt16(exe, pe + 6);
            int firstSection = pe + 24 + BitConverter.ToInt16(exe, pe + 20);
            for (int i = 0; i < sectionCount; i++)
            {
                int header = firstSection + i * 40;
                if (BitConverter.ToInt32(exe, header + 12) == sectionRva)
                    WriteInt(exe, header + 36, unchecked((int)0xE0000020));
            }
        }

        static string SectionNameAt(byte[] exe, int header)
        {
            int length = 0;
            while (length < 8 && exe[header + length] != 0)
                length++;
            return System.Text.Encoding.ASCII.GetString(exe, header, length);
        }

        static void WriteInt(byte[] data, int offset, int value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        static byte[] FromHex(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }
}

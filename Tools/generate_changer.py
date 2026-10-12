"""Generates CM3ResolutionChanger.cs from a template: assembles each code cave with sentinel immediates
and records where the sentinels are so the C# side can fill them in."""
import struct, sys
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

P = {'OX': 0x7A7A7A01, 'OY': 0x7A7A7A02, 'S': 0x7A7A7A03, 'INV': 0x7A7A7A04,
     'W': 0x7A7A7A05, 'H': 0x7A7A7A06, 'D': 0x7A7A7A07, 'SM': 0x7A7A7A08, 'K': 0x7A7A7A09, 'Q2': 0x7A7A7A0A, 'POST': 0x7A7A7A0B, 'HELD': 0x7A7A7A0C}
ORDER = ['OX', 'OY', 'S', 'INV', 'W', 'H', 'D', 'SM', 'K', 'Q2', 'POST', 'HELD']

# ---- data area layout (offsets from D) ----
STRINGS = ['gdi32.dll', 'StretchBlt', 'SetStretchBltMode', 'SetBrushOrgEx', 'user32.dll', 'GetDC', 'ReleaseDC',
           'gdiplus.dll', 'GdiplusStartup', 'GdipCreateFromHDC', 'GdipSetInterpolationMode', 'GdipSetCompositingMode',
           'GdipCreateBitmapFromScan0', 'GdipDrawImageRectRectI', 'GdipDeleteGraphics', 'GdipDisposeImage',
           'GdipCreateImageAttributes', 'GdipSetImageAttributesWrapMode', 'GdipGetImageGraphicsContext', 'GdipSetPixelOffsetMode',
           'cm3text.dll', 'Cm3_Init', 'Cm3_PreDraw', 'Cm3_PostDraw', 'Cm3_BeforeStretch', 'Cm3_AfterStretch', 'Cm3_Rebake']
STR_BASE, STR_SLOT = 0x200, 0x20
def S_(name): return STR_BASE + STR_SLOT * STRINGS.index(name)

PTR = dict(StretchBlt=0x00, SetStretchBltMode=0x04, SetBrushOrgEx=0x08, GetDC=0x0C, ReleaseDC=0x10,
           GdiplusStartup=0x14, GdipCreateFromHDC=0x18, GdipSetInterpolationMode=0x1C, GdipSetCompositingMode=0x20,
           GdipCreateBitmapFromScan0=0x24, GdipDrawImageRectRectI=0x28, GdipDeleteGraphics=0x2C, GdipDisposeImage=0x30,
           GdipCreateImageAttributes=0x34, GdipSetImageAttributesWrapMode=0x38,
           GdipGetImageGraphicsContext=0x3C, GdipSetPixelOffsetMode=0x40,
           DllInit=0x44, DllPre=0x48, DllPost=0x4C, DllState=0x50, DllBefore=0x54, DllAfter=0x58, DllRebake=0x5C, DllSaved=0x60)
V_SRCDC, V_DSTDC = 0x80, 0x84
V_DX, V_DY, V_DW, V_DH, V_SX, V_SY, V_SW, V_SH = 0x88, 0x8C, 0x90, 0x94, 0x98, 0x9C, 0xA0, 0xA4
V_TOKEN, V_GFX, V_BMP, V_ATTR, V_STATUS, V_SINPUT, V_DESC = 0xA8, 0xAC, 0xB0, 0xB4, 0xB8, 0xC0, 0x100
V_BMP2, V_GFX2, V_W2, V_H2 = 0xD0, 0xD4, 0xD8, 0xDC
V_RL, V_RT, V_RR, V_RB = 0xE0, 0xE4, 0xE8, 0xEC


def h(n):
    return '0x%x' % n


def resolve(dll, names):
    s = "    lea eax, [ebx+%s]\n    push eax\n    call dword ptr [0x77804c]\n    mov esi, eax\n" % h(S_(dll))
    for n in names:
        s += ("    lea eax, [ebx+%s]\n    push eax\n    push esi\n    call dword ptr [0x77805c]\n"
              "    mov [ebx+%s], eax\n" % (h(S_(n)), h(PTR[n])))
    return s


def build_present():
    L = []
    a = L.append
    a("""
    inc dword ptr [esp+0x18]
    inc dword ptr [esp+0x1c]
    mov eax, {SM}
    test eax, eax
    jnz smooth
    cmp dword ptr [0x83b7e4], 0
    je windowed
    xor esi, esi
    xor edi, edi
    jmp compute
windowed:
    lea eax, [esp+0x20]
    push eax
    push dword ptr [0x865c5c]
    call dword ptr [0x7781bc]
    mov esi, [esp+0x20]
    add esi, [0x83b7cc]
    mov edi, [esp+0x24]
    add edi, [0x83b794]
compute:
    add esi, {OX}
    add edi, {OY}
""")
    for srcoff, dstoff, reg in [(0x10, 0x20, 'esi'), (0x14, 0x24, 'edi'), (0x18, 0x28, 'esi'), (0x1c, 0x2c, 'edi')]:
        a("    mov eax, [esp+%s]\n    imul eax, eax, {S}\n    shr eax, 16\n    add eax, %s\n    mov [esp+%s], eax\n"
          % (h(srcoff), reg, h(dstoff)))
    a("""    push 0
    push 0x1000000
    lea eax, [esp+0x18]
    push eax
    push dword ptr [0x844428]
    lea eax, [esp+0x30]
    push eax
    mov eax, [0x83b7ec]
    mov ecx, [eax]
    push eax
    call dword ptr [ecx+0x14]
    jmp done

smooth:
    mov ebx, {D}
""")
    # grow the source rect by 2px (clamped) so the filters have neighbouring pixels to sample
    for off in (0x10, 0x14):
        a("    sub dword ptr [esp+%s], 2\n    jns g%x\n    mov dword ptr [esp+%s], 0\ng%x:\n" % (h(off), off, h(off), off))
    for off, lim in ((0x18, 800), (0x1c, 600)):
        a("    add dword ptr [esp+%s], 2\n    cmp dword ptr [esp+%s], %d\n    jbe g%x\n    mov dword ptr [esp+%s], %d\ng%x:\n"
          % (h(off), h(off), lim, off, h(off), lim, off))
    for srcoff, dst, org, sub in [(0x10, V_DX, 'OX', None), (0x14, V_DY, 'OY', None), (0x18, V_DW, 'OX', V_DX), (0x1c, V_DH, 'OY', V_DY)]:
        a("    mov eax, [esp+%s]\n    imul eax, eax, {S}\n    shr eax, 16\n    add eax, {%s}\n" % (h(srcoff), org))
        if sub is not None:
            a("    sub eax, [ebx+%s]\n" % h(sub))
        a("    mov [ebx+%s], eax\n" % h(dst))
    a("    mov eax, [esp+0x10]\n    mov [ebx+%s], eax\n    mov eax, [esp+0x14]\n    mov [ebx+%s], eax\n" % (h(V_SX), h(V_SY)))
    a("    mov eax, [esp+0x18]\n    sub eax, [esp+0x10]\n    mov [ebx+%s], eax\n" % h(V_SW))
    a("    mov eax, [esp+0x1c]\n    sub eax, [esp+0x14]\n    mov [ebx+%s], eax\n" % h(V_SH))
    a("""    cmp dword ptr [ebx+%s], 1
    jne nobefore
    mov eax, [ebx+%s]
    add eax, [ebx+%s]
    push eax
    mov eax, [ebx+%s]
    add eax, [ebx+%s]
    push eax
    push dword ptr [ebx+%s]
    push dword ptr [ebx+%s]
    call dword ptr [ebx+%s]
    add esp, 16
nobefore:
""" % (h(PTR['DllState']), h(V_SY), h(V_SH), h(V_SX), h(V_SW), h(V_SY), h(V_SX), h(PTR['DllBefore'])))
    a("    cmp dword ptr [ebx+%s], 0\n    jne resolved\n" % h(PTR['StretchBlt']))
    a(resolve('gdi32.dll', ['StretchBlt', 'SetStretchBltMode', 'SetBrushOrgEx']))
    a(resolve('user32.dll', ['GetDC', 'ReleaseDC']))
    a("    mov eax, {SM}\n    cmp eax, 2\n    jb resolved\n")
    a(resolve('gdiplus.dll', ['GdiplusStartup', 'GdipCreateFromHDC', 'GdipSetInterpolationMode', 'GdipSetCompositingMode',
                              'GdipCreateBitmapFromScan0', 'GdipDrawImageRectRectI', 'GdipDeleteGraphics', 'GdipDisposeImage',
                              'GdipCreateImageAttributes', 'GdipSetImageAttributesWrapMode', 'GdipGetImageGraphicsContext',
                              'GdipSetPixelOffsetMode']))
    a("""    mov dword ptr [ebx+%(status)s], 2
    cmp dword ptr [ebx+%(draw)s], 0
    je resolved
    cmp dword ptr [ebx+%(start)s], 0
    je resolved
    push 0
    lea eax, [ebx+%(sinput)s]
    push eax
    lea eax, [ebx+%(token)s]
    push eax
    call dword ptr [ebx+%(start)s]
    test eax, eax
    jnz resolved
    mov dword ptr [ebx+%(status)s], 1
resolved:
    cmp dword ptr [0x83b7e4], 0
    jne fullscreen
    push dword ptr [0x865c5c]
    call dword ptr [ebx+%(getdc)s]
    mov [ebx+%(dstdc)s], eax
    jmp gotdst
fullscreen:
    lea eax, [ebx+%(dstdc)s]
    mov ecx, [0x83b7ec]
    mov edx, [ecx]
    push eax
    push ecx
    call dword ptr [edx+0x44]
gotdst:
    mov eax, {SM}
    cmp eax, 2
    jb halftone
    cmp dword ptr [ebx+%(status)s], 1
    jne halftone
    mov dword ptr [ebx+%(desc)s], 108
    push 0
    push 0x31
    lea eax, [ebx+%(desc)s]
    push eax
    push 0
    mov ecx, [0x844428]
    mov edx, [ecx]
    push ecx
    call dword ptr [edx+0x64]
    test eax, eax
    jnz relall
    mov eax, 0x21005
    cmp dword ptr [ebx+%(descg)s], 0x7e0
    jne fmt
    mov eax, 0x21006
fmt:
    lea ecx, [ebx+%(bmp)s]
    push ecx
    push dword ptr [ebx+%(descscan)s]
    push eax
    push dword ptr [ebx+%(descpitch)s]
    push 600
    push 800
    call dword ptr [ebx+%(createbmp)s]
    cmp dword ptr [ebx+%(attr)s], 0
    jne haveattr
    lea eax, [ebx+%(attr)s]
    push eax
    call dword ptr [ebx+%(createattr)s]
    push 0
    push 0
    push 3
    push dword ptr [ebx+%(attr)s]
    call dword ptr [ebx+%(setwrap)s]
haveattr:
    mov eax, {SM}
    cmp eax, 3
    jne direct
    mov eax, [ebx+%(sw)s]
    imul eax, eax, {K}
    mov [ebx+%(w2)s], eax
    mov eax, [ebx+%(sh)s]
    imul eax, eax, {K}
    mov [ebx+%(h2)s], eax
    lea eax, [ebx+%(bmp2)s]
    push eax
    push 0
    push 0x22009
    push 0
    push dword ptr [ebx+%(h2)s]
    push dword ptr [ebx+%(w2)s]
    call dword ptr [ebx+%(createbmp)s]
    lea eax, [ebx+%(gfx2)s]
    push eax
    push dword ptr [ebx+%(bmp2)s]
    call dword ptr [ebx+%(getgfx)s]
    push 5
    push dword ptr [ebx+%(gfx2)s]
    call dword ptr [ebx+%(setinterp)s]
    push 4
    push dword ptr [ebx+%(gfx2)s]
    call dword ptr [ebx+%(setpixoff)s]
    push 1
    push dword ptr [ebx+%(gfx2)s]
    call dword ptr [ebx+%(setcomp)s]
    push 0
    push 0
    push dword ptr [ebx+%(attr)s]
    push 2
    push dword ptr [ebx+%(sh)s]
    push dword ptr [ebx+%(sw)s]
    push dword ptr [ebx+%(sy)s]
    push dword ptr [ebx+%(sx)s]
    push dword ptr [ebx+%(h2)s]
    push dword ptr [ebx+%(w2)s]
    push 0
    push 0
    push dword ptr [ebx+%(bmp)s]
    push dword ptr [ebx+%(gfx2)s]
    call dword ptr [ebx+%(draw)s]
    push dword ptr [ebx+%(gfx2)s]
    call dword ptr [ebx+%(delgfx)s]
    lea eax, [ebx+%(gfx)s]
    push eax
    push dword ptr [ebx+%(dstdc)s]
    call dword ptr [ebx+%(fromhdc)s]
    push {Q2}
    push dword ptr [ebx+%(gfx)s]
    call dword ptr [ebx+%(setinterp)s]
    push 4
    push dword ptr [ebx+%(gfx)s]
    call dword ptr [ebx+%(setpixoff)s]
    push 1
    push dword ptr [ebx+%(gfx)s]
    call dword ptr [ebx+%(setcomp)s]
    push 0
    push 0
    push dword ptr [ebx+%(attr)s]
    push 2
    push dword ptr [ebx+%(h2)s]
    push dword ptr [ebx+%(w2)s]
    push 0
    push 0
    push dword ptr [ebx+%(dh)s]
    push dword ptr [ebx+%(dw)s]
    push dword ptr [ebx+%(dy)s]
    push dword ptr [ebx+%(dx)s]
    push dword ptr [ebx+%(bmp2)s]
    push dword ptr [ebx+%(gfx)s]
    call dword ptr [ebx+%(draw)s]
    push dword ptr [ebx+%(gfx)s]
    call dword ptr [ebx+%(delgfx)s]
    push dword ptr [ebx+%(bmp2)s]
    call dword ptr [ebx+%(disposeimg)s]
    jmp unlock
direct:
    lea eax, [ebx+%(gfx)s]
    push eax
    push dword ptr [ebx+%(dstdc)s]
    call dword ptr [ebx+%(fromhdc)s]
    push 7
    push dword ptr [ebx+%(gfx)s]
    call dword ptr [ebx+%(setinterp)s]
    push 1
    push dword ptr [ebx+%(gfx)s]
    call dword ptr [ebx+%(setcomp)s]
    push 0
    push 0
    push dword ptr [ebx+%(attr)s]
    push 2
    push dword ptr [ebx+%(sh)s]
    push dword ptr [ebx+%(sw)s]
    push dword ptr [ebx+%(sy)s]
    push dword ptr [ebx+%(sx)s]
    push dword ptr [ebx+%(dh)s]
    push dword ptr [ebx+%(dw)s]
    push dword ptr [ebx+%(dy)s]
    push dword ptr [ebx+%(dx)s]
    push dword ptr [ebx+%(bmp)s]
    push dword ptr [ebx+%(gfx)s]
    call dword ptr [ebx+%(draw)s]
    push dword ptr [ebx+%(gfx)s]
    call dword ptr [ebx+%(delgfx)s]
unlock:
    push dword ptr [ebx+%(bmp)s]
    call dword ptr [ebx+%(disposeimg)s]
    push 0
    mov ecx, [0x844428]
    mov edx, [ecx]
    push ecx
    call dword ptr [edx+0x80]
    jmp relall
halftone:
    lea eax, [ebx+%(srcdc)s]
    mov ecx, [0x844428]
    mov edx, [ecx]
    push eax
    push ecx
    call dword ptr [edx+0x44]
    test eax, eax
    jnz relall
    push 4
    push dword ptr [ebx+%(dstdc)s]
    call dword ptr [ebx+%(setmode)s]
    push 0
    push 0
    push 0
    push dword ptr [ebx+%(dstdc)s]
    call dword ptr [ebx+%(brush)s]
    push 0xCC0020
    push dword ptr [ebx+%(sh)s]
    push dword ptr [ebx+%(sw)s]
    push dword ptr [ebx+%(sy)s]
    push dword ptr [ebx+%(sx)s]
    push dword ptr [ebx+%(srcdc)s]
    push dword ptr [ebx+%(dh)s]
    push dword ptr [ebx+%(dw)s]
    push dword ptr [ebx+%(dy)s]
    push dword ptr [ebx+%(dx)s]
    push dword ptr [ebx+%(dstdc)s]
    call dword ptr [ebx+%(stretch)s]
    mov ecx, [0x844428]
    mov edx, [ecx]
    push dword ptr [ebx+%(srcdc)s]
    push ecx
    call dword ptr [edx+0x68]
relall:
    cmp dword ptr [ebx+%(dllstate)s], 1
    jne nooverlay
    push {OY}
    push {OX}
    push {S}
    push dword ptr [ebx+%(dh)s]
    push dword ptr [ebx+%(dw)s]
    push dword ptr [ebx+%(dy)s]
    push dword ptr [ebx+%(dx)s]
    push dword ptr [ebx+%(dstdc)s]
    call dword ptr [ebx+%(dllafter)s]
    add esp, 32
nooverlay:
    cmp dword ptr [0x83b7e4], 0
    jne relfull
    push dword ptr [ebx+%(dstdc)s]
    push dword ptr [0x865c5c]
    call dword ptr [ebx+%(reldc)s]
    jmp done
relfull:
    mov ecx, [0x83b7ec]
    mov edx, [ecx]
    push dword ptr [ebx+%(dstdc)s]
    push ecx
    call dword ptr [edx+0x68]
done:
    mov eax, {D}
    cmp dword ptr [eax+%(dllstate)s], 1
    jne skiprebake
    call dword ptr [eax+%(dllrebake)s]
skiprebake:
    mov edi, [esp+0x40]
    mov ebx, [esp+0x34]
""" % dict(status=h(V_STATUS), draw=h(PTR['GdipDrawImageRectRectI']), start=h(PTR['GdiplusStartup']), sinput=h(V_SINPUT),
           token=h(V_TOKEN), getdc=h(PTR['GetDC']), dstdc=h(V_DSTDC), desc=h(V_DESC), descg=h(V_DESC + 0x5c),
           bmp=h(V_BMP), descscan=h(V_DESC + 0x24), descpitch=h(V_DESC + 0x10), createbmp=h(PTR['GdipCreateBitmapFromScan0']),
           gfx=h(V_GFX), fromhdc=h(PTR['GdipCreateFromHDC']), setinterp=h(PTR['GdipSetInterpolationMode']),
           setcomp=h(PTR['GdipSetCompositingMode']), attr=h(V_ATTR), createattr=h(PTR['GdipCreateImageAttributes']),
           setwrap=h(PTR['GdipSetImageAttributesWrapMode']), sh=h(V_SH), sw=h(V_SW), sy=h(V_SY), sx=h(V_SX), dh=h(V_DH),
           dw=h(V_DW), dy=h(V_DY), dx=h(V_DX), delgfx=h(PTR['GdipDeleteGraphics']), disposeimg=h(PTR['GdipDisposeImage']),
           srcdc=h(V_SRCDC), setmode=h(PTR['SetStretchBltMode']), brush=h(PTR['SetBrushOrgEx']), stretch=h(PTR['StretchBlt']),
           reldc=h(PTR['ReleaseDC']), w2=h(V_W2), h2=h(V_H2), bmp2=h(V_BMP2), gfx2=h(V_GFX2),
           getgfx=h(PTR['GdipGetImageGraphicsContext']), setpixoff=h(PTR['GdipSetPixelOffsetMode']),
           dllstate=h(PTR['DllState']), dllafter=h(PTR['DllAfter']), dllrebake=h(PTR['DllRebake'])))
    return ''.join(L)


def build_drawtext():
    return ("""
start:
    mov eax, {D}
    mov ecx, [eax+%(state)s]
    cmp ecx, 1
    je ready
    test ecx, ecx
    jnz original
    push ebx
    push esi
    mov ebx, eax
    mov dword ptr [ebx+%(state)s], 2
    lea eax, [ebx+%(sdll)s]
    push eax
    call dword ptr [0x77804c]
    test eax, eax
    jz loadfail
    mov esi, eax
""" + "".join(
    """    lea eax, [ebx+%%(s%s)s]
    push eax
    push esi
    call dword ptr [0x77805c]
    mov [ebx+%%(p%s)s], eax
    test eax, eax
    jz loadfail
""" % (n, n) for n in ('init', 'pre', 'post', 'before', 'after', 'rebake')) + """    call dword ptr [ebx+%(pinit)s]
    test eax, eax
    jz loadfail
    mov dword ptr [ebx+%(state)s], 1
loadfail:
    pop esi
    pop ebx
    jmp start
ready:
    lea ecx, [esp+4]
    push ecx
    call dword ptr [eax+%(ppre)s]
    add esp, 4
    mov eax, {D}
    pop ecx
    mov [eax+%(saved)s], ecx
    push {POST}
original:
    sub esp, 0xd0
""") % dict(state=h(PTR['DllState']), sdll=h(S_('cm3text.dll')), sinit=h(S_('Cm3_Init')), spre=h(S_('Cm3_PreDraw')),
           spost=h(S_('Cm3_PostDraw')), sbefore=h(S_('Cm3_BeforeStretch')), safter=h(S_('Cm3_AfterStretch')),
           srebake=h(S_('Cm3_Rebake')), pinit=h(PTR['DllInit']), ppre=h(PTR['DllPre']), ppost=h(PTR['DllPost']),
           pbefore=h(PTR['DllBefore']), pafter=h(PTR['DllAfter']), prebake=h(PTR['DllRebake']), saved=h(PTR['DllSaved']))


def build_drawpost():
    return """
    push eax
    pushfd
    pushad
    mov eax, {D}
    call dword ptr [eax+%(ppost)s]
    popad
    popfd
    pop eax
    push ecx
    mov ecx, {D}
    mov ecx, [ecx+%(saved)s]
    xchg ecx, [esp]
    ret
""" % dict(ppost=h(PTR['DllPost']), saved=h(PTR['DllSaved']))


caves = [
    ('Present', 0x13361b, build_present()),
    ('Mouse', 0x1a2190, '''
    mov eax, [esp+0x154]
    movsx edx, ax
    sar eax, 16
    sub edx, {OX}
    jns x1
    xor edx, edx
x1:
    imul edx, edx, {INV}
    shr edx, 16
    cmp edx, 799
    jbe x2
    mov edx, 799
x2:
    sub eax, {OY}
    jns y1
    xor eax, eax
y1:
    imul eax, eax, {INV}
    shr eax, 16
    cmp eax, 599
    jbe y2
    mov eax, 599
y2:
    mov [0x865c50], edx
    mov [0x865c44], eax
'''),
    ('FsMode', 0x132f7e, '''
    push 0x10
    push {H}
    push {W}
    mov ecx, [eax]
'''),
    ('Win1', 0x132d48, '''
    mov ebx, {W}
    mov ebp, {H}
    mov edx, [0x83b7cc]
'''),
    ('Win2', 0x132dac, '''
    mov ebx, 800
    mov ebp, 600
    test eax, eax
    mov [0x865c5c], eax
'''),
    ('TogEnd', 0x132f24, '''
    mov ebx, 800
    mov ebp, 600
    mov edx, [0x865c5c]
'''),
    ('DrawText', 0x135596, build_drawtext()),
    ('DrawPost', 0x135596, build_drawpost()),
    # The end of the game's message pump (it drains the queue, then reads the clock). While the left mouse button is held
    # down, wait {HELD} ms here: every "hold the button" loop in the game (scroll bars, +/- buttons) calls the pump once
    # per pass, so this makes them step in real time instead of as fast as the CPU can spin.
    ('HeldPump', 0x1a1f2c, '''
    cmp dword ptr [0x865c64], 0
    je skip
    push {HELD}
    push -1
    call dword ptr [0x7780dc]
skip:
    mov eax, 0x75f56e
    call eax
'''),
    ('Brush', 0x132ca4, '''
    mov dword ptr [esp+0x34], 9
    mov dword ptr [esp+0x38], edi
'''),
]

ks = Ks(KS_ARCH_X86, KS_MODE_32)
out = []
for name, ret, src in caves:
    code = src
    for k, v in P.items():
        code = code.replace('{%s}' % k, '0x%x' % v)
    assert '{' not in code, (name, code[code.index('{'):code.index('{') + 20])
    enc, _ = ks.asm(code, 0)
    b = bytes(enc)
    relocs = []
    for pn in ORDER:
        pat = struct.pack('<I', P[pn])
        i = 0
        while True:
            i = b.find(pat, i)
            if i < 0:
                break
            relocs.append((i, pn))
            i += 4
    out.append((name, ret, b, sorted(relocs)))


def cs_bytes(b):
    return ', '.join('0x%02X' % x for x in b)


lines = []
for name, ret, b, relocs in out:
    lines.append('''        // %s: returns to 0x%X
        private static readonly byte[] Cave%s = { %s };
        private static readonly int[][] Cave%sParams = { %s };
        private const int Cave%sReturn = 0x%X;''' % (
        name, ret, name, cs_bytes(b), name,
        ', '.join('new int[] { %d, %d }' % (o, ORDER.index(p)) for o, p in relocs), name, ret))

template = open(sys.argv[1]).read()
template = template.replace('/*@CAVES@*/', '\n\n'.join(lines))
arrays = []
arrays.append('        static readonly byte[][] CaveCode = { %s };' % ', '.join('Cave%s' % n for n, *_ in out))
arrays.append('        static readonly int[][][] CaveParams = { %s };' % ', '.join('Cave%sParams' % n for n, *_ in out))
arrays.append('        static readonly int[] CaveReturn = { %s };' % ', '.join('Cave%sReturn' % n for n, *_ in out))
template = template.replace('/*@CAVEARRAYS@*/', '\n'.join(arrays))
template = template.replace('/*@CAVELIST@*/', ', '.join('"%s"' % n for n, *_ in out))
template = template.replace('/*@STRINGS@*/', ', '.join('"%s"' % s for s in STRINGS))
open(sys.argv[2], 'w', encoding='utf-8').write(template)
print('wrote', sys.argv[2], [(n, len(b)) for n, _, b, _ in out])

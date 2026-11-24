"""Caps every texture the game ships at a size the camera can actually show.

docs/06 allows 120 MB of texture and the game loads about 390. The reason is not that
anybody chose badly: it is that third-party packs ship at 2048 and 4096 because they do not
know what camera they will be seen through, and this game's camera never changes. It is a
2.5D side view at a fixed distance -- nothing is ever nearer than the fighter, and most of
what is on screen is a prop five metres behind him.

So the cap is decided by WHAT A THING IS, not by what it shipped as:

    1024  the fighters, their weapons and their armour -- the nearest thing to the lens
    1024  the sky, which is wide and has no detail to lose
    1024  loading screens, which are full-bleed and on screen for a second
     512  everything else in the world: props, ground, walls, particles
      --  the game's own UI, untouched, because ui_fit_check measures it in millimetres
          and a sprite that changes size changes what that check reads

It writes maxTextureSize into the .meta and nothing else -- no material is touched, no
shader changes, nothing moves on screen except that a 4096 texture on a cooking pot stops
being a 4096 texture on a cooking pot. It is idempotent, and every byte of it is in git, so
putting it back is one revert.

    python3 texture_budget.py            # says what it would do
    python3 texture_budget.py --write    # does it
"""
import os
import re
import struct
import sys
import importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("perf_check",
                                               os.path.join(HERE, "perf_check.py"))
perf = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(perf)

# What a thing is, by where it lives and what it is called. First match wins, so the
# narrow rules come before the wide one.
RULES = (
    # The game's own interface is measured in millimetres by ui_fit_check. Leave it.
    (r"(^|/)_Game/UI/(?!Loading)", None,
     "the game's own UI, measured in millimetres by ui_fit_check"),
    # 2048, not 1024, and this is the one row in the table that goes UP.
    #
    # "On screen for a second" is true and it is not the measurement that matters. These
    # are full-bleed: MenuBackdrop grows the rect until the picture covers the screen and
    # lets the overflow go off the edge, so one of them fills every pixel of the glass. The
    # files are 1024 x 420 and a phone is 2400 across, which the note on MenuBackdrop
    # already works out as a 2.3x blow-up -- so capping them at 1024 saves nothing today
    # (they are already smaller than that) and throws away the only fix there is, which is
    # bigger art.
    (r"Loading screen", 2048, "full-bleed and blown up 2.3x already -- do not cap further"),

    # The nearest thing to the lens, and the only thing a player looks AT.
    (r"character|Character|GanzSe|Mercenary|mercenary|hero|Hero|soldier|Soldier"
     r"|Weapon|weapon|Armor|armor|Armour|armour|MedievalWeaponPack", 1024,
     "a fighter or what he is holding -- nearest the camera"),

    # Wide and detail-free.
    (r"Skybox|skybox|Panoramic|Panoramics|FS\d{3}", 1024, "sky"),

    # The floor is the one surface the fight is always ON, so it fills more of the frame
    # than anything else and it is the one place 512 would read as mud.
    #
    # Coal is in it for the same reason: GR_MineGround draws its sides from
    # Coal_Surface_2, which is the face of every block in Arena_Mine. Nothing else
    # in the name says floor, so without it the catch-all would cost it as a prop.
    #
    # Planks and wood are in that list because they ARE the floor here: GR_ArenaRock and
    # GR_wood both draw from Planks_Wood_16, which is the surface every fight happens on.
    # Without them its normal map fell through to the catch-all and was costed as a prop
    # at 512, which on the one surface that fills most of the frame is the mud this rule
    # exists to prevent.
    (r"[Gg]round|[Ff]loor|[Tt]errain|[Dd]irt|[Gg]rass|[Pp]avement|[Rr]ock|[Ss]tone"
     r"|[Ss]and|[Ww]all|[Pp]lanks|[Ww]ood|[Cc]oal", 1024,
     "floor, wall or ground -- the biggest thing on screen"),

    # THE PARALLAX LAYERS ARE NOT PROPS, and the catch-all below was about to treat them
    # as ones. A prop five metres back occupies a few hundred pixels of the screen; these
    # are full-bleed quads 3.21 SCREENS wide, so their 2048 already only works out to 637
    # texels across a phone's whole screen width -- about a 1.7x magnification on a 1080
    # handset. Capped to 512 that becomes 160 texels across the screen, a 6.8x blow-up,
    # which is the exact look make_backdrops.py was rewritten to get rid of.
    #
    # Left where they are. Cheap anyway: fifteen layers at 2048 x 544 or less, all of them
    # single-channel-ish flat colour that compresses to tens of kilobytes on disk.
    (r"Effects/Backdrops/|fx_intro_smoke", None,
     "a parallax layer, 3.2 screens wide -- already magnified at 2048"),

    # Everything else in the world. A 2.5D camera at a fixed distance never gets closer
    # to a wall than it does to the fighter.
    (r".", 512, "a prop, a wall or a floor, five metres behind the fight"),
)

IMAGE = (".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".tif", ".tiff")


def decide(rel):
    for pattern, cap, why in RULES:
        if re.search(pattern, rel):
            return cap, why
    return None, "no rule"


def jpeg_size(path):
    """Width and height out of a JPEG's frame header, without pillow.

    perf.png_size only reads PNGs, and cost() fell back to the .meta's maxTextureSize when
    it got nothing -- so every JPEG in the project was costed at its CEILING rather than at
    its size. The four loading screens are 1024 x 420 files with the ceiling left at 2048,
    so they were being reported at 4.0 MB each and recommended for a cap to 1024 that would
    not have saved a single byte, because they are already smaller than that. Twelve
    megabytes of the overspend this file exists to measure was imaginary.
    """
    try:
        with open(path, "rb") as f:
            if f.read(2) != b"\xff\xd8":
                return None
            while True:
                head = f.read(2)
                if len(head) < 2 or head[0] != 0xFF:
                    return None
                marker = head[1]
                if marker in (0xD8, 0x01) or 0xD0 <= marker <= 0xD7:
                    continue
                length = struct.unpack(">H", f.read(2))[0]
                if marker in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7,
                              0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF):
                    f.read(1)
                    h, w = struct.unpack(">HH", f.read(4))
                    return w, h
                f.seek(length - 2, 1)
    except (OSError, struct.error):
        return None


def cost(path, cap):
    wh = perf.png_size(path) or jpeg_size(path)
    w = h = cap or perf.cap_of(path) or 1024
    if wh:
        w, h = wh
        limit = cap or perf.cap_of(path)
        if limit:
            w, h = min(w, limit), min(h, limit)
    return w * h * perf.BYTES_PER_PIXEL * perf.MIP_FACTOR / (1024 * 1024)


def set_cap(meta, cap):
    """Writes maxTextureSize on every platform block in the .meta. True when it changed."""
    text = open(meta, encoding="utf-8", errors="replace").read()
    new = re.sub(r"maxTextureSize: \d+", f"maxTextureSize: {cap}", text)
    if new == text:
        return False
    open(meta, "w", encoding="utf-8").write(new)
    return True


def main(argv):
    write = "--write" in argv

    ship = []
    pool = os.path.join(perf.ASSETS, "_Game", "Maps", "MapPool.asset")
    if os.path.exists(pool):
        for m in re.finditer(r"- scene: (.+?)\n(?:.*?\n)*?\s*enabled: (\d)",
                             perf.read(pool)):
            if m.group(2) != "1":
                continue
            at = os.path.join(perf.SCENES, m.group(1).strip() + ".unity")
            if os.path.exists(at):
                ship.append(at)
    menu = os.path.join(perf.SCENES, "Menu.unity")
    if os.path.exists(menu):
        ship.append(menu)

    paths = perf.guid_map()
    seen = perf.reachable(ship, paths)
    pictures = sorted(p for p in seen if p.lower().endswith(IMAGE))

    before = after = 0.0
    changed, kept, rows = 0, 0, []
    for p in pictures:
        rel = os.path.relpath(p, perf.ASSETS).replace(os.sep, "/")
        was = perf.cap_of(p)
        cap, why = decide(rel)
        before += cost(p, was)

        if cap is None or (was is not None and was <= cap):
            after += cost(p, was)
            kept += 1
            continue

        after += cost(p, cap)
        rows.append((cost(p, was) - cost(p, cap), rel, was, cap, why))
        if write and set_cap(p + ".meta", cap):
            changed += 1

    rows.sort(reverse=True)
    print(f"  note: {len(pictures)} texture(s) ship; {len(rows)} would be capped, "
          f"{kept} left alone")
    for saved, rel, was, cap, why in rows[:12]:
        print(f"      -{saved:5.1f} MB  {was or '-':>5} -> {cap:<5} {rel}")
        print(f"                     {why}")
    if len(rows) > 12:
        print(f"      and {len(rows) - 12} more")

    print(f"  note: {before:.0f} MB before, {after:.0f} MB after, "
          f"against docs/06's {perf.TEXTURE_BUDGET_MB} MB")
    if write:
        print(f"  note: wrote {changed} .meta file(s). Unity reimports them on next focus.")
    else:
        print("  note: nothing written. Run again with --write to do it.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

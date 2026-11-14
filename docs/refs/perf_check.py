"""What the game will cost on a phone, measured off the project rather than guessed.

docs/06 set the budget and nothing has ever checked it:

    Rendering: URP, Forward+, unlit/simple-lit shaders, no post on low-tier.
    Triangles on screen: <= 180 K.  Texture memory: <= 120 MB (flat colours, atlases,
    almost no large textures).  60 fps on a weak phone.

So this measures four things that decide whether that holds, and it measures them on what
the game actually SHIPS -- the scenes in the map pool plus the menu -- rather than on
everything sitting in Assets. A project with 1.8 GB of third-party demo content and 994
textures is not a project in trouble; it is a project where almost none of that is
reachable, and the only way to tell the difference is to follow the references.

  * TEXTURES THAT SHIP. Every guid reachable from a shipped scene, through prefabs,
    materials and controllers, to a fixed point. Their import caps, and what they cost in
    memory. A 2048 texture on a prop nobody looks at closely is the cheapest 6 MB anybody
    ever saved.
  * LIGHTS. A realtime light in URP is the most expensive object in a mobile scene, and
    docs/07's own mitigation for "low poly looks cheap" is BAKED lighting -- so a scene
    with no lightmap and a dozen realtime lights is paying twice and getting neither.
  * THE PIPELINE ASSET. Render scale, MSAA, shadow distance, and above all whether extra
    lights are per-pixel, which is the single setting that decides the cost of the lights
    above.
  * GARBAGE PER FRAME. Anything inside Update, LateUpdate, FixedUpdate or OnGUI that
    allocates: a Find, a GetComponent, a new list, an interpolated string. At 60 Hz a
    single 48-byte allocation per frame per fighter is 5 KB a second, and a collection on
    a phone is a dropped frame you cannot reproduce in the editor.

Nothing here opens Unity. Everything is read off the YAML and the C#, so it runs in a
second and in CI.

    python3 perf_check.py
"""
import os
import re
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.join(HERE, "..", "..", "Gulag runners")
ASSETS = os.path.join(PROJECT, "Assets")
SCENES = os.path.join(ASSETS, "Scenes")

# docs/06, to the number.
TEXTURE_BUDGET_MB = 120
TRIANGLE_BUDGET_K = 180

# What a compressed texel costs on a phone. ASTC 4x4 and ETC2 RGBA are both a byte a
# pixel; mips add a third. Both are the pessimistic end, which is the end a budget wants.
BYTES_PER_PIXEL = 1.0
MIP_FACTOR = 1.33

# A realtime light in a forward mobile renderer is per-pixel work on everything it
# touches. More than this many in one scene is worth a line in the report; docs/07 asks
# for baked light anyway.
REALTIME_LIGHT_WARN = 4

# Inside a per-frame method, these allocate or search, and both are the same mistake at
# 60 Hz. The text is what the report says when it finds one.
IN_FRAME = (
    (r"\bFindObjectsByType\b|\bFindFirstObjectByType\b|\bFindObjectOfType\b|\bGameObject\.Find\b",
     "searches the whole scene"),
    (r"\bGetComponent(InChildren|InParent)?\s*<", "looks a component up"),
    (r"\bnew\s+(List|Dictionary|HashSet|Queue|Stack)\s*<", "allocates a collection"),
    (r"\bnew\s+\w+\s*\[", "allocates an array"),
    (r"\$\"", "builds a string"),
    (r"\.ToString\(\)|string\.Format|string\.Join|\bstring\.Concat\b", "builds a string"),
    (r"\bLinq\b|\.Where\(|\.Select\(|\.OrderBy\(|\.ToList\(\)|\.ToArray\(\)",
     "runs LINQ, which allocates twice"),
)

PER_FRAME = ("Update", "LateUpdate", "FixedUpdate", "OnGUI", "OnAnimatorIK")

# Files that are allowed to do it: editor tools run once when a human presses a button,
# and a few places do the lookup once and cache it. Anything here says WHY.
ALLOWED = {
    "Editor",                      # a builder runs when a human clicks, not at 60 Hz
}


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def walk(root, suffix):
    for here, _, files in os.walk(root):
        for f in files:
            if f.endswith(suffix):
                yield os.path.join(here, f)


# ---------------------------------------------------------------- the reference graph

def guid_map():
    """guid -> path, from every .meta in the project."""
    found = {}
    for meta in walk(ASSETS, ".meta"):
        try:
            head = open(meta, encoding="utf-8", errors="replace").read(400)
        except OSError:
            continue
        m = re.search(r"^guid: ([0-9a-f]{32})", head, re.M)
        if m:
            found[m.group(1)] = meta[:-5]
    return found


GUID = re.compile(r"guid: ([0-9a-f]{32})")
FOLLOW = (".prefab", ".mat", ".asset", ".controller", ".overrideController", ".unity")


def reachable(seeds, paths):
    """Every asset a set of scenes can reach, to a fixed point."""
    seen, queue = set(), list(seeds)
    while queue:
        path = queue.pop()
        if path in seen or not os.path.exists(path):
            continue
        seen.add(path)
        if not path.endswith(FOLLOW):
            continue
        try:
            text = read(path)
        except OSError:
            continue
        for g in set(GUID.findall(text)):
            hit = paths.get(g)
            if hit and hit not in seen:
                queue.append(hit)
    return seen


def png_size(path):
    """Width and height from the IHDR, without pillow."""
    try:
        with open(path, "rb") as f:
            if f.read(8) != b"\x89PNG\r\n\x1a\n":
                return None
            f.read(4)
            if f.read(4) != b"IHDR":
                return None
            return struct.unpack(">II", f.read(8))
    except (OSError, struct.error):
        return None


def cap_of(path):
    """The import cap on a texture, and whether it is set per platform."""
    meta = path + ".meta"
    if not os.path.exists(meta):
        return None
    text = read(meta)
    sizes = [int(v) for v in re.findall(r"maxTextureSize: (\d+)", text)]
    return max(sizes) if sizes else None


# ---------------------------------------------------------------- per-frame methods

def bodies(text, names):
    """The body of each named method, by brace matching."""
    out = []
    for name in names:
        for m in re.finditer(r"(?:void|IEnumerator)\s+" + name + r"\s*\([^)]*\)\s*\{", text):
            at = m.end() - 1
            depth, i = 0, at
            while i < len(text):
                if text[i] == "{":
                    depth += 1
                elif text[i] == "}":
                    depth -= 1
                    if depth == 0:
                        break
                i += 1
            out.append((name, text[at:i], text[:at].count("\n") + 1))
    return out


def strip(text):
    """Comments gone, strings kept, so a word in a tooltip is not a finding."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            out.append(c)
            i += 1
            while i < n:
                out.append(text[i])
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def main():
    problems, notes = [], []

    # ---- which scenes ship
    # The pool names its arenas rather than pointing at them, which is what lets an
    # arena be renamed without a guid hunt -- so they are found by name, the way every
    # other check in here finds them.
    pool = os.path.join(ASSETS, "_Game", "Maps", "MapPool.asset")
    paths = guid_map()
    ship = []
    if os.path.exists(pool):
        text = read(pool)
        for m in re.finditer(r"- scene: (.+?)\n(?:.*?\n)*?\s*enabled: (\d)", text):
            if m.group(2) != "1":
                continue
            at = os.path.join(SCENES, m.group(1).strip() + ".unity")
            if os.path.exists(at):
                ship.append(at)
    menu = os.path.join(SCENES, "Menu.unity")
    if os.path.exists(menu):
        ship.append(menu)
    ship = sorted(set(ship))

    if not ship:
        print("  no scene in the map pool, so there is nothing to measure yet")
        print("perf_check: 1 problem(s)")
        return 1

    notes.append(f"{len(ship)} scene(s) ship: "
                 + ", ".join(os.path.basename(s)[:-6] for s in ship))

    # ---- lights, particles and objects, per scene
    for scene in ship:
        text = read(scene)
        objects = text.count("--- !u!1 &")
        lights = re.findall(r"(?s)--- !u!108 &.*?(?=--- !u!|\Z)", text)
        live = sum(1 for l in lights if "m_Lightmapping: 4" in l)
        baked = len(lights) - live
        particles = text.count("--- !u!198 &")
        name = os.path.basename(scene)[:-6]
        notes.append(f"{name}: {objects} object(s), {len(lights)} light(s) "
                     f"({live} realtime, {baked} baked or mixed), "
                     f"{particles} particle system(s)")
        if live > REALTIME_LIGHT_WARN:
            problems.append(
                f"{name} has {live} REALTIME light(s). In a forward mobile renderer each "
                f"one is per-pixel work on everything it reaches, and docs/07's own answer "
                f"to \"low poly looks cheap\" is BAKED light -- so this scene is paying for "
                f"the expensive kind and not getting the look the cheap kind buys")

        lighting = os.path.join(os.path.dirname(scene),
                                os.path.basename(scene)[:-6], "LightingData.asset")
        if not os.path.exists(lighting) and live > 0:
            problems.append(
                f"{name} has no LightingData.asset beside it, so nothing in it is baked at "
                f"all. Every light is being paid for every frame")

    # ---- the pipeline asset
    rp = os.path.join(ASSETS, "Settings", "Mobile_RPAsset.asset")
    if os.path.exists(rp):
        text = read(rp)

        def num(key, fallback=None):
            m = re.search(key + r": ([-\d.]+)", text)
            return float(m.group(1)) if m else fallback

        mode = num("m_AdditionalLightsRenderingMode")
        limit = num("m_AdditionalLightsPerObjectLimit")
        scale = num("m_RenderScale")
        msaa = num("m_MSAA")
        notes.append(f"mobile pipeline: render scale {scale}, MSAA {msaa}, "
                     f"extra lights mode {mode} (0 off, 1 per pixel, 2 per vertex), "
                     f"up to {limit} per object")
        if mode == 1:
            problems.append(
                "the mobile pipeline shades extra lights PER PIXEL, up to "
                f"{limit:.0f} on every object. That is the single most expensive setting "
                "in a mobile URP, and it is what makes the realtime lights above cost "
                "what they cost. Per-vertex, or baked and off, is the same picture for a "
                "fraction of the fill")
    else:
        notes.append("no Mobile_RPAsset.asset, so the phone tier is unconfigured")

    # ---- the textures that actually ship
    seen = reachable(ship, paths)
    pictures = [p for p in seen
                if p.lower().endswith((".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr"))]
    total, uncapped, biggest = 0.0, [], []
    for p in pictures:
        cap = cap_of(p)
        size = png_size(p)
        w = h = cap or 1024
        if size:
            w, h = size
            if cap:
                w, h = min(w, cap), min(h, cap)
        cost = w * h * BYTES_PER_PIXEL * MIP_FACTOR / (1024 * 1024)
        total += cost
        biggest.append((cost, os.path.relpath(p, ASSETS), w, h, cap))
        if cap is None or cap >= 2048:
            uncapped.append((os.path.relpath(p, ASSETS), cap))

    biggest.sort(reverse=True)
    notes.append(f"{len(pictures)} texture(s) are reachable from the shipped scenes, "
                 f"out of {len(list(walk(ASSETS, '.png'))) + len(list(walk(ASSETS, '.tga')))} "
                 f"in the project")
    notes.append(f"they cost about {total:.0f} MB of the {TEXTURE_BUDGET_MB} MB docs/06 "
                 f"allows, at a byte a pixel with mips")
    for cost, rel, w, h, cap in biggest[:8]:
        notes.append(f"    {cost:5.1f} MB  {w}x{h}"
                     + (f" capped {cap}" if cap else " uncapped") + f"  {rel}")

    # Which KIND of texture it is, because the answer changes the fix. docs/06 says the
    # shaders are unlit and simple-lit: a simple-lit shader samples a base map and, at
    # most, a normal. It never samples roughness, metallic, occlusion or emission -- so
    # those are megabytes loaded to be ignored, and deleting them costs nothing at all.
    UNUSED = re.compile(r"(_|\b)(R|M|E|AO|ORM|MRA|Roughness|Metallic|Occlusion|Emissive|"
                        r"Emission|Specular|Height|Displacement)(\.|_|$)", re.I)
    NORMALS = re.compile(r"(_|\b)(N|Normal|NormalMap)(\.|_|$)", re.I)
    dead = sum(c for c, rel, *_ in biggest
               if UNUSED.search(os.path.splitext(os.path.basename(rel))[0]))
    normals = sum(c for c, rel, *_ in biggest
                  if NORMALS.search(os.path.splitext(os.path.basename(rel))[0]))
    third = sum(c for c, rel, *_ in biggest if rel.startswith("ThirdParty"))
    notes.append(f"of that, about {dead:.0f} MB is roughness, metallic, occlusion or "
                 f"emission -- maps a simple-lit shader never samples (docs/06)")
    notes.append(f"about {normals:.0f} MB is normal maps, and {third:.0f} MB comes from "
                 f"ThirdParty packs rather than the game's own art")

    if dead > 10:
        problems.append(
            f"about {dead:.0f} MB of the shipped textures are roughness, metallic, "
            f"occlusion or emission maps. docs/06 ships unlit and simple-lit shaders, "
            f"which never sample them: that is memory loaded to be ignored. Taking them "
            f"off the materials that reference them costs nothing visible at all and is "
            f"the single cheapest thing on this list")

    if total > TEXTURE_BUDGET_MB:
        problems.append(
            f"the shipped textures come to about {total:.0f} MB against docs/06's "
            f"{TEXTURE_BUDGET_MB} MB. The cheapest fix is the import cap: {len(uncapped)} "
            f"of them are at 2048 or have no cap at all, and a prop nobody walks up to "
            f"does not need more than 512")

    # ---- garbage per frame
    hot = []
    for cs in walk(os.path.join(ASSETS, "_Game"), ".cs"):
        rel = os.path.relpath(cs, os.path.join(ASSETS, "_Game"))
        if rel.split(os.sep)[0] in ALLOWED:
            continue
        text = strip(read(cs))
        for name, body, line in bodies(text, PER_FRAME):
            for pattern, why in IN_FRAME:
                for m in re.finditer(pattern, body):
                    hot.append((rel, name, line + body[:m.start()].count("\n"),
                                why, m.group(0).strip()))

    notes.append(f"{len(hot)} allocation(s) or lookup(s) inside a per-frame method")
    kinds = {}
    for rel, name, line, why, what in hot:
        kinds.setdefault(why, []).append(f"{rel}:{line} in {name}()")
    for why, where in sorted(kinds.items(), key=lambda kv: -len(kv[1])):
        notes.append(f"    {len(where):3d}  {why}")
        for one in where[:3]:
            notes.append(f"         {one}")
        if len(where) > 3:
            notes.append(f"         and {len(where) - 3} more")

    if hot:
        problems.append(
            f"{len(hot)} thing(s) allocate or search inside a method that runs every "
            f"frame. At 60 Hz the collections they cause are the dropped frames nobody "
            f"can reproduce in the editor -- cache the lookups in Awake, keep one list "
            f"and clear it, and build strings only when the text has changed")

    for n in notes:
        print(("  " + n) if n.startswith(" ") else ("  note: " + n))
    for p in problems:
        print("  " + p)
    print(f"perf_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

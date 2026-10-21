"""Does the rarity glow actually glow?

The tier of an item is a GLOW and nothing else — it is the one thing that answers "is that
worth walking to" from the other side of an arena. Both loot views write the tier colour into
_EmissionColor and scale it by a rarity multiplier, and `LootGlowSetup` switches the emission
keyword on in the materials, because a MaterialPropertyBlock cannot do that itself.

None of which produces a GLOW on its own. An emissive surface is just a bright surface: the
halo around it is Bloom, and a colour above 1 keeps its hue only if a tonemapper brings it
back down. Measured when this check was written, the project had neither. Both gameplay
scenes pointed their Global Volume at `SampleSceneProfile`, Unity's template, in which every
single override — Bloom, Tonemapping, Vignette, Motion Blur — sits at `active: 0`; so the
effective post-processing was URP's fallback `DefaultVolumeProfile`, whose Tonemapping is
mode 0 (None) and whose Bloom is on at intensity 0.

So a legendary at an emission of 3.6 did not bloom and did not stay violet. It clipped to
white, which is the failure `ItemModels.DefaultPalette` already carries a long comment
about — the palette was made super-saturated to fight it, and `LootGlowSetup` hangs a point
light on every loot slot to fight it again. Both are workarounds for these two switches.

This check reads the chain end to end and fails if any link is dead:

  * every gameplay scene has a Volume, and its profile has Bloom active with intensity > 0;
  * that profile tonemaps (mode != None), or bright colours clip to white and lose their hue;
  * the cameras render post-processing at all;
  * the render pipeline supports HDR, without which nothing is ever above 1 to bloom;
  * and the rarity emissions in C# straddle the profile's own bloom threshold — a legendary
    well over it, a common at or below it, which is the design docs/03 asks for: a common
    find is the baseline and if everything shouts then nothing does.

That last one is the point of keeping this in one place. The threshold lives in a YAML asset
and the emissions live in a C# file, nothing links them, and either can be moved on its own
by somebody who cannot see the other.
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
ASSETS = os.path.join(ROOT, "Gulag runners", "Assets")
GAME = os.path.join(ASSETS, "_Game")

# The scenes a match is played in. The menu has its own look and its own profile.
GAMEPLAY = ("Arena_Demo.unity", "SetupScene.unity")

VOLUME_SCRIPT = "172515602e62fb746b5d573b38a5fe58"


def read(path):
    return io.open(path, encoding="utf-8", errors="replace").read()


def profiles():
    """guid -> the text of that .asset, for everything under Assets."""
    out = {}
    for root, _, files in os.walk(ASSETS):
        for f in files:
            if not f.endswith(".asset.meta"):
                continue
            m = re.search(r"^guid: ([0-9a-f]{32})", read(os.path.join(root, f)), re.M)
            if m:
                out[m.group(1)] = os.path.join(root, f[:-5])
    return out


def override(src, name):
    """One volume override out of a profile, as {field: value} plus 'active'."""
    m = re.search(r"m_Name: " + name + r"\n(.*?)(?=\n--- |\Z)", src, re.S)
    if not m:
        return None
    body = m.group(1)
    d = {}
    a = re.search(r"^  active: (\d)", body, re.M)
    d["active"] = int(a.group(1)) if a else 0
    for field, value in re.findall(r"^  (\w+):\n    m_OverrideState: \d\n    m_Value: "
                                   r"(-?[\d.]+)$", body, re.M):
        d[field] = float(value)
    return d


def main():
    found = profiles()
    problems, notes = [], []
    thresholds = []

    for scene in GAMEPLAY:
        path = os.path.join(ASSETS, "Scenes", scene)
        if not os.path.exists(path):
            problems.append(f"{scene}: not there")
            continue
        src = read(path)

        if f"guid: {VOLUME_SCRIPT}" not in src:
            problems.append(f"{scene}: no Volume at all, so it renders with URP's fallback "
                            f"profile and nothing in this project decides its look")
            continue

        m = re.search(r"sharedProfile: \{fileID: 11400000, guid: ([0-9a-f]{32})", src)
        if not m:
            problems.append(f"{scene}: its Volume has no profile")
            continue

        asset = found.get(m.group(1))
        if asset is None:
            problems.append(f"{scene}: its volume profile {m.group(1)} is not in the project")
            continue

        name = os.path.basename(asset)
        psrc = read(asset)

        bloom = override(psrc, "Bloom")
        if bloom is None:
            problems.append(f"{scene} -> {name}: no Bloom override, so an emissive item is "
                            f"only a bright item and the rarity never haloes")
        else:
            if not bloom["active"]:
                problems.append(f"{scene} -> {name}: Bloom is switched OFF (active: 0)")
            elif bloom.get("intensity", 0) <= 0:
                problems.append(f"{scene} -> {name}: Bloom intensity is "
                                f"{bloom.get('intensity', 0)}, which is Bloom doing nothing")
            else:
                thresholds.append((name, bloom.get("threshold", 0)))

        tone = override(psrc, "Tonemapping")
        if tone is None or not tone["active"]:
            problems.append(f"{scene} -> {name}: no tonemapping, so every colour above 1 "
                            f"clips channel by channel and a bright violet comes out white")
        elif tone.get("mode", 0) == 0:
            problems.append(f"{scene} -> {name}: tonemapping mode is None, same thing")

        if "m_RenderPostProcessing: 1" not in src:
            problems.append(f"{scene}: no camera renders post-processing, so the volume is "
                            f"decoration")

    for rp in ("Mobile_RPAsset.asset", "PC_RPAsset.asset"):
        path = os.path.join(ASSETS, "Settings", rp)
        if os.path.exists(path) and "m_SupportsHDR: 1" not in read(path):
            problems.append(f"{rp}: HDR is off, so no colour is ever above 1 and there is "
                            f"nothing for Bloom to find")

    # ---- the C# side: do the rarity emissions straddle that threshold?
    models = read(os.path.join(GAME, "Presentation", "ItemModel.cs"))
    emission = {}
    body = re.search(r"RarityEmission\(ItemId item\)(.*?)\};", models, re.S)
    if body is None:
        problems.append("ItemModel.cs: no RarityEmission to read")
    else:
        for rarity, value in re.findall(r"Sim\.Rarity\.(\w+) =>\s*([\d.]+)f",
                                        body.group(1)):
            emission[rarity] = float(value)
        fallback = re.search(r"_ =>\s*([\d.]+)f", body.group(1))
        if fallback:
            emission["Common"] = float(fallback.group(1))

    glow = re.search(r"public float glow = ([\d.]+)f",
                     read(os.path.join(GAME, "Presentation", "GroundItemView.cs")))
    scale = float(glow.group(1)) if glow else 1.0

    if thresholds and len(emission) >= 2:
        name, threshold = thresholds[0]
        lit = {r: v * scale for r, v in emission.items()}
        for r, v in sorted(lit.items(), key=lambda kv: kv[1]):
            notes.append(f"{r} lights to {v:.2f} against {name}'s bloom threshold "
                         f"{threshold:g}")
        if lit.get("Legendary", 0) < threshold * 1.6:
            problems.append(f"a legendary lights to {lit.get('Legendary', 0):.2f} against a "
                            f"bloom threshold of {threshold:g}: the best find in the game "
                            f"would barely halo")
        if lit.get("Common", 0) > threshold * 1.35:
            problems.append(f"a common lights to {lit.get('Common', 0):.2f} against a bloom "
                            f"threshold of {threshold:g}: the baseline find would halo as "
                            f"loudly as a rare, and if everything shouts then nothing does")

    # ---- one ramp, everywhere. A chest and the item that comes out of it may not have
    # two ideas of what "rare" looks like, and the serialised arrays in prefabs and scenes
    # are a copy of the C# default that nothing keeps honest.
    want = [tuple(round(float(v), 3) for v in m)
            for m in re.findall(r"new Color\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)",
                                re.search(r"DefaultPalette\(\)(.*?)\};", models,
                                          re.S).group(1))]
    for folder in ("Prefabs", "Scenes", "_Game"):
        root = os.path.join(ASSETS, folder)
        for dirpath, _, files in os.walk(root):
            for f in files:
                if not (f.endswith(".prefab") or f.endswith(".unity")):
                    continue
                src = read(os.path.join(dirpath, f))
                for block in re.findall(r"tierColours:\n((?:  - \{r: [^\n]*\n)+)", src):
                    got = [tuple(round(float(x), 3) for x in m)
                           for m in re.findall(r"r: (-?[\d.]+), g: (-?[\d.]+), "
                                               r"b: (-?[\d.]+)", block)]
                    if want and got != want:
                        problems.append(f"{f}: a tierColours array that is not the one in "
                                        f"ItemModels.DefaultPalette() -- {got} against "
                                        f"{want}")

    for n in notes:
        print(f"  note - {n}")
    for p in problems:
        print(f"  {p}")
    print(f"glow_check: {len(problems)} problem(s) across {len(GAMEPLAY)} gameplay scene(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

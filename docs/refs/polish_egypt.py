"""The look of the Egypt arena (loc3_new.unity): NIGHT, and seven flames to see by.

"loc3_new is the Egypt arena and the one you made playable is just the old version of it
-- it is the NIGHT Egypt arena."

Measured before changed, because the first half of that is not what the files say:
The arena already carried 201 of loc3_new's 201 prefab instances (build_egypt.py
took them from loc3_new, not from loc3, and the only two it left behind are a pair of
wall-mounted swords). What was the old version was the LIGHT. Dawid lit his level with one
cubemap -- FS000_Night_02_Moonless -- and this pass had given the transplant a SUNSET:
FS002_Sunset, a 2.1 sun from 55 degrees up, sand-coloured fog and a grade with the warmth
and the saturation pushed up. A night arena in daylight is a different arena, which is
exactly how it read.

So the same five knobs, turned the other way:

  the sky             FS000_Night_02_Moonless, his own choice. FS000_Night_02 is the same
                      sky with the moon left in it, one guid away, if a moon in frame is
                      wanted -- but the arena is his and so is the sky.
  the moon            0.8 at (0.64, 0.74, 1.00) from 62 degrees up: the key light has to
                      shape a fighter even when the sky says there is nothing up there.
  the air             the same 14..46 m the fog had, because that distance is docs/08's
                      rule (haze behind the fight, never on it) and light_check measures
                      it against the camera, not against the hour. Only the COLOUR is
                      night: deep blue instead of sand.
  Arena_Egypt_Grade   cool balance, exposure up a quarter stop, contrast up, saturation
                      DOWN: night is blue and quiet, and the lift is what keeps a fighter
                      off a black floor.
  the flames          seven, not five. Dawid placed three Latern, two PP_Lantern and two
                      fire bowls, and the sunset pass only looked for "lantern" -- so the
                      two bowls stood dark, which in daylight is a detail and at night is
                      two of the seven things in the room that give off light.

And the lights sit IN the lamps now. Every one of the five was 0.52 m above its lamp,
which was a guess at "half its own height" made without measuring one: a Latern is 0.48 m
tall and a PP_Lantern 0.53, so half a metre up is a glow hanging in the air above a lamp
rather than inside it. Measured, from the FBX bounds and from where each prop stands
against its own floor:

  Latern        0.48 m, pivot near its foot          -> the flame is 0.24 m up
  PP_Lantern    0.59 m at 0.9 scale = 0.53 m         -> 0.27 m up
  fire bowl     0.89 m, pivot at its CENTRE (its base lands on the floor to within 4 cm
                at both of the two, which is how the pivot is known) -> the rim, and the
                fire over it, is 0.50 m up

Nothing here is simulation: every number is a renderer field, and the arena plays the same
with all of it switched off.

    python3 polish_egypt.py
"""
import io
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry                        # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENE = os.path.join(ASSETS, "Scenes", "loc3_new.unity")
GRADE = os.path.join(ASSETS, "Settings", "Arena_Egypt_Grade.asset")

GRADE_GUID = "7c1b0d93a6f54c5f9d2e8ab41f0c37d2"
GROUND_MAT = "f7a4bc5a3db4b9042bbf990596a40780"   # CGM_EgyptPack/Material/LVL_Ground
MINE_GRADE_GUID = "50a3be9bad144fada0613db6dd2f653f"
NIGHT_SKY = "94caf2ba4ca5e46bfaba53afcc8778dd"   # FS000_Night_02_Moonless, his
MOON_SKY = "8202c457dff32486ba5ae1f2f548d983"    # the same sky with the moon in it

WHITE_BALANCE = "221518ef91623a7438a71fef23660601"
COLOR_ADJUSTMENTS = "66f335fb1ffd8684294ad653bf1c7564"
VOLUME_PROFILE = "d7fd9488000d3734a9e00ee676215985"

# Where the fire is, per prop, in metres above the prop's OWN origin -- measured from the
# FBX bounds and checked against the floor each one stands on, rather than guessed at half
# a lamp's height. See the note at the top. The number after it is the light: colour,
# intensity, range and how hard it flickers; a bowl is an open fire and a lantern is a
# flame in a box, so the bowl is brighter, wider and less steady.
FLAMES = (
    ("Latern",     0.24, "Lantern Light",   2.4, 7.0,  0.12),
    ("PP_Lantern", 0.27, "Lantern Light",   2.4, 7.0,  0.12),
    ("fire_bowl",  0.50, "Fire Bowl Light", 3.2, 9.5,  0.20),
)
FIRE = "{r: 1, g: 0.72, b: 0.38, a: 1}"


def documents(text):
    return {int(m.group(2)): (int(m.group(1)), m.group(4)) for m in
            re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                        text, re.S)}


def grade():
    """The arena's own colour grade, built the way Arena_Mine_Grade is.

    Written every run, not kept if it exists: the file on disk is the SUNSET grade this
    script used to write -- warm balance, saturation up -- and leaving it alone is how an
    arena ends up lit for night and graded for noon.
    """

    io.open(GRADE, "w", encoding="utf-8").write(f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {VOLUME_PROFILE}, type: 3}}
  m_Name: Arena_Egypt_Grade
  m_EditorClassIdentifier:
  components:
  - {{fileID: -9001}}
  - {{fileID: -9002}}
--- !u!114 &-9001
MonoBehaviour:
  m_ObjectHideFlags: 3
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {WHITE_BALANCE}, type: 3}}
  m_Name: WhiteBalance
  m_EditorClassIdentifier:
  active: 1
  temperature:
    m_OverrideState: 1
    m_Value: -14
  tint:
    m_OverrideState: 1
    m_Value: 2
--- !u!114 &-9002
MonoBehaviour:
  m_ObjectHideFlags: 3
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {COLOR_ADJUSTMENTS}, type: 3}}
  m_Name: ColorAdjustments
  m_EditorClassIdentifier:
  active: 1
  postExposure:
    m_OverrideState: 1
    m_Value: 0.25
  contrast:
    m_OverrideState: 1
    m_Value: 8
  colorFilter:
    m_OverrideState: 1
    m_Value: {{r: 0.94, g: 0.97, b: 1, a: 1}}
  hueShift:
    m_OverrideState: 1
    m_Value: 0
  saturation:
    m_OverrideState: 1
    m_Value: -6
""")
    io.open(GRADE + ".meta", "w", encoding="utf-8").write(
        "fileFormatVersion: 2\n"
        f"guid: {GRADE_GUID}\n"
        "NativeFormatImporter:\n"
        "  externalObjects: {}\n"
        "  mainObjectFileID: 11400000\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n")
    print("  Arena_Egypt_Grade.asset: cool balance, +0.25 EV, +8 contrast, "
          "saturation DOWN")


def flames():
    """Every prop in the level that is on fire, in world space, left to right.

    "lantern" was the whole filter before, which is why Dawid's two fire bowls stood dark:
    a filter written from the name of the thing you remember placing is a filter that
    misses the things you forgot. The list is FLAMES now, and each entry carries what the
    light on it should be.
    """
    scene = geometry.Scene(SCENE)
    out = []
    for pos, who in sorted(scene.prefab_art(), key=lambda r: r[0][0]):
        for kind, up, name, intensity, span, swing in FLAMES:
            if kind.lower() in who.lower():
                out.append(dict(pos=pos, who=who, up=up, name=name,
                                intensity=intensity, span=span, swing=swing))
                break
    return out


GROUND_BOX = 1920100000


def under_the_temple(text):
    """Something below the bottom floor, because the camera frames five metres.

    light_check's words: "the lowest floor is at y +1.12 and nothing is drawn below y
    +0.00 ... standing on that floor he is looking at 0.40 m of skybox under his own feet
    -- the level running out rather than carrying on." The fortress answers it with a cloud
    layer and the mine with more shaft; a temple answers it with more temple, so this is a
    slab of the same stone from y 0 down to y -3, the width of the arena.

    Renderer only: no SimCollider, so nothing is baked and the simulation is untouched.
    """
    if f"&{GROUND_BOX}" in text:
        print("  the stone under the temple is already there")
        return text

    docs = documents(text)

    # The group the floors hang off, so this hangs there too.
    father = None
    for fid, (cls, body) in sorted(docs.items()):
        if cls != 4:
            continue
        go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
        nm = re.search(r"m_Name: (.*)", docs[int(go.group(1))][1]) if go else None
        if nm and nm.group(1).strip() == "Cube (3)":
            father = int(re.search(r"m_Father: \{fileID: (-?\d+)\}", body).group(1))
            break
    if father is None:
        print("  no floor group to hang the stone off; left alone")
        return text

    go, tr, mf, mr = (GROUND_BOX, GROUND_BOX + 1, GROUND_BOX + 2, GROUND_BOX + 3)
    block = f"""--- !u!1 &{go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {tr}}}
  - component: {{fileID: {mf}}}
  - component: {{fileID: {mr}}}
  m_Layer: 0
  m_Name: Under the temple
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{tr}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  serializedVersion: 2
  m_LocalRotation: {{x: -0, y: -0, z: -0, w: 1}}
  m_LocalPosition: {{x: 0, y: -1.5, z: 0.6}}
  m_LocalScale: {{x: 34, y: 3, z: 8}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {father}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!33 &{mf}
MeshFilter:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Mesh: {{fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}}
--- !u!23 &{mr}
MeshRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Enabled: 1
  m_CastShadows: 1
  m_ReceiveShadows: 1
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RayTracingMode: 2
  m_RayTraceProcedural: 0
  m_RayTracingAccelStructBuildFlagsOverride: 0
  m_RayTracingAccelStructBuildFlags: 1
  m_SmallMeshCulling: 1
  m_ForceMeshLod: -1
  m_MeshLodSelectionBias: 0
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {{fileID: 2100000, guid: {GROUND_MAT}, type: 2}}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {{fileID: 0}}
  m_ProbeAnchor: {{fileID: 0}}
  m_LightProbeVolumeOverride: {{fileID: 0}}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 0
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 1
  m_SelectedEditorRenderState: 3
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {{fileID: 0}}
  m_GlobalIlluminationMeshLod: 0
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: 0
  m_MaskInteraction: 0
  m_AdditionalVertexStreams: {{fileID: 0}}
"""

    text = text.rstrip("\n") + "\n" + block

    # Into the floor group's children, or it is in the file and in no scene.
    body = documents(text)[father][1]
    fixed = (body.replace("m_Children: []", f"m_Children:\n  - {{fileID: {tr}}}")
             if "m_Children: []" in body
             else re.sub(r"(  m_Children:\n)", r"\g<1>" + f"  - {{fileID: {tr}}}\n",
                         body, count=1))
    text = text.replace(body, fixed)
    print("  stone under the temple: y 0 down to -3, 34 m across, no collider")
    return text


LIGHT_BASE = 1_920_300_000


def lamp_lights(text, want):
    """Exactly `want` lamp lights, by cloning or deleting whole objects.

    The transplant brought the mine's ten and the first pass deleted the surplus, which
    was the right direction then and the wrong one now: Dawid's level has seven things
    that burn and the scene has five lights. A light here is five documents -- the
    GameObject, its Transform, the Light, the TorchLight that makes it flicker and URP's
    own per-light data -- all five wired to each other by fileID, so the honest way to
    add one is to copy an existing one and renumber it rather than to write a Light from
    memory and hope the pipeline finds its data.
    """
    docs = documents(text)

    def name_of(go):
        m = re.search(r"m_Name: (.*)", docs[go][1]) if go in docs else None
        return m.group(1).strip() if m else "?"

    def collect(docs):
        out = []
        for fid, (cls, body) in sorted(docs.items()):
            if cls != 108:
                continue
            go = int(re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body).group(1))
            if not re.match(r"(Lantern|Fire Bowl) Light", name_of(go)):
                continue
            comps = [int(x) for x in
                     re.findall(r"- component: \{fileID: (-?\d+)\}", docs[go][1])]
            tr = next(x for x in comps if x in docs and docs[x][0] == 4)
            out.append((go, tr, comps))
        return out

    lights = collect(docs)
    if not lights:
        print("  no lamp light to copy from, so none are placed")
        return text, []

    # Surplus first, so the count is right whichever way it is wrong.
    for go, tr, comps in lights[want:]:
        drop = {go, tr} | set(comps)
        for fid in drop:
            if fid not in docs:
                continue
            cls, _body = docs[fid]
            head = f"--- !u!{cls} &{fid}\n"
            at = text.find(head)
            if at < 0:
                continue
            end = text.find("--- !u!", at + len(head))
            text = text[:at] + (text[end:] if end > 0 else "")
        text = re.sub(r"(?m)^  - \{fileID: (?:%s)\}\n" % "|".join(str(d) for d in drop),
                      "", text)
    if len(lights) > want:
        print(f"    {len(lights) - want} surplus light(s) removed")
        docs = documents(text)
        lights = collect(docs)

    # And copies for the flames that have none.
    template = lights[0]
    father = int(re.search(r"m_Father: \{fileID: (-?\d+)\}",
                           docs[template[1]][1]).group(1))
    made = 0
    while len(lights) < want:
        old = [template[0], template[1]] + [c for c in template[2] if c != template[1]]
        new = [LIGHT_BASE + made * 10 + i for i in range(len(old))]
        if any(f"&{n}" in text for n in new):
            print("    a cloned fileID is already in the scene; no lights added")
            break

        block = ""
        for fid in old:
            cls, body = docs[fid]
            block += f"--- !u!{cls} &{fid}\n{body}"
        for was, now in zip(old, new):
            block = block.replace(f"&{was}", f"&{now}")
            block = block.replace(f"fileID: {was}}}", f"fileID: {now}}}")
        text = text.rstrip("\n") + "\n" + block.rstrip("\n") + "\n"

        # Into the group the others hang off, or it is in the file and in no scene.
        body = documents(text)[father][1]
        fixed = (body.replace("m_Children: []", f"m_Children:\n  - {{fileID: {new[1]}}}")
                 if "m_Children: []" in body
                 else re.sub(r"(  m_Children:\n)", r"\g<1>" + f"  - {{fileID: {new[1]}}}\n",
                             body, count=1))
        text = text.replace(body, fixed)

        made += 1
        docs = documents(text)
        lights = collect(docs)
    if made:
        print(f"    {made} light(s) copied for the flames that had none")

    return text, lights[:want]


def main():
    grade()

    text = io.open(SCENE, encoding="utf-8").read()
    docs = documents(text)

    def name_of(go):
        m = re.search(r"m_Name: (.*)", docs[go][1]) if go in docs else None
        return m.group(1).strip() if m else "?"

    # ---------------------------------------------------------------- the grade, the sky
    was = text
    text = text.replace(f"sharedProfile: {{fileID: 11400000, guid: {MINE_GRADE_GUID}, type: 2}}",
                        f"sharedProfile: {{fileID: 11400000, guid: {GRADE_GUID}, type: 2}}")
    print("  grade volume re-pointed" if text != was else "  grade volume: nothing to point")

    text = re.sub(r"m_SkyboxMaterial: \{fileID: 2100000, guid: \w+, type: 2\}",
                  f"m_SkyboxMaterial: {{fileID: 2100000, guid: {NIGHT_SKY}, type: 2}}", text)
    print("  sky: FS000_Night_02_Moonless, the one his own level wears")

    # ---------------------------------------------------------------- the air
    # The COLOUR is night; the distances are not a matter of the hour. 14..46 m is
    # docs/08's rule measured against this arena's camera -- the fight plane is 6.96 m
    # out, so the haze begins seven metres behind the fighters and never on them -- and
    # light_check fails the scene if that moves.
    text = re.sub(r"m_FogColor: \{r: [\d.]+, g: [\d.]+, b: [\d.]+, a: [\d.]+\}",
                  "m_FogColor: {r: 0.06, g: 0.08, b: 0.14, a: 1}", text)
    text = re.sub(r"m_LinearFogStart: [\d.]+", "m_LinearFogStart: 14", text)
    text = re.sub(r"m_LinearFogEnd: [\d.]+", "m_LinearFogEnd: 46", text)
    # FLAT ambient, where every other arena takes its ambient from the sky -- because
    # this sky gives almost none. The cubemap's six faces measure 29 to 58 out of 255,
    # about 2.6% once it is linear, so skybox ambient here is near enough to nothing and
    # a fighter standing out of the moonlight is a silhouette. 0.08/0.10/0.16 linear is
    # roughly a quarter of what the mine gets from its overcast sky: enough to keep a
    # shape off a black floor, little enough to still be night. This is the one number
    # to turn if the arena plays too dark (up) or looks too flat (down).
    text = re.sub(r"m_AmbientMode: \d+", "m_AmbientMode: 3", text)
    text = re.sub(r"m_AmbientSkyColor: \{r: [\d.]+, g: [\d.]+, b: [\d.]+, a: [\d.]+\}",
                  "m_AmbientSkyColor: {r: 0.08, g: 0.1, b: 0.16, a: 1}", text)
    print("  fog is night blue, the same 14..46 m; ambient flat and cool, 0.08/0.10/0.16")

    # ---------------------------------------------------------------- the sun
    docs = documents(text)
    for fid, (cls, body) in sorted(docs.items()):
        if cls != 108:
            continue
        go = int(re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body).group(1))
        if "Directional" not in name_of(go):
            continue
        fixed = body
        fixed = re.sub(r"m_Color: \{r: [\d.]+, g: [\d.]+, b: [\d.]+, a: [\d.]+\}",
                       "m_Color: {r: 0.64, g: 0.74, b: 1, a: 1}", fixed, count=1)
        fixed = re.sub(r"m_Intensity: [\d.]+", "m_Intensity: 0.8", fixed, count=1)
        text = text.replace(body, fixed)

        # And where it comes from: higher and more to the side than a mine's.
        comps = [int(x) for x in
                 re.findall(r"- component: \{fileID: (-?\d+)\}", docs[go][1])]
        tr = next(x for x in comps if x in docs and docs[x][0] == 4)
        rx, ry = math.radians(62.0), math.radians(-28.0)
        qx, qy = math.sin(rx / 2), math.sin(ry / 2)
        cx, cy = math.cos(rx / 2), math.cos(ry / 2)
        q = (qx * cy, cx * qy, -qx * qy, cx * cy)      # Unity's YXZ order, Z = 0
        tbody = docs[tr][1]
        tfixed = re.sub(r"m_LocalRotation: \{x: [-\d.eE+]+, y: [-\d.eE+]+, "
                        r"z: [-\d.eE+]+, w: [-\d.eE+]+\}",
                        "m_LocalRotation: {x: %.7f, y: %.7f, z: %.7f, w: %.7f}" % q, tbody)
        tfixed = re.sub(r"m_LocalEulerAnglesHint: \{x: [-\d.eE+]+, y: [-\d.eE+]+, "
                        r"z: [-\d.eE+]+\}",
                        "m_LocalEulerAnglesHint: {x: 62, y: -28, z: 0}", tfixed)
        text = text.replace(tbody, tfixed)
        print("  moon: 0.8 at (0.64, 0.74, 1.00), from 62 degrees up and 28 round")
        break

    # ---------------------------------------------------------------- a light per flame
    props = flames()
    text, lights = lamp_lights(text, len(props))
    docs = documents(text)
    print(f"  {len(props)} flame(s) in the level, {len(lights)} light(s) for them")

    seen = {}
    for (go, tr, comps), prop in zip(lights, props):
        (x, y, z) = prop["pos"]
        at = y + prop["up"]

        # "Lantern Light" five times over is five things nobody can tell apart in the
        # hierarchy, so they are numbered the way Unity numbers a duplicate.
        n = seen.get(prop["name"], 0)
        seen[prop["name"]] = n + 1
        label = prop["name"] if n == 0 else f"{prop['name']} ({n})"

        body = docs[tr][1]
        fixed = re.sub(r"m_LocalPosition: \{x: [-\d.eE+]+, y: [-\d.eE+]+, "
                       r"z: [-\d.eE+]+\}",
                       "m_LocalPosition: {x: %.3f, y: %.3f, z: %.3f}" % (x, at, z), body)
        text = text.replace(body, fixed)

        # The light itself: fire colour, and the reach a flame that size has.
        cls, body = docs[next(c for c in comps if c in docs and docs[c][0] == 108)]
        fixed = re.sub(r"m_Color: \{r: [\d.]+, g: [\d.]+, b: [\d.]+, a: [\d.]+\}",
                       "m_Color: " + FIRE, body, count=1)
        fixed = re.sub(r"m_Intensity: [\d.]+", f"m_Intensity: {prop['intensity']}",
                       fixed, count=1)
        fixed = re.sub(r"m_Range: [\d.]+", f"m_Range: {prop['span']}", fixed, count=1)
        text = text.replace(body, fixed)

        # And the flicker on it, which carries its OWN copy of the intensity: a torch
        # whose TorchLight still says 1.6 fades back to 1.6 on the first frame, so the
        # number in the inspector and the number on the light have to move together.
        for c in comps:
            if c in docs and docs[c][0] == 114 and "TorchLight" in docs[c][1]:
                body = docs[c][1]
                fixed = re.sub(r"baseIntensity: [\d.]+",
                               f"baseIntensity: {prop['intensity']}", body)
                fixed = re.sub(r"swing: [\d.]+", f"swing: {prop['swing']}", fixed)
                text = text.replace(body, fixed)

        # The name says which prop it belongs to, because the next person to open this
        # scene will be looking for the lamp that is too bright.
        body = docs[go][1]
        text = text.replace(body, re.sub(r"m_Name: .*", f"m_Name: {label}", body, count=1))
        print(f"    {label:20s} -> {prop['who']:38s} x {x:+.2f} y {at:+.2f}  "
              f"{prop['intensity']} over {prop['span']} m")

    text = under_the_temple(text)

    io.open(SCENE, "w", encoding="utf-8").write(text)
    print("  wrote loc3_new.unity")


if __name__ == "__main__":
    sys.exit(main())

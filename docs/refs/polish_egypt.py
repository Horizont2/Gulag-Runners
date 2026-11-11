"""The look of Arena_Egypt: sun, sky, fog, grade, and a light in every lantern.

The transplant gave the Egyptian level the mine's machinery, and with it the mine's LIGHT:
a grey rainy sky, cold blue fog from ten metres out, a white-ish sun at 1.5, a grade that
pulls the white balance barely warm and the saturation DOWN, and ten point lights hanging
where the mine's lanterns were rather than where Dawid's are. Underground lighting in a
desert temple, with five of its lamps lighting nothing.

So, per the project's own idiom -- a shared Arena_PostProcess for the bloom and a grade per
arena beside it:

  Arena_Egypt_Grade   warm white balance, a touch more exposure and contrast, saturation
                      UP rather than down. The mine is a hole; this is noon on stone.
  the sun             warm, harder, higher: 2.1 at (1, 0.94, 0.80), euler (55, -35)
  the sky             FS002_Sunset, which is the one in the pack that reads as heat
  the fog             sand-coloured and pushed back to 14..46 m, because a mine's fog at
                      ten metres is a tunnel and this room is thirty-two metres across
  the lanterns        one point light per lantern Dawid placed, at the flame rather than
                      at the foot; the surplus lights deleted rather than left dark

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
SCENE = os.path.join(ASSETS, "Scenes", "Arena_Egypt.unity")
GRADE = os.path.join(ASSETS, "Settings", "Arena_Egypt_Grade.asset")

GRADE_GUID = "7c1b0d93a6f54c5f9d2e8ab41f0c37d2"
GROUND_MAT = "f7a4bc5a3db4b9042bbf990596a40780"   # CGM_EgyptPack/Material/LVL_Ground
MINE_GRADE_GUID = "50a3be9bad144fada0613db6dd2f653f"
SUNSET_SKY = "44d0573bf6d91e447b62f3aff840f2ae"       # FS002_Sunset

WHITE_BALANCE = "221518ef91623a7438a71fef23660601"
COLOR_ADJUSTMENTS = "66f335fb1ffd8684294ad653bf1c7564"
VOLUME_PROFILE = "d7fd9488000d3734a9e00ee676215985"

# A lantern's flame sits about half its own height up from the prop's own origin, which is
# where the mine's lights were offset to as well.
FLAME = 0.52


def documents(text):
    return {int(m.group(2)): (int(m.group(1)), m.group(4)) for m in
            re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                        text, re.S)}


def grade():
    """The arena's own colour grade, built the way Arena_Mine_Grade is."""
    if os.path.exists(GRADE):
        print("  Arena_Egypt_Grade.asset is already there")
        return

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
    m_Value: 22
  tint:
    m_OverrideState: 1
    m_Value: 6
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
    m_Value: 0.05
  contrast:
    m_OverrideState: 1
    m_Value: 6
  colorFilter:
    m_OverrideState: 1
    m_Value: {{r: 1, g: 0.98, b: 0.94, a: 1}}
  hueShift:
    m_OverrideState: 1
    m_Value: 0
  saturation:
    m_OverrideState: 1
    m_Value: 4
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
    print("  made Arena_Egypt_Grade.asset: warm balance, +6 contrast, saturation UP")


def lanterns():
    """Where Dawid's lanterns are, in world space."""
    scene = geometry.Scene(SCENE)
    out = []
    for pos, who in sorted(scene.prefab_art(), key=lambda r: r[0][0]):
        if re.search(r"lan?tern", who, re.I):
            out.append((pos, who))
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
                  f"m_SkyboxMaterial: {{fileID: 2100000, guid: {SUNSET_SKY}, type: 2}}", text)

    # ---------------------------------------------------------------- the air
    text = re.sub(r"m_FogColor: \{r: [\d.]+, g: [\d.]+, b: [\d.]+, a: [\d.]+\}",
                  "m_FogColor: {r: 0.85, g: 0.76, b: 0.6, a: 1}", text)
    text = re.sub(r"m_LinearFogStart: [\d.]+", "m_LinearFogStart: 14", text)
    text = re.sub(r"m_LinearFogEnd: [\d.]+", "m_LinearFogEnd: 46", text)
    text = re.sub(r"m_AmbientSkyColor: \{r: [\d.]+, g: [\d.]+, b: [\d.]+, a: [\d.]+\}",
                  "m_AmbientSkyColor: {r: 0.3, g: 0.26, b: 0.21, a: 1}", text)
    print("  fog is sand now, 14..46 m; ambient warm")

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
                       "m_Color: {r: 1, g: 0.94, b: 0.8, a: 1}", fixed, count=1)
        fixed = re.sub(r"m_Intensity: [\d.]+", "m_Intensity: 2.1", fixed, count=1)
        text = text.replace(body, fixed)

        # And where it comes from: higher and more to the side than a mine's.
        comps = [int(x) for x in
                 re.findall(r"- component: \{fileID: (-?\d+)\}", docs[go][1])]
        tr = next(x for x in comps if x in docs and docs[x][0] == 4)
        rx, ry = math.radians(55.0), math.radians(-35.0)
        qx, qy = math.sin(rx / 2), math.sin(ry / 2)
        cx, cy = math.cos(rx / 2), math.cos(ry / 2)
        q = (qx * cy, cx * qy, -qx * qy, cx * cy)      # Unity's YXZ order, Z = 0
        tbody = docs[tr][1]
        tfixed = re.sub(r"m_LocalRotation: \{x: [-\d.eE+]+, y: [-\d.eE+]+, "
                        r"z: [-\d.eE+]+, w: [-\d.eE+]+\}",
                        "m_LocalRotation: {x: %.7f, y: %.7f, z: %.7f, w: %.7f}" % q, tbody)
        tfixed = re.sub(r"m_LocalEulerAnglesHint: \{x: [-\d.eE+]+, y: [-\d.eE+]+, "
                        r"z: [-\d.eE+]+\}",
                        "m_LocalEulerAnglesHint: {x: 55, y: -35, z: 0}", tfixed)
        text = text.replace(tbody, tfixed)
        print("  sun: 2.1 at (1, 0.94, 0.80), from 55 degrees up and 35 round")
        break

    # ---------------------------------------------------------------- a light per lantern
    docs = documents(text)
    lights = []
    for fid, (cls, body) in sorted(docs.items()):
        if cls != 108:
            continue
        go = int(re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body).group(1))
        if name_of(go).startswith("Lantern Light"):
            comps = [int(x) for x in
                     re.findall(r"- component: \{fileID: (-?\d+)\}", docs[go][1])]
            tr = next(x for x in comps if x in docs and docs[x][0] == 4)
            lights.append((go, tr, comps))

    props = lanterns()
    print(f"  {len(props)} lantern(s) in the level, {len(lights)} light(s) to put in them")

    for i, (go, tr, comps) in enumerate(lights):
        if i < len(props):
            (x, y, z), who = props[i]
            body = docs[tr][1]
            fixed = re.sub(r"m_LocalPosition: \{x: [-\d.eE+]+, y: [-\d.eE+]+, "
                           r"z: [-\d.eE+]+\}",
                           "m_LocalPosition: {x: %.3f, y: %.3f, z: %.3f}"
                           % (x, y + FLAME, z), body)
            text = text.replace(body, fixed)
            print(f"    light -> {who} at x {x:+.2f} y {y + FLAME:+.2f}")
            continue

        # Surplus: a light with no lamp around it is a glow in mid-air, and ten of the
        # mine's lamps are five more than this level has.
        drop = {go, tr} | set(comps)
        for fid in drop:
            if fid not in docs:
                continue
            cls, body = docs[fid]
            head = f"--- !u!{cls} &{fid}\n"
            at = text.find(head)
            if at < 0:
                continue
            end = text.find("--- !u!", at + len(head))
            text = text[:at] + (text[end:] if end > 0 else "")
        text = re.sub(r"(?m)^  - \{fileID: (?:%s)\}\n" % "|".join(str(d) for d in drop),
                      "", text)

    dropped = max(0, len(lights) - len(props))
    if dropped:
        print(f"    {dropped} surplus light(s) removed")

    text = under_the_temple(text)

    io.open(SCENE, "w", encoding="utf-8").write(text)
    print("  wrote Arena_Egypt.unity")


if __name__ == "__main__":
    sys.exit(main())

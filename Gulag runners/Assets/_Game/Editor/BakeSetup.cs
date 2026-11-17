using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Gets a scene ready for Generate Lighting, which is the single largest thing that can
    /// be done to how this game looks -- and the one docs/06 asked for and never got.
    ///
    /// What is there now: between eleven and fourteen REALTIME lights in every arena, with
    /// no LightingData.asset beside any scene, on a pipeline set to shade additional lights
    /// PER PIXEL, eight to an object. So each arena pays the most expensive kind of light
    /// there is and gets none of what the cheap kind buys: no ambient occlusion in the
    /// corners, no bounce, no colour carried from a torch onto the rock beside it. That
    /// absence is most of what "low poly looks cheap" actually means, and docs/07 says so.
    ///
    /// THE PREREQUISITE NOBODY MENTIONS. A lightmap needs a second UV set, and exactly ONE
    /// model out of 1828 in this project has Generate Lightmap UVs ticked. Baking before
    /// that is not a worse bake, it is a garbage one: overlapping charts, light bleeding
    /// through walls, seams down the middle of flat faces. The saving grace is that the
    /// arenas only instance 140 distinct models between them -- eight per cent of the
    /// project -- so this turns it on for those and leaves the other 1688 alone. Even so it
    /// is a long reimport and it asks first.
    ///
    /// WHAT THE LIGHTS BECOME. Everything local -- torches, lanterns, braziers -- goes to
    /// Baked. They light the arena, which never moves, and baked is free. The fighters then
    /// get their light from a LIGHT PROBE GRID this also builds, which samples the same bake
    /// and follows them about; without probes a baked arena leaves every moving thing flat
    /// and unlit, which is the usual reason people give up on baking and go back to realtime.
    /// One directional light stays Mixed so bodies still have a direction and an edge.
    ///
    /// This tool does NOT bake. Baking is minutes to hours and belongs to whoever is sitting
    /// in front of Unity: Window > Rendering > Lighting > Generate Lighting.
    /// </summary>
    public static class BakeSetup
    {
        const string ProbesName = "Light probes";
        const string SettingsPath = "Assets/Settings/GR_Bake.lighting";

        // Metres between probes. The arenas are about 31 x 9, so 2.5 gives a 13 x 5 grid:
        // enough for the light on a fighter to change as they cross a torch, few enough that
        // the bake stays quick and the data stays small.
        const float ProbeSpacing = 2.5f;

        [MenuItem("Tools/Gulag Runners/Get this scene ready to bake")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            SideViewCamera rig = Object.FindFirstObjectByType<SideViewCamera>(
                FindObjectsInactive.Include);
            if (rig == null)
            {
                Debug.LogWarning($"{scene.name} has no SideViewCamera, so there is no way to "
                                 + "tell where the arena is to put probes in it.");
                return;
            }

            List<string> needUVs = ModelsWithoutLightmapUVs(scene);

            if (!EditorUtility.DisplayDialog(
                    "Get this scene ready to bake?",
                    $"In {scene.name} this will:\n\n"
                    + "  • mark the static scenery as contributing to GI\n"
                    + "  • turn the local lights from Realtime to Baked, and leave one\n"
                    + "    directional light Mixed so fighters keep a direction\n"
                    + $"  • build a {ProbeSpacing} m light probe grid across the arena, which\n"
                    + "    is what lights the fighters once the arena is baked\n"
                    + "  • set the lightmap settings for mobile\n\n"
                    + (needUVs.Count > 0
                        ? $"  • TICK GENERATE LIGHTMAP UVS on {needUVs.Count} model(s) and\n"
                          + "    reimport them. This is the slow part, and without it a bake\n"
                          + "    produces garbage — overlapping charts and light through walls.\n\n"
                        : "  • every model it uses already has lightmap UVs.\n\n")
                    + "It does NOT bake. That is Window > Rendering > Lighting afterwards.",
                    "Set it up", "Cancel"))
                return;

            int uvs = EnableLightmapUVs(needUVs);
            int statics = MarkStatic(scene);
            (int baked, int mixed) = SettleLights();
            int probes = BuildProbes(scene, rig);
            Configure(scene);

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"{scene.name} is ready to bake.\n"
                      + $"  {statics} renderer(s) now contribute to GI\n"
                      + $"  {baked} light(s) baked, {mixed} left mixed\n"
                      + $"  {probes} light probe(s) on a {ProbeSpacing} m grid\n"
                      + $"  {uvs} model(s) had lightmap UVs turned on\n\n"
                      + "Now: Window > Rendering > Lighting > Generate Lighting. Expect "
                      + "minutes, and expect the first look to be darker — that is the "
                      + "occlusion that was never there before.");
        }

        /// <summary>
        /// Everything the scene draws that is not a fighter, a loot item, a particle or one
        /// of our own backdrop quads.
        ///
        /// A SkinnedMeshRenderer is never static: it is a body, and a body that got baked
        /// into the lightmap would leave its own shadow standing in the arena after it
        /// walked away.
        /// </summary>
        static int MarkStatic(Scene scene)
        {
            int touched = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // Our own generated things light themselves and must stay out of the bake.
                if (root.name == "Backdrop" || root.name == "Atmosphere"
                    || root.name == "Ground shadows") continue;

                foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.GetComponentInParent<PlayerController>() != null) continue;
                    if (r.GetComponent<ParticleSystem>() != null) continue;

                    StaticEditorFlags had = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    StaticEditorFlags want = had | StaticEditorFlags.ContributeGI
                                                 | StaticEditorFlags.BatchingStatic;
                    if (had == want) continue;

                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, want);
                    touched++;
                }
            }
            return touched;
        }

        /// <summary>
        /// Realtime to Baked for everything local; one directional light stays Mixed.
        ///
        /// Mixed on the key light is what keeps a fighter from looking like a cardboard
        /// cut-out: it still lights and shadows moving things in real time, while its
        /// indirect bounce comes out of the bake. Everything else -- the torches and
        /// lanterns, which is the bulk of the count -- becomes free.
        /// </summary>
        static (int, int) SettleLights()
        {
            int baked = 0, mixed = 0;
            Light key = null;

            // INCLUDING the inactive ones. A light on a switched-off object is still a light
            // the moment anything switches it on, and the default search skips them --
            // which left two point lights in Arena_Forest realtime through a whole bake,
            // paying per-pixel for a scene that is otherwise entirely baked.
            Light[] lights = Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            // The brightest directional light is the key, if there is one at all.
            foreach (Light l in lights)
                if (l.type == LightType.Directional && (key == null || l.intensity > key.intensity))
                    key = l;

            foreach (Light l in lights)
            {
                Undo.RecordObject(l, "Get this scene ready to bake");
                if (l == key)
                {
                    l.lightmapBakeType = LightmapBakeType.Mixed;
                    mixed++;
                }
                else
                {
                    l.lightmapBakeType = LightmapBakeType.Baked;

                    // A baked light's shadows come out of the lightmap, so a realtime
                    // shadow setting on it is pure cost for a picture that is already drawn.
                    l.shadows = LightShadows.None;
                    baked++;
                }
                EditorUtility.SetDirty(l);
            }
            return (baked, mixed);
        }

        /// <summary>
        /// A grid of probes through the playable volume.
        ///
        /// This is the half that gets forgotten, and forgetting it is why a first bake so
        /// often looks worse than no bake: the arena gets its occlusion and its bounce, and
        /// every moving thing in it -- both fighters, every loot item, every chest lid --
        /// goes flat, because dynamic objects do not read the lightmap. They read probes.
        /// </summary>
        static int BuildProbes(Scene scene, SideViewCamera rig)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == ProbesName) Undo.DestroyObjectImmediate(root);

            GameObject go = new GameObject(ProbesName);
            Undo.RegisterCreatedObjectUndo(go, "Get this scene ready to bake");
            LightProbeGroup group = go.AddComponent<LightProbeGroup>();

            Vector2 min = rig.bordersMin, max = rig.bordersMax;
            List<Vector3> spots = new List<Vector3>();

            int across = Mathf.Max(2, Mathf.CeilToInt((max.x - min.x) / ProbeSpacing) + 1);
            int up = Mathf.Max(2, Mathf.CeilToInt((max.y - min.y) / ProbeSpacing) + 1);

            // Two sheets, one either side of the fight plane. A single sheet exactly on the
            // plane cannot tell the engine how the light changes through the depth the
            // fighters actually move in, and the arenas are 2.5D, not 2D.
            for (int z = -1; z <= 1; z += 2)
            {
                for (int i = 0; i < across; i++)
                {
                    float x = Mathf.Lerp(min.x, max.x, i / (float)(across - 1));
                    for (int j = 0; j < up; j++)
                    {
                        // A little above the floor at the bottom row: a probe buried in the
                        // ground samples the inside of a solid and comes back black.
                        float y = Mathf.Lerp(min.y + 0.4f, max.y, j / (float)(up - 1));
                        spots.Add(new Vector3(x, y, rig.planeZ + z * 1.2f));
                    }
                }
            }

            group.probePositions = spots.ToArray();
            return spots.Count;
        }

        /// <summary>Lightmap settings sized for a phone, not for a screenshot.</summary>
        static void Configure(Scene scene)
        {
            LightingSettings settings =
                AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);

            if (settings == null)
            {
                settings = new LightingSettings { name = "GR_Bake" };
                if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                    AssetDatabase.CreateFolder("Assets", "Settings");
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;        // realtime GI on mobile is not a thing

            // Ten texels to the metre. A 31 m arena is then about 310 texels across, which
            // on flat stylised colour is plenty: there is no detail in these surfaces for a
            // finer map to resolve, only bake time and memory.
            settings.lightmapResolution = 10f;
            settings.lightmapMaxSize = 1024;
            settings.lightmapPadding = 2;
            settings.compressLightmaps = true;

            // Non-directional halves the lightmap memory and costs nothing a flat-shaded
            // arena can show. docs/06 caps textures at 120 MB for the whole game.
            settings.directionalityMode = LightmapsMode.NonDirectional;

            // The reason for doing any of this. Occlusion in the corners is what stops low
            // poly reading as cardboard, and it is the part realtime point lights cannot do.
            settings.ao = true;
            settings.aoMaxDistance = 1.4f;
            settings.aoExponentDirect = 1f;
            settings.aoExponentIndirect = 1.2f;

            settings.maxBounces = 2;
            settings.indirectResolution = 2f;
            settings.mixedBakeMode = MixedLightingMode.Subtractive;

            EditorUtility.SetDirty(settings);
            Lightmapping.SetLightingSettingsForScene(scene, settings);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Every model this scene draws that has no second UV set yet.</summary>
        static List<string> ModelsWithoutLightmapUVs(Scene scene)
        {
            HashSet<string> paths = new HashSet<string>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh == null) continue;
                    string path = AssetDatabase.GetAssetPath(f.sharedMesh);
                    if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
                    paths.Add(path);
                }
            }

            List<string> need = new List<string>();
            foreach (string path in paths)
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter model
                    && !model.generateSecondaryUV)
                    need.Add(path);
            }
            return need;
        }

        static int EnableLightmapUVs(List<string> paths)
        {
            if (paths.Count == 0) return 0;

            int done = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < paths.Count; i++)
                {
                    EditorUtility.DisplayProgressBar(
                        "Generating lightmap UVs", paths[i], (i + 0.5f) / paths.Count);

                    if (AssetImporter.GetAtPath(paths[i]) is not ModelImporter model) continue;
                    model.generateSecondaryUV = true;
                    model.SaveAndReimport();
                    done++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }
            return done;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the parallax backdrop behind an arena: three flat silhouette layers, each at
    /// its own depth, in that arena's own palette.
    ///
    /// THE WHOLE TRICK IS THAT THERE IS NO TRICK. The arena cameras are perspective -- fov
    /// 30, framing ten metres of world height, which puts them about 18.6 m from the fight.
    /// A thing 58 m away therefore slides across the screen at 18.6/58 = 32% of the speed
    /// the fighters do, and a thing 310 m away at 6%, purely because that is what a
    /// perspective projection does. So there is no scrolling script here, no second camera,
    /// no render texture and nothing in Update. Three quads are parked in the world at three
    /// depths and the projection does the rest -- including, for free, the parallax when the
    /// camera zooms, which a scrolling script would have had to be told about.
    ///
    /// The layers are CUTOUT, not alpha-blended. That is not a performance nicety, it is the
    /// difference between working and not: blended geometry draws after the opaque pass, so
    /// a blended backdrop would be painted on top of the arena and the fighters. As cutout
    /// they are ordinary opaque geometry sitting 58 to 310 metres away, which the level
    /// occludes by depth like anything else. docs/00 wants flat colour and hard edges
    /// anyway, so nothing is lost by having no soft alpha to spend.
    ///
    /// The art, the depths and every size here come from docs/refs/make_backdrops.py, which
    /// writes layers.json beside the textures. Nothing in this file re-derives them: when
    /// those figures lived in two places they disagreed within the hour, and the result was
    /// art stretched to nearly three times its height.
    /// </summary>
    public static class BackdropBuilder
    {
        const string BackdropsDir = "Assets/_Game/Effects/Backdrops";
        const string MaterialsDir = "Assets/_Game/Effects/Materials";
        const string RootName = "Backdrop";
        const string BackdropShader = "Gulag Runners / Backdrop";

        // Which palette belongs to which arena. A scene that is not in here has no backdrop
        // of its own yet, and is left alone rather than given somebody else's weather.
        static readonly Dictionary<string, string> Themes = new Dictionary<string, string>
        {
            { "Arena_Mine", "mine" },
            { "Arena_Forest", "forest" },
            { "Arena_Nordic fortress", "nordic" },
            { "loc3_new", "temple" },
        };

        [Serializable]
        class Layer
        {
            public string name;
            public float depth;
            public float drift;
            public float metres_wide;
            public float metres_high;
        }

        [Serializable]
        class Table
        {
            public Cam camera;
            public Layer[] layers;
        }

        [Serializable]
        class Cam
        {
            public float fov;
            public float pitch;
            public float view_height;
            public float rest_distance;
        }

        [MenuItem("Tools/Gulag Runners/Build the parallax backdrop")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (!Themes.TryGetValue(scene.name, out string theme))
            {
                Debug.LogWarning($"No backdrop theme is defined for the scene \"{scene.name}\". "
                                 + "The themes are: " + string.Join(", ", Themes.Keys)
                                 + ". Add one to BackdropBuilder.Themes and give "
                                 + "make_backdrops.py a palette for it.");
                return;
            }

            Table table = ReadTable();
            if (table == null) return;

            SideViewCamera rig = UnityEngine.Object.FindFirstObjectByType<SideViewCamera>();
            if (rig == null)
            {
                Debug.LogWarning($"{scene.name} has no SideViewCamera, so there is nothing to "
                                 + "put a backdrop behind.");
                return;
            }

            // Where the camera sits when it is looking at the middle of the arena. The quads
            // are nailed to the world at that axis; everything the camera does afterwards --
            // following, leading, zooming, shaking -- is what MAKES the parallax, so none of
            // it belongs in the placement.
            Vector2 middle = (rig.bordersMin + rig.bordersMax) * 0.5f;
            Vector3 focus = new Vector3(middle.x, middle.y, rig.planeZ);
            Quaternion facing = Quaternion.Euler(rig.pitchDegrees, 0f, 0f);
            Vector3 forward = facing * Vector3.forward;

            // EffectiveViewHeight, not visibleWorldHeight: the latter is only the framing
            // when manualFraming is on. Otherwise the rig derives the view from the
            // fighter's own height and the fraction of the screen they must fill, and
            // reading the inspector field would quietly give the wrong distance.
            float framing = rig.EffectiveViewHeight;
            float rest = framing * 0.5f
                         * Mathf.Cos(Mathf.Deg2Rad * rig.pitchDegrees)
                         / Mathf.Tan(Mathf.Deg2Rad * rig.fieldOfView * 0.5f);

            // The table was computed for one particular framing. If a scene has been retuned
            // since, every depth and every quad size in it is wrong, and wrong quietly: the
            // backdrop would still appear, just drifting at the wrong rate and not quite
            // covering the frame. Say so rather than build it.
            if (Mathf.Abs(rest - table.camera.rest_distance) > 0.5f)
            {
                Debug.LogWarning(
                    $"{scene.name} frames {framing:0.00} m at fov {rig.fieldOfView} "
                    + $"and {rig.pitchDegrees} degrees of pitch, which puts the camera {rest:0.00} m "
                    + $"from the fight. The backdrop was built for {table.camera.rest_distance:0.0} m. "
                    + "Update the measured numbers at the top of docs/refs/make_backdrops.py, "
                    + "re-run it, and build again -- otherwise the layers drift at the wrong "
                    + "rate and leave an edge showing.");
                return;
            }

            Transform old = FindRoot(scene);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build the parallax backdrop");
            root.transform.SetPositionAndRotation(focus, Quaternion.identity);

            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (quad == null)
            {
                GameObject borrow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad = borrow.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(borrow);
            }
            List<string> built = new List<string>();

            foreach (Layer layer in table.layers)
            {
                string texture = $"{BackdropsDir}/bg_{theme}_{layer.name}.png";
                Texture2D art = AssetDatabase.LoadAssetAtPath<Texture2D>(texture);
                if (art == null)
                {
                    Debug.LogWarning($"{texture} is missing. Run docs/refs/make_backdrops.py.");
                    continue;
                }

                Material skin = MaterialFor(theme, layer.name, art);
                if (skin == null) return;   // it has already said why

                GameObject go = new GameObject($"Backdrop_{layer.name}");
                go.transform.SetParent(root.transform, false);

                // Down the camera's own axis, so the quad faces it square on even though the
                // camera is pitched. Parallel to the world instead and the far layer would
                // show its top edge over the horizon.
                go.transform.SetPositionAndRotation(
                    focus + forward * (layer.depth - rest), facing);
                go.transform.localScale =
                    new Vector3(layer.metres_wide, layer.metres_high, 1f);

                go.AddComponent<MeshFilter>().sharedMesh = quad;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = skin;

                // Scenery this far away has no business in any lighting calculation, and a
                // 431-metre quad in a shadow cascade would wreck the near shadows.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

                // Batching only. A 431-metre quad is emphatically not an occluder: flagged
                // as one it would be baked as a wall standing across the whole level.
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

                built.Add($"{layer.name} at {layer.depth:0} m ({layer.drift * 100f:0}%)");
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"Backdrop built in {scene.name} ({theme}): {string.Join(", ", built)}. "
                      + "The layers are static and have no script on them -- the drift is the "
                      + "perspective camera, not code.");
        }

        static Transform FindRoot(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == RootName) return go.transform;
            return null;
        }

        static Table ReadTable()
        {
            string path = $"{BackdropsDir}/layers.json";
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (json == null)
            {
                Debug.LogWarning($"{path} is missing. It is written by "
                                 + "docs/refs/make_backdrops.py, along with the artwork.");
                return null;
            }

            Table table = JsonUtility.FromJson<Table>(json.text);
            if (table == null || table.layers == null || table.layers.Length == 0)
            {
                Debug.LogWarning($"{path} has no layers in it.");
                return null;
            }
            return table;
        }

        /// <summary>
        /// One unlit cutout material per layer, made once and reused by every scene that
        /// shares the theme.
        /// </summary>
        static Material MaterialFor(string theme, string layer, Texture2D art)
        {
            string path = $"{MaterialsDir}/GR_Backdrop_{theme}_{layer}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                // The project's own shader, and it has to be this one. Every arena runs
                // linear fog that is fully opaque by 34-46 m, and two of the three layers
                // sit beyond that -- under any stock shader they come out as 100% fog
                // colour and the whole backdrop is one flat wall. URP's Unlit takes its fog
                // keyword from the scene globally, so there is no material switch for it;
                // the only way out is a shader that never applies fog. See Backdrop.shader.
                Shader shader = Shader.Find(BackdropShader);
                if (shader == null)
                {
                    Debug.LogWarning(
                        $"The shader \"{BackdropShader}\" is missing, so the layers would be "
                        + "built on a stock unlit one and the scene's fog would swallow "
                        + "everything past about 40 m -- the middle and far layers entirely. "
                        + "Restore Assets/_Game/Shaders/Backdrop.shader.");
                    return null;
                }
                mat = new Material(shader);

                if (!AssetDatabase.IsValidFolder(MaterialsDir))
                {
                    Directory.CreateDirectory(MaterialsDir);
                    AssetDatabase.Refresh();
                }
                AssetDatabase.CreateAsset(mat, path);
            }

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", art);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", art);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

            // Opaque with alpha clipping -- NOT transparent. See the note at the top: a
            // transparent backdrop draws after the opaque pass and lands in front of the
            // fight. URP re-derives its blend state from _Surface when the material is
            // validated, so setting _SrcBlend by hand here would simply be overwritten.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;

            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}

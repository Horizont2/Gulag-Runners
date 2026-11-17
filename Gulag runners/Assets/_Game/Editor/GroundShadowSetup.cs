using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts a blob shadow under every fighter in the open scene.
    ///
    /// docs/06 specified two kinds of shadow and the project shipped neither: "нуль
    /// real-time тіней (тіні — запечені + один blob під персонажем)". This is the blob half.
    /// A fighter with nothing underfoot does not look like they are standing on the arena,
    /// they look pasted in front of it -- and that got worse the day a detailed parallax
    /// backdrop went in behind them.
    ///
    /// The quads are NOT parented to the fighters. A fighter's root is scaled to 0.5 and
    /// turns to face the way they are walking, and a thing lying on the floor should inherit
    /// neither; under that parent a localScale of 0.6 would quietly mean 0.3. So they live
    /// at the scene root under one holder with an identity transform, and GroundShadow
    /// places them in world space each frame.
    /// </summary>
    public static class GroundShadowSetup
    {
        const string TexturePath = "Assets/_Game/Effects/Textures/vfx_shadow.png";
        const string MaterialsDir = "Assets/_Game/Effects/Materials";
        const string MaterialPath = MaterialsDir + "/GR_GroundShadow.mat";
        const string HolderName = "Ground shadows";

        [MenuItem("Tools/Gulag Runners/Build the ground shadows")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            PlayerController[] fighters =
                Object.FindObjectsByType<PlayerController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (fighters.Length == 0)
            {
                Debug.LogWarning($"{scene.name} has no fighters in it, so there is nothing "
                                 + "to put a shadow under.");
                return;
            }

            Material ink = ShadowMaterial();
            if (ink == null) return;

            Transform holder = Holder(scene);
            Mesh quadMesh = Quad();

            List<string> done = new List<string>();

            foreach (PlayerController fighter in fighters)
            {
                GroundShadow shadow = fighter.GetComponent<GroundShadow>();
                if (shadow == null)
                {
                    shadow = Undo.AddComponent<GroundShadow>(fighter.gameObject);
                    shadow.player = fighter;
                }

                // Named after its owner so two of them are tellable apart in the Hierarchy,
                // and so a re-run finds the one it made last time instead of adding another.
                string name = $"Shadow — {fighter.name}";
                Transform quad = holder.Find(name);
                if (quad == null)
                {
                    GameObject go = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(go, "Build the ground shadows");
                    go.transform.SetParent(holder, false);
                    quad = go.transform;
                }

                MeshFilter filter = quad.GetComponent<MeshFilter>()
                                    ?? Undo.AddComponent<MeshFilter>(quad.gameObject);
                filter.sharedMesh = quadMesh;

                MeshRenderer renderer = quad.GetComponent<MeshRenderer>()
                                        ?? Undo.AddComponent<MeshRenderer>(quad.gameObject);
                renderer.sharedMaterial = ink;

                // It is a shadow. It must not cast one, receive one, or ask a light probe
                // anything -- all three would be work done to light a dark patch on a floor.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                shadow.player = fighter;
                shadow.quad = quad;
                shadow.quadRenderer = renderer;

                EditorUtility.SetDirty(shadow);
                done.Add(fighter.name);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Ground shadows built in {scene.name} for {done.Count} fighter(s): "
                      + string.Join(", ", done) + ". They read the floor out of the sim, not "
                      + "out of Unity physics, so they are right on one-way platforms too.");
        }

        static Transform Holder(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == HolderName) return root.transform;

            GameObject made = new GameObject(HolderName);
            Undo.RegisterCreatedObjectUndo(made, "Build the ground shadows");
            return made.transform;
        }

        static Mesh Quad()
        {
            Mesh mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (mesh != null) return mesh;

            GameObject borrow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mesh = borrow.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(borrow);
            return mesh;
        }

        /// <summary>One material for every shadow in the project; the colour comes per
        /// fighter from a MaterialPropertyBlock, so they still share one draw state.</summary>
        static Material ShadowMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;

            Texture2D art = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (art == null)
            {
                Debug.LogWarning($"{TexturePath} is missing. Run "
                                 + "docs/refs/make_vfx_textures.py, which draws it.");
                return null;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("The URP Unlit shader is missing, so the shadows cannot be "
                                 + "built.");
                return null;
            }

            mat = new Material(shader);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", art);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", art);

            // Transparent and alpha-blended, which for once is right: a shadow IS a partial
            // darkening of what is behind it, so there is nothing to cut out. It writes no
            // depth, because a flat quad a centimetre over the floor that wrote depth would
            // fight every decal and effect that lands on the same spot.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);       // Alpha
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");

            if (!AssetDatabase.IsValidFolder(MaterialsDir))
            {
                Directory.CreateDirectory(MaterialsDir);
                AssetDatabase.Refresh();
            }
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}

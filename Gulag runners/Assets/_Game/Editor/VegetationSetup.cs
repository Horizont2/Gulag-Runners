using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the grass and the palms of the temple arena into the wind.
    ///
    /// THE REASON THIS IS A TOOL AND NOT A MATERIAL EDIT. Every plant in the Egypt pack --
    /// grass, palms, cacti, flowers, the pot on the balcony -- shares ONE material,
    /// LVL_Vegetation. Putting a wind shader on it would set the cacti swaying, and a
    /// swaying cactus is worse than a still one. So this makes two new materials off the
    /// same texture, leaves LVL_Vegetation alone for everything else, and sorts the
    /// renderers by what they are.
    ///
    /// IT MEASURES RATHER THAN ASSUMES. Both shaders need to know how tall the plant is, and
    /// the palm also needs to know how far a frond reaches, because that is how it tells a
    /// frond from the trunk with no vertex colour to go on. Those are read off the actual
    /// meshes in the scene -- the tallest grass, the tallest palm, the widest crown -- not
    /// typed into a property and hoped over. A _Height that is too small makes the bend
    /// happen halfway up a blade; too large and nothing moves at all.
    ///
    /// AND IT TAKES THEM OUT OF THE LIGHTMAP. BakeSetup marks every MeshRenderer as
    /// contributing to GI, which is right for a wall and wrong for a leaf: a vertex that
    /// moves cannot carry a baked shadow that does not, so the plant would animate while its
    /// own shading stayed welded to where it used to be. These read their ambient from the
    /// probe grid instead, which is the half of the bake that exists for things that move.
    /// </summary>
    public static class VegetationSetup
    {
        const string MaterialsDir = "Assets/_Game/Effects/Materials";
        const string GrassMat = MaterialsDir + "/GR_Egypt_Grass.mat";
        const string PalmMat = MaterialsDir + "/GR_Egypt_Palm.mat";
        const string GrassShader = "Gulag Runners/Egypt Grass";
        const string PalmShader = "Gulag Runners/Egypt Palm";

        // What counts as which, by the pack's own naming. Anything matching neither is left
        // exactly as it was -- cacti and flowers included, on purpose.
        static readonly string[] GrassNames = { "grass" };
        static readonly string[] PalmNames = { "palm" };

        [MenuItem("Tools/Gulag Runners/Put the vegetation in the wind")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            List<MeshRenderer> grass = new List<MeshRenderer>();
            List<MeshRenderer> palms = new List<MeshRenderer>();

            foreach (MeshRenderer r in
                     Object.FindObjectsByType<MeshRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;

                // The MESH name, not the GameObject's: a scene renames objects, an imported
                // mesh keeps the name the pack gave it. CGM_ENV_VEG_palm4, grass5, cactus21.
                string name = f.sharedMesh.name.ToLowerInvariant();
                if (Matches(name, GrassNames)) grass.Add(r);
                else if (Matches(name, PalmNames)) palms.Add(r);
            }

            if (grass.Count == 0 && palms.Count == 0)
            {
                Debug.LogWarning($"{scene.name} has no grass or palm meshes in it. This is "
                                 + "for the temple arena; the pack names them "
                                 + "CGM_ENV_VEG_grass* and CGM_ENV_VEG_palm*.");
                return;
            }

            Texture leafy = VegetationTexture(grass, palms);

            int dressed = 0, unbaked = 0;

            if (grass.Count > 0)
            {
                Material mat = Make(GrassMat, GrassShader, leafy);
                if (mat == null) return;
                mat.SetFloat("_Height", Tallest(grass));
                EditorUtility.SetDirty(mat);
                dressed += Wear(grass, mat, ref unbaked);
            }

            if (palms.Count > 0)
            {
                Material mat = Make(PalmMat, PalmShader, leafy);
                if (mat == null) return;
                float high = Tallest(palms);
                mat.SetFloat("_Height", high);
                mat.SetFloat("_FrondReach", Reach(palms, high));
                EditorUtility.SetDirty(mat);
                dressed += Wear(palms, mat, ref unbaked);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"The vegetation of {scene.name} is in the wind: {grass.Count} patch(es) of "
                + $"grass and {palms.Count} palm(s), {dressed} renderer(s) in all.\n"
                + $"  grass height {(grass.Count > 0 ? Tallest(grass) : 0f):0.00} m\n"
                + (palms.Count > 0
                    ? $"  palm height {Tallest(palms):0.00} m, fronds reach "
                      + $"{Reach(palms, Tallest(palms)):0.00} m\n"
                    : "")
                + $"  {unbaked} of them taken out of the lightmap -- a vertex that moves "
                + "cannot carry a baked shadow that does not, so they light off the probe "
                + "grid instead.\n"
                + "  Cacti and flowers share the pack's material and were left still.");
        }

        static bool Matches(string name, string[] words)
        {
            foreach (string w in words)
                if (name.Contains(w)) return true;
            return false;
        }

        /// <summary>The albedo the pack already uses, so nothing has to be re-authored.</summary>
        static Texture VegetationTexture(List<MeshRenderer> a, List<MeshRenderer> b)
        {
            foreach (List<MeshRenderer> set in new[] { a, b })
                foreach (MeshRenderer r in set)
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                            return m.GetTexture("_BaseMap");
                        if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null)
                            return m.GetTexture("_MainTex");
                    }
            return null;
        }

        static Material Make(string path, string shaderName, Texture art)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"The shader \"{shaderName}\" is missing, so the "
                                     + "vegetation cannot be dressed. Restore "
                                     + "Assets/_Game/Shaders.");
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

            if (art != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", art);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", art);
            }
            return mat;
        }

        /// <summary>The tallest of these meshes, in world metres.</summary>
        static float Tallest(List<MeshRenderer> set)
        {
            float high = 0f;
            foreach (MeshRenderer r in set)
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;

                // Object space, because the shader works there: the mesh's own size times
                // the scale somebody gave the instance.
                high = Mathf.Max(high, f.sharedMesh.bounds.size.y);
            }
            return high > 0.01f ? high : 1f;
        }

        /// <summary>
        /// How far a frond reaches from the trunk: the widest horizontal spread found in
        /// the TOP half of a palm, which is the crown and nothing else.
        ///
        /// Measured rather than guessed because this number is the shader's only way of
        /// telling a frond from the mast. Too small and the trunk flaps; too large and the
        /// crown sits dead still.
        /// </summary>
        static float Reach(List<MeshRenderer> set, float height)
        {
            float reach = 0f;
            foreach (MeshRenderer r in set)
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;

                Vector3[] points = f.sharedMesh.vertices;
                foreach (Vector3 p in points)
                {
                    if (p.y < height * 0.5f) continue;
                    reach = Mathf.Max(reach, new Vector2(p.x, p.z).magnitude);
                }
            }

            // A mesh with Read/Write off gives nothing, so fall back to the bounds: half
            // the crown's own width is a fair reading of how far a frond gets out.
            if (reach <= 0.01f)
            {
                foreach (MeshRenderer r in set)
                {
                    MeshFilter f = r.GetComponent<MeshFilter>();
                    if (f == null || f.sharedMesh == null) continue;
                    Vector3 size = f.sharedMesh.bounds.size;
                    reach = Mathf.Max(reach, Mathf.Max(size.x, size.z) * 0.5f);
                }
            }
            return reach > 0.01f ? reach : 1f;
        }

        static int Wear(List<MeshRenderer> set, Material mat, ref int unbaked)
        {
            int n = 0;
            foreach (MeshRenderer r in set)
            {
                Undo.RecordObject(r, "Put the vegetation in the wind");

                Material[] slots = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = mat;
                r.sharedMaterials = slots;

                StaticEditorFlags had = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                if ((had & StaticEditorFlags.ContributeGI) != 0)
                {
                    GameObjectUtility.SetStaticEditorFlags(
                        r.gameObject, had & ~StaticEditorFlags.ContributeGI);
                    unbaked++;
                }

                // And batching static is wrong too: a batched mesh is baked into one combined
                // mesh at its authored transform, which is the opposite of per-plant motion.
                had = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                if ((had & StaticEditorFlags.BatchingStatic) != 0)
                    GameObjectUtility.SetStaticEditorFlags(
                        r.gameObject, had & ~StaticEditorFlags.BatchingStatic);

                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                EditorUtility.SetDirty(r);
                n++;
            }
            return n;
        }
    }
}

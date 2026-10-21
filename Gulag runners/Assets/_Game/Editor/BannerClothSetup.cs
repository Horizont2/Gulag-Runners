using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the cloth shader on the banners and measures its rig off the mesh.
    ///
    /// The shader needs to know which way the cloth hangs, where it is nailed and how
    /// long the drop is, and none of that can be guessed from inside a shader. It also
    /// cannot be read off the source file: this banner came out of Blender, and whether
    /// Unity baked the Z-up to Y-up conversion into the vertices or left it as a rotation
    /// on the transform depends on an import setting — so the FBX says one thing and the
    /// Mesh in memory may say another.
    ///
    /// So it is measured, once, from the Mesh that is actually there. The longest axis of
    /// the bounds is the drop, the next longest is the width, and the anchor is whichever
    /// end of the drop sits closer to the object's own origin — because that is where a
    /// banner is nailed and where a pivot is put.
    ///
    /// Re-run it after changing the mesh. It logs every number it wrote, so a banner that
    /// comes out swinging sideways can be corrected by reading the log rather than by
    /// guessing at the material.
    /// </summary>
    public static class BannerClothSetup
    {
        const string ShaderName = "Gulag Runners/Banner Cloth";
        const string MaterialFolder = "Assets/_Game/Materials";

        [MenuItem("Tools/Gulag Runners/Set up banner cloth")]
        public static void Setup()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"No shader named \"{ShaderName}\". Has it imported yet?");
                return;
            }

            List<MeshRenderer> banners = Targets();
            if (banners.Count == 0)
            {
                Debug.LogWarning("No banners found. Select the renderers to dress, or name " +
                                 "them so the name starts with \"Banner\".");
                return;
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/_Game", "Materials");

            List<string> report = new List<string>();
            foreach (MeshRenderer renderer in banners)
                Dress(renderer, shader, report);

            AssetDatabase.SaveAssets();
            Debug.Log($"Banner cloth: {banners.Count} banner(s) dressed.\n  " +
                      string.Join("\n  ", report) + "\n" +
                      "If one of them swings along its own length instead of across it, the " +
                      "hang axis below is wrong — switch Show The Hang Mask on in the " +
                      "material: it must read black at the rail and white at the hem.");
        }

        static List<MeshRenderer> Targets()
        {
            List<MeshRenderer> found = new List<MeshRenderer>();

            foreach (GameObject go in Selection.gameObjects)
                foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
                    if (!found.Contains(r)) found.Add(r);

            if (found.Count > 0) return found;

            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform t = r.transform;
                while (t != null)
                {
                    if (t.name.StartsWith("Banner", System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (!found.Contains(r)) found.Add(r);
                        break;
                    }
                    t = t.parent;
                }
            }
            return found;
        }

        static void Dress(MeshRenderer renderer, Shader shader, List<string> report)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                report.Add($"{renderer.name}: no mesh — skipped");
                return;
            }

            // A mesh with four corners cannot bend, however good the maths is.
            if (mesh.vertexCount < 40)
                report.Add($"{renderer.name}: only {mesh.vertexCount} vertices — the cloth " +
                           "will move as one flat board. Subdivide it.");

            Bounds b = mesh.bounds;
            Vector3 size = b.size;

            int drop = Longest(size, -1);
            int width = Longest(size, drop);

            Vector3 hangAxis = Axis(drop);
            Vector3 widthAxis = Axis(width);

            // The nailed end is the one nearer the pivot: a banner hangs from its origin.
            float min = Component(b.min, drop), max = Component(b.max, drop);
            bool anchorAtMax = Mathf.Abs(max) < Mathf.Abs(min);
            if (!anchorAtMax) hangAxis = -hangAxis;      // point from the anchor to the hem

            Vector3 anchor = b.center;
            anchor = SetComponent(anchor, drop, anchorAtMax ? max : min);

            Material material = MaterialFor(renderer, shader);

            Undo.RecordObject(material, "Set up banner cloth");
            material.SetVector("_HangAxis", hangAxis);
            material.SetVector("_WidthAxis", widthAxis);
            material.SetVector("_AnchorPoint", anchor);
            material.SetFloat("_HangLength", Component(size, drop));
            material.SetFloat("_HalfWidth", Component(size, width) * 0.5f);
            EditorUtility.SetDirty(material);

            Undo.RecordObject(renderer, "Set up banner cloth");
            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++) slots[i] = material;
            renderer.sharedMaterials = slots;
            EditorUtility.SetDirty(renderer);

            report.Add($"{renderer.name}: hangs along {Pretty(hangAxis)}, " +
                       $"{Component(size, drop):0.###} long, " +
                       $"{Component(size, width):0.###} wide, anchored at " +
                       $"{anchor.ToString("0.###")} ({mesh.vertexCount} verts)");
        }

        /// <summary>
        /// One material per banner MESH, not per renderer and not one shared by all.
        ///
        /// Shared, every banner would carry the same rig, which is right only while they
        /// are the same model. One each, two copies of one banner would drift apart the
        /// first time somebody tuned one of them. The mesh is what the rig describes, so
        /// the mesh is what the material is named after.
        /// </summary>
        static Material MaterialFor(MeshRenderer renderer, Shader shader)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            string meshName = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.name : renderer.name;
            string path = $"{MaterialFolder}/Cloth_{Sanitise(meshName)}.mat";

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader != shader) existing.shader = shader;
                return existing;
            }

            Material made = new Material(shader);
            made.name = $"Cloth_{Sanitise(meshName)}";

            // Keep the look it already had: the pack's own texture and tint carry over, so
            // the only thing that changes on screen is that it moves.
            Material was = renderer.sharedMaterial;
            if (was != null)
            {
                if (was.HasProperty("_BaseMap") && made.HasProperty("_BaseMap"))
                    made.SetTexture("_BaseMap", was.GetTexture("_BaseMap"));
                else if (was.HasProperty("_MainTex") && made.HasProperty("_BaseMap"))
                    made.SetTexture("_BaseMap", was.GetTexture("_MainTex"));

                if (was.HasProperty("_BaseColor") && made.HasProperty("_BaseColor"))
                    made.SetColor("_BaseColor", was.GetColor("_BaseColor"));
                else if (was.HasProperty("_Color") && made.HasProperty("_BaseColor"))
                    made.SetColor("_BaseColor", was.GetColor("_Color"));
            }

            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        static string Sanitise(string s) =>
            s.Replace(" ", "_").Replace("/", "_").Replace("(", "").Replace(")", "").Trim();

        static int Longest(Vector3 size, int skip)
        {
            int best = -1;
            float bestValue = -1f;
            for (int i = 0; i < 3; i++)
            {
                if (i == skip) continue;
                float v = Component(size, i);
                if (v <= bestValue) continue;
                bestValue = v;
                best = i;
            }
            return best;
        }

        static float Component(Vector3 v, int i) => i == 0 ? v.x : i == 1 ? v.y : v.z;

        static Vector3 SetComponent(Vector3 v, int i, float value)
        {
            if (i == 0) v.x = value; else if (i == 1) v.y = value; else v.z = value;
            return v;
        }

        static Vector3 Axis(int i) =>
            i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        static string Pretty(Vector3 axis) =>
            $"{(axis.x != 0 ? (axis.x > 0 ? "+X" : "-X") : axis.y != 0 ? (axis.y > 0 ? "+Y" : "-Y") : axis.z > 0 ? "+Z" : "-Z")}";
    }
}

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
    /// on the transform depends on an import setting -- so the FBX says one thing and the
    /// Mesh in memory may say another.
    ///
    /// So it is measured, once, from the Mesh that is actually there.
    ///
    /// A banner is NOT one surface. Banner_wall is a single mesh carrying three sub-meshes
    /// -- a wooden rail, iron fittings and the cloth -- and the first version of this tool
    /// put the cloth material on all three. That is what turned the banners into flat dark
    /// boards: the shader rebuilds the vertex normal from the sheet it thinks it is bending,
    /// so a rail and a bracket, which are solid objects with normals pointing every way,
    /// were all re-normalled to one single direction and stopped catching any light at all.
    /// The cloth's own red was lost on top of that, because the tint was copied from
    /// whichever material happened to be in slot 0.
    ///
    /// So the cloth sub-mesh is picked by measurement too: of the sub-meshes, the cloth is
    /// the biggest flat one -- two large extents and a thin one. A rail is long but thin in
    /// two directions, a bracket is small. Every other slot is put back the way the model
    /// had it, which also means a second run repairs a scene the old version broke.
    ///
    /// Re-run it after changing the mesh. It logs every number it wrote, and it checks its
    /// own answer with the same expression the shader uses, so a banner that comes out
    /// swinging sideways -- or not swinging at all -- can be corrected by reading the log
    /// rather than by guessing at the material.
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
                      "If one of them hangs still, or swings along its own length instead of " +
                      "across it, switch Show The Hang Mask on in the material: it must read " +
                      "black at the rail and white at the hem.");
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
                report.Add($"{renderer.name}: no mesh -- skipped");
                return;
            }

            // A mesh with four corners cannot bend, however good the maths is.
            if (mesh.vertexCount < 40)
                report.Add($"{renderer.name}: only {mesh.vertexCount} vertices -- the cloth " +
                           "will move as one flat board. Subdivide it.");

            Material[] source = SourceMaterials(renderer);
            List<Bounds> parts = SubMeshBounds(mesh);
            int slot = ClothSlot(parts);
            if (slot < 0 || renderer.sharedMaterials.Length == 0)
            {
                report.Add($"{renderer.name}: could not read the sub-mesh bounds -- skipped");
                return;
            }
            slot = Mathf.Min(slot, renderer.sharedMaterials.Length - 1);

            Bounds b = parts[slot];
            Vector3 size = b.size;

            int drop = Longest(size, -1);
            int width = Longest(size, drop);

            Vector3 hangAxis = Axis(drop);
            Vector3 widthAxis = Axis(width);

            // The nailed end is the one nearer the pivot: a banner hangs from its origin.
            // The hang axis points from THERE TO THE OTHER END, so it is the anchor sitting
            // at the maximum that flips it, not the anchor sitting at the minimum.
            float min = Component(b.min, drop), max = Component(b.max, drop);
            bool anchorAtMax = Mathf.Abs(max) < Mathf.Abs(min);
            if (anchorAtMax) hangAxis = -hangAxis;

            Vector3 anchor = SetComponent(b.center, drop, anchorAtMax ? max : min);
            Vector3 hem = SetComponent(b.center, drop, anchorAtMax ? min : max);
            float length = Component(size, drop);

            // The shader's own expression, run here on the far end of the cloth. It has to
            // come out at 1: if the axis is backwards every vertex saturates to 0 instead,
            // the displacement is multiplied by zero and the banner hangs dead still --
            // which looks exactly like a shader that is not running.
            float alongHem = Mathf.Clamp01(Vector3.Dot(hem - anchor, hangAxis) /
                                           Mathf.Max(length, 1e-4f));
            if (alongHem < 0.99f)
                report.Add($"{renderer.name}: the rig came out upside down (the hem reads " +
                           $"{alongHem:0.###} where it must read 1). The banner will not " +
                           "move. This is a bug in the tool, not in the mesh.");

            Material material = MaterialFor(renderer, mesh, slot, source, shader, report);

            Undo.RecordObject(material, "Set up banner cloth");
            material.SetVector("_HangAxis", hangAxis);
            material.SetVector("_WidthAxis", widthAxis);
            material.SetVector("_AnchorPoint", anchor);
            material.SetFloat("_HangLength", length);
            material.SetFloat("_HalfWidth", Component(size, width) * 0.5f);
            EditorUtility.SetDirty(material);

            Undo.RecordObject(renderer, "Set up banner cloth");
            Material[] slots = renderer.sharedMaterials;
            List<string> kept = new List<string>();
            for (int i = 0; i < slots.Length; i++)
            {
                if (i == slot) continue;

                // Everything that is not the cloth goes back to the model's own material.
                // Without this, a scene the first version of the tool ran on keeps its rail
                // and its brackets painted with the cloth shader.
                if (source != null && i < source.Length && source[i] != null)
                    slots[i] = source[i];
                kept.Add($"{i} ({(slots[i] != null ? slots[i].name : "none")})");
            }
            slots[slot] = material;
            renderer.sharedMaterials = slots;
            EditorUtility.SetDirty(renderer);

            string others = kept.Count == 0 ? "no other slots"
                                            : "slots " + string.Join(", ", kept) + " left solid";
            report.Add($"{renderer.name}: cloth is slot {slot} of {slots.Length} " +
                       $"({material.name}), hangs along {Pretty(hangAxis)}, " +
                       $"{length:0.####} long, {Component(size, width):0.####} wide, " +
                       $"anchored at {anchor.ToString("0.####")}; {others} " +
                       $"({mesh.vertexCount} verts)");
        }

        /// <summary>
        /// The materials the model itself ships with, read through the prefab the renderer
        /// is an instance of. These are the truth about how the banner is meant to look;
        /// whatever is on the renderer right now may be a previous run of this tool.
        /// </summary>
        static Material[] SourceMaterials(MeshRenderer renderer)
        {
            MeshRenderer origin =
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer);
            return origin != null ? origin.sharedMaterials : null;
        }

        /// <summary>
        /// Per-sub-mesh bounds, in the mesh's own space.
        ///
        /// The descriptor carries them and needs no Read/Write on the importer, which this
        /// banner does not have. If a mesh comes back with the bounds unfilled -- they are
        /// all the same box, or an empty one -- the vertices are read instead, and if that
        /// is refused as well the caller is told by getting an empty list back.
        /// </summary>
        static List<Bounds> SubMeshBounds(Mesh mesh)
        {
            List<Bounds> parts = new List<Bounds>();
            int count = mesh.subMeshCount;
            if (count <= 0) return parts;

            bool empty = false, allTheSame = true;
            for (int i = 0; i < count; i++)
            {
                Bounds b = mesh.GetSubMesh(i).bounds;
                parts.Add(b);
                if (b.size.sqrMagnitude <= 1e-12f) empty = true;
                if (i > 0 && b != parts[0]) allTheSame = false;
            }
            if (!empty && (count == 1 || !allTheSame)) return parts;

            Vector3[] verts = mesh.vertices;
            if (verts == null || verts.Length == 0) return new List<Bounds>();

            parts.Clear();
            for (int i = 0; i < count; i++)
            {
                int[] indices = mesh.GetIndices(i);
                if (indices == null || indices.Length == 0)
                {
                    parts.Add(new Bounds(Vector3.zero, Vector3.zero));
                    continue;
                }
                Bounds b = new Bounds(verts[indices[0]], Vector3.zero);
                for (int k = 1; k < indices.Length; k++) b.Encapsulate(verts[indices[k]]);
                parts.Add(b);
            }
            return parts;
        }

        /// <summary>
        /// Which sub-mesh is the cloth.
        ///
        /// A sheet has two large extents and a thin one. A rail is long and thin in BOTH of
        /// the other directions, a bracket is small in all three. So: of the sub-meshes flat
        /// enough to be a sheet, the one with the largest face area; and if none of them is
        /// flat, the largest face area anyway, because something has to be the cloth.
        /// </summary>
        static int ClothSlot(List<Bounds> parts)
        {
            int best = -1, bestFlat = -1;
            float bestArea = -1f, bestFlatArea = -1f;

            for (int i = 0; i < parts.Count; i++)
            {
                Vector3 s = parts[i].size;
                float a = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                float c = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
                float b = s.x + s.y + s.z - a - c;
                float area = a * b;

                if (area > bestArea) { bestArea = area; best = i; }
                if (c <= 0.5f * b && area > bestFlatArea) { bestFlatArea = area; bestFlat = i; }
            }
            return bestFlat >= 0 ? bestFlat : best;
        }

        /// <summary>
        /// One material per cloth sub-mesh, named after the mesh AND the material it came
        /// from -- so a mesh with two sheets on it gets two, and so the flattened material
        /// the first version of this tool wrote is left behind rather than reused.
        ///
        /// The colour and the texture carry over, because they are the banner's identity.
        /// The specular does not: a wall material arrives at a smoothness of 0.31 and a
        /// cloth shader exists precisely so that a banner stops being shaded like a wall.
        /// </summary>
        static Material MaterialFor(MeshRenderer renderer, Mesh mesh, int slot,
                                    Material[] source, Shader shader, List<string> report)
        {
            Material was = Original(renderer, slot, source);
            string meshName = Sanitise(mesh.name);

            string stale = $"{MaterialFolder}/Cloth_{meshName}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(stale) != null)
                report.Add($"{renderer.name}: {stale} is left over from an earlier run and " +
                           "is no longer used by anything -- delete it.");

            string name = $"Cloth_{meshName}_{Sanitise(was != null ? was.name : "cloth")}";
            string path = $"{MaterialFolder}/{name}.mat";

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader != shader) existing.shader = shader;
                return existing;
            }

            Material made = new Material(shader);
            made.name = name;
            CopyLook(was, made);

            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        /// <summary>
        /// The material this slot is meant to look like: the model's own, or what is on the
        /// renderer now -- unless that is already a cloth material, in which case it says
        /// nothing about how the banner started out and copying from it would make a
        /// previous mistake permanent.
        /// </summary>
        static Material Original(MeshRenderer renderer, int slot, Material[] source)
        {
            if (source != null && slot < source.Length && source[slot] != null)
                return source[slot];

            Material[] now = renderer.sharedMaterials;
            if (slot >= now.Length || now[slot] == null) return null;
            if (now[slot].shader != null && now[slot].shader.name == ShaderName) return null;
            return now[slot];
        }

        static void CopyLook(Material was, Material made)
        {
            if (was == null) return;

            string map = was.HasProperty("_BaseMap") ? "_BaseMap"
                       : was.HasProperty("_MainTex") ? "_MainTex" : null;
            if (map != null && was.GetTexture(map) != null)
            {
                made.SetTexture("_BaseMap", was.GetTexture(map));
                made.SetTextureScale("_BaseMap", was.GetTextureScale(map));
                made.SetTextureOffset("_BaseMap", was.GetTextureOffset(map));
            }

            if (was.HasProperty("_BaseColor"))
                made.SetColor("_BaseColor", was.GetColor("_BaseColor"));
            else if (was.HasProperty("_Color"))
                made.SetColor("_BaseColor", was.GetColor("_Color"));
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

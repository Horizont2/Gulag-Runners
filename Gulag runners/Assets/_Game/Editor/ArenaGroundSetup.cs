using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts forest ground on the arena's grey blocks.
    ///
    /// Arena_Demo is built out of Unity primitives, and nineteen of them are still on
    /// Unity's own Default-Material: the main floor, the three upper walkways, the back
    /// wall, the six pillars and a handful of steps and panels. They are the grey boxes
    /// the fight happens on.
    ///
    /// Dropping a tiling texture on them does not work, and it is worth being exact about
    /// why, because it is the reason this is a tool and a custom shader rather than four
    /// drags in the Inspector. The main floor is a Cube scaled 4.41 x 0.24 x 30.16. A
    /// Unity Cube's UVs run 0..1 across every face, so one tiling factor has to serve a
    /// 4.41 x 30.16 top AND a 4.41 x 0.24 front -- a hundred and twenty-five to one. Any
    /// value smears one of them. "Gulag Runners/Arena Ground" sidesteps it by projecting
    /// from the world axes instead of reading the mesh's UVs, so a tile is a fixed number
    /// of METRES wherever it lands and the block's scale stops mattering.
    ///
    /// Three materials, because the shader needs one number it cannot work out for
    /// itself. _MeshSize is the mesh's size in its own space, and the lip and the foot
    /// shading are measured from the object's real top and bottom using it. Unity's Cube
    /// is one unit tall and its Cylinder is TWO -- the trap SimCollider.LocalBox already
    /// documents -- so the pillars need their own material or their grass lip lands half
    /// a pillar down.
    ///
    ///   GR_ArenaGround   forest floor on top, rocky earth down the sides. Everything
    ///                    wider than it is tall: floors, walkways, steps.
    ///   GR_ArenaRock     the same pair, with the cap pulled back to the flat top only.
    ///                    Walls and panels, where grass creeping down a vertical face
    ///                    would read as moss on a cliff rather than turf on a slab.
    ///   GR_ArenaPillar   wood, all round, _MeshSize (1,2,1). The six cylinders.
    ///
    /// WHAT IT WILL NOT TOUCH. Only renderers still on Unity's built-in Default-Material.
    /// Anything somebody has already textured is left alone and listed in the log instead,
    /// because this runs on a scene two people are dressing at once and a tool that
    /// overwrites the other one's work is worse than no tool. Use the Retexture item below
    /// when you do want it to take everything.
    ///
    /// Everything it makes is an asset: three materials in Assets/_Game/Materials, visible
    /// and tunable in the Inspector afterwards. Nothing is created at play time.
    /// </summary>
    public static class ArenaGroundSetup
    {
        const string ShaderName = "Gulag Runners/Arena Ground";
        const string MaterialFolder = "Assets/_Game/Materials";
        const string Pack = "Assets/Game Buffs/Free Stylized Textures/Textures";

        const string GroundMaterial = MaterialFolder + "/GR_ArenaGround.mat";
        const string RockMaterial = MaterialFolder + "/GR_ArenaRock.mat";
        const string PillarMaterial = MaterialFolder + "/GR_ArenaPillar.mat";

        // The sets this arena uses. Each folder in the pack holds <Set>_Albedo.png and
        // <Set>_Normal.png among others; the pack already imports the normals as normal
        // maps, so nothing here has to correct an importer.
        const string ForestFloor = "Forest_Ground_38";
        const string RockyEarth = "Rocky_Dirt_2";
        const string Planks = "Planks_Wood_16";

        // A floor seen from the side of the arena is seen at a grazing angle for most of
        // its length, which is the one case trilinear filtering blurs into mud. The pack
        // ships every texture at aniso 1.
        const int Aniso = 4;

        [MenuItem("Tools/Gulag Runners/Setup/Texture the arena ground")]
        public static void Apply() => Run(takeEverything: false);

        [MenuItem("Tools/Gulag Runners/Setup/Retexture every arena block")]
        public static void Retexture() => Run(takeEverything: true);

        static void Run(bool takeEverything)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"Arena ground: the shader \"{ShaderName}\" is not in the " +
                               "project. It lives in Assets/_Game/Shaders/ArenaGround.shader.");
                return;
            }

            Material ground = BuildGround(shader);
            Material rock = BuildRock(shader);
            Material pillar = BuildPillar(shader);
            if (ground == null || rock == null || pillar == null) return;

            AssetDatabase.SaveAssets();

            var changed = new List<string>();
            var kept = new List<string>();
            int seen = 0;

            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                if (renderer.GetComponent<SimCollider>() == null) continue;

                seen++;
                Material[] slots = renderer.sharedMaterials;
                Material want = Choose(renderer, filter.sharedMesh, ground, rock, pillar);

                bool touched = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == want) continue;
                    if (!takeEverything && !IsUnityDefault(slots[i]))
                    {
                        if (!IsOurs(slots[i]))
                            kept.Add($"  {renderer.name}: left on " +
                                     $"{(slots[i] != null ? slots[i].name : "no material")}");
                        continue;
                    }
                    slots[i] = want;
                    touched = true;
                }

                if (!touched) continue;

                Undo.RecordObject(renderer, "Texture the arena ground");
                renderer.sharedMaterials = slots;
                EditorUtility.SetDirty(renderer);
                changed.Add($"  {renderer.name} -> {want.name}  " +
                            $"({Describe(renderer.bounds)})");
            }

            if (changed.Count > 0)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Arena ground: {changed.Count} of {seen} marked blocks textured.\n" +
                string.Join("\n", changed) +
                (kept.Count > 0
                    ? $"\n\n{kept.Count} already had a material and were left alone -- run " +
                      "Retexture every arena block to take those too:\n" +
                      string.Join("\n", kept)
                    : "") +
                "\n\nThe three materials are in " + MaterialFolder + ". Tile size, the " +
                "grass lip and the shading under it are all on them in the Inspector; " +
                "_MeshSize is the one field to leave alone unless the mesh changes.");
        }

        // ------------------------------------------------------------------ the materials

        static Material BuildGround(Shader shader)
        {
            Material m = Load(GroundMaterial, shader, "GR_ArenaGround");
            if (m == null) return null;
            if (!Pair(m, "_TopMap", "_TopNormal", ForestFloor)) return null;
            if (!Pair(m, "_SideMap", "_SideNormal", RockyEarth)) return null;

            // A tile is this many metres, and both are 2 because they are measured
            // against the fighter rather than against the block. At 2 m a fern in the
            // floor texture is about 20 cm and a stone in the bank about 16 cm, which
            // are the sizes those things are beside someone 1.8 m tall. Tighter and the
            // bank turns to gravel and its repeat down a thirty-metre floor starts to
            // read as wallpaper; looser and it is boulders.
            //
            // The same earth gets the same number on every material here on purpose.
            // One texture at two sizes on two blocks that touch reads as two different
            // materials, which is the fault this whole shader is trying not to commit.
            m.SetFloat("_TopTile", 2.0f);
            m.SetFloat("_SideTile", 2.0f);

            // Both sets in this pack sit at about 0.13 luma. Left alone, the cap and the
            // side of a block are the same value and the block reads as one dark mass --
            // which is the flatness this is meant to fix, arriving by another road. The
            // side is lifted rather than the top darkened because the fighters are dark
            // silhouettes and need something to stand against.
            m.SetColor("_TopColor", new Color(1.15f, 1.15f, 1.1f, 1f));
            m.SetColor("_SideColor", new Color(1.9f, 1.85f, 1.75f, 1f));
            m.SetFloat("_TopNormalStrength", 1.0f);
            m.SetFloat("_SideNormalStrength", 1.15f);
            m.SetFloat("_TopSmoothness", 0.08f);
            m.SetFloat("_SideSmoothness", 0.05f);

            m.SetFloat("_TopFacing", 0.45f);
            m.SetFloat("_TopBlend", 0.22f);
            m.SetFloat("_Sharpness", 6f);

            // Five centimetres of turf over the edge of a 24 cm walkway. This is the
            // number that decides whether a platform reads as a slab of ground or as a
            // box with a picture of grass on it.
            m.SetFloat("_Overhang", 0.05f);
            m.SetFloat("_OverhangFade", 1.6f);
            m.SetFloat("_LipShade", 0.35f);
            m.SetFloat("_LipShadeDepth", 0.12f);
            m.SetFloat("_FootShade", 0.18f);
            m.SetFloat("_FootShadeHeight", 0.25f);

            m.SetVector("_MeshSize", new Vector4(1f, 1f, 1f, 0f));
            Save(m);
            return m;
        }

        static Material BuildRock(Shader shader)
        {
            Material m = Load(RockMaterial, shader, "GR_ArenaRock");
            if (m == null) return null;
            if (!Pair(m, "_TopMap", "_TopNormal", ForestFloor)) return null;
            if (!Pair(m, "_SideMap", "_SideNormal", RockyEarth)) return null;

            // The same earth as the ground material, at the same size: these two meet
            // everywhere, a floor edge in front of a bank.
            m.SetFloat("_TopTile", 2.0f);
            m.SetFloat("_SideTile", 2.0f);
            m.SetColor("_TopColor", new Color(1.15f, 1.15f, 1.1f, 1f));
            m.SetColor("_SideColor", new Color(1.8f, 1.74f, 1.62f, 1f));
            m.SetFloat("_TopNormalStrength", 1.0f);
            m.SetFloat("_SideNormalStrength", 1.25f);
            m.SetFloat("_TopSmoothness", 0.08f);
            m.SetFloat("_SideSmoothness", 0.05f);

            // The cap only on what is genuinely flat, and a short hard lip. A wall is two
            // metres of earth with a little moss on its cap, not a lawn with sides.
            m.SetFloat("_TopFacing", 0.72f);
            m.SetFloat("_TopBlend", 0.12f);
            m.SetFloat("_Sharpness", 8f);
            m.SetFloat("_Overhang", 0.03f);
            m.SetFloat("_OverhangFade", 2.4f);
            m.SetFloat("_LipShade", 0.3f);
            m.SetFloat("_LipShadeDepth", 0.18f);
            m.SetFloat("_FootShade", 0.3f);
            m.SetFloat("_FootShadeHeight", 0.45f);

            m.SetVector("_MeshSize", new Vector4(1f, 1f, 1f, 0f));
            Save(m);
            return m;
        }

        static Material BuildPillar(Shader shader)
        {
            Material m = Load(PillarMaterial, shader, "GR_ArenaPillar");
            if (m == null) return null;
            if (!Pair(m, "_TopMap", "_TopNormal", Planks)) return null;
            if (!Pair(m, "_SideMap", "_SideNormal", Planks)) return null;

            // A post is the same wood all the way round, so there is no cap to place.
            // _TopFacing has to clear 1 by more than _TopBlend for that: at exactly 1 a
            // flat top still comes out half capped, because 1 is the MIDDLE of the blend.
            m.SetFloat("_TopTile", 1.0f);
            m.SetFloat("_SideTile", 1.0f);
            m.SetColor("_TopColor", Color.white);
            m.SetColor("_SideColor", Color.white);
            m.SetFloat("_TopNormalStrength", 1.0f);
            m.SetFloat("_SideNormalStrength", 1.0f);
            m.SetFloat("_TopSmoothness", 0.12f);
            m.SetFloat("_SideSmoothness", 0.12f);
            m.SetFloat("_TopFacing", 1.5f);
            m.SetFloat("_TopBlend", 0.05f);
            m.SetFloat("_Sharpness", 4f);
            m.SetFloat("_Overhang", 0f);
            m.SetFloat("_OverhangFade", 1f);
            m.SetFloat("_LipShade", 0f);
            m.SetFloat("_LipShadeDepth", 0.1f);
            m.SetFloat("_FootShade", 0.35f);
            m.SetFloat("_FootShadeHeight", 0.4f);

            // Unity's Cylinder mesh is TWO units tall. Left at one, the foot shading
            // would sit halfway up the post.
            m.SetVector("_MeshSize", new Vector4(1f, 2f, 1f, 0f));
            Save(m);
            return m;
        }

        static Material Load(string path, Shader shader, string name)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null)
            {
                if (m.shader != shader) m.shader = shader;
                Undo.RecordObject(m, "Texture the arena ground");
                return m;
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                Debug.LogError($"Arena ground: {MaterialFolder} does not exist.");
                return null;
            }

            m = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void Save(Material m)
        {
            EditorUtility.SetDirty(m);
        }

        /// <summary>
        /// Hangs one set's albedo and normal on a pair of slots, and raises the anisotropy
        /// on both while it is there.
        ///
        /// The albedo and the normal are separate properties on purpose. The material this
        /// replaces had its _BumpMap pointing at the ALBEDO -- a perfectly reasonable
        /// mistake to make by dragging, and one that costs nothing visible until you
        /// notice the light never catches anything, because a colour image read as a
        /// normal map is noise around straight up.
        /// </summary>
        static bool Pair(Material m, string albedoSlot, string normalSlot, string set)
        {
            Texture2D albedo = Map(set, "Albedo");
            Texture2D normal = Map(set, "Normal");
            if (albedo == null || normal == null) return false;

            m.SetTexture(albedoSlot, albedo);
            m.SetTexture(normalSlot, normal);
            return true;
        }

        static Texture2D Map(string set, string map)
        {
            string path = $"{Pack}/{set}/{set}_{map}.png";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                Debug.LogError(
                    $"Arena ground: {path} is missing.\n" +
                    "That is the Free Stylized Textures pack. It is on this branch under " +
                    "Assets/Game Buffs -- if the folder is there but Unity cannot see it, " +
                    "the import has not finished; let it, then run this again.");
                return null;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.anisoLevel < Aniso)
            {
                importer.anisoLevel = Aniso;
                importer.SaveAndReimport();
            }

            return tex;
        }

        // ----------------------------------------------------------------- which is which

        /// <summary>
        /// Ground, rock or pillar, from the shape of the thing.
        ///
        /// A block is ground when you could stand on it: its top is at least as broad as
        /// the block is tall, in BOTH directions. Everything else is rock.
        ///
        /// Both directions is the whole rule, and getting it wrong is easy. "Wider than
        /// tall" sounds like the same test and is not: the back wall here is 28.38 m
        /// long, 1.46 m thick and 1.90 m high, so it is far wider than it is tall and
        /// would come out as ground -- a two-metre wall with grass growing down its face.
        /// Against its NARROW side, 1.90 over 1.46, it is plainly a wall.
        ///
        /// Measured on the renderer's WORLD bounds rather than the transform's scale,
        /// because this level is laid out along Z and turned ninety degrees to run along
        /// X -- read off lossyScale, the main floor's thirty metres come back as its
        /// depth and every floor in the arena is classified as a wall.
        /// </summary>
        static Material Choose(MeshRenderer renderer, Mesh mesh,
                               Material ground, Material rock, Material pillar)
        {
            if (mesh.name == "Cylinder" || mesh.name == "Capsule") return pillar;

            Vector3 size = renderer.bounds.size;
            float narrowest = Mathf.Min(size.x, size.z);
            return size.y <= narrowest ? ground : rock;
        }

        /// <summary>Unity's own grey, which is what "untextured" looks like in this scene.</summary>
        static bool IsUnityDefault(Material m)
        {
            if (m == null) return true;
            string path = AssetDatabase.GetAssetPath(m);
            return string.IsNullOrEmpty(path) || path.StartsWith("Resources/unity_builtin_extra");
        }

        static bool IsOurs(Material m)
        {
            if (m == null) return false;
            string path = AssetDatabase.GetAssetPath(m);
            return path == GroundMaterial || path == RockMaterial || path == PillarMaterial;
        }

        static string Describe(Bounds b) =>
            $"{b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m";
    }
}

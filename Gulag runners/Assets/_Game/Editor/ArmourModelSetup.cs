using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Gives the armour on the floor a model, instead of the stand-in cube.
    ///
    /// GroundItemView shows a fallback box for any item with nothing authored for it, and
    /// the note on that field has said since it was written that "armour and the bandage
    /// have no mesh in the weapon pack". That stopped being true: Low-Poly Medieval Market
    /// ships a body piece and THREE helmets, and not one of them is in any scene. So ten of
    /// the game's twenty-two items -- every vest, every coif, every belt -- land on the
    /// floor as a grey box, which is why armour reads as a square in one arena and as a
    /// thing in another: the weapons are wired and the armour never was.
    ///
    /// WHAT MAPS AND WHAT DOES NOT. The three helmets map one to one, which is the whole
    /// reason this is worth doing. The three body armours share the pack's single body
    /// piece -- they are a leather vest, a mail shirt and a plate cuirass, and the pack has
    /// one torso -- so they differ by TINT rather than by mesh, taken from the rarity ramp
    /// that already colours them in the HUD. The belts and the bandage keep the stand-in,
    /// because inventing a belt out of a shoulder pad would be worse than a box that is
    /// honest about being a box.
    /// </summary>
    public static class ArmourModelSetup
    {
        const string Pack = "Assets/Low-Poly Medieval Market/Prefabs/Armor";

        // Item, the pack prefab for it, and how much of the rarity colour to carry. One
        // torso mesh has to serve three grades of body armour, so the tint is what tells
        // a leather vest from a plate at a glance across a room.
        static readonly (ItemId item, string prefab, float tint)[] Wiring =
        {
            (ItemId.LeatherVest, "armor.prefab",             0.85f),
            (ItemId.Chainmail,   "armor.prefab",             0.45f),
            (ItemId.Plate,       "armor.prefab",             0.15f),

            (ItemId.LeatherCap,  "armor_gorget.prefab",      0.70f),
            (ItemId.MailCoif,    "armor_helmet.prefab",      0.35f),
            (ItemId.GreatHelm,   "helmet_visor_solo.prefab", 0.10f),
        };

        [MenuItem("Tools/Gulag Runners/Setup/Give the armour a model")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            GroundItemView[] views = Object.FindObjectsByType<GroundItemView>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (views.Length == 0)
            {
                Debug.LogWarning($"{scene.name} has no GroundItemView in it, so there is "
                                 + "nothing on the floor to dress.");
                return;
            }

            List<string> missing = new List<string>();
            int dressed = 0;

            foreach (GroundItemView view in views)
            {
                List<ItemModel> models = new List<ItemModel>(
                    view.models ?? new ItemModel[0]);

                foreach ((ItemId item, string prefabName, float tint) in Wiring)
                {
                    if (models.Exists(m => m != null && m.item == item)) continue;

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"{Pack}/{prefabName}");
                    if (prefab == null)
                    {
                        if (!missing.Contains(prefabName)) missing.Add(prefabName);
                        continue;
                    }

                    GameObject made = (GameObject)PrefabUtility.InstantiatePrefab(
                        prefab, view.transform);
                    Undo.RegisterCreatedObjectUndo(made, "Give the armour a model");
                    made.name = item.ToString();
                    made.transform.localPosition = Vector3.zero;
                    made.transform.localRotation = Quaternion.identity;

                    // Off until an item of this kind is actually on this spot; Show() turns
                    // exactly one of them on.
                    made.SetActive(false);

                    Tint(made, tint);

                    models.Add(new ItemModel { item = item, root = made });
                    dressed++;
                }

                Undo.RecordObject(view, "Give the armour a model");
                view.models = models.ToArray();
                EditorUtility.SetDirty(view);
            }

            EditorSceneManager.MarkSceneDirty(scene);

            string said = $"Armour dressed in {scene.name}: {dressed} model(s) added across "
                          + $"{views.Length} floor slot(s). The three helmets map one to one; "
                          + "the three body armours share the pack's single torso and differ "
                          + "by tint.";
            if (missing.Count > 0)
                said += "\n\nNOT FOUND in " + Pack + ": " + string.Join(", ", missing);
            said += "\n\nThe belts and the bandage still show the stand-in box: the pack has "
                    + "no mesh for either, and a box that is honest beats a shoulder pad "
                    + "pretending to be a belt.";

            Debug.Log(said);
        }

        /// <summary>
        /// Pulls the model towards the rarity colour it already wears in the HUD, so one
        /// torso mesh can be three grades of armour without three meshes.
        ///
        /// THROUGH A MATERIAL ASSET, and it used to be through a MaterialPropertyBlock,
        /// which from an editor tool is the same as doing nothing at all. A property block
        /// is a runtime override: it is handed to a renderer for the frames it is drawing
        /// and it is NOT serialized, so every colour this tool set was gone the moment the
        /// scene was saved and reopened. Three grades of body armour that differ only by
        /// tint were therefore three identical torsos, and the tool reported success.
        ///
        /// So the tint is baked into a material beside the pack it came from, one per
        /// shade, shared by every arena. Made once and reused: running this in the fourth
        /// scene finds the three the first scene made.
        /// </summary>
        static void Tint(GameObject root, float towardsLeather)
        {
            // Leather is warm and dull, plate is cold and bright. The ramp between them is
            // the same two ends the interface uses for Common and Rare, so the floor and
            // the HUD agree about which is the better one.
            Color leather = new Color(0.46f, 0.31f, 0.18f);
            Color steel = UiPalette.Common;
            Color want = Color.Lerp(steel, leather, Mathf.Clamp01(towardsLeather));

            Material tinted = Shade(root, want, towardsLeather);
            if (tinted == null) return;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Undo.RecordObject(r, "Give the armour a model");
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = tinted;
                r.sharedMaterials = mats;
                EditorUtility.SetDirty(r);
            }
        }

        /// <summary>
        /// The material for one shade, made from whatever the pack's own armour uses so the
        /// shader, the maps and the smoothness all come along -- only the colour changes.
        /// Found rather than remade on a second run.
        /// </summary>
        static Material Shade(GameObject root, Color want, float towardsLeather)
        {
            string folder = $"{Pack}/Tinted";
            string path = $"{folder}/Armour_{Mathf.RoundToInt(towardsLeather * 100f):000}.mat";

            Material had = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (had != null) return had;

            Material from = null;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != null) { from = r.sharedMaterial; break; }
            if (from == null) return null;

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Pack, "Tinted");

            Material made = new Material(from);
            made.name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (made.HasProperty("_BaseColor")) made.SetColor("_BaseColor", want);
            if (made.HasProperty("_Color")) made.SetColor("_Color", want);

            AssetDatabase.CreateAsset(made, path);
            return made;
        }
    }
}

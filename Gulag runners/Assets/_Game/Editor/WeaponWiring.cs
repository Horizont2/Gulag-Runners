using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts a weapon pack into the game.
    ///
    /// Every item has to appear in two places: in the fighter's hand when it is carried, and
    /// on the floor when it comes out of a chest. With a dozen weapons and six loot slots that
    /// is around a hundred objects to drag and a hundred list rows to fill in, which is not a
    /// job anybody does twice — so it is done here instead, from the item names, and the
    /// result is ordinary scene objects that can be nudged by hand afterwards.
    ///
    /// Re-runnable. It replaces what it made last time rather than piling a second copy on
    /// top, so swapping the pack for a better one is the same single menu item.
    /// </summary>
    public static class WeaponWiring
    {
        const string Made = "__wired";          // what this tool made, and may replace

        /// <summary>
        /// Which prefab is which item. Named rather than guessed, because a weapon pack's
        /// idea of "mace" and the game's are two different things and only one of them is
        /// load-bearing.
        /// </summary>
        static readonly (string prefab, ItemId item)[] Map =
        {
            ("Dagger",      ItemId.Dagger),
            ("Club",        ItemId.Club),
            ("Gladius",     ItemId.Gladius),
            ("Mace",        ItemId.Mace),
            ("Sword",       ItemId.Sword),
            ("Saber",       ItemId.Saber),
            ("Axe",         ItemId.Axe),
            ("Spear",       ItemId.Spear),
            ("Scythe",      ItemId.Scythe),
            ("FlangedMace", ItemId.FlangedMace),
            ("Hammer",      ItemId.Warhammer),
        };

        /// <summary>
        /// How big a pack weapon is drawn, against the size it was modelled at.
        ///
        /// A weapon pack is authored for a human of about 1.8 m. This game's fighter is 0.91 m
        /// — the model is scaled to a half and the simulation's body was tuned to match — so a
        /// sword dropped in at its authored size is a sword twice as long as the person
        /// holding it, and on the floor it looks like scenery rather than loot.
        /// </summary>
        /// <summary>
        /// No scaling here any more.
        ///
        /// This used to be 0.5f — 0.91 over 1.8, the fighter's height over the height the pack
        /// was authored for. It was a guess about one pack, and it reached the scene only if
        /// somebody re-ran this tool after it was added, which is exactly what did not happen:
        /// every weapon in Arena_Demo stayed at pack scale, a metre or two long on a fighter
        /// under a metre tall. Sizing now happens where the models are used, measured from
        /// each mesh against the item's own reach — see ItemModels.Fit.
        /// </summary>
        const float PackScale = 1f;

        [MenuItem("Tools/Gulag Runners/Setup/Wire weapon models into the scene")]
        public static void Wire()
        {
            Dictionary<ItemId, GameObject> prefabs = FindPrefabs(out List<string> missing);
            if (prefabs.Count == 0)
            {
                Debug.LogError("Wire weapons: found none of the pack's prefabs. They are " +
                               "matched by name — see the table in WeaponWiring.");
                return;
            }

            int hands = 0, slots = 0;

            foreach (PlayerLoadoutView loadout in Object.FindObjectsByType<PlayerLoadoutView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform under = loadout.socket != null
                    ? loadout.socket.transform
                    : loadout.transform;
                loadout.weapons = Build(prefabs, under, weaponsOnly: true);
                EditorUtility.SetDirty(loadout);
                hands++;
            }

            foreach (GroundItemView view in Object.FindObjectsByType<GroundItemView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform under = view.spinRoot != null ? view.spinRoot : view.transform;
                view.models = Build(prefabs, under, weaponsOnly: false);
                if (view.fallbackModel == null) view.fallbackModel = MakeStandIn(under);
                EditorUtility.SetDirty(view);
                slots++;
            }

            Debug.Log($"Wire weapons: {prefabs.Count} models into {hands} hand(s) and " +
                      $"{slots} loot slot(s)." +
                      (missing.Count > 0
                          ? $" No prefab found for: {string.Join(", ", missing)}."
                          : "") +
                      " Armour and the bandage have no models in this pack, so they drop as " +
                      "a stand-in cube rather than as nothing at all.");
        }

        /// <summary>
        /// Points the loadout back at the models that are ALREADY in the hand.
        ///
        /// Wire above makes its own copies and throws away whatever was there, which is the
        /// wrong tool for a hand that somebody has already dressed: the models under the
        /// socket may have been nudged, re-parented or swapped for better ones, and all of
        /// that is lost. What goes wrong far more often is narrower than that -- the models
        /// are fine and only the REFERENCES to them were dropped, which happens whenever the
        /// socket is rebuilt and the children are re-created underneath it.
        ///
        /// A row that points at nothing is not a visible error. ItemModels.Show finds no
        /// root for the item, switches nothing on, and the fighter carries it invisibly.
        /// Measured on this project: Player 2's prefab lost all eleven that way, and it went
        /// unnoticed because every arena happens to override them -- so it was waiting for
        /// the next scene built from the prefab rather than breaking the ones that exist.
        ///
        /// So this matches by NAME, fills only the rows that are empty, and creates nothing.
        /// Run it on a fighter whose weapons have gone invisible.
        /// </summary>
        [MenuItem("Tools/Gulag Runners/Setup/Relink weapon models already in the hand")]
        public static void Relink()
        {
            int filled = 0, hands = 0;
            List<string> blank = new List<string>();

            foreach (PlayerLoadoutView loadout in Object.FindObjectsByType<PlayerLoadoutView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (loadout.weapons == null || loadout.weapons.Length == 0) continue;

                Transform under = loadout.socket != null
                    ? loadout.socket.transform
                    : loadout.transform;

                hands++;
                bool touched = false;

                for (int i = 0; i < loadout.weapons.Length; i++)
                {
                    ItemModel row = loadout.weapons[i];
                    if (row == null || row.root != null) continue;

                    GameObject found = FindByName(under, row.item);
                    if (found == null)
                    {
                        blank.Add($"{loadout.name}/{row.item}");
                        continue;
                    }

                    Undo.RecordObject(loadout, "Relink weapon models");
                    row.root = found;
                    touched = true;
                    filled++;
                }

                if (touched) EditorUtility.SetDirty(loadout);
            }

            Debug.Log(
                $"Relink weapons: {filled} empty row(s) pointed back at a model, across "
                + $"{hands} hand(s). Nothing was created and nothing that was already set "
                + "was changed."
                + (blank.Count > 0
                    ? $"\n  Still empty, with no model of that name under the socket: "
                      + string.Join(", ", blank)
                    : ""));
        }

        /// <summary>
        /// The model for an item, among the socket's children, by name.
        ///
        /// The pack's own prefab name is what a child is called, so the item is matched
        /// through the same table Wire uses rather than against the enum -- "Hammer" and
        /// ItemId.Warhammer are the same weapon and no string comparison will say so.
        /// A name with this tool's own suffix on it still matches, so a hand that was
        /// wired once and lost its references later is repaired too.
        /// </summary>
        static GameObject FindByName(Transform under, ItemId item)
        {
            string want = null;
            foreach ((string prefab, ItemId which) in Map)
                if (which == item) { want = prefab; break; }
            if (want == null) return null;

            for (int i = 0; i < under.childCount; i++)
            {
                Transform child = under.GetChild(i);
                string name = child.name;
                if (name.EndsWith(Made)) name = name.Substring(0, name.Length - Made.Length);

                if (string.Equals(name, want, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, item.ToString(),
                                     System.StringComparison.OrdinalIgnoreCase))
                    return child.gameObject;
            }
            return null;
        }

        [MenuItem("Tools/Gulag Runners/Setup/Remove wired weapon models")]
        public static void Unwire()
        {
            int gone = 0;
            foreach (Transform t in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || !t.name.EndsWith(Made)) continue;
                Undo.DestroyObjectImmediate(t.gameObject);
                gone++;
            }
            Debug.Log($"Wire weapons: removed {gone} model(s) this tool had made.");
        }

        static ItemModel[] Build(Dictionary<ItemId, GameObject> prefabs, Transform under,
                                 bool weaponsOnly)
        {
            ClearMade(under);

            List<ItemModel> list = new List<ItemModel>();
            foreach ((string _, ItemId item) in Map)
            {
                if (!prefabs.TryGetValue(item, out GameObject prefab)) continue;
                if (weaponsOnly && ItemTable.Get(item).Kind != ItemKind.Weapon) continue;

                GameObject made = (GameObject)PrefabUtility.InstantiatePrefab(prefab, under);
                made.name = $"{item}{Made}";
                made.transform.localPosition = Vector3.zero;
                made.transform.localRotation = Quaternion.identity;
                made.transform.localScale = prefab.transform.localScale * PackScale;
                made.SetActive(false);
                Undo.RegisterCreatedObjectUndo(made, "Wire weapon models");

                list.Add(new ItemModel { item = item, root = made });
            }
            return list.ToArray();
        }

        /// <summary>
        /// The stand-in for an item with no mesh. Armour and the bandage have none in this
        /// pack, and they are a third of what the chests pay out: without this, a third of
        /// every round lands on the floor invisible and reads as "that chest gave me nothing",
        /// which is the worst lie available in a game about deciding what to spend time on.
        /// </summary>
        static GameObject MakeStandIn(Transform under)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Stand-in{Made}";
            cube.transform.SetParent(under, false);
            cube.transform.localScale = Vector3.one * 0.18f;
            Object.DestroyImmediate(cube.GetComponent<Collider>());   // loot is not geometry
            cube.SetActive(false);
            Undo.RegisterCreatedObjectUndo(cube, "Wire weapon models");
            return cube;
        }

        static void ClearMade(Transform under)
        {
            for (int i = under.childCount - 1; i >= 0; i--)
            {
                Transform c = under.GetChild(i);
                if (c.name.EndsWith(Made)) Undo.DestroyObjectImmediate(c.gameObject);
            }
        }

        static Dictionary<ItemId, GameObject> FindPrefabs(out List<string> missing)
        {
            Dictionary<ItemId, GameObject> found = new Dictionary<ItemId, GameObject>();
            missing = new List<string>();

            foreach ((string name, ItemId item) in Map)
            {
                // Named exactly, anywhere in the project: a pack can be imported wherever.
                string[] hits = AssetDatabase.FindAssets($"{name} t:Prefab");
                GameObject best = null;
                foreach (string hit in hits)
                {
                    string path = AssetDatabase.GUIDToAssetPath(hit);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                    best = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    break;
                }

                if (best != null) found[item] = best;
                else missing.Add(name);
            }
            return found;
        }
    }
}

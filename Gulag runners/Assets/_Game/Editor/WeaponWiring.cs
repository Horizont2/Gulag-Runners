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

        [MenuItem("Gulag Runners/Wire weapon models into the scene")]
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

        [MenuItem("Gulag Runners/Remove wired weapon models")]
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

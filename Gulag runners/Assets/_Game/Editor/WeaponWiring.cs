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
            ("Shield",      ItemId.Shield),
        };

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
                EditorUtility.SetDirty(view);
                slots++;
            }

            Debug.Log($"Wire weapons: {prefabs.Count} models into {hands} hand(s) and " +
                      $"{slots} loot slot(s)." +
                      (missing.Count > 0
                          ? $" No prefab found for: {string.Join(", ", missing)}."
                          : "") +
                      " Armour and the bandage have no models in this pack, so they still " +
                      "drop and are still invisible on the floor.");
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
                made.SetActive(false);
                Undo.RegisterCreatedObjectUndo(made, "Wire weapon models");

                list.Add(new ItemModel { item = item, root = made });
            }
            return list.ToArray();
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

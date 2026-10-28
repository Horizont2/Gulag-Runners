using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Files the scene's root into groups, and keeps doing it as the level grows.
    ///
    /// Arena_Demo has 295 objects at its root: thirteen that mean something and nearly
    /// three hundred barrels, crates, cobwebs, chains and trees dropped in beside them.
    /// Finding the camera in that list is a scroll, and every prop added makes it worse.
    ///
    /// WHAT IT WILL NOT MOVE, and why, because getting this wrong breaks the game rather
    /// than the tidiness:
    ///
    ///   THE FIGHTERS stay at the root. WeaponSocket.Resolve falls back to
    ///   transform.root.GetComponentInChildren&lt;Animator&gt;() when a socket has no
    ///   PlayerController and no Animator above it. Put both fighters under one group
    ///   and that root becomes the group, so the fallback can hand a socket on one
    ///   fighter the other fighter's rig. Two objects stay loose and the whole class of
    ///   bug goes with them.
    ///
    ///   ANYTHING ALREADY IN A GROUP is left where it is. This only ever files things
    ///   that are loose at the root, so running it twice does nothing the first run did
    ///   not, and it never undoes a grouping somebody made on purpose.
    ///
    /// Grouping is safe for the simulation, which was worth checking rather than
    /// assuming: SimWorldBaker walks scene.GetRootGameObjects() and then
    /// GetComponentsInChildren on each, so a collider one level deeper is still found.
    /// The groups are created active and at the origin, so nothing moves and nothing
    /// drops out of the bake.
    ///
    /// Re-run it whenever the level grows. That is the answer to keeping it tidy: not a
    /// rule somebody has to remember, a menu item somebody can click.
    /// </summary>
    public static class HierarchyTidy
    {
        const string Props = "Props";
        const string Systems = "Systems";
        const string Lighting = "Lighting";
        const string Cameras = "Cameras";
        const string Geometry = "Arena";

        // Below this many of a kind, a sub-group is more clutter than the props were.
        const int WorthItsOwnGroup = 3;

        static readonly Regex Numbered = new Regex(@"\s*\(\d+\)$");

        [MenuItem("Tools/Gulag Runners/Setup/Tidy the hierarchy")]
        public static void Tidy()
        {
            var loose = new List<GameObject>();
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go.transform.parent != null) continue;      // already filed
                if (IsGroup(go)) continue;                      // a group itself
                if (Keep(go)) continue;                         // must stay at the root
                loose.Add(go);
            }

            if (loose.Count == 0)
            {
                Debug.Log("Hierarchy: nothing loose at the root. Already tidy.");
                return;
            }

            // How many of each kind there are decides whether a kind gets its own
            // sub-group, so count before moving anything.
            var kinds = new Dictionary<string, int>();
            foreach (GameObject go in loose)
            {
                if (Bucket(go) != Props) continue;
                string kind = Numbered.Replace(go.name, string.Empty).Trim();
                kinds.TryGetValue(kind, out int n);
                kinds[kind] = n + 1;
            }

            var counts = new SortedDictionary<string, int>();
            foreach (GameObject go in loose)
            {
                string bucket = Bucket(go);
                Transform parent = Group(bucket);

                if (bucket == Props)
                {
                    string kind = Numbered.Replace(go.name, string.Empty).Trim();
                    if (kinds.TryGetValue(kind, out int n) && n >= WorthItsOwnGroup)
                        parent = Group(kind, parent);
                }

                // worldPositionStays, so filing a prop never moves it. The groups sit at
                // the origin anyway; this is belt and braces against somebody dragging
                // a group later.
                Undo.SetTransformParent(go.transform, parent, "Tidy the hierarchy");

                counts.TryGetValue(bucket, out int c);
                counts[bucket] = c + 1;
            }

            foreach (Transform group in Groups()) Sort(group);

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

            var lines = new List<string>();
            foreach (var pair in counts) lines.Add($"  {pair.Value,4} -> {pair.Key}");

            Debug.Log($"Hierarchy: filed {loose.Count} loose object(s).\n" +
                      string.Join("\n", lines) +
                      "\n\nThe fighters are left at the root on purpose: WeaponSocket " +
                      "falls back to transform.root to find a rig, and grouping them " +
                      "would let it find the wrong one. Run this again whenever the " +
                      "level grows.");
        }

        // ------------------------------------------------------------- what goes where

        /// <summary>
        /// Which group this object belongs in, from what it IS rather than what it is
        /// called. Names in this scene came from six asset packs and agree about nothing.
        /// </summary>
        static string Bucket(GameObject go)
        {
            if (go.GetComponentInChildren<Camera>(true) != null) return Cameras;
            if (go.GetComponentInChildren<Light>(true) != null) return Lighting;

            if (go.GetComponentInChildren<SimWorldBaker>(true) != null) return Systems;
            if (go.GetComponentInChildren<MatchState>(true) != null) return Systems;
            if (go.GetComponentInChildren<AudioManager>(true) != null) return Systems;
            if (go.GetComponentInChildren<EventSystem>(true) != null) return Systems;
            if (go.GetComponentInChildren<Canvas>(true) != null) return Systems;

            // Collision the fight actually runs on, as against scenery that only looks
            // solid. Worth its own group: it is the part anybody tuning the level edits.
            if (go.GetComponentInChildren<SimCollider>(true) != null) return Geometry;

            // A prop is something you can SEE. Anything with nothing to draw and nothing
            // recognised on it is machinery -- the post-process volume, a spawn marker,
            // an empty somebody parents things to -- and belongs with the machinery
            // rather than among the barrels.
            if (go.GetComponentInChildren<Renderer>(true) != null) return Props;
            return Systems;
        }

        /// <summary>Objects that must stay exactly where they are in the hierarchy.</summary>
        static bool Keep(GameObject go)
        {
            // See the class comment: WeaponSocket's last-resort rig lookup walks to
            // transform.root, so a fighter's root has to be the fighter.
            if (go.GetComponentInChildren<PlayerController>(true) != null) return true;

            // UI is never filed. A RectTransform's position is meaningless outside the
            // layout it belongs to, so moving one under a plain GameObject does not
            // tidy the menu, it rearranges it -- silently, and only where the anchors
            // happen to disagree. The menu scene has sixteen roots this would have
            // picked up.
            if (go.transform is RectTransform) return true;

            return false;
        }

        static readonly string[] All = { Geometry, Props, Systems, Lighting, Cameras };

        static bool IsGroup(GameObject go)
        {
            foreach (string name in All)
                if (go.name == name) return true;
            return false;
        }

        static IEnumerable<Transform> Groups()
        {
            foreach (string name in All)
            {
                GameObject go = GameObject.Find("/" + name);
                if (go != null) yield return go.transform;
            }
        }

        static Transform Group(string name, Transform under = null)
        {
            Transform existing = under != null
                ? under.Find(name)
                : (GameObject.Find("/" + name) is GameObject g ? g.transform : null);
            if (existing != null) return existing;

            var made = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(made, "Tidy the hierarchy");
            if (under != null) made.transform.SetParent(under, false);
            made.transform.localPosition = Vector3.zero;
            made.transform.localRotation = Quaternion.identity;
            made.transform.localScale = Vector3.one;
            return made.transform;
        }

        /// <summary>
        /// Alphabetical, but with numbers read as numbers: Unity's own naming makes
        /// "Barrel (10)" and plain string order puts it between (1) and (2), which looks
        /// like no order at all.
        /// </summary>
        static void Sort(Transform group)
        {
            var children = new List<Transform>();
            foreach (Transform child in group) children.Add(child);
            children.Sort((a, b) => Natural(a.name, b.name));

            for (int i = 0; i < children.Count; i++)
                children[i].SetSiblingIndex(i);

            foreach (Transform child in children)
                if (child.childCount > 0 && IsSubGroup(child)) Sort(child);
        }

        static bool IsSubGroup(Transform t) =>
            t.GetComponents<Component>().Length == 1;      // nothing but a Transform

        static int Natural(string a, string b)
        {
            Match ma = Numbered.Match(a), mb = Numbered.Match(b);
            string rootA = Numbered.Replace(a, string.Empty);
            string rootB = Numbered.Replace(b, string.Empty);

            int byName = string.CompareOrdinal(rootA, rootB);
            if (byName != 0) return byName;
            if (!ma.Success) return mb.Success ? -1 : 0;
            if (!mb.Success) return 1;

            int na = int.Parse(ma.Value.Trim().Trim('(', ')'));
            int nb = int.Parse(mb.Value.Trim().Trim('(', ')'));
            return na.CompareTo(nb);
        }
    }
}

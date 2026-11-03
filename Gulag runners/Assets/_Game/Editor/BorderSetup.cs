using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Makes the walls at the ends of an arena into something the simulation can rely on.
    ///
    /// Every border in both arenas is a Unity Plane, and a Plane is FLAT. Its collider
    /// comes out of the bake with MinX equal to MaxX once the box is rounded to
    /// millimetres -- a wall of zero width. It does stop a fighter today, and that is
    /// worth being precise about rather than reassured by: it stops him because the
    /// sweep test counts touching as overlapping, not because there is anything in the
    /// way. A body moved far enough in one tick -- a dodge, a knockback, a weapon that
    /// throws -- has nothing to be stopped BY.
    ///
    /// The second half is worse and has already bitten. The borders carry no SimCollider,
    /// so they reach the simulation only through bakeUnityColliders, which means they are
    /// subject to every heuristic meant for scenery. dropBackdrop threw away the Nordic
    /// arena's top-storey borders -- four walls out of six -- because planeZ was set for
    /// a different storey, and nothing anywhere said so. A marker is a decision; a
    /// collider that happens to be in the right place is a guess that keeps being
    /// re-made.
    ///
    /// So this gives each border both: a SimCollider set to Solid, which no depth rule
    /// may cull, and a BoxCollider with real thickness in the one axis it is flat in.
    /// It measures that axis rather than assuming X, because an arena with a wall across
    /// its depth would otherwise be thickened in the direction it is already wide.
    /// </summary>
    public static class BorderSetup
    {
        /// <summary>
        /// How thick a wall becomes, in metres. Wider than any single tick of movement
        /// in this game -- a run covers 5 cm, the fastest dodge about 20 -- so there is
        /// no speed at which a body can be on both sides of it in consecutive frames.
        /// </summary>
        const float Thickness = 0.4f;

        /// <summary>Thinner than this and the object is a flat plane pretending to be a wall.</summary>
        const float Flat = 0.05f;

        /// <summary>Names that mean "this is the edge of the level", lowercased.</summary>
        static readonly string[] Hints = { "border", "bound", "edge", "limit", "wall_end" };

        [MenuItem("Tools/Gulag Runners/Setup/List the arena borders")]
        public static void List() => Run(dryRun: true);

        [MenuItem("Tools/Gulag Runners/Setup/Close the arena borders")]
        public static void Close() => Run(dryRun: false);

        static void Run(bool dryRun)
        {
            var done = new List<string>();
            var skipped = new List<string>();

            foreach (GameObject go in Borders())
            {
                if (!Measure(go, out Bounds box, out int flat))
                {
                    skipped.Add($"  {go.name}: nothing to measure on it");
                    continue;
                }

                string axis = flat == 0 ? "X" : flat == 1 ? "Y" : "Z";
                float was = box.size[flat];

                if (dryRun)
                {
                    done.Add($"  {go.name}: {box.size.x:0.00} x {box.size.y:0.00} x " +
                             $"{box.size.z:0.00} m, flat in {axis} ({was:0.###} m) -> " +
                             $"{Thickness:0.00} m, marked Solid");
                    continue;
                }

                Thicken(go, box, flat);
                Mark(go);
                done.Add($"  {go.name}: {axis} {was:0.###} -> {Thickness:0.00} m, " +
                         "marked Solid");
            }

            var message = new System.Text.StringBuilder();
            if (done.Count == 0 && skipped.Count == 0)
            {
                Debug.Log("Borders: nothing in this scene looks like the edge of the " +
                          "level. They are found by name -- " +
                          string.Join(", ", Hints) + " -- on the object or a parent, " +
                          "so a group called \"Borders\" is enough.");
                return;
            }

            message.AppendLine(dryRun
                ? $"Borders (DRY RUN, nothing changed): {done.Count} wall(s)."
                : $"Borders: {done.Count} wall(s) given {Thickness:0.00} m of thickness " +
                  "and a Solid marker.");
            foreach (string line in done) message.AppendLine(line);
            if (skipped.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Left alone:");
                foreach (string line in skipped) message.AppendLine(line);
            }

            if (!dryRun && done.Count > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                message.AppendLine("Scene saved. Check it with " +
                                   "python3 docs/refs/border_check.py, which runs a " +
                                   "fighter at both ends of every storey.");
            }
            else if (dryRun)
            {
                message.AppendLine();
                message.AppendLine("Run \"Close the arena borders\" to do it.");
            }

            Debug.Log(message.ToString());
        }

        static List<GameObject> Borders()
        {
            var found = new List<GameObject>();
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go.GetComponent<Renderer>() == null &&
                    go.GetComponent<Collider>() == null) continue;
                if (!NamedLikeABorder(go.transform)) continue;
                found.Add(go);
            }
            return found;
        }

        /// <summary>
        /// The name on the object OR on anything above it, because these are kept in a
        /// group called Borders and the walls themselves are called "Plane (3)".
        /// </summary>
        static bool NamedLikeABorder(Transform t)
        {
            for (Transform at = t; at != null; at = at.parent)
            {
                string name = at.name.ToLowerInvariant();
                foreach (string hint in Hints)
                    if (name.Contains(hint)) return true;
            }
            return false;
        }

        /// <summary>The wall's world box, and which axis it is flat in.</summary>
        static bool Measure(GameObject go, out Bounds box, out int flat)
        {
            box = new Bounds();
            flat = 0;

            bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { box = r.bounds; any = true; }
                else box.Encapsulate(r.bounds);
            }
            if (!any)
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (!any) { box = c.bounds; any = true; }
                    else box.Encapsulate(c.bounds);
                }
            if (!any) return false;

            flat = box.size.x <= box.size.y && box.size.x <= box.size.z ? 0
                 : box.size.y <= box.size.z ? 1 : 2;
            return true;
        }

        static void Thicken(GameObject go, Bounds box, int flat)
        {
            BoxCollider collider = go.GetComponent<BoxCollider>();
            if (collider == null) collider = Undo.AddComponent<BoxCollider>(go);
            if (collider == null) return;

            Undo.RecordObject(collider, "Close the arena borders");

            // In the object's own space, because that is what a BoxCollider is in. A
            // wall scaled to a tenth needs ten times the local size to be the same
            // number of metres thick, and the whole point of this is metres.
            Vector3 lossy = go.transform.lossyScale;
            Vector3 size = collider.size;
            Vector3 world = new Vector3(
                Mathf.Abs(lossy.x) > 1e-5f ? box.size.x / Mathf.Abs(lossy.x) : box.size.x,
                Mathf.Abs(lossy.y) > 1e-5f ? box.size.y / Mathf.Abs(lossy.y) : box.size.y,
                Mathf.Abs(lossy.z) > 1e-5f ? box.size.z / Mathf.Abs(lossy.z) : box.size.z);

            size = world;
            float scale = Mathf.Abs(lossy[flat]);
            size[flat] = scale > 1e-5f ? Thickness / scale : Thickness;

            collider.size = size;
            collider.center = go.transform.InverseTransformPoint(box.center);
            collider.isTrigger = false;
        }

        static void Mark(GameObject go)
        {
            SimCollider marker = go.GetComponent<SimCollider>();
            if (marker == null) marker = Undo.AddComponent<SimCollider>(go);
            if (marker == null) return;
            if (marker.kind == SimColliderKind.Solid) return;

            Undo.RecordObject(marker, "Close the arena borders");
            marker.kind = SimColliderKind.Solid;
        }
    }
}

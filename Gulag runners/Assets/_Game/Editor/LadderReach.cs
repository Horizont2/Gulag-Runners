using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Makes a ladder marker reach the floor it is climbed FROM.
    ///
    /// Reported as "I can still jump onto the next floor's ladder through the ceiling in
    /// Egypt", and the measurement turned the complaint inside out. Arena_Egypt's five
    /// ladder markers are 0.80 x 0.62 m stubs that only straddle the lip of the floor they
    /// land on, and its upper slab runs y 3.03..3.27 across the WHOLE arena with no hole in
    /// it. So from the storey at y 1.12 a body tops out with its head against that slab at
    /// 3.02 -- seven centimetres into a ladder that starts at 2.94. That graze used to be a
    /// grab, and the climb then went up through solid stone, because a ladder that runs
    /// through a solid turns that solid into a hole (PlayerMotor, "runs through").
    ///
    /// The grab was tightened to need a quarter of a body, which stops the graze. But that
    /// only removed the illegitimate way up, and there was never a legitimate one: with
    /// stubs, NOTHING connects 1.12 to 3.27. The arena was climbable only through its own
    /// bug.
    ///
    /// Arena_Forest is the proof that this is authoring and not a rule: LadderSetup built
    /// its ladders from St.fbx and St2.fbx and they measure 1.79 m and 3.00 m -- foot on the
    /// floor you leave, head above the floor you reach. Egypt and Mine never went through
    /// that tool, so their markers are whatever the prop's own mesh bounds happened to be.
    ///
    /// So this grows the marker DOWN to the top of the floor underneath it. Down to that
    /// top exactly, and never past it: a solid counts as a hole only when its underside
    /// lies inside the ladder's span, so stopping flush with the floor leaves the floor you
    /// are standing on solid while the slab overhead becomes the hatch it was always drawn
    /// as. One metre of overlap where there were seven centimetres, and the quarter-body
    /// grab now has something real to take hold of.
    ///
    /// THE ART NEVER MOVES. The box is a BoxCollider set to trigger -- measured, never
    /// bumped into -- exactly as LadderSetup makes one, and the transform is not touched.
    ///
    /// It only touches ladders that fall short, so running it twice does nothing the second
    /// time, and it says what it did to each one. A ladder with no floor under it at all is
    /// left alone and named: that is a ladder placed in mid air, which is a different
    /// mistake and not one to paper over by stretching it into the void.
    /// </summary>
    public static class LadderReach
    {
        /// <summary>
        /// A foot this close to the floor is already standing on it. Loose on purpose: it
        /// decides whether to act, and nothing is ever snapped to it.
        /// </summary>
        const float Standing = 0.10f;

        /// <summary>
        /// Narrower than this is a prop, not a storey. The same line ReachInDepth draws,
        /// and for the same reason: a crate under a ladder is not the floor it is climbed
        /// from, and stopping the ladder on one would leave it short of the real floor.
        /// </summary>
        const float StoreyWidth = 1.0f;

        /// <summary>
        /// Further down than this and the thing below is not the floor this ladder serves.
        /// Two storeys of this game measure 2.15 m, so three of them is generous while
        /// still refusing to stretch a ladder the height of the arena.
        /// </summary>
        const float TooFar = 6.5f;

        [MenuItem("Tools/Gulag Runners/Setup/Make the ladders reach the floor below")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            List<SimCollider> all = new List<SimCollider>(
                Object.FindObjectsByType<SimCollider>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None));

            List<SimCollider> ladders = all.FindAll(c => c.kind == SimColliderKind.Ladder);
            if (ladders.Count == 0)
            {
                Debug.LogWarning($"{scene.name} has no ladder markers in it, so there is "
                                 + "nothing to lengthen.");
                return;
            }

            List<string> grown = new List<string>();
            List<string> already = new List<string>();
            List<string> midair = new List<string>();
            List<string> refused = new List<string>();

            foreach (SimCollider ladder in ladders)
            {
                Bounds was = ladder.WorldBounds;

                float floor = FloorUnder(was, all, out string floorName);
                if (float.IsNaN(floor))
                {
                    midair.Add($"{ladder.name} (y {was.min.y:0.00}..{was.max.y:0.00} at x "
                               + $"{was.center.x:0.00})");
                    continue;
                }

                if (was.min.y <= floor + Standing)
                {
                    already.Add(ladder.name);
                    continue;
                }

                // Down to the floor's top, not through it. The floor underfoot has to stay
                // solid; only the slab the ladder passes through becomes the hole.
                Bounds want = was;
                want.min = new Vector3(was.min.x, floor, was.min.z);
                want.max = was.max;

                if (!Fit(ladder, want, out string why))
                {
                    refused.Add($"{ladder.name}: {why}");
                    continue;
                }

                Bounds now = ladder.WorldBounds;
                grown.Add($"{ladder.name}: y {was.min.y:0.00}..{was.max.y:0.00} -> "
                          + $"{now.min.y:0.00}..{now.max.y:0.00}, down onto {floorName} "
                          + $"at y {floor:0.00}");
            }

            EditorSceneManager.MarkSceneDirty(scene);

            string message =
                $"{scene.name}: {ladders.Count} ladder marker(s).\n"
                + $"  {grown.Count} lengthened down to the floor they are climbed from:\n"
                + (grown.Count > 0 ? "    " + string.Join("\n    ", grown) + "\n" : "")
                + $"  {already.Count} already stood on their floor and were left alone.\n";

            if (midair.Count > 0)
                message += "  " + midair.Count + " have no floor beneath them at all and "
                           + "were left alone -- check these by hand:\n    "
                           + string.Join("\n    ", midair) + "\n";

            if (refused.Count > 0)
            {
                message += "  " + refused.Count + " could not be resized:\n    "
                           + string.Join("\n    ", refused);
                Debug.LogWarning(message);
                return;
            }

            Debug.Log(message);
        }

        /// <summary>
        /// The top of the storey under this ladder: the highest solid top that is below the
        /// ladder's foot and wide enough to be a floor, overlapping it across X.
        ///
        /// NaN when there is none within reach, which is a ladder hanging in mid air.
        /// </summary>
        static float FloorUnder(Bounds ladder, List<SimCollider> all, out string name)
        {
            float best = float.NaN;
            name = null;

            foreach (SimCollider other in all)
            {
                if (other.kind != SimColliderKind.Solid
                    && other.kind != SimColliderKind.OneWay) continue;

                Bounds b = other.WorldBounds;
                if (b.size.x < StoreyWidth) continue;
                if (b.max.x <= ladder.min.x || b.min.x >= ladder.max.x) continue;

                // Strictly below the foot, or level with it. A slab the ladder already
                // runs through is not the floor it is climbed from.
                if (b.max.y > ladder.min.y + Standing) continue;
                if (ladder.min.y - b.max.y > TooFar) continue;

                if (float.IsNaN(best) || b.max.y > best)
                {
                    best = b.max.y;
                    name = other.name;
                }
            }
            return best;
        }

        /// <summary>
        /// Makes the marker's box be <paramref name="want"/> in world space, by way of a
        /// trigger BoxCollider on the marker itself, and then MEASURES what it got.
        ///
        /// The measurement is the point. SimCollider.WorldBounds asks the object for a
        /// Collider and falls back to the transform's own box, so what a box ends up
        /// meaning depends on what else is on the object -- a mesh collider already there,
        /// a rotated parent, a pivot that is not the centre. Rather than reason about every
        /// one of those, this writes the box, reads the marker back the way the bake will
        /// read it, and refuses the ladder by name if the answer is not what was asked for.
        /// </summary>
        static bool Fit(SimCollider marker, Bounds want, out string why)
        {
            why = null;
            GameObject go = marker.gameObject;

            Collider existing = go.GetComponent<Collider>();
            if (existing != null && !(existing is BoxCollider))
            {
                why = $"it carries a {existing.GetType().Name}, and the bake would have to "
                      + "choose between that and a new box";
                return false;
            }

            BoxCollider box = existing as BoxCollider;
            if (box == null) box = Undo.AddComponent<BoxCollider>(go);
            if (box == null) { why = "a BoxCollider could not be added"; return false; }

            // World corners into the marker's own space, so a turned or scaled transform
            // still gets a box that lands where it was asked to.
            Transform t = go.transform;
            Bounds local = new Bounds();
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3(
                    (c & 1) == 0 ? want.min.x : want.max.x,
                    (c & 2) == 0 ? want.min.y : want.max.y,
                    (c & 4) == 0 ? want.min.z : want.max.z);
                Vector3 p = t.InverseTransformPoint(corner);
                if (c == 0) local = new Bounds(p, Vector3.zero);
                else local.Encapsulate(p);
            }

            Undo.RecordObject(box, "Make the ladders reach the floor below");
            box.center = local.center;
            box.size = local.size;
            // Climbed, never bumped into: the simulation does its own collision and only
            // ever measures this box. The same choice LadderSetup makes.
            box.isTrigger = true;
            EditorUtility.SetDirty(box);
            PrefabUtility.RecordPrefabInstancePropertyModifications(box);

            Bounds got = marker.WorldBounds;
            if (Mathf.Abs(got.min.y - want.min.y) > 0.02f
                || Mathf.Abs(got.max.y - want.max.y) > 0.02f)
            {
                why = $"asked for y {want.min.y:0.00}..{want.max.y:0.00} but the marker "
                      + $"reads back y {got.min.y:0.00}..{got.max.y:0.00}";
                return false;
            }

            return true;
        }
    }
}

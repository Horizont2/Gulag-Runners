using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Brings a platform that hangs in its own slice of depth back within reach of the
    /// fight, by widening its COLLISION box forward. The art never moves.
    ///
    /// Reported as "standing on the hanging cage in the forest, the fighter still falls
    /// through it", and measured rather than guessed: Arena_Forest's two Cage_1_Black hang
    /// at z -0.63 while a fighter fights at -3.16. Their boxes span z -0.97 to -0.29. The
    /// body, half a metre wide in depth, spans -3.31 to -3.01. The two never touch, so as
    /// far as the simulation is concerned there is nothing underfoot at all -- the cage is
    /// not thin, or slippery, or mis-sized. It is somewhere else.
    ///
    /// WHY THE ARENA'S REAL FLOORS DO NOT HAVE THIS PROBLEM, which is the whole shape of
    /// the fix: they are DEEP. The upper gallery is modelled z -2.63 to +1.78, four and a
    /// half metres of it, so whatever lane a body is on it finds floor under its feet and
    /// TryFloorLane keeps it where it is. A prop dropped in from a pack is as deep as the
    /// prop, which here is 68 cm, and 68 cm placed two and a half metres behind the fight
    /// is a platform in another room.
    ///
    /// So the box grows FORWARD -- its far edge stays exactly where the artist put it, and
    /// its near edge comes out to meet the nearest lane a fighter can actually be on, plus
    /// a body's half-depth and a little margin. Nothing visible changes: SimCollider is an
    /// authoring box, not the model.
    ///
    /// It only touches boxes that are UNREACHABLE. A platform already within reach is left
    /// alone, so running this twice does nothing the second time.
    /// </summary>
    public static class ReachInDepth
    {
        // A little more than a body's half-depth, so landing on the lip still registers
        // rather than depending on the last millimetre.
        const float Margin = 0.25f;

        [MenuItem("Tools/Gulag Runners/Setup/Bring hanging platforms into reach")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            PlayerController fighter = Object.FindFirstObjectByType<PlayerController>(
                FindObjectsInactive.Include);
            if (fighter == null)
            {
                Debug.LogWarning($"{scene.name} has no fighter in it, so there is no way to "
                                 + "tell which depths the fight happens on.");
                return;
            }

            float plane = fighter.planeZ;
            float halfBody = fighter.tuning != null
                ? Mathf.Max(0.05f, fighter.tuning.bodyDepth * 0.5f) : 0.15f;

            // Every lane a body can be on: the near face of each storey, plus half a body.
            // Taken from the deep boxes themselves rather than from a list, so a scene that
            // gains a storey is measured and not forgotten.
            List<float> lanes = Lanes(plane, halfBody);

            List<string> moved = new List<string>();
            List<string> fine = new List<string>();

            foreach (SimCollider box in Object.FindObjectsByType<SimCollider>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (box.kind != SimColliderKind.Solid
                    && box.kind != SimColliderKind.OneWay) continue;

                Bounds b = box.WorldBox;
                if (b.size.z <= 0.001f) continue;

                float near = b.min.z, far = b.max.z;

                // Can any lane a body can stand on reach this box as it is?
                bool reachable = false;
                float nearestLane = plane;
                float bestGap = float.MaxValue;
                foreach (float lane in lanes)
                {
                    if (lane + halfBody > near && lane - halfBody < far) { reachable = true; break; }
                    float gap = lane < near ? near - (lane + halfBody) : (lane - halfBody) - far;
                    if (gap < bestGap) { bestGap = gap; nearestLane = lane; }
                }
                if (reachable) { fine.Add(box.name); continue; }

                // Only ever grow TOWARDS the fight. A box that sits in FRONT of every lane
                // is a different mistake and widening it backwards would hide it.
                if (nearestLane > near)
                {
                    Debug.LogWarning($"{box.name} sits in front of every lane a body can be "
                                     + $"on (box z {near:0.00}..{far:0.00}, nearest lane "
                                     + $"{nearestLane:0.00}). Left alone: that is a prop "
                                     + "placed too close to the camera, not too far from it.");
                    continue;
                }

                float wantNear = nearestLane - halfBody - Margin;
                float grow = near - wantNear;

                Transform t = box.transform;
                Undo.RecordObject(t, "Bring hanging platforms into reach");

                // The far edge must not move, so the box grows by `grow` and its centre
                // slides forward by half of that.
                Vector3 scale = t.localScale;
                float perUnit = b.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
                scale.z += grow / Mathf.Max(0.0001f, perUnit) * Mathf.Sign(scale.z == 0 ? 1 : scale.z);
                t.localScale = scale;

                Vector3 at = t.position;
                at.z -= grow * 0.5f;
                t.position = at;

                EditorUtility.SetDirty(t);
                moved.Add($"{box.name} (z {near:0.00}..{far:0.00} -> {wantNear:0.00}..{far:0.00})");
            }

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"{scene.name}: the fight happens at z {plane:0.00}, on lane(s) "
                + string.Join(", ", lanes.ConvertAll(l => l.ToString("0.00")))
                + $".\n  {moved.Count} platform(s) widened forward so a body can land on "
                + "them; their art did not move.\n"
                + (moved.Count > 0 ? "    " + string.Join("\n    ", moved) + "\n" : "")
                + $"  {fine.Count} were already within reach and were left alone.");
        }

        /// <summary>
        /// The depths a body can actually be standing at: for every box deep enough to be a
        /// storey, its near face plus half a body.
        ///
        /// A storey here is anything at least a metre deep. That is the line between a
        /// FLOOR -- modelled deep on purpose so a body finds it from any lane -- and a prop,
        /// which is as deep as the prop happens to be.
        /// </summary>
        static List<float> Lanes(float plane, float halfBody)
        {
            List<float> lanes = new List<float> { plane };

            foreach (SimCollider box in Object.FindObjectsByType<SimCollider>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (box.kind != SimColliderKind.Solid
                    && box.kind != SimColliderKind.OneWay) continue;

                Bounds b = box.WorldBox;
                if (b.size.z < 1f) continue;              // a prop, not a storey
                if (b.size.x < 2f) continue;              // nor a post

                float lane = b.min.z + halfBody;
                if (!lanes.Exists(l => Mathf.Abs(l - lane) < 0.2f)) lanes.Add(lane);
            }

            lanes.Sort();
            return lanes;
        }
    }
}

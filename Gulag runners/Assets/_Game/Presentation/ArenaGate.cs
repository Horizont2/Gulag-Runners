using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Marks a piece of the arena as a steel shutter: collision that is not there until the
    /// closing phase of docs/01 reaches it.
    ///
    /// Put this on the object whose <see cref="SimCollider"/> boxes are the shutter — itself,
    /// or a parent of several of them when a doorway takes more than one box. The baker reads
    /// the gates in the scene, sorts them, and tags every solid underneath each one with its
    /// number; from then on the match clock decides whether those boxes count.
    ///
    /// The shutter must be a wall nobody can get over or round, or the closing phase herds
    /// nobody. In practice that means taller than a fighter can jump from the HIGHEST floor it
    /// crosses, and deep enough to cover every lane the room has. The gizmo draws the box so
    /// that is easy to see, and <see cref="GateView"/> is what makes it move.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaGate : MonoBehaviour
    {
        [Header("When it shuts (docs/01)")]
        [Tooltip("Which beat of the last minute this gate belongs to. Closing gates go one " +
                 "after another across the shutter window; sudden death gates all go on its " +
                 "first tick — that is the ceiling of the central hall coming down.")]
        public GateWhen when = GateWhen.Closing;

        [Tooltip("Shut order among the gates of the same beat. 0 goes first. Gates sharing a " +
                 "number keep the order the scene lists them in, which is fragile — give the " +
                 "outer rooms low numbers and work inwards.")]
        public int order;

        [Header("Which way it herds")]
        [Tooltip("Where a fighter caught behind this shutter is brought when it slams. This is " +
                 "what keeps the closing phase a herding rather than an accident: a shutter " +
                 "that only becomes solid can seal somebody into a wing with nobody to fight, " +
                 "and the match then ends on the clock in two empty rooms. Point it at the " +
                 "middle of the arena.")]
        public GateToward toward = GateToward.PlusX;

        /// <summary>
        /// Which gate this became in the baked world, or -1 before the first bake. The view
        /// needs it to ask the clock when its own shutter is due.
        /// </summary>
        public int Index { get; private set; } = -1;

        /// <summary>The baker's answer. Nothing else may call it.</summary>
        public void Baked(int index) => Index = index;

        /// <summary>The boxes this gate owns, for the gizmo and for the view's travel.</summary>
        public Bounds Box(out bool any)
        {
            SimCollider[] marked = GetComponentsInChildren<SimCollider>(true);
            Bounds box = default;
            any = false;

            foreach (SimCollider c in marked)
            {
                if (c.kind != SimColliderKind.Solid) continue;
                if (!any) { box = c.WorldBounds; any = true; }
                else box.Encapsulate(c.WorldBounds);
            }

            if (any) return box;

            // No markers yet: fall back to whatever colliders are here, so the gizmo is useful
            // while the shutter is still being placed.
            foreach (Collider col in GetComponentsInChildren<Collider>(true))
            {
                if (!any) { box = col.bounds; any = true; }
                else box.Encapsulate(col.bounds);
            }

            return box;
        }

        void OnDrawGizmos()
        {
            Bounds box = Box(out bool any);
            if (!any) return;

            Color shut = when == GateWhen.SuddenDeath
                ? new Color(1f, 0.35f, 0.2f, 1f)
                : new Color(0.55f, 0.75f, 1f, 1f);

            Gizmos.color = new Color(shut.r, shut.g, shut.b, 0.14f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = shut;
            Gizmos.DrawWireCube(box.center, box.size);

            // The arrow is the important half: it says which way somebody behind this gets
            // brought, and an arrow pointing out of the arena is the authoring mistake that
            // shoves a fighter into the void.
            Vector3 dir = toward switch
            {
                GateToward.MinusX => Vector3.left,
                GateToward.PlusX => Vector3.right,
                GateToward.MinusY => Vector3.down,
                _ => Vector3.up
            };

            float reach = Mathf.Max(0.8f, box.size.magnitude * 0.3f);
            Vector3 from = box.center;
            Vector3 to = from + dir * reach;
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireSphere(to, 0.1f);
        }
    }
}

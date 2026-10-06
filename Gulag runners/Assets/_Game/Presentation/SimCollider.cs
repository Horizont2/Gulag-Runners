using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    public enum SimColliderKind
    {
        /// <summary>Blocks from every side: walls, floors, ceilings.</summary>
        Solid = 0,
        /// <summary>Jump up through it, drop down from it with down + jump.</summary>
        OneWay = 1,
        /// <summary>A ladder or hatch: the connection between floors (docs/05).</summary>
        Ladder = 2,

        /// <summary>
        /// Scenery. Not baked at all.
        ///
        /// A level built in 3D is full of things that are solid to look at and nothing to walk
        /// into: handrails, trim, a beam two metres behind the plane. Flattened onto one plane
        /// they become walls and ceilings nobody can see, and the first symptom is a player who
        /// cannot stand up somewhere that looks empty.
        /// </summary>
        Ignore = 3
    }

    /// <summary>
    /// Marks a box in the scene as part of the simulated arena.
    ///
    /// The simulation does its own collision and never touches Unity physics: PhysX is not
    /// deterministic across platforms, which would break rollback (docs/06). So the scene is
    /// only the authoring surface — these boxes are baked into a <see cref="SimWorld"/> once.
    ///
    /// Size comes from a BoxCollider if there is one, otherwise from the transform scale.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SimCollider : MonoBehaviour
    {
        [Tooltip("Solid blocks from every side. One-way can be jumped through from below and " +
                 "dropped from with down + jump. Ladder connects floors.")]
        public SimColliderKind kind = SimColliderKind.Solid;

        public Rect ToRect() => WorldRect(this, GetComponent<Collider>());

        /// <summary>
        /// World-space X/Y footprint of a marked object.
        ///
        /// Taken from the collider's own world bounds when there is one, because lossyScale is
        /// not rotation-aware: under a parent turned ninety degrees — which is how a level laid
        /// out along Z is made to run along X — it hands back the depth as the width.
        /// </summary>
        public static Rect WorldRect(Component owner, Collider collider)
        {
            Bounds b = collider != null
                ? collider.bounds
                : RotatedBounds(owner.transform);

            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }

        /// <summary>A unit cube at this transform, turned and scaled the way the transform is.</summary>
        static Bounds RotatedBounds(Transform t)
        {
            Vector3 s = t.lossyScale * 0.5f;
            Vector3 x = t.right * s.x, y = t.up * s.y, z = t.forward * s.z;
            Vector3 extents = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(t.position, extents * 2f);
        }

        public Aabb ToAabb() => RectToAabb(ToRect());

        /// <summary>
        /// Converts a world-space rect to simulation space. Rounded to millimetres on purpose:
        /// the simulation is integer-only, and quantising here means two machines reading the
        /// same scene produce the same world (docs/06).
        /// </summary>
        public static Aabb RectToAabb(Rect r) => new Aabb(
            Fix.FromMilli(Mathf.RoundToInt(r.xMin * 1000f)),
            Fix.FromMilli(Mathf.RoundToInt(r.yMin * 1000f)),
            Fix.FromMilli(Mathf.RoundToInt(r.xMax * 1000f)),
            Fix.FromMilli(Mathf.RoundToInt(r.yMax * 1000f)));

        void OnDrawGizmos()
        {
            Rect r = ToRect();
            Gizmos.color = kind switch
            {
                SimColliderKind.Solid => new Color(0.2f, 0.9f, 1f, 0.9f),
                SimColliderKind.OneWay => new Color(1f, 0.85f, 0.2f, 0.9f),
                SimColliderKind.Ladder => new Color(0.4f, 1f, 0.4f, 0.9f),
                _ => new Color(0.5f, 0.5f, 0.5f, 0.35f)
            };
            Gizmos.DrawWireCube(new Vector3(r.center.x, r.center.y, transform.position.z),
                                new Vector3(r.width, r.height, 0.05f));
        }
    }
}

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
        Ladder = 2
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

        public Rect ToRect()
        {
            Bounds b;
            BoxCollider box = GetComponent<BoxCollider>();
            if (box != null)
            {
                Vector3 centre = transform.TransformPoint(box.center);
                Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
                b = new Bounds(centre, size);
            }
            else
            {
                b = new Bounds(transform.position, transform.lossyScale);
            }

            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
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
                _ => new Color(0.4f, 1f, 0.4f, 0.9f)
            };
            Gizmos.DrawWireCube(new Vector3(r.center.x, r.center.y, transform.position.z),
                                new Vector3(r.width, r.height, 0.05f));
        }
    }
}

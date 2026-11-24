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

        /// <summary>
        /// The box this marker stands for, in world space: the collider's own bounds when
        /// there is one, and the transform's own box when there is not. A level can be marked
        /// up with no Unity colliders at all, which is the cheaper way to author one.
        /// </summary>
        public Bounds WorldBounds => WorldBox(transform, GetComponent<Collider>());

        /// <summary>
        /// The box this object occupies in its OWN space: the BoxCollider's if it has one, the
        /// mesh's own bounds if it has a mesh, a unit cube when it has neither.
        ///
        /// Asking the mesh is the whole point. This used to assume a unit cube at the pivot for
        /// anything without a BoxCollider, which is right for Unity's Cube and wrong for
        /// everything else: Unity's own Cylinder and Capsule meshes are two units tall, so each
        /// of the pillars in this location baked a box half its visible height, and any
        /// modelled piece whose pivot is not its centre baked a box beside itself. Invisible
        /// collision that does not line up with the art is the worst bug this project can
        /// have, because the scene view says everything is fine.
        /// </summary>
        public static Bounds LocalBox(Component owner, Collider collider)
        {
            if (collider is BoxCollider box) return new Bounds(box.center, box.size);

            if (collider is SphereCollider ball)
                return new Bounds(ball.center, Vector3.one * (ball.radius * 2f));

            if (collider is CapsuleCollider capsule)
            {
                float d = capsule.radius * 2f;
                Vector3 size = new Vector3(d, d, d);
                // 0, 1, 2 is x, y, z -- and the height is the WHOLE capsule including both
                // caps, so it is never less than the diameter.
                float along = Mathf.Max(capsule.height, d);
                if (capsule.direction == 0) size.x = along;
                else if (capsule.direction == 2) size.z = along;
                else size.y = along;
                return new Bounds(capsule.center, size);
            }

            MeshFilter mf = owner != null ? owner.GetComponent<MeshFilter>() : null;
            if (mf != null && mf.sharedMesh != null) return mf.sharedMesh.bounds;

            return new Bounds(Vector3.zero, Vector3.one);
        }

        public Rect ToRect()
        {
            Bounds b = WorldBounds;
            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }

        /// <summary>
        /// The Z this collider was modelled at. The simulation is flat and never asks;
        /// presentation does, to draw a body on the same plane as the thing it is touching.
        /// </summary>
        public float Depth => WorldBounds.center.z;

        /// <summary>
        /// World-space X/Y footprint of a marked object.
        /// </summary>
        public static Rect WorldRect(Component owner, Collider collider)
        {
            Bounds b = WorldBox(owner != null ? owner.transform : null, collider);
            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }

        /// <summary>
        /// Where this object's box is in the world, WITHOUT asking the physics engine.
        ///
        /// THIS USED TO BE Collider.bounds WHEN THERE WAS A COLLIDER, and that is why a
        /// build did not agree with the editor.
        ///
        /// Collider.bounds is not geometry, it is a number PhysX holds, computed from the
        /// last transform it was told about -- and this project ships with
        /// m_AutoSyncTransforms: 0 in DynamicsManager, so it is told about transforms
        /// when the simulation steps and at no other time. SimWorldBaker bakes in Awake,
        /// at execution order -100, which is before the first physics step has ever run.
        /// In the editor that is harmless: the editor keeps the physics scene in step
        /// with the scene you are looking at, so by the time you press Play every
        /// collider's bounds are already right. In a player there is no editor doing
        /// that, and the first thing that reads Collider.bounds reads a box that has
        /// never been moved, rotated or scaled -- a unit box at the origin for anything
        /// built out of a prefab.
        ///
        /// The symptoms are the ones you cannot debug from a screenshot: invisible walls
        /// in the middle of an arena where nothing is drawn, floors that are not there, a
        /// fighter pushed out of a solid he is standing inside and landing behind the
        /// level. All of it in the build and none of it in the editor, which makes the
        /// build look like a different game.
        ///
        /// So it is computed here instead, from the collider's own local box and the
        /// transform -- the same arithmetic PhysX would do, done where this project can
        /// see it. The simulation's whole premise is that Unity colliders are an
        /// authoring surface and nothing more (docs/06); asking the physics engine to
        /// agree about anything was the one place that premise was broken.
        /// </summary>
        public static Bounds WorldBox(Transform t, Collider collider)
        {
            if (t == null) return new Bounds(Vector3.zero, Vector3.one);

            Bounds local = LocalBox(t, collider);
            Vector3 s = Vector3.Scale(local.extents, t.lossyScale);
            Vector3 x = t.right * s.x, y = t.up * s.y, z = t.forward * s.z;
            Vector3 extents = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));

            // TransformPoint, not t.position: a mesh whose pivot is not its centre sits beside
            // its own transform, and the box has to sit on the mesh.
            return new Bounds(t.TransformPoint(local.center), extents * 2f);
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

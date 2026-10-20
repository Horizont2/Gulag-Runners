using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// A wall that closes the arena by growing into it.
    ///
    /// Place the box where it should END — filling everything from the arena's edge to the
    /// face of the last room. That whole box is what it becomes; at the start of the match
    /// it has no thickness at all. The edge it is anchored to is the one opposite the way it
    /// grows, so a wall growing towards +X is pinned at its own left side.
    ///
    /// Authoring the END rather than the start is deliberate twice over: the inner face is
    /// the number that decides how the match finishes, so it is the one worth being able to
    /// see and drag — and a wall that grows has no "start" to place, only a thickness of
    /// nothing.
    ///
    /// One or two of these, never a row. Two from the sides is the arena closing in; one
    /// from above is the ceiling coming down. A row of them is a corridor of plates, which
    /// is a different game and was the first two attempts at this.
    ///
    /// Collision and picture are the same function in the same tick. No interpolation and no
    /// easing here on purpose: an eased picture and a linear collision disagree in the
    /// middle, and the disagreement is a fighter crushed by nothing.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]        // after the match has stepped, before the camera
    public sealed class ClosingWall : MonoBehaviour
    {
        [Tooltip("Leave empty to use the baker in the scene. The wall's closeness is read " +
                 "from the world the simulation collides against, so the picture and the " +
                 "collision are the same number and cannot drift apart.")]
        public SimWorldBaker worldBaker;

        [Tooltip("Which way the wall's inner face advances — and so which way a fighter " +
                 "caught by it is pushed.\\n\\n" +
                 "A wall on the LEFT of the arena grows Plus X. One on the right grows " +
                 "Minus X. A ceiling grows Minus Y. Get this wrong and the wall grows out of " +
                 "the arena instead of into it, and shoves anybody it catches the same way.")]
        public GateToward grows = GateToward.PlusX;

        [Tooltip("What is scaled. Leave empty to scale this object, which is the usual " +
                 "answer — the collision is read from this object's own box.\\n\\n" +
                 "Set it to a child when the wall has art that must not stretch: the child " +
                 "scales, the marker does not.")]
        public Transform art;

        [Tooltip("Draw the finished wall in the scene view.")]
        public bool drawGizmo = true;

        /// <summary>The box at full close, in world space, from this object.</summary>
        public Bounds Shut
        {
            get
            {
                Collider c = GetComponent<Collider>();
                if (c != null) return c.bounds;

                Renderer r = GetComponent<Renderer>();
                if (r != null) return r.bounds;

                return new Bounds(transform.position, transform.lossyScale);
            }
        }

        /// <summary>This wall as the simulation holds it. Called by the baker, once.</summary>
        public WallDef ToDef()
        {
            Measure();
            Bounds b = _shut;
            return new WallDef
            {
                Box = SimCollider.RectToAabb(new Rect(b.min.x, b.min.y, b.size.x, b.size.y)),
                Span = new Span(ToFix(b.min.z), ToFix(b.max.z)),
                Grows = grows
            };
        }

        Bounds _shut;
        Vector3 _scale;
        bool _measured;

        void Awake() => Measure();

        /// <summary>
        /// Remembers the finished wall before anything shrinks it.
        ///
        /// Separate from Awake because the baker runs at -100 and may ask first; measuring
        /// again after the art has started growing would record a part-grown wall as the
        /// finished one, and it would then close to somewhere short of where it belongs.
        /// </summary>
        public void Measure()
        {
            if (_measured) return;
            _shut = Shut;
            _scale = (art != null ? art : transform).localScale;
            _measured = true;
        }

        void LateUpdate()
        {
            Measure();
            if (worldBaker == null) worldBaker = SimWorldBaker.Instance;

            float p = Mathf.Clamp01(Progress());
            Transform moved = art != null ? art : transform;

            // Scaled from the anchored edge, which is why the position moves as well: Unity
            // scales about the pivot, and the pivot is the middle.
            bool vertical = grows == GateToward.MinusY || grows == GateToward.PlusY;
            Vector3 scale = _scale;
            Vector3 centre = _shut.center;

            if (vertical)
            {
                scale.y = _scale.y * p;
                float half = _shut.extents.y * p;
                centre.y = grows == GateToward.MinusY ? _shut.max.y - half : _shut.min.y + half;
            }
            else
            {
                scale.x = _scale.x * p;
                float half = _shut.extents.x * p;
                centre.x = grows == GateToward.PlusX ? _shut.min.x + half : _shut.max.x - half;
            }

            moved.localScale = scale;
            moved.position = centre;
        }

        /// <summary>
        /// How shut the wall is, from the same clock the simulation reads. Outside play it is
        /// nothing, so the scene view shows the finished wall rather than a part-grown one.
        /// </summary>
        public float Progress()
        {
            if (!Application.isPlaying) return 1f;
            SimWorld world = worldBaker != null ? worldBaker.World : null;
            return world != null ? world.WallProgress.ToMilli() / 1000f : 0f;
        }

        static Fix ToFix(float v) => Fix.FromMilli(Mathf.RoundToInt(v * 1000f));

        void OnDrawGizmos()
        {
            if (!drawGizmo) return;

            Bounds b = Application.isPlaying && _measured ? _shut : Shut;

            Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(b.center, b.size);

            // Which way it eats. An arrow from the anchored edge to the finished face.
            Vector3 from = b.center, to = b.center;
            switch (grows)
            {
                case GateToward.PlusX:  from.x = b.min.x; to.x = b.max.x; break;
                case GateToward.MinusX: from.x = b.max.x; to.x = b.min.x; break;
                case GateToward.MinusY: from.y = b.max.y; to.y = b.min.y; break;
                default:                from.y = b.min.y; to.y = b.max.y; break;
            }
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 1f);
            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireSphere(to, 0.25f);
        }
    }
}

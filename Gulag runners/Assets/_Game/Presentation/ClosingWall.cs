using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// A wall that closes the arena by moving.
    ///
    /// Put it on the box itself, and place that box where the wall should FINISH — the shut
    /// position, the edge of the last room. Travel then says where it comes from. Authoring
    /// it at the end rather than the start is deliberate: the end is the number that decides
    /// how the match finishes, so it is the one you want to be able to see and drag.
    ///
    /// There are meant to be one or two of these and no more. Two from the sides is the arena
    /// closing in; one from above is the ceiling coming down. A row of them is a corridor of
    /// plates, which is a different game.
    ///
    /// Collision and picture are the same function, evaluated in the same tick: the body is
    /// pushed by <see cref="SimWorld.WallAt"/>, and this moves the art to exactly that. There
    /// is no interpolation and no easing here on purpose — an eased wall and a linear
    /// collision disagree in the middle, and the disagreement is a fighter crushed by nothing.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]        // after the match has stepped, before the camera
    public sealed class ClosingWall : MonoBehaviour
    {
        [Tooltip("Leave empty to use the baker in the scene. The wall's closeness is read " +
                 "from the world the simulation collides against, so the picture and the " +
                 "collision are the same number and cannot drift apart.")]
        public SimWorldBaker worldBaker;

        [Tooltip("How far the wall travels to reach where it now stands, in metres, and which " +
                 "way.\\n\\n" +
                 "Negative on X is a wall that comes in from the right; positive comes from " +
                 "the left. On Y, positive is a ceiling coming down from above. The sign is " +
                 "also what tells the shove which way to herd a fighter it catches, so a sign " +
                 "the wrong way round posts them out into the dead ground behind it.")]
        public float travel = -14f;

        [Tooltip("Travel along Y instead of X: a ceiling rather than a side wall.")]
        public bool vertical;

        [Tooltip("What moves. Leave empty to move this object itself, which is the usual " +
                 "answer — the collision is read from this object's own box.")]
        public Transform art;

        [Tooltip("Draw the open and shut positions in the scene view.")]
        public bool drawGizmo = true;

        /// <summary>Where this wall ends up, in world space, from its own box.</summary>
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
            // The box is measured where the object stands, which is the SHUT position, so the
            // definition needs no second transform to describe itself.
            Bounds b = _shut;
            return new WallDef
            {
                Box = SimCollider.RectToAabb(new Rect(b.min.x, b.min.y, b.size.x, b.size.y)),
                Span = new Span(ToFix(b.min.z), ToFix(b.max.z)),
                Travel = ToFix(travel),
                Vertical = vertical
            };
        }

        Bounds _shut;
        Vector3 _home;
        bool _measured;

        void Awake() => Measure();

        /// <summary>
        /// Remembers where the wall belongs before anything moves it.
        ///
        /// Separate from Awake because the baker runs at -100 and may ask first; measuring
        /// twice after the art has started sliding would record the slid position as home,
        /// and the wall would then close to somewhere it has already been.
        /// </summary>
        public void Measure()
        {
            if (_measured) return;
            _shut = Shut;
            _home = (art != null ? art : transform).position;
            _measured = true;
        }

        void LateUpdate()
        {
            Measure();
            if (worldBaker == null) worldBaker = SimWorldBaker.Instance;

            Transform moved = art != null ? art : transform;
            float back = travel - travel * Progress();

            moved.position = _home + (vertical ? new Vector3(0f, back, 0f)
                                               : new Vector3(back, 0f, 0f));
        }

        /// <summary>
        /// How shut the wall is, from the same clock the simulation reads. Outside play, and
        /// before a match exists, it is open — so the scene view shows where it starts.
        /// </summary>
        public float Progress()
        {
            if (!Application.isPlaying) return 0f;
            SimWorld world = worldBaker != null ? worldBaker.World : null;
            return world != null ? world.WallProgress.ToMilli() / 1000f : 0f;
        }

        static Fix ToFix(float v) => Fix.FromMilli(Mathf.RoundToInt(v * 1000f));

        void OnDrawGizmos()
        {
            if (!drawGizmo) return;

            Bounds b = Application.isPlaying && _measured ? _shut : Shut;
            Vector3 open = b.center - (vertical ? new Vector3(0f, travel, 0f)
                                                : new Vector3(travel, 0f, 0f));

            Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.9f);     // where it ends
            Gizmos.DrawWireCube(b.center, b.size);

            Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.25f);    // where it starts
            Gizmos.DrawWireCube(open, b.size);
            Gizmos.DrawLine(open, b.center);
        }
    }
}

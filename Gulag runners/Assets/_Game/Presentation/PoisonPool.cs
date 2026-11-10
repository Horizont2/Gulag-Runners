using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The green that comes up out of the floor in the last minute, and the one place the
    /// arena says how far up it comes.
    ///
    /// Place the box where the poison ENDS — filling everything from the lowest ground worth
    /// drowning to the height the surface stops at. That whole box is what it becomes; at the
    /// start of the match it has no height at all. Authoring the END rather than the start is
    /// the same decision <see cref="ClosingWall"/> made and for the same reason: the top face
    /// is the number that decides how the match finishes, so it is the one worth being able to
    /// see and drag — and a pool that fills has no "start" to place, only a depth of nothing.
    ///
    /// WHAT THE TOP FACE MEANS, and it is worth getting right. Everything under it is lost
    /// ground by the end of the closing phase, so the pool's top is the arena saying which
    /// platforms the fight finishes on. Set it above every floor and the last twenty seconds
    /// are two fighters drowning side by side, which decides the round by whoever had more
    /// health rather than by the fight. Set it just under the high ground — the cages, the
    /// walkways, the terraces — and the flood herds them up there, which is the whole point:
    /// docs/01 wants the arena to take itself away and leave the two of them somewhere they
    /// cannot avoid each other.
    ///
    /// It is NOT collision, deliberately. Poison needs no boxes, no gate bit and no shove: a
    /// tide is never in anybody's way, it only makes being under it expensive. So give this
    /// object a renderer and no collider, and nothing in the simulation will ever treat it as
    /// a floor you can stand on — which a half-full pool would otherwise read as.
    ///
    /// Collision and picture do not have to agree here because there is no collision, but the
    /// LEVEL and the DAMAGE do, and they are the same number: the surface drawn is
    /// world.PoisonY, straight off the simulation, so what you can see is what bites.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]        // after the match has stepped, before the camera
    public sealed class PoisonPool : MonoBehaviour
    {
        [Tooltip("Leave empty to use the baker in the scene. The level is read from the world " +
                 "the simulation itself reads, so the surface you can see and the surface " +
                 "that bites are the same number and cannot drift apart.")]
        public SimWorldBaker worldBaker;

        [Tooltip("What is scaled. Leave empty to scale this object, which is the usual " +
                 "answer.\n\n" +
                 "Set it to a child when the pool has art that must not stretch — a mist, a " +
                 "light, a rim of weed: the child scales, the marker does not.")]
        public Transform body;

        [Tooltip("Optional. Kept sitting exactly on the surface, at full size, however full " +
                 "the pool is.\n\n" +
                 "This is where the poison actually reads from across the arena: a flat lid " +
                 "with its own brighter material, which is a waterline. Without one the pool " +
                 "is a block of green that is hard to judge the top of — and the top is the " +
                 "only part of it a player has to judge.")]
        public Transform surface;

        [Tooltip("The smoke IN the poison: scaled and placed to fill exactly the part of the " +
                 "pool that has flooded, so its top edge is the waterline and its bottom is " +
                 "the floor of the pool.\n\n" +
                 "Filled rather than floated, and that is the whole difference between a tide " +
                 "and a fog machine. Parked at the surface instead, the band emits across the " +
                 "whole arena from the first tick -- the poison then appears everywhere at " +
                 "once and only the line moves, which is not what rising means. Scaled with " +
                 "the flood, the emitter at the start is a flat slab on the lowest ground: " +
                 "the green comes up out of the floor, because that is where it is.\n\n" +
                 "The particle system has to scale its SHAPE and not its particles for this " +
                 "to work (Scaling Mode: Shape, which GR_PoisonFog is authored with); " +
                 "anything else grows the smoke itself as the pool fills.\n\n" +
                 "This is what the poison LOOKS like. The boxes above are a measurement — " +
                 "hidden the moment the game starts — and a slab of flat green is not a " +
                 "thing anybody wants to look at for eighty seconds; a fog bank creeping up " +
                 "the arena is. Switched on with the flood and off again while the pool is " +
                 "empty, so the first minute has no green in it at all.\n\n" +
                 "Not played or stopped from here: whatever is dropped in keeps its own " +
                 "authored emission, lifetime and colour.")]
        public Transform mist;

        [Tooltip("The smoke BELOW: left exactly where it is authored, at the bottom of the " +
                 "pool, under the arena.\n\n" +
                 "The surface band alone reads as a line that moves. What makes it read as a " +
                 "place filling up is smoke welling out from underneath the floor and rising " +
                 "through it — the source of the thing rather than its edge. Switched on and " +
                 "off with the flood, never moved.")]
        public Transform source;

        [Tooltip("Hide the boxes when the game starts, leaving only the smoke.\n\n" +
                 "The box IS the authoring: it is what you drag to say where the poison stops, " +
                 "and the only thing in the scene that can be measured. It is also a flat green " +
                 "slab, which is not what a rising poison looks like — so it is visible while " +
                 "you build the arena and switched off the moment anybody plays it. Turn this " +
                 "off to see the measured volume in play, which is how you check that the smoke " +
                 "and the damage agree about where the surface is.")]
        public bool hideBoxesInPlay = true;

        [Tooltip("Draw the pool at full flood in the scene view, and the floors it drowns.")]
        public bool drawGizmo = true;

        [Tooltip("Scene view only: how full to DRAW it while nothing is playing, so the " +
                 "finished level can be judged against the arena without entering play mode. " +
                 "It has no effect on the match.")]
        [Range(0f, 1f)] public float preview = 1f;

        /// <summary>
        /// The pool at full flood, in world space: the green box as it was authored.
        ///
        /// The BODY's own renderer first, because that is the box somebody dragged into the
        /// arena and the one they expect the poison to fill — measuring this object instead
        /// would measure an empty group transform, and a pool of nothing is a pool nobody can
        /// see is wrong. Then this object's own collider or renderer, for a pool that is one
        /// object rather than a group. The scale is the last resort, which is what an empty
        /// with nothing on it leaves to go on.
        /// </summary>
        public Bounds Flooded
        {
            get
            {
                // The MESH rather than the renderer, so a hidden box still says where the
                // pool is. Renderer.bounds on a switched-off renderer is not something to
                // rely on, and this one is switched off in every build: the box is the
                // measurement, the smoke is the picture.
                if (body != null)
                {
                    MeshFilter mesh = body.GetComponent<MeshFilter>();
                    if (mesh != null && mesh.sharedMesh != null)
                    {
                        Bounds local = mesh.sharedMesh.bounds;
                        return new Bounds(body.TransformPoint(local.center),
                                          Scaled(local.size, body.lossyScale));
                    }

                    Renderer art = body.GetComponent<Renderer>();
                    if (art != null && art.enabled) return art.bounds;
                }

                Collider c = GetComponent<Collider>();
                if (c != null) return c.bounds;

                Renderer r = GetComponent<Renderer>();
                if (r != null) return r.bounds;

                Transform t = body != null ? body : transform;
                return new Bounds(t.position, Abs(t.lossyScale));
            }
        }

        static Vector3 Scaled(Vector3 size, Vector3 scale) => new Vector3(
            Mathf.Abs(size.x * scale.x), Mathf.Abs(size.y * scale.y), Mathf.Abs(size.z * scale.z));

        static Vector3 Abs(Vector3 v) =>
            new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>This pool as the simulation holds it. Called by the baker, once.</summary>
        public Aabb ToBox()
        {
            Measure();
            Bounds b = _flooded;
            return SimCollider.RectToAabb(new Rect(b.min.x, b.min.y, b.size.x, b.size.y));
        }

        /// <summary>How deep it reaches. A walkway behind the pool stays dry.</summary>
        public Span ToSpan()
        {
            Measure();
            return new Span(ToFix(_flooded.min.z), ToFix(_flooded.max.z));
        }

        Bounds _flooded;
        Vector3 _scale;
        Vector3 _surfaceScale;
        Vector3 _mistScale;
        bool _measured;

        void Awake()
        {
            // Measured FIRST, and then hidden. The box is the only thing in the scene that
            // says how high the poison comes, so it is read before anything about it changes.
            Measure();

            if (hideBoxesInPlay)
            {
                Hide(body);
                Hide(surface);
            }

            // And nothing green until the flood starts. Switched off here rather than left
            // to the first LateUpdate, so a particle system does not get one frame of
            // emission in at the top of the match.
            Show(mist, false);
            Show(source, false);
        }

        static void Hide(Transform art)
        {
            if (art == null) return;
            foreach (Renderer r in art.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
        }

        static void Show(Transform art, bool on)
        {
            if (art != null && art.gameObject.activeSelf != on) art.gameObject.SetActive(on);
        }

        /// <summary>
        /// Remembers the full pool before anything shrinks it.
        ///
        /// Separate from Awake because the baker runs at -100 and may ask first; measuring
        /// again after the art has already been squashed to an empty pool would record THAT
        /// as the finished flood, and the poison would then rise to nothing.
        /// </summary>
        public void Measure()
        {
            if (_measured) return;
            _flooded = Flooded;
            _scale = (body != null ? body : transform).localScale;
            _surfaceScale = surface != null ? surface.localScale : Vector3.one;
            _mistScale = mist != null ? mist.localScale : Vector3.one;
            _measured = true;
        }

        void LateUpdate()
        {
            Measure();
            if (worldBaker == null) worldBaker = SimWorldBaker.Instance;

            float p = Mathf.Clamp01(Rise());
            Fill(p);
        }

        /// <summary>
        /// Puts the art where a given fullness says it is: the body scaled up from its own
        /// floor, the surface riding on top of it at full size.
        ///
        /// Scaled from the bottom edge, which is why the position moves as well — Unity scales
        /// about the pivot, and the pivot is the middle of the box.
        /// </summary>
        void Fill(float p)
        {
            Transform moved = body != null ? body : transform;

            Vector3 scale = _scale;
            scale.y = _scale.y * p;

            Vector3 centre = _flooded.center;
            centre.y = _flooded.min.y + _flooded.extents.y * p;

            moved.localScale = scale;
            moved.position = centre;

            // The lid stays its authored size and only moves. A surface scaled with the body
            // would thin out as the pool fills, which is the opposite of what a waterline
            // does, and at p = 0 it would vanish on the one frame it has to be visible on.
            float top = _flooded.min.y + _flooded.size.y * p;
            if (surface != null)
            {
                surface.localScale = _surfaceScale;
                Vector3 at = surface.position;
                at.y = top;
                surface.position = at;
            }

            // The smoke is the poison, as far as anybody looking at it is concerned: the
            // fog fills what has flooded, the source stays under the arena, and neither
            // exists until the pool starts filling. Switched by the LEVEL rather than by the
            // phase, so there is nothing to keep in step with the clock.
            Show(mist, p > 0f);
            Show(source, p > 0f);

            // The fog is scaled to the flooded volume, exactly as the body box is: bottom
            // on the floor of the pool, top at the waterline. The emitter is then a flat
            // slab at the start and a filled room at the end, which is what makes the green
            // come UP rather than simply appear.
            if (mist != null)
            {
                Vector3 fog = _mistScale;
                fog.y = _mistScale.y * p;
                mist.localScale = fog;

                Vector3 at = mist.position;
                at.y = _flooded.min.y + _flooded.extents.y * p;
                mist.position = at;
            }
        }

        /// <summary>
        /// How full the pool is, from the same clock the simulation reads. Outside play it is
        /// the preview, so the scene view can show the finished level rather than an empty box
        /// nobody can judge.
        /// </summary>
        public float Rise()
        {
            if (!Application.isPlaying) return preview;
            SimWorld world = worldBaker != null ? worldBaker.World : null;
            return world != null ? world.PoisonRise.ToMilli() / 1000f : 0f;
        }

        /// <summary>Where the surface stands right now, in metres. For a view to read.</summary>
        public float SurfaceY()
        {
            Measure();
            return _flooded.min.y + _flooded.size.y * Mathf.Clamp01(Rise());
        }

        static Fix ToFix(float v) => Fix.FromMilli(Mathf.RoundToInt(v * 1000f));

        void OnDrawGizmos()
        {
            if (!drawGizmo) return;

            Bounds b = Application.isPlaying && _measured ? _flooded : Flooded;

            // The pool at full flood. Green, because that is what it is -- and in a build
            // this gizmo is the ONLY place the volume is drawn, since the boxes are hidden
            // and the smoke has no edges.
            Gizmos.color = new Color(0.35f, 0.95f, 0.3f, 0.2f);
            Gizmos.DrawCube(b.center, b.size);

            Gizmos.color = new Color(0.6f, 1f, 0.35f, 1f);
            Gizmos.DrawWireCube(b.center, b.size);

            // And the waterline the preview asks for, which is the number worth judging:
            // everything under it is lost ground, so this line is the arena saying which
            // platforms the fight finishes on.
            float p = Mathf.Clamp01(Application.isPlaying ? Rise() : preview);
            Vector3 lid = b.center;
            lid.y = b.min.y + b.size.y * p;
            Gizmos.color = new Color(0.75f, 1f, 0.4f, 1f);
            Gizmos.DrawWireCube(lid, new Vector3(b.size.x, 0f, b.size.z));
        }
    }
}

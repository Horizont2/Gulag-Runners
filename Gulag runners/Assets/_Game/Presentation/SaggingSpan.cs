using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Feeds the Gulag Runners / Sagging Span shader the weight that is on it.
    ///
    /// The rig that shader needs -- which way the span runs and how long it is -- is
    /// measured and written by SaggingSpanSetup, not here. This only ever writes the
    /// load.
    ///
    /// This is presentation and nothing else. It READS where the fighters are and WRITES
    /// four numbers into a material property block. It never moves a transform, never
    /// touches a collider and never calls into the simulation, and that is not tidiness:
    /// the fight is deterministic fixed-point and the collision under these props was
    /// baked out of their colliders once at load (docs/06). Anything here that nudged a
    /// transform would move the art off the collision on one phone and not the other, and
    /// the two would be playing different matches while agreeing tick for tick.
    ///
    /// Which means the sag is a lie told to the camera, and a small lie is the only kind
    /// worth telling. The fighters go on standing on the box the bake made, so past
    /// roughly ten centimetres of <see cref="SaggingSpan"/> they visibly hover over the
    /// cage they are standing on. The defaults sit well inside that.
    ///
    /// THE SETTLE. The load does not jump to its new value, it springs to it: a fighter
    /// landing on the cage drives it past where it ends up and it comes back. That
    /// overshoot is most of what makes it read as weight rather than as an animation,
    /// and it is why this is a spring here rather than a smoothstep in the shader --
    /// one spring drives the cage AND the four chains holding it, so they move together.
    ///
    /// CHAINS. A chain does not carry the fighter, it carries the cage. Point its
    /// <see cref="follow"/> at the cage's span and it takes that load as its own, so the
    /// whole assembly gives at once. A chain left to work it out for itself would hang
    /// still while the thing under it dropped.
    ///
    /// THE SWING. A slung cage does not only give, it HANGS, and a hanging thing is never
    /// still: it drifts in the air with nobody near it, lurches when somebody lands on it,
    /// and rings down slowly afterwards. Sag on its own made the cage in this arena read
    /// as welded to the sky and slightly bent.
    ///
    /// It is a real pendulum and not a wobble, because the period is the whole of what
    /// makes it read: T = 2*pi*sqrt(L/g), so a cage a metre under its anchor takes two
    /// seconds a swing and there is no number to argue about. <see cref="pendulumLength"/>
    /// is that L, in metres, and zero switches the swing off -- which is right for the
    /// bridges, who are held at both ends and do not hang at all.
    ///
    /// And it swings DIFFERENTLY in the two modes, which is the detail the obvious
    /// implementation gets wrong. Four chains from four corners are a parallel linkage,
    /// not a pendulum about one point: the cage stays LEVEL and slides along an arc while
    /// each chain turns about its own top. So this works one angle out and hands the
    /// shader both forms of it -- a turn for the chains, an offset for the cage -- which
    /// is also what keeps the floor flat under a fighter whose feet are still standing on
    /// the box the bake made.
    ///
    /// THE RIG. Which way the span runs, how long it is and which way is down are facts
    /// about one OBJECT, and they used to live in the material, which is shared. The
    /// eight chains in Arena_Forest wear one material and are each rotated differently,
    /// so object-space down is a different vector for every one of them: six of the eight
    /// were being stretched up to 29 degrees off true, visibly bending sideways into the
    /// tree. So the rig is written per-renderer from here, every frame, out of the same
    /// fields the inspector shows.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SaggingSpan : MonoBehaviour
    {
        [Header("What bends")]
        [Tooltip("Renderers wearing the Sagging Span material. Empty means this object's.")]
        public Renderer[] skin;

        [Header("Rig — measured by the setup tool, in object space")]
        [Tooltip("The fixed end. For a chain, the top.")]
        public Vector3 spanOrigin;
        [Tooltip("Unit vector from the fixed end towards the far one.")]
        public Vector3 spanAxis = Vector3.right;
        [Tooltip("How long the span is, in this object's own units.")]
        public float spanLength = 1f;

        [Tooltip("Which way is DOWN in this object's own space.\n\n" +
                 "Not a preference and not a constant: every one of these props is " +
                 "rotated differently, so world down is a different object-space vector " +
                 "for each. Leave it at zero and the setup tool's value in the material " +
                 "is used instead, which is only right for whichever object the tool " +
                 "happened to measure.")]
        public Vector3 sagDir = Vector3.down;

        [Header("What counts as standing on it")]
        [Tooltip("The fighters. A watcher above the span and within this height loads it.")]
        public Transform[] watch;
        [Tooltip("How far above the span a watcher still counts, metres.")]
        [Range(0.05f, 3f)] public float carryHeight = 0.75f;
        [Tooltip("How far outside the ends a watcher still counts, span fractions.")]
        [Range(0f, 0.5f)] public float overhang = 0.08f;

        [Header("The settle")]
        [Tooltip("How hard it pulls towards the load it is actually carrying.")]
        [Range(1f, 200f)] public float stiffness = 70f;
        [Tooltip("How quickly the bounce dies. Low is a trampoline.")]
        [Range(0.5f, 30f)] public float damping = 7f;
        [Tooltip("Sideways lean of a hanger at full load, metres.")]
        [Range(0f, 0.4f)] public float swayPerLoad = 0.06f;

        [Header("The swing — a hanging thing is never still")]
        [Tooltip("How far it hangs below its anchor, in METRES, measured to the middle " +
                 "of the thing that swings.\n\n" +
                 "This is the one number that decides how the swing reads, because a " +
                 "pendulum's period is fixed by its length: T = 2*pi*sqrt(L/g), so a " +
                 "metre gives two seconds a swing. Measure it rather than tuning it.\n\n" +
                 "ZERO SWITCHES THE SWING OFF, which is right for anything held at both " +
                 "ends -- a bridge deck, a plank floor -- because it does not hang.")]
        public float pendulumLength;

        [Tooltip("How far it drifts in the air with nobody on it, in degrees. One or two. " +
                 "This is the thing that stops it reading as scenery.\n\n" +
                 "It fades out under a load, and not for taste: the fighter's feet are " +
                 "standing on the box the bake made, so a loaded cage that wandered would " +
                 "slide out from under them.")]
        [Range(0f, 8f)] public float idleSwing = 1.2f;

        [Tooltip("How far a landing swings it, in DEGREES OF PEAK -- a full load arriving " +
                 "at once sends it this far off plumb and then it rings down. The lurch " +
                 "is most of what sells the weight.\n\n" +
                 "Stated as the peak rather than as an impulse on purpose: an impulse has " +
                 "to be divided by the pendulum's own frequency before it means anything, " +
                 "so the same number gave a long cage a flick and a short one a lurch. " +
                 "Here six degrees is six degrees, and at a metre that is ten centimetres " +
                 "-- which is also the whole budget before a fighter standing on it " +
                 "visibly slides, because their feet are on the box the bake made.")]
        [Range(0f, 20f)] public float landSwing = 6f;

        [Tooltip("How fast the ring-down dies, as a fraction of critical. A cage on chains " +
                 "is barely damped -- it swings for several seconds -- so this is small. " +
                 "Push it towards 1 and the swing is over before it is seen, which is " +
                 "what a stiff spring looks like.")]
        [Range(0.005f, 0.6f)] public float swingDamping = 0.07f;

        [Tooltip("How far a load standing off-centre tips it, in degrees at the very end " +
                 "of the span. Smooth through the middle, so walking across does not flip " +
                 "the lean from one side to the other in a frame.")]
        [Range(0f, 10f)] public float leanPerLoad = 2.5f;

        [Tooltip("The anchor a HANGER turns about, in this object's own space. For a " +
                 "chain that is the top -- the same point as Span Origin, which is what " +
                 "it is left at when zero.")]
        public Vector3 swingPivot;

        [Header("Carried by something else")]
        [Tooltip("A chain holding a cage: take that cage's load AND its swing instead of " +
                 "working either out. Without this the chain hangs dead still while the " +
                 "cage under it moves, which is what a cage that has come loose looks like.")]
        public SaggingSpan follow;

        static readonly int LoadAtId = Shader.PropertyToID("_LoadAt");
        static readonly int LoadAmountId = Shader.PropertyToID("_LoadAmount");
        static readonly int SwayId = Shader.PropertyToID("_Sway");
        static readonly int SpanOriginId = Shader.PropertyToID("_SpanOrigin");
        static readonly int SpanAxisId = Shader.PropertyToID("_SpanAxis");
        static readonly int SpanLengthId = Shader.PropertyToID("_SpanLength");
        static readonly int SagDirId = Shader.PropertyToID("_SagDir");
        static readonly int SwingAngleId = Shader.PropertyToID("_SwingAngle");
        static readonly int SwingPivotId = Shader.PropertyToID("_SwingPivot");
        static readonly int SwingAxisId = Shader.PropertyToID("_SwingAxis");
        static readonly int SwingOffsetId = Shader.PropertyToID("_SwingOffset");

        const float Gravity = 9.81f;

        MaterialPropertyBlock _block;
        float _load, _velocity, _at = 0.5f;
        float _angle, _spin, _lastTarget, _phase, _swing;
        bool _settled;

        /// <summary>How loaded this span is right now, after the spring. A chain reads it.</summary>
        public float Load => _load;

        /// <summary>Where along the span that load sits, 0 to 1.</summary>
        public float LoadAt => _at;

        /// <summary>How far off plumb the whole assembly hangs, radians. A chain reads it.</summary>
        public float Swing => _swing;

        void Reset()
        {
            skin = GetComponentsInChildren<Renderer>();
        }

        void Awake()
        {
            // Out of where it stands, so two cages in one arena never drift in step and
            // nothing has to be seeded by hand.
            Vector3 at = transform.position;
            _phase = (at.x * 12.9898f + at.z * 37.719f) % (Mathf.PI * 2f);
        }

        void LateUpdate()
        {
            float target = follow != null ? follow.Load : Measure(out _at);
            if (follow != null) _at = follow.LoadAt;

            // A critically-ish damped spring. Integrated semi-implicitly so a long frame
            // cannot send it negative and turn the cage inside out.
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);
            _velocity += (target - _load) * stiffness * dt;
            _velocity -= _velocity * Mathf.Min(damping * dt, 1f);
            _load = Mathf.Clamp(_load + _velocity * dt, -0.25f, 1.5f);

            _swing = follow != null ? follow.Swing : Swung(target, dt);

            Push();
        }

        /// <summary>
        /// How far off plumb it hangs this frame, radians: the pendulum, plus the air,
        /// plus the tip of an off-centre load.
        ///
        /// Kept in three parts on purpose. The pendulum has to be integrated because its
        /// whole point is that it carries on after the thing that started it; the air and
        /// the tip are states of the world right now and are read straight off, so their
        /// amplitudes are the degrees the inspector says rather than whatever a force fed
        /// into a spring happens to come out as.
        /// </summary>
        float Swung(float target, float dt)
        {
            if (pendulumLength <= 0f) return 0f;

            float omega = Mathf.Sqrt(Gravity / pendulumLength);

            // A landing is a step in the load, and a step is an impulse. Taken per
            // second rather than per frame so the kick is the same on a phone at 30 and
            // a desk at 144 -- a jolt scaled by nothing is four times softer at four
            // times the rate, which is the kind of bug that only shows on one device.
            // The FIRST frame is not a landing. A fighter spawning on the cage arrives as
            // a step from nothing, and taken as an impulse it would fling the thing on
            // the opening frame of the round -- which is the one frame everybody watches.
            float jolt = _settled ? (target - _lastTarget) / Mathf.Max(dt, 1e-4f) : 0f;
            _lastTarget = target;
            _settled = true;

            // Times omega, so the PEAK comes out at landSwing degrees: a free pendulum
            // given angular speed v reaches v/omega, and dividing it out here is what
            // makes the inspector's number mean the angle it says.
            _spin += jolt * landSwing * Mathf.Deg2Rad * omega * dt;

            // theta'' = -w^2 sin(theta) - 2 z w theta'. Semi-implicit, and the real sine
            // rather than the small-angle form: it costs nothing here and it means the
            // swing cannot run away if something hands it a large angle.
            _spin -= (omega * omega * Mathf.Sin(_angle) + 2f * swingDamping * omega * _spin) * dt;
            _angle = Mathf.Clamp(_angle + _spin * dt, -0.5f, 0.5f);

            // The air. Two slow waves at unrelated speeds, so it never repeats on a beat
            // you can hear, and gone by the time anybody is standing on it.
            float w = Time.time * (omega * 0.21f) + _phase;
            float air = (Mathf.Sin(w) * 0.65f + Mathf.Sin(w * 2.37f + 1.3f) * 0.35f)
                        * idleSwing * Mathf.Deg2Rad * (1f - Mathf.Clamp01(_load));

            // And the tip. Signed and linear through the middle: the old lean used
            // sign(at - 0.5), which flipped the whole cage over in one frame as a
            // fighter crossed its centre.
            float tip = leanPerLoad * Mathf.Deg2Rad * _load * (_at - 0.5f) * 2f;

            return _angle + air + tip;
        }

        /// <summary>
        /// How much weight is on this span, and where.
        ///
        /// Measured against the span's own line rather than against world X, because the
        /// chains in this arena are not vertical and the bridges are not square to the
        /// camera. A watcher counts when it is above the span, within
        /// <see cref="carryHeight"/> of it, and between the ends.
        /// </summary>
        float Measure(out float at)
        {
            at = 0.5f;
            if (watch == null || spanLength <= 0f) return 0f;

            Vector3 axis = spanAxis.sqrMagnitude > 1e-6f ? spanAxis.normalized : Vector3.right;
            float best = 0f;
            float bestAt = 0.5f;

            foreach (Transform t in watch)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;

                Vector3 local = transform.InverseTransformPoint(t.position) - spanOrigin;
                float along = Vector3.Dot(local, axis) / spanLength;
                if (along < -overhang || along > 1f + overhang) continue;

                // Height above the span's line, in world metres rather than object units,
                // so a prop scaled down does not quietly stop noticing anybody.
                Vector3 onAxis = axis * (along * spanLength);
                float offMetres = transform.TransformVector(local - onAxis).magnitude;
                if (offMetres > carryHeight) continue;

                float weight = 1f - Mathf.Clamp01(offMetres / Mathf.Max(carryHeight, 1e-3f));
                if (weight > best)
                {
                    best = weight;
                    bestAt = Mathf.Clamp01(along);
                }
            }

            at = bestAt;
            return best;
        }

        void Push()
        {
            if (skin == null || skin.Length == 0) return;
            _block ??= new MaterialPropertyBlock();

            // Signed and linear, for the reason given in Swung: Mathf.Sign flipped the
            // lean across the whole prop in one frame as a fighter crossed the middle.
            float sway = swayPerLoad * _load * (_at - 0.5f) * 2f;

            Vector3 axis = spanAxis.sqrMagnitude > 1e-6f ? spanAxis.normalized : Vector3.right;
            Vector3 down = sagDir.sqrMagnitude > 1e-6f ? sagDir.normalized : Vector3.down;
            Vector3 pivot = swingPivot.sqrMagnitude > 1e-9f ? swingPivot : spanOrigin;

            // What the two modes need out of one angle.
            //
            // A HANGER turns about its anchor, and the axis is horizontal and across the
            // arena -- object space, because every one of these props is rotated
            // differently and a chain told to turn about its own Z turns into the tree.
            //
            // A SPAN does not turn. Four chains hold it level and carry it sideways along
            // an arc, so it gets the offset of that arc: across by L sin(theta), down by
            // L (1 - cos(theta)).
            Vector3 turnAxis = transform.InverseTransformDirection(Vector3.forward);
            if (turnAxis.sqrMagnitude < 1e-9f) turnAxis = Vector3.forward;

            Vector3 slide = Vector3.zero;
            if (pendulumLength > 0f)
            {
                Vector3 across = transform.InverseTransformDirection(Vector3.right);
                float unit = Mathf.Abs(transform.lossyScale.y);
                float perMetre = unit > 1e-4f ? 1f / unit : 1f;
                slide = across.normalized * (pendulumLength * Mathf.Sin(_swing) * perMetre)
                      + down * (pendulumLength * (1f - Mathf.Cos(_swing)) * perMetre);
            }

            foreach (Renderer r in skin)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);

                // The rig, per renderer. The material carries the SHAPE -- how much it
                // droops, how hard the ends hold -- which is shared and should be; this
                // is the geometry, which is not.
                _block.SetVector(SpanOriginId, spanOrigin);
                _block.SetVector(SpanAxisId, axis);
                _block.SetFloat(SpanLengthId, spanLength);
                _block.SetVector(SagDirId, down);

                _block.SetFloat(LoadAtId, _at);
                _block.SetFloat(LoadAmountId, _load);
                _block.SetFloat(SwayId, sway);

                _block.SetFloat(SwingAngleId, _swing);
                _block.SetVector(SwingPivotId, pivot);
                _block.SetVector(SwingAxisId, turnAxis.normalized);
                _block.SetVector(SwingOffsetId, slide);

                r.SetPropertyBlock(_block);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (spanLength <= 0f) return;
            Vector3 axis = spanAxis.sqrMagnitude > 1e-6f ? spanAxis.normalized : Vector3.right;
            Vector3 a = transform.TransformPoint(spanOrigin);
            Vector3 b = transform.TransformPoint(spanOrigin + axis * spanLength);

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(a, 0.06f);
            Gizmos.DrawWireSphere(b, 0.06f);

            // Where the weight is, and how much of it.
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(Vector3.Lerp(a, b, _at), 0.05f + 0.12f * _load);

            // What it hangs from, and how far below. Drawn because the pendulum is a
            // LENGTH in metres and the pivot is a point in object space, and nothing
            // else in the inspector shows whether the two agree with the chains above.
            if (pendulumLength <= 0f) return;
            Vector3 p = transform.TransformPoint(
                swingPivot.sqrMagnitude > 1e-9f ? swingPivot : spanOrigin);
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.9f);
            Gizmos.DrawWireSphere(p, 0.05f);
            Gizmos.DrawLine(p, p + Vector3.down * pendulumLength);
            Gizmos.DrawWireCube(p + Vector3.down * pendulumLength, Vector3.one * 0.06f);
        }
    }
}

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

        [Header("Carried by something else")]
        [Tooltip("A chain holding a cage: take that cage's load instead of looking.")]
        public SaggingSpan follow;

        static readonly int LoadAtId = Shader.PropertyToID("_LoadAt");
        static readonly int LoadAmountId = Shader.PropertyToID("_LoadAmount");
        static readonly int SwayId = Shader.PropertyToID("_Sway");

        MaterialPropertyBlock _block;
        float _load, _velocity, _at = 0.5f;

        /// <summary>How loaded this span is right now, after the spring. A chain reads it.</summary>
        public float Load => _load;

        /// <summary>Where along the span that load sits, 0 to 1.</summary>
        public float LoadAt => _at;

        void Reset()
        {
            skin = GetComponentsInChildren<Renderer>();
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

            Push();
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

            float sway = swayPerLoad * _load * Mathf.Sign(_at - 0.5f);

            foreach (Renderer r in skin)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetFloat(LoadAtId, _at);
                _block.SetFloat(LoadAmountId, _load);
                _block.SetFloat(SwayId, sway);
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
        }
    }
}

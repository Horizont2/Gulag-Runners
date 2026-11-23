using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The shutter, moving.
    ///
    /// docs/01 does not have the outer rooms fade out — it has steel slabs come down with a
    /// bang, one after another, so that the last minute is something you hear coming and can
    /// run from. That is a performance, so all of it is in the inspector.
    ///
    /// The one rule it must not break: the slab ARRIVES as the gate becomes solid, it does not
    /// set off then. The schedule is a pure function of the frame
    /// (<see cref="MatchMotor.GateShutsAt"/>), so this can look it up, start sliding early, and
    /// land on the tick collision changes. The other way about — animating after the gate goes
    /// live — gives you a doorway you cannot walk through that still looks open, which reads as
    /// a bug and gets blamed on the netcode.
    ///
    /// Presentation only. It reads the clock and never writes to it (docs/06).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ArenaGate))]
    public sealed class GateView : MonoBehaviour
    {
        [Header("Wiring")]
        public ArenaGate gate;

        [Tooltip("Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("What moves. Leave empty to move this object — but the SimCollider boxes are " +
                 "baked from where they stand at Awake, so moving this object itself is only " +
                 "right when the slab starts and ends in the same place the collision is.")]
        public Transform slab;

        [Header("The slam")]
        [Tooltip("Where the slab waits, relative to where it ends up, in metres. Up and out of " +
                 "sight is the usual answer for a shutter in a ceiling slot.")]
        public Vector3 from = new Vector3(0f, 4f, 0f);

        [Tooltip("How long the slide takes, in seconds. Short: this is a slab dropping, not a " +
                 "door closing. It lands exactly as the gate becomes solid.")]
        [Range(0.1f, 3f)] public float slideSeconds = 0.55f;

        [Tooltip("How much of the slide is spent accelerating. Low values read as a drop; high " +
                 "values read as machinery lowering it.")]
        [Range(0f, 1f)] public float weight = 0.8f;

        [Tooltip("How far it rebounds on landing, as a share of the travel, and for how long. " +
                 "A slab that stops dead reads as a slab that was always there.")]
        [Range(0f, 0.3f)] public float bounce = 0.06f;
        [Range(0f, 1f)] public float bounceSeconds = 0.18f;

        [Header("Warning")]
        [Tooltip("Seconds before the slide starts that the warning begins — the siren light, " +
                 "the rattle, whatever is on this object. Long enough to run.")]
        [Range(0f, 10f)] public float warnSeconds = 3f;

        [Tooltip("Set active while the warning runs, then switched off. Lamps, a rotating " +
                 "beacon, a dust emitter.")]
        public GameObject warning;

        [Tooltip("Optional light on that beacon. It is pulsed rather than just switched on: a " +
                 "lamp that comes on and sits there is scenery, and a lamp that beats is a " +
                 "countdown. Its own intensity in the inspector is the brightest it gets.")]
        public Light warnLight;

        [Tooltip("Beats per second while the warning runs.")]
        [Range(0.2f, 8f)] public float warnPulse = 2.4f;

        [Header("Sound")]
        [Tooltip("Played once as the warning begins, and once as the slab lands.")]
        public AudioSource warnSound;
        public AudioSource slamSound;

        Vector3 _shut;
        bool _warned, _slammed;
        int _shutsAt = int.MaxValue;
        bool _scheduleKnown;
        float _lightFull;

        void Reset()
        {
            gate = GetComponent<ArenaGate>();
            slab = transform;
        }

        void Awake()
        {
            if (gate == null) gate = GetComponent<ArenaGate>();
            if (matchState == null) matchState = MatchState.Instance;
            if (slab == null) slab = transform;

            // Where the collision is. The slab is drawn away from here and comes back to it;
            // the boxes never move, because the baked arena is immutable (docs/05).
            _shut = slab.localPosition;
            if (warning != null) warning.SetActive(false);
        }

        void LateUpdate()
        {
            if (slab == null || gate == null) return;
            if (matchState == null) matchState = MatchState.Instance;
            if (matchState == null) return;

            MatchSimState m = matchState.Match;

            if (!_scheduleKnown && gate.Index >= 0)
            {
                SimWorldBaker baker = matchState.worldBaker != null
                    ? matchState.worldBaker : SimWorldBaker.Instance;
                SimWorld world = baker != null ? baker.World : null;
                if (world != null && world.Gates.Length > 0)
                {
                    MatchConfig rules = matchState.MatchRules;
                    _shutsAt = MatchMotor.GateShutsAt(gate.Index, world.Gates, in rules);
                    _scheduleKnown = true;
                }
            }

            // A new round puts the slab back up with the clock.
            if (m.Frame == 0 && !m.Finished)
            {
                _warned = false;
                _slammed = false;
                if (warning != null) warning.SetActive(false);
            }

            float t = Slide(in m);
            int since = _scheduleKnown ? m.Frame - _shutsAt : -1;
            slab.localPosition = _shut + from * (1f - Shaped(t)) + Rebound(t, since);

            bool warnNow = (t > 0f && t < 1f) ||
                           (_scheduleKnown && m.Frame >= WarnFrame && m.Frame < _shutsAt);
            if (warning != null && warning.activeSelf != warnNow) warning.SetActive(warnNow);

            if (warnLight != null)
            {
                if (_lightFull <= 0f) _lightFull = warnLight.intensity;
                float beat = 0.5f + 0.5f * Mathf.Sin(Time.time * warnPulse * Mathf.PI * 2f);
                warnLight.intensity = warnNow ? _lightFull * Mathf.Lerp(0.25f, 1f, beat) : 0f;
                if (warnLight.enabled != warnNow) warnLight.enabled = warnNow;
            }

            if (!_warned && warnNow) { _warned = true; Play(warnSound); }
            if (!_slammed && t >= 1f && _scheduleKnown) { _slammed = true; Play(slamSound); }
        }

        int WarnFrame => _shutsAt - Mathf.RoundToInt(
            (warnSeconds + slideSeconds) * PlayerMotor.TicksPerSecond);

        /// <summary>
        /// How far through the slide, 0 to 1, from the frame the gate is due to shut on. 1
        /// everywhere afterwards, and while the match is over, so the arena stays as it closed.
        /// </summary>
        float Slide(in MatchSimState m)
        {
            if (!_scheduleKnown) return m.Shut(gate.Index) ? 1f : 0f;
            if (m.Shut(gate.Index)) return 1f;

            int frames = Mathf.Max(1, Mathf.RoundToInt(slideSeconds * PlayerMotor.TicksPerSecond));
            return Mathf.Clamp01((m.Frame - (_shutsAt - frames)) / (float)frames);
        }

        /// <summary>Weight: slow off the mark, then dropping.</summary>
        float Shaped(float t)
        {
            t = Mathf.Clamp01(t);
            float eased = t * t * (3f - 2f * t);
            float dropped = t * t;
            return Mathf.Lerp(eased, dropped, Mathf.Clamp01(weight));
        }

        /// <summary>
        /// The settle after it lands, measured in ticks since the gate went solid. Negative
        /// means the schedule is not known yet, and a slab that is simply already shut — the
        /// result phase, or a view that woke late — gets no bounce rather than one on arrival.
        /// </summary>
        Vector3 Rebound(float t, int sinceFrames)
        {
            if (bounce <= 0f || t < 1f || sinceFrames < 0) return Vector3.zero;

            float frames = Mathf.Max(1f, bounceSeconds * PlayerMotor.TicksPerSecond);
            float k = Mathf.Clamp01(sinceFrames / frames);
            if (k >= 1f) return Vector3.zero;

            return from.normalized *
                   (bounce * from.magnitude * Mathf.Sin(k * Mathf.PI * 2f) * (1f - k));
        }

        static void Play(AudioSource source)
        {
            if (source != null) source.Play();
        }
    }
}

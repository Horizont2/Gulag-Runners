using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Telling the arena that the gas is coming, and then that it is here.
    ///
    /// Until this existed the flood simply began: at 1:00 the surface started climbing and
    /// nothing said so. A player found out when their health started going, which is the
    /// one way a timed event must never be announced -- by its damage.
    ///
    /// It is told the way docs/01 tells everything else, and <see cref="SirenView"/> argues
    /// the case: DIEGETICALLY, on the arena and on the body, never as an arrow bolted to the
    /// edge of the screen. There is no text here and no icon. Everything below can be read
    /// with the sound off, because sound is the channel this game spends on telling you
    /// where the OTHER player is, and a warning that needs it is a warning that competes
    /// with the thing it should be protecting.
    ///
    /// Three beats, and each says a different thing:
    ///
    ///   1. THE BRACE, three seconds before. The arena's lamps dip, the collar at your
    ///      throat begins a slow pulse, and a pressure builds underneath. It says: coming,
    ///      and from below.
    ///   2. THE VENT, on the tick the phase turns. A clank, one hard shove of the camera,
    ///      and the green arrives at the bottom of the arena over about a third of a
    ///      second -- not faded in over three, which reads as a bug rather than a release.
    ///      It says: now.
    ///   3. THE RISE, for the eighty seconds after. The collar's pulse quickens as the
    ///      surface nears YOUR feet, and the hiss follows it. It says: how long YOU have.
    ///
    /// The third beat is the one that makes this a mechanic rather than a cutscene. The
    /// flood is the same for both fighters; how close it is to your boots is not, and in a
    /// game built on not being able to see the other man, a signal that is only about you
    /// is worth more than a spectacle you both watch.
    ///
    /// Presentation only. It reads the simulation and never writes to it (docs/06): the
    /// surface height comes from <see cref="PoisonMotor.RiseAt"/>, which is the same pure
    /// function the damage uses, so what you are shown and what bites you cannot disagree.
    /// </summary>
    [DefaultExecutionOrder(113)]      // after the camera (100) and the siren (112)
    [DisallowMultipleComponent]
    public sealed class PoisonWarning : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The round. Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("The pool this announces. Leave empty to find the one in the scene.")]
        public PoisonPool pool;

        [Tooltip("Whose collar pulses and whose feet the distance is measured from. Leave " +
                 "empty for the local fighter -- the warning is personal, and the other " +
                 "man gets his own on his own phone.")]
        public PlayerController player;

        [Tooltip("Shoved once when the gas vents. Leave empty to use the scene's camera.")]
        public SideViewCamera shake;

        [Header("The brace, before it comes")]
        [Tooltip("How long the arena braces, in seconds. Three is long enough to stop what "
                 + "you are doing and get off the bottom floor, and short enough that it "
                 + "reads as a warning rather than a countdown.")]
        [Range(0.5f, 8f)] public float braceSeconds = 3f;

        [Tooltip("Lamps that dip while it braces. Any light in the arena; the brown-out is "
                 + "what makes a player look up before anything has happened.")]
        public Light[] lamps = new Light[0];

        [Tooltip("How far they dip, as a fraction of their own intensity. They come back "
                 + "over the brace, so the arena breathes in rather than going dark.")]
        [Range(0f, 1f)] public float lampDip = 0.55f;

        [Header("The vent, on the tick")]
        [Tooltip("One shove of the camera. The same trauma a heavy blow adds, which is the "
                 + "most this may ever be: the gas is an event, not a hit.")]
        [Range(0f, 1f)] public float ventShake = 0.35f;

        [Tooltip("Vents: where it comes OUT. Switched on for the burst and off again. "
                 + "Leave empty and the pool's own source smoke carries it alone.")]
        public ParticleSystem[] vents = new ParticleSystem[0];

        [Tooltip("How long the burst lasts before it settles into the slow flood.")]
        [Range(0.1f, 3f)] public float ventSeconds = 0.9f;

        [Header("The rise, for the eighty seconds after")]
        [Tooltip("What lights at the throat, as in SirenView. Optional -- without one the "
                 + "lamps and the arena carry it.")]
        public Renderer collar;

        [Tooltip("The colour it pulses. Green, because that is what is rising; the amber is "
                 + "the siren's and the two must not be confused.")]
        public Color collarColour = new Color(0.45f, 1f, 0.35f);

        [Tooltip("How high above your feet the surface has to be for the collar to go quiet "
                 + "again, in metres. Inside this, the pulse quickens the nearer it gets.")]
        [Range(0.5f, 20f)] public float worryFrom = 7f;

        [Tooltip("Pulses a second when the gas is at the edge of that range, and when it is "
                 + "at your boots.")]
        public Vector2 pulseRate = new Vector2(0.6f, 4.5f);

        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock _block;
        MatchPhase _was = MatchPhase.Cages;
        float _brace = -1f;              // seconds left of the brace, or -1
        float _vent = -1f;               // seconds left of the burst, or -1
        float _phase;                    // where the collar is in its pulse
        float[] _lampWas;
        bool _armed;

        void Awake()
        {
            if (matchState == null) matchState = FindFirstObjectByType<MatchState>();
            if (pool == null) pool = FindFirstObjectByType<PoisonPool>();
            if (shake == null) shake = FindFirstObjectByType<SideViewCamera>();

            _lampWas = new float[lamps.Length];
            for (int i = 0; i < lamps.Length; i++)
                if (lamps[i] != null) _lampWas[i] = lamps[i].intensity;

            Quiet();
        }

        PlayerController Local()
        {
            if (player != null) return player;
            if (matchState == null || matchState.fighters == null) return null;
            int at = matchState.LocalFighter;
            return at >= 0 && at < matchState.fighters.Count ? matchState.fighters[at] : null;
        }

        void LateUpdate()
        {
            if (matchState == null) { Quiet(); return; }

            MatchSimState match = matchState.Match;
            MatchConfig rules = matchState.MatchRules;
            PoisonConfig poison = matchState.Poison;

            if (!poison.On || match.Phase == MatchPhase.Over)
            {
                if (_was != match.Phase) Quiet();
                _was = match.Phase;
                return;
            }

            // How far off the flood is, in seconds, from the clock itself rather than from
            // a timer of our own -- a timer would drift from the simulation the moment a
            // rollback moved the frame, and the one thing this must agree with is the tide.
            int begins = MatchMotor.PhaseStart(MatchPhase.Closing, in rules);
            float away = (begins - match.Frame) * PlayerController.SecondsPerTick;

            // ---- beat one: the arena braces
            if (away > 0f && away <= braceSeconds)
            {
                if (!_armed)
                {
                    _armed = true;
                    Post(SoundId.PoisonWarn);
                }
                _brace = away;
                Dip(1f - away / braceSeconds);
            }
            else if (_brace >= 0f && away <= 0f)
            {
                _brace = -1f;
                Dip(0f);
            }

            // ---- beat two: it vents
            if (_was != MatchPhase.Closing && match.Phase == MatchPhase.Closing)
            {
                _vent = ventSeconds;
                if (shake != null) shake.AddTrauma(ventShake);
                foreach (ParticleSystem v in vents)
                    if (v != null) v.Play(true);
                Post(SoundId.PoisonVent);
            }
            _was = match.Phase;

            if (_vent > 0f)
            {
                _vent -= Time.deltaTime;
                if (_vent <= 0f)
                    foreach (ParticleSystem v in vents)
                        if (v != null) v.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            // ---- beat three: how close it is to YOUR feet
            if (match.Phase < MatchPhase.Closing) { Lamp(); return; }

            PlayerController mine = Local();
            if (mine == null || collar == null || pool == null) { Lamp(); return; }

            // The waterline from the pool itself, not worked out again here: RiseAt gives
            // how FULL the pool is, nought to one, and turning that into a height needs
            // the box it is filling. Two places doing that is two places that can disagree.
            float gap = mine.transform.position.y - pool.SurfaceY;

            if (gap > worryFrom || gap < -8f)
            {
                Paint(0f);
                Lamp();
                return;
            }

            // Nearer is faster. Below the surface it is as fast as it goes, because there is
            // nothing left to warn about -- you are standing in it.
            float near = gap <= 0f ? 1f : 1f - Mathf.Clamp01(gap / worryFrom);
            _phase += Time.deltaTime * Mathf.Lerp(pulseRate.x, pulseRate.y, near);
            if (_phase > 1f) _phase -= 1f;

            // A hard pulse rather than a sine: this is a lamp on a collar, not a breath.
            Paint(_phase < 0.45f ? Mathf.Lerp(0.25f, 1f, near) : 0f);
            Lamp();
        }

        /// <summary>The lamps' brown-out, deepest at the moment the gas arrives.</summary>
        void Dip(float amount)
        {
            for (int i = 0; i < lamps.Length; i++)
                if (lamps[i] != null)
                    lamps[i].intensity = _lampWas[i] * (1f - lampDip * Mathf.Clamp01(amount));
        }

        void Lamp()
        {
            if (_brace < 0f) Dip(0f);
        }

        void Paint(float strength)
        {
            if (collar == null) return;
            _block ??= new MaterialPropertyBlock();
            collar.GetPropertyBlock(_block);
            _block.SetColor(Emission, collarColour * strength);
            collar.SetPropertyBlock(_block);
        }

        void Post(SoundId sound)
        {
            if (AudioManager.Instance == null) return;
            Vector3 at = pool != null ? pool.transform.position : transform.position;

            // Loud, and heard through the floor: this is the one event in a match that
            // reaches both fighters at once, wherever they are standing.
            AudioManager.Instance.Post(sound, at, NoiseLevel.Loud, at);
        }

        void Quiet()
        {
            _brace = -1f;
            _vent = -1f;
            _armed = false;
            Dip(0f);
            Paint(0f);
            foreach (ParticleSystem v in vents)
                if (v != null) v.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void OnDisable() => Quiet();
    }
}

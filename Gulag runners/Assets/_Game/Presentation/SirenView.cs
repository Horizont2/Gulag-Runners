using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The radar pulse of docs/01, told without any UI.
    ///
    /// docs/01 wants one impulse at 0:25 that gives a DIRECTION and never a position, and it
    /// asks for a screen-edge arrow so that the game is playable deaf. An arrow is UI, and this
    /// build has none — so the arrow's job is done by the body instead: the collar at your
    /// fighter's throat lights, their head turns, and the camera leans the same way. A player
    /// who cannot hear still gets the direction, from the thing they are already looking at.
    ///
    /// Which is arguably the better reading of the rule anyway: docs/01's whole information
    /// ladder is diegetic — sound, traces, beams from the locks — and an arrow bolted to the
    /// edge of the screen is the one rung that is not.
    ///
    /// The pulse itself is simulation (<see cref="MatchSimState.SirenDirection"/>), taken once
    /// on the tick the siren begins. Nothing here may write to it (docs/06).
    /// </summary>
    [DefaultExecutionOrder(112)]      // after the camera (100) and the round shots (110, 111)
    [DisallowMultipleComponent]
    public sealed class SirenView : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("Whose pulse this is. Leave empty to use the PlayerController on this object " +
                 "or its parents — the arena has two ends and each fighter is told their own " +
                 "side of the direction.")]
        public PlayerController player;

        [Header("The look")]
        [Tooltip("The head bone. Leave empty to take it from the Animator's humanoid rig.")]
        public Transform head;

        [Tooltip("How far the head turns towards the pulse, in degrees. Enough to read from " +
                 "across the arena, not enough to look broken.")]
        [Range(0f, 80f)] public float headYaw = 48f;

        [Tooltip("How far it tilts up as it turns, in degrees — listening, not just looking.")]
        [Range(-30f, 30f)] public float headPitch = -8f;

        [Tooltip("How quickly the head gets there and back, in seconds.")]
        [Range(0.02f, 1f)] public float headEase = 0.18f;

        [Header("The collar")]
        [Tooltip("What lights at the throat. docs/01's collar is the one piece of hardware " +
                 "both fighters are wearing, so it is where the game's own signals belong.")]
        public Renderer collar;

        [Tooltip("Colour and strength of the flash. Pushed to albedo as well as emission, " +
                 "because URP will not emit at all unless the material's own emission is on " +
                 "and a property block cannot switch that keyword.")]
        [ColorUsage(false, true)] public Color collarColour = new Color(1f, 0.42f, 0.2f);
        [Range(0f, 8f)] public float collarEmission = 4f;
        [Range(0f, 1f)] public float collarAlbedo = 0.6f;

        [Tooltip("How many times it beats across the pulse. Two reads as a radar; one reads " +
                 "as a glitch.")]
        [Range(1, 6)] public int collarBeats = 2;

        [Header("The camera")]
        [Tooltip("Optional. The camera that follows this fighter: it leans the way the pulse " +
                 "points, which is the same information again for a player who is watching " +
                 "the arena rather than their own fighter.")]
        public SideViewCamera view;

        [Tooltip("How far the camera leans, in metres.")]
        [Range(0f, 2f)] public float cameraLean = 0.55f;

        [Header("Sound")]
        public AudioSource pulseSound;

        static readonly int BaseColour = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColour = Shader.PropertyToID("_Color");
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock _block;
        Color _collarOwn;
        bool _haveOwn;
        bool _sounded;

        Quaternion _headRest;
        bool _haveRest;
        float _shown;
        float _shownVelocity;

        void Reset()
        {
            matchState = MatchState.Instance;
            player = GetComponentInParent<PlayerController>();
            view = null;
        }

        void Awake()
        {
            if (matchState == null) matchState = MatchState.Instance;
            if (player == null) player = GetComponentInParent<PlayerController>();

            if (head == null)
            {
                Animator animator = GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman)
                    head = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (collar != null)
            {
                _block = new MaterialPropertyBlock();
                _collarOwn = OwnColour(collar);
                _haveOwn = true;
            }
        }

        /// <summary>
        /// The collar's own colour, read off the shared material rather than assumed.
        ///
        /// Painting a flat tint over a renderer at rest is how the fighters came out white at
        /// the start of a round once already: a property block that is written every frame
        /// replaces the material's colour, it does not add to it.
        /// </summary>
        static Color OwnColour(Renderer r)
        {
            Material m = r.sharedMaterial;
            if (m == null) return Color.white;
            if (m.HasProperty(BaseColour)) return m.GetColor(BaseColour);
            if (m.HasProperty(LegacyColour)) return m.GetColor(LegacyColour);
            return Color.white;
        }

        void LateUpdate()
        {
            if (matchState == null) matchState = MatchState.Instance;
            if (matchState == null) return;

            MatchSimState m = matchState.Match;
            bool on = m.Phase == MatchPhase.Siren;

            int side = player != null ? m.SirenFor(player.playerIndex) : 0;

            // Eased rather than switched, in both directions: a head that snaps to one side and
            // back is a twitch, and the pulse is supposed to read as the collar working.
            _shown = headEase <= 0.001f
                ? (on ? 1f : 0f)
                : Mathf.SmoothDamp(_shown, on ? 1f : 0f, ref _shownVelocity, headEase,
                                   Mathf.Infinity, Time.deltaTime);

            // Re-armed by anything earlier than the siren, which covers a new round as well
            // as a view that woke up partway through one.
            if (m.Phase < MatchPhase.Siren) _sounded = false;
            if (!_sounded && on) { _sounded = true; Play(pulseSound); }

            Turn(side);
            Light(on ? Beat(in m) : 0f);
            Lean(side);
        }

        /// <summary>
        /// How bright the collar is: a couple of beats across the pulse rather than one long
        /// glow, so it reads as a sweep being taken and not as damage.
        /// </summary>
        float Beat(in MatchSimState m)
        {
            MatchConfig rules = matchState.MatchRules;
            int length = Mathf.Max(1, rules.SirenFrames);
            int start = MatchMotor.PhaseStart(MatchPhase.Siren, in rules);
            float t = Mathf.Clamp01((m.Frame - start) / (float)length);
            return Mathf.Abs(Mathf.Sin(t * Mathf.PI * collarBeats));
        }

        void Turn(int side)
        {
            if (head == null || side == 0) return;

            if (!_haveRest) { _headRest = head.localRotation; _haveRest = true; }

            // Towards the pulse in world terms, then expressed on the bone, so a rig whose
            // head bone points anywhere at all still turns the right way.
            Quaternion want = Quaternion.AngleAxis(headYaw * side, Vector3.up) *
                              Quaternion.AngleAxis(headPitch, Vector3.right);
            head.rotation = Quaternion.Slerp(head.rotation, want * head.rotation, _shown);
        }

        void Light(float strength)
        {
            if (collar == null || !_haveOwn) return;

            float k = Mathf.Clamp01(strength) * _shown;

            if (k <= 0.001f)
            {
                // Cleared outright, not set back to the colour we think it had. A block left
                // in place is a tint that never goes away.
                collar.SetPropertyBlock(null);
                return;
            }

            _block.Clear();
            _block.SetColor(Emission, collarColour * (collarEmission * k));
            _block.SetColor(BaseColour, Color.Lerp(_collarOwn, collarColour, collarAlbedo * k));
            _block.SetColor(LegacyColour, Color.Lerp(_collarOwn, collarColour, collarAlbedo * k));
            collar.SetPropertyBlock(_block);
        }

        void Lean(int side)
        {
            if (view == null || side == 0 || cameraLean <= 0f) return;
            if (_shown <= 0.001f) return;

            Transform cam = view.transform;
            cam.position += new Vector3(cameraLean * side * _shown, 0f, 0f);
        }

        static void Play(AudioSource source)
        {
            if (source != null) source.Play();
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The screen itself reacting to the fight.
    ///
    /// NOTHING WAS DRIVING POST-PROCESSING AT ALL. Every arena carries a Global Volume with
    /// a tonemap, a bloom and a vignette, and a grade per biome on top of it -- and all of
    /// it is static. The same picture at full health and at four, with a blade in your face
    /// and alone in a corridor, at 1:30 and in sudden death. Everything the game had to say
    /// about the state of the fight was being said by the HUD, which is the one part of the
    /// screen a player is not looking at while somebody is swinging at them.
    ///
    /// Four things, and the reason it is four Volumes rather than one is the reason this
    /// file is short. Each look is AUTHORED, in a profile a designer can open and tune with
    /// the scene view live, and all this does is move four weights. There is no colour
    /// arithmetic here, nothing to keep in step with an artist's intent, and no runtime
    /// access to a VolumeComponent -- which also means no profile is ever instanced and no
    /// shared asset is ever dirtied by play mode:
    ///
    ///   hurt         a blow landed on this fighter. Punched to full and let go fast, so it
    ///                reads as the hit rather than as a state. Scaled by how big the hit
    ///                actually was, off the damage and not the weapon, so armour scales it
    ///                down exactly as much as it scales the hit down.
    ///   landed       a blow landed FROM this fighter. Smaller, and deliberately NOT a
    ///                vignette: the screen edge is where damage lives, and borrowing it for
    ///                a hit you dealt is the surest way to make the two unreadable.
    ///   desperate    held while below the desperation threshold, where docs/02 makes bare
    ///                hands hit 40% harder -- both ways. A mechanic, not a mood, and the
    ///                health bar already breathes for it; this is the same statement made
    ///                somewhere the player is already looking.
    ///   suddenDeath  held through the last phase. Drained rather than reddened, because
    ///                the red is the player's own health and must not be spent on a clock.
    ///
    /// Every slot is optional. An arena with none of them wired costs one component reading
    /// a struct once a frame and changes nothing on screen, which is what lets this go into
    /// every scene before any of the profiles are tuned.
    ///
    /// Reads the SIMULATION and never the animation, like every other view here: a hit is a
    /// fact about a tick, and a frame that happens to run two ticks or none must not double
    /// or lose one. Unscaled time throughout, so MatchOutcomeView's slow motion on the
    /// finishing blow does not stretch the last punch out over three seconds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatGrade : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The match, for the phase and for which fighter is holding this phone. " +
                 "Found in the scene when empty.")]
        public MatchState match;

        [Tooltip("Optional. The fighter this screen belongs to. Left empty it follows the " +
                 "match's own local fighter, which is what makes this correct on the " +
                 "guest's phone without being told: NetMatch moves that over on " +
                 "connecting, and a grade pointed at fighter one would be reporting the " +
                 "wrong man's health to half of every online match.")]
        public PlayerController player;

        [Header("Volumes")]
        [Tooltip("A blow landing on this fighter. Punched and released.")]
        public Volume hurt;

        [Tooltip("A blow landing from this fighter. Punched and released, smaller.")]
        public Volume landed;

        [Tooltip("Held while this fighter is in desperation range.")]
        public Volume desperate;

        [Tooltip("Held through sudden death.")]
        public Volume suddenDeath;

        [Header("The punches")]
        [Tooltip("Weight a full-health hit punches the hurt volume to. A smaller hit gets " +
                 "less, by the square root of its share -- the same curve the camera's " +
                 "shake uses, so a dagger and a warhammer differ on screen by as much as " +
                 "they differ in the fight.")]
        [Range(0f, 1f)] public float hurtWeight = 1f;

        [Tooltip("And for a blow this fighter lands. Smaller than taking one: connecting " +
                 "should feel good, not like being hurt.")]
        [Range(0f, 1f)] public float landedWeight = 0.75f;

        [Tooltip("Weight per second the punches let go at. 6 is about a sixth of a second, " +
                 "which is a long hit stop -- so the screen is still coming back as the " +
                 "bodies start moving again.")]
        [Range(1f, 20f)] public float punchDecay = 6f;

        [Header("The held looks")]
        [Tooltip("Weight the desperation look settles at.")]
        [Range(0f, 1f)] public float desperateWeight = 1f;

        [Tooltip("Seconds the held looks take to come up and go away.\n\n" +
                 "Not instant, and that is the whole difference between this and a flash: " +
                 "desperation is a condition and conditions arrive. It also covers the one " +
                 "case an instant switch cannot -- being healed back above the threshold, " +
                 "or a new round dealing full health, which would otherwise snap the " +
                 "screen back on a single frame.")]
        [Range(0.05f, 3f)] public float holdFade = 0.45f;

        [Tooltip("Weight sudden death settles at.")]
        [Range(0f, 1f)] public float suddenWeight = 1f;

        [Tooltip("Pulses per second the desperation look breathes at. 0 holds it still.\n\n" +
                 "Shallow on purpose -- it moves between 85% and 100% of the weight above, " +
                 "not between nothing and everything. A screen that throbs is a screen " +
                 "somebody turns the game off to get away from; one that is very slightly " +
                 "alive is the difference between a filter and a heartbeat.")]
        [Range(0f, 3f)] public float desperateBeat = 0.9f;

        [Header("Debug")]
        public string state = "";

        float _hurt;
        float _landed;
        float _desperate;
        float _sudden;
        PlayerController _subscribed;

        /// <summary>
        /// Share of full health below which the desperation look comes up.
        ///
        /// Off the simulation's own number rather than a copy: CombatMotor reads
        /// DesperationHealthPermille out of the live CombatConfig, and a second copy here
        /// would mean the screen and the damage disagreeing about where the threshold is
        /// the first time somebody tuned it.
        /// </summary>
        int Threshold =>
            match != null ? match.Combat.DesperationHealthPermille : 300;

        void OnEnable()
        {
            if (match == null) match = MatchState.Instance;
            if (match == null) match = FindFirstObjectByType<MatchState>();

            // Held at nothing, in case the scene was saved with a weight on one of them.
            // A volume left at full weight in the asset is a scene that opens in the colour
            // of somebody dying, which is a confusing thing to find and a very quiet one to
            // have caused.
            Write();
        }

        void OnDisable()
        {
            Unsubscribe();

            _hurt = _landed = _desperate = _sudden = 0f;
            Write();
        }

        /// <summary>
        /// Whose screen this is: the field if somebody set one, and otherwise the match's
        /// own local fighter.
        ///
        /// Asked every frame rather than cached, because the answer CHANGES: the arena is
        /// built with fighter one holding the controls on both devices, and NetMatch moves
        /// the whole point of view over to fighter two on the guest's phone the moment the
        /// clock is taken. Anything that cached this in Awake would spend every online
        /// match as the guest reporting the host's health.
        /// </summary>
        PlayerController Mine()
        {
            if (player != null) return player;
            if (match == null || match.fighters == null) return null;

            int me = match.LocalFighter;
            return me >= 0 && me < match.fighters.Count ? match.fighters[me] : null;
        }

        /// <summary>The other fighter, so a blow this one LANDS has something to hear it.</summary>
        PlayerController Theirs(PlayerController mine)
        {
            if (match == null || match.fighters == null || mine == null) return null;

            for (int i = 0; i < match.fighters.Count; i++)
            {
                PlayerController other = match.fighters[i];
                if (other != null && other != mine) return other;
            }
            return null;
        }

        void Subscribe(PlayerController mine)
        {
            if (_subscribed == mine) return;

            Unsubscribe();
            _subscribed = mine;
            if (_subscribed == null) return;

            _subscribed.Hurt += OnHurt;

            _opponent = Theirs(mine);
            if (_opponent != null) _opponent.Hurt += OnOpponentHurt;
        }

        void Unsubscribe()
        {
            if (_subscribed != null) _subscribed.Hurt -= OnHurt;
            if (_opponent != null) _opponent.Hurt -= OnOpponentHurt;
            _subscribed = null;
            _opponent = null;
        }

        PlayerController _opponent;

        void OnHurt(int damage) =>
            _hurt = Mathf.Max(_hurt, hurtWeight * Mathf.Sqrt(Share(damage)));

        void OnOpponentHurt(int damage) =>
            _landed = Mathf.Max(_landed, landedWeight * Mathf.Sqrt(Share(damage)));

        /// <summary>
        /// How big a hit was, as a share of full health, off the damage DEALT.
        ///
        /// Not off the weapon, for the reason SideViewCamera gives about its own shake:
        /// armour and a glancing blow scale the number down, and what the screen does
        /// should match what happened rather than what was swung.
        /// </summary>
        float Share(int damage)
        {
            int full = _subscribed != null ? _subscribed.MaxHealth : 100;
            return full > 0 ? Mathf.Clamp01(damage / (float)full) : 0f;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            PlayerController mine = Mine();
            Subscribe(mine);

            _hurt = Mathf.Max(0f, _hurt - punchDecay * dt);
            _landed = Mathf.Max(0f, _landed - punchDecay * dt);

            bool desperateNow = false;
            bool suddenNow = false;

            if (mine != null && Application.isPlaying)
            {
                PlayerSimState s = mine.State;
                int full = Mathf.Max(1, mine.MaxHealth);

                // Dead counts as desperate rather than clearing it. A fighter whose health
                // has just reached zero is the one moment the screen most obviously should
                // not go back to looking fine, and the outcome sequence takes a second and
                // a half to play out over the top of it.
                desperateNow = s.Health * 1000 <= full * Threshold;

                suddenNow = match != null &&
                            match.Match.Phase == MatchPhase.SuddenDeath;
            }

            // Eased both ways, off one number. MoveTowards and not SmoothDamp: a held look
            // wants a predictable length -- holdFade seconds, corner to corner -- and a
            // critically damped spring never quite arrives, so a volume driven by one would
            // sit at a weight of 0.98 for ever with nothing to say why.
            float step = dt / Mathf.Max(0.05f, holdFade);
            _desperate = Mathf.MoveTowards(_desperate, desperateNow ? desperateWeight : 0f,
                                           step);
            _sudden = Mathf.MoveTowards(_sudden, suddenNow ? suddenWeight : 0f, step);

            Write();
        }

        /// <summary>
        /// The four weights onto the four volumes. The only thing this component does to
        /// the render pipeline, and the whole reason it cannot break a profile.
        /// </summary>
        void Write()
        {
            float breath = 1f;
            if (desperateBeat > 0f)
            {
                float beat = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * desperateBeat *
                                                     Mathf.PI * 2f);
                breath = Mathf.Lerp(0.85f, 1f, beat);
            }

            Set(hurt, _hurt);
            Set(landed, _landed);
            Set(desperate, _desperate * breath);
            Set(suddenDeath, _sudden);

            state = $"hurt {_hurt:0.00}  landed {_landed:0.00}  "
                  + $"desperate {_desperate:0.00}  sudden {_sudden:0.00}";
        }

        /// <summary>
        /// One weight, written only when it would differ.
        ///
        /// Volume.weight is a plain field and writing it is free, but it is also what the
        /// pipeline reads to decide whether this volume takes part in the frame's blend at
        /// all -- and a volume pinned at exactly zero is skipped. Rounding the tail of a
        /// decay to zero is therefore worth the line: without it every arena pays for four
        /// volumes blending at a weight of one ten-thousandth for the rest of the match.
        /// </summary>
        static void Set(Volume volume, float weight)
        {
            if (volume == null) return;

            float want = weight < 0.001f ? 0f : Mathf.Clamp01(weight);
            if (!Mathf.Approximately(volume.weight, want)) volume.weight = want;
        }
    }
}

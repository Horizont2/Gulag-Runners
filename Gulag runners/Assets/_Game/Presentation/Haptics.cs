using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The one place the phone answers the hands.
    ///
    /// Before this there was nothing: docs/38 counted the mentions of vibration in 42,000
    /// lines of game code and found none. On a touchscreen that is a whole feedback channel
    /// left switched off, and in THIS game it is the second channel rather than a third one.
    /// docs/01 builds the match on not being able to see the other man, so sound is already
    /// carrying most of the information -- and sound has one weakness touch does not: it is
    /// ambiguous about WHOSE event it was. A hit you hear might be his blow landing on
    /// nobody. A buzz in your palm is always yours.
    ///
    /// Which is also the rule this file is written around, and the one that makes it worth
    /// reading before changing: NOTHING HERE MAY SPEAK FOR THE OTHER FIGHTER. Every signal
    /// below is read off <see cref="MatchState.LocalFighter"/> or subscribed on that one
    /// controller, and the single place a player index is compared -- the chest -- compares
    /// it against that. A buzz for his blow landing on him is not merely noise: in a game
    /// built on not seeing him, it is false information about where he is, which is worse
    /// than silence. docs/refs/haptics_check.py keeps that rule.
    ///
    /// THE VOCABULARY, and it is a vocabulary rather than a buzz on everything. Eleven feels,
    /// each a duration and a strength, and the hand can tell them apart because they differ
    /// along both axes at once:
    ///
    ///   LANDED   12 ms, light      A blow of yours connected. The shortest thing here: a
    ///                              tick, not a buzz. Crisp is what landing should feel like.
    ///   GRAZE    28 ms             A hit you took that barely counted.
    ///   HIT      45 ms, strong     A hit you took.
    ///   CRUSHER  70 ms, full       A hit you took that nearly ended it. Longer AND harder
    ///                              than anything you can do to him, on purpose: docs/38's
    ///                              table has taking a blow softer than landing one, and that
    ///                              is the one line of it this file disagrees with. Landing
    ///                              should read as crisp; being hit should read as a SHOCK,
    ///                              and a shock is a hard onset you did not choose.
    ///   PARRY    14 + 26 ms        Blade on blade. The only two-pulse feel in the fight, so
    ///                              it is unmistakable even on hardware with no strength
    ///                              control at all, where rhythm is the only axis left. It is
    ///                              the hardest input in the game and docs/02 makes it the
    ///                              whole reason to hold a weapon up, so it gets the best
    ///                              thing the phone can do.
    ///   GUARD    22 ms, weak       A block that held. Shorter and much weaker than a parry:
    ///                              it stopped, but nothing happened to him.
    ///   VENT     260 ms            The gas coming out of the floor. Long, and deliberately
    ///                              NOT at full strength -- it is the arena, not a blow, and
    ///                              it must not be mistaken for one.
    ///   THROB    16 ms, faintest   The surface near your boots, replayed faster the closer it
    ///                              gets. The lightest thing in the vocabulary because it is
    ///                              pressure rather than an event.
    ///   CHEST    10 + 18 ms        A crate came open: a latch giving, then opening. Rising,
    ///                              which is what a reward feels like.
    ///   REFUSED  55 ms, weak       The lock said no. Long and soft, which nothing else is,
    ///                              so it reads as dull -- docs/38 asks for exactly that, and
    ///                              docs/04 makes a wrong touch LOUD, so the sound of your
    ///                              mistake goes to him too. The buzz is the only part of it
    ///                              that is private.
    ///   DEATH    420 ms, full      Yours. One long, as docs/38 asks.
    ///
    /// Presentation only, and read-only against the simulation (docs/06). It subscribes and
    /// it samples; there is no path in this file that writes a byte of simulation state, and
    /// it finds what it needs itself rather than having other components call it -- so
    /// deleting it changes nothing else.
    ///
    /// THE OFF SWITCH is required, not a courtesy: docs/02's "Обов'язкові налаштування" lists
    /// вібровідгук among the six settings the game must ship. It lives on this class as a
    /// static backed by PlayerPrefs, because the screen that will flip it is in the MENU
    /// scene where no arena and no Haptics exist, so the saved key has to be the authority
    /// rather than a field on a component. It is deliberately NOT in
    /// <see cref="GulagRunners.Meta.PlayerProgress"/>: that file is the ladder, the purse and
    /// the wardrobe, it is versioned at 6 with a migration ladder behind it, and a device
    /// preference riding that save would be thrown away by the next version bump. See the
    /// note on <see cref="Wanted"/>.
    ///
    /// WHY THERE IS A NATIVE LAYER AT THE BOTTOM OF THIS FILE. Handheld.Vibrate is useless
    /// here and does not appear anywhere in it: on Android it is one fixed buzz of about half
    /// a second with no duration and no strength, and a fight needs taps of twelve
    /// milliseconds. So Android is driven through Vibrator and VibrationEffect directly, and
    /// every one of the eleven feels above is built ONCE, in Awake, and kept -- see
    /// <see cref="Device"/> for why that matters and what each platform really does.
    /// </summary>
    [DefaultExecutionOrder(115)]      // after the gas warning (113) it reads the surface from
    [DisallowMultipleComponent]
    public sealed class Haptics : MonoBehaviour
    {
        /// <summary>
        /// What the phone can say. The order is the order of the built table and nothing
        /// else; Count is the size of it and is never a feel.
        /// </summary>
        public enum Feel
        {
            Landed = 0,
            Graze = 1,
            Hit = 2,
            Crusher = 3,
            Parry = 4,
            Vent = 6,
            Throb = 7,
            Chest = 8,
            Refused = 9,
            Death = 10,
            Count = 11,
        }

        /// <summary>
        /// One feel: a pulse, and optionally a second one after a gap.
        ///
        /// Two pulses rather than an arbitrary pattern because two is what the hand can
        /// actually count on a motor this size, and because every feel in the table fits in
        /// it -- so there is one shape to build, one shape to tune and one shape to degrade
        /// when the hardware cannot do strength.
        ///
        /// Milliseconds and 0-255, which is what Android's own API takes, so nothing is
        /// converted between the inspector and the device.
        /// </summary>
        [System.Serializable]
        public struct Buzz
        {
            [Tooltip("How long the first pulse lasts, in milliseconds. Zero silences this "
                     + "feel entirely, which is how you mute one without turning the whole "
                     + "thing off. Under about 10 ms nothing is felt at all; over about 100 "
                     + "it stops being an event and becomes a rumble.")]
            public int ms;

            [Tooltip("How hard, 1 to 255. Hardware older than Android 8, which this project "
                     + "still supports, ignores this completely and plays everything at full "
                     + "-- so no feel may rely on strength ALONE to be told apart from "
                     + "another. Duration and rhythm are the axes that always survive.")]
            public int strength;

            [Tooltip("Milliseconds of silence before the second pulse. Zero means there is "
                     + "no second pulse and this feel is a single tap.")]
            public int gap;

            [Tooltip("How long the second pulse lasts. Ignored when the gap is zero.")]
            public int thenMs;

            [Tooltip("How hard the second pulse is, 1 to 255. Ignored when the gap is zero.")]
            public int thenStrength;
        }

        [Header("Wiring")]
        [Tooltip("The round. Leave empty to use the MatchState in the scene.\n\n"
                 + "Everything here hangs off it: which fighter is mine, which phase the "
                 + "match is in, and whether a chest was opened by me or by him.")]
        public MatchState matchState;

        [Tooltip("Whose hands these are. Leave empty for the local fighter -- which is the "
                 + "right answer, and the only one this file is allowed to give. The other "
                 + "man gets his own buzzes on his own phone.")]
        public PlayerController player;

        [Tooltip("The gas, for the throb as it nears your boots. Leave empty to find the one "
                 + "in the scene; without a pool that one feel is simply silent.")]
        public PoisonPool pool;

        [Header("A blow of yours that landed")]
        [Tooltip("Crisp and the shortest thing in the table. Landing a blow is confirmation, "
                 + "not an impact on you: the hit stop and the sound are already carrying the "
                 + "weight of it, and all the hand has to add is that it connected.")]
        public Buzz landed = new Buzz { ms = 12, strength = 120 };

        [Header("A blow you took")]
        [Tooltip("The share of full health a hit has to take to stop being a graze, and then "
                 + "to count as one that nearly ended it. Two numbers rather than a curve "
                 + "because the hand reads rungs and not ramps -- and because three rungs can "
                 + "be BUILT once and kept, where a strength that varied continuously would "
                 + "mean a new native object every single blow.")]
        public Vector2 hitRungs = new Vector2(0.08f, 0.22f);

        [Tooltip("A hit that barely counted. Still sharper than landing one.")]
        public Buzz graze = new Buzz { ms = 28, strength = 150 };

        [Tooltip("An ordinary hit. Longer and harder than anything you can do to him.")]
        public Buzz hit = new Buzz { ms = 45, strength = 205 };

        [Tooltip("A hit that nearly finished it: the longest and hardest single pulse here. "
                 + "This is the one thing in the game that should make somebody flinch.")]
        public Buzz crusher = new Buzz { ms = 70, strength = 255 };

        [Header("The guard")]
        [Tooltip("Blade on blade, and the best thing the phone does: a hard click, a beat, "
                 + "and a fuller knock. The only double in the fight, so it is still "
                 + "unmistakable on hardware that cannot do strength at all.")]
        public Buzz parry = new Buzz
        {
            ms = 14, strength = 255, gap = 26, thenMs = 26, thenStrength = 200,
        };


        [Header("The gas (docs/33)")]
        [Tooltip("The vent, on the tick the flood begins. Long, and NOT at full strength: it "
                 + "is the arena taking itself away, and it must not read as having been hit. "
                 + "It lands on the same tick as PoisonWarning's one shove of the camera.")]
        public Buzz vent = new Buzz { ms = 260, strength = 170 };

        [Tooltip("The surface near your boots, replayed faster the closer it gets. The "
                 + "faintest thing in the table -- this is pressure, not an event, and the "
                 + "moment it reads as a blow it is wrong.")]
        public Buzz throb = new Buzz { ms = 16, strength = 90 };

        [Tooltip("How high above your feet the surface has to be before the throb starts, in "
                 + "metres. Tighter than PoisonWarning's seven on purpose: the lamps and the "
                 + "collar have seven metres to say it is COMING, and the hand is for when it "
                 + "is at your boots.")]
        [Range(0.5f, 12f)] public float throbFrom = 3f;

        [Tooltip("Throbs a second at the edge of that range, and at your boots. The upper "
                 + "number is capped low on purpose: past about four a second a motor stops "
                 + "being a rhythm and becomes a continuous buzz, which reads as a broken "
                 + "phone and costs real battery.")]
        public Vector2 throbRate = new Vector2(0.9f, 3.2f);

        [Header("Loot (docs/03, docs/04)")]
        [Tooltip("A crate came open: a latch giving, then opening. Rising, because that is "
                 + "what a reward feels like -- and because prying a chest is the most "
                 + "exposed thing you do in a match, so the payoff is worth a feel of its own.")]
        public Buzz chest = new Buzz
        {
            ms = 10, strength = 90, gap = 50, thenMs = 18, thenStrength = 150,
        };

        [Tooltip("The lock sulking after a wrong touch. Long and soft, which nothing else in "
                 + "the table is, so it reads as dull rather than as damage.")]
        public Buzz refused = new Buzz { ms = 55, strength = 130 };

        [Header("The end")]
        [Tooltip("Your own death, and nobody else's. The one feel allowed through after the "
                 + "round is over, because it IS the end of the round rather than something "
                 + "happening behind the result screen.")]
        public Buzz death = new Buzz { ms = 420, strength = 255 };

        [Header("Debug")]
        [Tooltip("Says in the console what the native layer actually managed on this device: "
                 + "which API it got, whether the motor can do strength, or why it gave up. "
                 + "Read it first when the phone is quiet on a device that should buzz.")]
        public bool log;

        /// <summary>The key the player's answer is saved under. Its own, for the reason in
        /// the class summary: this is a device preference and not progress.</summary>
        const string Key = "gr.haptics.on";

        /// <summary>-1 before the saved answer has been read; 0 off, 1 on after.</summary>
        static int _wanted = -1;

        /// <summary>
        /// Does the player want this at all -- the whole of docs/02's required switch.
        ///
        /// Static and PlayerPrefs-backed so that the settings screen can flip it from the
        /// MENU scene, where there is no arena, no MatchState and no instance of this to
        /// hold the answer. A component-shaped flag would have meant the switch only existed
        /// while a match was running, which is the one place it cannot be reached.
        ///
        /// ON until somebody says otherwise, and that default is written here rather than as
        /// a field on the component for the same reason: an arena's inspector cannot be the
        /// authority on a question the menu asks before any arena is loaded, or the answer
        /// would depend on which scene happened to ask first.
        ///
        /// Read from PlayerPrefs exactly once per process and cached, because this is tested
        /// on the way to every buzz and PlayerPrefs is a native call -- cheap, but not cheap
        /// enough to do several times a second for the life of a match.
        /// </summary>
        public static bool Wanted
        {
            get
            {
                if (_wanted < 0) _wanted = PlayerPrefs.GetInt(Key, 1) != 0 ? 1 : 0;
                return _wanted == 1;
            }
            set
            {
                _wanted = value ? 1 : 0;
                PlayerPrefs.SetInt(Key, _wanted);
                PlayerPrefs.Save();
            }
        }

        /// <summary>For a UnityEvent or a MenuSwitch: the switch itself.</summary>
        public void Set(bool on) => Wanted = on;

        /// <summary>For a button with no argument to give.</summary>
        public void Toggle() => Wanted = !Wanted;

        /// <summary>What the native layer managed, for a settings screen to show.</summary>
        public static string Hardware => Device.What;

        /// <summary>
        /// Whether this device can do any of this at all, so a settings screen can grey the
        /// switch out instead of offering a control that changes nothing. False in the
        /// editor, on iOS and on a phone with no motor.
        /// </summary>
        public static bool Available => Device.Live;

        /// <summary>
        /// Which component currently owns the one motor in the phone.
        ///
        /// There is one <see cref="Device"/> because there is one vibrator, and the Java
        /// references it holds have to be released exactly once. Without this, loading an
        /// arena on top of another in the wrong order -- new Awake first, old OnDestroy
        /// second -- would have the departing component tear down the arriving one's effects
        /// and leave a silent phone with nothing in the log to explain it.
        /// </summary>
        static Haptics _owner;

        Buzz[] _table;
        PlayerController _bound;
        int _swing = -1;        // the last swing of ours already spoken for
        int _clash;             // last tick's clash lock, to catch it rising
        int _clashAt = -1;      // the simulation frame the last parry was reported on
        int _chestAt = -1;      // which chest the sulk below belongs to
        int _stun;              // last frame's sulk, to catch IT rising
        float _beat;            // where the gas throb is in its cycle

        void Awake()
        {
            if (matchState == null) matchState = FindFirstObjectByType<MatchState>();
            if (pool == null) pool = FindFirstObjectByType<PoisonPool>();

            _owner = this;
            Rebuild();
        }

        /// <summary>
        /// Builds every feel into whatever the hardware actually takes, once.
        ///
        /// Here rather than on the first buzz, and that is the whole performance story of
        /// this file: a VibrationEffect is a Java object, and building one per blow would
        /// allocate on every exchange of every fight. Eleven are built at Awake, kept, and
        /// handed to the motor by index after that. On the menu it is also the one place a
        /// retune takes effect, which is why it is on the context menu.
        /// </summary>
        [ContextMenu("Rebuild the feels")]
        public void Rebuild()
        {
            // A pair in the wrong order would make every graze a crusher, which is the one
            // way this table can be mistuned into being worse than silence.
            if (hitRungs.y < hitRungs.x) hitRungs.y = hitRungs.x;

            _table = Table();
            Device.Build(_table);
            if (log) Debug.Log($"{name}: haptics — {Device.What}", this);
        }

        Buzz[] Table()
        {
            Buzz[] t = new Buzz[(int)Feel.Count];
            t[(int)Feel.Landed] = landed;
            t[(int)Feel.Graze] = graze;
            t[(int)Feel.Hit] = hit;
            t[(int)Feel.Crusher] = crusher;
            t[(int)Feel.Parry] = parry;
            t[(int)Feel.Vent] = vent;
            t[(int)Feel.Throb] = throb;
            t[(int)Feel.Chest] = chest;
            t[(int)Feel.Refused] = refused;
            t[(int)Feel.Death] = death;
            return t;
        }

        void OnEnable()
        {
            if (matchState == null) return;
            matchState.PhaseBegan += OnPhase;
            matchState.ChestOpened += OnChest;
            matchState.RoundBegan += OnRound;
        }

        void OnDisable()
        {
            if (matchState != null)
            {
                matchState.PhaseBegan -= OnPhase;
                matchState.ChestOpened -= OnChest;
                matchState.RoundBegan -= OnRound;
            }
            Bind(null);
            _beat = 0f;
        }

        void OnDestroy()
        {
            if (_owner != this) return;        // see the note on _owner
            _owner = null;
            Device.Shutdown();
        }

        /// <summary>
        /// The fighter this device's thumbs are on, asked fresh rather than cached.
        ///
        /// The same question <see cref="PoisonWarning"/> asks and for the same reason: the
        /// answer can CHANGE. MatchState re-decides it as each round is dealt unless a wire
        /// has spoken, so a reference taken once in Awake is a reference that can end up
        /// pointing at the opponent -- which is the exact fault docs/refs/local_fighter_check
        /// exists for, and the one fault this file must never have.
        /// </summary>
        PlayerController Local()
        {
            if (player != null) return player;
            if (matchState == null || matchState.fighters == null) return null;
            int at = matchState.LocalFighter;
            return at >= 0 && at < matchState.fighters.Count ? matchState.fighters[at] : null;
        }

        /// <summary>
        /// Follows the local fighter from one round to the next, subscribing to the new one
        /// and letting the old one go. Nothing is ever subscribed on the opponent.
        /// </summary>
        void Bind(PlayerController next)
        {
            if (_bound == next) return;

            if (_bound != null)
            {
                _bound.Hurt -= OnHurt;
                _bound.Died -= OnDied;
            }

            _bound = next;

            if (_bound != null)
            {
                _bound.Hurt += OnHurt;
                _bound.Died += OnDied;
            }

            Forget();
        }

        /// <summary>Every edge tracker back to "nothing has happened yet".</summary>
        void Forget()
        {
            _swing = -1;
            _clash = 0;
            _clashAt = -1;
            _chestAt = -1;
            _stun = 0;
            _beat = 0f;
        }

        void LateUpdate()
        {
            PlayerController mine = Local();
            Bind(mine);
            if (mine == null || matchState == null) return;

            PlayerSimState s = mine.State;

            // ---- a blow of OURS that landed
            //
            // Not DealtPermille, which is the obvious field and the wrong one: it lasts a
            // single tick and is cleared at the front of the next. A frame runs as many
            // ticks as it has time for (up to eight), so on a phone holding 30 fps -- which
            // docs/06 budgets for -- every frame runs TWO ticks and a flag sampled here
            // would miss roughly half of every player's hits. Half the blows buzzing is
            // worse than none: that reads as a broken phone rather than a missing feature.
            //
            // SpentOn is latched instead. The simulation sets it to the current SwingId when
            // a swing draws blood and leaves it there until the arm reverses into the next
            // swing, so the fact outlives any frame rate -- and SwingId is then the perfect
            // key to report it once against, because "one buzz per swing" is the fight's own
            // rule for hits rather than one invented here.
            if (s.SpentOn == s.SwingId && s.SwingId != _swing)
            {
                _swing = s.SwingId;
                Say(Feel.Landed);
            }

            // ---- blade on blade
            //
            // Also an edge off a latch, for the same reason, and off ClashLock rather than
            // WasClash: the lock runs ten ticks, which no frame can step over, while the
            // flag lasts one. Blades cannot clash again while the lock is up, so any RISE
            // in it is a fresh parry.
            //
            // The frame guard behind it is for docs/06's rollback, which is not here yet --
            // the link is lockstep today. What it earns right now is the other half of the
            // same problem: above 60 fps plenty of frames run NO ticks at all, and without
            // it the same parry would be reported again on each of them.
            if (s.ClashLock > _clash && matchState.Match.Frame > _clashAt)
            {
                _clashAt = matchState.Match.Frame;
                Say(Feel.Parry);
            }
            _clash = s.ClashLock;

            Refusal(in s);
            Rising(mine);
        }

        /// <summary>
        /// The lock sulking, on the chest THIS fighter has open and no other.
        ///
        /// docs/04 makes a wrong touch cost a second of the lock taking nothing, and the
        /// simulation says so by standing PuzzleStun up to its full count -- so a rise in it
        /// is a wrong touch, on the one chest this fighter's own hands are on.
        ///
        /// The chest index is remembered with it, and a change of chest SEEDS the tracker
        /// rather than zeroing it. Zeroing was wrong in a way worth keeping a note about:
        /// step away from a sulking lock and back within the second and the sulk it is still
        /// serving would read as a brand new mistake.
        /// </summary>
        void Refusal(in PlayerSimState s)
        {
            ChestSimState[] chests = matchState.ChestStates;
            int at = s.OpeningChest;

            if (chests == null || at < 0 || at >= chests.Length)
            {
                _chestAt = -1;
                _stun = 0;
                return;
            }

            int now = chests[at].PuzzleStun;

            if (at != _chestAt)
            {
                _chestAt = at;
                _stun = now;
                return;
            }

            if (now > _stun) Say(Feel.Refused);
            _stun = now;
        }

        /// <summary>
        /// How close the gas is to YOUR feet, as a quickening throb.
        ///
        /// This is the one signal in the match that is only about you -- the flood is the
        /// same height for both fighters, how near it is to your boots is not -- which is
        /// exactly why PoisonWarning spends its third beat on it and why it is worth a feel
        /// here as well. And the hands are the only channel that is otherwise silent about
        /// it: the bite deliberately does NOT set WasHit, so no amount of wading raises the
        /// Hurt event, and without this the gas would take a fighter's whole health without
        /// the phone ever mentioning it.
        ///
        /// The waterline is asked of the pool rather than worked out again from the rise, for
        /// the reason PoisonPool gives: two places computing one height is two places that
        /// can disagree, and what would disagree is what you are warned by against what
        /// bites you.
        /// </summary>
        void Rising(PlayerController mine)
        {
            if (pool == null || matchState.Match.Phase < MatchPhase.Closing
                || !matchState.Poison.On)
            {
                _beat = 0f;
                return;
            }

            float gap = mine.transform.position.y - pool.SurfaceY();

            // Well under it there is nothing left to warn about, and a throb from the bottom
            // of a flooded arena is a buzz with no information in it.
            if (gap > throbFrom || gap < -8f)
            {
                _beat = 0f;
                return;
            }

            // SCALED time, where NetNotice deliberately uses unscaled for its held line. The
            // difference is what each is tracking: a line on the HUD has to outlive a stopped
            // clock, while this is tracking the gas -- and MatchOutcomeView stops the clock
            // outright, at which point the flood is not rising and a throb saying it is would
            // be the phone inventing a tide.
            float near = gap <= 0f ? 1f : 1f - Mathf.Clamp01(gap / throbFrom);
            _beat += Time.deltaTime * Mathf.Lerp(throbRate.x, throbRate.y, near);
            if (_beat < 1f) return;

            _beat -= 1f;
            Say(Feel.Throb);
        }

        /// <summary>
        /// A hit that got through, scaled onto one of three rungs.
        ///
        /// Rungs and not a ramp, for two reasons that happen to agree. A palm cannot tell 183
        /// from 197 on a motor this size, so a continuous strength is a precision nobody
        /// feels; and three fixed rungs can be built once at Awake and kept, where a strength
        /// worked out per blow would mean constructing a native object on every exchange.
        /// </summary>
        void OnHurt(int damage)
        {
            PlayerController mine = _bound;

            // The swing we are in has just been spent by being HIT rather than by landing --
            // CombatMotor does that so a man cannot swing on through a blow -- and the latch
            // this file reads for "a blow of ours landed" cannot tell the two apart. So the
            // swing is marked as spoken for here, which is exactly right: the fight itself
            // guarantees a swing spent this way can never also land.
            if (mine != null) _swing = mine.State.SwingId;

            int full = mine != null ? mine.MaxHealth : 100;
            float share = full > 0 ? damage / (float)full : 0f;

            Say(share >= hitRungs.y ? Feel.Crusher
                : share >= hitRungs.x ? Feel.Hit
                : Feel.Graze);
        }

        void OnDied() => Say(Feel.Death);

        void OnRound(int round) => Forget();

        /// <summary>The gas venting: one hard event, on the tick the phase turns.</summary>
        void OnPhase(MatchPhase phase)
        {
            if (phase != MatchPhase.Closing) return;
            if (matchState == null || !matchState.Poison.On) return;
            Say(Feel.Vent);
        }

        /// <summary>
        /// A chest was forced -- and the ONE place in this file a player index is compared,
        /// because it is the one signal the simulation announces for both fighters at once.
        /// The HUD wants to know when HE opens one (MatchState says so for exactly that
        /// reason); the hand must not, and a buzz for his crate would be the phone claiming
        /// something about a man you cannot see.
        /// </summary>
        void OnChest(int who, int which)
        {
            if (matchState == null || who != matchState.LocalFighter) return;
            Say(Feel.Chest);
        }

        /// <summary>
        /// THE ONLY WAY TO THE MOTOR. Every feel above arrives here and nothing else in this
        /// file touches <see cref="Device"/>, so there is one place the switch is tested and
        /// one place the match's own silences are kept. A second route to the hardware would
        /// be a second route around both, which is what docs/refs/haptics_check.py is
        /// checking when it counts the call sites.
        ///
        /// The silences, and why each one:
        ///
        ///   THE CAGES. docs/17 opens a round with both fighters locked in and the camera
        ///       doing its own work. Nothing in that is the player's doing, so nothing in it
        ///       is the player's to feel.
        ///   THE RESULT SCREEN. The round is decided; a phase that is over has no events
        ///       worth a hand. Death is the single exception, because it IS the end of the
        ///       round rather than something happening behind the result -- it arrives on
        ///       the tick the phase turns, and swallowing it would lose the one buzz the
        ///       whole match builds towards.
        ///
        /// The phase is read here rather than cached in LateUpdate on purpose: these handlers
        /// run from inside the simulation tick, during Update, so a cached answer would be a
        /// frame stale -- and the frame it would be stale on is precisely the one where
        /// somebody dies.
        /// </summary>
        void Say(Feel feel)
        {
            if (!Wanted) return;
            if (matchState == null) return;

            MatchPhase phase = matchState.Match.Phase;
            if (phase == MatchPhase.Cages) return;
            if (phase == MatchPhase.Over && feel != Feel.Death) return;

            Device.Play((int)feel);
        }

        // ------------------------------------------------------------------ the hardware
        //
        /// <summary>
        /// The motor, and the only code in the project that talks to one.
        ///
        /// STATIC, because there is one vibrator in a phone. Two components each holding
        /// their own handle to it would be two components fighting over one piece of
        /// hardware, and the Java objects below are global references that have to be
        /// released exactly once.
        ///
        /// WHAT IS REAL AND WHAT IS NOT, said plainly:
        ///
        ///   ANDROID is real, and it is three paths deep because the project's minimum is
        ///       genuinely low -- ProjectSettings has AndroidMinSdkVersion at 25, which is
        ///       Android 7.1, one below the release that introduced VibrationEffect. So the
        ///       ladder is not decoration:
        ///         API 26+ with amplitude control -- durations AND strengths AND rhythms.
        ///             The whole vocabulary.
        ///         API 26+ without it -- the same effects with DEFAULT_AMPLITUDE. Durations
        ///             and rhythms survive, strength does not, which is why no two feels in
        ///             the table above differ by strength alone.
        ///         API 25 -- no VibrationEffect at all. vibrate(long) and
        ///             vibrate(long[], int), both of which have existed since API 1, so
        ///             durations and rhythms still survive.
        ///   iOS is a NO-OP, deliberately and visibly. See the branch below for exactly what
        ///       plugin would fill it and why one is not shipped here.
        ///   THE EDITOR is a no-op. Note the guards are UNITY_ANDROID && !UNITY_EDITOR and
        ///       not UNITY_ANDROID: the platform symbol is defined in the editor whenever the
        ///       build target is Android, so without the second half every one of these calls
        ///       would run on a desktop with no JNI at all and throw on the first frame.
        ///
        /// NOTHING IN HERE MAY THROW OUT. A haptics call that throws takes the frame with it,
        /// and the devices this will first run on are not the ones it was written on: an
        /// unexpected API level, a missing VIBRATE permission (which raises a SecurityException
        /// rather than failing quietly), a manufacturer's own Vibrator. So every native call
        /// sits in a try, and a catch does not merely swallow -- it LATCHES the layer off, so
        /// a device that cannot do this is asked once rather than sixty times a second for the
        /// rest of the match.
        ///
        /// AND NOTHING IN HERE MAY ALLOCATE after Build. The effects are built once; the
        /// argument lists handed to the call are built once, with their numbers already boxed,
        /// so playing a feel is an index into an array and a call. The one cost left is inside
        /// Unity's own AndroidJavaObject.Call, which resolves a method signature each time; the
        /// floor below that is a cached AndroidJNI method id and a jvalue array, and it is not
        /// written here because it cannot be verified without a device. It is worth doing only
        /// if a profile ever asks: a feel plays a few times a SECOND at worst, never per frame.
        /// </summary>
        static class Device
        {
            /// <summary>Has the layer got something that works. False silences everything.</summary>
            public static bool Live => _live;

            /// <summary>What happened, for the console and for a settings screen.</summary>
            public static string What => _what;

            static bool _live;
            static string _what = "not started";

#if UNITY_ANDROID && !UNITY_EDITOR

            static AndroidJavaObject _vibrator;
            static AndroidJavaObject[] _effects;      // one per feel on API 26+, else null
            static object[][] _args;                  // one ready argument list per feel
            static int _sdk;
            static bool _amplitudes;

            public static void Build(Buzz[] table)
            {
                Shutdown();
                if (table == null) { _what = "no table to build"; return; }

                try
                {
                    using (AndroidJavaClass version =
                               new AndroidJavaClass("android.os.Build$VERSION"))
                        _sdk = version.GetStatic<int>("SDK_INT");

                    using (AndroidJavaClass player =
                               new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject activity =
                               player.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        // Android 12 put the vibrator behind a manager and deprecated the
                        // old service. The literals are Context.VIBRATOR_MANAGER_SERVICE and
                        // Context.VIBRATOR_SERVICE; written out rather than looked up,
                        // because a static field read over JNI to learn a string that has
                        // not changed since 2011 is work for nothing.
                        if (_sdk >= 31)
                        {
                            using (AndroidJavaObject manager =
                                       activity.Call<AndroidJavaObject>("getSystemService",
                                                                        "vibrator_manager"))
                            {
                                if (manager != null)
                                    _vibrator =
                                        manager.Call<AndroidJavaObject>("getDefaultVibrator");
                            }
                        }

                        if (_vibrator == null)
                            _vibrator = activity.Call<AndroidJavaObject>("getSystemService",
                                                                         "vibrator");
                    }

                    if (_vibrator == null)
                    {
                        _what = "this device has no vibrator service";
                        return;
                    }

                    if (!_vibrator.Call<bool>("hasVibrator"))
                    {
                        _what = "this device reports no vibrator";
                        Shutdown();
                        return;
                    }

                    // API 26 and up only. Asked on 25 it is a NoSuchMethodError, which the
                    // catch below would turn into a silent phone rather than a quiet one.
                    _amplitudes = _sdk >= 26 && _vibrator.Call<bool>("hasAmplitudeControl");

                    _args = new object[table.Length][];

                    if (_sdk >= 26)
                    {
                        _effects = new AndroidJavaObject[table.Length];
                        Effects(table);
                    }
                    else
                    {
                        Patterns(table);
                    }

                    _live = true;
                    _what = "Android API " + _sdk
                            + (_sdk >= 26 ? ", VibrationEffect" : ", legacy vibrate()")
                            + (_amplitudes ? ", strengths" : ", durations only");
                }
                catch (System.Exception e)
                {
                    Shutdown();
                    _what = "the vibrator could not be reached: " + e.GetType().Name;
                    Debug.LogWarning("Haptics: " + _what + " — the phone stays quiet rather "
                                     + "than risking the frame. " + e.Message);
                }
            }

            /// <summary>API 26+: one VibrationEffect per feel, built once and kept.</summary>
            static void Effects(Buzz[] table)
            {
                using (AndroidJavaClass effect = new AndroidJavaClass("android.os.VibrationEffect"))
                {
                    for (int i = 0; i < table.Length; i++)
                    {
                        Buzz b = table[i];
                        if (b.ms <= 0) continue;        // a feel deliberately muted

                        AndroidJavaObject made = Two(in b)
                            ? effect.CallStatic<AndroidJavaObject>(
                                  "createWaveform",
                                  new long[] { 0L, b.ms, b.gap, b.thenMs },
                                  new int[] { 0, Amp(b.strength), 0, Amp(b.thenStrength) },
                                  -1)
                            : effect.CallStatic<AndroidJavaObject>(
                                  "createOneShot", (long)b.ms, Amp(b.strength));

                        _effects[i] = made;

                        // The argument list, built HERE and reused for the life of the
                        // match. Passing an object[] that already exists to a params method
                        // hands that same array through, so playing a feel allocates neither
                        // the array nor a box for anything inside it.
                        _args[i] = new object[] { made };
                    }
                }
            }

            /// <summary>
            /// API 25 and below: no VibrationEffect, so the arguments are the numbers
            /// themselves -- boxed once, here, rather than on every blow.
            /// </summary>
            static void Patterns(Buzz[] table)
            {
                for (int i = 0; i < table.Length; i++)
                {
                    Buzz b = table[i];
                    if (b.ms <= 0) continue;

                    _args[i] = Two(in b)
                        ? new object[] { new long[] { 0L, b.ms, b.gap, b.thenMs }, -1 }
                        : new object[] { (long)b.ms };
                }
            }

            static bool Two(in Buzz b) => b.gap > 0 && b.thenMs > 0;

            /// <summary>
            /// A strength the hardware will accept. Without amplitude control the only legal
            /// answer is DEFAULT_AMPLITUDE, which is -1 and means "however hard this motor
            /// goes" -- so the feel keeps its length and loses its weight, which is the
            /// degradation the table above is designed to survive.
            /// </summary>
            static int Amp(int strength)
            {
                if (!_amplitudes) return -1;
                return strength < 1 ? 1 : strength > 255 ? 255 : strength;
            }

            public static void Play(int feel)
            {
                if (!_live || _args == null) return;
                if (feel < 0 || feel >= _args.Length) return;

                object[] args = _args[feel];
                if (args == null) return;              // this feel is muted

                try
                {
                    _vibrator.Call("vibrate", args);
                }
                catch (System.Exception e)
                {
                    // Latched, not swallowed. Whatever this device objects to, it will object
                    // to it every time, and asking again on the next blow would be a thrown
                    // exception per blow for the rest of the match.
                    _live = false;
                    _what = "the vibrator refused a call and has been switched off";
                    Debug.LogWarning("Haptics: " + _what + ". " + e.Message);
                }
            }

            public static void Shutdown()
            {
                _live = false;

                try
                {
                    if (_effects != null)
                    {
                        for (int i = 0; i < _effects.Length; i++)
                        {
                            if (_effects[i] == null) continue;
                            _effects[i].Dispose();
                            _effects[i] = null;
                        }
                    }

                    if (_vibrator != null) _vibrator.Dispose();
                }
                catch (System.Exception e)
                {
                    // Letting go of a Java reference is not worth a crash on the way out of
                    // a scene.
                    Debug.LogWarning("Haptics: the vibrator did not let go cleanly. "
                                     + e.Message);
                }

                _effects = null;
                _args = null;
                _vibrator = null;
            }

#elif UNITY_IOS && !UNITY_EDITOR

            // iOS DOES NOTHING, and that is the honest answer rather than a pretended one.
            //
            // The decent haptics on this platform are UIKit's feedback generators --
            // UIImpactFeedbackGenerator, UINotificationFeedbackGenerator,
            // UISelectionFeedbackGenerator -- and all three are Objective-C with no C#
            // surface at all. They need a plugin. One is not shipped here for a reason worth
            // stating: it cannot be compiled, linked or FELT from this environment, and a
            // .mm file with a hand-written .meta that includes itself in the wrong build is a
            // broken build, which is strictly worse than a quiet phone.
            //
            // WHAT WOULD FILL IT. Assets/Plugins/iOS/GulagHaptics.mm, iOS-only in its
            // importer settings, exporting four C functions:
            //
            //     void GulagHapticsPrepare(void);
            //     void GulagHapticsImpact(int style);   // 0 light 1 medium 2 heavy
            //                                           // 3 soft  4 rigid
            //     void GulagHapticsNotify(int kind);    // 0 success 1 warning 2 error
            //     void GulagHapticsSelect(void);
            //
            // each holding its generator in a file static and calling -prepare on it, because
            // an unprepared generator is 100-200 ms late and a late haptic is a wrong one.
            // The C# side is one [DllImport("__Internal")] per function inside this branch.
            //
            // AND THE MAPPING IS BY STYLE, NOT BY MILLISECONDS, which is the part worth
            // knowing before anybody writes it: UIFeedbackGenerator has no duration and no
            // amplitude, so the table in this class cannot be expressed through it. It
            // collapses to:
            //
            //     Landed -> impact light      Graze  -> impact soft
            //     Hit    -> impact medium     Crusher-> impact heavy
            //     Parry  -> notify success    Guard  -> impact rigid
            //     Vent   -> notify warning    Throb  -> selection
            //     Chest  -> notify success    Refused-> notify error
            //     Death  -> notify error
            //
            // Which loses the thing this vocabulary is built on: that the length of a feel
            // says how much of it there was. The only iOS API that keeps it is Core Haptics
            // (CHHapticEngine, iOS 13+), where a CHHapticEvent carries intensity and
            // sharpness and a continuous event carries a duration -- a bigger plugin, and
            // the only one that would make iOS feel like Android here.

            public static void Build(Buzz[] table)
            {
                _live = false;
                _what = "iOS: no haptics plugin in this build (see the note in Haptics.cs)";
            }

            public static void Play(int feel)
            {
            }

            public static void Shutdown()
            {
                _live = false;
            }

#else

            // The editor -- including the editor with Android as the build target, which is
            // exactly why the guards above say !UNITY_EDITOR -- and any platform with no
            // motor. There is nothing to drive, so there is nothing to do and nothing that
            // can throw.
            //
            // The component above still does all of its own work in play mode: it still
            // assembles the table, still watches the fight, still decides which feel an
            // event deserves and still refuses the ones that are not the local player's.
            // Only the last line of it goes nowhere. So everything in this feature except
            // the buzz itself can be tested in the editor, and the buzz can only be judged
            // on a phone.

            public static void Build(Buzz[] table)
            {
                _live = false;
                _what = "the editor, or a platform with no motor";
            }

            public static void Play(int feel)
            {
            }

            public static void Shutdown()
            {
                _live = false;
            }

#endif
        }
    }
}

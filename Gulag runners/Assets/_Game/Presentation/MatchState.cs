using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The part of a round that both players share: the chests.
    ///
    /// The arena geometry is immutable and lives in <see cref="SimWorldBaker"/>. Chests are not —
    /// one of them coming open is a thing that happened, and both players have to agree on it.
    /// That state lives here, as one array parallel to the baked chest list.
    ///
    /// Two players currently tick independently, which works because of the rule in
    /// <see cref="LootMotor"/>: a chest has one opener and only its opener may change it. When
    /// rollback arrives (docs/06) the whole match steps from one place and this becomes the
    /// object that is snapshotted; nothing about the chest rules has to change for that.
    /// </summary>
    [DefaultExecutionOrder(-60)]     // after the baker (-100), before the players (-50)
    [DisallowMultipleComponent]
    public sealed class MatchState : MonoBehaviour
    {
        public static MatchState Instance { get; private set; }

        [Header("Wiring")]
        [Tooltip("The baker whose chest list this state belongs to. Leave empty to use the one " +
                 "in the scene.")]
        public SimWorldBaker worldBaker;

        [Tooltip("Optional. The item data files; leave empty to use the table compiled into " +
                 "ItemTable.cs, which is what the game read before any of these existed.\n\n" +
                 "Applied once in Awake, before the first tick. Both devices must hold the " +
                 "same library or they will disagree about what a sword does (docs/06) — the " +
                 "same rule any data file follows, and the reason this is one asset rather " +
                 "than a list assembled per scene.")]
        public ItemLibrary itemLibrary;

        [Header("Chest tuning (docs/03)")]
        public ChestTuning chests = new ChestTuning();

        [Header("Loot on the floor (docs/02, docs/03)")]
        public DropTuning drops = new DropTuning();

        [Tooltip("How many items can lie on the floor at once. Loot does not appear from nowhere: " +
                 "this is a pool, sized here and shown by exactly this many views in the scene. " +
                 "A drop with the pool full recycles the stalest item on the floor.")]
        [Range(1, 32)] public int groundItemCapacity = 6;

        [Tooltip("Seed the round's loot is dealt from. 0 rolls a new one every round, which is " +
                 "what a match wants; set it to replay an arena that produced something worth " +
                 "looking at, and both devices dealt from the same number will agree (docs/06).")]
        public uint seed;

        [Tooltip("Deal a different round ONE every time the scene is played.\n\n" +
                 "Without it the rolling seed starts from a constant in the source, so the " +
                 "first scavenge of every session is the same six chests -- and only the " +
                 "first, because every round after it rolls the seed forward, which is why " +
                 "it reads as \"the loot never changes\" rather than as a seed.\n\n" +
                 "It gives up no determinism: two phones throw their state away when they " +
                 "pair and deal from the number the host chose. Switch it off to make a " +
                 "session repeatable without pinning Seed, which would deal the identical " +
                 "chests every round rather than the same SEQUENCE of rounds.")]
        public bool freshEachRun = true;

        [Header("The fight (docs/02)")]
        public CombatTuning combat = new CombatTuning();

        [Header("The shape of a match (docs/01)")]
        [Tooltip("Seconds in the cages before anyone can move. The doors come up over this.")]
        public float cageSeconds = 3f;

        [Tooltip("Seconds alone with the chests, not shown the other player. docs/01's whole " +
                 "open question is whether this is tense or empty, and it is the first number " +
                 "to reach for when the answer is empty.")]
        public float scavengeSeconds = 22f;

        [Tooltip("The radar pulse: a direction, not a position.")]
        public float sirenSeconds = 1.5f;

        [Tooltip("Seconds from the siren to the rooms starting to shut — the 0:25-1:00 " +
                 "window, siren included. The pulse is its opening beat, not a phase in front " +
                 "of it, so it comes out of this rather than on top of it.\n\n" +
                 "docs/01 writes this window as 0:25-1:15. It is shorter here on purpose: at " +
                 "75 s the walls started moving so late that the middle of the match was a " +
                 "fighter walking an arena nothing was happening in. The closing phase got the " +
                 "difference, so the match is the same length and the pressure starts sooner.")]
        public float huntSeconds = 35f;

        [Tooltip("Seconds from the rooms starting to shut to the ceiling coming down — the " +
                 "1:00-2:20 window. The shutters themselves take the first sixty of it, so " +
                 "they still finish at 2:00; the rest is both fighters already in the one room " +
                 "with nowhere left to walk away to.")]
        public float closingSeconds = 80f;

        [Tooltip("Seconds the steel shutters take, inside the closing phase: the outer rooms " +
                 "go one after another across this window, evenly spaced, and what is left of " +
                 "the phase afterwards is both fighters in the one room with nowhere to walk " +
                 "away to.\n\n" +
                 "Sixty of the phase's eighty, so the last room shuts at 2:00 exactly as " +
                 "docs/01 has it — the window starts earlier, it does not end later.")]
        public float shutterSeconds = 60f;

        [Tooltip("Seconds of sudden death before the match is decided on health. docs/01 bounds " +
                 "a match at 2:00-2:30, so this is what stops a round where neither fighter " +
                 "will close the distance from running forever. The fighter with more health " +
                 "left takes it; level pegging is a draw.")]
        public float suddenDeathSeconds = 10f;

        [Header("The poison (docs/33)")]
        [Tooltip("The green coming up out of the floor across the closing phase, which is " +
                 "what the last minute does instead of narrowing the arena.\n\n" +
                 "The pool itself is authored in the arena, as a box with a PoisonPool on " +
                 "it: that box is where the poison stops. An arena without one never floods, " +
                 "whatever is set here.")]
        public PoisonTuning poison = new PoisonTuning();

        [Header("Series")]
        [Tooltip("Rounds a fighter has to take to win the MATCH. Two is best of three.\n\n" +
                 "A round is one deal of the arena; the match is the series of them, and " +
                 "until now nothing counted. Kept here rather than in MatchSimState " +
                 "because that struct is re-made fresh for every round -- the series is " +
                 "the thing that outlives it.\n\n" +
                 "It stays deterministic all the same: both devices see the same JustEnded " +
                 "on the same tick, so they count the same wins.")]
        [Range(1, 5)] public int roundsToWin = 2;

        [Tooltip("Seconds the result stands before the next round deals. docs/01 puts \"Again\" " +
                 "straight after a loss rather than a lobby, so this is short — long enough to " +
                 "watch the body land. Set it to 0 to hold the result instead, which is what a " +
                 "build with a results screen in front of the rematch wants.")]
        public float restartSeconds = 5f;

        [Tooltip("The fighters, in a fixed order. Hits are resolved once for the whole match " +
                 "rather than once per player: a hit is a fact about two bodies, and two players " +
                 "each deciding their own would disagree about it.")]
        public System.Collections.Generic.List<PlayerController> fighters =
            new System.Collections.Generic.List<PlayerController>();

        [Header("Debug")]
        [Tooltip("Log each chest as it is opened, with who opened it and what was inside.")]
        public bool logOpenings = true;

        [Tooltip("Log each phase as it begins, and the result when the round ends. There is no " +
                 "runtime UI by design, so for now this is the only place a round says out " +
                 "loud what happened in it.")]
        public bool logMatch = true;

        public ChestSimState[] ChestStates { get; private set; } = System.Array.Empty<ChestSimState>();
        public GroundItem[] GroundItems { get; private set; } = System.Array.Empty<GroundItem>();
        public ChestConfig Config { get; private set; }
        public DropConfig Drops { get; private set; }
        public CombatConfig Combat { get; private set; }

        /// <summary>
        /// The arm: how fast it swings, how long it is, and what counts as a blow
        /// (docs/31). Tuned in the inspector on CombatTuning like everything else.
        /// </summary>
        public ArmConfig Arm { get; private set; } = ArmConfig.Default();
        public MatchConfig MatchRules { get; private set; }

        /// <summary>
        /// The rising poison: how fast and how hard. WHERE it is belongs to the arena and is
        /// authored as a box (<see cref="PoisonPool"/>); an arena without one never floods.
        /// </summary>
        public PoisonConfig Poison { get; private set; } = PoisonConfig.Default();

        /// <summary>Where the match has got to. Read it; only the tick moves it.</summary>
        public MatchSimState Match => _match;

        /// <summary>
        /// Which fighter THIS device is playing. The one place that answers it.
        ///
        /// It was answered in three places before, as a number typed into the inspector on
        /// the HUD, on the results screen and on the recorder, and written to all three by
        /// NetMatch once the wire said who this device was. Three copies of one fact, set
        /// once, is a fact that goes stale: a device whose link is adopted a frame late, or
        /// re-adopted after a drop, keeps the scene's answer -- and the scene's answer is
        /// always "player one". What that looks like is the health bar, the camera and the
        /// ladder all belonging to the other fighter, which is a bug nobody can describe
        /// except as "the bars are wrong".
        ///
        /// So it lives here, beside the fighters it indexes, and everything that needs it
        /// reads it every frame. Set by <see cref="SetLocalFighter"/> when a wire knows
        /// better; until then it is the fighter nobody's brain is driving, which is the
        /// human on a phone playing alone, whichever end of the arena they start at.
        /// </summary>
        public int LocalFighter { get; private set; }

        /// <summary>
        /// The wire has said which fighter this device drives. Clamped, because an index
        /// from outside is an index that can be wrong, and a HUD painted from a bad one is
        /// harder to explain than a HUD painted from the fallback.
        /// </summary>
        public void SetLocalFighter(int index)
        {
            int many = fighters.Count > 0 ? fighters.Count : 1;
            LocalFighter = Mathf.Clamp(index, 0, many - 1);
        }

        /// <summary>
        /// The fighter this device's own thumbs are on: the first one a brain is not
        /// driving. The same question NetMatch asks to find the local controls, so the
        /// answer cannot differ from the one the input takes.
        /// </summary>
        int HumanFighter()
        {
            for (int i = 0; i < fighters.Count; i++)
            {
                PlayerController f = fighters[i];
                if (f == null) continue;
                FighterBrain brain = f.GetComponentInChildren<FighterBrain>(true);
                if (brain == null || !brain.TakingOver) return i;
            }
            return 0;
        }

        MatchSimState _match = MatchSimState.Fresh();

        /// <summary>A phase began this tick. Raised once, not every frame.</summary>
        public event System.Action<MatchPhase> PhaseBegan;

        /// <summary>Somebody won: 0, 1, or 2 for a draw.</summary>
        public event System.Action<int> Ended;

        /// <summary>A round has just been dealt. The argument is which round, from 0.</summary>
        public event System.Action<int> RoundBegan;

        /// <summary>The match is decided. The argument is the winner, or 2 for a draw.</summary>
        public event System.Action<int> SeriesEnded;

        readonly int[] _wins = new int[2];

        /// <summary>Rounds this fighter has taken in the current match.</summary>
        public int Wins(int player) =>
            player >= 0 && player < _wins.Length ? _wins[player] : 0;

        /// <summary>Longest the match can run: best of three is three.</summary>
        public int RoundsInSeries => Mathf.Max(1, roundsToWin) * 2 - 1;

        /// <summary>Somebody has taken enough rounds.</summary>
        public bool SeriesOver =>
            _wins[0] >= Mathf.Max(1, roundsToWin) || _wins[1] >= Mathf.Max(1, roundsToWin);

        /// <summary>Who took the match, or -1 while it is still running.</summary>
        public int SeriesWinner =>
            _wins[0] >= Mathf.Max(1, roundsToWin) ? 0 :
            _wins[1] >= Mathf.Max(1, roundsToWin) ? 1 : -1;

        /// <summary>
        /// Hands the match to the other fighter and ends it there.
        ///
        /// For the one case a round cannot decide on its own: somebody stopped playing.
        /// A match abandoned has still happened -- it costs the person who stayed their
        /// time either way -- so it is recorded rather than quietly dropped, which is
        /// also what stops walking out being a free way not to lose.
        /// </summary>
        public void Concede(int loser)
        {
            if (loser < 0 || loser > 1) return;
            if (SeriesOver) return;

            int winner = 1 - loser;
            _wins[winner] = Mathf.Max(1, roundsToWin);
            _wins[loser] = Mathf.Min(_wins[loser], Mathf.Max(1, roundsToWin) - 1);

            if (logMatch)
                Debug.Log($"{name}: player {loser + 1} conceded, so the match goes to "
                          + $"player {winner + 1} ({_wins[0]}–{_wins[1]})", this);

            SeriesEnded?.Invoke(winner);
        }

        /// <summary>Back to nil-nil. The next round dealt is round one of a new match.</summary>
        public void ResetSeries()
        {
            _wins[0] = _wins[1] = 0;
        }

        PlayerSimState[] _scratch = System.Array.Empty<PlayerSimState>();

        PlayerController _tickOwner;
        bool _warnedNoFighters;

        void Awake()
        {
            Instance = this;

            // Before anything reads a weapon: the loot deal below asks the table what exists,
            // and the views ask it how long a sword is.
            if (itemLibrary != null)
            {
                int applied = itemLibrary.Apply();
                if (logMatch) Debug.Log($"{name}: item library applied, {applied} item(s).", this);
            }

            Config = chests.ToConfig();

            // Round one of the FIRST match was the same round one every time. The rolling
            // seed starts from a constant written into this file, so the chests a player
            // opens in their first scavenge are the chests they opened yesterday -- which
            // is the whole of "the loot is always the same", and it only shows on round
            // one because every round after it rolls the seed forward.
            //
            // Nothing determinism-shaped is given up by this. Two phones throw their
            // state away the moment they pair: StartFresh sets the rolling seed from the
            // number the host chose, so from there both devices deal the same rounds from
            // the same number. This is the seed of a match nobody else is in yet.
            if (seed == 0 && freshEachRun)
                _rollingSeed = (uint)System.DateTime.UtcNow.Ticks ^ 0x9E3779B9u;

            // A fresh scene is a fresh match, so the series starts at nil-nil. ResetRound
            // on its own re-deals the SAME round and deliberately leaves the score alone.
            ResetSeries();
            ResetRound();
        }

        void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            // The baker gets a second chance at Start, so take one too: a chest list that was
            // empty on Awake is a stale bake, not an arena without chests.
            if (ChestStates.Length == 0) ResetRound();
        }

        /// <summary>
        /// The seed this round is dealt from. Rolled forward each round so "Reset round" twice
        /// gives two different arenas, and settable so a seed that produced something worth
        /// looking at can be played again.
        /// </summary>
        uint NextSeed()
        {
            if (seed != 0) return seed;                  // pinned in the inspector
            _rollingSeed = _rollingSeed * 1664525u + 1013904223u;
            return _rollingSeed;
        }

        uint _rollingSeed = 0x5EED1234u;

        /// <summary>
        /// Deals the next round: a new number on the clock, chests shut and filled again, the
        /// floor cleared, and the fighters back in their cages.
        ///
        /// docs/01 is explicit that there are no best-of-three rounds — one life, and an
        /// "Again" straight after a loss. So this is the rematch, and the one thing it cannot
        /// do is hand the player back the arena they have already learnt. The geometry is baked
        /// and immutable, so what gets dealt again is what is IN it: a new seed means the chest
        /// they remember as the good one is now a crate with a bandage in it, and the scavenge
        /// phase is a search rather than a route. A genuinely new arena is docs/05's job.
        ///
        /// Safe to call from inside the tick, which is where the automatic rematch calls it
        /// from: a respawn is a teleport, and the rest of that tick runs on a fighter standing
        /// in a cage with their input already locked.
        /// </summary>
        [ContextMenu("Next round")]
        public void NextRound()
        {
            Deal(_match.Round + 1);
            ResetFighters();

            if (logMatch) Debug.Log($"{name}: round {_match.Round} — dealt, back in the cages",
                                    this);

            RoundBegan?.Invoke(_match.Round);
            PhaseBegan?.Invoke(_match.Phase);
        }

        /// <summary>Shuts every chest again and re-deals this same round.</summary>
        [ContextMenu("Reset round")]
        public void ResetRound() => Deal(_match.Round);

        /// <summary>
        /// Round one of a new match, dealt from a number somebody else chose.
        ///
        /// Two devices about to play each other have each been sitting in this scene for a
        /// while -- bots fighting, rounds turning over, chests opened -- so their states
        /// are nothing alike by the time anybody taps JOIN. Neither one is wrong; they are
        /// simply two matches. This throws both away and starts one, which is the only
        /// moment in a deterministic design where the two devices have to be told anything
        /// at all beyond each other's thumbs.
        ///
        /// What is handed over is the number the loot is dealt from, not the loot. The
        /// rolling seed is set rather than pinned, so round two is a different arena from
        /// round one on both devices and still the SAME different arena -- a pinned seed
        /// would deal the identical chests every round of the match.
        /// </summary>
        public void StartFresh(uint dealtFrom)
        {
            // A seed pinned in the inspector is already the same number on both devices,
            // because it is the same build. Leave a deliberate pin alone.
            if (seed == 0) _rollingSeed = dealtFrom;

            ResetSeries();
            Deal(0);
            ResetFighters();

            if (logMatch) Debug.Log($"{name}: new match, dealt from {dealtFrom}", this);

            RoundBegan?.Invoke(_match.Round);
            PhaseBegan?.Invoke(_match.Phase);
        }

        void Deal(int round)
        {
            // Through the one method that reads the inspector, rather than a second copy of
            // the same five lines: a tuning class added to one of them and not the other is a
            // number that works in play mode and not in a fresh round, or the other way
            // about, and neither reads as a missing line.
            ApplyTuning();

            // Whose thumbs are on which fighter, re-taken rather than remembered: a round
            // dealt after a bot substituted for a dropped player is a round where the
            // answer has changed. A wire that knows better overwrites it on the next tick.
            LocalFighter = HumanFighter();

            // And the chest watchdog is re-armed per round, so a stall in the second round
            // is reported as loudly as one in the first.
            for (int i = 0; i < _pryingAt.Length; i++)
            {
                _pryingAt[i] = -1;
                _pryingFor[i] = 0;
                _warnedStall[i] = false;
            }

            if (worldBaker == null) worldBaker = SimWorldBaker.Instance;
            SimWorld world = worldBaker != null ? worldBaker.World : null;

            int count = world != null ? world.Chests.Length : 0;
            ChestStates = ChestSimState.FreshSet(count);
            _match = MatchSimState.Fresh(round);

            // Deal the round. docs/01's symmetric start and asymmetric result: both sides of
            // the arena have the same chests and different things in them, which is what makes
            // the scavenge phase a set of decisions rather than a race down a known list.
            if (world != null)
                LootTable.Deal(world.Chests, ChestStates, NextSeed());
            GroundItems = GroundItem.Pool(groundItemCapacity);
            _tickOwner = null;
        }

        /// <summary>
        /// Advances everything that belongs to nobody in particular — loot in the air — exactly
        /// once per tick.
        ///
        /// The first player to ask becomes the one who drives it, because the players own the only
        /// tick loop there is. That is the seam the match ticker replaces when rollback arrives
        /// (docs/06); until then this keeps the shared state stepping once, from inside the same
        /// loop the players step in, rather than from a second clock that would drift from it.
        /// </summary>
        public void AdvanceShared(PlayerController caller, SimWorld world)
        {
            if (!Owns(caller)) return;

            LootMotor.StepGround(GroundItems, world, Drops);
            ResolveHits();
            BitePoison(world);
            WatchChests(world);
        }

        /// <summary>
        /// Says so when a fighter has been prying at one chest for longer than that chest
        /// can possibly take.
        ///
        /// A SEARCH ANIMATION THAT NEVER ENDS is the one loot fault that looks like nothing:
        /// the fighter works, the chest does not open, and every number involved is a
        /// plausible number somewhere else in the project. The progress is in thousandths of
        /// a bare-handed frame and the gain is the weapon's pry speed in the same units, so
        /// a pry speed that arrives as 0.001 instead of 1.0 -- one bad conversion, one data
        /// file written by hand -- clamps the gain to 1 and makes a 2.5 s chest take
        /// forty-one minutes. Nothing errors. Nothing logs. The player holds the button.
        ///
        /// So the one thing that cannot be read off a running game is printed once: what
        /// the chest needs, what this weapon adds, and where the progress actually is.
        /// Which of those three is wrong says which layer to go and look at.
        /// </summary>
        void WatchChests(SimWorld world)
        {
            if (world == null || ChestStates == null || ChestStates.Length == 0) return;

            int watched = fighters.Count < _pryingAt.Length ? fighters.Count : _pryingAt.Length;
            for (int i = 0; i < watched; i++)
            {
                PlayerController f = fighters[i];
                if (f == null) continue;

                int chest = f.State.OpeningChest;
                if (chest < 0 || chest >= world.Chests.Length)
                {
                    _pryingAt[i] = -1;
                    _pryingFor[i] = 0;
                    continue;
                }

                if (_pryingAt[i] != chest) { _pryingAt[i] = chest; _pryingFor[i] = 0; }
                _pryingFor[i]++;

                // Three times what it should take, plus a second: slack for a hand coming
                // off the button, a drop and a re-claim, and a chest shared with the other
                // fighter -- none of which should ever add up to this much.
                int should = LootMotor.FramesToOpen(world.Chests[chest].Kind,
                                                    f.State.Inventory.Weapon, Config);
                if (_pryingFor[i] <= should * 3 + 60 || _warnedStall[i]) continue;

                _warnedStall[i] = true;
                int gain = ItemTable.WeaponOrFists(f.State.Inventory.Weapon).PrySpeed.ToMilli();
                Debug.LogError(
                    $"{name}: player {i + 1} has been prying at chest {chest} " +
                    $"({world.Chests[chest].Kind}) for {_pryingFor[i] / 60f:0.0} s and it has " +
                    $"not opened. It should take {should / 60f:0.0} s.\n" +
                    $"  progress {ChestStates[chest].Progress} of " +
                    $"{Config.For(world.Chests[chest].Kind).BareFrames * 1000} needed, " +
                    $"+{gain} a tick from {f.State.Inventory.Weapon} (pry speed " +
                    $"{gain / 1000f:0.###}), claimed by player " +
                    $"{ChestStates[chest].Opener + 1}.\n" +
                    "  A gain of 1 means the weapon's pry speed arrived as a thousandth of " +
                    "what it should be: check that item's data file. A progress that is not " +
                    "climbing means something else is draining it. A claim held by the OTHER " +
                    "player means the two fighters are sharing an index they should not.",
                    this);
            }
        }

        readonly int[] _pryingAt = { -1, -1 };
        readonly int[] _pryingFor = new int[2];
        readonly bool[] _warnedStall = new bool[2];

        /// <summary>
        /// The poison takes its bite, once for the whole match.
        ///
        /// Here rather than at the front of the tick, next to the clock that raised the level,
        /// for two reasons that both come down to timing. Where a body ENDED the tick is where
        /// it is standing, so a fighter who climbed out this tick is out. And the outputs a
        /// view reads — the damage, the death — are cleared at the front of each fighter's own
        /// tick, so damage dealt before that is damage nothing ever hears about: no number
        /// over the head, no death cry, a corpse that simply appears.
        ///
        /// Which makes it the hit resolution's neighbour exactly: both decide damage once for
        /// the whole match, from the state as the tick leaves it.
        /// </summary>
        void BitePoison(SimWorld world)
        {
            if (world == null || !world.HasPoison || fighters.Count < 2) return;

            PoisonConfig venom = Poison;
            MatchConfig rules = MatchRules;
            if (!PoisonMotor.Bites(_match.Frame, in rules, in venom)) return;
            if (_match.Finished) return;

            if (_scratch.Length != fighters.Count) _scratch = new PlayerSimState[fighters.Count];
            for (int i = 0; i < fighters.Count; i++)
            {
                if (fighters[i] == null) return;
                _scratch[i] = fighters[i].State;
            }

            PoisonMotor.Bite(_scratch, world, in venom, fighters[0].Config);

            for (int i = 0; i < fighters.Count; i++) fighters[i].OverwriteState(in _scratch[i]);
        }

        /// <summary>
        /// The front of the tick, before any fighter has moved.
        ///
        /// The clock runs here rather than at the end for one reason: docs/01's shutters are a
        /// change to collision. A gate counted as shut only after the fighters have moved is a
        /// gate they walk through on the tick it closes — and the fighter being shoved through
        /// a doorway has to be put down before the motor steps them, not after, or they spend
        /// a tick inside a wall.
        ///
        /// It also means the input lock and the phase now agree on which tick they are on.
        /// </summary>
        public void BeginTick(PlayerController caller, SimWorld world)
        {
            if (!Owns(caller)) return;
            StepMatch(world);
        }

        /// <summary>
        /// Whether this caller is the one driving the shared tick. The players own the only
        /// tick loop there is, so the first to ask becomes the one that drives it (docs/06).
        /// </summary>
        bool Owns(PlayerController caller)
        {
            if (caller == null) return false;
            if (_tickOwner == null || !_tickOwner.isActiveAndEnabled) _tickOwner = caller;
            return _tickOwner == caller;
        }

        static int Ticks(float seconds) =>
            Mathf.Max(0, Mathf.RoundToInt(seconds * PlayerMotor.TicksPerSecond));

        /// <summary>
        /// docs/01's table of phases, in ticks.
        ///
        /// One method rather than one per caller: this was written out twice, once for the round
        /// reset and once for re-reading the tuning in play mode, and the second copy is exactly
        /// where a phase length goes stale without anything complaining.
        /// </summary>
        MatchConfig BuildRules() => new MatchConfig
        {
            CageFrames     = Ticks(cageSeconds),
            ScavengeFrames = Ticks(scavengeSeconds),
            SirenFrames    = Ticks(sirenSeconds),

            // The siren is the OPENING of the hunt, not a phase in front of it: docs/01 puts
            // the pulse at 0:25 and the hunt at 0:25-1:15, so its second and a half comes out
            // of the hunt's fifty rather than on top of them. Added instead of subtracted,
            // every boundary after it lands late — which is what the clock did until a test
            // counted the table out in minutes and seconds.
            HuntFrames        = Mathf.Max(0, Ticks(huntSeconds) - Ticks(sirenSeconds)),
            ClosingFrames     = Ticks(closingSeconds),
            SuddenDeathFrames = Ticks(suddenDeathSeconds),
            RestartFrames     = Ticks(restartSeconds),
            ShutterFrames     = Ticks(shutterSeconds),
        };

        /// <summary>
        /// The match's own clock, once per tick, from inside the same loop everything else
        /// steps in. The phase has to arrive on the same tick on both devices, so it cannot be
        /// driven off a second clock of its own (docs/06).
        /// </summary>
        void StepMatch(SimWorld world)
        {
            if (fighters.Count < 2 || fighters[0] == null || fighters[1] == null)
            {
                // Said out loud, because the way this fails is silence. Without both fighters
                // the clock cannot step; without the clock the cage phase never ends; and
                // while it is the cage phase nobody may move. The symptom is a game that
                // accepts no input at all, and nothing anywhere says why.
                if (!_warnedNoFighters)
                {
                    _warnedNoFighters = true;
                    int live = 0;
                    for (int i = 0; i < fighters.Count; i++) if (fighters[i] != null) live++;
                    Debug.LogError(
                        $"{name}: Fighters needs both players and has {live} of " +
                        $"{fighters.Count}. The match clock cannot step without them, so the " +
                        "cage phase never ends and NOBODY CAN MOVE. Drag both fighters into " +
                        "the Fighters list on this component.", this);
                }
                return;
            }

            _warnedNoFighters = false;

            GateDef[] gates = world != null ? world.Gates : null;
            PlayerSimState[] states = { fighters[0].State, fighters[1].State };
            MatchConfig rules = MatchRules;
            MatchMotor.Step(ref _match, in rules, states, gates);

            // The arena, then the bodies in it. One word of state, copied onto the world the
            // motor reads, so every collision query this tick already knows what is shut.
            if (world != null)
            {
                world.GatesShut = _match.GatesShut;

                // How far the walls have come in. Written here, before anything moves, for
                // the same reason the gate mask is: it is a pure function of the frame, so
                // the two devices cannot disagree and a rollback needs nothing but the frame.
                world.WallProgress = _match.ClosingProgress(rules);

                // And how full the pool is. Nothing in the motor collides with it -- poison
                // is a level, not a box -- but it is written at the same moment and for the
                // same reason: the number the surface is DRAWN at and the number that decides
                // who is drowning have to be one number, taken once a tick.
                PoisonConfig venom = Poison;
                world.PoisonRise = PoisonMotor.RiseAt(_match.Frame, in rules, in venom);
            }

            if (_match.GatesJustShut != 0)
            {
                GateMotor.Shove(states, gates, _match.GatesJustShut, fighters[0].Config);
                for (int i = 0; i < states.Length; i++) fighters[i].OverwriteState(in states[i]);

                if (logMatch)
                    Debug.Log($"{name}: gate(s) shut — mask {_match.GatesShut:x}", this);
            }

            if (_match.PhaseChanged)
            {
                if (logMatch) Debug.Log($"{name}: round {_match.Round} — " +
                                        $"{_match.Phase} at {Clock(_match.Frame)}", this);
                PhaseBegan?.Invoke(_match.Phase);
            }

            if (_match.JustEnded)
            {
                // The series is counted here and nowhere else, on the one tick the round
                // is decided -- not on the restart, which may never come, and not in the
                // interface, which must be able to disappear without changing the game.
                if (_match.Winner == 0 || _match.Winner == 1) _wins[_match.Winner]++;

                if (logMatch) Debug.Log($"{name}: round {_match.Round} — {Result()} " +
                                        $"at {Clock(_match.Frame)} " +
                                        $"({_wins[0]}–{_wins[1]}, first to {roundsToWin})",
                                        this);
                Ended?.Invoke(_match.Winner);
                if (SeriesOver) SeriesEnded?.Invoke(SeriesWinner);
            }

            // The result has stood long enough. Done here, from inside the tick, because the
            // alternative is a coroutine on a second clock: the restart would land on a
            // different tick on each device, and the round after it would deal from a chest
            // list the two of them no longer agreed about (docs/06).
            // And no further round is dealt once the match is decided. Without this the
            // arena simply re-deals for ever and "best of three" is a number on a label.
            if (_match.WantsRestart && !SeriesOver) NextRound();
        }

        /// <summary>
        /// Who hit whom, once per tick. The states are copied out, resolved together and copied
        /// back, so the decision lives in the deterministic layer rather than here — and when the
        /// match ticker arrives this becomes the loop that owns them outright.
        /// </summary>
        void ResolveHits()
        {
            if (fighters.Count < 2) return;
            if (_scratch.Length != fighters.Count) _scratch = new PlayerSimState[fighters.Count];

            for (int i = 0; i < fighters.Count; i++)
            {
                if (fighters[i] == null) return;
                _scratch[i] = fighters[i].State;
            }

            // Weapons against weapons first, then weapons against bodies -- that order
            // IS the block, and ArmMotor owns it because it owns the geometry.
            // docs/01: on the last platform, bare hands hit twice as hard.
            ArmMotor.Resolve(_scratch, fighters[0].Config, Arm, Combat,
                             _match.Phase == MatchPhase.SuddenDeath);

            for (int i = 0; i < fighters.Count; i++) fighters[i].OverwriteState(in _scratch[i]);
        }

        /// <summary>
        /// The other fighter, or null. Used by the aim assist, which needs to know where
        /// the man is before it can help point a blade at him.
        /// </summary>
        public PlayerController Opponent(PlayerController mine)
        {
            for (int i = 0; i < fighters.Count; i++)
            {
                PlayerController p = fighters[i];
                if (p != null && p != mine && !p.State.Dead) return p;
            }
            return null;
        }

        /// <summary>Puts every fighter back on their feet at full health. One call is a new round.</summary>
        [ContextMenu("Reset fighters")]
        public void ResetFighters()
        {
            foreach (PlayerController p in fighters)
                if (p != null) p.Respawn();
        }

        /// <summary>Re-reads the inspector tuning. Handy while balancing in play mode.</summary>
        public void ApplyTuning()
        {
            Config = chests.ToConfig();
            Drops = drops.ToConfig();
            Combat = combat.ToConfig();
            Arm = combat.ToArmConfig();
            Poison = poison.ToConfig();
            MatchRules = BuildRules();
        }

        /// <summary>The result in words, for the log. Winner 2 is the draw.</summary>
        string Result()
        {
            if (_match.Winner == 2) return "a draw";

            int winner = _match.Winner;
            if (winner < 0 || winner >= fighters.Count || fighters[winner] == null)
                return $"player {winner + 1} wins";

            // How much was left of them says more about the round than who won it.
            PlayerController p = fighters[winner];
            return $"player {winner + 1} wins with {p.State.Health}/{p.MaxHealth} left";
        }

        static string Clock(int frame)
        {
            int seconds = frame / PlayerMotor.TicksPerSecond;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        /// <summary>
        /// A chest was just forced. The HUD listens so it can point at one opened off
        /// screen: in a match where both fighters are looting at once, the one thing a
        /// player cannot see and most wants to know is that the OTHER one just found
        /// something, and roughly where.
        /// </summary>
        public event System.Action<int, int> ChestOpened;

        public void ReportOpened(int playerIndex, int chestIndex)
        {
            // Raised before the log switch, which is a debugging convenience and has no
            // business deciding whether the interface is told.
            ChestOpened?.Invoke(playerIndex, chestIndex);

            if (!logOpenings) return;

            SimWorld world = worldBaker != null ? worldBaker.World : null;
            if (world == null || chestIndex < 0 || chestIndex >= world.Chests.Length) return;

            ChestDef def = world.Chests[chestIndex];
            Debug.Log($"{name}: player {playerIndex + 1} opened {def.Kind} #{chestIndex} " +
                      $"-> {def.Contents}", this);
        }
    }
}

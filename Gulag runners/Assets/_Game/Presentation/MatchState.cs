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

        [Tooltip("Seconds from the siren to the rooms starting to shut — the whole 0:25-1:15 " +
                 "window of docs/01, siren included. The pulse is its opening beat, not a " +
                 "phase in front of it, so it comes out of this rather than on top of it.")]
        public float huntSeconds = 50f;

        [Tooltip("Seconds from the rooms starting to shut to the ceiling coming down — the " +
                 "whole 1:15-2:20 window of docs/01. The shutters themselves take the first " +
                 "forty-five of it; the rest is both fighters already in the one room with " +
                 "nowhere left to walk away to.")]
        public float closingSeconds = 65f;

        [Tooltip("Seconds of sudden death before the match is decided on health. docs/01 bounds " +
                 "a match at 2:00-2:30, so this is what stops a round where neither fighter " +
                 "will close the distance from running forever. The fighter with more health " +
                 "left takes it; level pegging is a draw.")]
        public float suddenDeathSeconds = 10f;

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
        public MatchConfig MatchRules { get; private set; }

        /// <summary>Where the match has got to. Read it; only the tick moves it.</summary>
        public MatchSimState Match => _match;

        MatchSimState _match = MatchSimState.Fresh();

        /// <summary>A phase began this tick. Raised once, not every frame.</summary>
        public event System.Action<MatchPhase> PhaseBegan;

        /// <summary>Somebody won: 0, 1, or 2 for a draw.</summary>
        public event System.Action<int> Ended;

        /// <summary>A round has just been dealt. The argument is which round, from 0.</summary>
        public event System.Action<int> RoundBegan;

        PlayerSimState[] _scratch = System.Array.Empty<PlayerSimState>();

        PlayerController _tickOwner;

        void Awake()
        {
            Instance = this;
            Config = chests.ToConfig();
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

        void Deal(int round)
        {
            Config = chests.ToConfig();
            Drops = drops.ToConfig();
            Combat = combat.ToConfig();
            MatchRules = BuildRules();

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
            if (caller == null) return;
            if (_tickOwner == null || !_tickOwner.isActiveAndEnabled) _tickOwner = caller;
            if (_tickOwner != caller) return;

            LootMotor.StepGround(GroundItems, world, Drops);
            ResolveHits();
            StepMatch();
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
        };

        /// <summary>
        /// The match's own clock, once per tick, from inside the same loop everything else
        /// steps in. The phase has to arrive on the same tick on both devices, so it cannot be
        /// driven off a second clock of its own (docs/06).
        /// </summary>
        void StepMatch()
        {
            if (fighters.Count < 2 || fighters[0] == null || fighters[1] == null) return;

            PlayerSimState[] states = { fighters[0].State, fighters[1].State };
            MatchConfig rules = MatchRules;
            MatchMotor.Step(ref _match, in rules, states);

            if (_match.PhaseChanged)
            {
                if (logMatch) Debug.Log($"{name}: round {_match.Round} — " +
                                        $"{_match.Phase} at {Clock(_match.Frame)}", this);
                PhaseBegan?.Invoke(_match.Phase);
            }

            if (_match.JustEnded)
            {
                if (logMatch) Debug.Log($"{name}: round {_match.Round} — {Result()} " +
                                        $"at {Clock(_match.Frame)}", this);
                Ended?.Invoke(_match.Winner);
            }

            // The result has stood long enough. Done here, from inside the tick, because the
            // alternative is a coroutine on a second clock: the restart would land on a
            // different tick on each device, and the round after it would deal from a chest
            // list the two of them no longer agreed about (docs/06).
            if (_match.WantsRestart) NextRound();
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

            CombatMotor.Resolve(_scratch, Combat, fighters[0].Config);

            for (int i = 0; i < fighters.Count; i++) fighters[i].OverwriteState(in _scratch[i]);
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

        public void ReportOpened(int playerIndex, int chestIndex)
        {
            if (!logOpenings) return;

            SimWorld world = worldBaker != null ? worldBaker.World : null;
            if (world == null || chestIndex < 0 || chestIndex >= world.Chests.Length) return;

            ChestDef def = world.Chests[chestIndex];
            Debug.Log($"{name}: player {playerIndex + 1} opened {def.Kind} #{chestIndex} " +
                      $"-> {def.Contents}", this);
        }
    }
}

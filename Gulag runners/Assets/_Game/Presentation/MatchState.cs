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

        [Header("The fight (docs/02)")]
        public CombatTuning combat = new CombatTuning();

        [Tooltip("The fighters, in a fixed order. Hits are resolved once for the whole match " +
                 "rather than once per player: a hit is a fact about two bodies, and two players " +
                 "each deciding their own would disagree about it.")]
        public System.Collections.Generic.List<PlayerController> fighters =
            new System.Collections.Generic.List<PlayerController>();

        [Header("Debug")]
        [Tooltip("Log each chest as it is opened, with who opened it and what was inside.")]
        public bool logOpenings = true;

        public ChestSimState[] ChestStates { get; private set; } = System.Array.Empty<ChestSimState>();
        public GroundItem[] GroundItems { get; private set; } = System.Array.Empty<GroundItem>();
        public ChestConfig Config { get; private set; }
        public DropConfig Drops { get; private set; }
        public CombatConfig Combat { get; private set; }

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

        /// <summary>Shuts every chest again. One call is a new round.</summary>
        [ContextMenu("Reset round")]
        public void ResetRound()
        {
            Config = chests.ToConfig();
            Drops = drops.ToConfig();
            Combat = combat.ToConfig();

            if (worldBaker == null) worldBaker = SimWorldBaker.Instance;
            SimWorld world = worldBaker != null ? worldBaker.World : null;

            int count = world != null ? world.Chests.Length : 0;
            ChestStates = ChestSimState.FreshSet(count);
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

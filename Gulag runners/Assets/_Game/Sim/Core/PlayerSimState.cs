namespace GulagRunners.Sim
{
    public enum MoveMode : byte
    {
        Airborne = 0,
        Grounded = 1,
        Climbing = 2,
        Dodging  = 3,
        /// <summary>Climbing out at the top of a ladder onto the floor beside it.</summary>
        Mantling = 4,
        /// <summary>Reaching for a ladder: the short move that puts the body onto the rungs.</summary>
        Mounting = 5
    }

    /// <summary>
    /// Everything the simulation knows about one player's movement.
    /// Deliberately a plain struct of value types: docs/06 requires the whole round state to
    /// serialise into under 2 KB so an 8-frame rollback costs nothing.
    /// </summary>
    public struct PlayerSimState
    {
        /// <summary>Feet position: centre on X, bottom on Y.</summary>
        public FixVec2 Position;
        public FixVec2 Velocity;

        public MoveMode Mode;
        /// <summary>+1 facing right, -1 facing left. Drives the visual flip and, later, attacks.</summary>
        public sbyte Facing;
        public bool Crouching;
        public bool JumpHeld;

        public int CoyoteTimer;
        public int JumpBufferTimer;
        public int DodgeTimer;
        public int DodgeRecoverTimer;
        public int FallThroughTimer;
        public int StepNoiseTimer;

        public int StaminaCharges;
        public int StaminaTimer;

        public int LadderIndex;

        /// <summary>Three slots, no menu: weapon, armour, utility (docs/02).</summary>
        public Inventory Inventory;

        /// <summary>
        /// Chest this player has claimed, or -1. The claim is held until the chest's progress
        /// drains away, even after walking off, because only the claimant may drain it.
        /// </summary>
        public int OpeningChest;

        /// <summary>Output of the last tick: what came out of a chest, for presentation to react to.</summary>
        public ItemId PickedUp;

        /// <summary>Output of the last tick: which chest that was, or -1.</summary>
        public int OpenedChest;

        /// <summary>Output of the last tick: what was pushed out of a slot to make room.</summary>
        public ItemId Dropped;

        /// <summary>
        /// Output of the last tick: the ground item the body is standing over, or -1. Presentation
        /// reads it to say what the action button would do BEFORE it is pressed, which is the
        /// difference between a contextual button and a guess.
        /// </summary>
        public int StandingOn;

        /// <summary>Was the action button down last tick. Swapping wants a press, not a hold.</summary>
        public bool ActionHeld;
        /// <summary>Counts down after letting go of a ladder; no new ladder is grabbed until it
        /// reaches zero.</summary>
        public int LadderCooldownTimer;

        /// <summary>
        /// A short scripted move: where it started, where it ends, how many frames it lasts in
        /// total and how many are left. Only valid while Mantling or Mounting — the two places
        /// where the simulation walks the body along a path instead of integrating velocity,
        /// because both are transitions between two states rather than states of their own.
        /// </summary>
        public FixVec2 ScriptFrom;
        public FixVec2 ScriptTo;
        public int ScriptTimer;
        public int ScriptFrames;
        /// <summary>Which way the scripted move goes: climb direction for a mount, step side for
        /// a climb-out. Presentation reads it to pick the matching clip.</summary>
        public sbyte ScriptDir;

        /// <summary>Output of the last tick. Read by presentation, and later by the audio-info system.</summary>
        public NoiseLevel Noise;

        public bool Invulnerable => DodgeTimer > 0;
        public bool OnGround => Mode == MoveMode.Grounded;

        public static PlayerSimState Spawn(FixVec2 position, sbyte facing, in MoveConfig config)
        {
            PlayerSimState s = default;
            s.Position = position;
            s.Velocity = FixVec2.Zero;
            s.Mode = MoveMode.Airborne;
            s.Facing = facing == 0 ? (sbyte)1 : facing;
            s.LadderIndex = -1;
            s.OpeningChest = -1;
            s.OpenedChest = -1;
            s.StandingOn = -1;
            s.PickedUp = ItemId.None;
            s.Dropped = ItemId.None;
            s.StaminaCharges = config.StaminaMax;
            s.Noise = NoiseLevel.Silent;
            return s;
        }

        public Aabb Body(in MoveConfig config) =>
            Aabb.FromFeet(Position, config.BodyWidth, Crouching ? config.CrouchHeight : config.BodyHeight);
    }
}

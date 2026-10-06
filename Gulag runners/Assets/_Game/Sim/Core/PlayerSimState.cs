namespace GulagRunners.Sim
{
    public enum MoveMode : byte
    {
        Airborne = 0,
        Grounded = 1,
        Climbing = 2,
        Dodging  = 3,
        /// <summary>Climbing out at the top of a ladder onto the floor beside it.</summary>
        Mantling = 4
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

        /// <summary>Where the climb-out started, and where it ends. Only valid while Mantling.</summary>
        public FixVec2 MantleFrom;
        public FixVec2 MantleTo;
        public int MantleTimer;

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
            s.StaminaCharges = config.StaminaMax;
            s.Noise = NoiseLevel.Silent;
            return s;
        }

        public Aabb Body(in MoveConfig config) =>
            Aabb.FromFeet(Position, config.BodyWidth, Crouching ? config.CrouchHeight : config.BodyHeight);
    }
}

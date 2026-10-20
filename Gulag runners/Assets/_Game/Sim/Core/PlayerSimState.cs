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

    /// <summary>Where a swing is in its life. docs/02: wind-up, the hit, and the price of missing.</summary>
    public enum AttackPhase : byte
    {
        None = 0,
        /// <summary>Committed, nothing has happened yet. This is the window the opponent reads.</summary>
        Windup = 1,
        /// <summary>The hitbox is live.</summary>
        Active = 2,
        /// <summary>What a miss costs.</summary>
        Recovery = 3
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
        /// <summary>
        /// +1 right, -1 left: which way the fighter AIMS. The hitbox extends this way, the
        /// guard only stops what comes at it from this way, and the model is turned this way.
        ///
        /// It is not the same thing as which way he is going. A fighter can back off along
        /// one axis while keeping his blade on the other — see <see cref="Travel"/> — and
        /// pretending otherwise is what makes a side-view fight read as two people queueing.
        /// </summary>
        public sbyte Facing;

        /// <summary>
        /// +1 right, -1 left: which way the body last MOVED, which is a different question.
        ///
        /// The level reads this and not the aim: what is in the way is what is in front of
        /// where you are going. Presentation reads both — travel against aim is the sign of
        /// the locomotion, and a fighter whose feet and blade disagree is running backwards.
        /// </summary>
        public sbyte Travel;

        /// <summary>
        /// Frames left of an aim the player asked for, after the swipe that asked for it.
        ///
        /// Without it the aim would last exactly one tick: a swipe is a single frame of
        /// input and the stick is held for hundreds, so movement would take the facing back
        /// before the windup finished and the blow would land the way the feet were going.
        /// </summary>
        public int AimLockTimer;
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

        // ---------------------------------------------------------------- the fight (docs/02)

        public short Health;

        public AttackPhase Attack;
        /// <summary>Frames left in the current phase.</summary>
        public int AttackTimer;
        /// <summary>How long the whole current phase lasts, for presentation to scrub a clip by.</summary>
        public int AttackPhaseFrames;
        /// <summary>0, 1, 2. The third is the heavy one that ends the string.</summary>
        public byte ComboIndex;
        /// <summary>Frames left to continue the combo with another press.</summary>
        public int ComboTimer;
        /// <summary>This swing has already connected; one swing hits once.</summary>
        /// <summary>
        /// True when the scripted move ends HIGHER than it began, which is the only case
        /// the haul-yourself-up clip describes. A ladder whose head is flush with its
        /// platform leaves the body slightly above the floor it is stepping onto, and that
        /// climb-out goes DOWN -- measured at 6.6 cm down on this arena's top ladder. The
        /// haul clip played over it is a man pulling himself up while his body sinks.
        /// </summary>
        /// <summary>
        /// Hits taken in the current string, and the clock that ends it. What stops the
        /// fast weapon looping the slow one for ever.
        /// </summary>
        public byte StunStack;
        public int StunStackTimer;

        public bool ScriptRises;

        public bool SwingSpent;

        /// <summary>
        /// This swing has already connected, which is what buys back half the recovery.
        /// </summary>
        public bool SwingConnected;

        /// <summary>
        /// Frames an attack press is still worth something.
        ///
        /// Without it a press during recovery is simply dropped, and the only way to chain
        /// is to let go and press again on the exact frame the swing ends — which on a
        /// phone is not a skill, it is a coin toss. Every fighting game made since the
        /// nineties remembers the press for a few frames; this is that, and nothing more.
        /// </summary>
        public int AttackBuffer;

        public bool Blocking;
        /// <summary>Frames the guard has been up. Under ParryFrames it parries instead of blocks.</summary>
        public int GuardTimer;
        /// <summary>Guard broken: frames until it can be raised again.</summary>
        public int GuardBreakTimer;

        /// <summary>Hit and unable to act. Knockback is carried by velocity, not by this.</summary>
        /// <summary>
        /// Frames the whole body is frozen for, after a hit connects on it or from it.
        ///
        /// Hit stop, and the one thing in the fight that nothing else can stand in for. A
        /// swing that merely changes a number has nothing in it for the hands: the weight of
        /// a blow is the moment both fighters stop dead. It is a handful of frames, it is
        /// what a heavy weapon has more of than a dagger, and it is the difference between a
        /// hit you watch and a hit you feel.
        ///
        /// In the simulation rather than in the view, because it changes timing — the next
        /// swing lands later for both of them — and a pause one device took and the other
        /// did not would desynchronise the match on the first exchange (docs/06). One int,
        /// which the 2 KB round state has room for.
        /// </summary>
        public int HitStopTimer;

        public int HitstunTimer;
        /// <summary>Parried. Longer than hitstun, and entirely the attacker's own fault.</summary>
        public int StaggerTimer;

        public bool Dead;
        public int DeathTimer;
        public bool GuardHeld;

        /// <summary>
        /// Ground speed cap as a fraction of the run speed, in thousandths. Zero means no cap —
        /// the combat layer sets it so that a raised guard slows the body without the movement
        /// motor needing to know what a guard is.
        /// </summary>
        public short SpeedPermille;

        // ---- outputs of the last tick, for presentation and audio
        public short DamageTaken;
        public bool WasHit;
        public bool WasBlocked;
        public bool WasParried;
        public bool SwingStarted;
        public bool JustDied;
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

        /// <summary>
        /// The slices a climb-out starts and ends on. A location modelled in 3D does not line
        /// its floors up: the ladder here stands in FRONT of the walkway it serves, so getting
        /// off at the top is a step back as well as up, and the climb-out carries the body
        /// across in depth over the same frames it carries it in height.
        /// </summary>
        public Fix ScriptFromDepth;
        public Fix ScriptToDepth;

        /// <summary>
        /// Is this climb-out a step FORWARD onto the floor the rungs end at, rather than a
        /// shuffle sideways out of a hatch.
        ///
        /// The two look nothing alike and presentation cannot tell them apart from the
        /// positions alone. A fighter climbing out forwards is still facing the ladder at the
        /// moment he steps off it, so turning him side-on the instant the climb-out starts —
        /// which is what happened — plays the whole move as though the platform he is getting
        /// onto were somewhere off to his left.
        /// </summary>
        public bool ScriptForward;
        /// <summary>Which way the scripted move goes: climb direction for a mount, step side for
        /// a climb-out. Presentation reads it to pick the matching clip.</summary>
        public sbyte ScriptDir;

        /// <summary>Output of the last tick. Read by presentation, and later by the audio-info system.</summary>
        public NoiseLevel Noise;

        /// <summary>
        /// How fast the body was falling on the tick it landed, and zero on every other tick.
        /// The motor measures it; the combat pass spends it. Keeping health in one place is
        /// worth the single tick of delay.
        /// </summary>
        public Fix LandedSpeed;

        /// <summary>
        /// Which slice of the level this body is on, in metres of Z. Simulation state, not
        /// presentation: it decides which boxes are in the way, so both devices must agree on
        /// it to the millimetre.
        /// </summary>
        public Fix Depth;

        public bool Invulnerable => DodgeTimer > 0;
        /// <summary>Unable to act: hit, parried, or dead.</summary>
        public bool Reeling => HitstunTimer > 0 || StaggerTimer > 0 || Dead;
        public bool Swinging => Attack != AttackPhase.None;
        public bool OnGround => Mode == MoveMode.Grounded;

        public static PlayerSimState Spawn(FixVec2 position, sbyte facing, in MoveConfig config)
        {
            PlayerSimState s = default;
            s.Position = position;
            s.Velocity = FixVec2.Zero;
            s.Mode = MoveMode.Airborne;
            s.Facing = facing == 0 ? (sbyte)1 : facing;
            s.Travel = s.Facing;
            s.LadderIndex = -1;
            s.OpeningChest = -1;
            s.OpenedChest = -1;
            s.StandingOn = -1;
            s.PickedUp = ItemId.None;
            s.Dropped = ItemId.None;
            s.StaminaCharges = config.StaminaMax;
            s.Health = 100;
            s.Attack = AttackPhase.None;
            s.Noise = NoiseLevel.Silent;
            return s;
        }

        public Aabb Body(in MoveConfig config) =>
            Aabb.FromFeet(Position, config.BodyWidth, Crouching ? config.CrouchHeight : config.BodyHeight);
    }
}

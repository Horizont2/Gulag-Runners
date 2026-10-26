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

        // ----------------------------------------------------------------- the arm
        // Direct-aim combat (docs/31). The arm is not a state machine any more, it is a
        // DIRECTION, and the weapon is a segment hanging off the end of it.

        /// <summary>Unit vector from the shoulder, where the arm is pointing right now.</summary>
        public FixVec2 Arm;

        /// <summary>Last tick's weapon tip, so this tick's speed is a subtraction.</summary>
        public FixVec2 TipPrev;

        /// <summary>
        /// False until the tip has been recorded once. Without it the first tick of a
        /// match reads a tip speed of "all the way from the origin", which is a free
        /// kill on frame one.
        /// </summary>
        public bool TipPrimed;

        /// <summary>Metres the tip travelled last tick. This is what damage is made of.</summary>
        public Fix TipSpeed;

        /// <summary>
        /// How far this swing has travelled in one direction, in radians, and which
        /// direction that is. A swing BEGINS where the arm reverses and ENDS at the next
        /// reversal -- which is the only definition of "a swing" the simulation has, and
        /// it comes free out of the arm's own motion rather than out of a button.
        /// </summary>
        public Fix Swept;
        public int TurnSign;

        /// <summary>
        /// Which swing this is, and which one already drew blood. One hit per swing: a
        /// blade may not bill the same body twice for one stroke, however long the
        /// stroke lies across it -- and at the distance fighters actually stand, a
        /// weapon's tip is inside the other one between a third and a half of the time.
        /// </summary>
        public int SwingId;
        public int SpentOn;

        /// <summary>
        /// The stick reading the aim assist last resolved against, and what it resolved
        /// to. Held so the assist only re-aims when the THUMB moves.
        ///
        /// Without that it re-aims every tick, and the direction to a man you are
        /// walking into swings faster the closer you get -- so the assist moved the arm
        /// by itself, that movement is tip speed, and tip speed is damage. Every weapon
        /// in the game could kill by walking forward with the stick held. The assist is
        /// there to help a SWING, not to hold a lock.
        /// </summary>
        public FixVec2 AimPrev;
        public FixVec2 AssistDir;
        public bool AssistHeld;

        /// <summary>Ticks until this weapon may hurt anybody again. One arc, one hit.</summary>
        public int HitLock;

        /// <summary>Ticks since this arm was beaten aside, during which it cannot land.</summary>
        public int ClashLock;

        /// <summary>Output: weapons met this tick. For the spark and the sound.</summary>
        public bool WasClash;

        /// <summary>
        /// The blades were already touching last tick.
        ///
        /// A clash has to be an EDGE, not a state, and this is what remembers which.
        /// Measured without it: two fighters at close range have their weapons crossed
        /// on 899 ticks out of 900 -- long weapons are longer than the gap people fight
        /// at -- so the lock was refreshed every single tick and neither of them could
        /// ever land a blow again. A permanent stalemate, reachable by walking forwards.
        /// Blades MEETING is a parry; blades resting against each other is just two
        /// people standing close.
        /// </summary>
        public bool InContact;

        /// <summary>Output: how much of a full blow this fighter landed, in thousandths.</summary>
        public int DealtPermille;

        /// <summary>
        /// How dangerous the weapon is RIGHT NOW, in thousandths of a full-power blow.
        /// Zero means the blade is being carried rather than swung, and nothing it
        /// touches will be hurt. Everything that used to ask "is this fighter in their
        /// active frames" asks this instead, and gets a better answer: a number the
        /// player is directly moving, rather than a window they are waiting out.
        /// </summary>
        public int SwingShare;

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
        /// <summary>Frames the attacker's own recoil coasts after landing a blow.</summary>
        public int RecoilTimer;

        public byte StunStack;
        public int StunStackTimer;

        /// <summary>
        /// Where the rise ends and the step across begins, as a fraction of the window.
        ///
        /// Worked out when the move starts rather than per tick, because it is sized from
        /// the speed the body is ALREADY travelling at, which only the start knows.
        /// </summary>
        public Fix ScriptRiseShare;

        public bool ScriptRises;





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

        public bool JustDied;

        /// <summary>Output: damage taken this tick, for the view to react to.</summary>
        public short DamageTaken;

        /// <summary>Output: a blow landed on this fighter this tick.</summary>
        public bool WasHit;

        /// <summary>
        /// Output: a blade met a blade this tick. It keeps the old parry name because
        /// every spark, sound and camera shake already wired to a parry is exactly what
        /// this is -- the parry simply stopped being a timing window and became a place
        /// to hold your weapon.
        /// </summary>
        public bool WasParried;

        /// <summary>The dodge's invulnerable frames (docs/02).</summary>
        public bool Invulnerable => DodgeTimer > 0;

        /// <summary>Hurt, staggered or dead: not steering, not swinging.</summary>
        public bool Reeling => HitstunTimer > 0 || StaggerTimer > 0 || Dead;

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

        public bool OnGround => Mode == MoveMode.Grounded;

        public static PlayerSimState Spawn(FixVec2 position, sbyte facing, in MoveConfig config,
                                           in ArmConfig arm)
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
            s.Noise = NoiseLevel.Silent;
            // In the carry pose, which is the pose a fighter who is not asking for
            // anything is in. Straight out was the OLD rest pose, and docs/31 took it
            // out for a measured reason: an arm held level is a perfect guard, free and
            // permanent, so two fighters who had not touched a stick clashed for ever.
            // Spawning into it meant every round opened in the pose the design removed,
            // and the arm then visibly dropped once the first tick ran.
            s.Arm = ArmMotor.Carry(in s, in arm);
            s.SpentOn = -1;
            s.TipPrimed = false;
            return s;
        }

        public Aabb Body(in MoveConfig config) =>
            Aabb.FromFeet(Position, config.BodyWidth, Crouching ? config.CrouchHeight : config.BodyHeight);
    }
}

namespace GulagRunners.Sim
{
    /// <summary>
    /// Direct-aim combat: the stick points the arm, and a weapon hurts what it touches.
    ///
    /// This replaces the windup/active/recovery machine, and the argument for it is in
    /// docs/30 and docs/31. The short version is that the old model put a state machine
    /// between the thumb and the blade -- four knobs multiplying each other across two
    /// files -- and measured out at a 49-frame windup on a warhammer, a hitbox alive for
    /// 3.5% of the commitment, and twelve weapons out of twelve that could not reach a
    /// man walking backwards. Here there is no windup to be wrong about. The arm is
    /// WHERE YOU PUT IT.
    ///
    /// Three rules carry the whole design:
    ///
    ///   1. The weapon's own AttackFrames becomes its ANGULAR SPEED, not a timer. It
    ///      keeps its meaning -- a dagger at 9 and a warhammer at 34 are still fast and
    ///      slow -- but now it is how many ticks the arm takes to sweep half a circle,
    ///      which is a thing the player can see rather than a number they wait out.
    ///
    ///   2. Damage comes from the SPEED OF THE TIP, as a share of the fastest that
    ///      weapon's own arm can move it. A blade resting against somebody does nothing.
    ///      Walking into them with it out does nothing: a body runs at 3.6 m/s and a
    ///      dagger tip tops out near 24, so a walk is 15% of a swing and lands under the
    ///      floor. This is the rule that kills the lawnmower, and it is the same rule
    ///      that makes a heavy weapon committal without a single frame of windup -- a
    ///      warhammer needs most of an arc to reach its own top speed, and everyone can
    ///      see the arc.
    ///
    ///   3. Weapons stop weapons. Two blades that touch clash, nothing gets through, and
    ///      the slower arm is knocked aside. That is the block and the parry, with no
    ///      button: you hold the thing between you and him, which is what a person does.
    /// </summary>
    public struct ArmConfig
    {
        /// <summary>
        /// Shoulder height as a share of body height, in thousandths. The arm hangs from
        /// here and every length below is measured from it.
        /// </summary>
        public int ShoulderPermille;

        /// <summary>
        /// Arm length as a share of body height, in thousandths. The weapon's reach is
        /// added beyond the hand.
        ///
        /// A SHARE and not metres, which is the whole bug this field used to carry. It
        /// was 0.45 m, written against the 1.8 m fighter in MoveConfig's defaults -- and
        /// the fighter in the prefabs is 0.91 m tall. So the arm was half the height of
        /// its owner, the IK goal sat far outside anything the shoulder could reach, and
        /// Unity did the only thing it can: dragged the spine out to meet it. That is
        /// the body bending itself into knots at some angles.
        ///
        /// 350 is roughly where a human arm is, and it is right at any size.
        /// </summary>
        public int ArmLengthPermille;

        /// <summary>
        /// Ticks for the arm to sweep half a circle, with a weapon whose AttackFrames is
        /// <see cref="ReferenceFrames"/>. Every weapon scales off this by its own frames,
        /// so the table in ItemTable still says which weapons are quick.
        /// </summary>
        public int SweepFrames;
        public int ReferenceFrames;

        /// <summary>
        /// How much of the stick's push is needed before the arm leaves its rest pose,
        /// in thousandths. Below it the arm goes to the carry pose below.
        /// </summary>
        public int DeadzonePermille;

        /// <summary>
        /// How far BELOW horizontal the weapon is carried when the stick is centred, in
        /// thousandths of a right angle's worth of drop.
        ///
        /// This number exists because of a measured bug, and it is not a cosmetic one.
        /// The carry pose used to be straight ahead -- arm horizontal, pointed at the
        /// opponent -- which meant every fighter was holding a perfect guard, for free,
        /// permanently, without touching the stick. Weapons stop weapons, so two fighters
        /// standing still with their arms out simply clashed for ever: two of the twelve
        /// weapons could not kill a motionless opponent in forty seconds.
        ///
        /// Carried LOW, a guard becomes a thing you have to ask for by pushing the stick
        /// at them and holding it there -- which costs you the swing you are not making.
        /// That is the trade the whole fight is built on.
        /// </summary>
        public int CarryDropPermille;

        /// <summary>
        /// How far the attack stick is read RELATIVE TO THE OTHER FIGHTER rather than to
        /// the world, in thousandths. 1000 means pushing the stick "forward" always
        /// means "at him", wherever he is standing.
        ///
        /// This is the answer to having to trace a moving man with a thumb, and it is a
        /// FRAME rather than a pull. Nothing is dragged toward him and no blow is
        /// steered: the stick's own direction is kept exactly, measured from the line to
        /// him instead of from world right. Push forward and the blade goes at him; push
        /// up and it goes over the top of him; push down and it goes under his guard --
        /// all of which are the things a player actually means.
        ///
        /// An earlier version pulled the blade toward him inside a cone, and it had to
        /// go: pulling the blade is pulling it off the line the OTHER man is holding his
        /// weapon on, so aim assist and blocking are the same number. Measured, a guard
        /// stopped taking any damage off at all. A frame costs blocking nothing, because
        /// it moves nobody's blade anywhere.
        /// </summary>
        public int AimRelativePermille;

        /// <summary>
        /// Tip speed below this share of the weapon's own maximum does no damage at all.
        ///
        /// The single most important number here. Too low and holding the blade out
        /// while walking is a strategy; too high and only a full windmill counts, which
        /// is the committal nonsense this system replaced.
        /// </summary>
        public int MinSwingPermille;

        /// <summary>
        /// How long a blade IS, given the reach its item says it has. The hitbox and the
        /// model have to be the same object, and these four numbers are the rule that
        /// makes them one: a haft as a share of body height, plus the reach the weapon
        /// adds over bare hands, held between a floor and a ceiling.
        ///
        /// It is not a new rule. PlayerLoadoutView has drawn weapons this way all along,
        /// and the comment there says why: reach is measured from the MIDDLE OF THE BODY,
        /// so it already contains half a torso and an arm, and a weapon is only the part
        /// it adds. What is new is the simulation using it. Before this, the blade the
        /// sim swung ran the full Reach from the hand -- an extra arm and half a torso of
        /// invisible steel past the tip of the model. Measured on the shipping fighter it
        /// came out at 1.5x the weapon on screen for every one of the twelve, and bare
        /// fists swung a 0.60 m blade out of a hand that holds nothing.
        ///
        /// That one error is most of why the fight played badly. Blades longer than the
        /// gap between two fighters are crossed nearly all the time, every crossing is a
        /// clash, and a clash locks both arms: two people swinging at each other could
        /// stand there for forty seconds without landing a blow. It also made every blow
        /// land from somewhere the player could not see it coming.
        ///
        /// Bare hands get no blade at all. The fist IS the weapon, so the damaging part
        /// is the hand, and that is also exactly what gets drawn: nothing.
        /// </summary>
        public int HaftPermille;

        /// <summary>Of the reach a weapon adds over bare hands, how much is blade.</summary>
        public int ReachToBladePermille;

        /// <summary>Shortest and longest a blade may be, as shares of body height.</summary>
        public int MinBladePermille;
        public int MaxBladePermille;

        /// <summary>
        /// Body radius for the hit capsule, as a share of body height. The body is a
        /// segment from the feet to the head with this around it -- a capsule, the
        /// cheapest shape with no corners for a blade to catch on.
        ///
        /// A share, for the same reason as the arm: 0.30 m was a third of the way up a
        /// 0.91 m fighter.
        /// </summary>
        public int BodyRadiusPermille;

        /// <summary>Half-width of a weapon for weapon-on-weapon contact, likewise.</summary>
        public int BladeRadiusPermille;

        /// <summary>
        /// Ticks before the same weapon may hurt the same fighter again. One hit per
        /// swing, in effect: without it a blade laid across somebody bills them sixty
        /// times a second.
        /// </summary>
        public int HitLockFrames;

        /// <summary>
        /// How hard a clash throws the slower arm aside, in thousandths of the speed
        /// difference. This is what a parry FEELS like, and the only number that decides
        /// whether holding your weapon up is worth doing.
        /// </summary>
        public int ClashDeflectPermille;

        /// <summary>Ticks a fighter whose weapon was beaten aside cannot damage.</summary>
        public int ClashLockFrames;

        public static ArmConfig Default()
        {
            ArmConfig c;
            c.ShoulderPermille = 780;      // just below the head on a 1.8 m body
            c.ArmLengthPermille = 350;     // about where a human arm is

            // A reference weapon sweeps 180 degrees in its own AttackFrames. The club,
            // at 15, is the middle of the table and the one the feel was tuned on.
            c.SweepFrames = 15;
            c.ReferenceFrames = 15;

            c.DeadzonePermille = 220;
            c.CarryDropPermille = 700;     // about 35 degrees below level: a lowered blade
            c.AimRelativePermille = 1000;  // forward on the stick means at him

            // A quarter of the weapon's own top speed. Measured against a 3.6 m/s run:
            // a walk-in with a dagger reaches 15% and a spear 9%, so neither pays.
            c.MinSwingPermille = 250;

            // The same four numbers PlayerLoadoutView draws a weapon with, so the blade
            // that is swung is the blade that is seen. Its own defaults are in metres
            // against a 0.91 m fighter: a 0.246 m haft, a 0.28 m floor, a 1.30 m ceiling.
            c.HaftPermille = 270;
            c.ReachToBladePermille = 730;
            c.MinBladePermille = 310;
            c.MaxBladePermille = 1430;

            c.BodyRadiusPermille = 180;    // a bit wider than the collision box
            c.BladeRadiusPermille = 60;

            c.HitLockFrames = 18;          // 0.30 s, so one arc is one hit
            c.ClashDeflectPermille = 700;
            c.ClashLockFrames = 10;
            return c;
        }
    }
}

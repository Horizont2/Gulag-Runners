namespace GulagRunners.Sim
{
    /// <summary>
    /// Every number behind the fight, from docs/02.
    ///
    /// The target the rest is tuned against is a time-to-kill of 8 to 14 seconds of ACTIVE
    /// fighting. That is not the length of a match: most of a round is spent not in contact, and
    /// a fight that ends in two exchanges makes the whole scavenge phase pointless.
    /// </summary>
    public struct CombatConfig
    {
        public int MaxHealth;

        /// <summary>Frames the hitbox is live. Short: a long active window hits round corners.</summary>
        public int ActiveFrames;

        /// <summary>Recovery as a fraction of the weapon's own wind-up, in thousandths.</summary>
        public int RecoveryPermille;

        /// <summary>
        /// Frames after a swing during which another press continues the combo. docs/02 caps a
        /// combo at three — light, light, heavy — because longer strings on a phone are not depth,
        /// they are a dexterity tax.
        /// </summary>
        public int ComboWindowFrames;
        public int MaxCombo;

        /// <summary>The third hit of a combo, in thousandths of the weapon's damage.</summary>
        public int HeavyDamagePermille;
        /// <summary>And it takes this much longer to wind up and recover from.</summary>
        public int HeavyTimingPermille;

        /// <summary>
        /// Frames both fighters freeze for when a blow lands, is blocked, or is parried.
        ///
        /// Three separate numbers because they are three separate events and docs/02 asks
        /// that a player be able to tell them apart from across the arena. A parry gets the
        /// longest by a wide margin: it is the skill ceiling of the medieval mode, and the
        /// reward for finding one is that the world stops while everybody sees it happen.
        /// </summary>
        public int HitStopFrames;
        public int BlockStopFrames;
        public int ParryStopFrames;

        /// <summary>Hitstun per point of damage, in frames, and its ceiling.</summary>
        public int HitstunPerDamage;
        public int MaxHitstunFrames;

        /// <summary>
        /// Frames an attack press survives while it cannot be used yet.
        ///
        /// A press during recovery used to be thrown away, so continuing a string meant
        /// releasing and pressing again on the exact frame the swing ended. The buffer is
        /// the difference between a game that reads what you meant and one that asks you
        /// to guess a frame number.
        /// </summary>
        public int AttackBufferFrames;

        /// <summary>
        /// What a CONNECTED swing gets back of its own recovery, in thousandths.
        ///
        /// Measured before this existed, every weapon in the game was at a frame
        /// DISADVANTAGE after landing a blow: from -3 with a saber to -12 with a scythe.
        /// The person you just hit acted first. That makes the correct play never to swing
        /// first, and two players both playing correctly produce the stand-off that reads
        /// as a wooden fight. Halving the recovery of a swing that landed is what turns a
        /// hit into a turn.
        /// </summary>
        public int HitRecoveryCutPermille;

        /// <summary>
        /// The frames of initiative a clean hit is guaranteed to buy, whatever the weapon.
        ///
        /// Hitstun is raised to at least "what the attacker has left, plus this", so every
        /// weapon is at worst this far ahead after connecting. Damage can still earn more
        /// -- a warhammer stuns for longer than this floor on its own -- but nothing is
        /// ever punished for landing a blow.
        /// </summary>
        public int HitAdvantageFrames;

        public Fix KnockbackSpeed;
        public Fix HeavyKnockbackSpeed;

        /// <summary>
        /// Upward part of a knockback, and ONLY on the heavy finisher.
        ///
        /// It used to be every hit, and it was the quietest of the reasons the fight felt
        /// bad: 1.6 m/s against this gravity is a peak of three centimetres -- nothing you
        /// can see -- but it took the victim off the ground for 21 frames while stunning
        /// them for 8. So a light blow produced no visible lift, slid the victim away with
        /// no footing, and put them out of reach before the follow-up could land. Keeping
        /// them planted is what makes a second hit possible at all; the finisher still
        /// lifts, and that is what makes the finisher read as the finisher.
        /// </summary>
        public Fix KnockbackLift;

        // ---- blocking (docs/02: the medieval mode's answer to pressure)
        /// <summary>Damage that still gets through a block, in thousandths.</summary>
        public int ChipPermille;

        /// <summary>
        /// Stamina spent for every hit absorbed. It has to outrun the stamina regen or holding
        /// the guard is free, which is the one thing a guard must never be.
        /// </summary>
        public int BlockStaminaCost;

        /// <summary>Frames the guard is down for after it is broken.</summary>
        public int GuardBreakFrames;
        /// <summary>Movement speed while holding a block, in thousandths of the run speed.</summary>
        public int BlockSpeedPermille;

        /// <summary>
        /// Movement speed through the three phases of a swing, in thousandths of the run
        /// speed. 1000 is full speed; 0 is rooted.
        ///
        /// A swing used to take the movement keys away outright, and a fighter who stops
        /// dead to attack is a fighter who cannot attack while he is going anywhere — every
        /// exchange becomes two people standing still taking turns. But a strike with no
        /// weight behind it is worse than one that is slow, so the phases differ and the
        /// difference is the whole feel of the thing: you gather while still moving, you
        /// plant for the blow itself, and you are slow to get going again afterwards.
        ///
        /// The commitment is kept where it belongs — a swing still cannot be cancelled with
        /// a jump or a dodge. It is the walking that is given back, not the escape.
        /// </summary>
        public int WindupSpeedPermille;
        public int ActiveSpeedPermille;
        public int RecoverySpeedPermille;

        /// <summary>
        /// Frames after raising the guard during which a hit is PARRIED instead of blocked: no
        /// damage at all, and the attacker is staggered. The one window in the game that rewards
        /// reading the other player rather than reacting to them.
        /// </summary>
        public int ParryFrames;

        /// <summary>
        /// Frames of holding the guard that cost one charge. docs/02 spends stamina on holding
        /// the block, and without that a raised guard is free: a fighter can walk the whole
        /// arena braced at 42% speed and never find out why they are slow. 0 turns it off.
        /// </summary>
        public int GuardDrainFrames;

        /// <summary>
        /// Below this share of full health, bare hands hit for DesperationDamagePermille.
        /// docs/02 asks for it by name: it is the only thing that makes an unarmed fighter
        /// frightening, and it only ever helps whoever is losing.
        /// </summary>
        public int DesperationHealthPermille;
        public int DesperationDamagePermille;

        /// <summary>
        /// What bare hands do in sudden death, per mille. docs/01: "кулаки/безоружний удар
        /// б'ють ×2" — on the one platform, with the ceiling down, the fighter who has lost or
        /// spent their weapon is not out of the fight.
        ///
        /// It does not stack with desperation: the larger of the two applies. Two multipliers
        /// on the same punch is a one-hit kill arriving from a rule nobody could see.
        /// </summary>
        public int SuddenDeathBarePermille;

        /// <summary>
        /// Landing faster than this hurts, by FallDamagePerSpeed for every m/s over it.
        /// docs/02 counts a fall off a floor among the ways a fight ends. 12.5 m/s is the
        /// speed reached dropping about 2.8 m, so every drop inside a storey is free and the
        /// full height of the arena is not.
        /// </summary>
        public Fix FallDamageSpeed;
        public int FallDamagePerSpeed;
        public int StaggerFrames;

        public int DeathFrames;

        public static CombatConfig Default()
        {
            CombatConfig c;
            c.MaxHealth = 100;

            c.ActiveFrames = 3;
            c.RecoveryPermille = 700;      // recovery is 0.7 of the wind-up

            c.ComboWindowFrames = 20;      // 0.33 s
            c.MaxCombo = 3;
            c.HeavyDamagePermille = 1700;
            c.HeavyTimingPermille = 1500;

            // 0.05 s on a hit, 0.03 blocked, 0.12 on a parry. Small numbers: hit stop is
            // felt rather than seen, and past about a tenth of a second on an ordinary hit it
            // stops reading as weight and starts reading as a dropped frame.
            c.HitStopFrames = 3;
            c.BlockStopFrames = 2;
            c.ParryStopFrames = 7;

            c.HitstunPerDamage = 1;
            c.MaxHitstunFrames = 34;   // was 26, which clipped the floor below off a warhammer

            c.AttackBufferFrames = 14;     // 0.23 s: clears the longest connected recovery (11)
            c.HitRecoveryCutPermille = 500;
            c.HitAdvantageFrames = 4;      // +4 on hit with everything, more with the heavy

            c.KnockbackSpeed = Fix.FromMilli(3200);
            c.HeavyKnockbackSpeed = Fix.FromMilli(6000);
            c.KnockbackLift = Fix.FromMilli(1600);

            c.ChipPermille = 300;          // docs/02: a block absorbs 70%, not 85%
            c.BlockStaminaCost = 1;        // one of the three charges docs/02 gives a fighter
            c.GuardBreakFrames = 42;       // 0.7 s wide open
            c.BlockSpeedPermille = 420;

            c.WindupSpeedPermille = 620;   // gathering: you may still close or back off
            c.ActiveSpeedPermille = 180;   // the blow itself; a crawl, not a stop
            c.RecoverySpeedPermille = 400; // heavy on your feet again

            c.ParryFrames = 12;            // 0.20 s, straight from docs/02
            c.GuardDrainFrames = 48;       // a charge every 0.8 s, so a full pool holds 3.2 s
            c.StaggerFrames = 48;          // 0.80 s, likewise: the parry is the skill ceiling

            c.DesperationHealthPermille = 300;   // docs/02: bare hands below 30% HP
            c.DesperationDamagePermille = 1400;
            c.SuddenDeathBarePermille   = 2000;  // docs/01: bare hands x2 on the last platform

            c.FallDamageSpeed = Fix.FromMilli(12500);
            c.FallDamagePerSpeed = 8;

            c.DeathFrames = 90;
            return c;
        }
    }
}

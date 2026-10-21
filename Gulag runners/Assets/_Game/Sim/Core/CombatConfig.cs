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
        /// What each FURTHER hit of a string is worth, in thousandths of the one before.
        ///
        /// Without it the game had an infinite. Measured with both fighters holding attack
        /// in range: a bare-handed fighter landed twenty blows in eight seconds and the man
        /// with a club landed NONE -- he started twenty swings and every one of them was
        /// cancelled by the next punch. Fists cycle in 19 frames; his stun plus his own
        /// wind-up came to 22, so he was always hit first, for ever. The fast weapon simply
        /// won, and the whole loot layer with it.
        ///
        /// So the guarantee below applies to the FIRST hit of a string only. That one
        /// confirms and buys the turn; the ones after it are paid for out of damage alone
        /// and worth this much less each time, until the string runs out and the other
        /// player gets to move.
        /// </summary>
        public int ComboStunDecayPermille;

        /// <summary>Frames of not being hit that end a string and clear the decay.</summary>
        public int ComboStackFrames;

        /// <summary>
        /// Extra knockback per hit already taken in this string, in thousandths. The second
        /// way a string ends: the attacker runs out of reach and has to step back in, and
        /// stepping in is what costs him the frames that hand the turn over.
        /// </summary>
        public int ComboPushPermille;

        /// <summary>
        /// Every weapon's wind-up and recovery, in thousandths of the table's own numbers.
        ///
        /// One knob over the whole table, because the question it answers is about the
        /// whole fight: can a player SEE the blow coming? A dagger wound up in 9 frames and
        /// a pair of fists in 12 -- 0.15 and 0.20 s -- and human reaction to a visual cue is
        /// about 0.25. At those speeds there is nothing to read and nothing to answer with,
        /// which is most of why the fight felt like a blur rather than an exchange. Scaling
        /// here rather than editing twelve rows keeps the weapons' relationship to each
        /// other -- which is the design -- and makes the pace one number to tune.
        /// </summary>
        public int AttackFramesPermille;

        /// <summary>
        /// What the ATTACKER is thrown back by, in thousandths of the victim's knockback.
        ///
        /// Knockback ends a string by taking the victim out of reach -- except against a
        /// wall, where there is nowhere for them to go. Measured in the corner, a
        /// bare-handed fighter still looped a spearman twenty hits to none while the same
        /// pair on open floor read one to two the other way. Pushing the attacker off
        /// instead is the oldest answer in the genre: the separation happens either way,
        /// and who moves is decided by who has room.
        /// </summary>
        public int AttackerRecoilPermille;

        /// <summary>Frames the attacker's recoil coasts before the ground takes it back.</summary>
        public int RecoilFrames;

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

        // ---- what the worn kit is allowed to add up to
        /// <summary>
        /// The most all three worn pieces together may remove from a blow, in thousandths.
        ///
        /// docs/02 priced ARMOUR at -15/-30/-45% by rarity, back when armour was one slot.
        /// It is three slots now, and that number is what a FULL SET of one rarity comes
        /// to: the helmet, the chest and the belt share it out rather than each carrying
        /// it. So the shipped table tops out at exactly the 450 docs/02 asks for, the
        /// 8-14 s time to kill survives the best kit in the game, and this cap never
        /// fires — which is the point of it. It is the guard rail for the day a fourth
        /// worn piece is added and nobody re-adds the three numbers.
        /// </summary>
        public int MaxArmourPermille;

        /// <summary>
        /// The most the worn kit may take off the hitstun, in thousandths.
        ///
        /// Only the helmet carries any, so today this is a ceiling over one item rather than
        /// a sum — and it stays a config knob for the same reason as the one above: the day
        /// a second piece carries stun resistance, the question of what they add up to is
        /// already answered.
        /// </summary>
        public int MaxStunResistPermille;

        // ---- blocking (docs/02: the medieval mode's answer to pressure)
        /// <summary>Damage that still gets through a block, in thousandths.</summary>
        public int ChipPermille;

        /// <summary>
        /// Stamina spent for every hit absorbed. It has to outrun the stamina regen or holding
        /// the guard is free, which is the one thing a guard must never be.
        /// </summary>
        /// <summary>
        /// Stamina spent for every LIGHT hit absorbed. Zero, and deliberately.
        ///
        /// At one charge a blow it was four blows and out: measured against a bare-handed
        /// fighter mashing, a swordsman's guard broke after 1.2 SECONDS and left him wide
        /// open for another 0.7. The only answer the game offers to being rushed was
        /// therefore the fastest way to lose, which is why a player with a weapon felt he
        /// had no move at all. Holding the guard still costs -- GuardDrainFrames charges it
        /// by the second, so a braced fighter still runs down -- but absorbing a jab no
        /// longer does. What breaks a guard is a committed blow, not a flurry.
        /// </summary>
        public int BlockStaminaCost;

        /// <summary>What the heavy finisher costs the man behind the guard. This is what a
        /// guard is broken BY: one committed swing, read and answered, not four jabs.</summary>
        public int HeavyBlockStaminaCost;

        /// <summary>
        /// What the attacker is thrown back by when the guard eats it, against the victim's
        /// own knockback. Larger than on a clean hit: meeting a braced shield is the one
        /// thing in a fight that should put the man swinging back on his heels, and it is
        /// what stops a rush simply standing in your face.
        /// </summary>
        public int BlockRecoilPermille;

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
        /// <summary>
        /// Gathering: you may still close or back off, at this share of the run.
        ///
        /// Cut from 620 when the wind-ups were lengthened by a third, because what matters
        /// is the GROUND covered during one, not the speed: at 620 a slower club carried
        /// its owner 0.59 m backwards before the blow went live, which is far enough out of
        /// his own 1.25 m reach that a fighter walking away could no longer land the swing
        /// he had aimed behind him. 480 keeps the distance where it was.
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

            c.AttackBufferFrames = 18;     // 0.30 s: clears the longest connected recovery (15)
            c.HitRecoveryCutPermille = 500;
            c.HitAdvantageFrames = 4;      // +4 on the FIRST hit, more with the heavy
            c.ComboStunDecayPermille = 620;
            c.ComboStackFrames = 75;       // 1.25 s without being hit and the string is over
            c.ComboPushPermille = 300;
            c.AttackerRecoilPermille = 400;
            c.RecoilFrames = 10;
            c.AttackFramesPermille = 1450;   // 0.28 s to read the commonest wind-up

            c.KnockbackSpeed = Fix.FromMilli(3200);
            c.HeavyKnockbackSpeed = Fix.FromMilli(6000);
            c.KnockbackLift = Fix.FromMilli(1600);

            c.MaxArmourPermille = 500;     // the full legendary kit is 450; this never fires
            c.MaxStunResistPermille = 400; // a great helm is 380 of it

            c.ChipPermille = 300;          // docs/02: a block absorbs 70%, not 85%
            c.BlockStaminaCost = 0;        // a jab does not break a guard; holding it still drains
            c.HeavyBlockStaminaCost = 2;   // a committed blow does
            c.BlockRecoilPermille = 900;
            c.GuardBreakFrames = 42;       // 0.7 s wide open
            c.BlockSpeedPermille = 420;

            c.WindupSpeedPermille = 480;   // gathering: you may still close or back off
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

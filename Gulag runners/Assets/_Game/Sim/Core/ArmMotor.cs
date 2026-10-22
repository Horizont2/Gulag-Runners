namespace GulagRunners.Sim
{
    /// <summary>
    /// The arm, and everything a weapon touching somebody means.
    ///
    /// Two halves, in this order every tick:
    ///
    ///   Step     one fighter's arm swings toward where their stick is pointing, as
    ///            fast as that weapon allows, and the tip's speed is recorded.
    ///   Resolve  once for the whole match, after every arm has moved: weapons against
    ///            weapons first, then weapons against bodies.
    ///
    /// Weapons are checked against each other BEFORE bodies, and that ordering is the
    /// block. A blade that has been caught cannot be the blade that lands, so holding
    /// your weapon in the way genuinely stops the other one -- not by a flag that says
    /// "blocking" but because the geometry got there first.
    /// </summary>
    public static class ArmMotor
    {
        /// <summary>Half a turn, in radians. The arm's sweep is measured in these.</summary>
        static readonly Fix Pi = Fix.FromMilli(3142);

        // ------------------------------------------------------------------ geometry

        /// <summary>Where the arm hangs from, in world space.</summary>
        public static FixVec2 Shoulder(in PlayerSimState s, in MoveConfig move,
                                       in ArmConfig cfg)
        {
            Fix height = s.Crouching ? move.CrouchHeight : move.BodyHeight;
            return new FixVec2(s.Position.X,
                               s.Position.Y + height * Fix.FromMilli(cfg.ShoulderPermille));
        }

        /// <summary>The grip. The weapon runs from here to the tip.</summary>
        public static FixVec2 Hand(in PlayerSimState s, in MoveConfig move,
                                   in ArmConfig cfg) =>
            Shoulder(in s, in move, in cfg) + s.Arm * ArmLength(in s, in move, in cfg);

        public static FixVec2 Tip(in PlayerSimState s, in MoveConfig move,
                                  in ArmConfig cfg) =>
            Shoulder(in s, in move, in cfg)
            + s.Arm * (ArmLength(in s, in move, in cfg)
                       + BladeLength(in move, in cfg, s.Inventory.WeaponDef.Reach));

        /// <summary>
        /// How much steel hangs past the hand: the blade that is DRAWN, not the item's
        /// reach. ArmConfig says why they are not the same thing.
        ///
        /// Reach is measured from the middle of the body, so a weapon is the part it
        /// adds over bare hands and nothing more. Bare hands add nothing and get no
        /// blade; the hand itself is then the whole weapon, which is correct and is also
        /// what the model shows.
        /// </summary>
        public static Fix BladeLength(in MoveConfig move, in ArmConfig cfg, Fix reach)
        {
            Fix bare = ItemTable.Get(ItemId.Fists).Reach;
            if (reach <= bare) return Fix.Zero;

            Fix want = move.BodyHeight * Fix.FromMilli(cfg.HaftPermille)
                       + (reach - bare) * Fix.FromMilli(cfg.ReachToBladePermille);

            Fix floor = move.BodyHeight * Fix.FromMilli(cfg.MinBladePermille);
            Fix ceiling = move.BodyHeight * Fix.FromMilli(cfg.MaxBladePermille);
            if (want < floor) return floor;
            return want > ceiling ? ceiling : want;
        }

        /// <summary>
        /// The lengths that used to be metres, now read off the fighter's own height.
        ///
        /// Everything about the arm scales with its owner. Written in metres they were
        /// written against ONE fighter -- the 1.8 m one in MoveConfig's defaults -- and
        /// the one in the prefabs is 0.91 m, so the arm came out half his height.
        ///
        /// Against BodyHeight and never CrouchHeight: a crouch changes the shape a man
        /// is in, not the size he is. Ducking moves the shoulder down, which Shoulder
        /// already does, and it does not shorten his arm or his sword.
        /// </summary>
        public static Fix ArmLength(in PlayerSimState s, in MoveConfig move,
                                    in ArmConfig cfg) =>
            move.BodyHeight * Fix.FromMilli(cfg.ArmLengthPermille);

        public static Fix BodyRadius(in PlayerSimState s, in MoveConfig move,
                                     in ArmConfig cfg) =>
            move.BodyHeight * Fix.FromMilli(cfg.BodyRadiusPermille);

        public static Fix BladeRadius(in PlayerSimState s, in MoveConfig move,
                                      in ArmConfig cfg) =>
            move.BodyHeight * Fix.FromMilli(cfg.BladeRadiusPermille);

        /// <summary>The body, as the segment a capsule is built around.</summary>
        public static void BodySegment(in PlayerSimState s, in MoveConfig move,
                                       out FixVec2 feet, out FixVec2 head)
        {
            Fix height = s.Crouching ? move.CrouchHeight : move.BodyHeight;
            feet = new FixVec2(s.Position.X, s.Position.Y + move.BodyWidth / 2);
            head = new FixVec2(s.Position.X, s.Position.Y + height - move.BodyWidth / 2);
        }

        /// <summary>
        /// Radians the arm may turn this tick, as a chord.
        ///
        /// The weapon's AttackFrames IS this number: a weapon of N frames sweeps half a
        /// circle in N ticks. For unit vectors a chord and its angle agree to within a
        /// few percent up to about 30 degrees, and a step this size is always far under
        /// that, so clamping the chord is clamping the angle.
        /// </summary>
        public static Fix Slew(in PlayerSimState s, in ArmConfig cfg)
        {
            int frames = s.Inventory.WeaponDef.AttackFrames;
            if (frames < 1) frames = 1;

            int ticks = cfg.ReferenceFrames > 0
                ? cfg.SweepFrames * frames / cfg.ReferenceFrames
                : frames;
            if (ticks < 1) ticks = 1;
            return Pi / ticks;
        }

        /// <summary>The fastest this weapon's tip can be made to travel, per tick.</summary>
        public static Fix TopTipSpeed(in PlayerSimState s, in MoveConfig move,
                                      in ArmConfig cfg) =>
            Slew(in s, in cfg) * (ArmLength(in s, in move, in cfg)
                                  + BladeLength(in move, in cfg, s.Inventory.WeaponDef.Reach));

        // ------------------------------------------------------------------ the swing

        /// <summary>
        /// Point the arm, and remember how fast the tip went.
        ///
        /// `aim` is the stick, unnormalised, in the range the input layer hands over.
        /// A stick inside the deadzone is not a command to point anywhere: the arm falls
        /// back to the way the body is facing, which is how the weapon is carried.
        /// </summary>
        public static void Step(ref PlayerSimState s, FixVec2 aim, in MoveConfig move,
                                in ArmConfig cfg) =>
            Step(ref s, aim, FixVec2.Zero, false, in move, in cfg);

        /// <summary>
        /// As above, but knowing where the other fighter is, so the aim can be helped.
        /// </summary>
        public static void Step(ref PlayerSimState s, FixVec2 aim, FixVec2 foeShoulder,
                                bool hasFoe, in MoveConfig move, in ArmConfig cfg)
        {
            FixVec2 want;
            Fix push = aim.LengthSq;
            Fix dead = Fix.FromMilli(cfg.DeadzonePermille);

            if (push > dead * dead)
                want = Assist(ref s, aim, foeShoulder, hasFoe, in move, in cfg);
            else
            {
                want = Carry(in s, in cfg);
                s.AssistHeld = false;
            }

            // Dead, stunned or frozen: the arm drops to the carry pose and stops being a
            // weapon. A corpse whose blade still answered the stick would keep killing.
            if (s.Dead || s.HitStopTimer > 0 || s.HitstunTimer > 0 || s.StaggerTimer > 0)
                want = Carry(in s, in cfg);

            if (s.Arm.LengthSq.Raw == 0)
                s.Arm = new FixVec2(Fix.FromInt(s.Facing == 0 ? 1 : s.Facing), Fix.Zero);

            // Toward the target by at most one step of chord, then back onto the unit
            // circle. No trigonometry, one square root, and the turn naturally eases as
            // it arrives, which is what an arm does.
            FixVec2 delta = (want - s.Arm).ClampLength(Slew(in s, in cfg));
            s.Arm = (s.Arm + delta).Normalized;
            if (s.Arm.LengthSq.Raw == 0)
                s.Arm = new FixVec2(Fix.FromInt(s.Facing == 0 ? 1 : s.Facing), Fix.Zero);

            // The tip's speed IN THE FIGHTER'S OWN FRAME -- how far it moved around the
            // shoulder, not how far it moved across the arena.
            //
            // Measured both ways. In world space a warhammer carried at a dead run
            // reaches 35% of its own top tip speed, because its arm is slow and a body
            // is not, and 35% was over the floor: three weapons could kill by walking
            // into somebody with the blade held still. That is the lawnmower the whole
            // design is supposed to prevent, and a higher floor would not have fixed it
            // -- it would have made light weapons unusable to buy off a bug in heavy
            // ones. Relative to the shoulder, a walk contributes exactly nothing, which
            // is also the honest statement: a swing is what your ARM did.
            FixVec2 local = s.Arm * (ArmLength(in s, in move, in cfg)
                                     + BladeLength(in move, in cfg, s.Inventory.WeaponDef.Reach));
            if (!s.TipPrimed)
            {
                s.TipPrev = local;
                s.TipPrimed = true;
            }

            s.TipSpeed = (local - s.TipPrev).Length;
            s.TipPrev = local;

            s.SwingShare = SwingPermille(in s, in move, in cfg);

            if (s.HitLock > 0) s.HitLock--;
            if (s.ClashLock > 0) s.ClashLock--;
        }

        static int s_weaponFrames(in PlayerSimState s)
        {
            int f = s.Inventory.WeaponDef.AttackFrames;
            return f < 1 ? 1 : f;
        }

        /// <summary>
        /// Reads the stick in the other fighter's frame instead of the world's.
        ///
        /// Forward on the stick means AT HIM, up means over him, down means under him.
        /// The direction the thumb asked for is kept exactly -- it is only measured from
        /// a different zero -- so nothing is steered, nothing snaps, and a blade sent
        /// deliberately over a guard goes exactly as far over it as the player asked.
        ///
        /// Which is why it replaced a cone-and-pull assist: pulling a blade toward a man
        /// pulls it off the line the OTHER man is holding HIS weapon on, so an assist
        /// and a block are the same number fighting each other. Measured, a held guard
        /// stopped taking any damage off at all.
        /// </summary>
        static FixVec2 Assist(ref PlayerSimState s, FixVec2 aim, FixVec2 foeShoulder,
                              bool hasFoe, in MoveConfig move, in ArmConfig cfg)
        {
            FixVec2 want = aim.Normalized;

            // Only when the THUMB moved -- measured since the last time the frame was
            // TAKEN, not since the last tick. That distinction is the whole of it: at
            // 60 Hz a thumb sliding round the ring moves about a hundredth of the stick
            // per tick, which never clears any sane threshold on its own, so comparing
            // tick to tick meant the arm froze and stopped answering the stick at all
            // for as long as you moved it smoothly. Against the last frame the movement
            // ADDS UP and crosses in a few ticks, which is what "the thumb moved" has
            // to mean.
            Fix moved = (aim - s.AimPrev).LengthSq;
            bool steady = s.AssistHeld && moved <= Fix.FromMilli(20) * Fix.FromMilli(20);

            if (steady) return s.AssistDir;

            s.AimPrev = aim;
            s.AssistHeld = true;
            s.AssistDir = want;

            if (!hasFoe || cfg.AimRelativePermille <= 0) return want;

            // Shoulder to SHOULDER, and that is the whole of the bug this used to have.
            // It measured from my shoulder to his chest -- the same anatomical point on
            // nobody -- so on flat ground the frame leaned 0.26 m downhill at every
            // distance. Pushing the stick right put the arm 17 degrees down at sparring
            // range, 40 degrees down at half a metre, and straight DOWN when the two of
            // you touched, because at zero gap the only direction left between a
            // shoulder and a chest is the drop between them. Up and down swapped places
            // as you closed. Between two shoulders on flat ground, "at him" is level,
            // which is what the player means by it.
            FixVec2 mine = Shoulder(in s, in move, in cfg);
            Fix dx = foeShoulder.X - mine.X;
            Fix dy = foeShoulder.Y - mine.Y;

            // Nearer than this and the frame stops asking exactly where he is: two
            // fighters on one spot have no direction between them, and just short of
            // that the direction is the difference of two nearly equal numbers.
            Fix near = move.BodyHeight * Fix.FromMilli(cfg.AimFloorPermille);
            if (Fix.Abs(dx) < near)
            {
                int side = dx.Raw > 0 ? 1 : dx.Raw < 0 ? -1 : (s.Facing == 0 ? 1 : s.Facing);
                dx = near * Fix.FromInt(side);
            }

            // And it never leans past its cap, so a man on a ledge overhead cannot
            // rotate the stick out from under the player's thumb.
            Fix lean = Fix.Abs(dx) * Fix.FromMilli(cfg.AimTiltPermille);
            if (dy > lean) dy = lean;
            else if (dy < -lean) dy = -lean;

            FixVec2 toFoe = new FixVec2(dx, dy).Normalized;
            if (toFoe.LengthSq.Raw == 0) return want;

            // The frame's forward axis: the line to him, blended back toward the way the
            // body is facing by however much of the world is being kept.
            FixVec2 ahead = new FixVec2(Fix.FromInt(s.Facing == 0 ? 1 : s.Facing), Fix.Zero);
            FixVec2 axis = (ahead + (toFoe - ahead) * Fix.FromMilli(cfg.AimRelativePermille))
                           .Normalized;
            if (axis.LengthSq.Raw == 0) return want;

            // Rotating a vector by the angle of a unit vector is one complex multiply.
            // No trigonometry, no angles, nothing to go round the wrong way at 180.
            FixVec2 turned = new FixVec2(want.X * axis.X - want.Y * axis.Y,
                                         want.X * axis.Y + want.Y * axis.X);
            if (turned.LengthSq.Raw != 0) s.AssistDir = turned.Normalized;
            return s.AssistDir;
        }

        /// <summary>Where the weapon rests when nobody is asking for anything: low.</summary>
        public static FixVec2 Carry(in PlayerSimState s, in ArmConfig cfg) =>
            new FixVec2(Fix.FromInt(s.Facing == 0 ? 1 : s.Facing),
                        -Fix.FromMilli(cfg.CarryDropPermille)).Normalized;

        /// <summary>
        /// What share of this weapon's full blow the tip has earned, in thousandths.
        /// Zero below the floor, so a carried weapon and a walk-in are both harmless.
        /// </summary>
        public static int SwingPermille(in PlayerSimState s, in MoveConfig move,
                                        in ArmConfig cfg)
        {
            Fix top = TopTipSpeed(in s, in move, in cfg);
            if (top.Raw <= 0) return 0;

            long share = (long)s.TipSpeed.Raw * 1000 / top.Raw;
            if (share <= cfg.MinSwingPermille) return 0;
            return share > 1000 ? 1000 : (int)share;
        }

        // ------------------------------------------------------------------ contact

        /// <summary>
        /// Every weapon against every other weapon and body, once for the match.
        ///
        /// Order matters and is the whole block mechanic: clashes are settled first, and
        /// a weapon that has just been caught carries a ClashLock, so it cannot be the
        /// weapon that lands in the same tick. Holding your blade in the way stops his
        /// because the geometry got there first, not because a flag said so.
        /// </summary>
        public static void Resolve(PlayerSimState[] players, in MoveConfig move,
                                   in ArmConfig cfg, in CombatConfig combat,
                                   bool suddenDeath = false)
        {
            if (players == null || players.Length < 2) return;

            for (int i = 0; i < players.Length; i++)
            {
                players[i].WasClash = false;
                players[i].DealtPermille = 0;
            }

            for (int a = 0; a < players.Length; a++)
                for (int b = a + 1; b < players.Length; b++)
                    Clash(ref players[a], ref players[b], in move, in cfg);

            for (int a = 0; a < players.Length; a++)
                for (int b = 0; b < players.Length; b++)
                {
                    if (a == b) continue;
                    Strike(ref players[a], ref players[b], in move, in cfg, in combat,
                           suddenDeath);
                }
        }

        /// <summary>
        /// Blade on blade. Nothing gets through, and the slower arm is thrown aside.
        ///
        /// "Slower" rather than "whoever pressed first", because speed is the only thing
        /// either player controls here and it is the thing they can see. Meeting a
        /// warhammer at the top of its arc with a dagger you have barely moved loses the
        /// dagger's position; meeting it with a committed swing of your own holds.
        /// </summary>
        static void Clash(ref PlayerSimState a, ref PlayerSimState b,
                          in MoveConfig move, in ArmConfig cfg)
        {
            if (a.Dead || b.Dead) return;
            // Two bare fists have no blade between them to catch, so there is nothing
            // here to resolve: fists meet bodies, in Strike, and nowhere else.
            if (BladeLength(in move, in cfg, a.Inventory.WeaponDef.Reach).Raw <= 0 &&
                BladeLength(in move, in cfg, b.Inventory.WeaponDef.Reach).Raw <= 0) return;

            FixVec2 ah = Hand(in a, in move, in cfg), at = Tip(in a, in move, in cfg);
            FixVec2 bh = Hand(in b, in move, in cfg), bt = Tip(in b, in move, in cfg);

            Fix touch = BladeRadius(in a, in move, in cfg) * 2;
            bool touching = FixGeom.SegmentDistanceSq(ah, at, bh, bt) <= touch * touch;

            a.InContact = touching;
            b.InContact = touching;

            if (!touching) return;

            // Somebody has to actually be SWINGING. Two blades resting against each
            // other have no energy to take off anybody, and this is the line that stops
            // the fight freezing: standing close with weapons crossed -- which at any
            // real range is most of the time -- is not a parry and locks nobody.
            if (SwingPermille(in a, in move, in cfg) <= 0 && SwingPermille(in b, in move, in cfg) <= 0)
                return;

            // And the last clash has to have run out. This used to be an EDGE instead --
            // clash only on the tick contact began -- which fixed the freeze and broke
            // the block: a blade held steadily against another never re-triggers, so it
            // stopped the first blow of a fight and none of the rest. A cooldown does
            // both. A guard parries again and again, and the gaps between are what a
            // patient attacker is looking for.
            if (a.ClashLock > 0 || b.ClashLock > 0) return;

            a.WasClash = true;
            b.WasClash = true;

            // BOTH are locked out, and that is the block.
            //
            // The first version locked only the loser, and a test of it found exactly
            // what that means: a defender holding their blade up took the same 61 damage
            // as one standing there with their arms down. Of course they did -- the
            // loser of a clash is the one who was NOT swinging, so the lock landed on the
            // man who was not going to hit anybody anyway, and the attacker swung on
            // through. A blade that is touching another blade is a blade that is not
            // touching a body; locking both is simply saying so.
            a.ClashLock = cfg.ClashLockFrames;
            b.ClashLock = cfg.ClashLockFrames;

            // The blow is NOT additionally "spent" here, and that was measured rather
            // than assumed. Making a clash also cost each fighter their weapon's arc
            // read as the obvious fix -- a parry should cost the attacker something --
            // but at one arc it changed nothing (the attacker was spending that arc
            // anyway) and at two it stopped the fight dead: zero damage in five seconds
            // at every guard angle. The lock above is the whole of it.

            // And the slower arm is thrown aside on top of it, which is the difference
            // between parrying and merely surviving: a committed swing beats a held
            // guard out of the way and gets the NEXT tick, while two committed swings
            // stop each other dead and both players start again.
            Fix diff = a.TipSpeed - b.TipSpeed;
            if (diff.Raw == 0) return;

            bool aWins = diff.Raw > 0;
            Fix shove = Fix.Abs(diff) * Fix.FromMilli(cfg.ClashDeflectPermille);

            if (aWins) Deflect(ref b, in a, shove, in move, in cfg);
            else Deflect(ref a, in b, shove, in move, in cfg);
        }

        /// <summary>Knocks a beaten arm away from the blade that beat it.</summary>
        static void Deflect(ref PlayerSimState loser, in PlayerSimState winner, Fix shove,
                            in MoveConfig move, in ArmConfig cfg)
        {
            // Away from the winner's arm, along whichever perpendicular points away.
            FixVec2 side = winner.Arm.Perpendicular;
            if (FixVec2.Dot(side, loser.Arm) > Fix.Zero) side = -side;

            loser.Arm = (loser.Arm + side * shove).Normalized;
            if (loser.Arm.LengthSq.Raw == 0)
                loser.Arm = new FixVec2(Fix.FromInt(loser.Facing == 0 ? 1 : loser.Facing),
                                        Fix.Zero);

            loser.ClashLock = cfg.ClashLockFrames;
            loser.TipSpeed = Fix.Zero;        // the blow is spent on the parry

            // And the deflection itself must not read as a swing of THEIRS next tick.
            // Without this the arm jumps aside, the jump is measured as tip speed, and
            // being parried became a free attack: every weapon in the game could kill by
            // walking into somebody, because the first contact knocked their own arm
            // hard enough to count.
            loser.TipPrev = loser.Arm * (ArmLength(in loser, in move, in cfg)
                                         + BladeLength(in move, in cfg, loser.Inventory.WeaponDef.Reach));
        }

        /// <summary>Weapon against body. The only place damage is dealt.</summary>
        static void Strike(ref PlayerSimState attacker, ref PlayerSimState victim,
                           in MoveConfig move, in ArmConfig cfg, in CombatConfig combat,
                           bool suddenDeath)
        {
            if (attacker.Dead || victim.Dead) return;
            if (attacker.HitLock > 0 || attacker.ClashLock > 0) return;
            if (attacker.HitStopTimer > 0 || attacker.DodgeTimer > 0) return;
            if (victim.DodgeTimer > 0) return;          // the dodge's invulnerable frames

            int share = SwingPermille(in attacker, in move, in cfg);
            if (share <= 0) return;

            FixVec2 hand = Hand(in attacker, in move, in cfg);
            FixVec2 tip = Tip(in attacker, in move, in cfg);
            BodySegment(in victim, in move, out FixVec2 feet, out FixVec2 head);

            if (FixGeom.SegmentDistanceSq(hand, tip, feet, head) >
                BodyRadius(in victim, in move, in cfg)
                * BodyRadius(in victim, in move, in cfg)) return;

            attacker.DealtPermille = share;

            // One hit per ARC, and the arc is this weapon's own. A flat lockout let a
            // warhammer land six 17-damage blows in two and a half seconds, because the
            // floor was 0.3 s and its swing takes 0.57 -- it was being allowed to hit
            // twice in one sweep. Taking the weapon's own sweep as the floor is the
            // same sentence the whole design is built on, applied here too.
            int lockout = s_weaponFrames(in attacker);
            attacker.HitLock = lockout > cfg.HitLockFrames ? lockout : cfg.HitLockFrames;

            // Kept as a Fix until the very end, and rounded rather than floored.
            // Truncating to an int first and multiplying after loses the whole bonus on
            // small numbers: a half-power punch is 2.0, and 2 x 1.4 floored is 2, so a
            // desperate fighter hit for exactly as much as a comfortable one. Measured
            // and fixed, which is the only reason this is one expression.
            Fix dealt = attacker.Inventory.WeaponDef.Damage * Fix.FromMilli(share);
            dealt = Desperate(in attacker, dealt, suddenDeath, in combat);

            int damage = (dealt + Fix.Half).ToInt();
            if (damage < 1) damage = 1;

            CombatMotor.ApplyContactHit(ref attacker, ref victim, damage, share,
                                        in cfg, in combat);
        }

        /// <summary>
        /// Bare hands get desperate (docs/02), and in sudden death they hurt anyway
        /// (docs/01).
        ///
        /// Carried over from the old Land() unchanged, including the part that is easy
        /// to get wrong: the two rules do NOT multiply, the bigger one wins. Stacked, a
        /// punch from a fighter on their last tenth of health would read as a one-hit
        /// kill arriving out of nowhere.
        ///
        /// It only applies to bare hands, and only while losing, which is what makes the
        /// fighter who dropped their weapon worth fearing without making unarmed a
        /// choice anybody would take on purpose.
        /// </summary>
        static Fix Desperate(in PlayerSimState s, Fix damage, bool suddenDeath,
                             in CombatConfig cfg)
        {
            bool bare = s.Inventory.Weapon == ItemId.None ||
                        s.Inventory.Weapon == ItemId.Fists;
            if (!bare) return damage;

            int boost = 1000;
            if (cfg.DesperationHealthPermille > 0 &&
                s.Health * 1000 <= cfg.MaxHealth * cfg.DesperationHealthPermille)
                boost = cfg.DesperationDamagePermille;

            if (suddenDeath && cfg.SuddenDeathBarePermille > boost)
                boost = cfg.SuddenDeathBarePermille;

            return boost == 1000 ? damage : damage * Fix.FromMilli(boost);
        }
    }
}

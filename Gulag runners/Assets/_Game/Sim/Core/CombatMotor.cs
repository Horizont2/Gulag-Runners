namespace GulagRunners.Sim
{
    /// <summary>
    /// The fight, from docs/02, rebuilt around a pointed arm (docs/30, docs/31).
    ///
    /// Runs in two halves, and the split is the important part. <see cref="Step"/> decides what
    /// ONE player is doing before they move, and hands back the input the movement motor is
    /// allowed to see. <see cref="ArmMotor.Resolve"/> runs ONCE for the whole match, after
    /// every arm has moved, and decides who hit whom. Hits cannot be resolved per player: a hit
    /// is a fact about two bodies, and two players each resolving their own would disagree.
    ///
    /// THERE IS NO WIND-UP AND NO RECOVERY. The phase machine that used to live here --
    /// windup, three active frames, recovery, a three-hit string -- is gone, and docs/30 has
    /// the measurements that killed it: a 49-frame windup on a warhammer, a hitbox alive for
    /// 3.5% of what you committed to, and all twelve weapons unable to reach somebody walking
    /// backwards. What replaced it is in <see cref="ArmMotor"/>: the stick points the arm, the
    /// weapon is a segment, and a blow is the tip MOVING into somebody.
    ///
    /// What stays here is everything that happens once a hit is a fact -- armour, hitstun, the
    /// advantage floor, knockback, recoil, death -- because none of that depended on how the
    /// blow was aimed.
    ///
    /// Blocking by button is gone too. A weapon is stopped by another weapon, which is settled
    /// in ArmMotor before any damage is, so holding yours in the way works because of where it
    /// IS. The one defensive button left is the dodge, and it is switched on.
    ///
    /// Pure and engine-free like the rest of the simulation (docs/06).
    /// </summary>
    public static class CombatMotor
    {
        /// <summary>
        /// One player's own combat tick, before they move. Returns the input the movement motor
        /// should act on: a body in hitstun does not steer.
        /// </summary>
        public static InputFlags Step(ref PlayerSimState s, InputFlags input,
                                      in CombatConfig cfg, in MoveConfig move,
                                      in ArmConfig arm)
        {
            ClearOutputs(ref s);

            // Frozen by a blow: no timers run and the motor is handed nothing to move with.
            // PlayerMotor counts the freeze down, so this never has to care which ran first.
            if (s.HitStopTimer > 0) return InputFlags.None;

            TickTimers(ref s);

            if (s.Dead) return InputFlags.None;

            bool dodgeHeld = input.Has(InputFlags.Dodge);
            bool dodgePressed = dodgeHeld && !s.GuardHeld;
            s.GuardHeld = dodgeHeld;

            if (s.Reeling)
            {
                s.SpeedPermille = 0;
                return InputFlags.None;
            }

            TakeTheFall(ref s, in cfg);

            // The whole defensive button, now that the guard is the weapon's job. Pressed
            // with a direction held it is a dodge; pressed standing still it is nothing,
            // which is honest -- there is no longer a posture to adopt, only somewhere to
            // be. Not in the air: a fighter off the ground has committed.
            bool wantsDodge = move.AllowDodge &&
                              dodgePressed && input.MoveX() != 0 &&
                              s.Mode == MoveMode.Grounded && s.DodgeRecoverTimer <= 0 &&
                              s.StaminaCharges >= move.DodgeStaminaCost;

            s.SpeedPermille = SpeedCap(in s, in move, in arm, in cfg);
            return MaskInput(in s, input, wantsDodge, in cfg);
        }

        /// <summary>
        /// A blow that landed because a weapon touched a body, from <see cref="ArmMotor"/>.
        ///
        /// Everything past this point is the old <see cref="Hit"/> unchanged -- armour, the
        /// hitstun floor that guarantees a landed blow buys the next move, knockback, recoil,
        /// the lift on a finisher. That machinery was measured and correct; only the way a
        /// hit is DECIDED changed.
        ///
        /// The hit stop is the one thing this adds, and it is the fix docs/30 asked for: it
        /// used to be three flat frames for a 4-damage jab and a 21-damage hammer alike, so
        /// nothing in the game felt heavier than anything else. Now it grows with the damage
        /// actually dealt, which is itself the tip speed, so the weight you feel is the
        /// weight you swung.
        /// </summary>
        public static void ApplyContactHit(ref PlayerSimState attacker,
                                           ref PlayerSimState victim,
                                           int damage, int sharePermille,
                                           in ArmConfig arm, in CombatConfig cfg)
        {
            int side = victim.Position.X >= attacker.Position.X ? 1 : -1;

            // A blow at most of the weapon's top speed carries the finisher's knockback and
            // its lift. There is no "third hit of a string" any more to be the heavy one;
            // what makes a blow heavy is how hard it was actually swung.
            bool heavy = sharePermille >= 800;

            Hit(ref attacker, ref victim, damage, side, heavy, in cfg, in arm);

            int frames = cfg.HitStopFrames;
            if (cfg.HitStopPerDamagePermille > 0)
                frames += Scale(damage, cfg.HitStopPerDamagePermille);
            if (cfg.MaxHitStopFrames > 0 && frames > cfg.MaxHitStopFrames)
                frames = cfg.MaxHitStopFrames;

            Freeze(ref attacker, ref victim, frames);
        }

        static void ClearOutputs(ref PlayerSimState s)
        {
            s.DamageTaken = 0;
            s.WasHit = false;
            s.WasParried = false;
            s.JustDied = false;
        }

        static void TickTimers(ref PlayerSimState s)
        {
            // Long enough without being hit and the string is over: the next blow confirms
            // again and buys its owner the turn, as the first one of any string does.
            if (s.StunStackTimer > 0 && --s.StunStackTimer == 0) s.StunStack = 0;
            if (s.RecoilTimer > 0) s.RecoilTimer--;
            if (s.HitstunTimer > 0) s.HitstunTimer--;
            if (s.StaggerTimer > 0) s.StaggerTimer--;
            if (s.DeathTimer > 0) s.DeathTimer--;

            // No combo window. A string used to be three presses inside 20 frames of each
            // other; now a string is however many times you can get the tip moving into
            // somebody before they leave, and the thing that limits it is HitLock, which
            // ArmMotor counts down.
        }



        /// <summary>
        /// How fast the body may travel this tick, in thousandths of the run speed.
        ///
        /// Zero means no cap at all. There are no attack phases left to switch on, so the
        /// cap comes off the one thing that now describes how committed you are: how fast
        /// the weapon tip is going. A fighter walking with the blade down is unslowed; one
        /// mid-swing is heavy on their feet. It is continuous rather than three steps,
        /// which is the point -- the player sees their own speed answer the stick instead
        /// of snapping between states.
        /// </summary>
        static short SpeedCap(in PlayerSimState s, in MoveConfig move, in ArmConfig arm,
                              in CombatConfig cfg)
        {
            int share = ArmMotor.SwingPermille(in s, in move, in arm);
            if (share <= 0) return 0;

            // Full swing is the old active-frame speed; a lazy wave barely costs anything.
            int slowest = cfg.ActiveSpeedPermille;
            int cap = 1000 - (1000 - slowest) * share / 1000;
            if (cap < slowest) cap = slowest;
            return (short)cap;
        }

        static InputFlags MaskInput(in PlayerSimState s, InputFlags input, bool wantsDodge,
                                    in CombatConfig cfg)
        {
            // Only the press that was read as a dodge reaches the motor as one. Nothing
            // else is taken away any more: there is no committed state to protect, and
            // taking the jump off somebody mid-swing would mean taking it off them most
            // of the match, since the arm is always doing something now.
            return wantsDodge ? input : input & ~InputFlags.Dodge;
        }

        /// <summary>
        /// Damage from the landing the motor recorded on the previous tick. docs/02 counts a
        /// drop off a floor among the ways a fight ends, and this is what makes knocking
        /// somebody through a gap in the walkway worth the opening it costs.
        /// </summary>
        static void TakeTheFall(ref PlayerSimState s, in CombatConfig cfg)
        {
            if (s.LandedSpeed <= cfg.FallDamageSpeed) { s.LandedSpeed = Fix.Zero; return; }

            Fix over = s.LandedSpeed - cfg.FallDamageSpeed;
            s.LandedSpeed = Fix.Zero;

            int damage = over.ToMilli() * cfg.FallDamagePerSpeed / 1000;
            if (damage < 1) return;

            Wound(ref s, damage);
            s.WasHit = true;
            s.DamageTaken = (short)damage;
            s.Noise = NoiseLevel.Loud;

            int stun = damage * cfg.HitstunPerDamage;
            s.HitstunTimer = stun > cfg.MaxHitstunFrames ? cfg.MaxHitstunFrames : stun;
        }

        // ---------------------------------------------------------------- hits
        //
        // TryHitbox, Resolve, Land, Parry and Block used to live here: an AABB in front of
        // the attacker, live for three frames, and a guard flag that turned a hit into a
        // block or a parry. All five are gone. A weapon is a segment now and the question
        // of what it touched belongs to ArmMotor, which owns the geometry; a parry is two
        // segments meeting, settled there before any damage is.
        //
        // What is left below is what happens AFTER a hit is a fact, and none of it cared
        // how the blow was aimed.

        static void Freeze(ref PlayerSimState attacker, ref PlayerSimState victim, int frames)
        {
            if (frames <= 0) return;
            if (attacker.HitStopTimer < frames) attacker.HitStopTimer = frames;
            if (victim.HitStopTimer < frames) victim.HitStopTimer = frames;
        }

        static void Hit(ref PlayerSimState attacker, ref PlayerSimState victim, int damage,
                        int side, bool heavy, in CombatConfig cfg, in ArmConfig arm)
        {
            // Armour is the one stat with no downside, so it is the one worth wearing
            // (docs/03) — and there are three pieces of it now, so they SUM and the sum is
            // capped. Three slots multiplying would have made a full kit take 70% off; a
            // plain sum keeps each piece worth exactly what the table says it is, and the
            // cap is one number in the inspector rather than an emergent one.
            int reduction = victim.Inventory.ArmourPermille;
            if (cfg.MaxArmourPermille > 0 && reduction > cfg.MaxArmourPermille)
                reduction = cfg.MaxArmourPermille;

            if (reduction > 0)
            {
                int keep = 1000 - reduction;
                if (keep < 0) keep = 0;
                damage = Scale(damage, keep);
                if (damage < 1) damage = 1;
            }

            Freeze(ref attacker, ref victim, cfg.HitStopFrames);

            Wound(ref victim, damage);
            victim.WasHit = true;
            victim.DamageTaken = (short)damage;
            victim.Noise = NoiseLevel.Loud;

            // Hitstun is the larger of what the damage earns and what the TURN costs.
            //
            // Damage alone left every weapon in the game at a disadvantage after landing:
            // a club stunned for 8 frames while its own active and recovery ran 12, so the
            // man who had just been hit moved first. Measured across the table it ran from
            // -3 to -12, which means the whole fight rewards waiting. Here the floor is
            // whatever the attacker still owes plus HitAdvantageFrames, so connecting
            // always buys the next move -- and a heavy weapon still buys more, because its
            // damage clears the floor on its own.
            int stun = damage * cfg.HitstunPerDamage;

            // The helmet, and BEFORE the floor below on purpose. Taken off afterwards it
            // would eat the attacker's guaranteed turn and put every weapon in the game at
            // a minus on a clean hit, which is the exact bug this floor exists to fix —
            // measured at -3 to -12 frames across the table before it was there. Here the
            // helmet does nothing to a light opening blow and a great deal to the second
            // and third, stacked on top of the combo decay: what it buys is getting OUT of
            // a string, not refusing to be hit.
            int resist = victim.Inventory.StunResistPermille;
            if (cfg.MaxStunResistPermille > 0 && resist > cfg.MaxStunResistPermille)
                resist = cfg.MaxStunResistPermille;
            if (resist > 0) stun = Scale(stun, 1000 - resist);

            if (victim.StunStack == 0)
            {
                // The FIRST hit of a string confirms: it is worth at least what the
                // attacker still owes plus the advantage, so connecting always buys the
                // next move and attacking is never punished.
                int floor = Owed(in attacker, in arm) + cfg.HitAdvantageFrames;
                if (stun < floor) stun = floor;
            }
            else
            {
                // Every one after it is paid for out of damage alone, and worth less each
                // time. Guaranteeing the turn on EVERY hit is an infinite: measured, a
                // bare-handed fighter landed twenty blows in eight seconds while the man
                // with a club landed none at all.
                for (int i = 0; i < victim.StunStack && stun > 0; i++)
                    stun = Scale(stun, cfg.ComboStunDecayPermille);
            }

            if (stun > cfg.MaxHitstunFrames) stun = cfg.MaxHitstunFrames;
            if (stun < 1) stun = 1;
            victim.HitstunTimer = stun;

            int stacked = victim.StunStack;
            if (victim.StunStack < 250) victim.StunStack++;
            victim.StunStackTimer = cfg.ComboStackFrames;

            // A hit takes the arm with it: whatever they were swinging is spent, and
            // they cannot convert off it until the stun is over.
            //
            // This line said `victim.HitLock = 0`, which is the exact opposite of the
            // sentence above it: HitLock is the cooldown that stops a weapon landing
            // twice in one arc, so zeroing it CLEARS the spend and lets the victim hit
            // back on the next tick that touches. With two blades crossed at fist range
            // that is a feedback loop -- each hit arms the other -- and it ran away:
            //
            //   bare fists, two fighters both swinging     11.8 hits a second
            //   hit stop at 4 frames a hit, on BOTH of them     72% of the match frozen
            //
            // Seventy-two percent of a fist fight was a game that did not answer the
            // stick, which is most of "it feels very inconvenient". Spending the swing
            // the way the comment always said brings fists to 2.9 hits a second and 19%,
            // and the whole weapon table into a 13-19% band instead of 13-72%.
            victim.HitLock = ArmMotor.ArcLock(in victim, in arm);
            victim.TipSpeed = Fix.Zero;
            victim.SwingShare = 0;

            // And the arc it had built is gone with it, so converting off being hit
            // means starting a new swing from nothing rather than finishing the one
            // that was already half way round.
            victim.SpentOn = victim.SwingId;
            victim.Swept = Fix.Zero;

            Fix push = heavy ? cfg.HeavyKnockbackSpeed : cfg.KnockbackSpeed;

            // Each further hit of a string pushes harder, so the attacker runs out of reach
            // and has to step back in. Stepping in is what costs him the frames that hand
            // the turn over -- the second way a string ends, and the one the player can see.
            if (stacked > 0 && cfg.ComboPushPermille > 0)
            {
                int grown = 1000 + cfg.ComboPushPermille * stacked;
                push = new Fix((int)(((long)push.Raw * grown) / 1000));
            }

            victim.Velocity.X = push * side;

            // And the attacker off the blow. Against a wall the victim cannot go anywhere,
            // so without this the string never ends there: measured in the corner, fists
            // looped a spearman twenty hits to none, where the same pair on open floor read
            // one to two the other way.
            if (cfg.AttackerRecoilPermille > 0 && cfg.RecoilFrames > 0)
            {
                Fix back = new Fix((int)(((long)push.Raw * cfg.AttackerRecoilPermille) / 1000));
                attacker.Velocity.X = Fix.Zero - back * side;
                attacker.RecoilTimer = cfg.RecoilFrames;
            }

            // Only the finisher lifts. A light blow leaves them standing, which is the one
            // change that makes a second hit possible: lifted, they slid away with no
            // footing for 21 frames over 3 centimetres of height nobody could see.
            if (heavy && victim.Mode == MoveMode.Grounded && cfg.KnockbackLift > Fix.Zero)
            {
                victim.Velocity.Y = cfg.KnockbackLift;
                victim.Mode = MoveMode.Airborne;
            }

            // Knocked off a ladder, like anything else that breaks concentration.
            if (victim.Mode == MoveMode.Climbing || victim.Mode == MoveMode.Mounting ||
                victim.Mode == MoveMode.Mantling)
            {
                // A CLIMB-OUT CARRIES THE BODY BETWEEN DEPTH LANES, and breaking it off
                // half way leaves it stranded between them -- which is what being thrown
                // behind the level is. Every ladder in this arena stands in front of the
                // floor it serves, so the move slides the body back onto that floor's lane
                // as it rises; interrupt it and the depth is some value that is on no lane
                // at all. Nothing then corrects it: UpdateDepth gives up outright while
                // airborne, on purpose, because a body in mid-air keeps the slice it left.
                //
                // So the move's promise is kept even though the move is not: the landing
                // lane is one a body can stand on by construction, and it is the one this
                // body was already travelling towards.
                if (victim.Mode == MoveMode.Mantling) victim.Depth = victim.ScriptToDepth;

                victim.Mode = MoveMode.Airborne;
                victim.LadderIndex = -1;
                victim.ScriptTimer = 0;
                victim.ScriptFrames = 0;
            }
        }

        static void Wound(ref PlayerSimState victim, int damage)
        {
            victim.Health -= (short)damage;
            if (victim.Health > 0) return;

            victim.Health = 0;
            victim.Dead = true;
            victim.JustDied = true;
            victim.TipSpeed = Fix.Zero;
            victim.SwingShare = 0;
            victim.Noise = NoiseLevel.Loud;
        }

        /// <summary>
        /// Frames the attacker still owes after the blow lands.
        ///
        /// This is the floor under the victim's hitstun, and it is the reason landing a
        /// blow has always bought the next move: stun the victim for at least as long as
        /// the attacker is still busy, plus the advantage. That argument survives the
        /// rewrite unchanged -- only the thing the attacker is busy WITH changed. It used
        /// to be the rest of the active window plus a recovery computed from the weapon's
        /// windup. Now it is HitLock, the ticks before this weapon may land again, which
        /// is the same quantity by a simpler route: the exact time the attacker cannot
        /// convert.
        /// </summary>
        static int Owed(in PlayerSimState s, in ArmConfig arm) => arm.HitLockFrames;


        static int Scale(int value, int permille) => (int)(((long)value * permille) / 1000);
    }
}

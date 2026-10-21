namespace GulagRunners.Sim
{
    /// <summary>
    /// The fight, from docs/02.
    ///
    /// Runs in two halves, and the split is the important part. <see cref="Step"/> decides what
    /// ONE player is doing — swing, guard, reel — before they move, and hands back the input the
    /// movement motor is allowed to see. <see cref="Resolve"/> runs ONCE for the whole match,
    /// after that, and decides who hit whom. Hits cannot be resolved per player: a hit is a fact
    /// about two bodies, and two players each resolving their own would disagree about it.
    ///
    /// The shape of an exchange:
    ///
    ///   wind-up    committed, nothing has happened. This is the window the opponent READS, and
    ///              it is longer for a spear than for a club, which is the whole reason the two
    ///              are different weapons rather than different numbers.
    ///   active     three frames. Short on purpose: a long active window hits round corners and
    ///              makes reach meaningless.
    ///   recovery   what a miss costs. Press again inside the combo window and it chains, up to
    ///              three — light, light, heavy — and no further, because longer strings on a
    ///              phone are a dexterity tax rather than depth.
    ///
    /// Blocking is the medieval mode's answer to pressure, and parrying is the one window in the
    /// game that rewards reading the other player rather than reacting: a hit taken in the first
    /// seven frames of a raised guard does nothing at all and staggers whoever threw it.
    ///
    /// Pure and engine-free like the rest of the simulation (docs/06).
    /// </summary>
    public static class CombatMotor
    {
        /// <summary>
        /// One player's own combat tick, before they move. Returns the input the movement motor
        /// should act on: a body in hitstun does not steer, and a body mid-swing is committed.
        /// </summary>
        public static InputFlags Step(ref PlayerSimState s, InputFlags input,
                                      in CombatConfig cfg, in MoveConfig move)
        {
            ClearOutputs(ref s);

            // Frozen by a blow: no timers run, no swing starts, no guard goes up, and the
            // motor is handed nothing to move with. PlayerMotor counts the freeze down, so
            // this never has to care which of the two ran first.
            if (s.HitStopTimer > 0) return InputFlags.None;

            // Remembered BEFORE anything can refuse the press, so a tap during recovery
            // still counts. Drained in TickTimers.
            if (input.Has(InputFlags.Attack) && cfg.AttackBufferFrames > 0)
                s.AttackBuffer = cfg.AttackBufferFrames;

            TickTimers(ref s);

            if (s.Dead) return InputFlags.None;

            bool guardHeld = input.Has(InputFlags.Dodge);
            bool guardPressed = guardHeld && !s.GuardHeld;
            s.GuardHeld = guardHeld;

            if (s.Reeling)
            {
                s.Blocking = false;
                s.SpeedPermille = 0;
                return InputFlags.None;
            }

            AdvanceSwing(ref s, cfg);
            TakeTheFall(ref s, in cfg);

            // docs/02 gives the medieval fighter ONE defensive button, and it has to be both
            // things it is for. Pressing it with a direction held is a dodge — you are already
            // going somewhere, so go there properly. Pressing it standing still puts the guard
            // up on the same frame, which is what makes the parry a tap rather than a bet on a
            // delay. Neither happens in the air: a fighter off the ground has committed.
            bool wantsDodge = move.AllowDodge &&
                              guardPressed && input.MoveX() != 0 && !s.Swinging &&
                              s.Mode == MoveMode.Grounded && s.DodgeRecoverTimer <= 0;

            // The guard goes up only while standing, only with stamina, and never mid-swing:
            // a block that cancels a committed attack removes every reason not to attack.
            bool canGuard = guardHeld && !wantsDodge && s.GuardBreakTimer <= 0 &&
                            s.StaminaCharges > 0 &&
                            s.Mode == MoveMode.Grounded && !s.Swinging;

            if (canGuard && !s.Blocking) s.GuardTimer = 0;
            else if (canGuard) s.GuardTimer++;
            s.Blocking = canGuard;

            // Holding the guard is an action, not a posture. Standing braced is slower (42%
            // of run speed) and that reads as the character mysteriously slowing down until
            // it also visibly runs out — so it runs out.
            if (s.Blocking && cfg.GuardDrainFrames > 0 && s.GuardTimer > 0 &&
                s.GuardTimer % cfg.GuardDrainFrames == 0)
            {
                s.StaminaCharges--;
                if (s.StaminaCharges <= 0)
                {
                    s.StaminaCharges = 0;
                    s.Blocking = false;         // the arm drops; no stun, it was not a hit
                    s.Noise = NoiseLevel.Quiet;
                }
            }

            if (!s.Blocking && !s.Swinging) TryStartSwing(ref s, input, in cfg);

            s.SpeedPermille = SpeedCap(in s, in cfg);
            return MaskInput(in s, input, wantsDodge, in cfg);
        }

        static void ClearOutputs(ref PlayerSimState s)
        {
            s.DamageTaken = 0;
            s.WasHit = false;
            s.WasBlocked = false;
            s.WasParried = false;
            s.SwingStarted = false;
            s.JustDied = false;
        }

        static void TickTimers(ref PlayerSimState s)
        {
            if (s.AttackBuffer > 0) s.AttackBuffer--;

            // Long enough without being hit and the string is over: the next blow confirms
            // again and buys its owner the turn, as the first one of any string does.
            if (s.StunStackTimer > 0 && --s.StunStackTimer == 0) s.StunStack = 0;
            if (s.HitstunTimer > 0) s.HitstunTimer--;
            if (s.StaggerTimer > 0) s.StaggerTimer--;
            if (s.GuardBreakTimer > 0) s.GuardBreakTimer--;
            if (s.DeathTimer > 0) s.DeathTimer--;

            // The combo window is how long you have AFTER a swing to continue the string, so it
            // only runs while there is no swing. Letting it run during one made it expire part
            // way through the next, which quietly reset the count and meant the heavy third hit
            // never happened at all.
            if (!s.Swinging)
            {
                if (s.ComboTimer > 0) s.ComboTimer--;
                if (s.ComboTimer == 0) s.ComboIndex = 0;
            }
        }

        static void TryStartSwing(ref PlayerSimState s, InputFlags input, in CombatConfig cfg)
        {
            // Either pressed now, or pressed recently enough to still mean it.
            if (!input.Has(InputFlags.Attack) && s.AttackBuffer <= 0) return;
            s.AttackBuffer = 0;

            ItemDef weapon = s.Inventory.WeaponDef;
            bool heavy = s.ComboIndex >= cfg.MaxCombo - 1;

            int windup = Windup(in weapon, in cfg);
            if (heavy) windup = Scale(windup, cfg.HeavyTimingPermille);

            s.Attack = AttackPhase.Windup;
            s.AttackTimer = windup > 0 ? windup : 1;
            s.AttackPhaseFrames = s.AttackTimer;
            s.SwingSpent = false;
            s.SwingConnected = false;
            s.SwingStarted = true;
            s.Blocking = false;

            // Winding up is quiet; it is the hit that tells the floor where you are.
            s.Noise = NoiseLevel.Quiet;
        }

        static void AdvanceSwing(ref PlayerSimState s, in CombatConfig cfg)
        {
            if (s.Attack == AttackPhase.None) return;

            s.AttackTimer--;
            if (s.AttackTimer > 0) return;

            switch (s.Attack)
            {
                case AttackPhase.Windup:
                    s.Attack = AttackPhase.Active;
                    s.AttackTimer = cfg.ActiveFrames > 0 ? cfg.ActiveFrames : 1;
                    s.AttackPhaseFrames = s.AttackTimer;
                    break;

                case AttackPhase.Active:
                {
                    ItemDef weapon = s.Inventory.WeaponDef;
                    bool heavy = s.ComboIndex >= cfg.MaxCombo - 1;
                    int recovery = Scale(Windup(in weapon, in cfg), cfg.RecoveryPermille);
                    if (heavy) recovery = Scale(recovery, cfg.HeavyTimingPermille);

                    // Landed, so half of it is given back. A blow that connects has already
                    // told the other player to stop; a blow that whiffed is the one you are
                    // supposed to be punished for.
                    if (s.SwingConnected && cfg.HitRecoveryCutPermille > 0)
                    {
                        recovery = Scale(recovery, cfg.HitRecoveryCutPermille);
                        if (recovery < 1) recovery = 1;
                    }

                    s.Attack = AttackPhase.Recovery;
                    s.AttackTimer = recovery > 0 ? recovery : 1;
                    s.AttackPhaseFrames = s.AttackTimer;
                    break;
                }

                default:
                    // The swing is over. A string that reached its last hit is finished; one that
                    // did not leaves the window open for the next press.
                    s.Attack = AttackPhase.None;
                    s.AttackTimer = 0;
                    s.AttackPhaseFrames = 0;

                    if (s.ComboIndex + 1 < cfg.MaxCombo)
                    {
                        s.ComboIndex++;
                        s.ComboTimer = cfg.ComboWindowFrames;
                    }
                    else
                    {
                        s.ComboIndex = 0;
                        s.ComboTimer = 0;
                    }
                    break;
            }
        }

        /// <summary>
        /// What the movement motor is allowed to see. Committing to a swing means committing to
        /// where you are standing, which is what makes reach and spacing matter at all.
        /// </summary>
        /// <summary>
        /// How fast the body may travel this tick, in thousandths of the run speed.
        ///
        /// Zero means no cap at all, which is what the motor reads as "unrestricted" — so
        /// every branch here either names a number or names zero deliberately.
        /// </summary>
        static short SpeedCap(in PlayerSimState s, in CombatConfig cfg)
        {
            if (s.Blocking) return (short)cfg.BlockSpeedPermille;

            switch (s.Attack)
            {
                case AttackPhase.Windup:   return (short)cfg.WindupSpeedPermille;
                case AttackPhase.Active:   return (short)cfg.ActiveSpeedPermille;
                case AttackPhase.Recovery: return (short)cfg.RecoverySpeedPermille;
                default:                   return 0;
            }
        }

        static InputFlags MaskInput(in PlayerSimState s, InputFlags input, bool wantsDodge,
                                    in CombatConfig cfg)
        {
            // A swing keeps the COMMITMENT and gives back the WALKING. Jump and dodge still
            // go: a swing you can cancel by rolling out of it is a swing with no risk in it,
            // and risk is the only reason to respect one. Left and right stay, slowed by
            // phase through SpeedPermille — a fighter rooted to the spot for every attack
            // turns every exchange into two people taking turns standing still.
            if (s.Swinging)
                return input & ~(InputFlags.Jump | InputFlags.Dodge);

            if (s.Blocking)
                return input & ~(InputFlags.Jump | InputFlags.Dodge);

            // Only the press that was read as a dodge reaches the motor as one. Without this
            // the button did both jobs at once: on the ground the guard swallowed it and
            // nothing rolled, in the air there was no guard to swallow it and it rolled — the
            // exact opposite of both.
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

        /// <summary>Walking speed while the guard is up, as a fraction of the run speed.</summary>
        public static Fix GuardSpeed(in CombatConfig cfg, in MoveConfig move) =>
            new Fix((int)(((long)move.RunSpeed.Raw * cfg.BlockSpeedPermille) / 1000));

        // ---------------------------------------------------------------- hits

        /// <summary>
        /// The live hitbox of a swing, or false when there is nothing to hit with this tick.
        /// Reach is measured from the front of the body, so a long weapon is long in the one way
        /// a player can see.
        /// </summary>
        public static bool TryHitbox(in PlayerSimState s, in MoveConfig move, out Aabb box)
        {
            box = default;
            if (s.Attack != AttackPhase.Active || s.SwingSpent || s.Dead) return false;

            ItemDef weapon = s.Inventory.WeaponDef;
            Fix half = move.BodyWidth / 2;
            Fix height = s.Crouching ? move.CrouchHeight : move.BodyHeight;

            // The OUTER edge is a body's width plus the weapon's reach, so reach is still
            // measured from the front of the body and a long weapon is long in the one way a
            // player can see. The inner edge is the attacker's own centre.
            //
            // It used to be the front face, and that is a miss at point-blank range. Two
            // fighters do not collide with each other — only with the level — so they can
            // and do stand on the same spot, and then the victim's whole box lies behind a
            // hitbox that starts at the attacker's front. Nothing lands, from either of
            // them, for as long as they are pressed together: "standing right up against
            // each other, no damage goes through". Starting at the centre cannot reach
            // behind the attacker and cannot miss something they are standing inside.
            Fix near = s.Position.X;
            Fix far = s.Facing >= 0 ? s.Position.X + half + weapon.Reach
                                    : s.Position.X - half - weapon.Reach;

            Fix front = s.Facing >= 0 ? near : far;
            Fix back = s.Facing >= 0 ? far : near;

            // Chest high, not full height: a swing that connects with an ankle is a swing nobody
            // can read.
            Fix low = s.Position.Y + height / 4;
            Fix high = s.Position.Y + height;

            box = new Aabb(front, low, back, high);
            return true;
        }

        /// <summary>
        /// Who hit whom, once for the whole match. Order is fixed by the array, so two devices
        /// stepping the same states reach the same answer.
        /// </summary>
        public static void Resolve(PlayerSimState[] players, in CombatConfig cfg,
                                   in MoveConfig move, bool suddenDeath = false)
        {
            if (players == null || players.Length < 2) return;

            for (int a = 0; a < players.Length; a++)
            {
                if (!TryHitbox(in players[a], in move, out Aabb box)) continue;

                for (int b = 0; b < players.Length; b++)
                {
                    if (a == b) continue;
                    if (players[b].Dead || players[b].Invulnerable) continue;

                    Aabb body = players[b].Body(in move);
                    if (!box.Overlaps(in body)) continue;

                    // Not through the level. Two fighters on different slices of a building —
                    // one on the gallery's lane, one out on the ramp — are not within reach of
                    // each other however their flattened boxes overlap.
                    if (move.BodyDepth > Fix.Zero &&
                        Fix.Abs(players[a].Depth - players[b].Depth) > move.BodyDepth)
                        continue;

                    players[a].SwingSpent = true;
                    players[a].SwingConnected = true;
                    Land(ref players[a], ref players[b], in cfg, in move, suddenDeath);
                    break;                       // one swing, one victim
                }
            }
        }

        static void Land(ref PlayerSimState attacker, ref PlayerSimState victim,
                         in CombatConfig cfg, in MoveConfig move, bool suddenDeath)
        {
            int side = victim.Position.X >= attacker.Position.X ? 1 : -1;
            bool heavy = attacker.ComboIndex >= cfg.MaxCombo - 1;

            ItemDef weapon = attacker.Inventory.WeaponDef;
            int damage = weapon.Damage.ToMilli() / 1000;
            if (damage < 1) damage = 1;
            if (heavy) damage = Scale(damage, cfg.HeavyDamagePermille);

            // Bare hands get desperate. Only bare hands, and only while losing: it makes the
            // fighter who dropped their weapon worth fearing without making unarmed a choice.
            //
            // In sudden death they hurt anyway (docs/01), because the whole point of that last
            // minute is that there is nowhere to go and no time to look for a weapon. The two
            // rules do not multiply — the bigger one wins. Stacked, a punch from a fighter on
            // their last tenth of health would read as a one-hit kill arriving out of nowhere.
            bool bare = attacker.Inventory.Weapon == ItemId.None ||
                        attacker.Inventory.Weapon == ItemId.Fists;
            if (bare)
            {
                int boost = 1000;
                if (cfg.DesperationHealthPermille > 0 &&
                    attacker.Health * 1000 <= cfg.MaxHealth * cfg.DesperationHealthPermille)
                    boost = cfg.DesperationDamagePermille;

                if (suddenDeath && cfg.SuddenDeathBarePermille > boost)
                    boost = cfg.SuddenDeathBarePermille;

                if (boost != 1000) damage = Scale(damage, boost);
            }

            // A guard only works towards the blow. Being hit in the back while holding a shield
            // up is being hit in the back.
            bool facingIt = victim.Facing != side;

            if (victim.Blocking && facingIt)
            {
                // A shield is what makes the parry worth hunting for (docs/02 calls it the
                // skill ceiling of the medieval mode).
                int window = cfg.ParryFrames + victim.Inventory.ArmourDef.ParryBonusFrames;
                if (victim.GuardTimer < window)
                {
                    Parry(ref attacker, ref victim, in cfg);
                    return;
                }

                Block(ref attacker, ref victim, damage, side, in cfg);
                return;
            }

            Hit(ref attacker, ref victim, damage, side, heavy, in cfg);
        }

        static void Parry(ref PlayerSimState attacker, ref PlayerSimState victim,
                          in CombatConfig cfg)
        {
            Freeze(ref attacker, ref victim, cfg.ParryStopFrames);

            victim.WasParried = true;
            victim.Noise = NoiseLevel.Medium;

            attacker.StaggerTimer = cfg.StaggerFrames;
            attacker.SwingConnected = false;
            attacker.Attack = AttackPhase.None;
            attacker.AttackTimer = 0;
            attacker.ComboIndex = 0;
            attacker.ComboTimer = 0;
            attacker.Velocity.X = Fix.Zero;
        }

        static void Block(ref PlayerSimState attacker, ref PlayerSimState victim, int damage,
                          int side, in CombatConfig cfg)
        {
            // Some weapons are the answer to a shield rather than a thing a shield answers
            // (docs/03: every item has a counter-item). A flail goes most of the way through.
            int pierce = attacker.Inventory.WeaponDef.BlockPierce;
            int through = pierce > cfg.ChipPermille ? pierce : cfg.ChipPermille;

            int chip = Scale(damage, through);

            // And what a shield is FOR: the hits you meet, rather than the hits you stand and
            // take. Zero is "the ordinary chip", which is what every other armour wants and
            // what an unset field already is.
            int guard = victim.Inventory.ArmourDef.BlockChipPermille;
            if (guard > 0) chip = Scale(chip, guard);

            if (chip < 1) chip = 1;

            Freeze(ref attacker, ref victim, cfg.BlockStopFrames);

            Wound(ref victim, chip);
            victim.DamageTaken = (short)chip;        // what got through, for the view to size
            victim.WasBlocked = true;
            victim.Noise = NoiseLevel.Medium;
            victim.Velocity.X = cfg.KnockbackSpeed / 2 * side;

            victim.StaminaCharges -= cfg.BlockStaminaCost;
            if (victim.StaminaCharges > 0) return;

            // Out of stamina: the guard breaks and they are wide open. This is what stops a
            // player simply holding block forever.
            victim.StaminaCharges = 0;
            victim.Blocking = false;
            victim.GuardBreakTimer = cfg.GuardBreakFrames;
            victim.HitstunTimer = cfg.GuardBreakFrames;
            victim.Noise = NoiseLevel.Loud;
        }

        /// <summary>
        /// Stops both of them dead for a few frames.
        ///
        /// Both, and not only the one who was hit: half the weight of a blow is the arm
        /// coming to a stop against something. Freezing the victim alone reads as the victim
        /// lagging, which is the opposite of the intended feeling.
        ///
        /// The longer freeze wins rather than accumulating, so a pair of hits landing on the
        /// same tick cannot stack into a visible hitch.
        /// </summary>
        static void Freeze(ref PlayerSimState attacker, ref PlayerSimState victim, int frames)
        {
            if (frames <= 0) return;
            if (attacker.HitStopTimer < frames) attacker.HitStopTimer = frames;
            if (victim.HitStopTimer < frames) victim.HitStopTimer = frames;
        }

        static void Hit(ref PlayerSimState attacker, ref PlayerSimState victim, int damage,
                        int side, bool heavy, in CombatConfig cfg)
        {
            // Armour is the one stat with no downside, so it is the one worth wearing (docs/03).
            Fix reduction = victim.Inventory.ArmourDef.DamageReduction;
            if (reduction > Fix.Zero)
            {
                int keep = 1000 - reduction.ToMilli();
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

            if (victim.StunStack == 0)
            {
                // The FIRST hit of a string confirms: it is worth at least what the
                // attacker still owes plus the advantage, so connecting always buys the
                // next move and attacking is never punished.
                int floor = Owed(in attacker, in cfg) + cfg.HitAdvantageFrames;
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

            victim.Blocking = false;
            victim.Attack = AttackPhase.None;
            victim.AttackTimer = 0;
            victim.ComboIndex = 0;
            victim.ComboTimer = 0;

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
                victim.Mode = MoveMode.Airborne;
                victim.LadderIndex = -1;
            }
        }

        static void Wound(ref PlayerSimState victim, int damage)
        {
            victim.Health -= (short)damage;
            if (victim.Health > 0) return;

            victim.Health = 0;
            victim.Dead = true;
            victim.JustDied = true;
            victim.Blocking = false;
            victim.Attack = AttackPhase.None;
            victim.Noise = NoiseLevel.Loud;
        }

        /// <summary>
        /// Frames the attacker still owes after the blow lands: the rest of the active
        /// window plus the recovery it is about to roll into, with the connected cut
        /// already applied. Read at the moment of the hit, before AdvanceSwing gets there,
        /// which is why it recomputes the recovery rather than reading AttackTimer.
        /// </summary>
        static int Owed(in PlayerSimState s, in CombatConfig cfg)
        {
            if (s.Attack != AttackPhase.Active) return 0;

            bool heavy = s.ComboIndex >= cfg.MaxCombo - 1;
            ItemDef w = s.Inventory.WeaponDef;
            int recovery = Scale(Windup(in w, in cfg), cfg.RecoveryPermille);
            if (heavy) recovery = Scale(recovery, cfg.HeavyTimingPermille);
            if (cfg.HitRecoveryCutPermille > 0)
            {
                recovery = Scale(recovery, cfg.HitRecoveryCutPermille);
                if (recovery < 1) recovery = 1;
            }

            int left = s.AttackTimer - 1;
            if (left < 0) left = 0;
            return left + recovery;
        }

        /// <summary>The weapon's wind-up, after the one knob that paces the whole fight.</summary>
        static int Windup(in ItemDef weapon, in CombatConfig cfg)
        {
            int frames = cfg.AttackFramesPermille > 0
                ? Scale(weapon.AttackFrames, cfg.AttackFramesPermille)
                : weapon.AttackFrames;
            return frames > 0 ? frames : 1;
        }

        static int Scale(int value, int permille) => (int)(((long)value * permille) / 1000);
    }
}

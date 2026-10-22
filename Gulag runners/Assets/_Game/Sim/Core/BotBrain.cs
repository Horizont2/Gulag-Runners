namespace GulagRunners.Sim
{
    /// <summary>What the bot is carrying between ticks. One struct, no allocation.</summary>
    public struct BotState
    {
        /// <summary>Frames until it is allowed to change its mind.</summary>
        public int Think;

        /// <summary>What it decided last time, replayed until then.</summary>
        public InputFlags Held;

        /// <summary>Its own stream of chance, so two bots never roll the same numbers.</summary>
        public Rng Rng;

        /// <summary>The ladder it is walking to, or -1. Hunter only.</summary>
        public int Ladder;

        /// <summary>Where it is pointing the weapon, in thousandths. See BotBrain.Aim.</summary>
        public short AimX;
        public short AimY;

        /// <summary>
        /// Ticks left on the current end of a swing. The bot attacks by throwing its arm
        /// from one end of an arc to the other, and it has to HOLD each end long enough
        /// for the arm to actually travel -- flipping every tick would just vibrate the
        /// blade on the spot and never build any tip speed, which is now the only thing
        /// that does damage.
        /// </summary>
        public int SwingHold;

        /// <summary>Which end of the arc it is heading for.</summary>
        public bool SwingHigh;

        /// <summary>
        /// Ticks left holding the blade level at the other fighter.
        ///
        /// A guard it has to DECIDE to hold, because the first version simply guarded
        /// whenever it was not swinging -- a perfect, free, permanent block that nothing
        /// could get through. Measured: a spearman walking straight into it and swinging
        /// for twenty seconds landed nothing at all. The carry pose had the same bug for
        /// the same reason, and it is the same fix: a guard costs you the swing you are
        /// not making, so it has to be asked for.
        /// </summary>
        public int GuardHold;

        public static BotState Seeded(uint seed)
        {
            BotState b = default;
            b.Rng = new Rng(seed);
            b.Ladder = -1;
            return b;
        }
    }

    /// <summary>
    /// A practice opponent: one tick of state in, one tick of intent out.
    ///
    /// It is a pure function on purpose, and it lives in the simulation assembly with no
    /// reference to the engine, for the same reason the motor does. The decision is then
    /// something the Python mirror can run for ten thousand ticks and measure, and the fault
    /// worth measuring is not "is it good at the game" — it is whether it TWITCHES. A bot
    /// that re-decides at 60 Hz does not read as fast, it reads as broken: wherever two
    /// choices are close it stutters on the spot, and no amount of tuning the choices fixes
    /// it, because the fault is the rate.
    ///
    /// So it thinks on a clock. Between thoughts it replays what it last decided, which is
    /// also where its reaction time comes from, and that is the thing that makes an opponent
    /// beatable rather than merely present.
    ///
    /// It produces INPUT BITS and nothing else. It cannot teleport, cannot reach further than
    /// its weapon, and takes damage from the same hitboxes — because it is playing the same
    /// game through the same door as the player, and anything else is a bot that proves
    /// nothing about whether the game works.
    /// </summary>
    public static class BotBrain
    {
        public static TickInput Decide(ref BotState bot, in PlayerSimState self,
                                       in PlayerSimState foe, SimWorld world,
                                       in BotConfig cfg, in MoveConfig move,
                                       in ArmConfig arm)
        {
            // The arm is re-aimed EVERY tick, outside the reaction gate below. A bot that
            // only moved its weapon once every fourteen frames would not be a slow bot, it
            // would be a bot that cannot swing at all: tip speed is damage now, and an arm
            // that jumps and then sits still for thirteen ticks has an average speed of
            // nearly nothing. What the reaction time still gates is DECISIONS -- where to
            // stand, whether to commit -- which is what it was always for.
            Aim(ref bot, in self, in foe, in cfg);
            // Dead, reeling, or being carried along by a scripted move: nothing to decide,
            // and pressing anything during a mount or a climb-out is pressing into a wall.
            if (self.Dead || self.Reeling ||
                self.Mode == MoveMode.Mounting || self.Mode == MoveMode.Mantling)
            {
                bot.Held = InputFlags.None;
                bot.Think = 0;
                bot.SwingHold = 0;
                return new TickInput(InputFlags.None, bot.AimX, bot.AimY);
            }

            if (bot.Think > 0)
            {
                bot.Think--;
                return new TickInput(bot.Held, bot.AimX, bot.AimY);
            }

            bot.Think = cfg.ReactionFrames +
                        (cfg.ReactionJitter > 0 ? bot.Rng.Below(cfg.ReactionJitter) : 0);
            bot.Held = Think(ref bot, in self, in foe, world, in cfg, in move, in arm);
            return new TickInput(bot.Held, bot.AimX, bot.AimY);
        }

        static InputFlags Think(ref BotState bot, in PlayerSimState self,
                                in PlayerSimState foe, SimWorld world,
                                in BotConfig cfg, in MoveConfig move,
                                in ArmConfig arm)
        {
            if (foe.Dead) return InputFlags.None;

            Fix gap = foe.Position.X - self.Position.X;
            Fix flat = Fix.Abs(gap);
            int towards = gap >= Fix.Zero ? 1 : -1;

            // On a ladder it has exactly one decision: which way along it. Everything else
            // here is about floors, and a body on rungs is between two of them.
            if (self.Mode == MoveMode.Climbing)
                return Climb(in self, in foe, world);

            Fix rise = foe.Position.Y - self.Position.Y;

            // Not on the same floor. Walking at someone a storey up is walking into a wall
            // for the rest of the round, which is the single most obviously broken thing a
            // bot can do, so it is the first thing checked.
            if (cfg.Skill == BotSkill.Hunter && Fix.Abs(rise) > cfg.FloorGap)
            {
                InputFlags toLadder = SeekLadder(ref bot, in self, rise, world, in cfg);
                if (toLadder != InputFlags.None) return toLadder;
            }

            // A dummy holds its weapon up and does nothing else, which is now a useful
            // thing to practise against rather than a post: walking into a held blade
            // still does nothing, so the player has to actually swing past it.
            if (cfg.Skill == BotSkill.Dummy) return InputFlags.None;

            // Where it wants to stand, measured off its OWN weapon: a spear bot that stands
            // at a club's distance has thrown away the only thing a spear is for.
            //
            // Off the arm and the blade rather than off the item's Reach, because those
            // are the same thing only by accident. Reach is a number in a table; the
            // distance the weapon actually covers is the arm plus the steel past the hand,
            // and once the blade became the one that is drawn the two parted company by
            // 0.37 m on a spear -- which is a spear bot standing where its spear does not
            // go. Asking the geometry leaves nothing to keep in step.
            Fix reach = ArmMotor.ArmLength(in self, in move, in arm)
                        + ArmMotor.BladeLength(in move, in arm,
                                               self.Inventory.WeaponDef.Reach);
            Fix want = Scale(reach, cfg.RangePermille);
            Fix tooClose = Scale(reach, cfg.TooClosePermille);

            InputFlags flags = InputFlags.None;

            if (flat > want)
            {
                // Too far: close, and do not swing at the air on the way in.
                flags |= towards > 0 ? InputFlags.Right : InputFlags.Left;
                if (Roll(ref bot, cfg.GuardPermille / 2)) flags |= InputFlags.Dodge;
                return flags;
            }

            if (flat < tooClose)
            {
                // Too close: give ground. And keep the blade on them while doing it, which
                // is the aim/travel split turned against the player — something that backs
                // off with its guard up has to be chased rather than walked after.
                flags |= towards > 0 ? InputFlags.Left : InputFlags.Right;

                // Backing off with the blade still on them, which is the thing a weapon
                // that is always live makes possible: something retreating behind its own
                // guard has to be gone round, not walked after.
                if (Roll(ref bot, cfg.FightingWithdrawalPermille) &&
                    Roll(ref bot, cfg.AggressionPermille))
                    Swing(ref bot, in self);

                return flags;
            }

            // In range. There is no wind-up to read any more, so what it reads instead is
            // the one thing that replaced it: a weapon already MOVING. That is strictly
            // more honest -- the old tell was a state flag no player could see, and this
            // one is the blade itself.
            if (foe.TipSpeed > Fix.Zero && Roll(ref bot, cfg.GuardPermille * 3))
            {
                // Hold the blade level between them for a moment. Weapons stop weapons,
                // so standing still with the arm out IS the guard -- but only for as
                // long as it chose to, not for ever.
                bot.SwingHold = 0;
                bot.GuardHold = cfg.ReactionFrames * 2;
                return InputFlags.None;
            }

            if (Roll(ref bot, cfg.DodgePermille) && self.StaminaCharges > 1)
            {
                // Pressed WITH a direction it is a roll, which is the only way out of a
                // corner it has.
                flags |= towards > 0 ? InputFlags.Left : InputFlags.Right;
                flags |= InputFlags.Dodge;
                return flags;
            }

            if (Roll(ref bot, cfg.AggressionPermille))
            {
                Swing(ref bot, in self);
                return flags;
            }

            // Nothing worth doing: shuffle to hold the range rather than stand like a post.
            if (flat > Scale(want, 900))
                flags |= towards > 0 ? InputFlags.Right : InputFlags.Left;

            return flags;
        }

        /// <summary>Up or down the rungs, and off at the end that is nearer the target.</summary>
        static InputFlags Climb(in PlayerSimState self, in PlayerSimState foe, SimWorld world)
        {
            Fix rise = foe.Position.Y - self.Position.Y;
            if (rise > Fix.FromMilli(300)) return InputFlags.Up;
            if (rise < Fix.FromMilli(-300)) return InputFlags.Down;

            // Level with them: step off towards them.
            return foe.Position.X >= self.Position.X ? InputFlags.Right : InputFlags.Left;
        }

        /// <summary>
        /// Towards a ladder that goes the right way, and onto it once there.
        ///
        /// Deliberately not pathfinding. This arena is a handful of floors joined by four
        /// ladders, and the honest version of "find a route" at that size is "walk at the
        /// nearest ladder that reaches where they are". A bot that cannot get there gives up
        /// and fights on the floor it has, which is a better failure than one that walks into
        /// a wall forever.
        /// </summary>
        static InputFlags SeekLadder(ref BotState bot, in PlayerSimState self, Fix rise,
                                     SimWorld world, in BotConfig cfg)
        {
            if (world == null || world.Ladders == null) return InputFlags.None;

            bool up = rise > Fix.Zero;
            int best = -1;
            Fix nearest = Fix.Zero;

            for (int i = 0; i < world.Ladders.Length; i++)
            {
                Aabb l = world.Ladders[i];

                // It has to span the way we are going, from about where we stand.
                if (up && l.MaxY <= self.Position.Y) continue;
                if (!up && l.MinY >= self.Position.Y) continue;

                Fix centre = (l.MinX + l.MaxX) / 2;
                Fix away = Fix.Abs(centre - self.Position.X);
                if (away > cfg.LadderSeek) continue;
                if (best >= 0 && away >= nearest) continue;

                best = i;
                nearest = away;
            }

            bot.Ladder = best;
            if (best < 0) return InputFlags.None;

            Aabb box = world.Ladders[best];
            Fix mid = (box.MinX + box.MaxX) / 2;
            Fix offset = mid - self.Position.X;

            // Close enough to be on it: reach for the rungs.
            if (Fix.Abs(offset) <= Fix.FromMilli(150))
                return up ? InputFlags.Up : InputFlags.Down;

            return offset > Fix.Zero ? InputFlags.Right : InputFlags.Left;
        }

        /// <summary>
        /// Where the bot points its weapon this tick.
        ///
        /// Two poses and nothing else, which is all a fight of this kind needs:
        ///
        ///   guard   blade level, straight at them. It does not attack, it does not have
        ///           to be timed, and it stops anything that arrives, because weapons
        ///           stop weapons. Standing still with the arm out is a real defence now.
        ///   swing   throw the arm from high to low, or low to high, through the level
        ///           where the body is. The flip is what builds tip speed, and tip speed
        ///           is the whole of damage.
        ///
        /// The arc is 120 degrees rather than a full windmill because the body sits at
        /// the middle of it -- the blade should be fastest where the target is, not where
        /// it turns around.
        /// </summary>
        /// <summary>
        /// Where the bot puts its arm, in the SAME FRAME the player's stick is read in:
        /// forward means at him, up means over him, down means under him.
        ///
        /// It used to aim in world terms -- AimX = towards * 1000 -- which is wrong for
        /// exactly the reason the frame exists. ArmMotor.Assist then turned that by the
        /// direction to the foe, which is the same direction, so the two cancelled: a
        /// bot standing to its foe's RIGHT held its guard to the right, away from him,
        /// and its high swings came out low. Only one of the two may know which way he
        /// is, and the frame is the one that does.
        /// </summary>
        static void Aim(ref BotState bot, in PlayerSimState self, in PlayerSimState foe,
                        in BotConfig cfg)
        {
            if (bot.SwingHold > 0)
            {
                bot.SwingHold--;
                if (bot.SwingHold == 0) bot.SwingHigh = !bot.SwingHigh;

                bot.AimX = 500;
                bot.AimY = (short)(bot.SwingHigh ? 866 : -866);
                return;
            }

            if (bot.GuardHold > 0)
            {
                bot.GuardHold--;
                bot.AimX = 1000;
                bot.AimY = 0;
                return;
            }

            // Carrying. The stick is centred, so the arm falls to the low pose and the
            // bot is open -- which is what makes it beatable.
            bot.AimX = 0;
            bot.AimY = 0;
        }

        /// <summary>
        /// Commit to one end of the arc, and hold it long enough for the arm to get there.
        ///
        /// The dwell is the weapon's own AttackFrames, which is exactly the number of
        /// ticks ArmMotor gives it to sweep half a circle. A shorter hold and the heavy
        /// weapons would be ordered to reverse before they had travelled, which reads as
        /// a bot waving a warhammer like a flag and doing no damage with it.
        /// </summary>
        static void Swing(ref BotState bot, in PlayerSimState self)
        {
            if (bot.SwingHold > 0) return;

            bot.GuardHold = 0;
            int frames = self.Inventory.WeaponDef.AttackFrames;
            bot.SwingHold = frames < 4 ? 4 : frames;
        }

        static bool Roll(ref BotState bot, int permille) =>
            permille > 0 && bot.Rng.Below(1000) < permille;

        static Fix Scale(Fix v, int permille) =>
            new Fix((int)(((long)v.Raw * permille) / 1000));
    }
}

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
        public static InputFlags Decide(ref BotState bot, in PlayerSimState self,
                                        in PlayerSimState foe, SimWorld world,
                                        in BotConfig cfg, in MoveConfig move)
        {
            // Dead, reeling, or being carried along by a scripted move: nothing to decide,
            // and pressing anything during a mount or a climb-out is pressing into a wall.
            if (self.Dead || self.Reeling ||
                self.Mode == MoveMode.Mounting || self.Mode == MoveMode.Mantling)
            {
                bot.Held = InputFlags.None;
                bot.Think = 0;
                return InputFlags.None;
            }

            if (bot.Think > 0)
            {
                bot.Think--;
                return bot.Held;
            }

            bot.Think = cfg.ReactionFrames +
                        (cfg.ReactionJitter > 0 ? bot.Rng.Below(cfg.ReactionJitter) : 0);
            bot.Held = Think(ref bot, in self, in foe, world, in cfg, in move);
            return bot.Held;
        }

        static InputFlags Think(ref BotState bot, in PlayerSimState self,
                                in PlayerSimState foe, SimWorld world,
                                in BotConfig cfg, in MoveConfig move)
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

            if (cfg.Skill == BotSkill.Dummy)
                return Roll(ref bot, cfg.GuardPermille) ? InputFlags.Dodge : InputFlags.None;

            // Where it wants to stand, measured off its OWN weapon: a spear bot that stands
            // at a club's distance has thrown away the only thing a spear is for.
            Fix reach = move.BodyWidth / 2 + self.Inventory.WeaponDef.Reach;
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

                if (Roll(ref bot, cfg.FightingWithdrawalPermille))
                {
                    flags |= towards > 0 ? InputFlags.AimRight : InputFlags.AimLeft;
                    if (Roll(ref bot, cfg.AggressionPermille)) flags |= InputFlags.Attack;
                }

                return flags;
            }

            // In range. Guard against a wind-up it can see, swing otherwise -- in that order,
            // because a bot that only ever attacks teaches the player to only ever attack.
            if (foe.Attack == AttackPhase.Windup && Roll(ref bot, cfg.GuardPermille * 3))
                return InputFlags.Dodge;                 // standing still, this is the guard

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
                flags |= InputFlags.Attack;
                flags |= towards > 0 ? InputFlags.AimRight : InputFlags.AimLeft;
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

        static bool Roll(ref BotState bot, int permille) =>
            permille > 0 && bot.Rng.Below(1000) < permille;

        static Fix Scale(Fix v, int permille) =>
            new Fix((int)(((long)v.Raw * permille) / 1000));
    }
}

namespace GulagRunners.Sim
{
    /// <summary>
    /// Opening chests and picking up what is in them.
    ///
    /// Separate from <see cref="PlayerMotor"/> because it is a different job — that one decides
    /// where a body is, this one decides what it is carrying — but it lives under the same rules:
    /// no float, no UnityEngine, no allocation, same result on both devices (docs/06).
    ///
    /// The one piece of shared state in the game so far is the chests, and two players tick
    /// independently. The rule that keeps that deterministic is simple: a chest has one opener,
    /// and only its opener may change its progress. Everyone else reads it and leaves it alone.
    /// The opener keeps the claim until the progress drains to nothing, even if they have walked
    /// away, so a half-opened chest is always drained by exactly one player on exactly one tick.
    /// </summary>
    public static class LootMotor
    {
        /// <summary>Progress added per tick by bare hands, in thousandths of a frame.</summary>
        const int BareGain = 1000;

        public static void Step(ref PlayerSimState s, int playerIndex, InputFlags input,
                                SimWorld world, ChestSimState[] chests, GroundItem[] ground,
                                in ChestConfig cfg, in DropConfig drop, in MoveConfig move)
        {
            s.PickedUp = ItemId.None;
            s.Dropped = ItemId.None;
            s.OpenedChest = -1;
            s.StandingOn = -1;

            // The swap wants a press, not a hold: holding the button is how a chest is opened,
            // and the two must not fight over the same frame.
            bool actionHeld = input.Has(InputFlags.Action);
            bool actionPressed = actionHeld && !s.ActionHeld;
            s.ActionHeld = actionHeld;

            if (world == null || chests == null ||
                world.Chests.Length == 0 || chests.Length != world.Chests.Length)
            {
                s.OpeningChest = -1;
                TakeFromGround(ref s, actionPressed, ground, in drop, in move, false);
                return;
            }

            // Opening is something you do standing on the floor. Doing it mid-jump, mid-dodge or
            // off a ladder is not a mechanic anybody asked for.
            bool working = input.Has(InputFlags.Action) && s.Mode == MoveMode.Grounded;
            int target = working ? FindChest(ref s, playerIndex, world, chests, in move) : -1;

            // Let go of whatever we were on. The claim survives walking away: it is the only way
            // a chest that nobody is standing at still drains, and it drains exactly once a tick.
            if (s.OpeningChest >= 0 && s.OpeningChest != target)
                Drain(ref s, playerIndex, chests, in cfg);

            if (target < 0)
            {
                // Nothing to open here, so the action button is the loot button.
                TakeFromGround(ref s, actionPressed, ground, in drop, in move, false);
                return;
            }

            // A chest under the hands wins the button: holding it to pry would otherwise swap
            // your weapon for whatever happens to be lying at your feet. Walking over something
            // with a free slot still picks it up, because that costs no button at all.
            TakeFromGround(ref s, actionPressed, ground, in drop, in move, true);

            ref ChestSimState state = ref chests[target];
            ChestDef def = world.Chests[target];
            ChestKindConfig kind = cfg.For(def.Kind);

            state.Opener = playerIndex;
            s.OpeningChest = target;

            // Turn to what you are prying at. A fighter levering a crate open with his back to
            // it is the kind of thing nobody files and everybody sees, and the search animation
            // has to have something to face. LootMotor runs last in the tick, after movement
            // has had its say, so this is the final word on facing for the frame, and it lapses
            // the moment the chest does, because the claim goes with it.
            //
            // The deadzone matters: a fighter stands INSIDE a chest's box to open it, so
            // without one, shuffling across its middle flips him back and forth.
            Fix chestX = def.Box.MinX + (def.Box.MaxX - def.Box.MinX) / 2;
            Fix deadzone = move.BodyWidth / 4;
            if (chestX > s.Position.X + deadzone) s.Facing = 1;
            else if (chestX < s.Position.X - deadzone) s.Facing = -1;

            // The weapon is the crowbar. Bare hands are slower and quieter, and that is the trade
            // the whole scavenge phase is built on (docs/03).
            bool tool = !s.Inventory.BareHanded;
            int gain = s.Inventory.WeaponDef.PrySpeed.ToMilli();
            if (gain < 1) gain = 1;
            state.Progress += gain;

            if (s.StepNoiseTimer == 0)
            {
                s.Noise = tool ? kind.ToolNoise : kind.BareNoise;
                s.StepNoiseTimer = cfg.NoiseFrames;
            }

            int needed = kind.BareFrames * BareGain;
            if (state.Progress < needed) return;

            // Open.
            state.Progress = needed;
            state.Opened = true;
            state.Opener = -1;
            s.OpeningChest = -1;

            // Spend the lever before taking the prize, so a weapon that breaks on the last chest
            // breaks even when that chest held its replacement.
            if (tool) s.Inventory.SpendWeapon(kind.ToolWear);

            // The loot comes OUT; it does not appear in a slot. Seeing it land is the payoff for
            // the time and the noise just spent, and it is what makes a full slot a decision
            // rather than something that happened to you (docs/03).
            // Thrown TOWARDS whoever opened it, not along their facing: at a chest you are
            // facing into it, and loot that lands on the far side is loot you have to walk round
            // the thing you just opened to reach.
            FixVec2 centre = Centre(in def.Box);
            // What the round dealt into it, not what the arena was authored with: the two are
            // the same only for a chest whose contents were pinned by hand.
            ItemId prize = state.Contents != ItemId.None ? state.Contents : def.Contents;
            Pop(ground, prize, centre, ThrowDirection(in s, in def.Box), in drop);

            s.OpenedChest = target;
            s.Noise = NoiseLevel.Loud;             // a chest coming open is heard across the floor
        }

        // ---------------------------------------------------------------- loot on the floor

        static FixVec2 Centre(in Aabb box) =>
            new FixVec2(box.MinX + (box.MaxX - box.MinX) / 2,
                        box.MinY + (box.MaxY - box.MinY) / 2);

        /// <summary>
        /// Which way loot should leave a chest: towards the player who opened it, so they do not
        /// have to walk round the thing they just opened to pick it up.
        ///
        /// Standing squarely on the chest there is no "towards", so it goes back the way they
        /// came — the opposite of the way they are facing, since walking up to a chest leaves you
        /// facing into it.
        /// </summary>
        static int ThrowDirection(in PlayerSimState s, in Aabb box)
        {
            Fix centreX = box.MinX + (box.MaxX - box.MinX) / 2;
            Fix offset = s.Position.X - centreX;
            Fix meaningful = (box.MaxX - box.MinX) / 4;

            if (offset > meaningful) return 1;
            if (offset < -meaningful) return -1;
            return s.Facing >= 0 ? -1 : 1;
        }

        /// <summary>Throws an item out of a chest.</summary>
        public static int Pop(GroundItem[] ground, ItemId item, FixVec2 from, int dir,
                              in DropConfig cfg)
        {
            return Launch(ground, item, from, new FixVec2(cfg.PopSpeedX * Sign(dir), cfg.PopSpeedY),
                          cfg.PopLockFrames);
        }

        /// <summary>Drops an item out of a hand, away from the way the body is facing.</summary>
        public static int DropItem(GroundItem[] ground, ItemId item, FixVec2 from, int dir,
                                   in DropConfig cfg)
        {
            return Launch(ground, item, from, new FixVec2(cfg.DropSpeedX * -Sign(dir), cfg.DropSpeedY),
                          cfg.DropLockFrames);
        }

        static int Sign(int dir) => dir >= 0 ? 1 : -1;

        static int Launch(GroundItem[] ground, ItemId item, FixVec2 from, FixVec2 velocity, int lockFrames)
        {
            if (ground == null || ground.Length == 0 || item == ItemId.None) return -1;

            int slot = FreeSlot(ground);
            if (slot < 0) return -1;

            ground[slot] = new GroundItem
            {
                Item = item,
                State = GroundItemState.Flying,
                Position = from,
                Velocity = velocity,
                PickupLock = lockFrames,
                Age = 0
            };
            return slot;
        }

        /// <summary>
        /// An empty slot, or the oldest item already resting on the floor. A full pool means the
        /// floor is littered, and losing the stalest thing on it is better than a drop that
        /// silently does not happen.
        /// </summary>
        static int FreeSlot(GroundItem[] ground)
        {
            int oldest = -1, oldestAge = -1;
            for (int i = 0; i < ground.Length; i++)
            {
                if (!ground[i].Live) return i;
                if (ground[i].State == GroundItemState.Resting && ground[i].Age > oldestAge)
                {
                    oldestAge = ground[i].Age;
                    oldest = i;
                }
            }
            return oldest;
        }

        /// <summary>
        /// Moves every item in the air and lands it. Called once per tick for the whole match,
        /// not once per player: an item belongs to nobody until somebody takes it.
        /// </summary>
        public static void StepGround(GroundItem[] ground, SimWorld world, in DropConfig cfg)
        {
            if (ground == null || world == null) return;

            for (int i = 0; i < ground.Length; i++)
            {
                ref GroundItem item = ref ground[i];
                if (!item.Live) continue;

                item.Age++;
                if (item.PickupLock > 0) item.PickupLock--;
                if (item.State != GroundItemState.Flying) continue;

                item.Velocity.Y = item.Velocity.Y - cfg.Gravity * PlayerMotor.Dt;
                if (item.Velocity.Y < -cfg.MaxFallSpeed) item.Velocity.Y = -cfg.MaxFallSpeed;

                FixVec2 next = new FixVec2(item.Position.X + item.Velocity.X * PlayerMotor.Dt,
                                           item.Position.Y + item.Velocity.Y * PlayerMotor.Dt);

                // A wall stops it dead. Without this an item thrown at a wall sails through it
                // and lands in the next room, or inside the geometry, where nobody can reach it.
                if (item.Velocity.X != Fix.Zero && Blocked(next.X, item.Position.Y, world, cfg))
                {
                    next.X = item.Position.X;
                    item.Velocity.X = Fix.Zero;
                }

                if (item.Velocity.Y <= Fix.Zero &&
                    TryFloorUnder(next.X, item.Position.Y, world, cfg.PickupRadius, out Fix floorY))
                {
                    Fix rest = floorY + cfg.PickupRadius;
                    if (next.Y <= rest)
                    {
                        // One soft bounce, then dead. An item that rolls is an item that ends up
                        // somewhere neither player can predict.
                        Fix up = -item.Velocity.Y * cfg.Bounce;
                        item.Position = new FixVec2(next.X, rest);

                        if (up > Fix.FromMilli(400))
                        {
                            item.Velocity = new FixVec2(item.Velocity.X / 2, up);
                        }
                        else
                        {
                            item.Velocity = FixVec2.Zero;
                            item.State = GroundItemState.Resting;
                        }
                        continue;
                    }
                }

                item.Position = next;

                // Fell out of the world: give it back rather than leaving a ghost in the pool.
                if (item.Position.Y < Fix.FromInt(-40)) item = default;
            }
        }

        /// <summary>Is there solid where the item is about to be.</summary>
        static bool Blocked(Fix x, Fix y, SimWorld world, in DropConfig cfg)
        {
            Aabb box = new Aabb(x - cfg.PickupRadius, y - cfg.PickupRadius,
                                x + cfg.PickupRadius, y + cfg.PickupRadius);
            for (int i = 0; i < world.Solids.Length; i++)
                if (box.Overlaps(in world.Solids[i])) return true;
            return false;
        }

        /// <summary>Top of the nearest solid under a point, searching a short way down.</summary>
        static bool TryFloorUnder(Fix x, Fix fromY, SimWorld world, Fix radius, out Fix floorY)
        {
            floorY = Fix.Zero;
            bool found = false;
            Fix lowest = fromY - Fix.FromInt(2);

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb solid = world.Solids[i];
                if (solid.MaxX <= x - radius || solid.MinX >= x + radius) continue;
                if (solid.MaxY > fromY + radius) continue;
                if (solid.MaxY < lowest) continue;
                if (!found || solid.MaxY > floorY) { floorY = solid.MaxY; found = true; }
            }

            return found;
        }

        /// <summary>
        /// Picking things up. Two rules, both from docs/02:
        ///
        ///   walking over something with the slot free takes it, with no button at all;
        ///   with the slot full it is a deliberate press, and what was in the slot drops.
        ///
        /// The free-slot rule never costs the player anything, so it runs even while they are
        /// busy prying a chest. The swap does not: that button is already spoken for.
        /// </summary>
        static void TakeFromGround(ref PlayerSimState s, bool actionPressed, GroundItem[] ground,
                                   in DropConfig cfg, in MoveConfig move, bool buttonTaken)
        {
            if (ground == null || ground.Length == 0) return;
            if (s.Mode != MoveMode.Grounded && s.Mode != MoveMode.Airborne) return;

            Aabb body = s.Body(in move);
            int free = -1, swap = -1;

            for (int i = 0; i < ground.Length; i++)
            {
                GroundItem item = ground[i];
                if (!item.Takeable) continue;

                Aabb box = new Aabb(item.Position.X - cfg.PickupRadius, item.Position.Y - cfg.PickupRadius,
                                    item.Position.X + cfg.PickupRadius, item.Position.Y + cfg.PickupRadius);
                if (!body.Overlaps(in box)) continue;

                if (s.Inventory.SlotContents(ItemTable.KindOf(item.Item)) == ItemId.None)
                {
                    if (free < 0) free = i;
                }
                else if (swap < 0)
                {
                    swap = i;
                }
            }

            // Tell presentation what the action button would do if it were pressed now, so the
            // button can say so before it is pressed rather than after.
            s.StandingOn = free >= 0 ? free : swap;

            if (free >= 0)
            {
                Take(ref s, ground, free, in cfg, false);
                return;
            }

            if (swap < 0 || buttonTaken || !actionPressed) return;
            if (s.Mode != MoveMode.Grounded) return;

            Take(ref s, ground, swap, in cfg, true);
        }

        static void Take(ref PlayerSimState s, GroundItem[] ground, int index,
                         in DropConfig cfg, bool swapping)
        {
            ItemId taken = ground[index].Item;
            FixVec2 at = ground[index].Position;
            ground[index] = default;

            ItemId displaced = s.Inventory.Equip(taken);
            s.PickedUp = taken;

            if (swapping && displaced != ItemId.None)
            {
                DropItem(ground, displaced, at, s.Facing, in cfg);
                s.Dropped = displaced;
            }

            s.Noise = NoiseLevel.Quiet;            // picking something up is still a sound
        }

        /// <summary>
        /// Drains the chest this player had claimed. Returns it to the pool once it is empty, so
        /// somebody else can start on it.
        /// </summary>
        static void Drain(ref PlayerSimState s, int playerIndex, ChestSimState[] chests,
                          in ChestConfig cfg)
        {
            int i = s.OpeningChest;
            if (i < 0 || i >= chests.Length) { s.OpeningChest = -1; return; }

            ref ChestSimState held = ref chests[i];
            if (held.Opener != playerIndex) { s.OpeningChest = -1; return; }

            int decay = BareGain * (cfg.DecayMultiplier > 0 ? cfg.DecayMultiplier : 1);
            held.Progress -= decay;

            if (held.Progress > 0) return;

            held.Progress = 0;
            held.Opener = -1;
            s.OpeningChest = -1;
        }

        /// <summary>
        /// The chest this player can work on: one their body overlaps, still shut, and not claimed
        /// by the other player. The one already being opened wins, so standing where two chests
        /// overlap does not make the progress flicker between them.
        /// </summary>
        static int FindChest(ref PlayerSimState s, int playerIndex, SimWorld world,
                             ChestSimState[] chests, in MoveConfig move)
        {
            Aabb body = s.Body(in move);

            if (s.OpeningChest >= 0 && s.OpeningChest < world.Chests.Length &&
                Available(chests[s.OpeningChest], playerIndex) &&
                body.Overlaps(in world.Chests[s.OpeningChest].Box))
                return s.OpeningChest;

            for (int i = 0; i < world.Chests.Length; i++)
            {
                if (!Available(chests[i], playerIndex)) continue;
                if (body.Overlaps(in world.Chests[i].Box)) return i;
            }

            return -1;
        }

        static bool Available(in ChestSimState state, int playerIndex) =>
            !state.Opened && (state.Opener < 0 || state.Opener == playerIndex);

        /// <summary>Seconds this chest takes for a player holding this weapon. For tools and UI.</summary>
        public static int FramesToOpen(ChestKind kind, ItemId weapon, in ChestConfig cfg)
        {
            int gain = ItemTable.WeaponOrFists(weapon).PrySpeed.ToMilli();
            if (gain < 1) gain = 1;
            return (cfg.For(kind).BareFrames * BareGain + gain - 1) / gain;
        }
    }
}

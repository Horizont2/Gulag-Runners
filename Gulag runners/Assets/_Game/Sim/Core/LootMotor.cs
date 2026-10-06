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
                                SimWorld world, ChestSimState[] chests,
                                in ChestConfig cfg, in MoveConfig move)
        {
            s.PickedUp = ItemId.None;
            s.OpenedChest = -1;

            if (world == null || chests == null ||
                world.Chests.Length == 0 || chests.Length != world.Chests.Length)
            {
                s.OpeningChest = -1;
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

            if (target < 0) return;

            ref ChestSimState state = ref chests[target];
            ChestDef def = world.Chests[target];
            ChestKindConfig kind = cfg.For(def.Kind);

            state.Opener = playerIndex;
            s.OpeningChest = target;

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

            s.Inventory.Equip(def.Contents);
            s.PickedUp = def.Contents;
            s.OpenedChest = target;
            s.Noise = NoiseLevel.Loud;             // a chest coming open is heard across the floor
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

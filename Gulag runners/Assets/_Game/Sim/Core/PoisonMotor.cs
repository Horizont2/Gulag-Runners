namespace GulagRunners.Sim
{
    /// <summary>
    /// The arena filling up from the bottom, and what that costs whoever is standing in it.
    ///
    /// Two pure functions of the frame number and one loop over the fighters. That is the
    /// whole mechanic, and being pure is the point: two devices compute the same surface to
    /// the millimetre without exchanging a word about it, and a rollback rebuilds the flood
    /// from the frame alone (docs/06). There is no poison state anywhere — not in
    /// <see cref="MatchSimState"/>, not in the world — only a level that the frame number
    /// already answers.
    ///
    /// It takes the floor away without ever moving a body, which is the difference between
    /// this and the steel it replaces. A shutter has to decide what happens to a fighter in
    /// its way; a tide does not, because it is never in anybody's way. All it does is make
    /// the ground floor expensive — and then, once it is over the ground floor, impossible.
    /// </summary>
    public static class PoisonMotor
    {
        /// <summary>
        /// How far up the pool the surface has climbed, 0 to 1.
        ///
        /// Nothing until the closing phase begins — the same beat docs/01 gives the shutters,
        /// and the same beat the gate warning lights are already counting down to — then even
        /// across the rise, then full for the rest of the match. So the surface arrives at the
        /// top of the pool exactly as sudden death starts, which is when the last shutter
        /// would have fallen, and it stays there rather than draining away under a result.
        /// </summary>
        public static Fix RiseAt(int frame, in MatchConfig clock, in PoisonConfig cfg)
        {
            if (!cfg.On) return Fix.Zero;

            int from = MatchMotor.PhaseStart(MatchPhase.Closing, in clock);
            int window = cfg.RiseFrames > 0 ? cfg.RiseFrames : clock.ClosingFrames;

            if (window <= 0) return frame >= from ? Fix.One : Fix.Zero;

            int done = frame - from;
            if (done <= 0) return Fix.Zero;
            if (done >= window) return Fix.One;

            // Thousandths, like every other progress in this layer: integer arithmetic on the
            // frame number, so the mirror in docs/refs can reproduce it exactly.
            return Fix.FromMilli(done * 1000 / window);
        }

        /// <summary>
        /// Is this the tick the poison takes its bite.
        ///
        /// Counted from the start of the rise rather than from frame zero, so changing a phase
        /// length ahead of it cannot shift the bite onto a different tick of the flood — and
        /// so the first bite is always a whole interval after the poison appears, never on the
        /// tick it arrives.
        /// </summary>
        public static bool Bites(int frame, in MatchConfig clock, in PoisonConfig cfg)
        {
            if (!cfg.On || cfg.Bite <= 0 || cfg.BiteFrames <= 0) return false;

            int done = frame - MatchMotor.PhaseStart(MatchPhase.Closing, in clock);
            return done > 0 && done % cfg.BiteFrames == 0;
        }

        /// <summary>
        /// Bites everyone standing in it, once.
        ///
        /// Called from the end of the shared tick, beside the hit resolution, for two reasons.
        /// Where a body ENDED the tick is where it is standing, so a fighter who climbed out
        /// this tick is out; and the outputs a view reads — the damage, the death — are
        /// cleared at the front of each fighter's own tick, so damage dealt before that is
        /// damage nothing ever hears about.
        ///
        /// It judges the FEET. Ankle-deep in poison is in the poison: the surface is a line
        /// you are either above or below, and a rule that waited for it to reach the chest
        /// would let a fighter stand in a flooded room arguing about how much of him was wet.
        /// </summary>
        public static void Bite(PlayerSimState[] fighters, SimWorld world,
                                in PoisonConfig cfg, in MoveConfig move)
        {
            if (fighters == null || world == null || !world.HasPoison) return;
            if (!cfg.On || cfg.Bite <= 0) return;

            Fix halfDepth = move.BodyDepth / 2;

            for (int i = 0; i < fighters.Length; i++)
            {
                if (fighters[i].Dead) continue;
                if (!world.InPoison(fighters[i].Position, fighters[i].Depth, halfDepth)) continue;

                CombatMotor.Wound(ref fighters[i], cfg.Bite);
                fighters[i].DamageTaken = (short)cfg.Bite;

                // NOT WasHit. That flag means a blow landed: it drives the flinch, the hit
                // sparks and the camera shake, and a fighter wading through poison does not
                // flinch twice a second. The damage and the death are told truthfully; what
                // is left out is the claim that somebody hit him.
            }
        }
    }
}

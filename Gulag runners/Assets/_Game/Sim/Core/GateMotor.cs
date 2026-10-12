namespace GulagRunners.Sim
{
    /// <summary>
    /// What a closing gate does to a body that is on the wrong side of it.
    ///
    /// This is the part that keeps docs/01's closing phase a herding and not an accident. A
    /// shutter that simply becomes solid can seal a fighter into a wing with nothing left to
    /// walk to and nobody to fight: the match then ends on the clock, in two separate empty
    /// rooms, which is the opposite of what the phase is for. So on the tick a gate shuts,
    /// everything still behind it comes through.
    ///
    /// It is deliberately a shove and never a kill. docs/01 shuts rooms to force the fight;
    /// a shutter that crushes would decide the fight instead, and a death nobody could see
    /// coming is the worst way for a 1v1 to end.
    /// </summary>
    public static class GateMotor
    {
        /// <summary>A hair's clearance, so the body is not left touching what just moved it.</summary>
        public static readonly Fix Skin = Fix.FromMilli(10);

        /// <summary>
        /// Brings every fighter through the gates that shut on this tick.
        ///
        /// Runs once for the whole match, from the same place the clock is stepped and before
        /// anything moves, because a fighter shoved after they have already moved this tick is
        /// a fighter who spent one tick inside a wall.
        /// </summary>
        public static void Shove(PlayerSimState[] fighters, GateDef[] gates, uint justShut,
                                 in MoveConfig cfg)
        {
            if (fighters == null || gates == null || justShut == 0) return;

            Fix halfWidth = cfg.BodyWidth / 2;
            Fix halfDepth = cfg.BodyDepth / 2;

            int count = gates.Length < 32 ? gates.Length : 32;

            for (int g = 0; g < count; g++)
            {
                if ((justShut & (1u << g)) == 0) continue;
                GateDef gate = gates[g];

                for (int f = 0; f < fighters.Length; f++)
                {
                    Fix height = fighters[f].Crouching ? cfg.CrouchHeight : cfg.BodyHeight;

                    // Only a gate that would actually have blocked this body herds it. A
                    // shutter on a slice the fighter cannot touch is a shutter they could walk
                    // around, and moving them for it would teleport them out of a room that
                    // is still open.
                    if (!gate.Span.Reaches(fighters[f].Depth, halfDepth)) continue;
                    if (!gate.Behind(fighters[f].Position, height)) continue;

                    Bring(ref fighters[f], in gate, halfWidth, height, in cfg);
                }
            }
        }

        /// <summary>
        /// One body, through one gate: put down clear of it, let go of everything it was
        /// holding, and knocked about for it.
        /// </summary>
        static void Bring(ref PlayerSimState s, in GateDef gate, Fix halfWidth, Fix height,
                          in MoveConfig cfg)
        {
            s.Position = gate.Clear(s.Position, halfWidth, height, Skin);
            s.Velocity = FixVec2.Zero;

            // Everything the body was attached to was on the other side of a wall a moment
            // ago. A ladder index or a chest left pointing back through it is a fighter the
            // motor drags into the shutter on the next tick.
            s.Mode = MoveMode.Airborne;
            s.LadderIndex = -1;
            s.OpeningChest = -1;
            s.StandingOn = -1;
            s.ScriptTimer = 0;
            s.ScriptFrames = 0;
            s.Attack = AttackPhase.None;

            // Staggered, not merely moved. Being put somewhere you did not walk to has to read
            // as something that happened to you, and the window is what makes the shove a risk
            // rather than a free escape from whoever was chasing you through that doorway.
            if (s.StaggerTimer < cfg.GateStaggerFrames)
                s.StaggerTimer = cfg.GateStaggerFrames;

            // The whole arena hears it. docs/01's information ladder is built on sound, and a
            // steel shutter coming down on somebody is the loudest thing in the last minute.
            s.Noise = NoiseLevel.Loud;
        }
    }
}

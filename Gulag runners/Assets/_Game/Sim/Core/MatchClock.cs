namespace GulagRunners.Sim
{
    /// <summary>The shape of a match, from docs/01. One match is one fight and one life.</summary>
    public enum MatchPhase : byte
    {
        /// <summary>Both in their cages at opposite ends. The doors are coming up.</summary>
        Cages = 0,

        /// <summary>Fog of war. You are not shown the other player; you are told about them.</summary>
        Scavenge = 1,

        /// <summary>One radar pulse: a direction, not a position.</summary>
        Siren = 2,

        /// <summary>You know roughly where. The rest is sound and what has been broken open.</summary>
        Hunt = 3,

        /// <summary>The outer rooms shut, one after another, until only the middle is left.</summary>
        Closing = 4,

        /// <summary>One platform, and bare hands hurt.</summary>
        SuddenDeath = 5,

        /// <summary>
        /// Somebody won. The result stands for a few seconds and then the next round deals:
        /// docs/01 puts an "Again" button straight after a loss rather than a lobby.
        /// </summary>
        Over = 6
    }

    /// <summary>
    /// How long each phase runs, in ticks. Straight off the table in docs/01, which is written
    /// in clock time; sixty of these is a second.
    ///
    /// The two numbers that are not rows in that table are the ones that make the match finite.
    /// docs/01 bounds it at 2:00-2:30 and then hands the player an "Again" button, so sudden
    /// death gets a length of its own and the result gets a length of its own, and the default
    /// set adds up to exactly 2:30.
    /// </summary>
    public struct MatchConfig
    {
        public int CageFrames;
        public int ScavengeFrames;
        public int SirenFrames;
        public int HuntFrames;
        public int ClosingFrames;

        /// <summary>
        /// How long sudden death may run before the match is decided on health.
        ///
        /// docs/01 bounds a match at 2:00-2:30, so this is the last phase and it has to be
        /// finite: without it the clock reaches sudden death and sits there, and a round where
        /// neither fighter can close the distance never ends at all.
        /// </summary>
        public int SuddenDeathFrames;

        /// <summary>
        /// Ticks the result stands before the next round deals. 0 holds it forever, which is
        /// what a build with a results screen in front of the rematch wants.
        /// </summary>
        public int RestartFrames;

        /// <summary>
        /// How long the shutters take, inside the closing phase. docs/01 gives them 45 s of
        /// that phase's 65: the outer rooms go one after another across this window, and what
        /// is left of the phase afterwards is both fighters in the one room with nowhere to
        /// walk away to. 0 spreads them across the whole phase instead.
        /// </summary>
        public int ShutterFrames;

        public static MatchConfig Default() => new MatchConfig
        {
            CageFrames        = 180,    // 0:00-0:03  the doors
            ScavengeFrames    = 1320,   // 0:03-0:25  alone with the chests
            SirenFrames       = 90,     // 0:25       one pulse, 1.5 s
            HuntFrames        = 3000 - 90,  // 0:25-1:15  you know the direction
            ClosingFrames     = 3900,   // 1:15-2:20  the rooms shut, then the waiting
            SuddenDeathFrames = 600,    // 2:20-2:30  the ceiling, and a decision either way
            RestartFrames     = 300,    // 0:05       long enough to see what happened
            ShutterFrames     = 2700,   // 1:15-2:00  the rooms, one after another
        };
    }

    /// <summary>
    /// The match's clock, as simulation state.
    ///
    /// It belongs here and not in a MonoBehaviour for the same reason everything else does: two
    /// devices have to agree on which phase it is without talking about it, and a phase that
    /// arrives a frame apart is a siren that fires twice (docs/06).
    /// </summary>
    public struct MatchSimState
    {
        public int Frame;
        public MatchPhase Phase;

        /// <summary>-1 while it is still anybody's, 0 or 1 for the winner, 2 for a draw.</summary>
        public int Winner;

        /// <summary>
        /// Which round this is, from 0. Carried across a restart, because it is the one thing
        /// about a finished round that the next one inherits — everything else is dealt again.
        /// </summary>
        public int Round;

        /// <summary>Ticks since the match ended. Presentation holds the result on it.</summary>
        public int OverFrame;

        /// <summary>
        /// Which of the arena's gates are shut, one bit each. Copied onto the world before
        /// anything moves; a pure function of the frame, so it needs no reconciling.
        /// </summary>
        public uint GatesShut;

        /// <summary>The gates that shut on this tick, for the shove and for the rumble.</summary>
        public uint GatesJustShut;

        /// <summary>
        /// The radar pulse of docs/01: which way fighter 1 was from fighter 0 when the siren
        /// fired, as a sign. A DIRECTION and not a position, which is the whole rule — it is
        /// taken once, on the siren's first tick, and does not follow them afterwards.
        /// </summary>
        public sbyte SirenDirection;

        /// <summary>Output of the last tick, for presentation to react to exactly once.</summary>
        public bool PhaseChanged;
        public bool JustEnded;

        /// <summary>
        /// The result has stood long enough; deal the next round.
        ///
        /// Unlike the two above this is not a one-shot: it stays up every tick until whoever
        /// owns the match actually restarts it. A one-shot would mean a frame that happened to
        /// run no ticks, or a listener attached a frame late, loses the rematch and the match
        /// sits on a corpse forever.
        /// </summary>
        public bool WantsRestart;

        public static MatchSimState Fresh() => Fresh(0);

        public static MatchSimState Fresh(int round) => new MatchSimState
        {
            Frame = 0, Phase = MatchPhase.Cages, Winner = -1, Round = round,
            OverFrame = 0, PhaseChanged = true, JustEnded = false, WantsRestart = false,
            GatesShut = 0, GatesJustShut = 0, SirenDirection = 0
        };

        /// <summary>The same match, one round on. The clock is new; the count is not.</summary>
        public MatchSimState NextRound() => Fresh(Round + 1);

        /// <summary>Can a fighter act yet. In the cage, no: the doors are still coming up.</summary>
        public bool InputAllowed => Phase != MatchPhase.Cages && Phase != MatchPhase.Over;

        /// <summary>True once somebody has won. Winner says who.</summary>
        public bool Finished => Phase == MatchPhase.Over;

        /// <summary>
        /// Which way this fighter is told to look when the siren goes. Fighter 0 is given the
        /// pulse as it was taken; everyone else is given its mirror, because the arena has two
        /// ends and the answer is a direction rather than a place.
        /// </summary>
        public sbyte SirenFor(int index) =>
            (sbyte)(index == 0 ? SirenDirection : -SirenDirection);

        /// <summary>Is a gate shut. Out-of-range gates read as open.</summary>
        public bool Shut(int gate) =>
            gate >= 0 && gate < 32 && (GatesShut & (1u << gate)) != 0;

        /// <summary>Did this gate shut on this very tick.</summary>
        public bool JustShut(int gate) =>
            gate >= 0 && gate < 32 && (GatesJustShut & (1u << gate)) != 0;

        /// <summary>How far through the cage doors opening, 0 to 1. Presentation drives on it.</summary>
        public Fix CageProgress(in MatchConfig cfg) =>
            Phase != MatchPhase.Cages || cfg.CageFrames <= 0
                ? Fix.One
                : Fix.FromMilli(1000 * (Frame < cfg.CageFrames ? Frame : cfg.CageFrames)
                                / cfg.CageFrames);

        /// <summary>
        /// How far through the result, 0 to 1, for a camera that holds on the end and then lets
        /// go. 0 everywhere but the Over phase, so a caller can drive on it unconditionally.
        /// </summary>
        public Fix OverProgress(in MatchConfig cfg) =>
            Phase != MatchPhase.Over || cfg.RestartFrames <= 0
                ? Fix.Zero
                : Fix.FromMilli(1000 * (OverFrame < cfg.RestartFrames
                                            ? OverFrame : cfg.RestartFrames)
                                / cfg.RestartFrames);
    }

    /// <summary>Advances the clock and decides when it has stopped mattering.</summary>
    public static class MatchMotor
    {
        /// <summary>
        /// One tick of the match itself. Called once per tick for the whole match, by whoever
        /// owns the tick, with every fighter's state so a death can end it.
        /// </summary>
        public static void Step(ref MatchSimState s, in MatchConfig cfg,
                                PlayerSimState[] fighters, GateDef[] gates = null)
        {
            s.PhaseChanged = false;
            s.JustEnded = false;
            s.GatesJustShut = 0;

            // The result, held. The clock still runs here — it is what decides when the next
            // round deals — but nothing about the fight can change any more. The arena stays
            // as it was shut: a result shown in a room that has just sprung open again reads
            // as a bug rather than an ending.
            if (s.Phase == MatchPhase.Over)
            {
                s.OverFrame++;
                s.WantsRestart = cfg.RestartFrames > 0 && s.OverFrame >= cfg.RestartFrames;
                return;
            }

            // A death ends it wherever the clock had got to. Both at once is a draw, which is
            // rare and has to be decided rather than raced for.
            if (fighters != null && fighters.Length >= 2)
            {
                bool a = fighters[0].Dead, b = fighters[1].Dead;
                if (a || b)
                {
                    End(ref s, a && b ? 2 : a ? 1 : 0);
                    return;
                }
            }

            s.Frame++;

            // Time ran out. docs/01 bounds the match, so sudden death cannot be the last word:
            // the one left standing with more of their health is the one who won the fight that
            // happened, and level pegging is the draw it looks like. Decided from the state
            // rather than from who got there first, so both devices reach the same verdict.
            if (cfg.SuddenDeathFrames > 0 && s.Frame >= TotalFrames(in cfg))
            {
                int winner = 2;
                if (fighters != null && fighters.Length >= 2)
                {
                    int ha = fighters[0].Health, hb = fighters[1].Health;
                    winner = ha == hb ? 2 : ha > hb ? 0 : 1;
                }
                End(ref s, winner);
                return;
            }

            // The arena before the bodies. A shutter that is counted as shut only after the
            // fighters have moved is a shutter they walk through on the tick it closes.
            uint shut = GatesShutAt(s.Frame, gates, in cfg);
            s.GatesJustShut = shut & ~s.GatesShut;
            s.GatesShut = shut;

            MatchPhase want = PhaseAt(s.Frame, in cfg);
            if (want == s.Phase) return;

            Enter(ref s, want);

            // One pulse, taken on the tick the siren begins and then left alone. Reading the
            // positions again every tick would turn a direction into a tracker, which is the
            // one thing docs/01 does not give you.
            if (want == MatchPhase.Siren && fighters != null && fighters.Length >= 2)
                s.SirenDirection =
                    (sbyte)(fighters[1].Position.X.Raw >= fighters[0].Position.X.Raw ? 1 : -1);
        }

        /// <summary>
        /// Which gates are shut on a given tick. Pure, so a test can ask it directly and a
        /// rollback can rebuild the arena from the frame number alone.
        ///
        /// The closing gates are spread evenly across the shutter window in the order the
        /// array lists them, which the baker sorts: docs/01 says the outer rooms go "one after
        /// another", and evenly is the only spacing that needs no second table. Sudden death's
        /// gates — the ceiling of the central hall — all go on its first tick.
        /// </summary>
        public static uint GatesShutAt(int frame, GateDef[] gates, in MatchConfig cfg)
        {
            if (gates == null || gates.Length == 0) return 0;

            uint mask = 0;
            int count = gates.Length < MaxGates ? gates.Length : MaxGates;

            for (int i = 0; i < count; i++)
                if (frame >= GateShutsAt(i, gates, in cfg)) mask |= 1u << i;

            return mask;
        }

        /// <summary>How many gates one arena may have: one bit each in a single word.</summary>
        public const int MaxGates = 32;

        /// <summary>
        /// The tick one gate shuts on. Pure, and the only place the schedule is written, so
        /// presentation can start a shutter sliding so that it ARRIVES as the gate goes solid
        /// rather than beginning to move once it already has.
        /// </summary>
        public static int GateShutsAt(int gate, GateDef[] gates, in MatchConfig cfg)
        {
            if (gates == null || gate < 0 || gate >= gates.Length) return int.MaxValue;

            int closingAt = cfg.CageFrames + cfg.ScavengeFrames + cfg.SirenFrames +
                            cfg.HuntFrames;

            if (gates[gate].When == GateWhen.SuddenDeath) return closingAt + cfg.ClosingFrames;

            // Evenly across the shutter window, in the order the array lists them — which the
            // baker sorts. docs/01 says the outer rooms go "one after another"; evenly is the
            // only spacing that needs no second table to say how.
            int closing = 0, nth = 0;
            for (int i = 0; i < gates.Length; i++)
            {
                if (gates[i].When != GateWhen.Closing) continue;
                closing++;
                if (i <= gate) nth = closing;
            }

            if (closing == 0) return closingAt;

            int window = cfg.ShutterFrames > 0 ? cfg.ShutterFrames : cfg.ClosingFrames;
            return closingAt + nth * window / closing;
        }

        /// <summary>The tick the match is over by, come what may.</summary>
        public static int TotalFrames(in MatchConfig cfg) =>
            cfg.CageFrames + cfg.ScavengeFrames + cfg.SirenFrames + cfg.HuntFrames +
            cfg.ClosingFrames + cfg.SuddenDeathFrames;

        static void End(ref MatchSimState s, int winner)
        {
            s.Winner = winner;
            s.OverFrame = 0;
            s.WantsRestart = false;
            Enter(ref s, MatchPhase.Over);
            s.JustEnded = true;
        }

        /// <summary>
        /// The tick a phase begins on. Pure, and the inverse of <see cref="PhaseAt"/>, so
        /// presentation can work out how far through a phase it is without being told.
        /// </summary>
        public static int PhaseStart(MatchPhase phase, in MatchConfig cfg)
        {
            int at = 0;
            if (phase <= MatchPhase.Cages) return at;

            at += cfg.CageFrames;
            if (phase == MatchPhase.Scavenge) return at;

            at += cfg.ScavengeFrames;
            if (phase == MatchPhase.Siren) return at;

            at += cfg.SirenFrames;
            if (phase == MatchPhase.Hunt) return at;

            at += cfg.HuntFrames;
            if (phase == MatchPhase.Closing) return at;

            at += cfg.ClosingFrames;
            return at;                       // sudden death, and Over has no tick of its own
        }

        /// <summary>Which phase a given tick falls in. Pure, so a test can ask it directly.</summary>
        public static MatchPhase PhaseAt(int frame, in MatchConfig cfg)
        {
            int at = cfg.CageFrames;
            if (frame < at) return MatchPhase.Cages;

            at += cfg.ScavengeFrames;
            if (frame < at) return MatchPhase.Scavenge;

            at += cfg.SirenFrames;
            if (frame < at) return MatchPhase.Siren;

            at += cfg.HuntFrames;
            if (frame < at) return MatchPhase.Hunt;

            at += cfg.ClosingFrames;
            if (frame < at) return MatchPhase.Closing;

            return MatchPhase.SuddenDeath;
        }

        static void Enter(ref MatchSimState s, MatchPhase phase)
        {
            s.Phase = phase;
            s.PhaseChanged = true;
        }
    }
}

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

        /// <summary>Somebody won.</summary>
        Over = 6
    }

    /// <summary>
    /// How long each phase runs, in ticks. Straight off the table in docs/01, which is written
    /// in clock time; sixty of these is a second.
    /// </summary>
    public struct MatchConfig
    {
        public int CageFrames;
        public int ScavengeFrames;
        public int SirenFrames;
        public int HuntFrames;
        public int ClosingFrames;

        public static MatchConfig Default() => new MatchConfig
        {
            CageFrames     = 180,    // 0:00-0:03  the doors
            ScavengeFrames = 1320,   // 0:03-0:25  alone with the chests
            SirenFrames    = 90,     // 0:25       one pulse, 1.5 s
            HuntFrames     = 3000,   // 0:25-1:15  you know the direction
            ClosingFrames  = 2700,   // 1:15-2:00  the rooms shut
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

        /// <summary>Output of the last tick, for presentation to react to exactly once.</summary>
        public bool PhaseChanged;
        public bool JustEnded;

        public static MatchSimState Fresh() => new MatchSimState
        {
            Frame = 0, Phase = MatchPhase.Cages, Winner = -1,
            PhaseChanged = true, JustEnded = false
        };

        /// <summary>Can a fighter act yet. In the cage, no: the doors are still coming up.</summary>
        public bool InputAllowed => Phase != MatchPhase.Cages && Phase != MatchPhase.Over;

        /// <summary>How far through the cage doors opening, 0 to 1. Presentation drives on it.</summary>
        public Fix CageProgress(in MatchConfig cfg) =>
            Phase != MatchPhase.Cages || cfg.CageFrames <= 0
                ? Fix.One
                : Fix.FromMilli(1000 * (Frame < cfg.CageFrames ? Frame : cfg.CageFrames)
                                / cfg.CageFrames);
    }

    /// <summary>Advances the clock and decides when it has stopped mattering.</summary>
    public static class MatchMotor
    {
        /// <summary>
        /// One tick of the match itself. Called once per tick for the whole match, by whoever
        /// owns the tick, with every fighter's state so a death can end it.
        /// </summary>
        public static void Step(ref MatchSimState s, in MatchConfig cfg,
                                PlayerSimState[] fighters)
        {
            s.PhaseChanged = false;
            s.JustEnded = false;

            if (s.Phase == MatchPhase.Over) return;

            // A death ends it wherever the clock had got to. Both at once is a draw, which is
            // rare and has to be decided rather than raced for.
            if (fighters != null && fighters.Length >= 2)
            {
                bool a = fighters[0].Dead, b = fighters[1].Dead;
                if (a || b)
                {
                    s.Winner = a && b ? 2 : a ? 1 : 0;
                    Enter(ref s, MatchPhase.Over);
                    s.JustEnded = true;
                    return;
                }
            }

            s.Frame++;
            MatchPhase want = PhaseAt(s.Frame, in cfg);
            if (want != s.Phase) Enter(ref s, want);
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

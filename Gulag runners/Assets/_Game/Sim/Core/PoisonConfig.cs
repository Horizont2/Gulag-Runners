namespace GulagRunners.Sim
{
    /// <summary>
    /// The rising poison: how fast it comes up and how hard it bites.
    ///
    /// docs/01 ends a match by taking the arena away. The first answer to that was steel —
    /// shutters, and then walls that grow — which closes the arena ALONG the floor and so has
    /// to decide what happens to a fighter standing where the steel is going. That is the
    /// whole of <see cref="GateMotor"/>: a shove, a stagger, and a rule that it must never
    /// kill, because a death nobody saw coming is the worst way for a 1v1 to end.
    ///
    /// This closes it from BELOW instead, and that one change removes the problem rather than
    /// solving it. Poison takes the floor away without ever moving a body: nothing is shoved,
    /// nothing is crushed, and a fighter who drowns in it did so over twenty seconds with the
    /// level in plain sight the whole time. The arena keeps its full width — which is what the
    /// parkour is for — and the question it asks stops being "where may I stand" and becomes
    /// "how high can I get, and is the loot down there worth it".
    ///
    /// Both numbers are in ticks and health, like every other config here, and
    /// <see cref="GulagRunners.Game.PoisonTuning"/> is the inspector's side of them.
    /// </summary>
    public struct PoisonConfig
    {
        /// <summary>
        /// Ticks the surface takes to climb the whole pool, measured from the first tick of
        /// the closing phase. 0 floods it instantly, which is a trap rather than a tide.
        /// </summary>
        public int RiseFrames;

        /// <summary>
        /// Ticks between bites. The damage is a SERIES of small bites rather than a rate per
        /// tick because health is an integer: a rate smaller than one point a tick either
        /// rounds to nothing or needs a fractional accumulator in the state, and the one
        /// thing rollback does not want is another number to carry.
        /// </summary>
        public int BiteFrames;

        /// <summary>Health one bite takes.</summary>
        public int Bite;

        /// <summary>
        /// Is there poison in this match at all. False leaves the pool empty for the whole
        /// round — the arena as it was before the last minute meant anything — which is what
        /// a movement sandbox (docs/11) wants from a flooded location.
        ///
        /// A switch rather than "set the damage to zero", because a flood that rises and
        /// costs nothing is the worst of both: it hides the floor and lets you stand there.
        /// </summary>
        public bool On;

        /// <summary>
        /// A tide, not a trap: 80 s to fill the pool, 4 health a second once you are in it.
        ///
        /// The rise is the closing phase's own length, so the surface arrives at the top of
        /// the pool exactly as sudden death begins — the same beat the last shutter used to
        /// fall on. Four a second is twenty-five seconds from full health, which is long
        /// enough to cross a flooded floor for a chest and short enough that doing it twice
        /// is the reason you lost.
        /// </summary>
        public static PoisonConfig Default() => new PoisonConfig
        {
            RiseFrames = 4800,     // 1:00-2:20, the whole closing phase
            BiteFrames = 30,       // twice a second
            Bite = 2,
            On = true,
        };
    }
}

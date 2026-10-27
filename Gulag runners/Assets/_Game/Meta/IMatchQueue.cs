namespace GulagRunners.Meta
{
    /// <summary>Where a search has got to. The screen shows a different thing in each.</summary>
    public enum QueueState
    {
        /// <summary>Not looking.</summary>
        Idle = 0,

        /// <summary>In the pool, waiting to be paired.</summary>
        Looking = 1,

        /// <summary>Paired. <see cref="IMatchQueue.TryTake"/> has the details, once.</summary>
        Found = 2,

        /// <summary>No pairing is coming: no network, no service, signed out.</summary>
        Failed = 3,
    }

    /// <summary>
    /// Who you were paired with, and everything the match needs to start the same on
    /// both devices.
    ///
    /// <see cref="Seed"/> is the one that has to be here rather than agreed later. The
    /// simulation is deterministic, so the arena is not sent -- it is DEALT, from a
    /// number, and two devices dealing their own are playing two different matches that
    /// look fine on both screens. Whoever does the pairing decides it, because that is
    /// the one moment both ends are listening to the same voice.
    /// </summary>
    public struct MatchFound
    {
        /// <summary>Which fighter this device drives. 0 or 1, and the pairer decides.</summary>
        public int LocalPlayer;

        /// <summary>The number this match's loot is dealt from.</summary>
        public uint Seed;

        /// <summary>Who they are, for the screen to show for a beat.</summary>
        public string OpponentName;

        /// <summary>What the ladder thought of them when the match began.</summary>
        public double OpponentRating;

        /// <summary>A practice opponent rather than a person. docs/09 says so out loud.</summary>
        public bool AgainstBot;

        /// <summary>
        /// How to reach them, in whatever the transport understands: an address on a
        /// local network, a join code on a relay. Opaque here on purpose -- the queue
        /// knows who, the transport knows how, and neither needs the other's half.
        /// </summary>
        public string Join;
    }

    /// <summary>
    /// The pool: the thing that takes a player who wants a match and hands back another
    /// player who wants one.
    ///
    /// The RULES of who may meet whom are already written and already tested --
    /// <see cref="Matchmaker"/> widens the band from 60 to 400 over twenty-five seconds,
    /// <see cref="Ladder.MayMeet"/> refuses to hand a beginner to a professional however
    /// empty the queue is, and ladder_test.py asserts both against docs/09. None of that
    /// is in here, and none of it changes when the pool does.
    ///
    /// What IS in here is where the other player comes from. Today that is nowhere -- a
    /// bot, offered by the same rules -- and tomorrow it is a service. The screen asks
    /// the same three questions either way, which is the entire reason this is an
    /// interface and not an if.
    /// </summary>
    public interface IMatchQueue
    {
        /// <summary>Where the search has got to.</summary>
        QueueState State { get; }

        /// <summary>A sentence for the screen, when there is something worth saying.</summary>
        string Status { get; }

        /// <summary>Join the pool. Called once, when the player presses PLAY.</summary>
        void Begin(double rating, int matchesPlayed);

        /// <summary>
        /// Advance the search. Called every frame while looking, with the seconds since
        /// it began -- because the band widens with time and the pool is the thing that
        /// knows what to do about it.
        /// </summary>
        void Poll(float seconds);

        /// <summary>
        /// The pairing, once. True exactly one time per search: a screen that read it
        /// twice would load two matches.
        /// </summary>
        bool TryTake(out MatchFound found);

        /// <summary>Leave the pool. Must be safe when not in it.</summary>
        void Cancel();
    }
}

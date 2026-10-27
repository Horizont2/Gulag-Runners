using UnityEngine;

namespace GulagRunners.Meta
{
    /// <summary>
    /// The pool with nobody in it: everybody is paired with a practice opponent.
    ///
    /// This is not a stub standing in for the real thing, and it does not go away when a
    /// service arrives. docs/09 makes the first five matches bots BY DESIGN and says so
    /// to the player's face, and it is equally honest about the online being thin at the
    /// start: twenty-five seconds of an empty queue is a player deciding the game is
    /// dead, and a bot they were told about is better than a queue they were not. Both
    /// of those remain true with a hundred people online at three in the morning.
    ///
    /// So this is the floor every other pool falls back to, and it applies exactly the
    /// rules in <see cref="Matchmaker"/> -- immediately while placing, and otherwise
    /// only once the net is as wide as it goes AND the clock has run out, so a bot is
    /// never the answer to a search that simply had not finished looking.
    ///
    /// A plain object and not a component: there is nothing here for a designer to set
    /// that is not already on the search screen's own Rules, and a MonoBehaviour would
    /// be one more thing to wire into a scene to get the behaviour the scene already
    /// has.
    /// </summary>
    public sealed class BotQueue : IMatchQueue
    {
        readonly SearchRules _rules;
        readonly uint _seed;

        MatchFound _found;
        bool _waiting;
        int _played;

        public BotQueue(SearchRules rules, uint seed = 0)
        {
            _rules = rules;

            // A bot match still needs a number to deal from, and it may as well be a
            // different one each time: the practice arena being the same six chests
            // every match is how a player learns the route instead of the game.
            _seed = seed != 0
                ? seed
                : unchecked((uint)System.DateTime.UtcNow.Ticks) * 2654435761u + 1u;
        }

        public QueueState State { get; private set; } = QueueState.Idle;

        public string Status { get; private set; } = "";

        public void Begin(double rating, int matchesPlayed)
        {
            _played = matchesPlayed;
            _waiting = false;
            State = QueueState.Looking;
            Status = "";
        }

        public void Poll(float seconds)
        {
            if (State != QueueState.Looking || _waiting) return;
            if (!Matchmaker.OfferBot(_rules, _played, seconds)) return;

            bool placing = Ladder.PoolFor(_played) == Pool.Training;

            _found = new MatchFound
            {
                // The bot is always the second fighter, because the scene is built that
                // way: fighter one has the controls and fighter two has the brain.
                LocalPlayer = 0,
                Seed = _seed,
                OpponentName = placing ? "TRAINING OPPONENT" : "A BOT, WHILE THE QUEUE IS THIN",

                // Scored against a middling, settled player rather than against yourself:
                // a ladder that marks your own homework swings wildly on a bot that was
                // never trying. MatchRecorder holds the same number and the same reason.
                OpponentRating = 1500,
                AgainstBot = true,
                Join = "",
            };

            _waiting = true;
            State = QueueState.Found;
            Status = _found.OpponentName;
        }

        public bool TryTake(out MatchFound found)
        {
            found = _found;
            if (!_waiting) return false;
            _waiting = false;
            return true;
        }

        public void Cancel()
        {
            _waiting = false;
            State = QueueState.Idle;
            Status = "";
        }
    }
}

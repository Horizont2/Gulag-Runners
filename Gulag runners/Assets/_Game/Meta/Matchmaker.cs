using UnityEngine;

namespace GulagRunners.Meta
{
    /// <summary>What the search screen is doing. The bar means a different thing in each.</summary>
    public enum SearchPhase
    {
        /// <summary>Looking. The bar is how far the net has been cast, not progress.</summary>
        Searching = 0,

        /// <summary>Somebody was found. Held a beat, long enough to read who.</summary>
        Found = 1,

        /// <summary>The arena is loading here. The bar is the real load progress.</summary>
        Loading = 2,

        /// <summary>Loaded here, not there. The bar is full and the wait is theirs.</summary>
        Waiting = 3,

        /// <summary>Both in. The scene is let through.</summary>
        Starting = 4,

        /// <summary>The player left.</summary>
        Cancelled = 5,
    }

    /// <summary>How wide the net is cast and how long before a bot is offered.</summary>
    [System.Serializable]
    public struct SearchRules
    {
        [Tooltip("Rating points either side accepted at the first second.")]
        public int StartBand;

        [Tooltip("How many more points are accepted for every second of waiting.")]
        public int WidenPerSecond;

        [Tooltip("The widest the net ever goes. Past this the queue is not thin, it is empty.")]
        public int MaxBand;

        [Tooltip("Seconds before a bot is offered rather than keep somebody waiting.")]
        public int BotAfterSeconds;

        public static SearchRules Default => new SearchRules
        {
            // A band of 60 is about a third of a league: close enough that the first few
            // seconds only ever pair people who belong together.
            StartBand = 60,
            WidenPerSecond = 25,
            // 400 is two leagues. Past that the match is not a match, it is a lesson.
            MaxBand = 400,
            // docs/07 is honest about the online being small at the start. Twenty-five
            // seconds of nothing is a player deciding the game is dead, and a bot they
            // were told about is better than a queue they were not.
            BotAfterSeconds = 25,
        };
    }

    /// <summary>
    /// Who may be put in a match with whom, and how patient the search is.
    ///
    /// The hard rules are <see cref="Ladder.MayMeet"/>'s and this never argues with them:
    /// the band below only ever makes the search NARROWER than the ladder allows, never
    /// wider. That ordering is the whole safety of it -- a queue that has been empty for
    /// forty seconds is exactly when somebody would be tempted to hand a beginner to a
    /// professional, and docs/09's note is that this is the one thing not to do.
    ///
    /// Arithmetic, mirrored in docs/refs/ladder_mirror.py. Unlike Glicko2 and Ladder it
    /// does lean on UnityEngine -- Mathf, and the attributes that put SearchRules in the
    /// inspector where a designer can move the numbers without a build.
    /// </summary>
    public static class Matchmaker
    {
        /// <summary>Rating points either side that are acceptable after this long.</summary>
        public static int Band(in SearchRules rules, float seconds)
        {
            int start = Mathf.Max(0, rules.StartBand);
            int max = Mathf.Max(start, rules.MaxBand);
            int widened = start + Mathf.RoundToInt(Mathf.Max(0f, seconds) *
                                                   Mathf.Max(0, rules.WidenPerSecond));
            return Mathf.Min(max, widened);
        }

        /// <summary>
        /// How far the net has been cast, 0 to 1 — and what the bar shows while searching.
        ///
        /// Not a guess at how long this will take. A search has no length, and a bar that
        /// crawls to nine tenths and stops is the oldest lie on a loading screen. This is
        /// a real measurement of a real thing: at 0 only your own rating is accepted, at 1
        /// the net is as wide as it will ever go and the next step is a bot.
        /// </summary>
        public static float Cast(in SearchRules rules, float seconds)
        {
            int start = Mathf.Max(0, rules.StartBand);
            int max = Mathf.Max(start, rules.MaxBand);
            if (max == start) return 1f;
            return Mathf.Clamp01((Band(rules, seconds) - start) / (float)(max - start));
        }

        /// <summary>
        /// May these two be put together, this many seconds into the search?
        ///
        /// Both halves have to agree, and in this order: the ladder's own rules first --
        /// nobody inside their first twenty matches ever meets a Platinum, however long
        /// the queue has been empty -- and only then the band.
        /// </summary>
        public static bool Accepts(in SearchRules rules, float seconds,
                                   int matchesA, double ratingA,
                                   int matchesB, double ratingB)
        {
            if (!Ladder.MayMeet(matchesA, ratingA, matchesB, ratingB)) return false;
            return Mathf.Abs((float)(ratingA - ratingB)) <= Band(rules, seconds);
        }

        /// <summary>
        /// Stop waiting and put a bot in.
        ///
        /// Immediately for the first five matches, because docs/09 makes those bots by
        /// design and says so to the player's face -- there is nobody to wait for. After
        /// that, only once the net is as wide as it goes AND the clock has run out, so a
        /// bot is never the answer to a search that simply had not finished looking.
        /// </summary>
        public static bool OfferBot(in SearchRules rules, int matchesPlayed, float seconds)
        {
            if (Ladder.PoolFor(matchesPlayed) == Pool.Training) return true;
            return seconds >= Mathf.Max(1, rules.BotAfterSeconds) &&
                   Cast(rules, seconds) >= 1f;
        }

        /// <summary>What the line under the phase says while searching.</summary>
        public static string Detail(double rating, float seconds, int matchesPlayed)
        {
            if (Ladder.PoolFor(matchesPlayed) == Pool.Training) return "TRAINING";
            return $"{Ladder.Name(rating).ToUpperInvariant()}  ·  {Mathf.FloorToInt(seconds)} S";
        }
    }
}

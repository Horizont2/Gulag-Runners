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

        [Tooltip("The same, for somebody inside their first five matches -- shorter, " +
                 "because a first-time player staring at a search screen is the one who " +
                 "has least reason to believe it will end.\n\n" +
                 "It used to be ZERO, in effect: the training pool was handed a bot on " +
                 "the frame PLAY was pressed and the service was never asked. That is " +
                 "what docs/29 means by a bot immediately in training, and it cost the " +
                 "game its matchmaking: on a fresh install both devices are inside their " +
                 "first five matches, so two people searching at the same moment were " +
                 "each given their own bot instead of each other.\n\n" +
                 "So it is a short wait rather than none. Ten seconds is long enough for " +
                 "a ticket to find somebody who is already in the queue and short enough " +
                 "that a player alone in it still gets a match before they wonder.")]
        public int TrainingBotAfterSeconds;

        public static SearchRules Default => new SearchRules
        {
            // A band of 60 is about a third of a league: close enough that the first few
            // seconds only ever pair people who belong together.
            StartBand = 60,
            WidenPerSecond = 25,
            // 400 is two leagues. Past that the match is not a match, it is a lesson.
            MaxBand = 400,
            // ZERO IS NEVER, for both of these, and that is the whole of "it must not
            // start me on my own". See OfferBot. The band above goes on opening instead,
            // so an empty pool is answered by looking further up and down the ladder
            // rather than by a stand-in.
            BotAfterSeconds = 0,
            TrainingBotAfterSeconds = 0,
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

            // AND IT IS NOT CAPPED. MaxBand used to be a ceiling, and a band that stops
            // growing is a queue that refuses the only other person in it: two players
            // 500 points apart, alone in the pool, waited for each other for ever while
            // the search told them both it was still looking.
            //
            // MaxBand is now the point at which the net counts as FULLY CAST -- what
            // Cast() reports and the bar fills to -- and past it the band keeps opening,
            // which is what "if there is nobody, look higher and lower" means. The hard
            // rule is still Ladder.MayMeet's and it is untouched: nobody inside their
            // first twenty matches is shown a Platinum, however empty the queue is.
            return start + Mathf.RoundToInt(Mathf.Max(0f, seconds) *
                                            Mathf.Max(0, rules.WidenPerSecond));
        }

        /// <summary>
        /// How far the net has been cast, 0 to 1 — and what the bar shows while searching.
        ///
        /// Not a guess at how long this will take. A search has no length, and a bar that
        /// crawls to nine tenths and stops is the oldest lie on a loading screen. This is
        /// a real measurement of a real thing: at 0 only your own rating is accepted,
        /// and at 1 the net is as wide as the ladder calls meaningful -- two leagues --
        /// after which it goes on opening and this stays at 1.
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
        /// Stop waiting and put a bot in -- which, at the shipped numbers, never happens.
        ///
        /// BOTH DEADLINES DEFAULT TO ZERO, AND ZERO MEANS NEVER. Starting a player alone
        /// against a bot they did not ask for is the one thing the search must not do:
        /// it reads as a match, it counts as a match, and the player cannot tell it from
        /// the real thing until the opponent does something no person would. If the pool
        /// is empty the answer is to keep looking, higher and lower by rating, which is
        /// what the uncapped band in Band() is for.
        ///
        /// The code stays because the deadline is a number somebody may want: a build
        /// handed to playtesters with no servers behind it is a build where a practice
        /// opponent after thirty seconds is kinder than a spinner. Set the number and it
        /// comes back.
        ///
        /// This is NOT the substitution in NetMatch. An opponent who drops in the middle
        /// of a round is a match already in progress, and finishing it against a bot is
        /// better than abandoning it; that one is untouched.
        ///
        /// Below: the training pool on its own clock, and otherwise only once the net is
        /// as wide as MaxBand calls cast AND the clock has run out, so a
        /// bot is never the answer to a search that simply had not finished looking.
        /// </summary>
        public static bool OfferBot(in SearchRules rules, int matchesPlayed, float seconds)
        {
            // Zero is never, and it is the shipped value for both.
            if (Ladder.PoolFor(matchesPlayed) == Pool.Training)
                return rules.TrainingBotAfterSeconds > 0 &&
                       seconds >= rules.TrainingBotAfterSeconds;

            return rules.BotAfterSeconds > 0 &&
                   seconds >= rules.BotAfterSeconds &&
                   Cast(rules, seconds) >= 1f;
        }

        /// <summary>What the line under the phase says while searching.</summary>
        public static string Detail(in SearchRules rules, double rating,
                                   float seconds, int matchesPlayed)
        {
            // The band, because that is the thing that is actually happening. A player
            // watching a search wants to know it is still doing something, and "plus or
            // minus three hundred points" says what "still looking" does not: it is
            // looking FURTHER than it was a moment ago.
            string band = $"±{Band(rules, seconds)}";

            if (Ladder.PoolFor(matchesPlayed) == Pool.Training)
                return $"TRAINING  ·  {band}  ·  {Mathf.FloorToInt(seconds)} S";

            return $"{Ladder.Name(rating).ToUpperInvariant()}  ·  {band}  ·  "
                   + $"{Mathf.FloorToInt(seconds)} S";
        }
    }
}

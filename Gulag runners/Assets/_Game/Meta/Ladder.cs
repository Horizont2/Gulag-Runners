using System;

namespace GulagRunners.Meta
{
    /// <summary>The six docs/09 names, lowest first.</summary>
    public enum League
    {
        Bronze = 0,
        Silver = 1,
        Gold = 2,
        Platinum = 3,
        Diamond = 4,

        /// <summary>The top of the region. docs/09 calls it Втікач -- the one who got out.</summary>
        Runner = 5,
    }

    /// <summary>Who a player is allowed to meet, by how many matches they have played.</summary>
    public enum Pool
    {
        /// <summary>Matches 1-5. Bots, and SAID to be bots.</summary>
        Training = 0,

        /// <summary>Matches 6-20. Other newcomers, and never anybody Platinum or above.</summary>
        Newcomers = 1,

        /// <summary>Match 21 onwards.</summary>
        Open = 2,
    }

    /// <summary>
    /// The league a rating falls in, and who may meet whom.
    ///
    /// The player is never shown the number. docs/09 is explicit about it and the reason is
    /// not decoration: a visible rating is a thing to blame a team-mate for, and in a 1v1 it
    /// is a thing to blame yourself for after one unlucky chest. A league moves rarely enough
    /// to read as a standing rather than as a score.
    ///
    /// Three divisions inside each league, counted DOWN -- Silver III is the bottom of
    /// silver, Silver I the top -- because that is the convention every player already knows.
    /// </summary>
    public static class Ladder
    {
        /// <summary>The bottom of each league, on the ordinary scale. Bronze has no floor.</summary>
        public static readonly int[] Floors = { 0, 1300, 1500, 1700, 1900, 2100 };

        /// <summary>How wide a league is, for working out the division inside it.</summary>
        public const int Band = 200;

        /// <summary>Divisions in a league, counted down: 3 is the bottom.</summary>
        public const int Divisions = 3;

        public static League LeagueOf(double rating)
        {
            for (int i = Floors.Length - 1; i >= 0; i--)
                if (rating >= Floors[i]) return (League)i;
            return League.Bronze;
        }

        /// <summary>3, 2 or 1 -- the bottom, middle or top third of the league.</summary>
        public static int DivisionOf(double rating)
        {
            League league = LeagueOf(rating);

            // Runner is the top of the ladder and has no ceiling to divide, and bronze has no
            // floor; both are measured from the edge they DO have.
            if (league == League.Runner) return 1;

            int floor = Floors[(int)league];
            double into = league == League.Bronze
                ? Math.Max(0.0, rating - (Floors[1] - Band))
                : rating - floor;

            int step = (int)(into / (Band / (double)Divisions));
            return Divisions - Math.Min(Divisions - 1, Math.Max(0, step));
        }

        /// <summary>"Silver III". What the menu shows where the number would be.</summary>
        public static string Name(double rating)
        {
            League league = LeagueOf(rating);
            if (league == League.Runner) return "Runner";
            return $"{league} {Roman(DivisionOf(rating))}";
        }

        /// <summary>
        /// How far through the current league a rating is, 0 to 1 — the bar under the name.
        /// Runner is always full: there is nothing above it to fill towards.
        /// </summary>
        public static double Progress(double rating)
        {
            League league = LeagueOf(rating);
            if (league == League.Runner) return 1.0;

            double floor = league == League.Bronze ? Floors[1] - Band : Floors[(int)league];
            double share = (rating - floor) / Band;
            return share < 0.0 ? 0.0 : share > 1.0 ? 1.0 : share;
        }

        public static Pool PoolFor(int matchesPlayed) =>
            matchesPlayed < 5 ? Pool.Training :
            matchesPlayed < 20 ? Pool.Newcomers : Pool.Open;

        /// <summary>
        /// May these two be put in the same match?
        ///
        /// The whole point of the ranking, in one function. docs/09's note is "professionals
        /// wont fight with starting players all the game", and the protection it asks for is
        /// specific: inside the first twenty matches a newcomer is never shown anybody
        /// Platinum or above, however the matchmaker is feeling about queue times.
        /// </summary>
        public static bool MayMeet(int matchesA, double ratingA, int matchesB, double ratingB)
        {
            Pool a = PoolFor(matchesA), b = PoolFor(matchesB);

            // Training is bots only -- there is no human on the other side to allow.
            if (a == Pool.Training || b == Pool.Training) return false;

            if (a == Pool.Newcomers && LeagueOf(ratingB) >= League.Platinum) return false;
            if (b == Pool.Newcomers && LeagueOf(ratingA) >= League.Platinum) return false;

            return true;
        }

        static string Roman(int division) => division switch
        {
            1 => "I",
            2 => "II",
            _ => "III"
        };
    }
}

namespace GulagRunners.Meta
{
    /// <summary>What one match paid, broken down so a results screen can show the lines.</summary>
    public struct Reward
    {
        public int Played;
        public int Won;
        public int FirstToday;
        public int Dailies;
        public int WeeklyStreak;

        public int Total => Played + Won + FirstToday + Dailies + WeeklyStreak;
    }

    /// <summary>
    /// Gold: where it comes from and what it may buy.
    ///
    /// The rates are docs/09's, to the coin, and the shape of them is the design. A match
    /// pays whether it is WON or not, because a ladder that only pays winners stops the
    /// weaker half playing, and a 1v1 with nobody in the bottom half is a 1v1 with nothing
    /// in the queue. Winning pays more, and the dailies pay most of all -- they are the part
    /// that teaches the mechanics, and docs/09 is clear that a lesson nobody is paid to take
    /// is a lesson nobody takes.
    ///
    /// Nothing here sells power. That is the iron rule of docs/09 and the reason this class
    /// has no method that takes a fighter: gold cannot reach the arena, so there is nothing
    /// to pass it.
    /// </summary>
    public static class Purse
    {
        public const int PerMatch = 10;
        public const int PerWin = 15;
        public const int FirstOfDay = 25;
        public const int PerDaily = 40;
        public const int WeeklyStreak = 150;

        /// <summary>docs/09's three a day.</summary>
        public const int DailiesPerDay = 3;

        /// <summary>
        /// What the gold is FOR, in one place. docs/09: a T1 skin at 800 and a T3 at 3000,
        /// which against a day of 250-350 is three days and ten.
        ///
        /// The prices and the rates have to live beside each other or they drift: a rate
        /// quietly changed by one coin moves both of these and nobody notices until a skin
        /// takes a fortnight.
        /// </summary>
        public static int SkinPrice(int tier) => tier switch
        {
            <= 1 => 800,
            2 => 1600,
            _ => 3000,
        };

        public static Reward ForMatch(bool won, bool firstToday, int dailiesJustDone,
                                      bool weeklyStreakHit)
        {
            if (dailiesJustDone < 0) dailiesJustDone = 0;
            if (dailiesJustDone > DailiesPerDay) dailiesJustDone = DailiesPerDay;

            return new Reward
            {
                Played = PerMatch,
                Won = won ? PerWin : 0,
                FirstToday = firstToday ? FirstOfDay : 0,
                Dailies = dailiesJustDone * PerDaily,
                WeeklyStreak = weeklyStreakHit ? WeeklyStreak : 0,
            };
        }

        /// <summary>
        /// What a day looks like at the pace docs/09 aims for: 250-350 gold.
        ///
        /// Kept here as a function rather than a comment so it can be ASSERTED. The prices
        /// hang off it -- a T1 skin is 800 and a T3 is 3000, which is three days and ten --
        /// and a rate quietly changed by one coin moves both without anybody noticing.
        /// </summary>
        public static int DayAt(int matches, int wins, int dailiesDone) =>
            matches * PerMatch + wins * PerWin + (matches > 0 ? FirstOfDay : 0) +
            dailiesDone * PerDaily;
    }
}

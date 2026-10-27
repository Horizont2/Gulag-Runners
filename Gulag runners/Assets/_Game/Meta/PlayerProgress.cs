using UnityEngine;

namespace GulagRunners.Meta
{
    /// <summary>
    /// What this player has done so far, and the only thing that writes it.
    ///
    /// <see cref="Glicko2"/>, <see cref="Ladder"/> and <see cref="Purse"/> are pure
    /// arithmetic with no Unity in them, which is what lets the mirror in
    /// docs/refs/ladder_mirror.py check them outside the editor. This is the other half:
    /// the numbers that persist, the one place they change, and the bridge to the menu.
    ///
    /// Saved in PlayerPrefs. That is not a save SYSTEM and is not pretending to be one --
    /// docs/09 ends at an account on a server -- but a ladder that forgets itself when the
    /// app closes cannot be played with for an evening, and an evening is what tells you
    /// whether the placement really takes ten matches.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerProgress : MonoBehaviour
    {
        public static PlayerProgress Instance { get; private set; }

        [Header("Who")]
        public string playerName = "RUNNER_7";

        [Header("Purse")]
        [Tooltip("docs/09: gold only ever buys cosmetics. Nothing in the arena reads it.")]
        public int gold;

        [Header("Ladder — hidden from the player, shown only as a league")]
        [Tooltip("The Glicko-2 rating. docs/09 is explicit that this number is never shown: " +
                 "a visible rating is a thing to blame yourself for after one unlucky chest.")]
        public double rating = 1500;

        [Tooltip("How unsure the ladder is. 350 is a new player, 50-ish is a settled one.")]
        public double deviation = 350;

        public double volatility = 0.06;

        [Header("History")]
        public int matchesPlayed;
        public int matchesWon;

        [Tooltip("Rounds of the day's three that are done. Reset by the day rolling over.")]
        [Range(0, 3)] public int dailiesDone;

        [Tooltip("The day the last match was played, as a day number. -1 means never.")]
        public int lastPlayedDay = -1;

        [Tooltip("Days in a row with at least one match played. docs/09's weekly streak " +
                 "pays 150 gold every seventh, and before this nothing counted the days " +
                 "— the largest single payout in the economy was unreachable.")]
        public int daysInARow;

        [Header("Saving")]
        [Tooltip("Write to PlayerPrefs on every change, and read it back on Awake.")]
        public bool persist = true;

        [Header("Debug")]
        [Tooltip("What the last read or write did. Read it when a save seems to have " +
                 "gone missing.")]
        public string saveState = "not loaded yet";

        const string Key = "gr.progress.";

        /// <summary>
        /// What the saved shape is. Raise it when a field changes meaning, and teach
        /// <see cref="Load"/> how to read the one before — a save that silently loads as
        /// something else is worse than one that fails to load at all.
        /// </summary>
        const int Version = 2;

        /// <summary>Days of play in a row that earn docs/09's weekly streak.</summary>
        public const int DaysInAStreak = 7;

        static readonly System.DateTime Epoch =
            new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

        /// <summary>
        /// Today, as a day number. The ONE definition of a day in the project.
        ///
        /// It counts from a fixed date rather than folding the year and the day-of-year
        /// together, because a streak has to survive the 31st of December: any scheme
        /// where New Year's Day is not exactly one more than New Year's Eve drops every
        /// player's streak once a year, and does it in the week they are most likely to
        /// be playing.
        ///
        /// UTC, so the number does not change when the player crosses a time zone —
        /// which would otherwise hand out "first match of the day" twice for one flight.
        /// </summary>
        public static int Today => (int)(System.DateTime.UtcNow.Date - Epoch).TotalDays;

        /// <summary>The league to show, and how far through it this player is.</summary>
        public string LeagueName => matchesPlayed < 5
            ? "Training" : Ladder.Name(rating);

        public float LeagueProgress => (float)Ladder.Progress(rating);

        /// <summary>docs/09 protects the first twenty. Below that, show the count instead.</summary>
        public int PlacementLeft => Mathf.Max(0, 20 - matchesPlayed);

        public Rating AsRating => new Rating
        {
            Value = rating, Deviation = deviation, Volatility = volatility,
        };

        void Awake()
        {
            Instance = this;
            if (persist) Load();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// A phone does not close an app, it stops it — and a stopped app is killed when
        /// the system wants the memory back, with no further warning and no OnApplicationQuit.
        /// So the moment the player swipes away from the game is the last moment anything
        /// can be written, and it is written then.
        /// </summary>
        void OnApplicationPause(bool paused)
        {
            if (paused && persist) Save();
        }

        void OnApplicationQuit()
        {
            if (persist) Save();
        }

        /// <summary>
        /// One match is over. This is the whole of the meta-game's write path.
        ///
        /// The order matters: the ladder is settled against who the opponent WAS at the
        /// start of the match, and the gold is counted from the same result. Doing it the
        /// other way round pays out against a rating the match has already moved.
        /// </summary>
        public Reward RecordMatch(bool won, Rating opponent, int today, int dailiesJustDone = 0)
        {
            bool firstToday = today != lastPlayedDay;

            Rating next = Glicko2.Update(AsRating, opponent, won ? 1.0 : 0.0);
            rating = next.Value;
            deviation = next.Deviation;
            volatility = next.Volatility;

            if (firstToday) dailiesDone = 0;
            dailiesDone = Mathf.Clamp(dailiesDone + dailiesJustDone, 0, Purse.DailiesPerDay);

            // The streak is this class's to keep, not the caller's to supply. Nothing
            // outside here knows what day the last match was on, which is why the weekly
            // bonus was a parameter nobody ever passed and 150 gold nobody ever earned.
            bool streakHit = firstToday && AdvanceStreak(today);

            Reward paid = Purse.ForMatch(won, firstToday, dailiesJustDone, streakHit);
            gold += paid.Total;

            matchesPlayed++;
            if (won) matchesWon++;

            // Forward only. Writing a day that is EARLIER than the last one -- a device
            // whose date was wrong and has just been corrected -- would leave a gap
            // behind the player that their next day of play cannot close, so the streak
            // AdvanceStreak just declined to break would break tomorrow instead.
            lastPlayedDay = Mathf.Max(lastPlayedDay, today);

            if (persist) Save();
            return paid;
        }

        /// <summary>
        /// Counts the day, and says whether this one completes a week of them.
        ///
        /// A streak is days in a ROW: a day missed starts again at one. The one case that
        /// is not a judgement about the player is the clock going backwards — a device
        /// whose date was wrong and has been corrected, or a time zone crossed the hard
        /// way. That leaves the streak where it was rather than taking a week off
        /// somebody for a reason they will never work out.
        /// </summary>
        bool AdvanceStreak(int today)
        {
            if (lastPlayedDay < 0) daysInARow = 1;              // the very first match
            else if (today == lastPlayedDay + 1) daysInARow++;  // yesterday, then today
            else if (today > lastPlayedDay) daysInARow = 1;     // a day was missed
            else return false;                                  // the clock went backwards

            return daysInARow % DaysInAStreak == 0;
        }

        /// <summary>Six weeks are up. docs/09's soft reset, not a wipe.</summary>
        [ContextMenu("End the season (soft reset)")]
        public void EndSeason()
        {
            Rating next = Glicko2.SeasonReset(AsRating);
            rating = next.Value;
            deviation = next.Deviation;
            volatility = next.Volatility;
            if (persist) Save();
        }

        /// <summary>Spends gold if there is enough. True when it went through.</summary>
        public bool Spend(int cost)
        {
            if (cost <= 0 || gold < cost) return false;
            gold -= cost;
            if (persist) Save();
            return true;
        }

        [ContextMenu("Forget everything")]
        public void Wipe()
        {
            gold = 0;
            rating = 1500; deviation = 350; volatility = 0.06;
            matchesPlayed = matchesWon = dailiesDone = daysInARow = 0;
            lastPlayedDay = -1;
            if (persist) Save();
            saveState = "wiped";
        }

        [ContextMenu("Save now")]
        public void Save()
        {
            PlayerPrefs.SetInt(Key + "version", Version);
            PlayerPrefs.SetString(Key + "name", playerName);
            PlayerPrefs.SetInt(Key + "gold", gold);

            // As exact text, not as a float. The ladder's three numbers are doubles, and
            // PlayerPrefs.SetFloat throws away half of each one: 0.06 comes back as
            // 0.059999998658895493, every single load. Glicko-2 then iterates on the
            // damaged number, so a player who opens the app twice a day has a volatility
            // being walked somewhere by the save file rather than by their matches.
            Put("rating", rating);
            Put("deviation", deviation);
            Put("volatility", volatility);

            PlayerPrefs.SetInt(Key + "played", matchesPlayed);
            PlayerPrefs.SetInt(Key + "won", matchesWon);
            PlayerPrefs.SetInt(Key + "dailies", dailiesDone);
            PlayerPrefs.SetInt(Key + "day", lastPlayedDay);
            PlayerPrefs.SetInt(Key + "inarow", daysInARow);
            PlayerPrefs.Save();

            saveState = $"saved v{Version}: {gold}g, {matchesPlayed} played, "
                        + $"{daysInARow} day(s) in a row";
        }

        public void Load()
        {
            int found = PlayerPrefs.GetInt(Key + "version", 0);
            bool old = found == 0 && PlayerPrefs.HasKey(Key + "rating");

            if (found == 0 && !old)
            {
                saveState = "nothing saved yet; starting fresh";
                return;
            }

            playerName = PlayerPrefs.GetString(Key + "name", playerName);
            gold = PlayerPrefs.GetInt(Key + "gold", gold);
            matchesPlayed = PlayerPrefs.GetInt(Key + "played", matchesPlayed);
            matchesWon = PlayerPrefs.GetInt(Key + "won", matchesWon);
            dailiesDone = Mathf.Clamp(PlayerPrefs.GetInt(Key + "dailies", dailiesDone),
                                      0, Purse.DailiesPerDay);

            if (old)
            {
                // Version 1 kept the ladder as floats and the day as day-of-year plus the
                // year times four hundred. The first is lossy and the second is not a day
                // count at all -- the gap across New Year is thirty-five -- so the day is
                // dropped rather than converted. The player's next match reads as their
                // first of the day and begins a new streak, which is the honest answer
                // when the old file genuinely does not say which day it was.
                rating = PlayerPrefs.GetFloat(Key + "rating", (float)rating);
                deviation = PlayerPrefs.GetFloat(Key + "deviation", (float)deviation);
                volatility = PlayerPrefs.GetFloat(Key + "volatility", (float)volatility);
                lastPlayedDay = -1;
                daysInARow = 0;

                Save();
                saveState = $"upgraded a v1 save: {gold}g, {matchesPlayed} played";
                return;
            }

            rating = Get("rating", rating);
            deviation = Get("deviation", deviation);
            volatility = Get("volatility", volatility);
            lastPlayedDay = PlayerPrefs.GetInt(Key + "day", lastPlayedDay);
            daysInARow = Mathf.Max(0, PlayerPrefs.GetInt(Key + "inarow", daysInARow));

            saveState = $"loaded v{found}: {gold}g, {matchesPlayed} played, "
                        + $"{daysInARow} day(s) in a row";
        }

        /// <summary>
        /// A double, written so it reads back as itself. "R" round-trips, and the
        /// invariant culture is what stops a phone set to Ukrainian writing "0,06" and
        /// the parser reading six.
        /// </summary>
        static void Put(string key, double value) =>
            PlayerPrefs.SetString(Key + key,
                value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

        static double Get(string key, double fallback)
        {
            string text = PlayerPrefs.GetString(Key + key, null);
            return !string.IsNullOrEmpty(text) &&
                   double.TryParse(text, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out double value)
                ? value : fallback;
        }
    }
}

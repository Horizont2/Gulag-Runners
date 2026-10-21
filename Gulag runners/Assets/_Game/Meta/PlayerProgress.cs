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

        [Header("Saving")]
        [Tooltip("Write to PlayerPrefs on every change, and read it back on Awake.")]
        public bool persist = true;

        const string Key = "gr.progress.";

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
        /// One match is over. This is the whole of the meta-game's write path.
        ///
        /// The order matters: the ladder is settled against who the opponent WAS at the
        /// start of the match, and the gold is counted from the same result. Doing it the
        /// other way round pays out against a rating the match has already moved.
        /// </summary>
        public Reward RecordMatch(bool won, Rating opponent, int today, int dailiesJustDone = 0,
                                  bool weeklyStreakHit = false)
        {
            bool firstToday = today != lastPlayedDay;

            Rating next = Glicko2.Update(AsRating, opponent, won ? 1.0 : 0.0);
            rating = next.Value;
            deviation = next.Deviation;
            volatility = next.Volatility;

            if (firstToday) dailiesDone = 0;
            dailiesDone = Mathf.Clamp(dailiesDone + dailiesJustDone, 0, Purse.DailiesPerDay);

            Reward paid = Purse.ForMatch(won, firstToday, dailiesJustDone, weeklyStreakHit);
            gold += paid.Total;

            matchesPlayed++;
            if (won) matchesWon++;
            lastPlayedDay = today;

            if (persist) Save();
            return paid;
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
            matchesPlayed = matchesWon = dailiesDone = 0;
            lastPlayedDay = -1;
            if (persist) Save();
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key + "name", playerName);
            PlayerPrefs.SetInt(Key + "gold", gold);
            PlayerPrefs.SetFloat(Key + "rating", (float)rating);
            PlayerPrefs.SetFloat(Key + "deviation", (float)deviation);
            PlayerPrefs.SetFloat(Key + "volatility", (float)volatility);
            PlayerPrefs.SetInt(Key + "played", matchesPlayed);
            PlayerPrefs.SetInt(Key + "won", matchesWon);
            PlayerPrefs.SetInt(Key + "dailies", dailiesDone);
            PlayerPrefs.SetInt(Key + "day", lastPlayedDay);
            PlayerPrefs.Save();
        }

        public void Load()
        {
            if (!PlayerPrefs.HasKey(Key + "rating")) return;

            playerName = PlayerPrefs.GetString(Key + "name", playerName);
            gold = PlayerPrefs.GetInt(Key + "gold", gold);
            rating = PlayerPrefs.GetFloat(Key + "rating", (float)rating);
            deviation = PlayerPrefs.GetFloat(Key + "deviation", (float)deviation);
            volatility = PlayerPrefs.GetFloat(Key + "volatility", (float)volatility);
            matchesPlayed = PlayerPrefs.GetInt(Key + "played", matchesPlayed);
            matchesWon = PlayerPrefs.GetInt(Key + "won", matchesWon);
            dailiesDone = PlayerPrefs.GetInt(Key + "dailies", dailiesDone);
            lastPlayedDay = PlayerPrefs.GetInt(Key + "day", lastPlayedDay);
        }
    }
}

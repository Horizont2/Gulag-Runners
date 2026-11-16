using System.Collections.Generic;
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

        [Header("The wardrobe")]
        [Tooltip("Every cosmetic this player owns, by id, oldest first.\n\n" +
                 "Ids and not indices: a catalogue that gains an item in the middle must " +
                 "not silently hand everybody somebody else's hat. The id is the key in " +
                 "the catalogue, in the save file and in the shop, and it only has to " +
                 "agree with itself.")]
        public List<string> owned = new List<string>();

        [Tooltip("What is worn, one id per category, in the order of the Cosmetic.Slot " +
                 "enum. Empty means the default look for that slot.\n\n" +
                 "One per slot rather than a set, because a second hat is not a thing a " +
                 "fighter can wear and a shop that lets you equip two is a shop with a " +
                 "bug waiting in it.")]
        public List<string> equipped = new List<string>();

        [Tooltip("The lock, a piece at a time: one id per Cosmetic.LockPart. Empty means " +
                 "that piece's free default.\n\n" +
                 "A list of its own rather than more entries on the one above, because the " +
                 "lock is the only thing in the wardrobe built out of pieces, and folding " +
                 "five of them into a list indexed by the slot enum would make that enum " +
                 "mean two things at once.")]
        public List<string> parts = new List<string>();

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

        [Tooltip("WHICH of the day's three tasks are done: one bit per DailyTask. Reset by " +
                 "the day rolling over.\n\n" +
                 "A mask and not a count, and that is the whole of it. A count cannot say " +
                 "which, so a player who completed the same task three times was paid for " +
                 "three tasks -- 120 gold for one lesson, where docs/09 says 40 each. With " +
                 "a mask, doing it again is a bit that is already set.")]
        public int dailiesMask;

        [Tooltip("Locks picked today, across every match of it. docs/09's first task is five " +
                 "safes opened with the puzzle, and five is more than one match's worth of " +
                 "chests, so the count has to outlive the match -- and die with the day.")]
        public int picksToday;

        /// <summary>
        /// How many of the day's three are done, for the menu's "2/3".
        ///
        /// Counted back off the mask every time it is asked, and never saved. A second
        /// stored number would be a second answer to one question, and on the day the two
        /// disagree the player is paid by whichever of them the purse happened to read.
        /// </summary>
        public int DailiesDone => Dailies.Bits(dailiesMask);

        /// <summary>
        /// The same number under its old name, because Menu/MenuUI.cs reads it and C# is
        /// case sensitive, so the property above does not answer to it.
        ///
        /// A bridge and nothing else: it is read-only, so the count can no longer be
        /// written from outside, which is where the 120 gold came from. Delete it the day
        /// MenuUI asks for DailiesDone instead -- it is one word in one line there.
        /// </summary>
        public int dailiesDone => DailiesDone;

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
        ///
        /// 3 adds the wardrobe: what is owned and what is worn. A v2 save has neither,
        /// which reads correctly as a player who has bought nothing — so there is nothing
        /// to convert, and <see cref="Load"/> says so rather than leaving the reader to
        /// wonder whether something was lost.
        ///
        /// 4 narrows what is on sale. Emotes and graffiti came off — in a game where the
        /// other player cannot be seen, a gesture and a mark on a far wall are bought by
        /// nobody — and locks and poses went on, so the slot enum is REORDERED. The worn
        /// list is indexed by that enum, so a v3 save has its ids one or two places out.
        /// There is nothing to convert here either: <see cref="Wardrobe.WornIn"/> checks
        /// that a saved id belongs to the slot it was found in, so an id that has shifted
        /// reads as the slot's default, and an id for a slot that no longer exists is
        /// simply never asked for again. What was OWNED is kept: it cost gold, and a
        /// category coming back would find it still paid for.
        ///
        /// 5 adds the lock's PIECES. The lock is the one thing in the wardrobe that is
        /// built a corner at a time rather than worn whole (Cosmetic.LockPart), so it
        /// saves as its own list. A v4 save has no such list, which reads correctly as a
        /// lock with nothing fitted to it yet — every piece its free default.
        ///
        /// 6 changes what the DAY'S TASKS are saved as. Up to 5 they were a count from
        /// nought to three, and a count cannot say WHICH three -- so one task completed
        /// three times paid out three times, 120 gold for one lesson where docs/09 says 40
        /// each. What is saved now is a MASK of which tasks are done, plus the day's running
        /// count of picked locks, because docs/09's first task is five of them across a day
        /// rather than within one match. The old count is not converted and its key is not
        /// read: read as a mask, a saved 3 claims the first two tasks are finished, and they
        /// are the wrong two. Today's progress starts again instead, which is what it does
        /// every morning in any case.
        /// </summary>
        const int Version = 6;

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
        ///
        /// It takes FACTS and not conclusions. There is no won flag beside facts.Won and no
        /// count of dailies to pay for, because every conclusion drawn in here is drawn from
        /// the same four facts and from the save -- so nothing outside can pay itself, and a
        /// match cannot be a win for the gold and a loss for the rating. The dailies in
        /// particular were a number the caller passed, no caller ever passed one, and three
        /// tasks worth 120 gold a day paid nobody anything for as long as that was true.
        /// </summary>
        public Reward RecordMatch(Rating opponent, int today, in MatchFacts facts)
        {
            bool firstToday = today != lastPlayedDay;

            // docs/09 keeps bots out of the ladder, and not only the first five of them.
            // Matches 1-5 are bots "чесно підписані «ТРЕНУВАННЯ»", and a match labelled
            // TRAINING that quietly moved the player's rank would be the label lying; and
            // matchmaking rule 2 makes a bot the FALLBACK whenever no suitable human is
            // queued -- "краще бот, ніж «професіонал проти новачка»" -- which can happen at
            // match 500 as easily as at match 5. So a bot match leaves the three ladder
            // numbers exactly where they were, at either end of the ladder.
            //
            // Everything else about it is a real match: it is played, it is won or lost, it
            // pays, it counts towards the day's tasks and towards the streak. matchesPlayed
            // most of all -- docs/09's protection of the newcomer counts matches 1-5, 6-20
            // and 21+ off it, so a training match that did not count would leave the player
            // in training for ever.
            if (!facts.AgainstBot)
            {
                Rating next = Glicko2.Update(AsRating, opponent, facts.Won ? 1.0 : 0.0);
                rating = next.Value;
                deviation = next.Deviation;
                volatility = next.Volatility;
            }

            if (firstToday) NewDay();

            // Judged first, then written down, and the order is the whole of it.
            //
            // Dailies.Earned works out the day's pick total for ITSELF -- picksToday plus
            // this match's -- and changes nothing. It is a question, not a step. So the two
            // lines under it are the only place the day's counters move, and they must run
            // after the question and not before: asked the other way round, the match's
            // picks would already be in picksToday, Earned would add them a second time,
            // and three picked locks would pay out for docs/09's five.
            //
            // Which also means neither line below is redundant with anything inside Earned.
            // Deleting them because "Earned already counts the picks" would leave picksToday
            // at nought for ever, and the five-safe task could then never be finished at all.
            int earnedNow = Dailies.Earned(in facts, this);
            if (facts.LocksPicked > 0) picksToday += facts.LocksPicked;
            dailiesMask |= earnedNow;

            // The streak is this class's to keep, not the caller's to supply. Nothing
            // outside here knows what day the last match was on, which is why the weekly
            // bonus was a parameter nobody ever passed and 150 gold nobody ever earned.
            bool streakHit = firstToday && AdvanceStreak(today);

            // Paid for what this match JUST finished, never for DailiesDone. The day's
            // total includes tasks that were paid for in an earlier match, and paying by it
            // would hand over 40 gold a match for the rest of the day for a task that was
            // done once.
            Reward paid = Purse.ForMatch(facts.Won, firstToday, Dailies.Bits(earnedNow),
                                         streakHit);
            gold += paid.Total;

            matchesPlayed++;
            if (facts.Won) matchesWon++;

            // Forward only. Writing a day that is EARLIER than the last one -- a device
            // whose date was wrong and has just been corrected -- would leave a gap
            // behind the player that their next day of play cannot close, so the streak
            // AdvanceStreak just declined to break would break tomorrow instead.
            lastPlayedDay = Mathf.Max(lastPlayedDay, today);

            if (persist) Save();
            return paid;
        }

        /// <summary>
        /// Starts the day's three tasks over. The ONE place that does, so that the two
        /// halves of the day cannot be reset apart.
        ///
        /// Both of them, always. The mask says which tasks are finished and picksToday says
        /// how far the first of them has got, and a reset that drops one and keeps the other
        /// either hands the player a finished task they have not done today or counts four
        /// locks picked yesterday towards the five wanted today.
        ///
        /// It touches nothing else. lastPlayedDay and the streak are the day's OTHER
        /// business, they have their own rules about clocks that have gone backwards
        /// (see <see cref="AdvanceStreak"/>), and folding them in here would put those rules
        /// in two places.
        /// </summary>
        void NewDay()
        {
            dailiesMask = 0;
            picksToday = 0;
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

        static int Count(List<string> list) => list?.Count ?? 0;

        /// <summary>
        /// A saved list, back as a list. An empty string is an empty list and not a list
        /// holding one empty id, which is the difference between a wardrobe with nothing
        /// in it and one holding a cosmetic called "".
        /// </summary>
        static List<string> Split(string saved)
        {
            List<string> out_ = new List<string>();
            if (string.IsNullOrEmpty(saved)) return out_;
            foreach (string part in saved.Split('|'))
                if (!string.IsNullOrEmpty(part)) out_.Add(part);
            return out_;
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
            matchesPlayed = matchesWon = daysInARow = 0;
            dailiesMask = picksToday = 0;
            lastPlayedDay = -1;
            owned = new List<string>();
            equipped = new List<string>();
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
            // The MASK and the day's pick count, which are what the day actually is.
            // Version 5 saved a COUNT here and version 6 does not, because a count cannot
            // say WHICH tasks are done -- see the version note above. Saving DailiesDone
            // would be saving the popcount of the mask and losing the mask, which is the
            // same bug wearing the new field's name.
            PlayerPrefs.SetInt(Key + "mask", dailiesMask);
            PlayerPrefs.SetInt(Key + "picks", picksToday);
            PlayerPrefs.SetInt(Key + "day", lastPlayedDay);
            PlayerPrefs.SetInt(Key + "inarow", daysInARow);

            // One line each, joined by a character an id may not contain. A JSON blob here
            // would be the same data with a parser in front of it, and PlayerPrefs is a
            // string store: the simplest thing that cannot be written wrong is a list of
            // ids with a separator the ids are checked against (see CosmeticCatalogue).
            PlayerPrefs.SetString(Key + "owned", string.Join("|", owned ?? new List<string>()));
            PlayerPrefs.SetString(Key + "worn", string.Join("|", equipped ?? new List<string>()));
            PlayerPrefs.SetString(Key + "parts", string.Join("|", parts ?? new List<string>()));
            PlayerPrefs.Save();

            saveState = $"saved v{Version}: {gold}g, {matchesPlayed} played, "
                        + $"{daysInARow} day(s) in a row, {Count(owned)} owned";
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
            // The old "dailies" key is deliberately not read. It held a count, and there
            // is no honest way to turn "two of them are done" into WHICH two -- any guess
            // would hand the player a finished task they have not done, or re-charge them
            // for one they have. The day's progress is lost on the upgrade, once, and the
            // day resets anyway.
            dailiesMask = PlayerPrefs.GetInt(Key + "mask", dailiesMask) & Dailies.AllBits;
            picksToday = Mathf.Max(0, PlayerPrefs.GetInt(Key + "picks", picksToday));

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

            owned = Split(PlayerPrefs.GetString(Key + "owned", ""));
            equipped = Split(PlayerPrefs.GetString(Key + "worn", ""));
            parts = Split(PlayerPrefs.GetString(Key + "parts", ""));

            saveState = $"loaded v{found}: {gold}g, {matchesPlayed} played, "
                        + $"{daysInARow} day(s) in a row, {Count(owned)} owned"
                        + Older(found);
        }

        /// <summary>
        /// What an older save is missing, said in the load line rather than left for the
        /// reader to work out. In order, because each case is the one before it plus more:
        /// a v3 save is also short of the lock's pieces, and a v2 save is short of
        /// everything.
        /// </summary>
        static string Older(int found)
        {
            if (found >= Version) return "";
            if (found < 3)
                return $" (a v{found} save: it owned no cosmetics, which is what an empty "
                     + "wardrobe means)";
            if (found < 4)
                return $" (a v{found} save: what is worn was written against the older "
                     + "slots, so anything that no longer lines up reads as that slot's "
                     + "default, and the lock has no pieces fitted. Nothing owned was lost)";
            return $" (a v{found} save: the lock has no pieces fitted, which is what a lock "
                 + "nobody has improved looks like)";
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

using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// One line per round, written to a file, so that a playtest produces NUMBERS rather
    /// than impressions.
    ///
    /// docs/07 sets the exit criteria for M1 as four numbers and nothing in this project has
    /// ever measured any of them:
    ///
    ///     a round runs 1:40-2:30 · the win rate vs the loot budget sits inside 50 ± 6% ·
    ///     at least 60% want another · at least 50% call the blind phase tense
    ///
    /// Two of those are facts about a match and are written here. The third has a proxy that
    /// is also a fact -- whether the player pressed AGAIN on the result screen -- and it gets
    /// its own line. The fourth is a question you have to ask a human, and no amount of
    /// logging replaces it. Being clear about which is which is the point of the file: one
    /// that implied an answer to the fourth would be worse than no file at all.
    ///
    /// It counts nothing itself. <see cref="MatchTally"/> counts the match once, because the
    /// ledger that pays the player needs the same facts and two counters would be two
    /// slightly different rules for what "picked" means. This takes the figures and writes
    /// them down -- and because it listens to the tally rather than to MatchState, it cannot
    /// read a round the tally has not finished closing.
    ///
    /// Nothing identifying is written: no name, no address, no id. A row says what happened
    /// in a match and nothing whatever about who played it.
    ///
    /// `docs/refs/playtest.py` reads the file and prints docs/07's numbers from it.
    /// </summary>
    [DefaultExecutionOrder(140)]          // after the tally (120) has closed the round
    [DisallowMultipleComponent]
    public sealed class MatchLog : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The round. Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("Where the match is counted. Required: without it there is nothing to "
                 + "write down. Leave empty to find the one in the scene.")]
        public MatchTally tally;

        [Tooltip("The result screen, for the AGAIN press. Leave empty to find the one in "
                 + "the scene; an arena without one simply records no rematches.")]
        public MatchResultView result;

        [Header("Where")]
        [Tooltip("The file, under Application.persistentDataPath. One line per round, with a "
                 + "header the first time, so it opens in any spreadsheet and reads in any "
                 + "script. Use the context menu on this component to print the full path.")]
        public string fileName = "gulag-matches.csv";

        [Tooltip("Off stops recording entirely. A build handed to a stranger wants this ON: "
                 + "the whole point is that the stranger's match ends up in the file.")]
        public bool recording = true;

        [Tooltip("Also print each row to the console. Handy in the editor, noise in a build.")]
        public bool alsoLog;

        [Header("Read-only")]
        [Tooltip("The last line written, so you can watch it work without leaving Unity.")]
        public string lastRow = "";

        // ----------------------------------------------------------------- the shape of a row

        /// <summary>
        /// Three kinds of line share one file and one set of columns, which is why the first
        /// column says which kind it is. ROUND carries everything. SERIES says who took the
        /// match and whether a bot played any of it. AGAIN is the press of the rematch
        /// button -- and it is the whole reason SERIES is written at all, because a rate
        /// needs something to divide by, and the number of times a player was OFFERED
        /// another is the number of series that ended.
        ///
        /// A row is built as an array of fields and joined, rather than assembled with
        /// commas by hand. Hand-counting the blanks on a row that uses six of seventeen
        /// columns is how a file ends up one column short on one kind of line, which every
        /// reader of it then has to work around. Here a field can only be set by name.
        /// </summary>
        enum F { Kind, When, Arena, Round, Seconds, Winner, Local, Ended,
                 Chests, PickedLocks, ForcedLocks, WrongTouches,
                 Weapon0, Armour0, Weapon1, Armour1, Count }

        static readonly string[] Names =
        {
            "kind", "when", "arena", "round", "seconds", "winner", "local", "ended",
            "chests", "pickedLocks", "forcedLocks", "wrongTouches",
            "weaponTier0", "armour0", "weaponTier1", "armour1"
        };

        string _path;

        void Awake()
        {
            if (matchState == null) matchState = FindFirstObjectByType<MatchState>();
            if (tally == null) tally = FindFirstObjectByType<MatchTally>();
            if (result == null) result = FindFirstObjectByType<MatchResultView>();
            _path = Path.Combine(Application.persistentDataPath, fileName);

            // The header and the fields are two lists that have to agree, so they are made
            // to say so once rather than drift quietly and put every row one column out.
            if (Names.Length != (int)F.Count)
                Debug.LogError($"{name}: {Names.Length} column name(s) for "
                               + $"{(int)F.Count} field(s) — add the name next to the "
                               + "field in MatchLog.F.", this);

            if (tally == null)
                Debug.LogWarning($"{name}: no MatchTally in this scene, so there is nothing "
                                 + "counting the match and nothing will be written. Run "
                                 + "Tools > Gulag Runners > Record the matches.", this);
        }

        void OnEnable()
        {
            if (tally != null) tally.RoundClosed += OnRoundClosed;
            if (matchState != null) matchState.SeriesEnded += OnSeriesEnded;
            if (result != null && result.RematchAsked != null)
                result.RematchAsked.AddListener(OnRematchAsked);
        }

        void OnDisable()
        {
            if (tally != null) tally.RoundClosed -= OnRoundClosed;
            if (matchState != null) matchState.SeriesEnded -= OnSeriesEnded;
            if (result != null && result.RematchAsked != null)
                result.RematchAsked.RemoveListener(OnRematchAsked);
        }

        void OnRoundClosed(int winner)
        {
            if (!recording || matchState == null || tally == null) return;

            MatchSimState match = matchState.Match;

            // The round's length from the round's own clock. MatchSimState.Frame is reset by
            // Fresh(round), so at the end of a round it IS the round's length in ticks --
            // and taking it from the simulation rather than from a stopwatch of our own
            // means a rollback cannot make the file disagree with the match.
            float seconds = match.Frame * PlayerController.SecondsPerTick;

            string[] row = Row("round");
            Set(row, F.Round, match.Round);
            Set(row, F.Seconds, seconds.ToString("0.0", CultureInfo.InvariantCulture));
            Set(row, F.Winner, Side(winner));
            Set(row, F.Local, matchState.LocalFighter);
            Set(row, F.Ended, Ending());
            Set(row, F.Chests, tally.chestsOpened);
            Set(row, F.PickedLocks, tally.locksPicked);
            Set(row, F.ForcedLocks, tally.locksForced);
            Set(row, F.WrongTouches, tally.wrongTouches);

            // What each side was holding when it ended, which is what docs/07's second
            // criterion is actually about. It says "win rate vs the LOOT BUDGET within
            // 50 ± 6%", and a bare side-balance figure would answer a different question
            // while looking like an answer to that one: whether the left-hand cage wins too
            // often is not whether the better-armed fighter does.
            //
            // The simulation's own two numbers for a fighter's kit, with nothing invented
            // out here: the weapon's tier (ItemDef.Tier, 0 for fists) and what the three
            // protective slots add up to. They are not added together into one "budget" --
            // a tier and a permille are different units, and a composite of them would be a
            // number nobody could check.
            Set(row, F.Weapon0, tally.EndTier(0));
            Set(row, F.Armour0, tally.EndArmour(0));
            Set(row, F.Weapon1, tally.EndTier(1));
            Set(row, F.Armour1, tally.EndArmour(1));

            Write(row);
        }

        /// <summary>
        /// The match is decided, which is also the moment the player is offered another one.
        /// That offer is what the rematch rate divides by.
        /// </summary>
        void OnSeriesEnded(int winner)
        {
            if (!recording || matchState == null) return;

            string[] row = Row("series");
            Set(row, F.Winner, Side(winner));
            Set(row, F.Local, matchState.LocalFighter);
            Write(row);
        }

        /// <summary>
        /// The press of AGAIN: docs/07's "I want another" as behaviour rather than as an
        /// opinion. Its own line, because it happens after the round it is about has already
        /// been written and belongs to none of that round's figures.
        /// </summary>
        void OnRematchAsked()
        {
            if (recording) Write(Row("again"));
        }

        /// <summary>
        /// How it finished, in the simulation's own words.
        ///
        /// This used to say only "knockout" or "time", because nothing recorded what took a
        /// fighter's last point of health -- and the note that shipped with this file said
        /// so, that a guess inside a file of measurements is worse than a gap in one, and
        /// that closing it wanted a field on PlayerSimState rather than an inference out
        /// here. <see cref="DeathCause"/> is that field. A blow, a fall and the gas are now
        /// three different endings in the file, which is what they are in the match:
        /// losing to the other man and losing to the arena are not the same round.
        /// </summary>
        string Ending()
        {
            for (int i = 0; i < 2; i++)
            {
                switch (tally.DiedOf(i))
                {
                    case DeathCause.Poison: return "poison";
                    case DeathCause.Fall: return "fall";
                    case DeathCause.Blow: return "blow";
                }
            }

            // Nobody died, so the clock ran out. A fighter on zero health whom nothing
            // claims to have killed cannot reach here: the tally only records a cause for a
            // fighter whose health is gone, and death_cause_check holds the rule that
            // nothing may take a fighter's last point without saying what it was.
            return "time";
        }

        // ----------------------------------------------------------------- writing it down

        string[] Row(string kind)
        {
            string[] row = new string[(int)F.Count];
            for (int i = 0; i < row.Length; i++) row[i] = "";
            Set(row, F.Kind, kind);
            Set(row, F.When,
                System.DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture));
            Set(row, F.Arena,
                Clean(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));
            return row;
        }

        static void Set(string[] row, F field, string value) =>
            row[(int)field] = Clean(value);

        static void Set(string[] row, F field, int value) =>
            row[(int)field] = value.ToString(CultureInfo.InvariantCulture);

        static string Side(int winner) =>
            winner == 0 ? "0" : winner == 1 ? "1" : "draw";

        /// <summary>A comma in an arena's name would make a column out of nothing.</summary>
        static string Clean(string text) =>
            string.IsNullOrEmpty(text) ? "" : text.Replace(',', ' ').Replace('\n', ' ');

        void Write(string[] row)
        {
            string line = string.Join(",", row);
            lastRow = line;
            try
            {
                bool fresh = !File.Exists(_path);
                using (StreamWriter file = new StreamWriter(_path, true, Encoding.UTF8))
                {
                    if (fresh) file.WriteLine(string.Join(",", Names));
                    file.WriteLine(line);
                }
                if (alsoLog) Debug.Log($"{name}: {line}", this);
            }
            catch (IOException e)
            {
                // Said once and never again. A recorder that fills the log on a full disk is
                // a recorder somebody switches off, and then there is no file at all.
                Debug.LogWarning($"{name}: could not write {_path} — {e.Message}. "
                                 + "Recording is off from here.", this);
                recording = false;
            }
        }

        /// <summary>Where the file is, for whoever has to go and collect it.</summary>
        [ContextMenu("Where is the file?")]
        public void Where() =>
            Debug.Log($"{name}: matches are written to "
                      + Path.Combine(Application.persistentDataPath, fileName), this);
    }
}

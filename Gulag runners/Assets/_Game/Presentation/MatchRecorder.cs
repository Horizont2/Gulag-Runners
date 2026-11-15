using System.Collections.Generic;
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
    ///     a round runs 1:40-2:30 · the win rate sits inside 50 ± 6% ·
    ///     at least 60% want another · at least 50% call the blind phase tense
    ///
    /// Two of those are facts about a match and are written here. The third has a proxy that
    /// is also a fact -- whether the player pressed AGAIN on the result screen -- and it gets
    /// its own line. The fourth is a question you have to ask a human, and no amount of
    /// logging replaces it. Being clear about which is which is the point of the file: one
    /// that implied an answer to the fourth would be worse than no file at all.
    ///
    /// There is a fifth question in here that matters more to this project than any of them.
    /// docs/03 calls the puzzles the point of difference of the whole game, and nobody knows
    /// whether a player uses a lock or simply breaks the safe. Every locked chest that opens
    /// is counted as PICKED or FORCED, and those two columns are the evidence for or against
    /// the most expensive idea in the design.
    ///
    /// Nothing identifying is written: no name, no address, no id. A row says what happened
    /// in a match and nothing whatever about who played it.
    ///
    /// `docs/refs/playtest.py` reads the file and prints docs/07's numbers from it.
    ///
    /// Presentation only, and read-only against the simulation (docs/06): it listens to
    /// MatchState's events, reads chest state, and writes nothing back. Nothing in
    /// <see cref="LateUpdate"/> allocates -- the one method here that runs every frame is
    /// the one that may not, and `perf_check` is what holds that.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchRecorder : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The round. Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

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
        /// commas by hand. Hand-counting the blanks on a row that uses six of thirteen
        /// columns is how a file ends up one column short on one kind of line, which every
        /// reader of it then has to work around. Here a field can only be set by name.
        /// </summary>
        enum F { Kind, When, Arena, Round, Seconds, Winner, Local, Ended,
                 Chests, PickedLocks, ForcedLocks, WrongTouches, Substituted,
                 Weapon0, Armour0, Weapon1, Armour1, Count }

        static readonly string[] Names =
        {
            "kind", "when", "arena", "round", "seconds", "winner", "local", "ended",
            "chests", "pickedLocks", "forcedLocks", "wrongTouches", "substituted",
            "weaponTier0", "armour0", "weaponTier1", "armour1"
        };

        // ----------------------------------------------------------------- what a round held

        readonly List<int> _opened = new List<int>();
        int _picked, _forced, _wrong;
        bool _substituted;
        byte[] _stunWas = new byte[0];
        string _path;

        void Awake()
        {
            if (matchState == null) matchState = FindFirstObjectByType<MatchState>();
            if (result == null) result = FindFirstObjectByType<MatchResultView>();
            _path = Path.Combine(Application.persistentDataPath, fileName);

            // The header and the fields are two lists that have to agree, so they are made
            // to say so once rather than drift quietly and put every row one column out.
            if (Names.Length != (int)F.Count)
                Debug.LogError($"{name}: {Names.Length} column name(s) for "
                               + $"{(int)F.Count} field(s) — add the name next to the "
                               + "field in MatchRecorder.F.", this);
        }

        void OnEnable()
        {
            if (matchState != null)
            {
                matchState.RoundBegan += OnRoundBegan;
                matchState.Ended += OnEnded;
                matchState.SeriesEnded += OnSeriesEnded;
                matchState.ChestOpened += OnChestOpened;
            }

            if (result != null && result.RematchAsked != null)
                result.RematchAsked.AddListener(OnRematchAsked);

            GulagRunners.Net.NetMatch net = FindFirstObjectByType<GulagRunners.Net.NetMatch>();
            if (net != null && net.Substituted != null)
                net.Substituted.AddListener(OnSubstituted);

            Fit();
        }

        void OnDisable()
        {
            if (matchState != null)
            {
                matchState.RoundBegan -= OnRoundBegan;
                matchState.Ended -= OnEnded;
                matchState.SeriesEnded -= OnSeriesEnded;
                matchState.ChestOpened -= OnChestOpened;
            }

            if (result != null && result.RematchAsked != null)
                result.RematchAsked.RemoveListener(OnRematchAsked);

            GulagRunners.Net.NetMatch net = FindFirstObjectByType<GulagRunners.Net.NetMatch>();
            if (net != null && net.Substituted != null)
                net.Substituted.RemoveListener(OnSubstituted);
        }

        void OnRoundBegan(int round)
        {
            _opened.Clear();
            _picked = _forced = _wrong = 0;
            Fit();
        }

        /// <summary>
        /// Size the sulk-watching buffer to the arena's chests, HERE rather than in
        /// LateUpdate.
        ///
        /// MatchState.Deal() replaces ChestStates and both of its callers raise RoundBegan
        /// after it, so the count is known by the time this runs. Growing the array where it
        /// is read would be an allocation inside a method that runs sixty times a second,
        /// and `perf_check` is right to refuse that even though it would in fact happen
        /// once: a rule that accepted "it only allocates the first time" would accept every
        /// lazy cache in the project, and some of those fire every frame.
        /// </summary>
        void Fit()
        {
            int count = matchState != null && matchState.ChestStates != null
                ? matchState.ChestStates.Length : 0;
            if (_stunWas.Length != count) _stunWas = new byte[count];
            else for (int i = 0; i < _stunWas.Length; i++) _stunWas[i] = 0;
        }

        /// <summary>A bot holds the other fighter for the rest of the SERIES, not the round.</summary>
        void OnSubstituted() => _substituted = true;

        void OnEnded(int winner)
        {
            if (!recording || matchState == null) return;

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
            Set(row, F.Chests, _opened.Count);
            Set(row, F.PickedLocks, _picked);
            Set(row, F.ForcedLocks, _forced);
            Set(row, F.WrongTouches, _wrong);
            Set(row, F.Substituted, _substituted ? 1 : 0);

            // What each side was holding when it ended, which is what docs/07's second
            // criterion is actually about. It says "win rate vs the LOOT BUDGET within
            // 50 ± 6%", and a bare side-balance figure would answer a different question
            // while looking like an answer to that one: whether the left-hand cage wins too
            // often is not whether the better-armed fighter does.
            //
            // The simulation's own two numbers for a fighter's kit, with nothing invented
            // out here: the weapon's tier (ItemDef.Tier, 0 for fists) and the armour the
            // three protective slots add up to (Inventory.ArmourPermille). They are not
            // added together into one "budget" -- a tier and a permille are different units,
            // and a composite of them would be a number nobody could check.
            Loot(row, 0, F.Weapon0, F.Armour0);
            Loot(row, 1, F.Weapon1, F.Armour1);

            Write(row);
        }

        void Loot(string[] row, int index, F weapon, F armour)
        {
            if (matchState.fighters == null || index >= matchState.fighters.Count) return;
            PlayerController f = matchState.fighters[index];
            if (f == null) return;

            Inventory kit = f.State.Inventory;
            Set(row, weapon, kit.WeaponDef.Tier);
            Set(row, armour, kit.ArmourPermille);
        }

        /// <summary>
        /// The match is decided, which is also the moment the player is offered another one.
        /// That offer is what the rematch rate divides by.
        /// </summary>
        void OnSeriesEnded(int winner)
        {
            if (recording && matchState != null)
            {
                string[] row = Row("series");
                Set(row, F.Winner, Side(winner));
                Set(row, F.Local, matchState.LocalFighter);
                Set(row, F.Substituted, _substituted ? 1 : 0);
                Write(row);
            }

            _substituted = false;
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
        /// A chest came open. Which WAY it came open is the column this file exists for.
        ///
        /// Read off the chest rather than guessed. <see cref="LootMotor"/> writes the last
        /// touch in and only then opens the safe, so a lock whose touches are all made at
        /// this moment is exactly the lock the simulation itself called picked -- the same
        /// test, one frame later, with no second rule to drift away from the first.
        ///
        /// A chest with no lock on it is neither picked nor forced. It counts only in the
        /// total, because there was no lock to use, and calling it forced would quietly make
        /// the puzzles look ignored.
        /// </summary>
        void OnChestOpened(int player, int chest)
        {
            if (!recording || matchState == null) return;
            ChestSimState[] chests = matchState.ChestStates;
            if (chests == null || chest < 0 || chest >= chests.Length) return;

            // A rollback replays the frames it rolled back, so the same opening can be
            // reported more than once. Counted once.
            if (_opened.Contains(chest)) return;
            _opened.Add(chest);

            if (!chests[chest].Puzzle.Exists) return;
            if (chests[chest].Puzzle.Solved(chests[chest].PuzzleDone)) _picked++;
            else _forced++;
        }

        /// <summary>
        /// Wrong touches, counted by watching the locks sulk.
        ///
        /// There is no event for a wrong answer and there should not be one: the simulation
        /// does not raise events, it holds state, and the state says it -- the sulk counts
        /// DOWN every tick and is only ever set upwards by a mistake, so a lock whose timer
        /// went up since the last frame has just taken one. Reading a fact out of a struct
        /// is what polling is for.
        ///
        /// Approximate in one direction, and worth saying so: a rollback that restores a
        /// longer sulk can read as a second mistake. It is a measure of fumbling rather than
        /// an exact tally, and it is used as one.
        /// </summary>
        void LateUpdate()
        {
            if (!recording || matchState == null) return;
            ChestSimState[] chests = matchState.ChestStates;
            if (chests == null) return;

            // Whichever is shorter. Switched on in the middle of a round the buffer is empty
            // and this does nothing until the next round deals -- one round's fumbling
            // uncounted, rather than an allocation every frame of every round.
            int n = chests.Length < _stunWas.Length ? chests.Length : _stunWas.Length;
            for (int i = 0; i < n; i++)
            {
                byte now = chests[i].PuzzleStun;
                if (now > _stunWas[i]) _wrong++;
                _stunWas[i] = now;
            }
        }

        /// <summary>
        /// How it finished, from what is honestly knowable here: a fighter at zero health is
        /// a knockout, and anything else is the clock running out.
        ///
        /// Whether that zero came from a blow or from the gas is NOT knowable -- the
        /// simulation does not carry what last hurt a fighter -- and a guess inside a file of
        /// measurements is worse than a gap in one. If that distinction turns out to matter,
        /// it wants a field on PlayerSimState, not an inference out here.
        /// </summary>
        string Ending()
        {
            if (matchState.fighters == null) return "time";
            for (int i = 0; i < matchState.fighters.Count; i++)
            {
                PlayerController f = matchState.fighters[i];
                if (f != null && f.State.Health <= 0) return "knockout";
            }
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

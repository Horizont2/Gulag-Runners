using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The facts about a match, counted once.
    ///
    /// Two things want them and they are not the same two things. The match file (docs/40)
    /// wants a row per round for a playtest. The ledger wants a series' worth to pay the
    /// player (docs/09). Left to themselves each would count picked locks with its own
    /// slightly different rule, and MatchState's own comments say what happens then: "two
    /// places doing that is two places that can disagree". So there is one counter and two
    /// readers.
    ///
    /// Everything here is read off the simulation and nothing is written back (docs/06).
    /// Where the simulation already decides something, this reads the decision rather than
    /// deciding again -- a lock is picked when <see cref="Puzzle.Solved"/> says so, a death
    /// has the cause <see cref="DeathCause"/> gives it.
    ///
    /// Two scopes, because the two readers need different ones:
    ///
    ///   THE ROUND resets every time a round is dealt. That is one line of the match file.
    ///   THE SERIES resets when a new match begins. That is what the player is paid for,
    ///   because docs/09's tasks are about a match and not about a round of one.
    ///
    /// Nothing in <see cref="LateUpdate"/> allocates. It is the one method here that runs
    /// every frame and `perf_check` is what holds that.
    /// </summary>
    [DefaultExecutionOrder(120)]          // after the fighters have ticked
    [DisallowMultipleComponent]
    public sealed class MatchTally : MonoBehaviour
    {
        [Tooltip("The round. Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Header("Read-only — the round in progress")]
        public int chestsOpened;
        public int locksPicked;
        public int locksForced;
        public int wrongTouches;

        [Header("Read-only — the series so far")]
        public int seriesLocksPicked;
        public int seriesRoundsWon0;
        public int seriesRoundsWon1;

        /// <summary>
        /// The highest weapon tier each fighter has HELD at any point in the series, taken
        /// as a running maximum rather than read at the end.
        ///
        /// docs/09 pays for winning "without a T3 weapon", and reading the weapon only when
        /// the round ends would pay a player who found a mace, won with it, and dropped it
        /// on the last tick. The task is about how you fought, so it is sampled while you
        /// fight.
        /// </summary>
        readonly int[] _topTier = new int[2];

        /// <summary>What each fighter was holding when the round ended, for the match file.</summary>
        readonly int[] _endTier = new int[2];
        readonly int[] _endArmour = new int[2];

        /// <summary>What killed each fighter this round, or None. From the simulation.</summary>
        readonly DeathCause[] _diedOf = new DeathCause[2];

        byte[] _stunWas = new byte[0];
        readonly System.Collections.Generic.List<int> _opened =
            new System.Collections.Generic.List<int>();

        public int TopTier(int player) => In(player) ? _topTier[player] : 0;
        public int EndTier(int player) => In(player) ? _endTier[player] : 0;
        public int EndArmour(int player) => In(player) ? _endArmour[player] : 0;
        public DeathCause DiedOf(int player) => In(player) ? _diedOf[player] : DeathCause.None;

        static bool In(int player) => player >= 0 && player < 2;

        /// <summary>Locked chests opened this round, picked and forced together.</summary>
        public int LocksOpened => locksPicked + locksForced;

        void Awake()
        {
            if (matchState == null) matchState = FindFirstObjectByType<MatchState>();
        }

        void OnEnable()
        {
            if (matchState == null) return;
            matchState.RoundBegan += OnRoundBegan;
            matchState.Ended += OnRoundEnded;
            matchState.SeriesEnded += OnSeriesEnded;
            matchState.ChestOpened += OnChestOpened;

            NewSeries();
        }

        void OnDisable()
        {
            if (matchState == null) return;
            matchState.RoundBegan -= OnRoundBegan;
            matchState.Ended -= OnRoundEnded;
            matchState.SeriesEnded -= OnSeriesEnded;
            matchState.ChestOpened -= OnChestOpened;
        }

        // ----------------------------------------------------------------- the round

        void OnRoundBegan(int round)
        {
            // Round zero is a new match. Nothing else says so from in here: MatchState deals
            // round 0 both for a fresh series and for the first round of one, and the series
            // counters have to start empty either way.
            if (round == 0) NewSeries();

            chestsOpened = locksPicked = locksForced = wrongTouches = 0;
            _opened.Clear();
            for (int i = 0; i < 2; i++)
            {
                _diedOf[i] = DeathCause.None;
                _endTier[i] = 0;
                _endArmour[i] = 0;
            }

            Fit();
        }

        /// <summary>
        /// Size the sulk-watching buffer to the arena's chests HERE and not where it is read.
        ///
        /// MatchState.Deal() replaces ChestStates and both of its callers raise RoundBegan
        /// after it, so the count is known by the time this runs. Growing an array inside a
        /// method that runs sixty times a second is what `perf_check` refuses, and rightly:
        /// a rule that accepted "it only allocates the first time" would accept every lazy
        /// cache in the project, and some of those fire every frame.
        /// </summary>
        void Fit()
        {
            int count = matchState != null && matchState.ChestStates != null
                ? matchState.ChestStates.Length : 0;
            if (_stunWas.Length != count) _stunWas = new byte[count];
            else for (int i = 0; i < _stunWas.Length; i++) _stunWas[i] = 0;
        }

        void NewSeries()
        {
            seriesLocksPicked = 0;
            seriesRoundsWon0 = seriesRoundsWon1 = 0;
            for (int i = 0; i < 2; i++) _topTier[i] = 0;
        }


        void OnSeriesEnded(int winner) { }

        /// <summary>
        /// The round is over AND everything about it has been written down. The argument is
        /// the winner, as MatchState gives it.
        ///
        /// Anything that reads these figures listens to this rather than to
        /// MatchState.Ended, and that is not a style preference. Both would otherwise be
        /// handlers on the same event, and which one ran first would come down to the order
        /// they happened to subscribe in -- a reader that lost that race would write the
        /// PREVIOUS round's weapons and deaths into this round's row, correctly, every time,
        /// and nothing would look broken. Hanging the readers off the counter makes the
        /// order a fact of the code instead of a lucky accident of execution order.
        /// </summary>
        public event System.Action<int> RoundClosed;

        void OnRoundEnded(int winner)
        {
            if (winner == 0) seriesRoundsWon0++;
            else if (winner == 1) seriesRoundsWon1++;

            // What each side finished with, and what killed them, taken on the tick the
            // round is declared over rather than sampled later -- a fighter's state keeps
            // moving after that, and the file is about the round.
            for (int i = 0; i < 2; i++)
            {
                PlayerController f = Fighter(i);
                if (f == null) continue;
                Inventory kit = f.State.Inventory;
                _endTier[i] = kit.WeaponDef.Tier;
                _endArmour[i] = kit.ArmourPermille;
                if (f.State.Health <= 0) _diedOf[i] = f.State.KilledBy;
            }

            RoundClosed?.Invoke(winner);
        }

        /// <summary>
        /// A chest came open, and which WAY it came open is the fact both readers are here
        /// for: docs/03 calls the locks the point of difference of the whole game and nobody
        /// has ever known whether a player uses one.
        ///
        /// Read off the chest, not guessed. <see cref="LootMotor"/> writes the last touch in
        /// and only then opens the safe, so a lock whose touches are all made at this moment
        /// is exactly the lock the simulation itself called picked -- the same test, one
        /// frame later, with no second rule to drift from the first.
        ///
        /// A chest with no lock on it is neither picked nor forced. It counts only in the
        /// total, because there was no lock to use, and calling it forced would quietly make
        /// the puzzles look ignored.
        /// </summary>
        void OnChestOpened(int player, int chest)
        {
            if (matchState == null) return;
            ChestSimState[] chests = matchState.ChestStates;
            if (chests == null || chest < 0 || chest >= chests.Length) return;

            // A rollback replays the frames it rolled back, so the same opening can be
            // reported more than once. Counted once.
            if (_opened.Contains(chest)) return;
            _opened.Add(chest);
            chestsOpened++;

            if (!chests[chest].Puzzle.Exists) return;
            if (chests[chest].Puzzle.Solved(chests[chest].PuzzleDone))
            {
                locksPicked++;
                seriesLocksPicked++;
            }
            else locksForced++;
        }

        // ----------------------------------------------------------------- every frame

        /// <summary>
        /// Two things are only knowable by watching: the best weapon a fighter ever held,
        /// and that a lock was touched wrongly.
        ///
        /// There is no event for a wrong answer and there should not be one. The simulation
        /// does not raise events, it holds state, and the state says it: the sulk after a
        /// mistake counts DOWN every tick and is only ever set upwards by another mistake, so
        /// a lock whose timer went up since the last frame has just taken one. Reading a fact
        /// out of a struct is what polling is for.
        ///
        /// Approximate in one direction, and worth saying so: a rollback that restores a
        /// longer sulk can read as a second mistake. It is a measure of fumbling rather than
        /// an exact tally, and it is used as one.
        /// </summary>
        void LateUpdate()
        {
            if (matchState == null) return;

            for (int i = 0; i < 2; i++)
            {
                PlayerController f = Fighter(i);
                if (f == null) continue;
                int tier = f.State.Inventory.WeaponDef.Tier;
                if (tier > _topTier[i]) _topTier[i] = tier;
            }

            ChestSimState[] chests = matchState.ChestStates;
            if (chests == null) return;

            // Whichever is shorter. Switched on in the middle of a round the buffer is empty
            // and this does nothing until the next round is dealt -- one round's fumbling
            // uncounted, rather than an allocation every frame of every round.
            int n = chests.Length < _stunWas.Length ? chests.Length : _stunWas.Length;
            for (int i = 0; i < n; i++)
            {
                byte now = chests[i].PuzzleStun;
                if (now > _stunWas[i]) wrongTouches++;
                _stunWas[i] = now;
            }
        }

        PlayerController Fighter(int index)
        {
            if (matchState == null || matchState.fighters == null) return null;
            if (index < 0 || index >= matchState.fighters.Count) return null;
            return matchState.fighters[index];
        }
    }
}

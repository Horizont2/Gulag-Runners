namespace GulagRunners.Meta
{
    /// <summary>
    /// The three tasks docs/09 hands out every day, and nothing else belongs in this enum.
    ///
    /// They are not three errands somebody liked the sound of. docs/09 pays 40 gold each --
    /// more than a win, and the largest regular line in the economy -- and gives the reason
    /// in four words: "завдання вчать механікам", the tasks TEACH THE MECHANICS. Each one is
    /// something a player can play an entire evening without ever finding out:
    ///
    ///   * a lock is an OPTIMISATION and never a gate (docs/04), so a player who only ever
    ///     holds the pry button never discovers there is a quiet way in -- and the quiet way
    ///     in is the point of difference of the whole game;
    ///   * a T3 weapon settles a fight on its own, so a player who finds one learns nothing
    ///     else that match;
    ///   * and the gas (docs/33) is the arena winning rather than the other man, which most
    ///     players only ever meet from the losing side.
    ///
    /// The VALUES are saved. <see cref="PlayerProgress.dailiesMask"/> is one bit per task,
    /// shifted left by the number below, so a task may be renamed freely and MOVED never: a
    /// reordered enum hands every player mid-day somebody else's finished task, and the same
    /// 40 gold twice. A fourth task goes on the END.
    /// </summary>
    public enum DailyTask
    {
        /// <summary>
        /// docs/09's "відкрий 5 сейфів загадкою": five locks opened by picking them rather
        /// than by forcing them. Across the DAY -- see <see cref="Dailies.PicksNeeded"/>.
        /// </summary>
        PickFiveLocks = 0,

        /// <summary>
        /// "виграй без зброї T3". Win a match having never held the top tier, which is the
        /// match where the player has to use the arena instead of the mace.
        /// </summary>
        WinWithoutT3 = 1,

        /// <summary>
        /// "вбий хазардом". The gas finishes the other man: DeathCause.Poison (docs/33).
        /// </summary>
        KillWithHazard = 2,
    }

    /// <summary>
    /// What one match contributed, as the only thing the arena ever says about it.
    ///
    /// Facts and no verdicts. Nothing in here is "a daily task was completed", because that
    /// is not a question a match can answer: five picked locks is five across the DAY and
    /// not five in one go, and a task already paid for today must not be paid for again --
    /// and neither of those is anything the arena knows. So the arena reports and
    /// <see cref="Dailies.Earned"/> judges, inside <see cref="PlayerProgress.RecordMatch"/>
    /// where the save is.
    ///
    /// That split is the fix for a real hole rather than a tidiness. RecordMatch used to
    /// take a dailiesJustDone count from whoever called it, which made the payout whatever
    /// the caller said it was -- and the caller said nothing, so three tasks worth 120 gold
    /// a day have never paid anybody a single coin.
    /// </summary>
    public struct MatchFacts
    {
        /// <summary>
        /// Did the local player win? The one source for the result: the ladder, the win
        /// bonus and the second daily all read this field, so a match cannot be a win for
        /// the gold and a loss for the rating.
        /// </summary>
        public bool Won;

        /// <summary>
        /// Locks PICKED this match, never forced. The simulation's own word for it: LootMotor
        /// writes the last touch and only then opens the safe, so a lock with all its touches
        /// made (Puzzle.Solved) is one that was picked, and a chest prised open with a crowbar
        /// is not. That is the distinction the whole of docs/04 is built on, and it is why
        /// this is a count of locks and not a count of chests.
        /// </summary>
        public int LocksPicked;

        /// <summary>
        /// The highest ItemDef.Tier the local player HELD at any point this match. Zero means
        /// bare hands for the whole of it, which is the hardest way to win and counts.
        ///
        /// Highest held, not what was in hand at the end: a player who finds a T3 mace, wins
        /// with it and drops it on the last tick has won with a T3.
        /// </summary>
        public int TopWeaponTier;

        /// <summary>
        /// The opponent died of the gas -- DeathCause.Poison (docs/33). Only the simulation
        /// can say so; the note on DeathCause explains why a guess from outside is wrong in
        /// exactly the case that matters, which is a fighter the gas has been biting who is
        /// then finished by a blow.
        /// </summary>
        public bool HazardKill;

        /// <summary>
        /// Was this a bot? docs/09 keeps bots out of the ladder and the reason is in two
        /// separate places in the note: matches 1-5 are bots "чесно підписані «ТРЕНУВАННЯ»",
        /// and a match labelled TRAINING that quietly moved your rank would be the label
        /// lying; and matchmaking rule 2 makes a bot the FALLBACK whenever no suitable human
        /// is queued -- "краще бот, ніж «професіонал проти новачка»" -- which can happen at
        /// match 500 as easily as at match 5.
        ///
        /// It is a fact about the match and so it lives here, not in a checkbox on a
        /// component that somebody has to remember to tick.
        /// </summary>
        public bool AgainstBot;

        /// <summary>
        /// A match that contributed nothing: what to pass when there is nothing to report,
        /// and what to build real facts on top of.
        ///
        /// It is the struct's ZERO value, deliberately, and AgainstBot is written out rather
        /// than left to C# so that the choice is visible. A "nothing" that differs from
        /// default(MatchFacts) is a trap that fires exactly once, quietly, the day somebody
        /// writes new MatchFacts() instead -- and since nothing can stop them writing that,
        /// a safer None would only hide the problem on one of the two paths. Won is false,
        /// so None completes no task and pays no daily (see <see cref="Dailies.Earned"/>);
        /// whether a match nobody can attribute should reach the ladder at all is a decision
        /// for whoever calls RecordMatch, and one a default cannot make for them.
        /// </summary>
        public static MatchFacts None => new MatchFacts { AgainstBot = false };
    }

    /// <summary>
    /// Which of the day's three tasks are done, which ones a match has just finished, and
    /// what to call them. The rules of docs/09's dailies, in one place.
    ///
    /// The whole of this type exists because of one bug in the save. What was stored was a
    /// COUNT -- dailiesDone, a number from nought to three -- and a count cannot say WHICH,
    /// so a player who completed the same task three times was paid for three tasks: 120
    /// gold for one lesson, every day, for ever. The fix is not arithmetic and not a clamp,
    /// it is the saved SHAPE. What is stored is a mask of which tasks are finished
    /// (<see cref="PlayerProgress.dailiesMask"/>), so "again" is a bit that is already set,
    /// and the count the menu shows is counted back off the mask rather than kept beside it.
    ///
    /// Everything here is a question asked of the save and nothing here writes to it. Only
    /// <see cref="PlayerProgress"/> may move the day's state, which is the other half of the
    /// same fix: if the thing that judges a task could also mark it done, there would be two
    /// places that can pay out and one of them would eventually be wrong.
    /// </summary>
    public static class Dailies
    {
        /// <summary>
        /// docs/09's three a day. The same number as <see cref="Purse.DailiesPerDay"/>, and
        /// they have to agree: that one decides what is PAID and this one decides what is
        /// OFFERED, so a day with four tasks and a purse that pays for three is a task that
        /// silently stops being worth doing.
        /// </summary>
        public const int Count = 3;

        /// <summary>
        /// docs/09's "5 сейфів": five locks picked, across the DAY and not within one match.
        ///
        /// Across the day on purpose. Three chests with locks on them is a normal match, so
        /// five in one match would ask the player to go out of their way in a round they are
        /// also trying to win -- and the lesson docs/09 wants taught is that picking is the
        /// better way in, not that it is worth dying for. It is also why the day keeps a
        /// counter of its own (<see cref="PlayerProgress.picksToday"/>) rather than judging
        /// each match on its own.
        /// </summary>
        public const int PicksNeeded = 5;

        /// <summary>
        /// The tier docs/09's second task bars: the top of ItemTable's four (T0 bare hands up
        /// to T3), and the one that settles a fight on its own.
        /// </summary>
        public const int TopTier = 3;

        /// <summary>
        /// Every task done. It is also what a mask read off a save is filtered through, so
        /// that a saved number nobody wrote -- a hand-edited PlayerPrefs, a file from a
        /// build with four tasks in it -- cannot report more tasks finished than there are.
        /// </summary>
        public const int AllBits = (1 << Count) - 1;

        /// <summary>
        /// The three, in the order the mask indexes them by. Here once so that nothing
        /// counts the enum by hand -- the same reason CosmeticCatalogue.Slots exists.
        /// </summary>
        public static readonly DailyTask[] All =
        {
            DailyTask.PickFiveLocks, DailyTask.WinWithoutT3, DailyTask.KillWithHazard,
        };

        /// <summary>One task as its bit in the mask.</summary>
        public static int Bit(DailyTask task) => 1 << (int)task;

        /// <summary>
        /// How many tasks a mask holds. The only popcount in the meta-game, because the menu
        /// shows this number and the purse pays by it, and two of them written separately is
        /// two answers to one question.
        ///
        /// Masked with <see cref="AllBits"/> first: a stray high bit would otherwise read as
        /// a fourth finished task and be paid for.
        /// </summary>
        public static int Bits(int mask)
        {
            int bits = mask & AllBits, n = 0;
            while (bits != 0) { n += bits & 1; bits >>= 1; }
            return n;
        }

        /// <summary>
        /// What the row in the menu says. Short, because it sits beside a progress string on
        /// a phone in portrait.
        ///
        /// The five comes from <see cref="PicksNeeded"/> and the three from
        /// <see cref="TopTier"/> rather than being written into the sentence: both are
        /// docs/09's numbers, and a menu promising five while the code wants six is worse
        /// than either number on its own.
        ///
        /// A task that is not one of the three gets an empty string and not somebody else's
        /// title. An enum is an int and a save can hold any int, and an empty row in the
        /// menu is visibly wrong, where a row quietly showing the wrong task is not.
        /// </summary>
        public static string Title(DailyTask task) => task switch
        {
            DailyTask.PickFiveLocks => $"PICK {PicksNeeded} LOCKS",
            DailyTask.WinWithoutT3 => $"WIN WITHOUT A T{TopTier}",
            DailyTask.KillWithHazard => "KILL WITH THE GAS",
            _ => "",
        };

        /// <summary>
        /// How far through one task the day has got, for the end of its row: "3 / 5", or
        /// "done".
        ///
        /// Only the first task has anything to count. The other two happen in one match or
        /// not at all, so they say so: a "0 / 1" on them would invite the player to look for
        /// the half-way point of winning a match, and there isn't one.
        /// </summary>
        public static string Progress(DailyTask task, PlayerProgress p)
        {
            if (Done(task, p)) return "done";
            if (task != DailyTask.PickFiveLocks) return "not yet";

            int picks = p != null ? p.picksToday : 0;
            if (picks < 0) picks = 0;

            // Clamped for the ROW only. A day that picked six locks has done the task and is
            // shown "done" by the line above; this is what a day that has somehow got past
            // five without the bit being set should read as, and "6 / 5" is not it.
            if (picks > PicksNeeded) picks = PicksNeeded;
            return $"{picks} / {PicksNeeded}";
        }

        /// <summary>
        /// Is that task finished today?
        ///
        /// Read off the mask and nowhere else. The count of picked locks is not consulted
        /// even for the first task: the mask is what was PAID for, and a second source of
        /// "done" is a second thing that can disagree with the gold already handed over.
        /// A day whose count has passed five with the bit still clear is a day whose next
        /// match sets the bit and pays once -- see <see cref="Earned"/>.
        /// </summary>
        public static bool Done(DailyTask task, PlayerProgress p) =>
            p != null && (p.dailiesMask & Bit(task)) != 0;

        /// <summary>
        /// Which tasks this match completes, as a mask of bits, and nothing else happens.
        ///
        /// Two things about it matter more than the three rules in it.
        ///
        /// It CHANGES NOTHING. It is a question, and the caller writes the answer down. That
        /// is why it works out the day's pick total for itself -- picksToday plus this
        /// match's -- rather than reading a total somebody has already updated: it has to be
        /// safe to ask before the day's counters have taken the match in, and asked the
        /// other way round the match's picks are counted twice and three picked locks pay
        /// out for five. docs/09's number would then be a lie about the game rather than a
        /// statement of it.
        ///
        /// So the local sum here and the `picksToday +=` in the caller are not two halves of
        /// one mistake. Remove either and the task breaks: without the sum a match's own
        /// picks never count towards the day, and without the caller's line picksToday stays
        /// at nought for ever and five safes can never be reached at all.
        ///
        /// And the last line takes the day's finished tasks back out of the answer. That line
        /// IS the fix for the 120 gold: without it, the one task a player happens to be good
        /// at pays 40 gold every match of the day, and "40 кожне" stops meaning anything.
        /// </summary>
        public static int Earned(in MatchFacts facts, PlayerProgress progress)
        {
            int already = (progress != null ? progress.dailiesMask : 0) & AllBits;
            int earned = 0;

            int picks = (progress != null ? progress.picksToday : 0)
                        + (facts.LocksPicked > 0 ? facts.LocksPicked : 0);
            if (picks >= PicksNeeded) earned |= Bit(DailyTask.PickFiveLocks);

            // Won, and without ever holding the top tier. The win is half the task and not
            // decoration: a "win without a T3" that pays for LOSING without one pays out on
            // every bare-handed defeat in the game, which is the most common way to lose a
            // first match -- so the lesson would be collected by the player who learnt
            // nothing, daily, while never winning.
            if (facts.Won && facts.TopWeaponTier < TopTier)
                earned |= Bit(DailyTask.WinWithoutT3);

            // Nothing else is asked of this one, deliberately. docs/09 wants the player to
            // find out that the arena can kill for them; whether they also went on to win
            // the match is not the lesson, and a gas kill is rare enough without conditions
            // hung off it.
            if (facts.HazardKill) earned |= Bit(DailyTask.KillWithHazard);

            return earned & ~already;
        }
    }
}

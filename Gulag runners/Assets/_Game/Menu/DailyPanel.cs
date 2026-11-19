using UnityEngine;
using UnityEngine.Events;
using GulagRunners.Meta;

namespace GulagRunners.Game
{
    /// <summary>
    /// The three daily tasks, written out where a player can read them.
    ///
    /// The menu has shown "1 / 3" for a while and the button beside it opened a panel with
    /// nothing in it, which is the worst of the three possible states: a player who is told
    /// they have finished one of three and is given no way to find out what the other two
    /// are has been handed a chore and no instructions. docs/09's whole argument for the
    /// tasks is that they TEACH THE MECHANICS -- «завдання вчать механікам» -- and a lesson
    /// nobody can read teaches nothing. It is also, at 120 gold a day against 10 for playing
    /// a match, most of where a player's gold comes from.
    ///
    /// The titles and the progress come from <see cref="Dailies"/> and are not written here.
    /// Two lists of three task names, one in the logic that awards them and one in the
    /// screen that shows them, is two lists that drift -- and the one that drifts is always
    /// the screen, so the player is shown a task the game is not paying for.
    ///
    /// Rows are UnityEvents, like every other readout in this menu: the builder wires them
    /// to labels, so the panel can be redrawn, restyled or replaced without this file
    /// knowing what a label is.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DailyPanel : MonoBehaviour
    {
        /// <summary>One task's two lines.</summary>
        [System.Serializable]
        public sealed class Row
        {
            [Tooltip("What the task is. Wired to a label by the menu builder.")]
            public UnityEvent<string> titleChanged;

            [Tooltip("How far it has got — \"3 / 5\", or done. Wired to a second label.")]
            public UnityEvent<string> progressChanged;
        }

        [Tooltip("The player's saved progress. Found through its Instance when empty.")]
        public PlayerProgress progress;

        [Tooltip("One row per task, in the order Dailies.All gives them. Fewer rows than "
                 + "there are tasks means a task the player can never read about, so the "
                 + "builder makes exactly as many as there are and says so if it cannot.")]
        public Row[] rows = new Row[Dailies.Count];

        [Header("Read-only")]
        [Tooltip("What it last wrote, so you can see it work without leaving Unity.")]
        public string said = "not shown yet";

        /// <summary>
        /// Filled when the panel opens rather than every frame.
        ///
        /// Nothing about a daily task changes while the panel is up: the only things that
        /// move the mask or the pick count are finishing a match and the day rolling over,
        /// and neither happens with this on the screen. So reading the save once, when it
        /// is put in front of somebody, is both enough and the only honest moment to do it.
        /// </summary>
        void OnEnable() => Refresh();

        /// <summary>Re-read the save and write the rows. Safe to call from a button.</summary>
        public void Refresh()
        {
            if (progress == null) progress = PlayerProgress.Instance;

            DailyTask[] tasks = Dailies.All;
            int shown = 0;

            for (int i = 0; i < rows.Length && i < tasks.Length; i++)
            {
                Row row = rows[i];
                if (row == null) continue;

                row.titleChanged?.Invoke(Dailies.Title(tasks[i]));
                row.progressChanged?.Invoke(Dailies.Progress(tasks[i], progress));
                shown++;
            }

            said = progress == null
                ? "no PlayerProgress, so the tasks are shown with nothing done"
                : $"{shown} of {tasks.Length} task(s) shown, {progress.DailiesDone} done today";
        }
    }
}

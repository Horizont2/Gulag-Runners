using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts a <see cref="FighterBrain"/> on each fighter, switched off.
    ///
    /// Off on purpose: the tick box is the feature, and a tool that adds a component AND
    /// turns it on takes the first decision away from whoever ran it. Player 2 is the one
    /// usually handed over, so its box is the one to tick.
    /// </summary>
    public static class BrainSetup
    {
        [MenuItem("Tools/Gulag Runners/Setup/Add practice opponent")]
        public static void Add()
        {
            PlayerController[] fighters =
                Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include,
                                                           FindObjectsSortMode.None);
            if (fighters.Length == 0)
            {
                Debug.LogWarning("No PlayerController in this scene.");
                return;
            }

            System.Array.Sort(fighters, (a, b) => a.playerIndex.CompareTo(b.playerIndex));
            int added = 0, had = 0;

            foreach (PlayerController fighter in fighters)
            {
                FighterBrain brain = fighter.GetComponent<FighterBrain>();
                if (brain != null) { had++; continue; }

                brain = Undo.AddComponent<FighterBrain>(fighter.gameObject);
                brain.self = fighter;
                brain.takeOver = false;
                brain.skill = BotSkill.Sparring;
                brain.tuning = BotConfig.For(BotSkill.Sparring);
                EditorUtility.SetDirty(brain);
                added++;
            }

            Debug.Log($"Practice opponent: {added} added, {had} already there. Tick Take Over " +
                      "on the fighter you want the computer to play — usually Player 2 — and " +
                      "press play. It can be ticked and unticked mid-round.");
        }
    }
}

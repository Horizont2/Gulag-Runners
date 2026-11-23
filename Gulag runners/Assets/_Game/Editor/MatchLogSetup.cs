using System.IO;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts a <see cref="MatchRecorder"/> in an arena, so that a playtest leaves numbers
    /// behind instead of memories.
    ///
    /// docs/07 names four numbers as the exit criteria for M1 and until now the project has
    /// measured none of them. Playing the game has been producing opinions -- "that felt
    /// long", "the gas seems early" -- which are worth having and are not evidence. One
    /// component in each arena turns every round played, by anybody, on any phone, into a
    /// line in a file.
    ///
    /// It goes on the MatchState's own object, which is the thing it is about, and which is
    /// where somebody looks when they ask where the figures came from.
    ///
    /// It is deliberately NOT part of "Make this scene playable". The recorder is a
    /// measuring instrument, not a part of the game, and an arena without one is a
    /// perfectly good arena -- just one you learn nothing from.
    /// </summary>
    public static class MatchLogSetup
    {
        [MenuItem("Tools/Gulag Runners/Record the matches")]
        public static void Run()
        {
            MatchState match = Object.FindFirstObjectByType<MatchState>();
            if (match == null)
            {
                Debug.LogWarning("No MatchState in this scene, so there are no matches to "
                                 + "record. Open an arena.");
                return;
            }

            MatchLog log = match.GetComponent<MatchLog>();
            bool fresh = log == null;
            if (fresh) log = Undo.AddComponent<MatchLog>(match.gameObject);
            Undo.RecordObject(log, "Record the matches");

            log.matchState = match;

            // The counter the file reads. One MatchTally per arena serves both this and
            // MatchRecorder -- the thing that pays the player -- because a second counter
            // would be a second rule for what "picked" means, and the two would disagree
            // about the one number docs/03 rests the whole game on.
            MatchTally tally = Object.FindFirstObjectByType<MatchTally>();
            if (tally == null) tally = Undo.AddComponent<MatchTally>(match.gameObject);
            Undo.RecordObject(tally, "Record the matches");
            tally.matchState = match;
            log.tally = tally;
            EditorUtility.SetDirty(tally);

            // The result screen, for the one criterion that has a behavioural proxy: docs/07
            // wants 60% of players to want another, and the press of AGAIN is the only
            // honest version of that a file can hold.
            MatchResultView result = Object.FindFirstObjectByType<MatchResultView>();
            log.result = result;

            EditorUtility.SetDirty(log);
            Selection.activeGameObject = match.gameObject;

            string path = Path.Combine(Application.persistentDataPath, log.fileName);

            Debug.Log(
                (fresh ? "Recording this arena's matches. " : "Recorder already here, rewired. ")
                + $"Rounds go to:\n{path}\n"
                + (result != null
                    ? "The result screen is wired, so the AGAIN press is recorded too — "
                      + "that is docs/07's \"want another\", as behaviour rather than as an "
                      + "opinion."
                    : "NO result screen in this scene, so no rematch is recorded and docs/07's "
                      + "third criterion stays unmeasured here. Run Tools > Gulag Runners > "
                      + "Build the result screen first.")
                + "\n\nRead the file with: python3 docs/refs/playtest.py <the path above>\n"
                + "Run it with no file first — it checks itself against a woven sample and "
                + "prints what a report looks like.",
                log);
        }
    }
}

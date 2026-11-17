using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the phone's answer into an arena (docs/38).
    ///
    /// <see cref="Haptics"/> finds everything it needs by itself at runtime -- that is the
    /// point of writing it the way <see cref="PoisonWarning"/> is written -- so this tool is
    /// not strictly wiring. It is the thing that makes the component EXIST in the four
    /// arenas, which is the step that actually gets skipped: a presentation component nobody
    /// has added is indistinguishable from the feature not being written, and that is exactly
    /// the state docs/38 found vibration in.
    ///
    /// It goes on the MatchState's own object, and that is a decision rather than a
    /// convenience. Every signal the component reads is either the local fighter's or the
    /// match's, and MatchState is where "which fighter is mine" is decided (see
    /// docs/refs/local_fighter_check.py) -- so somebody asking why their phone buzzed for
    /// the wrong man finds the two things in one place.
    ///
    /// What it will NOT do is invent an off switch on the menu. The switch itself is already
    /// real and saved -- Haptics.Wanted, in PlayerPrefs -- but docs/02 asks for a SETTINGS
    /// SCREEN carrying six of these, and there is no settings model in the project for any
    /// of them yet. So the tool says what is missing rather than quietly leaving a required
    /// setting with no control attached to it.
    /// </summary>
    public static class HapticsSetup
    {
        [MenuItem("Tools/Gulag Runners/Make the phone answer")]
        public static void Run()
        {
            MatchState match = Object.FindFirstObjectByType<MatchState>();
            if (match == null)
            {
                Debug.LogWarning("No MatchState in this scene, so there is no round for the "
                                 + "phone to answer and no way to tell whose hands these "
                                 + "are. Open an arena.");
                return;
            }

            Haptics feel = match.GetComponent<Haptics>();
            if (feel == null) feel = Undo.AddComponent<Haptics>(match.gameObject);
            Undo.RecordObject(feel, "Make the phone answer");

            feel.matchState = match;

            // Left empty on purpose, and worth being explicit about: the component asks
            // MatchState which fighter is local on every frame, because the answer CHANGES
            // as rounds are dealt. A reference dragged in here is the scene's answer, and
            // the scene's answer is always player one -- which on the guest's phone is the
            // opponent, and a phone that buzzes for the other man's blows is the one failure
            // this feature must not have.
            feel.player = null;

            // The pool, so the gas can be felt coming up to your boots. Optional: without
            // one that single feel is silent and the other ten are unaffected.
            PoisonPool pool = Object.FindFirstObjectByType<PoisonPool>();
            feel.pool = pool;

            EditorUtility.SetDirty(feel);
            Selection.activeGameObject = match.gameObject;

            Debug.Log("The phone answers: eleven feels on "
                      + match.gameObject.name
                      + " — a blow landed, three rungs of a blow taken, a parry, a guard, "
                      + "the vent, the gas at your feet, a chest, a refused lock and your "
                      + "own death. "
                      + (pool != null
                          ? "The gas throb is wired to " + pool.gameObject.name + ". "
                          : "NO PoisonPool found, so the gas throb is silent here — the "
                            + "other ten feels are unaffected. ")
                      + "Android is real; iOS is a marked no-op (the comment in Haptics.cs "
                      + "says exactly which plugin would fill it). Nothing in the editor "
                      + "buzzes, so this can only be judged on a phone. "
                      + "The off switch is saved under PlayerPrefs \"gr.haptics.on\" and "
                      + "reachable as Haptics.Wanted from any scene — docs/02 requires it, "
                      + "and there is still no settings screen in the project to put it on.",
                      feel);
        }
    }
}

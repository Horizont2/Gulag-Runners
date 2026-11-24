using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the four reacting volumes into an arena and hands them to
    /// <see cref="CombatGrade"/>.
    ///
    /// The profiles are ASSETS and this does not write them. That is the whole shape of it:
    /// each look is authored in Assets/Settings, where it can be opened and tuned against a
    /// live scene view, and both this tool and the component only ever touch a weight.
    /// Nothing here can produce a colour an artist did not choose, and nothing at play time
    /// can dirty a profile on disk.
    ///
    /// Four Global volumes rather than one with everything in it, because they have to
    /// overlap. A blow landing while the fighter is already in desperation range is two
    /// looks at once, at two different strengths, and one volume cannot be at two weights.
    ///
    /// Additive and safe to run twice: it finds the component wherever it already is,
    /// volumes already there keep their settings, and a profile is only assigned when the
    /// slot is empty -- so one somebody has swapped for their own is left alone.
    ///
    /// Every arena in the pool already carries the rig, on its Global Volume. This is for a
    /// NEW arena, and for putting back a volume somebody deleted.
    /// </summary>
    public static class CombatGradeSetup
    {
        const string Root = "Combat Grade";
        const string Folder = "Assets/Settings";

        /// <summary>
        /// Above the arena's own Global Volume and its biome grade, so these win while they
        /// have any weight -- that is the point of them. Spaced so a fifth can be slipped in
        /// between two of these later without renumbering.
        /// </summary>
        const float BasePriority = 100f;

        [MenuItem("Tools/Gulag Runners/Make the screen answer")]
        public static void Setup()
        {
            MatchState match = Object.FindFirstObjectByType<MatchState>();
            if (match == null)
            {
                Debug.LogWarning(
                    "No MatchState in this scene, so there is no fight for the screen to "
                    + "answer. Open an arena scene.");
                return;
            }

            // The component FIRST, wherever it already is, and only then a home for a new
            // one. Every arena in the pool already carries this rig on its Global Volume --
            // put there in the scene files, because that is where a view belongs in this
            // project and there was no editor to press this menu item with at the time --
            // and a tool that went looking for an object called "Combat Grade" instead would
            // find none, build a second rig beside the first, and leave two components
            // writing the same four weights.
            CombatGrade grade = Object.FindFirstObjectByType<CombatGrade>(
                FindObjectsInactive.Include);

            GameObject root;
            if (grade != null)
            {
                root = grade.gameObject;
            }
            else
            {
                root = GameObject.Find(Root);
                if (root == null)
                {
                    root = new GameObject(Root);
                    Undo.RegisterCreatedObjectUndo(root, "Make the screen answer");
                }
                grade = Undo.AddComponent<CombatGrade>(root);
            }

            Undo.RecordObject(grade, "Make the screen answer");
            grade.match = match;

            List<string> made = new List<string>();
            List<string> missing = new List<string>();

            grade.hurt = Ensure(root, "Hurt", "Combat_Hurt", BasePriority + 0f,
                                made, missing);
            grade.landed = Ensure(root, "Landed", "Combat_Landed", BasePriority + 1f,
                                  made, missing);
            grade.desperate = Ensure(root, "Desperate", "Combat_Desperate",
                                     BasePriority + 2f, made, missing);
            grade.suddenDeath = Ensure(root, "Sudden death", "Combat_SuddenDeath",
                                       BasePriority + 3f, made, missing);

            EditorUtility.SetDirty(grade);
            Selection.activeGameObject = root;

            string gone = missing.Count == 0
                ? ""
                : $"\nNo profile found for: {string.Join(", ", missing)}. Those volumes are "
                  + $"in the scene with nothing in them, which costs nothing and shows "
                  + $"nothing. The assets belong in {Folder}.";

            Debug.Log(
                $"The screen answers the fight: {string.Join(", ", made)}.\n"
                + "Each look is a profile in Assets/Settings and this tool does not write "
                + "them -- open one and tune it with the scene view live. CombatGrade only "
                + "moves the weights: a blow landing on this fighter punches Hurt, a blow "
                + "landing from them punches Landed, Desperate is held below the share of "
                + "health where bare hands hit 40% harder, and Sudden death is held through "
                + "the last phase.\n"
                + "It follows MatchState.LocalFighter rather than fighter one, so it is the "
                + "right man's health on the guest's phone as well."
                + gone,
                root);
        }

        /// <summary>
        /// One volume: the object, the component, the priority and the profile.
        ///
        /// Global, because these are states of the match and not places in it -- a fighter
        /// does not walk into being nearly dead. Weight zero, because every one of them is
        /// off until something happens, and a volume saved at full weight is a scene that
        /// opens in the colour of somebody dying.
        /// </summary>
        static Volume Ensure(GameObject parent, string name, string profile, float priority,
                             List<string> made, List<string> missing)
        {
            Transform found = parent.transform.Find(name);
            GameObject go;

            if (found != null)
            {
                go = found.gameObject;
            }
            else
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Make the screen answer");
                go.transform.SetParent(parent.transform, false);
                made.Add(name.ToLowerInvariant());
            }

            Volume volume = go.GetComponent<Volume>();
            if (volume == null) volume = Undo.AddComponent<Volume>(go);

            Undo.RecordObject(volume, "Make the screen answer");
            volume.isGlobal = true;
            volume.priority = priority;
            volume.weight = 0f;

            // Only when the slot is empty, so a profile somebody swapped for their own
            // survives this tool being run again.
            if (volume.sharedProfile == null)
            {
                VolumeProfile asset = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                    $"{Folder}/{profile}.asset");

                if (asset != null) volume.sharedProfile = asset;
                else missing.Add(profile);
            }

            EditorUtility.SetDirty(volume);
            return volume;
        }
    }
}

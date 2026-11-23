using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;
using GulagRunners.Net;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Turns a dressed location into one a match can be played in.
    ///
    /// A finished-looking arena and a playable one are not nearly the same thing, and
    /// they look identical in the scene view. Arena_Nordic fortress is 165 prefab
    /// instances of very good fortress with no simulation, no fighters, no HUD and no
    /// floor the simulation can see. Put it in the map pool as it stands and every
    /// match dealt to it loads a scene that counts down and never starts -- which reads
    /// as a netcode bug, because it fails for both players at once.
    ///
    /// WHAT IT COPIES, AND WHY IT COPIES RATHER THAN BUILDS. Everything a match needs
    /// already exists, wired, in Arena_Demo: two fighters with their arms, IK, armour,
    /// weapon sockets and brains, two cameras, the HUD canvas, the clock, the link.
    /// Rebuilding that from code would be a second description of it, and the day the
    /// two descriptions disagree is the day a new arena is subtly not the same game.
    /// So the working arena IS the description.
    ///
    /// It copies them in ONE operation on purpose. Instantiating each root separately
    /// loses every reference that crosses between them -- MatchState's fighter list,
    /// the cameras' targets, what the HUD reads -- and they would have to be found again
    /// by type afterwards, which works until one of them has two candidates. Parenting
    /// the wanted roots under one temporary object and copying that keeps every wire,
    /// because they are all inside the thing being copied. The reference scene is then
    /// closed without saving, so the parenting never happened as far as the disk is
    /// concerned.
    ///
    /// WHAT IT DOES NOT DO. It does not mark the floor. Which boxes in a hand-dressed
    /// location are ground, which are scenery and which are the backdrop is a level
    /// design decision with a fighter's reach riding on it, and a tool that guessed
    /// would produce an arena that is wrong in a way nobody can see. Mark the walkable
    /// geometry with SimCollider, then run the checks:
    ///
    ///   python3 docs/refs/arena_ready_check.py   what the scene is still missing
    ///   python3 docs/refs/depth_check.py         whether its storeys step back properly
    ///   python3 docs/refs/map_pool_check.py      whether the pool and build list agree
    /// </summary>
    public static class ArenaSceneSetup
    {
        // The arena a new one is copied from. Arena_Demo, which this named, was deleted
        // once it had served its purpose -- so the tool has been pointing at nothing, and
        // saying nothing about it, ever since. The forest is the finished one now.
        const string Reference = "Assets/Scenes/Arena_Forest.unity";

        /// <summary>
        /// One per thing a match cannot start without. The root carrying it in the
        /// reference arena is what gets copied, so this is a list of what to look for
        /// and not a list of what to build.
        /// </summary>
        static readonly System.Type[] Needed =
        {
            typeof(SimWorldBaker),
            typeof(MatchState),
            typeof(PlayerController),
            typeof(SideViewCamera),
            typeof(HudView),
            typeof(MatchRecorder),
            typeof(LanLink),
            typeof(NetMatch),
            typeof(AudioManager),
        };

        [MenuItem("Tools/Gulag Runners/Setup/List what this scene needs to be playable")]
        public static void List() => Run(dryRun: true);

        [MenuItem("Tools/Gulag Runners/Setup/Make this scene playable")]
        public static void Make() => Run(dryRun: false);

        static void Run(bool dryRun)
        {
            Scene target = SceneManager.GetActiveScene();
            if (!target.IsValid() || !target.isLoaded)
            {
                Debug.LogError("Arena setup: no scene is open.");
                return;
            }

            if (target.path == Reference)
            {
                Debug.Log("Arena setup: this IS the reference arena, so there is " +
                          "nothing to copy into it.");
                return;
            }

            List<System.Type> missing = Needed.Where(Absent).ToList();
            if (missing.Count == 0)
            {
                Debug.Log($"Arena setup: {target.name} already has everything a match " +
                          "needs. Run docs/refs/arena_ready_check.py for the rest of the " +
                          "list -- a marked floor is not a component and this cannot see " +
                          "whether there is one.");
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine($"Arena setup: {target.name} is missing " +
                              $"{missing.Count} of the {Needed.Length} things a match " +
                              "needs:");
            foreach (System.Type t in missing) report.AppendLine($"  {t.Name}");

            if (dryRun)
            {
                report.AppendLine();
                report.AppendLine("Nothing was changed. \"Make this scene playable\" " +
                                  "copies them in from Arena_Demo, wires and all.");
                Debug.Log(report.ToString());
                return;
            }

            Scene source = default;
            GameObject copied = null;
            var brought = new List<string>();

            try
            {
                source = EditorSceneManager.OpenScene(Reference, OpenSceneMode.Additive);
                if (!source.IsValid())
                {
                    Debug.LogError($"Arena setup: {Reference} would not open, so nothing " +
                                   "was changed.");
                    return;
                }

                // The outermost root behind each missing piece, once each. Two things
                // living under the same root -- the clock and the link usually do --
                // come across together and stay wired to each other.
                var roots = new List<Transform>();
                foreach (System.Type want in missing)
                    foreach (GameObject root in source.GetRootGameObjects())
                        if (root.GetComponentInChildren(want, true) != null &&
                            !roots.Contains(root.transform))
                            roots.Add(root.transform);

                if (roots.Count == 0)
                {
                    Debug.LogError("Arena setup: Arena_Demo does not carry those either, " +
                                   "which means this list and that scene have drifted " +
                                   "apart. Nothing was changed.");
                    return;
                }

                // One parent, one copy, every wire between them kept. The reference
                // scene is closed without saving below, so this never reaches the disk.
                var bundle = new GameObject("__arena setup bundle");
                SceneManager.MoveGameObjectToScene(bundle, source);
                foreach (Transform root in roots)
                {
                    brought.Add(root.name);
                    root.SetParent(bundle.transform, true);
                }

                copied = Object.Instantiate(bundle);
                SceneManager.MoveGameObjectToScene(copied, target);
            }
            finally
            {
                if (source.IsValid()) EditorSceneManager.CloseScene(source, true);
            }

            if (copied == null)
            {
                Debug.LogError("Arena setup: the copy did not come across. Nothing was " +
                               "changed in this scene.");
                return;
            }

            // Unparent into the scene proper and throw the wrapper away, so the result
            // is the same shape Arena_Demo has rather than everything under one object.
            var landed = new List<GameObject>();
            foreach (Transform child in copied.transform.Cast<Transform>().ToList())
            {
                child.SetParent(null, true);
                child.name = child.name.Replace("(Clone)", string.Empty).Trim();
                landed.Add(child.gameObject);
            }
            Object.DestroyImmediate(copied);
            foreach (GameObject go in landed)
                Undo.RegisterCreatedObjectUndo(go, "Make this scene playable");

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
            Selection.objects = landed.ToArray();

            report.AppendLine();
            report.AppendLine($"Copied {landed.Count} root(s) out of Arena_Demo: " +
                              string.Join(", ", brought));
            report.AppendLine("Scene saved, and all of them are selected.");
            report.AppendLine();
            report.AppendLine("STILL TO DO BY HAND, because none of it is a component:");
            report.AppendLine("  * mark the walkable geometry with SimCollider -- which " +
                              "boxes are ground, which are scenery and which are the " +
                              "backdrop is a level decision, not a guess a tool should " +
                              "make;");
            report.AppendLine("  * move the two fighters and the two cameras to where " +
                              "this arena's spawns are;");
            report.AppendLine("  * set the baker's Plane Z to this arena's own fight " +
                              "plane.");
            report.AppendLine("Then: arena_ready_check.py, depth_check.py, and switch " +
                              "the map on in MapPool.");

            Debug.Log(report.ToString());
        }

        static bool Absent(System.Type t) =>
            Object.FindObjectsByType(t, FindObjectsInactive.Include,
                                     FindObjectsSortMode.None).Length == 0;
    }
}

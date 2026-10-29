using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Swaps the props that came in as .blend files for the .fbx of the same model.
    ///
    /// WHY ANY OF THIS IS NEEDED. Unity does not read a .blend file itself: it shells out
    /// to Blender and imports what comes back. On a machine without Blender installed the
    /// import produces nothing, and every object made from one turns into "Missing Prefab
    /// with guid: ..." in the Hierarchy -- sixty-odd barrels, crates, buckets and cobwebs
    /// in this arena, all invisible, all still in the scene file. Nothing is lost and git
    /// is not at fault; the asset simply never imported.
    ///
    /// Installing Blender fixes it in one step and changes nothing in the project, which
    /// is the better answer if you are willing to carry that dependency. This is the
    /// other answer: the pack ships the same 48 models as .fbx as well, Unity reads those
    /// with no help from anybody, and the two are a clean one-to-one match by name.
    ///
    /// CHECKED BEFORE WRITING THIS, because a swap that silently resizes the level is
    /// worse than missing props: every .blend in that pack and its .fbx twin carry the
    /// same importer settings -- globalScale 1, useFileScale on, bakeAxisConversion off
    /// -- so they land at the same size and the same way up. The tool still copies the
    /// instance's own transform across rather than trusting that, and the dry run exists
    /// so you can read the list before anything moves.
    ///
    /// WHAT IT CANNOT DO. A missing prefab instance has no children loaded -- Unity
    /// keeps the GameObject, its transform and any component that was ADDED to the
    /// instance, and nothing else. So this restores position, rotation, scale, name,
    /// parent, sibling order, layer and active state, and it cannot carry a component
    /// across. For plain scenery that is everything there was; for anything that was
    /// marked up, it is the markup.
    ///
    /// So a broken object that still carries a component is left alone and listed, which
    /// is the whole difference between this and LadderSetup. The four ladders are marked
    /// SimCollider / Kind = Ladder, and a swap that dropped that would turn the arena's
    /// only routes between storeys into scenery -- invisible to the simulation, and in a
    /// way nothing in the scene view would show.
    ///
    /// Anything with no .fbx of its own is listed and left alone too. St.blend and
    /// St2.blend used to be the whole of that list; docs/refs/blend_to_fbx.py has since
    /// exported them.
    /// </summary>
    public static class BlendPropSwap
    {
        static readonly Regex Numbered = new Regex(@"\s*\(\d+\)$");

        /// <summary>
        /// The suffix Unity puts on a broken instance's NAME -- not just on its label in
        /// the Hierarchy, but on go.name itself, which is why the Inspector shows
        /// "Cobweb2 (4) (Missing Prefab with guid: 9ce8...)" in the name field.
        ///
        /// This is what made the tool report 0 of 80. It stripped a trailing " (4)" and
        /// looked up the rest, and the rest was the whole decorated string, which
        /// matches no model ever. The scene file stores the clean name, so nothing about
        /// the asset or the scene gave it away.
        ///
        /// It is also the fix: that suffix carries the SOURCE GUID, which beats any name
        /// as a key. Resolve it and ask the asset database what the file was called.
        /// </summary>
        static readonly Regex MissingSuffix = new Regex(
            @"\s*\(Missing Prefab with guid:\s*([0-9a-fA-F]{32})\s*\)\s*$");

        [MenuItem("Tools/Gulag Runners/Setup/List broken .blend props")]
        public static void List() => Run(dryRun: true);

        [MenuItem("Tools/Gulag Runners/Setup/Replace broken .blend props with FBX")]
        public static void Replace() => Run(dryRun: false);

        static void Run(bool dryRun)
        {
            Dictionary<string, string> models = ModelsByName();
            List<GameObject> broken = Broken();

            // Say how big the index is. The first version of this silently built an
            // empty one and then reported "0 of 80 matched", which reads as "your
            // models are missing" when it meant "I failed to look".
            if (models.Count == 0)
            {
                Debug.LogError(
                    "Blend props: found no .fbx anywhere under Assets. That cannot be " +
                    "right in this project, so the fault is in this tool or the asset " +
                    "database is still importing -- not in your files. Let the import " +
                    "finish and try again.");
                return;
            }

            if (broken.Count == 0)
            {
                Debug.Log("Blend props: nothing is missing its prefab in the open " +
                          "scene(s). Either Blender is installed and the .blend files " +
                          "imported, or this has already been run.");
                return;
            }

            var done = new List<string>();
            var stuck = new SortedDictionary<string, int>();
            var marked = new List<string>();
            int replaced = 0;

            var traced = new List<string>();
            var created = new List<GameObject>();
            foreach (GameObject go in broken)
            {
                // Read the name BEFORE anything can destroy the object. Doing it after
                // the swap is what stopped this run after a single barrel: Swap ends in
                // Undo.DestroyObjectImmediate, and the report line then asked the dead
                // object what it was called. MissingReferenceException, out of the loop,
                // 75 props left exactly as they were.
                string label = go.name;
                string baseName = SourceName(go);

                // Markup outranks the prop. Unity keeps a component that was added to an
                // instance even when the prefab behind it is gone -- that is why the
                // Inspector can show SimCollider / Kind = Ladder on an object with no
                // mesh -- and this tool has no way to put one back. Replacing such an
                // object trades a prop you cannot see for collision the simulation can
                // no longer find, which is the worse of the two.
                if (go.GetComponents<Component>().Length > 1)
                {
                    marked.Add($"  {label}  ({go.GetComponents<Component>().Length - 1} " +
                               "component(s) on it)");
                    continue;
                }

                // The first few, raw name and all. A tool that cannot say WHY it matched
                // nothing is a tool that gets argued with instead of fixed.
                if (traced.Count < 5)
                    traced.Add($"  \"{label}\"  ->  looked up \"{baseName}\"");

                if (!models.TryGetValue(baseName, out string path))
                {
                    stuck.TryGetValue(baseName, out int n);
                    stuck[baseName] = n + 1;
                    continue;
                }

                if (dryRun)
                {
                    done.Add($"  {label}  ->  {path}");
                    replaced++;
                    continue;
                }

                // One awkward object must not cost the other seventy-five. A batch tool
                // that stops dead halfway leaves the scene in a state nobody asked for.
                try
                {
                    GameObject made = Swap(go, path);
                    if (made != null)
                    {
                        replaced++;
                        created.Add(made);
                        // The renderer count is the difference between "an object now
                        // exists" and "you can see it". An FBX root with nothing to draw
                        // under it would replace a missing prop with an invisible one,
                        // and the count would still read 76.
                        int draws = made.GetComponentsInChildren<Renderer>(true).Length;
                        done.Add($"  {label}  ->  {path}  ({draws} renderer(s) at " +
                                 $"{made.transform.position})");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Blend props: {label} could not be replaced " +
                                     $"({e.GetType().Name}: {e.Message}). Carrying on.");
                }
            }

            // Count what is STILL broken, and save. "76 replaced" is a claim about what
            // this code did; the only thing worth printing is what the scene looks like
            // afterwards, counted the same way it was counted before. The two disagreeing
            // is the single most useful line this tool can produce.
            int left = -1;
            if (!dryRun && replaced > 0)
            {
                left = Broken().Count;
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            }

            var message = new System.Text.StringBuilder();
            message.AppendLine(dryRun
                ? $"Blend props (DRY RUN, nothing changed): {replaced} of {broken.Count} " +
                  $"broken objects have an FBX of the same name, out of {models.Count} " +
                  "FBX in the project."
                : $"Blend props: {replaced} of {broken.Count} replaced, from " +
                  $"{models.Count} FBX in the project. Scene saved.");

            if (left >= 0)
            {
                message.AppendLine($"  Still missing a prefab after the run: {left}. " +
                                   (left == broken.Count - replaced
                                    ? "That matches -- the swap stuck."
                                    : "That does NOT match what was replaced, so the " +
                                      "swap did not stick. Send this line."));
            }

            // WHERE they went, and all of them selected so you can press F and look.
            //
            // "It replaced them but I cannot see them" is not something a count can
            // answer. These props were placed by somebody working with the .blend
            // import; the swap puts the FBX on the same transform, so if one was tucked
            // behind the floor slab it still is. The combined box says whether they
            // landed in the arena or somewhere absurd, and the split above and below the
            // floor says how many were never going to be visible from the front.
            if (!dryRun && created.Count > 0)
            {
                Bounds all = new Bounds(created[0].transform.position, Vector3.zero);
                int drawable = 0, below = 0;
                foreach (GameObject made in created)
                {
                    all.Encapsulate(made.transform.position);
                    if (made.transform.position.y < 0f) below++;
                    foreach (Renderer r in made.GetComponentsInChildren<Renderer>(true))
                    {
                        all.Encapsulate(r.bounds);
                        drawable++;
                    }
                }

                message.AppendLine();
                message.AppendLine($"  They occupy {all.size.x:0.0} x {all.size.y:0.0} x " +
                                   $"{all.size.z:0.0} m centred on {all.center}, with " +
                                   $"{drawable} renderer(s) between them.");
                message.AppendLine($"  {below} of {created.Count} sit below y=0, so they " +
                                   "are under the main floor and were already hidden " +
                                   "from the front before this ran.");
                message.AppendLine("  All of them are selected now -- hover the Scene " +
                                   "view and press F to frame them.");

                Selection.objects = created.ToArray();
            }

            // Long lists are worse than useless in the console; the first few say whether
            // the names matched the way you expected, which is the thing to check.
            int show = Mathf.Min(done.Count, 12);
            for (int i = 0; i < show; i++) message.AppendLine(done[i]);
            if (done.Count > show) message.AppendLine($"  ... and {done.Count - show} more");

            if (marked.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Left alone because a plain swap would drop what is " +
                                   "on them:");
                foreach (string line in marked) message.AppendLine(line);
                message.AppendLine("Those are built by Tools > Gulag Runners > Setup > " +
                                   "Build the arena ladders, which carries the marker " +
                                   "across and sizes its box to the model.");
            }

            if (stuck.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("No .fbx of the same name, so left alone:");
                foreach (var pair in stuck)
                    message.AppendLine($"  {pair.Key} x{pair.Value}");
                message.AppendLine("Those need Blender installed, or an FBX exported by hand.");
            }

            if (replaced == 0 && traced.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Nothing matched. What the names turned into:");
                foreach (string line in traced) message.AppendLine(line);
                message.AppendLine("If the right-hand side is not a plain model name, " +
                                   "that is the bug -- send this block.");
            }

            if (dryRun) message.AppendLine("\nRun \"Replace broken .blend props with FBX\" to do it.");
            Debug.Log(message.ToString());
        }

        /// <summary>
        /// Every .fbx in the project, by its file name without the extension.
        ///
        /// Walks every asset path rather than asking AssetDatabase.FindAssets("t:Model").
        /// That filter is what the Project window's search box understands; from script
        /// it returned nothing here, and the tool reported "0 of 80 broken objects have
        /// an FBX of the same name" while sitting next to 48 FBX with exactly the right
        /// names. A path ending in .fbx cannot be misread the same way.
        /// </summary>
        /// <summary>
        /// What the asset behind a broken instance was CALLED.
        ///
        /// By guid where the name carries one -- that is exact, and survives somebody
        /// renaming the object in the Hierarchy. By the name with both of Unity's
        /// decorations stripped otherwise.
        /// </summary>
        static string SourceName(GameObject go)
        {
            Match missing = MissingSuffix.Match(go.name);

            if (missing.Success)
            {
                string source = AssetDatabase.GUIDToAssetPath(missing.Groups[1].Value);
                if (!string.IsNullOrEmpty(source))
                    return Path.GetFileNameWithoutExtension(source);
            }

            string cleaned = missing.Success
                ? go.name.Substring(0, missing.Index)
                : go.name;
            return Numbered.Replace(cleaned, string.Empty).Trim();
        }

        static Dictionary<string, string> ModelsByName()
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;

                string name = Path.GetFileNameWithoutExtension(path);
                if (!found.ContainsKey(name)) found[name] = path;
            }
            return found;
        }

        /// <summary>
        /// Every object in the open scenes whose prefab asset is gone.
        ///
        /// A missing instance has nothing under it, so each one found is its own root and
        /// there is no sub-object to filter out -- but the parent test stays, because an
        /// instance nested inside another missing one would otherwise be replaced and
        /// then deleted along with its parent.
        /// </summary>
        static List<GameObject> Broken()
        {
            var found = new List<GameObject>();
            foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!PrefabUtility.IsPrefabAssetMissing(go)) continue;

                Transform parent = go.transform.parent;
                if (parent != null && PrefabUtility.IsPrefabAssetMissing(parent.gameObject))
                    continue;

                found.Add(go);
            }
            return found;
        }

        // A prop whose whole model imports smaller than this is not a small prop, it is
        // a unit mistake.
        //
        // Measured rather than guessed, over every FBX in both packs, at the size Unity
        // currently gives them: the dungeon pack spans 0.2 cm to 5.8 cm and the AA Low
        // Poly pack starts at 21.6 cm and goes up. Nothing at all lands in between, so
        // 12 cm sits in the middle of a clean gap -- it catches all 48 of the mislabelled
        // models and none of the 855 correct ones, with about a factor of two of margin
        // either side.
        //
        // The first threshold was 5 cm and let Arch and Statue_Horse through at 5.8 and
        // 5.2, which are the same fault in bigger models.
        const float TooSmall = 0.12f;

        static readonly HashSet<string> Rescaled = new HashSet<string>();

        /// <summary>
        /// Makes an FBX import at the size it was modelled at, where its file lies about
        /// its own units.
        ///
        /// This is what made the swapped props microscopic. The dungeon pack's FBX
        /// declare UnitScaleFactor 1 -- "one unit is one centimetre" -- while their
        /// vertices are plainly in metres: the barrel's raw box is 1.04 x 1.03 x 1.35.
        /// The exporter papered over it with Lcl Scaling 100 on the node, which Unity
        /// does not carry onto the imported root. So Unity dutifully scaled the whole
        /// model by 0.01 and produced a one-centimetre barrel, which at the instance's
        /// own 0.4555 scale is four and a half millimetres.
        ///
        /// The same pack's .blend files go through Blender's exporter with Unity's own
        /// settings and come out in metres, which is why they were the right size and
        /// their FBX twins are not.
        ///
        /// It is NOT applied to everything, because for most models the declaration is
        /// true: the AA Low Poly chain is 15 x 114 units and really is in centimetres,
        /// and forcing metres on that would hang a 114-metre chain in the arena. The
        /// test is the imported size -- below five centimetres for an entire prop, the
        /// file is wrong about its units.
        /// </summary>
        static bool MatchUnits(string path)
        {
            if (Rescaled.Contains(path)) return false;
            Rescaled.Add(path);

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return false;

            float biggest = 0f;
            foreach (MeshFilter filter in asset.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    biggest = Mathf.Max(biggest, filter.sharedMesh.bounds.size.magnitude);

            if (biggest <= 0f || biggest >= TooSmall) return false;

            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || !importer.useFileScale) return false;

            importer.useFileScale = false;
            importer.globalScale = 1f;
            importer.SaveAndReimport();

            Debug.Log($"Blend props: {Path.GetFileName(path)} imported at " +
                      $"{biggest:0.###} m across -- its file claims centimetres but is " +
                      "modelled in metres. Switched that model to 1 unit = 1 metre.");
            return true;
        }

        static GameObject Swap(GameObject broken, string path)
        {
            MatchUnits(path);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogWarning($"Blend props: {path} would not load, so {broken.name} " +
                                 "was left as it is.");
                return null;
            }

            Transform old = broken.transform;
            var made = PrefabUtility.InstantiatePrefab(asset, old.parent) as GameObject;
            if (made == null) return null;

            Undo.RegisterCreatedObjectUndo(made, "Replace broken .blend props");

            // The CLEANED name. broken.name still carries Unity's
            // "(Missing Prefab with guid: ...)", and copying that onto a prop that is
            // now perfectly fine would leave the Hierarchy reading as broken for ever.
            made.name = MissingSuffix.Replace(broken.name, string.Empty).Trim();
            made.layer = broken.layer;
            made.transform.localPosition = old.localPosition;
            made.transform.localRotation = old.localRotation;
            made.transform.localScale = old.localScale;
            made.transform.SetSiblingIndex(old.GetSiblingIndex());
            made.SetActive(broken.activeSelf);

            Undo.DestroyObjectImmediate(broken);
            return made;
        }
    }
}

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
    /// WHAT IT CANNOT DO. A missing prefab instance has no components and no children
    /// loaded -- Unity keeps the GameObject and its transform and nothing else. So this
    /// restores position, rotation, scale, name, parent, sibling order, layer and active
    /// state, and it cannot restore a component somebody added to one of those instances,
    /// because that information is not in the scene any more either. For plain scenery
    /// that is everything there was.
    ///
    /// Anything with no .fbx of its own is listed and left alone. In this project that is
    /// St.blend and St2.blend, which are not from the pack and have no twin.
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
            int replaced = 0;

            var traced = new List<string>();
            foreach (GameObject go in broken)
            {
                string baseName = SourceName(go);

                // The first few, raw name and all. A tool that cannot say WHY it matched
                // nothing is a tool that gets argued with instead of fixed.
                if (traced.Count < 5)
                    traced.Add($"  \"{go.name}\"  ->  looked up \"{baseName}\"");

                if (!models.TryGetValue(baseName, out string path))
                {
                    stuck.TryGetValue(baseName, out int n);
                    stuck[baseName] = n + 1;
                    continue;
                }

                if (dryRun)
                {
                    done.Add($"  {go.name}  ->  {path}");
                    replaced++;
                    continue;
                }

                if (Swap(go, path)) { replaced++; done.Add($"  {go.name}  ->  {path}"); }
            }

            if (!dryRun && replaced > 0)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

            var message = new System.Text.StringBuilder();
            message.AppendLine(dryRun
                ? $"Blend props (DRY RUN, nothing changed): {replaced} of {broken.Count} " +
                  $"broken objects have an FBX of the same name, out of {models.Count} " +
                  "FBX in the project."
                : $"Blend props: {replaced} of {broken.Count} replaced, from " +
                  $"{models.Count} FBX in the project.");

            // Long lists are worse than useless in the console; the first few say whether
            // the names matched the way you expected, which is the thing to check.
            int show = Mathf.Min(done.Count, 12);
            for (int i = 0; i < show; i++) message.AppendLine(done[i]);
            if (done.Count > show) message.AppendLine($"  ... and {done.Count - show} more");

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

        static bool Swap(GameObject broken, string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogWarning($"Blend props: {path} would not load, so {broken.name} " +
                                 "was left as it is.");
                return false;
            }

            Transform old = broken.transform;
            var made = PrefabUtility.InstantiatePrefab(asset, old.parent) as GameObject;
            if (made == null) return false;

            Undo.RegisterCreatedObjectUndo(made, "Replace broken .blend props");

            made.name = broken.name;
            made.layer = broken.layer;
            made.transform.localPosition = old.localPosition;
            made.transform.localRotation = old.localRotation;
            made.transform.localScale = old.localScale;
            made.transform.SetSiblingIndex(old.GetSiblingIndex());
            made.SetActive(broken.activeSelf);

            Undo.DestroyObjectImmediate(broken);
            return true;
        }
    }
}

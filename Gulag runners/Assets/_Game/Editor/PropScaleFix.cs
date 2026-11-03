using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts back the props a change of import scale resized, by measuring them.
    ///
    /// WHY THIS KEEPS HAPPENING. A model's import scale is a hidden global. It lives in
    /// the .meta beside the model, every scene that uses that model was dressed against
    /// whatever it was at the time, and changing it resizes all of them at once --
    /// silently, with no merge conflict and nothing in any scene file to show for it.
    /// Half this project's packs declare UnitScaleFactor 1, "one unit is one
    /// centimetre", over vertices that are plainly in metres, so a prop imports at a
    /// hundredth of its size and whoever placed it compensated with a scale of 100 or
    /// 300 on the instance. Correct the model afterwards and those instances are
    /// suddenly a hundred times too big.
    ///
    /// Which is not hypothetical: fixing the dungeon pack's seventeen models for one
    /// arena left the loot chests in Arena_Demo and the menu at SIX MILLIMETRES, because
    /// their instances were sitting at a scale of 0.005 to cancel out the old import.
    /// Nobody touched those scenes.
    ///
    /// WHAT IT DOES. It asks the only question that has an answer: how big is this
    /// thing, in metres, next to a 1.8 m fighter. Renderer.bounds gives that exactly,
    /// including every scale above it in the hierarchy. Anything implausible is tried
    /// against the powers of ten -- the only factor a units mistake ever produces --
    /// and the one that lands it in a sane range wins. A prop that is already a sane
    /// size is never touched, whatever its scale number looks like.
    ///
    /// It never changes an importer. That is the thing that caused this, and doing it
    /// from here would fix one scene and break the others in the same stroke.
    /// </summary>
    public static class PropScaleFix
    {
        /// <summary>
        /// What a prop may be, in metres. Generous on purpose: a wall is allowed to be
        /// big and a coin is allowed to be small. This is for catching factors of a
        /// hundred, not for having an opinion about set dressing.
        /// </summary>
        const float TooSmall = 0.03f;
        const float TooBig = 12.0f;

        /// <summary>
        /// Where a repaired prop should land. Narrower than the band above, because
        /// "not obviously broken" is the test for leaving something alone and "clearly
        /// right" is the test for changing it.
        /// </summary>
        const float WantSmall = 0.08f;
        const float WantBig = 8.0f;

        /// <summary>The only factors a units mistake produces. Nothing else is tried.</summary>
        static readonly float[] Factors = { 100f, 0.01f, 10f, 0.1f, 1000f, 0.001f };

        [MenuItem("Tools/Gulag Runners/Setup/List props the wrong size")]
        public static void List() => Run(dryRun: true);

        [MenuItem("Tools/Gulag Runners/Setup/Fix props the wrong size")]
        public static void Fix() => Run(dryRun: false);

        static void Run(bool dryRun)
        {
            var fixedUp = new List<string>();
            var stuck = new List<string>();
            int looked = 0;

            foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // One per prefab instance, not one per renderer: a prop is the thing
                // somebody placed, and its scale lives on its root.
                if (PrefabUtility.GetOutermostPrefabInstanceRoot(go) != go) continue;

                if (!Measure(go, out float span)) continue;
                looked++;
                if (span >= TooSmall && span <= TooBig) continue;

                float pick = 0f;
                foreach (float f in Factors)
                {
                    float after = span * f;
                    if (after >= WantSmall && after <= WantBig) { pick = f; break; }
                }

                if (pick == 0f)
                {
                    stuck.Add($"  {go.name}: {span:0.###} m across, and no power of ten " +
                              "brings it into a sane range -- so this is not a units " +
                              "mistake and somebody meant it");
                    continue;
                }

                fixedUp.Add($"  {go.name}: {span:0.###} m -> {span * pick:0.###} m " +
                            $"(scale x{pick})");

                if (dryRun) continue;
                Undo.RecordObject(go.transform, "Fix props the wrong size");
                go.transform.localScale *= pick;
                PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            }

            var message = new System.Text.StringBuilder();
            message.AppendLine(dryRun
                ? $"Prop scale (DRY RUN, nothing changed): {fixedUp.Count} of {looked} " +
                  "prop(s) are not a size anything could be."
                : $"Prop scale: {fixedUp.Count} of {looked} prop(s) resized.");

            int show = Mathf.Min(fixedUp.Count, 20);
            for (int i = 0; i < show; i++) message.AppendLine(fixedUp[i]);
            if (fixedUp.Count > show)
                message.AppendLine($"  ... and {fixedUp.Count - show} more");

            if (stuck.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Left alone:");
                foreach (string line in stuck) message.AppendLine(line);
            }

            if (!dryRun && fixedUp.Count > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                message.AppendLine("Scene saved.");
            }
            else if (dryRun && fixedUp.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Run \"Fix props the wrong size\" to do it. It only " +
                                   "ever multiplies an instance's scale by a power of " +
                                   "ten, and never touches a model's importer -- that " +
                                   "is the setting that caused this, and changing it " +
                                   "from here would fix one scene and break the rest.");
            }

            Debug.Log(message.ToString());
        }

        /// <summary>
        /// How big this prop is in the world, across its widest axis.
        ///
        /// From the renderers rather than the transform, because the transform's scale
        /// is a number about a model whose own size nobody can see from the inspector.
        /// Renderer.bounds is the answer in metres with every scale above it already in
        /// it, which is exactly the question.
        /// </summary>
        static bool Measure(GameObject go, out float span)
        {
            span = 0f;
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;

            Bounds box = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) box.Encapsulate(renderers[i].bounds);

            span = Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
            return span > 0f;
        }
    }
}

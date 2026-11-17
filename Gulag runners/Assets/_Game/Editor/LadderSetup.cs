using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the four ladders back in the arena, and makes them climbable again.
    ///
    /// They were the only props the general swap could not help. Every other broken
    /// object in this scene came from a pack that ships the same model as .fbx as well;
    /// St.blend and St2.blend are Vlad's own and had no twin, so eighty props were fixed
    /// and four holes were left -- the two routes off the ground floor and the two up
    /// the end platforms. docs/refs/blend_to_fbx.py has since exported them with the
    /// Blender python module, and St.fbx and St2.fbx sit beside the .blend files.
    ///
    /// This is not just the swap again with two more files, for three reasons.
    ///
    /// THE MARKER WOULD BE LOST. A missing prefab keeps its added components -- the
    /// Inspector shows the SimCollider on "St (Missing Prefab with guid: 7660...)" with
    /// Kind set to Ladder -- and the general swap says plainly that it cannot carry a
    /// component across. For scenery that costs nothing. For a ladder it costs the only
    /// thing that makes it a ladder, and the arena is already down to no ladders at all:
    /// the bake reads 0, and arena_demo_test's "both ladders survived the bake" has been
    /// red since the dressed scene came in.
    ///
    /// THE BOX WOULD BE A UNIT CUBE. SimCollider.LocalBox asks the object it is on for a
    /// BoxCollider and then for a MeshFilter, and an FBX instance root has neither --
    /// the mesh is on the child. That bakes a one-unit box scaled by 0.019, which is a
    /// two-centimetre ladder, invisible to the simulation and hard to see in a gizmo.
    /// So the marker gets a BoxCollider sized to the model, the way Bounds_Left in this
    /// same scene does it, and the box then matches the art exactly.
    ///
    /// THE MODEL MAY ARRIVE TURNED. Blender models Z-up, Unity reads Y-up, and the turn
    /// rides on the model's root node, which some importers keep and some collapse away.
    /// Rather than bet on it, this measures what actually came in and turns it back if
    /// it needs it -- so the transform Vlad authored against the .blend keeps meaning
    /// what he meant by it.
    ///
    /// Everything it decides, it prints: the model's own box, the ladder's world box,
    /// and the floor each end of it lands on. A ladder with a foot in mid air is a
    /// warning, not a silent pass.
    /// </summary>
    public static class LadderSetup
    {
        /// <summary>The two models, by file name without extension.</summary>
        static readonly string[] LadderModels = { "St", "St2" };

        static readonly Regex MissingSuffix = new Regex(
            @"\s*\(Missing Prefab with guid:\s*([0-9a-fA-F]{32})\s*\)\s*$");

        /// <summary>
        /// How close an end has to be to a floor's top to count as standing on it. A
        /// ladder's foot sits on the floor it leaves and its head pokes above the floor
        /// it reaches, so this is deliberately loose: it is a sanity check on placement,
        /// not a tolerance anything is snapped to.
        /// </summary>
        const float FloorReach = 0.45f;

        /// <summary>
        /// Below this the model did not import at the size it was modelled at. No ladder
        /// in any game is half a metre tall, so a ladder shorter than this is a units
        /// mistake in the file rather than a small ladder -- the same fault that made
        /// the whole dungeon pack four millimetres across.
        /// </summary>
        const float TooShort = 0.5f;

        [MenuItem("Tools/Gulag Runners/Setup/Build the arena ladders")]
        public static void Build()
        {
            var models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                if (Array.IndexOf(LadderModels, name) >= 0 && !models.ContainsKey(name))
                    models[name] = path;
            }

            if (models.Count == 0)
            {
                Debug.LogError(
                    "Ladders: there is no St.fbx or St2.fbx in the project. They are " +
                    "produced by docs/refs/blend_to_fbx.py from St.blend and St2.blend " +
                    "and are meant to be committed next to them, so this is a missing " +
                    "file rather than anything to fix in the scene.");
                return;
            }

            var report = new StringBuilder();
            var parts = new List<(GameObject ladder, string model, string from,
                                  SimColliderKind kind, bool carried)>();
            int swapped = 0, oriented = 0, marked = 0;

            foreach (GameObject placeholder in Placeholders(models))
            {
                string model = ModelOf(placeholder, models);
                if (model == null) continue;

                // Read the marker BEFORE the object can be destroyed. The whole point of
                // a ladder-specific tool is that this survives the swap.
                SimCollider had = placeholder.GetComponent<SimCollider>();
                SimColliderKind kind = had != null ? had.kind : SimColliderKind.Ladder;
                bool carried = had != null;

                GameObject ladder = placeholder;
                if (PrefabUtility.IsPrefabAssetMissing(placeholder))
                {
                    ladder = Swap(placeholder, models[model]);
                    if (ladder == null) continue;
                    swapped++;
                }

                models.TryGetValue(model, out string from);
                parts.Add((ladder, model, from, kind, carried));

                if (Orient(ladder)) oriented++;
                if (Mark(ladder, kind)) marked++;
            }

            if (parts.Count == 0)
            {
                Debug.Log("Ladders: nothing in the open scene(s) came from St.blend, " +
                          "St2.blend, St.fbx or St2.fbx, so there was nothing to build.");
                return;
            }

            // A model that came in at a hundredth of its size cannot be fixed on the
            // instance: the importer has to be told, and then every instance follows.
            // Everything measured before that is stale, so the whole pass runs again.
            var rescaled = new List<string>();
            foreach (var part in parts)
                if (Height(part.ladder) < TooShort) MatchUnits(part.from, rescaled);

            if (rescaled.Count > 0)
                foreach (var part in parts)
                {
                    Orient(part.ladder);
                    Mark(part.ladder, part.kind);
                }

            foreach (var part in parts)
                report.AppendLine("  " + Describe(part.ladder, part.model, part.carried));

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Selection.objects = parts.ConvertAll(p => p.ladder).ToArray();

            var message = new StringBuilder();
            message.AppendLine($"Ladders: {parts.Count} in the scene -- {swapped} swapped " +
                               $"in from FBX, {oriented} turned upright, {marked} given a " +
                               "Ladder marker and a box. Scene saved.");
            foreach (string path in rescaled)
                message.AppendLine($"  {path} imported too small for a ladder; switched " +
                                   "that model to 1 unit = 1 metre.");
            message.Append(report);
            message.AppendLine("  All of them are selected -- hover the Scene view and " +
                               "press F to look.");
            Debug.Log(message.ToString());
        }

        // ------------------------------------------------------------------ finding them

        /// <summary>
        /// Every object in the open scenes that is one of the ladders: the broken ones,
        /// and the ones a previous run already built, so this is safe to run twice.
        /// </summary>
        static List<GameObject> Placeholders(Dictionary<string, string> models)
        {
            var found = new List<GameObject>();
            foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ModelOf(go, models) == null) continue;

                // Only the outermost object of each instance. A child of one is part of
                // the same ladder, not another ladder.
                Transform parent = go.transform.parent;
                if (parent != null && ModelOf(parent.gameObject, models) != null) continue;

                found.Add(go);
            }
            return found;
        }

        /// <summary>Which of the two models this object came from, or null.</summary>
        static string ModelOf(GameObject go, Dictionary<string, string> models)
        {
            // Broken: Unity writes the source guid into the object's own name.
            Match missing = MissingSuffix.Match(go.name);
            if (missing.Success)
            {
                string source = AssetDatabase.GUIDToAssetPath(missing.Groups[1].Value);
                string name = string.IsNullOrEmpty(source)
                    ? null : Path.GetFileNameWithoutExtension(source);
                return name != null && models.ContainsKey(name) ? name : null;
            }

            // Already built: ask the prefab it is an instance of.
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (root == null || root != go) return null;
            UnityEngine.Object asset =
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            if (asset == null) return null;

            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return null;
            string stem = Path.GetFileNameWithoutExtension(path);
            return models.ContainsKey(stem) ? stem : null;
        }

        static GameObject Swap(GameObject broken, string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogWarning($"Ladders: {path} would not load, so {broken.name} was " +
                                 "left as it is.");
                return null;
            }

            Transform old = broken.transform;
            var made = PrefabUtility.InstantiatePrefab(asset, old.parent) as GameObject;
            if (made == null) return null;

            Undo.RegisterCreatedObjectUndo(made, "Build the arena ladders");

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

        // ------------------------------------------------------------------ orientation

        /// <summary>
        /// Turns the model inside the instance so its own axes line up with the ones the
        /// placement was authored against: longest on X, then Y, then Z.
        ///
        /// That ordering is not a convention somebody picked, it is what the two models
        /// are -- St is 95.2 x 5.3 x 3.1 as modelled, St2 is 135.2 x 5.3 x 3.2 -- and it
        /// is the ordering Vlad's scale was dialled in against: 95.2 by 0.018768 is the
        /// 1.79 m from the ground floor to the walkway, and the squashed 0.154 on Z is
        /// what makes the rungs 48 cm wide rather than a metre and a half.
        ///
        /// Returns true if it had to turn anything.
        /// </summary>
        static bool Orient(GameObject ladder)
        {
            Bounds local = ModelBounds(ladder);
            if (local.size == Vector3.zero) return false;

            bool turned = false;
            Vector3 e = local.size;
            if (!(e.x >= e.y && e.y >= e.z))
            {
                // The axes of the model, biggest first.
                int[] order = { 0, 1, 2 };
                for (int i = 0; i < 2; i++)
                    for (int j = i + 1; j < 3; j++)
                        if (e[order[j]] > e[order[i]])
                            (order[i], order[j]) = (order[j], order[i]);

                Vector3 a0 = Axis(order[0]), a1 = Axis(order[1]), a2 = Axis(order[2]);

                // A permutation of three axes is as likely to be a mirror as a turn, and
                // a mirrored ladder has its faces inside out. Flipping the last axis
                // costs nothing on a symmetrical model and keeps it a rotation.
                if (Vector3.Dot(Vector3.Cross(a0, a1), a2) < 0f) a2 = -a2;

                // Columns a0,a1,a2 is the rotation that takes X,Y,Z ONTO the model's
                // axes; the one wanted is its inverse, taking the model's axes to X,Y,Z.
                Turn(ladder, Quaternion.Inverse(Quaternion.LookRotation(a2, a1)));
                turned = true;
            }

            return StandOnTheFloor(ladder) || turned;
        }

        /// <summary>
        /// Puts the ladder's foot on the floor rather than its head, end for end.
        ///
        /// WHY THIS IS MEASURED AND NOT KNOWN. Both models are built hanging DOWN from
        /// their origin -- St runs from -89 to +6.2 along its length -- and an FBX is a
        /// right-handed file read by a left-handed engine, so one axis comes back
        /// negated and which one is the importer's business, not the file's. Get it
        /// wrong and the ladder is a metre and a half underground: measured against this
        /// arena, the two choices are a foot 2 cm off the floor and a foot 1.57 m in
        /// mid air. That is far too big a difference to leave to a convention.
        ///
        /// So both are tried and the one whose foot is nearest a floor underneath it
        /// wins. The turn is 180 degrees about the model's thinnest axis, which is a
        /// rotation rather than a mirror -- the faces stay the right way out, and on
        /// eight evenly spaced rungs there is nothing to see either way.
        /// </summary>
        static bool StandOnTheFloor(GameObject ladder)
        {
            float before = FootGap(ladder);
            Turn(ladder, Quaternion.AngleAxis(180f, Vector3.forward));
            float after = FootGap(ladder);

            if (after <= before) return true;

            Turn(ladder, Quaternion.AngleAxis(180f, Vector3.forward));
            return false;
        }

        /// <summary>How far the bottom of the ladder is from the nearest floor top.</summary>
        static float FootGap(GameObject ladder)
        {
            Bounds world = WorldBox(ladder);
            float best = float.MaxValue;

            foreach (SimCollider other in UnityEngine.Object.FindObjectsByType<SimCollider>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (other.gameObject == ladder) continue;
                if (other.kind != SimColliderKind.Solid &&
                    other.kind != SimColliderKind.OneWay) continue;

                Bounds b = other.WorldBounds;
                if (b.max.x < world.min.x - 0.5f || b.min.x > world.max.x + 0.5f) continue;
                best = Mathf.Min(best, Mathf.Abs(b.max.y - world.min.y));
            }
            return best;
        }

        /// <summary>Turns the model inside the instance, leaving the instance alone.</summary>
        static void Turn(GameObject ladder, Quaternion turn)
        {
            foreach (Transform child in ladder.transform)
            {
                Undo.RecordObject(child, "Build the arena ladders");
                child.localRotation = turn * child.localRotation;
                child.localPosition = turn * child.localPosition;
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(ladder.transform);
        }

        static Vector3 Axis(int i) =>
            i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>The model's own box, in the instance root's space.</summary>
        static Bounds ModelBounds(GameObject ladder)
        {
            var box = new Bounds();
            bool any = false;
            Transform root = ladder.transform;

            foreach (MeshFilter filter in ladder.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;

                Bounds b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p = root.InverseTransformPoint(
                        filter.transform.TransformPoint(corner));
                    if (!any) { box = new Bounds(p, Vector3.zero); any = true; }
                    else box.Encapsulate(p);
                }
            }
            return any ? box : new Bounds(Vector3.zero, Vector3.zero);
        }

        // ------------------------------------------------------------------ the marker

        /// <summary>
        /// Gives the ladder back its SimCollider and a BoxCollider that matches the art,
        /// so the bake gets the ladder's real box instead of a unit cube.
        /// </summary>
        static bool Mark(GameObject ladder, SimColliderKind kind)
        {
            bool changed = false;

            SimCollider marker = ladder.GetComponent<SimCollider>();
            if (marker == null)
            {
                marker = Undo.AddComponent<SimCollider>(ladder);
                changed = true;
            }
            if (marker != null && marker.kind != kind)
            {
                Undo.RecordObject(marker, "Build the arena ladders");
                marker.kind = kind;
                changed = true;
            }

            Bounds local = ModelBounds(ladder);
            if (local.size != Vector3.zero)
            {
                BoxCollider box = ladder.GetComponent<BoxCollider>();
                if (box == null)
                {
                    box = Undo.AddComponent<BoxCollider>(ladder);
                    changed = true;
                }
                if (box != null && (box.center != local.center || box.size != local.size))
                {
                    Undo.RecordObject(box, "Build the arena ladders");
                    box.center = local.center;
                    box.size = local.size;
                    // A ladder is climbed, not bumped into. The simulation does its own
                    // collision from the marker and never asks PhysX, so this box is here
                    // to be measured, not to push anything.
                    box.isTrigger = true;
                    changed = true;
                }
            }

            return changed;
        }

        // ------------------------------------------------------------------ measuring

        static float Height(GameObject ladder)
        {
            Bounds world = WorldBox(ladder);
            return world.size.y;
        }

        static Bounds WorldBox(GameObject ladder)
        {
            Bounds local = ModelBounds(ladder);
            if (local.size == Vector3.zero)
                return new Bounds(ladder.transform.position, Vector3.zero);

            Transform root = ladder.transform;
            var box = new Bounds();
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3(
                    (c & 1) == 0 ? local.min.x : local.max.x,
                    (c & 2) == 0 ? local.min.y : local.max.y,
                    (c & 4) == 0 ? local.min.z : local.max.z);
                Vector3 p = root.TransformPoint(corner);
                if (c == 0) box = new Bounds(p, Vector3.zero);
                else box.Encapsulate(p);
            }
            return box;
        }

        /// <summary>
        /// One line per ladder: what it came from, how big it is, and whether both ends
        /// of it meet a floor. The last part is the only one worth arguing with.
        /// </summary>
        static string Describe(GameObject ladder, string model, bool carriedMarker)
        {
            Bounds world = WorldBox(ladder);
            string foot = Floor(world.min.y, world);
            string head = Floor(world.max.y, world);

            string marker = carriedMarker ? "marker carried over" : "marker added";
            return $"{ladder.name} ({model}): {world.size.x:0.00} x {world.size.y:0.00} x " +
                   $"{world.size.z:0.00} m, y {world.min.y:0.00} to {world.max.y:0.00} at " +
                   $"x {world.center.x:0.00}; foot {foot}, head {head}; {marker}.";
        }

        /// <summary>
        /// The solid whose top is nearest this height under the ladder's footprint, said
        /// in words. "in mid air" is the answer worth reading.
        /// </summary>
        static string Floor(float y, Bounds ladder)
        {
            float best = float.MaxValue;
            float bestTop = 0f;

            foreach (SimCollider other in UnityEngine.Object.FindObjectsByType<SimCollider>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (other.kind != SimColliderKind.Solid &&
                    other.kind != SimColliderKind.OneWay) continue;

                Bounds b = other.WorldBounds;
                if (b.max.x < ladder.min.x - 0.5f || b.min.x > ladder.max.x + 0.5f) continue;

                float gap = Mathf.Abs(b.max.y - y);
                if (gap < best) { best = gap; bestTop = b.max.y; }
            }

            if (best > FloorReach) return "in mid air";
            return $"on the floor at y {bestTop:0.00}";
        }

        /// <summary>
        /// Makes a model import at the size it was modelled at. Same fault, and same
        /// fix, as the dungeon pack: the file's own units are not to be trusted, and the
        /// only honest test is whether what came out could be the thing it claims to be.
        /// </summary>
        static bool MatchUnits(string path, List<string> rescaled)
        {
            if (string.IsNullOrEmpty(path) || rescaled.Contains(path)) return false;

            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || !importer.useFileScale) return false;

            importer.useFileScale = false;
            importer.globalScale = 1f;
            importer.SaveAndReimport();
            rescaled.Add(path);
            return true;
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Makes the slung cage, its chains and the plank bridges give under weight.
    ///
    /// The shader bends them; this measures what it needs to know and wires the load up
    /// to it. None of that can be guessed, which is why it is a tool and not a default:
    /// these props came out of three different packs with three different ideas about
    /// which way a model faces, so a chain whose length the shader thinks runs along X
    /// stretches sideways into the tree instead of down.
    ///
    /// WHAT IT MEASURES, per renderer, off the mesh's own bounds:
    ///
    ///   the span axis   the longest edge of the bounding box, in object space. For a
    ///                   plank that is its length; for a chain, its drop.
    ///   the fixed end   the end of that axis the thing hangs FROM. For a hanger that is
    ///                   whichever end is higher in the world, which is the one piece of
    ///                   this that needs the object placed in the scene first.
    ///   down            world down, brought back into object space, because the sag is
    ///                   towards the ground and not towards the model's own -Y.
    ///
    /// SPAN OR HANGER is decided by how the long axis sits in the world: a thing whose
    /// length runs mostly up and down hangs from its top, anything else is slung between
    /// two ends. That gets the chains right without naming them.
    ///
    /// AND WHO CARRIES WHOM. A chain does not feel the fighter, it feels the cage. Each
    /// hanger is pointed at the nearest span below it, so the cage and its four chains
    /// move as one assembly. The log says which went with which; if it guesses wrong the
    /// field is right there on the component.
    ///
    /// Everything it makes is an asset or a serialised component, visible in the
    /// Inspector afterwards. Nothing is created at play time.
    /// </summary>
    public static class SaggingSpanSetup
    {
        const string ShaderName = "Gulag Runners/Sagging Span";
        const string MaterialFolder = "Assets/_Game/Materials";

        // What this arena hangs. Matched on the mesh name, which for these packs is the
        // model's own name. Selection wins over this list when there is one.
        static readonly string[] Hung =
        {
            "Bridge_5_Brown", "Chain_1_Black", "Cage_1_Black", "Floor_5_Brown",
        };

        // Things that hang. This list is the WHOLE test, and it is a list rather than a
        // measurement because measuring was tried twice and was wrong both times.
        //
        // First it ran per RENDERER, off each one's bounding box: two uprights of
        // Bridge_5_Brown came out as hangers because an upright is tall, and
        // Chain_1_Black came out as a span because that chain's longest local axis is
        // not its drop. Measuring the whole prop instead fixed those and promptly made
        // the CAGE a hanger, because a cage is taller than it is wide.
        //
        // The thing a box cannot tell you is the one that matters: a chain hangs, and a
        // cage, a bridge and a floor are slung between two ends and stood on. That is
        // about what the object IS for, and the only honest source for it here is its
        // name. Short, visible, and wrong in a way somebody can see and fix.
        static readonly string[] HangerNames = { "chain", "rope", "cable" };

        [MenuItem("Tools/Gulag Runners/Setup/Set up sagging spans")]
        public static void Apply()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"Sagging spans: the shader \"{ShaderName}\" is not in the " +
                               "project. It lives in Assets/_Game/Shaders/SaggingSpan.shader.");
                return;
            }

            List<MeshRenderer> targets = Targets();
            if (targets.Count == 0)
            {
                Debug.LogWarning(
                    "Sagging spans: nothing to do. Select the cage, the chains and the " +
                    "bridges and run this again, or leave nothing selected and it will " +
                    "look for " + string.Join(", ", Hung) + ".");
                return;
            }

            Transform[] fighters = Fighters();
            var made = new List<SaggingSpan>();
            var report = new List<string>();

            foreach (MeshRenderer renderer in targets)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                bool hanger = Hangs(renderer);
                SaggingSpan span = Rig(renderer, filter.sharedMesh, shader, fighters,
                                       hanger, out string note);
                if (span == null) continue;

                made.Add(span);
                report.Add($"  {renderer.name}: {(hanger ? "hangs from its top" : "slung between ends")}, {note}");
            }

            // Chains carry the cage under them, not the fighter.
            int followed = Carry(made, report);

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Sagging spans: {made.Count} set up, {followed} hanger(s) pointed at the " +
                $"span they carry.\n" + string.Join("\n", report) +
                "\n\nThe give is on the MATERIALS (rest droop, load droop, how hard the " +
                "ends hold) and the settle is on the COMPONENTS (stiffness, damping). " +
                "Keep the load droop near a few centimetres: the fighters still stand on " +
                "the collision the bake made, so a deep sag makes them hover.");
        }

        // ------------------------------------------------------------------ what to rig

        static List<MeshRenderer> Targets()
        {
            var found = new List<MeshRenderer>();

            if (Selection.gameObjects != null && Selection.gameObjects.Length > 0)
            {
                foreach (GameObject go in Selection.gameObjects)
                    found.AddRange(go.GetComponentsInChildren<MeshRenderer>(true));
                return found;
            }

            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                foreach (string name in Hung)
                {
                    if (f.sharedMesh.name.StartsWith(name) || r.name.StartsWith(name))
                    {
                        found.Add(r);
                        break;
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Does this thing hang from its top, or is it slung between two ends?
        ///
        /// Decided for the whole prop, not for each piece of it, and by name first. See
        /// HangerNames for what that saves.
        /// </summary>
        static bool Hangs(MeshRenderer renderer)
        {
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject);
            if (root == null) root = renderer.gameObject;

            string name = root.name.ToLowerInvariant();
            foreach (string hint in HangerNames)
                if (name.Contains(hint)) return true;

            return false;
        }

        static Transform[] Fighters()
        {
            var list = new List<Transform>();
            foreach (PlayerController p in Object.FindObjectsByType<PlayerController>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                list.Add(p.transform);
            return list.ToArray();
        }

        // ------------------------------------------------------------------- the rig

        static SaggingSpan Rig(MeshRenderer renderer, Mesh mesh, Shader shader,
                               Transform[] fighters, bool hanger, out string note)
        {
            Transform t = renderer.transform;
            Bounds box = mesh.bounds;
            Vector3 axisOS;
            float lengthOS;

            if (hanger)
            {
                // A chain runs DOWNWARDS. Not along its longest local axis -- that is
                // whatever the modeller happened to build it along, and on this pack's
                // chain it is not the drop. Taking world down and bringing it back into
                // object space needs no guess and cannot be wrong about which way a
                // hanging thing hangs.
                axisOS = t.InverseTransformDirection(Vector3.down).normalized;
                lengthOS = Mathf.Abs(box.size.x * axisOS.x)
                         + Mathf.Abs(box.size.y * axisOS.y)
                         + Mathf.Abs(box.size.z * axisOS.z);
            }
            else
            {
                // Slung: the longest edge of its own box is the way it runs.
                Vector3 size = box.size;
                int longest = size.x >= size.y && size.x >= size.z
                            ? 0 : (size.y >= size.z ? 1 : 2);
                axisOS = longest == 0 ? Vector3.right
                       : longest == 1 ? Vector3.up : Vector3.forward;
                lengthOS = size[longest];
            }

            if (lengthOS <= 1e-4f)
            {
                note = "skipped, its mesh has no length to speak of";
                return null;
            }

            Vector3 low = box.center - axisOS * (lengthOS * 0.5f);
            Vector3 high = box.center + axisOS * (lengthOS * 0.5f);

            // A hanger is fixed at whichever end is higher in the world; a span may run
            // either way along its axis and it does not matter which end we call zero.
            Vector3 originOS = low;
            if (hanger && t.TransformPoint(low).y < t.TransformPoint(high).y)
            {
                originOS = high;
                axisOS = -axisOS;
            }

            Material material = MaterialFor(renderer, mesh, shader, hanger);
            if (material == null)
            {
                note = "skipped, could not make its material";
                return null;
            }

            Undo.RecordObject(material, "Set up sagging spans");
            material.SetVector("_SpanOrigin", originOS);
            material.SetVector("_SpanAxis", axisOS.normalized);
            material.SetFloat("_SpanLength", lengthOS);

            // Down, in this object's space. Not -Y: these models are rotated into place
            // and the sag goes towards the ground, not towards the model's own floor.
            material.SetVector("_SagDir", t.InverseTransformDirection(Vector3.down).normalized);

            material.SetFloat("_RestSag", hanger ? 0.004f : 0.025f);
            material.SetFloat("_LoadSag", hanger ? 0.008f : 0.055f);
            material.SetFloat("_EndStiffness", hanger ? 1f : 0.85f);
            material.SetFloat("_LoadSpread", 0.12f);
            material.SetFloat("_LoadAt", 0.5f);
            material.SetFloat("_LoadAmount", 0f);
            material.SetFloat("_Sway", 0f);
            material.SetFloat("_Hanger", hanger ? 1f : 0f);
            if (hanger) material.EnableKeyword("_HANGER");
            else material.DisableKeyword("_HANGER");
            EditorUtility.SetDirty(material);

            Undo.RecordObject(renderer, "Set up sagging spans");
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);

            SaggingSpan span = renderer.GetComponent<SaggingSpan>();
            if (span == null) span = Undo.AddComponent<SaggingSpan>(renderer.gameObject);

            Undo.RecordObject(span, "Set up sagging spans");
            span.skin = new Renderer[] { renderer };
            span.spanOrigin = originOS;
            span.spanAxis = axisOS.normalized;
            span.spanLength = lengthOS;
            span.watch = fighters;
            span.swayPerLoad = hanger ? 0.06f : 0f;
            EditorUtility.SetDirty(span);

            float metres = t.TransformVector(axisOS * lengthOS).magnitude;
            note = $"{metres:0.00} m long, origin {originOS}, {fighters.Length} fighter(s) watched";
            return span;
        }

        /// <summary>
        /// Points every hanger at the span it is holding up.
        ///
        /// Nearest span whose middle is BELOW the hanger's free end, because a chain
        /// hangs down onto what it carries. Measured from the free end rather than from
        /// the object's pivot: these chains pivot at their tops, so pivot distance would
        /// pick the bridge above them every time.
        /// </summary>
        static int Carry(List<SaggingSpan> all, List<string> report)
        {
            var spans = new List<SaggingSpan>();
            foreach (SaggingSpan s in all)
                if (s.swayPerLoad <= 0f) spans.Add(s);        // the slung ones

            int wired = 0;
            foreach (SaggingSpan hanger in all)
            {
                if (hanger.swayPerLoad <= 0f) continue;        // not a hanger

                Vector3 free = hanger.transform.TransformPoint(
                    hanger.spanOrigin + hanger.spanAxis.normalized * hanger.spanLength);

                SaggingSpan best = null;
                float bestDistance = float.MaxValue;
                foreach (SaggingSpan span in spans)
                {
                    Vector3 middle = span.transform.TransformPoint(
                        span.spanOrigin + span.spanAxis.normalized * (span.spanLength * 0.5f));
                    if (middle.y > free.y + 0.25f) continue;   // that one is above us

                    float d = Vector3.Distance(middle, free);
                    if (d < bestDistance) { bestDistance = d; best = span; }
                }

                if (best == null) continue;
                Undo.RecordObject(hanger, "Set up sagging spans");
                hanger.follow = best;
                EditorUtility.SetDirty(hanger);
                wired++;
                report.Add($"  {hanger.name} -> carries {best.name} " +
                           $"({bestDistance:0.00} m below its free end)");
            }
            return wired;
        }

        // --------------------------------------------------------------- the materials

        /// <summary>
        /// One material per mesh and mode, carrying over the look it had.
        ///
        /// Per MESH rather than per object on purpose: four chains off one model share a
        /// material and so share a draw call, and they are driven apart by the property
        /// block the component writes, not by having a material each.
        /// </summary>
        static Material MaterialFor(MeshRenderer renderer, Mesh mesh, Shader shader,
                                    bool hanger)
        {
            string name = $"Sag_{Sanitise(mesh.name)}_{(hanger ? "Hanger" : "Span")}";
            string path = $"{MaterialFolder}/{name}.mat";

            // A mesh that changes mode between runs leaves its old material behind, and
            // a stale Sag_Chain_..._Span.mat sitting next to a correct
            // Sag_Chain_..._Hanger.mat looks exactly like the bug that made it. Nothing
            // uses it any more, so say so rather than deleting somebody's asset.
            string stale = $"{MaterialFolder}/Sag_{Sanitise(mesh.name)}" +
                           $"_{(hanger ? "Span" : "Hanger")}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(stale) != null)
                Debug.LogWarning($"Sagging spans: {stale} is left over from a run when " +
                                 $"{mesh.name} was rigged the other way round. Nothing " +
                                 "uses it now -- delete it.");

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader != shader) existing.shader = shader;
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                Debug.LogError($"Sagging spans: {MaterialFolder} does not exist.");
                return null;
            }

            Material made = new Material(shader) { name = name };

            // Keep whatever it looked like before. Replacing a prop's material with one
            // that bends is no use if the prop also turns white.
            Material was = renderer.sharedMaterial;
            if (was != null)
            {
                if (was.HasProperty("_BaseMap") && was.GetTexture("_BaseMap") != null)
                    made.SetTexture("_BaseMap", was.GetTexture("_BaseMap"));
                else if (was.HasProperty("_MainTex") && was.GetTexture("_MainTex") != null)
                    made.SetTexture("_BaseMap", was.GetTexture("_MainTex"));

                if (was.HasProperty("_BaseColor"))
                    made.SetColor("_BaseColor", was.GetColor("_BaseColor"));
                else if (was.HasProperty("_Color"))
                    made.SetColor("_BaseColor", was.GetColor("_Color"));
            }

            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }
}

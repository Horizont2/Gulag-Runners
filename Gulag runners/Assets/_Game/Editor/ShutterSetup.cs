using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the closing phase: the steel shutters of docs/01 and docs/18.
    ///
    /// The clock has announced the rooms closing at 1:00 for a while now and nothing has
    /// closed, because the arena has no gates in it. Everything else is already there — the
    /// bake tags a gate's boxes, the match clock decides which bits are up, the motor skips a
    /// solid whose gate has not shut, and GateView drops the slab — so this is placement, and
    /// placement is arithmetic.
    ///
    /// The plan comes out of the arena's own width rather than out of typed coordinates:
    /// three pairs of vertical shutters, each pair closer to the middle than the last,
    /// alternating left and right so the squeeze is felt from both sides, and a ceiling over
    /// what is left for sudden death. The last room comes out one screen wide, which is the
    /// state docs/01 is aiming at — two fighters, one room, nowhere to walk away to.
    ///
    /// Three things make a shutter work rather than merely exist, and all three are easy to
    /// miss by hand:
    ///
    ///   It has to seal every storey, because a shutter a fighter can climb round herds
    ///   nobody. These run from under the basement floor to over the top walkway.
    ///
    ///   It has to cover every DEPTH lane, because the fight is a flat lane through a 3D set
    ///   and a shutter one lane deep is a shutter you step behind.
    ///
    ///   It has to herd INWARDS. A shutter that only becomes solid can seal somebody into an
    ///   outer wing with nobody to fight, and the match then ends on the clock in two empty
    ///   rooms. Each one points at the middle.
    ///
    /// Re-running it moves the shutters it made before instead of adding more.
    /// </summary>
    public static class ShutterSetup
    {
        const string RootName = "Shutters";

        /// <summary>How far from the middle each pair stands, as a share of the half-width.</summary>
        static readonly float[] Rings = { 0.71f, 0.46f, 0.25f };

        const float Thickness = 0.4f;        // in x
        const float Overshoot = 0.6f;        // past the top and bottom of the arena, in y
        const float DepthMargin = 0.3f;      // past the front and back of the level, in z

        /// <summary>
        /// How far under the walkway the sudden-death ceiling stops, in metres.
        ///
        /// Enough that there is no standing room up there, and not so much that it eats the
        /// headroom of the floor below: a fighter is 0.91 m and jumps 1.61 m, so from the
        /// ground floor they should just be able to touch it and never reach what it covers.
        /// </summary>
        const float CeilingClearance = 1.2f;

        [MenuItem("Tools/Gulag Runners/Build shutters")]
        public static void Build()
        {
            SimWorldBaker baker = Object.FindFirstObjectByType<SimWorldBaker>();
            if (baker == null)
            {
                Debug.LogWarning("No SimWorldBaker in this scene, so there is no arena to " +
                                 "measure. Open the arena scene and try again.");
                return;
            }

            // Measured off the bake, so the plan follows the arena rather than a memory of it.
            SimWorld world = baker.World ?? baker.Bake();
            if (world == null || world.Solids.Length == 0)
            {
                Debug.LogWarning($"{baker.name}: the bake found no solids, so there is nothing " +
                                 "to close in. Fix the bake first — its report says why.", baker);
                return;
            }

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb b = world.Solids[i];
                minX = Mathf.Min(minX, Metres(b.MinX));
                maxX = Mathf.Max(maxX, Metres(b.MaxX));
                minY = Mathf.Min(minY, Metres(b.MinY));
                maxY = Mathf.Max(maxY, Metres(b.MaxY));

                Span z = world.SolidSpan(i);
                minZ = Mathf.Min(minZ, Metres(z.Min));
                maxZ = Mathf.Max(maxZ, Metres(z.Max));
            }

            float centre = (minX + maxX) * 0.5f;
            float half = (maxX - minX) * 0.5f;
            float low = minY - Overshoot;
            float high = maxY + Overshoot;
            float depth = (maxZ + DepthMargin) - (minZ - DepthMargin);
            float depthCentre = (minZ + maxZ) * 0.5f;

            Transform root = Root(baker.gameObject.scene);
            List<string> report = new List<string>
            {
                $"arena x {minX:0.00}..{maxX:0.00} (centre {centre:0.00}), " +
                $"y {minY:0.00}..{maxY:0.00}, z {minZ:0.00}..{maxZ:0.00}"
            };

            int order = 0;
            for (int ring = 0; ring < Rings.Length; ring++)
            {
                float offset = half * Rings[ring];

                // Left first, then right, so the shutters alternate sides as they come in.
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = centre + offset * side;

                    // Caught outside it, you are brought towards the middle.
                    GateToward toward = side < 0 ? GateToward.PlusX : GateToward.MinusX;

                    Shutter(root, $"Shutter {order}", GateWhen.Closing, order, toward,
                            new Vector3(x, (low + high) * 0.5f, depthCentre),
                            new Vector3(Thickness, high - low, depth));

                    report.Add($"  closing {order}: x {x:0.00}, " +
                               $"y {low:0.00}..{high:0.00}, herds " +
                               (side < 0 ? "right" : "left"));
                    order++;
                }
            }

            // Sudden death: the ceiling of what is left. docs/01 wants both fighters on ONE
            // platform, so this fills the room from just under its top walkway upwards and
            // shoves anybody standing up there down out of it.
            float inner = half * Rings[Rings.Length - 1];
            float roomLeft = centre - inner, roomRight = centre + inner;

            // Measured off the walkway it is meant to take away, not off the bottom of the
            // arena. Taken from the bottom it came out at 0.66 m in this arena — below the
            // ground floor it was supposed to leave standing, and half a body's clearance
            // above the one beneath that, which is a ceiling nobody fits under.
            float ceilingBottom = Mathf.Max(TopFloorIn(world, roomLeft, roomRight,
                                                       maxY - 0.5f) - CeilingClearance,
                                            minY + 2.6f);

            Shutter(root, "Ceiling", GateWhen.SuddenDeath, 0, GateToward.MinusY,
                    new Vector3((roomLeft + roomRight) * 0.5f,
                                (ceilingBottom + high) * 0.5f, depthCentre),
                    new Vector3(roomRight - roomLeft, high - ceilingBottom, depth));

            report.Add($"  sudden death: ceiling over x {roomLeft:0.00}..{roomRight:0.00} " +
                       $"from y {ceilingBottom:0.00} up, herds down");
            report.Add($"  final room is {roomRight - roomLeft:0.0} m wide");

            baker.Bake();
            EditorUtility.SetDirty(baker);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(baker.gameObject.scene);

            Debug.Log($"{order + 1} shutter(s) built and the arena re-baked.\n" +
                      string.Join("\n", report) + "\n" +
                      baker.LastReport + "\n" +
                      "Each one is a plain box under \"" + RootName + "\": give it the art it " +
                      "deserves, and check no ladder or chest sits inside one — the bake " +
                      "treats that as an authoring mistake and says so.", baker);
        }

        /// <summary>
        /// The highest floor top inside a stretch of the arena, below a given height.
        ///
        /// "Floor" means something wide enough to stand on, so trim and handrails do not
        /// count: a 20 cm box counted as a storey would put the ceiling somewhere arbitrary.
        /// </summary>
        static float TopFloorIn(SimWorld world, float left, float right, float below)
        {
            float top = float.MinValue;
            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb b = world.Solids[i];
                if (Metres(b.MaxX) - Metres(b.MinX) < 0.5f) continue;
                if (Metres(b.MinX) >= right || Metres(b.MaxX) <= left) continue;

                float y = Metres(b.MaxY);
                if (y > below || y <= top) continue;
                top = y;
            }
            return top > float.MinValue ? top : below;
        }

        static Transform Root(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == RootName) return go.transform;

            GameObject made = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(made, "Build shutters");
            return made.transform;
        }

        /// <summary>One shutter: the gate, the box that bakes, and the view that drops it.</summary>
        static void Shutter(Transform root, string name, GateWhen when, int order,
                            GateToward toward, Vector3 centre, Vector3 size)
        {
            Transform found = root.Find(name);
            GameObject go;

            if (found != null) go = found.gameObject;
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(go, "Build shutters");
                go.name = name;
                go.transform.SetParent(root, false);

                // The simulation does its own collision and PhysX is never used (docs/06).
                Collider unity = go.GetComponent<Collider>();
                if (unity != null) Object.DestroyImmediate(unity);
            }

            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;

            SimCollider marker = go.GetComponent<SimCollider>();
            if (marker == null) marker = Undo.AddComponent<SimCollider>(go);
            Undo.RecordObject(marker, "Build shutters");
            marker.kind = SimColliderKind.Solid;

            ArenaGate gate = go.GetComponent<ArenaGate>();
            if (gate == null) gate = Undo.AddComponent<ArenaGate>(go);
            Undo.RecordObject(gate, "Build shutters");
            gate.when = when;
            gate.order = order;
            gate.toward = toward;

            GateView view = go.GetComponent<GateView>();
            if (view == null) view = Undo.AddComponent<GateView>(go);
            Undo.RecordObject(view, "Build shutters");
            view.gate = gate;

            // It waits above where it lands, by its own height, so that it is out of sight
            // until it drops rather than hanging in the room with a hole in it.
            view.from = new Vector3(0f, size.y + 1f, 0f);
        }

        static float Metres(Fix v) => v.ToMilli() / 1000f;
    }
}

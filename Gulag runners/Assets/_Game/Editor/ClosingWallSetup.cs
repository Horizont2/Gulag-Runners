using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the closing walls in the arena, and clears away the shutters that came before.
    ///
    /// The first attempt built the closing phase out of sixteen gated plates switching on
    /// from the outside in. It narrowed the arena on paper and read wrong on screen: each
    /// plate is an event, so what a fighter saw was a corridor assembling itself around them
    /// rather than the space running out. Smaller plates would not have fixed it — a step is
    /// a step however small.
    ///
    /// So: ONE wall per side, and it moves. Its position is a pure function of the frame, the
    /// art reads the same function as the collision, and nothing about the baked arena has to
    /// become mutable to allow it.
    ///
    /// What makes it read, beyond the arithmetic:
    ///
    ///   They GROW from the arena's edge rather than sliding across it. A slab that slides
    ///   leaves a gap opening up behind it, and the eye reads that gap as the level ending.
    ///   Anchored at the edge there is nothing behind it to see.
    ///
    ///   They are placed by their INNER FACE. That face is the edge of the last room, which
    ///   is the number that decides how the match finishes — so it is the one worth being
    ///   able to see and drag.
    ///
    ///   They travel LINEARLY. A wall that eases in is a wall whose remaining room a player
    ///   cannot judge, and judging it is the whole skill the closing phase asks for.
    ///
    /// Re-running it moves the walls it made before rather than adding more.
    /// </summary>
    public static class ClosingWallSetup
    {
        const string RootName = "Closing Walls";
        const string OldShutters = "Shutters";

        /// <summary>What is left in the middle, as a share of the arena's half-width.</summary>
        const float InnerShare = 0.25f;

        const float Overshoot = 0.6f;        // past the top and bottom of the arena
        const float DepthMargin = 0.3f;      // past the front and back of the level

        [MenuItem("Tools/Gulag Runners/Setup/Build closing walls")]
        public static void BuildSides() => Build(false);

        [MenuItem("Tools/Gulag Runners/Setup/Build closing ceiling")]
        public static void BuildCeiling() => Build(true);

        static void Build(bool ceiling)
        {
            SimWorldBaker baker = Object.FindFirstObjectByType<SimWorldBaker>();
            if (baker == null)
            {
                Debug.LogWarning("No SimWorldBaker in this scene, so there is no arena to " +
                                 "measure.");
                return;
            }

            SimWorld world = baker.World ?? baker.Bake();
            if (world == null || world.Solids.Length == 0)
            {
                Debug.LogWarning($"{baker.name}: the bake found no solids. Its report says why.",
                                 baker);
                return;
            }

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb b = world.Solids[i];
                minX = Mathf.Min(minX, M(b.MinX));
                maxX = Mathf.Max(maxX, M(b.MaxX));
                minY = Mathf.Min(minY, M(b.MinY));
                maxY = Mathf.Max(maxY, M(b.MaxY));

                Span z = world.SolidSpan(i);
                minZ = Mathf.Min(minZ, M(z.Min));
                maxZ = Mathf.Max(maxZ, M(z.Max));
            }

            float centre = (minX + maxX) * 0.5f;
            float half = (maxX - minX) * 0.5f;
            float low = minY - Overshoot, high = maxY + Overshoot;
            float depth = (maxZ - minZ) + DepthMargin * 2f;
            float depthCentre = (minZ + maxZ) * 0.5f;

            // Anything the old attempt left behind.
            foreach (GameObject go in baker.gameObject.scene.GetRootGameObjects())
                if (go.name == OldShutters) Undo.DestroyObjectImmediate(go);

            Transform root = Root(baker.gameObject.scene);
            float inner = half * InnerShare;

            if (ceiling)
            {
                // Measured off the walkway it takes away, not the bottom of the arena: taken
                // from the bottom it lands below the floor it is meant to leave standing.
                float stop = TopFloorIn(world, centre - inner, centre + inner, maxY - 0.5f) - 1.2f;
                float top = high;

                Wall(root, "Ceiling", GateToward.MinusY,
                     new Vector3(centre, (stop + top) * 0.5f, depthCentre),
                     new Vector3(inner * 2f, top - stop, depth));

                Debug.Log($"Ceiling over x {centre - inner:0.00}..{centre + inner:0.00}, " +
                          $"growing down from y {top:0.00} to rest at {stop:0.00}.\n" +
                          "It leaves the ground floor its headroom and takes the walkway out " +
                          "of play. Delete the side walls if you want only this.", baker);
            }
            else
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    // The finished wall: everything from the arena's edge to the face of the
                    // last room. At the start of the match it has no thickness at all, and
                    // it eats inwards from the edge — so there is never a gap behind it.
                    float edge = centre + half * side;
                    float face = centre + inner * side;

                    Wall(root, side < 0 ? "Wall Left" : "Wall Right",
                         side < 0 ? GateToward.PlusX : GateToward.MinusX,
                         new Vector3((edge + face) * 0.5f, (low + high) * 0.5f, depthCentre),
                         new Vector3(Mathf.Abs(face - edge), high - low, depth));
                }

                Debug.Log(
                    $"Two walls. Arena x {minX:0.00}..{maxX:0.00} (centre {centre:0.00}); " +
                    $"each grows {half - inner:0.1f} m inwards from its own edge and they " +
                    $"finish {inner * 2f:0.0} m apart, which is about one screen.\n" +
                    "They grow rather than slide, so nothing opens up behind them. Collision " +
                    "and art come from the same number every tick. A scrape of dust along " +
                    "the advancing face is the cheapest thing that will make them feel heavy.",
                    baker);
            }

            baker.Bake();
            EditorUtility.SetDirty(baker);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(baker.gameObject.scene);
        }

        static void Wall(Transform root, string name, GateToward grows,
                         Vector3 centre, Vector3 size)
        {
            Transform found = root.Find(name);
            GameObject go;

            if (found != null) go = found.gameObject;
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(go, "Build closing walls");
                go.name = name;
                go.transform.SetParent(root, false);
            }

            // The simulation does its own collision and never uses PhysX (docs/06). The box
            // is read off the renderer instead, so there is nothing for Unity to simulate.
            Collider unity = go.GetComponent<Collider>();
            if (unity != null) Object.DestroyImmediate(unity);

            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;

            ClosingWall wall = go.GetComponent<ClosingWall>();
            if (wall == null) wall = Undo.AddComponent<ClosingWall>(go);

            Undo.RecordObject(wall, "Build closing walls");
            wall.grows = grows;
        }

        static Transform Root(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == RootName) return go.transform;

            GameObject made = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(made, "Build closing walls");
            return made.transform;
        }

        /// <summary>The highest floor top in a stretch of the arena, below a given height.</summary>
        static float TopFloorIn(SimWorld world, float left, float right, float below)
        {
            float top = float.MinValue;
            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb b = world.Solids[i];
                if (M(b.MaxX) - M(b.MinX) < 0.5f) continue;
                if (M(b.MinX) >= right || M(b.MaxX) <= left) continue;

                float y = M(b.MaxY);
                if (y > below || y <= top) continue;
                top = y;
            }
            return top > float.MinValue ? top : below;
        }

        static float M(Fix v) => v.ToMilli() / 1000f;
    }
}

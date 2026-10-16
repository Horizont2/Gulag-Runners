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
    ///   The walls are THICK. A thin slab sliding in leaves the dead ground behind it in
    ///   shot, and the eye reads that as the level ending rather than as a wall. Four metres
    ///   of it fills the frame behind, so what you see is wall.
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

        /// <summary>How thick each wall is, in metres. Enough to fill the frame behind it.</summary>
        const float Thickness = 4f;

        const float Overshoot = 0.6f;        // past the top and bottom of the arena
        const float DepthMargin = 0.3f;      // past the front and back of the level

        [MenuItem("Tools/Gulag Runners/Build closing walls")]
        public static void BuildSides() => Build(false);

        [MenuItem("Tools/Gulag Runners/Build closing ceiling")]
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

                Wall(root, "Ceiling", true,
                     new Vector3(centre, stop + Thickness * 0.5f, depthCentre),
                     new Vector3(inner * 2f, Thickness, depth),
                     // From above the arena, down to rest on that height.
                     (high + Thickness) - (stop + Thickness * 0.5f));

                Debug.Log($"Ceiling over x {centre - inner:0.00}..{centre + inner:0.00}, " +
                          $"coming to rest at y {stop:0.00}.\n" +
                          "It leaves the ground floor its headroom and takes the walkway out " +
                          "of play. Delete the side walls if you want only this.", baker);
            }
            else
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    // Placed by the inner face: the edge of the last room.
                    float face = centre + inner * side;
                    float boxCentre = face + Thickness * 0.5f * side;

                    Wall(root, side < 0 ? "Wall Left" : "Wall Right", false,
                         new Vector3(boxCentre, (low + high) * 0.5f, depthCentre),
                         new Vector3(Thickness, high - low, depth),
                         (half - inner) * -side);
                }

                Debug.Log(
                    $"Two walls. Arena x {minX:0.00}..{maxX:0.00} (centre {centre:0.00}); " +
                    $"they start at the edges and finish {inner * 2f:0.0} m apart, which is " +
                    "about one screen.\n" +
                    "They move every tick across the shutter window, collision and art from " +
                    "the same number. Give them the art they deserve; the box is only the " +
                    "shape. A scrape of dust where they meet the floor is the cheapest thing " +
                    "that will make them feel heavy.", baker);
            }

            baker.Bake();
            EditorUtility.SetDirty(baker);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(baker.gameObject.scene);
        }

        static void Wall(Transform root, string name, bool vertical,
                         Vector3 centre, Vector3 size, float travel)
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
            wall.vertical = vertical;
            wall.travel = travel;
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

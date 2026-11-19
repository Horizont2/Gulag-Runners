using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Inspector for <see cref="ArenaBuilder"/>. Builds the greybox arena as real scene
    /// objects, with full undo support, so everything stays selectable and editable by hand.
    /// Nothing here runs in a build.
    /// </summary>
    [CustomEditor(typeof(ArenaBuilder))]
    public sealed class ArenaBuilderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ArenaBuilder builder = (ArenaBuilder)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"Arena: {builder.TotalWidth:0.#} x {builder.TotalHeight:0.#} m, " +
                $"{builder.floors} floors of {builder.roomsPerFloor} rooms.\n" +
                "Build creates ordinary scene objects you can then edit by hand. " +
                "Nothing is generated while the game runs.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build arena in scene", GUILayout.Height(28)))
                    Build(builder);

                if (GUILayout.Button("Clear", GUILayout.Height(28), GUILayout.Width(80)))
                    Clear(builder);
            }
        }

        static void Clear(ArenaBuilder b)
        {
            Transform existing = b.FindGeneratedRoot();
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        }

        static void Build(ArenaBuilder b)
        {
            Undo.SetCurrentGroupName("Build arena");
            int group = Undo.GetCurrentGroup();

            Clear(b);

            GameObject rootGo = new GameObject(ArenaBuilder.GeneratedRootName);
            Undo.RegisterCreatedObjectUndo(rootGo, "Build arena");
            rootGo.transform.SetParent(b.transform, false);
            Transform root = rootGo.transform;

            float left = b.LeftEdge, right = b.RightEdge, z = b.planeZ;
            float lintel = Mathf.Max(0.1f, b.floorHeight - b.doorHeight);

            for (int f = 0; f < b.floors; f++)
            {
                float y = f * b.floorHeight;
                BuildSlab(b, root, f, y);

                // Interior walls are lintels above a doorway. A full-height wall would seal a
                // room off, and docs/05 wants at least two ways out of every room.
                for (int r = 1; r < b.roomsPerFloor; r++)
                {
                    float x = left + r * b.roomWidth;
                    Box(b, root, $"Wall{f}_{r}",
                        new Vector3(x, y + b.doorHeight + lintel * 0.5f, z),
                        new Vector3(b.wallThickness, lintel, 1f), SimColliderKind.Solid);
                }
            }

            float height = b.TotalHeight;
            Box(b, root, "WallLeft", new Vector3(left - b.wallThickness * 0.5f, height * 0.5f, z),
                new Vector3(b.wallThickness, height, 1f), SimColliderKind.Solid);
            Box(b, root, "WallRight", new Vector3(right + b.wallThickness * 0.5f, height * 0.5f, z),
                new Vector3(b.wallThickness, height, 1f), SimColliderKind.Solid);

            if (b.buildLadders)
            {
                for (int f = 0; f < b.floors - 1; f++)
                {
                    float x = b.LadderXForFloor(f);
                    Box(b, root, $"Ladder{f}",
                        new Vector3(x, f * b.floorHeight + b.floorHeight * 0.5f, z),
                        new Vector3(b.ladderWidth, b.floorHeight + b.slabThickness, 1f),
                        SimColliderKind.Ladder);
                }
            }

            if (b.buildTestFeatures) BuildTestFeatures(b, root, z);

            if (b.buildCages)
            {
                Box(b, root, "CageLeft", new Vector3(left + 1.2f, 1.1f, z),
                    new Vector3(0.1f, 2.2f, 1f), SimColliderKind.Solid);
                Box(b, root, "CageRight", new Vector3(right - 1.2f, 1.1f, z),
                    new Vector3(0.1f, 2.2f, 1f), SimColliderKind.Solid);
            }

            if (b.cameraToFit != null)
            {
                Undo.RecordObject(b.cameraToFit, "Fit camera borders");
                b.cameraToFit.planeZ = z;
                b.cameraToFit.SetBorders(new Vector2(left, 0f), new Vector2(right, height + 1f));
                EditorUtility.SetDirty(b.cameraToFit);
            }

            if (b.playerToPlace != null)
            {
                Undo.RecordObject(b.playerToPlace, "Place player");
                PlayerController pc = b.playerToPlace.GetComponent<PlayerController>();
                float lift = pc != null ? pc.visualYOffset : 0.9f;
                b.playerToPlace.position = new Vector3(left + 2.5f, lift, z);
                EditorUtility.SetDirty(b.playerToPlace);
            }

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = rootGo;
        }

        /// <summary>
        /// A floor slab, built as segments around its holes: the hatch the ladder below comes
        /// up through, and on the top floor the gap that has to be jumped.
        /// </summary>
        static void BuildSlab(ArenaBuilder b, Transform root, int floor, float y)
        {
            float left = b.LeftEdge, right = b.RightEdge, z = b.planeZ;
            List<(float min, float max)> holes = new List<(float, float)>();

            if (floor > 0 && b.buildLadders)
            {
                float hx = b.LadderXForFloor(floor - 1);
                holes.Add((hx - b.hatchWidth * 0.5f, hx + b.hatchWidth * 0.5f));
            }

            // The gap goes on the top floor, the only one with open sky above it: indoors the
            // ceiling clips a jump to about a metre, so a gap down there would be a wall.
            if (floor == b.floors - 1 && b.buildTestFeatures)
            {
                float gap = GapWidth(b);
                if (gap > 0.05f) holes.Add((-gap * 0.5f, gap * 0.5f));
            }

            holes.Sort((p, q) => p.min.CompareTo(q.min));

            float cursor = left;
            int part = 0;
            foreach ((float min, float max) hole in holes)
            {
                float w = hole.min - cursor;
                if (w > 0.05f)
                    Box(b, root, $"Floor{floor}_{part++}",
                        new Vector3(cursor + w * 0.5f, y - b.slabThickness * 0.5f, z),
                        new Vector3(w, b.slabThickness, 1f), SimColliderKind.Solid);
                cursor = Mathf.Max(cursor, hole.max);
            }

            float last = right - cursor;
            if (last > 0.05f)
                Box(b, root, $"Floor{floor}_{part}",
                    new Vector3(cursor + last * 0.5f, y - b.slabThickness * 0.5f, z),
                    new Vector3(last, b.slabThickness, 1f), SimColliderKind.Solid);
        }

        /// <summary>
        /// The four obstacles the movement has to be tested against. Each one exists to prove a
        /// specific rule from docs/02 rather than to look like anything.
        /// </summary>
        /// <summary>
        /// Body sizes to scale the obstacles against. Read from the player when there is one,
        /// because an obstacle sized in metres stops working the moment the character changes
        /// height, and it does so silently.
        /// </summary>
        static void PlayerSize(ArenaBuilder b, out float body, out float crouch)
        {
            body = 1.8f;
            crouch = 1.1f;

            PlayerController pc = b.playerToPlace != null
                ? b.playerToPlace.GetComponent<PlayerController>()
                : null;
            if (pc == null) return;

            body = Mathf.Max(0.2f, pc.tuning.bodyHeight);
            crouch = Mathf.Min(pc.tuning.crouchHeight, body * 0.75f);
        }

        static float GapWidth(ArenaBuilder b)
        {
            if (!b.scaleFeaturesToPlayer) return b.floorGapWidth;
            PlayerSize(b, out float body, out _);
            return body * 0.8f;        // comfortably inside a running jump at any body size
        }

        static void BuildTestFeatures(ArenaBuilder b, Transform root, float z)
        {
            float left = b.LeftEdge;
            float clear = b.floorHeight - b.slabThickness;
            PlayerSize(b, out float body, out float crouch);

            float stepHeight = b.scaleFeaturesToPlayer ? body * 0.17f : 0.3f;
            float beamBottom = b.scaleFeaturesToPlayer ? (body + crouch) * 0.5f : 1.3f;
            beamBottom = Mathf.Min(beamBottom, clear - 0.2f);

            // Ledge assist: a step low enough to be walked up without jumping.
            Box(b, root, "Test_StepLedge", new Vector3(left + 9.6f, stepHeight * 0.5f, z),
                new Vector3(1.6f, stepHeight, 1f), SimColliderKind.Solid);

            // Crouch: a beam hanging between crouch height and standing height, so it blocks one
            // and passes the other whatever size the character is.
            Box(b, root, "Test_LowBeam",
                new Vector3(left + 12.6f, beamBottom + (clear - beamBottom) * 0.5f, z),
                new Vector3(1.6f, clear - beamBottom, 1f), SimColliderKind.Solid);

            // One-way platforms, spaced inside one jump of each other.
            Box(b, root, "Test_OneWayLow", new Vector3(left + 16.2f, 0.8f, z),
                new Vector3(3.0f, 0.2f, 1f), SimColliderKind.OneWay);
            Box(b, root, "Test_OneWayHigh", new Vector3(left + 18.0f, 1.6f, z),
                new Vector3(2.4f, 0.2f, 1f), SimColliderKind.OneWay);
        }

        static void Box(ArenaBuilder b, Transform parent, string name, Vector3 centre,
                        Vector3 size, SimColliderKind kind)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(go, "Build arena");
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.localScale = size;

            // The simulation does its own collision; a Unity collider would only fight it.
            Collider unityCollider = go.GetComponent<Collider>();
            if (unityCollider != null) Undo.DestroyObjectImmediate(unityCollider);

            SimCollider sim = Undo.AddComponent<SimCollider>(go);
            sim.kind = kind;

            Material m = kind switch
            {
                SimColliderKind.Solid => b.solidMaterial,
                SimColliderKind.OneWay => b.oneWayMaterial,
                _ => b.ladderMaterial
            };
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}

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
            float ladderX = b.LadderX;

            for (int f = 0; f < b.floors; f++)
            {
                float y = f * b.floorHeight;
                bool hatch = b.buildLadders && f > 0;

                if (hatch)
                {
                    float holeMin = ladderX - b.hatchWidth * 0.5f;
                    float holeMax = ladderX + b.hatchWidth * 0.5f;

                    float lw = holeMin - left;
                    if (lw > 0.05f)
                        Box(b, root, $"Floor{f}_L", new Vector3(left + lw * 0.5f, y - b.slabThickness * 0.5f, z),
                            new Vector3(lw, b.slabThickness, 1f), SimColliderKind.Solid);

                    float rw = right - holeMax;
                    if (rw > 0.05f)
                        Box(b, root, $"Floor{f}_R", new Vector3(holeMax + rw * 0.5f, y - b.slabThickness * 0.5f, z),
                            new Vector3(rw, b.slabThickness, 1f), SimColliderKind.Solid);
                }
                else
                {
                    Box(b, root, $"Floor{f}", new Vector3(0f, y - b.slabThickness * 0.5f, z),
                        new Vector3(b.TotalWidth, b.slabThickness, 1f), SimColliderKind.Solid);
                }

                // Interior walls: these are what make the fog of war of docs/01 real.
                for (int r = 1; r < b.roomsPerFloor; r++)
                {
                    float x = left + r * b.roomWidth;
                    if (b.buildLadders && Mathf.Abs(x - ladderX) < b.roomWidth * 0.5f) continue;
                    Box(b, root, $"Wall{f}_{r}", new Vector3(x, y + b.floorHeight * 0.5f, z),
                        new Vector3(b.wallThickness, b.floorHeight, 1f), SimColliderKind.Solid);
                }
            }

            Box(b, root, "WallLeft", new Vector3(left - b.wallThickness * 0.5f, b.TotalHeight * 0.5f, z),
                new Vector3(b.wallThickness, b.TotalHeight, 1f), SimColliderKind.Solid);
            Box(b, root, "WallRight", new Vector3(right + b.wallThickness * 0.5f, b.TotalHeight * 0.5f, z),
                new Vector3(b.wallThickness, b.TotalHeight, 1f), SimColliderKind.Solid);

            if (b.buildLadders)
                for (int f = 0; f < b.floors - 1; f++)
                    Box(b, root, $"Ladder{f}",
                        new Vector3(ladderX, f * b.floorHeight + b.floorHeight * 0.5f, z),
                        new Vector3(b.ladderWidth, b.floorHeight + b.slabThickness, 1f),
                        SimColliderKind.Ladder);

            if (b.buildOneWayPlatform)
                Box(b, root, "OneWayPlatform",
                    new Vector3(left + b.roomWidth * 2.2f, b.floorHeight * 0.6f, z),
                    new Vector3(b.roomWidth * 0.5f, 0.2f, 1f), SimColliderKind.OneWay);

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
                b.cameraToFit.SetBorders(new Vector2(left, 0f),
                                         new Vector2(right, b.TotalHeight + 1f));
                EditorUtility.SetDirty(b.cameraToFit);
            }

            if (b.playerToPlace != null)
            {
                Undo.RecordObject(b.playerToPlace, "Place player");
                PlayerController pc = b.playerToPlace.GetComponent<PlayerController>();
                float lift = pc != null ? pc.visualYOffset : 0.5f;
                b.playerToPlace.position = new Vector3(left + 2.5f, lift, z);
                EditorUtility.SetDirty(b.playerToPlace);
            }

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = rootGo;
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

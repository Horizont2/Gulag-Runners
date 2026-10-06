using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Authoring tool for the M0 greybox arena (docs/07).
    ///
    /// It never runs at play time. Pressing Build in the inspector creates ordinary scene
    /// objects — cubes with a <see cref="SimCollider"/> on each — that you can then select,
    /// move and tune by hand, and that get saved with the scene. Nothing in this project is
    /// spawned from code while the game is running.
    ///
    /// Proportions follow docs/05: floors 2.5 m high, rooms about 7.2 m wide. That is what
    /// makes the movement tuning meaningful — a 1.53 m jump apex visibly cannot clear a 2.5 m
    /// floor, so ladders are the only way up, which is exactly what the arena topology wants.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ArenaBuilder : MonoBehaviour
    {
        [Header("Shape")]
        [Min(1)] public int floors = 3;
        [Min(1)] public int roomsPerFloor = 3;
        public float roomWidth = 7.2f;
        [Tooltip("Floor to floor. Clear headroom is this minus the slab, and a 1.8 m fighter " +
                 "needs headroom well above their own height or jumping indoors does nothing. " +
                 "3.0 m leaves 2.7 m clear, which is 0.9 m of usable jump.")]
        public float floorHeight = 3.0f;
        public float slabThickness = 0.3f;
        public float wallThickness = 0.4f;

        [Tooltip("Height of the doorway left in every interior wall. The wall is built as a " +
                 "lintel above it, so a room is never sealed — docs/05 requires at least two " +
                 "ways out of every room, because a dead end is lethal in a game where you " +
                 "cannot see the other player coming.")]
        public float doorHeight = 2.2f;
        [Tooltip("The single gameplay plane. The arena is 3D; the fight is not (docs/02).")]
        public float planeZ;

        [Header("Connections")]
        public bool buildLadders = true;
        public float ladderWidth = 0.9f;
        public float hatchWidth = 1.6f;
        public bool buildOneWayPlatform = true;
        public bool buildCages = true;

        [Header("Materials (optional)")]
        public Material solidMaterial;
        public Material oneWayMaterial;
        public Material ladderMaterial;

        [Header("After building")]
        [Tooltip("Border these to the arena once it is built.")]
        public SideViewCamera cameraToFit;
        [Tooltip("Moved to the left spawn cage once the arena is built.")]
        public Transform playerToPlace;

        public const string GeneratedRootName = "GeneratedArena";

        public float TotalWidth => roomsPerFloor * roomWidth;
        public float TotalHeight => floors * floorHeight;
        public float LeftEdge => -TotalWidth * 0.5f;
        public float RightEdge => TotalWidth * 0.5f;
        public float LadderX => LeftEdge + roomWidth;

        public Transform FindGeneratedRoot()
        {
            Transform t = transform.Find(GeneratedRootName);
            return t;
        }

        void OnDrawGizmos()
        {
            // Draw the arena envelope even before anything is built, so the shape can be
            // judged from the inspector numbers alone.
            Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.6f);
            Gizmos.DrawWireCube(
                new Vector3(0f, TotalHeight * 0.5f, planeZ),
                new Vector3(TotalWidth, TotalHeight, 0.05f));

            Gizmos.color = new Color(1f, 1f, 1f, 0.18f);
            for (int f = 1; f < floors; f++)
            {
                float y = f * floorHeight;
                Gizmos.DrawLine(new Vector3(LeftEdge, y, planeZ), new Vector3(RightEdge, y, planeZ));
            }
        }
    }
}

using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Where a round begins, drawn in the scene view.
    ///
    /// An empty transform is invisible, and a spawn you cannot see is a spawn that drifts: the
    /// last three faults in this arena were all geometry that looked right and measured wrong.
    /// So the marker draws the fighter it stands for — the simulated body box, at the size the
    /// simulation will use, on the depth lane it will use — and it draws it whether or not
    /// anything is selected.
    ///
    /// It holds no state the simulation reads. <see cref="PlayerController.spawnPoint"/> points
    /// at the transform; this only makes the transform legible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnPointMarker : MonoBehaviour
    {
        [Tooltip("The fighter that starts here. Only used to read their body size and facing " +
                 "for the drawing — the fighter is what points at this marker, not the other " +
                 "way about.")]
        public PlayerController player;

        [Tooltip("Body size to draw when there is no fighter to ask, in metres.")]
        public Vector2 fallbackBody = new Vector2(0.3f, 0.91f);

        [Tooltip("Draw the lane the body stands on as well as the body: a flat rectangle at " +
                 "the body's own depth, which is the thing the bake actually tests against.")]
        public bool drawLane = true;

        Vector2 Body => player != null
            ? new Vector2(player.tuning.bodyWidth, player.tuning.bodyHeight)
            : fallbackBody;

        void OnDrawGizmos()
        {
            Vector3 feet = transform.position;
            Vector2 body = Body;

            // The body, from the feet up: this box is what collides, and nothing about the
            // model is.
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireCube(feet + new Vector3(0f, body.y * 0.5f, 0f),
                                new Vector3(body.x, body.y, 0.05f));

            // Which way they look on the first frame.
            int facing = player != null ? (player.spawnFacing >= 0 ? 1 : -1) : 1;
            Vector3 eye = feet + new Vector3(0f, body.y * 0.8f, 0f);
            Gizmos.DrawLine(eye, eye + new Vector3(facing * body.x, 0f, 0f));

            if (!drawLane) return;

            // The depth the body occupies. A spawn on the wrong lane is the one fault that
            // looks perfect from the side and drops the fighter through the floor.
            float depth = player != null ? player.tuning.bodyDepth : 0.6f;
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.35f);
            Gizmos.DrawWireCube(feet + new Vector3(0f, 0.01f, 0f),
                                new Vector3(body.x, 0.02f, depth));
        }
    }
}

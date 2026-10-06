using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// The side-view camera of docs/02.
    ///
    /// Rules it enforces, because they are balance, not taste:
    ///   - the camera looks straight at the gameplay plane, with only a slight downward tilt.
    ///     That tilt is the "top/side" of the Miro note: enough to read volume, not enough to
    ///     distort the fighters at the edges of the frame;
    ///   - the character is kept at 17-20% of screen height. Smaller than that and nothing is
    ///     readable on a 6" phone, so framing is expressed as "how many metres tall is the
    ///     view" rather than as a distance;
    ///   - zoom never leaves 60-110%, so two fighters can never shrink to specks;
    ///   - the view never leaves the arena: see the Borders section.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]   // after the player has moved this frame
    public sealed class SideViewCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The player this camera belongs to. Each player has their own camera: on a " +
                 "phone that is the only camera on the device, and in the editor the two share " +
                 "the screen through Viewport Rect below.")]
        public PlayerController player;

        [Tooltip("Filled in from Player automatically. Assign directly only for a camera that " +
                 "follows something other than a player.")]
        public Transform target;
        [Tooltip("Optional. When set, the camera frames both fighters and zooms to fit.")]
        public Transform secondTarget;
        [Tooltip("Find a PlayerController at startup if no target is assigned.")]
        public bool autoFindPlayer = true;

        [Header("Framing")]
        [Tooltip("Height of the visible world, in metres. A 1.8 m fighter at 10 m fills 18% " +
                 "of the screen, which is the readable minimum from docs/02.")]
        [Range(4f, 30f)] public float visibleWorldHeight = 10f;

        [Tooltip("Narrow FOV keeps fighters from deforming at the edges.")]
        [Range(14f, 50f)] public float fieldOfView = 30f;

        [Tooltip("Downward tilt. docs/02 asks for 10-14 degrees.")]
        [Range(0f, 30f)] public float pitchDegrees = 12f;

        [Tooltip("The Z of the gameplay plane the camera focuses on.")]
        public float planeZ;

        [Tooltip("Shifts the framing up so there is more headroom than floor.")]
        public float verticalBias = 0.9f;

        [Header("Follow")]
        [Tooltip("Metres the camera leads in front of the player, by facing.")]
        public float lookAhead = 1.5f;
        public float lookAheadSmoothTime = 0.35f;
        public float horizontalSmoothTime = 0.14f;
        public float verticalSmoothTime = 0.22f;

        [Tooltip("Vertical slack in metres. Small hops do not move the camera.")]
        public float verticalDeadZone = 0.8f;

        [Tooltip("When the player changes floor, catch up this much faster.")]
        public float floorChangeDistance = 2.0f;
        [Range(1f, 10f)] public float floorChangeSpeedUp = 4f;

        [Header("Two-target zoom")]
        [Tooltip("Extra metres kept around both fighters when framing them together.")]
        public float framingPadding = 3f;
        [Range(0.3f, 1f)] public float minZoom = 0.6f;    // docs/02: never below 60%
        [Range(1f, 2f)] public float maxZoom = 1.1f;      // docs/02: never above 110%
        public float zoomSmoothTime = 0.3f;

        [Header("Borders")]
        [Tooltip("Clamp the view to the arena so the camera never shows the void behind it.")]
        public bool useBorders = true;

        [Tooltip("Bottom-left corner of the arena, in world units.")]
        public Vector2 bordersMin = new Vector2(-30f, 0f);

        [Tooltip("Top-right corner of the arena, in world units.")]
        public Vector2 bordersMax = new Vector2(30f, 12f);

        [Tooltip("When the arena is narrower than the view, centre on it instead of clamping.")]
        public bool centreWhenSmallerThanView = true;

        [Header("Split screen (editor testing only)")]
        [Tooltip("Apply the rect below to the camera on Awake. On a real device each player " +
                 "has the whole screen, so leave this off outside local testing.")]
        public bool applyViewportRect;

        [Tooltip("Normalised viewport. Top half is (0, 0.5, 1, 0.5), bottom half (0, 0, 1, 0.5).")]
        public Rect viewportRect = new Rect(0f, 0f, 1f, 1f);

        Camera _camera;
        float _lookAheadCurrent;
        float _lookAheadVelocity;
        float _zoom = 1f;
        float _zoomVelocity;
        Vector2 _focus;
        Vector2 _focusVelocity;

        public Camera Camera => _camera;
        /// <summary>The point the camera is currently looking at, on the gameplay plane.</summary>
        public Vector2 Focus => _focus;

        void Awake()
        {
            _camera = GetComponent<Camera>();

            if (player != null) target = player.transform;

            if (target == null && autoFindPlayer)
            {
                PlayerController found = FindFirstObjectByType<PlayerController>();
                if (found != null)
                {
                    target = found.transform;
                    player = found;
                    Debug.LogWarning($"{name}: no target assigned, grabbed the first player in " +
                                     "the scene. Assign Player in the inspector — with two " +
                                     "players both cameras would otherwise follow the same one.",
                                     this);
                }
                else
                {
                    Debug.LogError($"{name}: no player to follow.", this);
                }
            }

            if (applyViewportRect && _camera != null) _camera.rect = viewportRect;

            ApplyLens();
            SnapToTarget();
        }

        void OnEnable() => SnapToTarget();

        void OnValidate()
        {
            if (bordersMax.x < bordersMin.x) bordersMax.x = bordersMin.x;
            if (bordersMax.y < bordersMin.y) bordersMax.y = bordersMin.y;
            if (maxZoom < minZoom) maxZoom = minZoom;
            if (_camera == null) _camera = GetComponent<Camera>();
            ApplyLens();
        }

        void ApplyLens()
        {
            if (_camera == null) return;
            _camera.orthographic = false;
            _camera.fieldOfView = fieldOfView;
        }

        void LateUpdate()
        {
            if (target == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 desired = DesiredFocus(dt, out float desiredZoom);

            _zoom = Mathf.SmoothDamp(_zoom, desiredZoom, ref _zoomVelocity, zoomSmoothTime, Mathf.Infinity, dt);
            _zoom = Mathf.Clamp(_zoom, minZoom, maxZoom);

            // Vertical dead zone: ignore small hops, chase hard when the floor changes.
            float dy = desired.y - _focus.y;
            float vSmooth = verticalSmoothTime;
            if (Mathf.Abs(dy) > floorChangeDistance)
            {
                vSmooth = verticalSmoothTime / floorChangeSpeedUp;
            }
            else if (Mathf.Abs(dy) < verticalDeadZone)
            {
                // Inside the dead zone the camera holds still. Clearing the stored velocity
                // matters: without it SmoothDamp keeps drifting after every small hop.
                desired.y = _focus.y;
                _focusVelocity.y = 0f;
            }

            float x = Mathf.SmoothDamp(_focus.x, desired.x, ref _focusVelocity.x, horizontalSmoothTime, Mathf.Infinity, dt);
            float y = Mathf.SmoothDamp(_focus.y, desired.y, ref _focusVelocity.y, vSmooth, Mathf.Infinity, dt);
            _focus = new Vector2(x, y);

            _focus = ClampToBorders(_focus, _zoom);
            Place(_focus, _zoom);
        }

        Vector2 DesiredFocus(float dt, out float desiredZoom)
        {
            Vector2 a = target.position;
            desiredZoom = 1f;

            if (secondTarget != null)
            {
                Vector2 b = secondTarget.position;
                Vector2 mid = (a + b) * 0.5f;

                float neededHeight = Mathf.Abs(a.y - b.y) + framingPadding;
                float neededWidth = Mathf.Abs(a.x - b.x) + framingPadding;
                float aspect = _camera != null && _camera.aspect > 0f ? _camera.aspect : 1.777f;
                float needed = Mathf.Max(neededHeight, neededWidth / aspect);

                desiredZoom = Mathf.Clamp(needed / visibleWorldHeight, minZoom, maxZoom);
                return new Vector2(mid.x, mid.y + verticalBias);
            }

            PlayerController pc = player != null ? player : target.GetComponent<PlayerController>();
            float facing = pc != null ? pc.State.Facing : 1f;

            _lookAheadCurrent = Mathf.SmoothDamp(_lookAheadCurrent, facing * lookAhead,
                                                 ref _lookAheadVelocity, lookAheadSmoothTime,
                                                 Mathf.Infinity, dt);

            return new Vector2(a.x + _lookAheadCurrent, a.y + verticalBias);
        }

        /// <summary>Half the visible world size at the gameplay plane, for the current zoom.</summary>
        public Vector2 ViewHalfExtents(float zoom)
        {
            float height = visibleWorldHeight * zoom;
            float aspect = _camera != null && _camera.aspect > 0f ? _camera.aspect : 1.777f;
            return new Vector2(height * aspect * 0.5f, height * 0.5f);
        }

        Vector2 ClampToBorders(Vector2 focus, float zoom)
        {
            if (!useBorders) return focus;

            Vector2 half = ViewHalfExtents(zoom);

            float minX = bordersMin.x + half.x;
            float maxX = bordersMax.x - half.x;
            float minY = bordersMin.y + half.y;
            float maxY = bordersMax.y - half.y;

            focus.x = minX > maxX
                ? (centreWhenSmallerThanView ? (bordersMin.x + bordersMax.x) * 0.5f : focus.x)
                : Mathf.Clamp(focus.x, minX, maxX);

            focus.y = minY > maxY
                ? (centreWhenSmallerThanView ? (bordersMin.y + bordersMax.y) * 0.5f : focus.y)
                : Mathf.Clamp(focus.y, minY, maxY);

            return focus;
        }

        void Place(Vector2 focus, float zoom)
        {
            float halfFov = Mathf.Deg2Rad * fieldOfView * 0.5f;
            float pitch = Mathf.Deg2Rad * pitchDegrees;

            // Distance that makes the plane show exactly visibleWorldHeight * zoom metres,
            // corrected for the tilt.
            float distance = (visibleWorldHeight * zoom * 0.5f) * Mathf.Cos(pitch) / Mathf.Tan(halfFov);

            Quaternion rotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            Vector3 focusPoint = new Vector3(focus.x, focus.y, planeZ);

            transform.SetPositionAndRotation(focusPoint - rotation * Vector3.forward * distance, rotation);
            if (_camera != null) _camera.fieldOfView = fieldOfView;
        }

        /// <summary>Jumps straight to the target. Call on spawn and after a teleport.</summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            _lookAheadCurrent = 0f;
            _zoom = 1f;
            _focusVelocity = Vector2.zero;
            _focus = ClampToBorders(DesiredFocus(1f, out float z), z);
            _zoom = Mathf.Clamp(z, minZoom, maxZoom);
            Place(_focus, _zoom);
        }

        /// <summary>Sets the arena borders at runtime, e.g. from the generated arena (docs/05).</summary>
        public void SetBorders(Vector2 min, Vector2 max)
        {
            bordersMin = Vector2.Min(min, max);
            bordersMax = Vector2.Max(min, max);
        }

        /// <summary>Fits the borders around every SimCollider in the scene, plus a margin.</summary>
        [ContextMenu("Fit borders to arena")]
        public void FitBordersToArena()
        {
            SimCollider[] boxes = FindObjectsByType<SimCollider>(FindObjectsSortMode.None);
            if (boxes.Length == 0) return;

            Rect first = boxes[0].ToRect();
            float minX = first.xMin, minY = first.yMin, maxX = first.xMax, maxY = first.yMax;

            foreach (SimCollider c in boxes)
            {
                Rect r = c.ToRect();
                minX = Mathf.Min(minX, r.xMin);
                minY = Mathf.Min(minY, r.yMin);
                maxX = Mathf.Max(maxX, r.xMax);
                maxY = Mathf.Max(maxY, r.yMax);
            }

            SetBorders(new Vector2(minX, minY), new Vector2(maxX, maxY + 3f));
        }

        void OnDrawGizmosSelected()
        {
            // Borders.
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            Vector3 c = new Vector3((bordersMin.x + bordersMax.x) * 0.5f,
                                    (bordersMin.y + bordersMax.y) * 0.5f, planeZ);
            Gizmos.DrawWireCube(c, new Vector3(bordersMax.x - bordersMin.x,
                                               bordersMax.y - bordersMin.y, 0.05f));

            // Current view at 100% zoom, so the framing can be judged while authoring.
            Vector2 half = ViewHalfExtents(1f);
            Vector2 focus = Application.isPlaying ? _focus
                          : (target != null ? (Vector2)target.position : (Vector2)c);
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(focus.x, focus.y, planeZ),
                                new Vector3(half.x * 2f, half.y * 2f, 0.05f));
        }
    }
}

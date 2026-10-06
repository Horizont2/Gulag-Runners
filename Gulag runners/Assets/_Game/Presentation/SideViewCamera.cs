using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// The side-view camera of docs/02.
    ///
    /// Following is a three-zone system rather than a spring on the player's position, because
    /// a camera that reacts to every step is exhausting to watch on a phone:
    ///
    ///   dead zone  the player moves freely and the camera does not move at all
    ///   soft zone  the camera eases after them, just enough to push them back to the dead edge
    ///   hard edge  the player can never cross it; the camera is clamped the same frame
    ///
    /// Vertically the camera anchors to the floor the player last stood on instead of tracking
    /// them. Following a jump arc makes the whole arena bob, and in an arena of 3 m floors the
    /// view holds a whole storey anyway — so the camera only moves when the player actually
    /// changes floor, climbs, or falls far enough to risk leaving the screen.
    ///
    /// Framing is expressed as "how many metres tall is the view" rather than as a distance,
    /// because the rule that matters is from docs/02: a 1.8 m fighter must fill 17-20% of
    /// screen height or nothing is readable on a 6" phone.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]   // after the player has moved this frame
    public sealed class SideViewCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The player this camera belongs to. Each player has their own camera.")]
        public PlayerController player;

        [Tooltip("Filled in from Player automatically. Assign directly only to follow something " +
                 "that is not a player.")]
        public Transform target;

        [Tooltip("Optional. When set, the camera frames both fighters and zooms to fit.")]
        public Transform secondTarget;

        public bool autoFindPlayer;

        [Header("Framing")]
        [Tooltip("Height of the visible world in metres. A 1.8 m fighter at 10 m fills 18% of " +
                 "screen height, the readable minimum from docs/02.")]
        [Range(4f, 30f)] public float visibleWorldHeight = 10f;

        [Tooltip("Narrow FOV keeps fighters from deforming at the edges of frame.")]
        [Range(14f, 50f)] public float fieldOfView = 30f;

        [Tooltip("Downward tilt. docs/02 asks for 10-14 degrees: enough to read volume, not " +
                 "enough to distort.")]
        [Range(0f, 30f)] public float pitchDegrees = 12f;

        [Tooltip("Z of the gameplay plane the camera focuses on.")]
        public float planeZ;

        [Tooltip("Scale the framing by the viewport's height, so Visible World Height always " +
                 "means the full-screen framing of the real game.\n\n" +
                 "Without this a half-height split-screen view shows TWICE the width: 35.6 m " +
                 "against a 21.6 m arena, which pins the camera to the centre of the border box " +
                 "and makes every follow rule below look broken, while the fighter drops to 9% " +
                 "of screen height — half the readable minimum. With it, both modes show the " +
                 "same 17.8 m span and the same 18%.")]
        public bool scaleFramingToViewport = true;

        [Tooltip("Metres above the feet that the camera aims at. Roughly chest height, so there " +
                 "is more headroom than floor in frame.")]
        public float aimHeight = 1.1f;

        [Header("Dead zone (fraction of the view)")]
        [Tooltip("Half-width of the box the player moves in without the camera reacting.")]
        [Range(0f, 0.45f)] public float deadZoneX = 0.12f;
        [Range(0f, 0.45f)] public float deadZoneY = 0.18f;

        [Tooltip("The player can never get closer to the edge of frame than this. The camera is " +
                 "clamped the same frame, with no easing, because being pushed off screen is " +
                 "worse than a hard camera move.")]
        [Range(0.05f, 0.5f)] public float hardEdgeX = 0.38f;
        [Range(0.05f, 0.5f)] public float hardEdgeY = 0.40f;

        [Header("Follow")]
        [Tooltip("Seconds for the camera to catch up horizontally inside the soft zone.")]
        public float horizontalSmoothTime = 0.18f;

        [Tooltip("Seconds to settle on a new floor after landing or stepping off a ladder.")]
        public float verticalSmoothTime = 0.22f;

        [Tooltip("While climbing, the camera tracks continuously and faster.")]
        public float climbSmoothTime = 0.10f;

        [Header("Look-ahead")]
        [Tooltip("Metres the camera leads in the direction of travel, so there is more room " +
                 "ahead than behind.")]
        public float lookAhead = 2.0f;

        [Tooltip("Seconds for the lead to swing across after a direction change. Long on " +
                 "purpose: a lead that snaps makes the camera lurch every time the player taps " +
                 "the other way.")]
        public float lookAheadSmoothTime = 0.45f;

        [Tooltip("Below this speed the lead returns to centre, so standing still re-centres.")]
        public float lookAheadMinSpeed = 0.4f;

        [Header("Vertical anchoring")]
        [Tooltip("Hold the camera on the floor last stood on instead of following jump arcs.")]
        public bool anchorToGround = true;

        [Tooltip("While airborne, rising this far above the anchor pulls the camera up anyway, " +
                 "so a jump to a high ledge is never blind.")]
        public float airRiseSlack = 2.2f;

        [Tooltip("Falling this far below the anchor pulls the camera down immediately. A fall " +
                 "the player cannot see is the one unforgivable camera failure.")]
        public float airFallSlack = 1.6f;

        [Header("Two-target framing")]
        [Tooltip("Extra metres kept around both fighters when framing them together.")]
        public float framingPadding = 3f;
        [Range(0.3f, 1f)] public float minZoom = 0.6f;    // docs/02: never below 60%
        [Range(1f, 2f)] public float maxZoom = 1.1f;      // docs/02: never above 110%
        public float zoomSmoothTime = 0.3f;

        [Header("Borders")]
        [Tooltip("Clamp the view to the arena so the camera never shows the void behind it.")]
        public bool useBorders = true;
        public Vector2 bordersMin = new Vector2(-10.8f, 0f);
        public Vector2 bordersMax = new Vector2(10.8f, 10f);

        [Tooltip("When the arena is smaller than the view, centre on it instead of clamping.")]
        public bool centreWhenSmallerThanView = true;

        [Header("Shake")]
        [Tooltip("Trauma from a hard landing, scaled by impact speed. 0 disables landing shake.")]
        [Range(0f, 1f)] public float landingTrauma = 0.25f;
        [Tooltip("Metres of offset at full trauma.")]
        public float shakeAmplitude = 0.25f;
        [Tooltip("Degrees of roll at full trauma.")]
        public float shakeRoll = 1.5f;
        public float shakeFrequency = 22f;
        [Tooltip("Trauma lost per second. Shake falls off as trauma squared, so it ends softly.")]
        public float traumaDecay = 1.8f;

        [Header("Split screen (local testing only)")]
        [Tooltip("Apply the rect below on Awake. On a device each player owns the whole screen.")]
        public bool applyViewportRect;
        public Rect viewportRect = new Rect(0f, 0f, 1f, 1f);

        [Header("Debug")]
        public bool drawZones = true;

        Camera _camera;
        Vector2 _focus;
        Vector2 _focusVelocity;
        float _anchorY;
        float _lookAhead;
        float _lookAheadVelocity;
        float _zoom = 1f;
        float _zoomVelocity;
        float _trauma;
        float _shakeSeed;
        PlayerController _subscribed;

        public Camera Camera => _camera;
        /// <summary>The point the camera is looking at on the gameplay plane.</summary>
        public Vector2 Focus => _focus;

        /// <summary>
        /// Visible height actually used, after the viewport correction. Authoring stays in
        /// full-screen terms no matter how the screen is divided during local testing.
        /// </summary>
        public float EffectiveViewHeight =>
            visibleWorldHeight *
            (scaleFramingToViewport && applyViewportRect ? Mathf.Clamp01(viewportRect.height) : 1f);

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _shakeSeed = UnityEngine.Random.value * 100f;

            if (player != null) target = player.transform;

            if (target == null && autoFindPlayer)
            {
                PlayerController found = FindFirstObjectByType<PlayerController>();
                if (found != null)
                {
                    player = found;
                    target = found.transform;
                    Debug.LogWarning($"{name}: no target assigned, took the first player in the " +
                                     "scene. Assign Player in the inspector — with two players " +
                                     "both cameras would otherwise follow the same one.", this);
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

        void OnEnable()
        {
            Subscribe();
            SnapToTarget();
        }

        void OnDisable()
        {
            if (_subscribed != null) _subscribed.Landed -= OnLanded;
            _subscribed = null;
        }

        void Subscribe()
        {
            if (_subscribed == player) return;
            if (_subscribed != null) _subscribed.Landed -= OnLanded;
            _subscribed = player;
            if (_subscribed != null) _subscribed.Landed += OnLanded;
        }

        void OnLanded(float impactSpeed)
        {
            if (landingTrauma <= 0f || player == null) return;

            // Only a real drop shakes. Stepping off a kerb must not.
            float hard = Mathf.Max(0.1f, player.tuning.hardLandSpeed);
            float t = Mathf.InverseLerp(hard * 0.6f, player.tuning.maxFallSpeed, impactSpeed);
            AddTrauma(landingTrauma * t);
        }

        /// <summary>Adds shake. Call it from hits and explosions too; it accumulates and decays.</summary>
        public void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);

        void OnValidate()
        {
            if (bordersMax.x < bordersMin.x) bordersMax.x = bordersMin.x;
            if (bordersMax.y < bordersMin.y) bordersMax.y = bordersMin.y;
            if (maxZoom < minZoom) maxZoom = minZoom;
            if (hardEdgeX < deadZoneX) hardEdgeX = deadZoneX;
            if (hardEdgeY < deadZoneY) hardEdgeY = deadZoneY;
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

            Subscribe();

            Vector2 aim = AimPoint(dt, out float desiredZoom);

            _zoom = Mathf.Clamp(
                Mathf.SmoothDamp(_zoom, desiredZoom, ref _zoomVelocity, zoomSmoothTime,
                                 Mathf.Infinity, dt),
                minZoom, maxZoom);

            Vector2 half = ViewHalfExtents(_zoom);
            Vector2 dead = new Vector2(half.x * deadZoneX, half.y * deadZoneY);
            Vector2 hard = new Vector2(half.x * hardEdgeX, half.y * hardEdgeY);

            // Soft follow: move only far enough to put the aim point back on the dead edge.
            float wantX = SoftTarget(_focus.x, aim.x, dead.x);
            float wantY = SoftTarget(_focus.y, aim.y, dead.y);

            float vSmooth = player != null && player.State.Mode == Sim.MoveMode.Climbing
                ? climbSmoothTime
                : verticalSmoothTime;

            _focus.x = Mathf.SmoothDamp(_focus.x, wantX, ref _focusVelocity.x,
                                        horizontalSmoothTime, Mathf.Infinity, dt);
            _focus.y = Mathf.SmoothDamp(_focus.y, wantY, ref _focusVelocity.y,
                                        vSmooth, Mathf.Infinity, dt);

            // Hard edge: never let the target leave the frame, easing or not.
            _focus.x = Mathf.Clamp(_focus.x, aim.x - hard.x, aim.x + hard.x);
            _focus.y = Mathf.Clamp(_focus.y, aim.y - hard.y, aim.y + hard.y);

            _focus = ClampToBorders(_focus, _zoom);

            _trauma = Mathf.Max(0f, _trauma - traumaDecay * dt);
            Place(_focus, _zoom, Shake(dt));
        }

        static float SoftTarget(float focus, float aim, float dead)
        {
            float offset = aim - focus;
            if (Mathf.Abs(offset) <= dead) return focus;
            return aim - Mathf.Sign(offset) * dead;
        }

        /// <summary>Where the camera wants to look: the player, led by travel and anchored by floor.</summary>
        Vector2 AimPoint(float dt, out float desiredZoom)
        {
            desiredZoom = 1f;
            Vector2 a = target.position;

            if (secondTarget != null)
            {
                Vector2 b = secondTarget.position;
                float neededHeight = Mathf.Abs(a.y - b.y) + framingPadding;
                float neededWidth = Mathf.Abs(a.x - b.x) + framingPadding;
                float aspect = _camera != null && _camera.aspect > 0f ? _camera.aspect : 1.777f;
                desiredZoom = Mathf.Clamp(Mathf.Max(neededHeight, neededWidth / aspect)
                                          / Mathf.Max(0.01f, EffectiveViewHeight), minZoom, maxZoom);
                _lookAhead = 0f;
                return new Vector2((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f + aimHeight);
            }

            // Lead by where the player is actually going, not by which way they last pressed:
            // facing flips on a tap, velocity does not.
            float speed = 0f, direction = 0f;
            if (player != null)
            {
                speed = Mathf.Abs(player.State.Velocity.X.Raw / (float)Sim.Fix.RawOne);
                direction = speed >= lookAheadMinSpeed
                    ? Mathf.Sign(player.State.Velocity.X.Raw)
                    : 0f;
            }

            _lookAhead = Mathf.SmoothDamp(_lookAhead, direction * lookAhead, ref _lookAheadVelocity,
                                          lookAheadSmoothTime, Mathf.Infinity, dt);

            float y = AnchoredY(a.y);
            return new Vector2(a.x + _lookAhead, y + aimHeight);
        }

        /// <summary>
        /// Vertical anchoring. On the ground the anchor is the floor; in the air it stays put so
        /// the view does not bob through every jump, and only gives way when the player gets far
        /// enough from it to risk leaving the screen.
        /// </summary>
        float AnchoredY(float targetY)
        {
            if (player == null || !anchorToGround) return targetY;

            // The player object's origin sits at body centre; the anchor is in the same space.
            float feetToOrigin = player.visualYOffset;

            switch (player.State.Mode)
            {
                case Sim.MoveMode.Grounded:
                    _anchorY = player.GroundedY + feetToOrigin;
                    return _anchorY;

                case Sim.MoveMode.Climbing:
                    _anchorY = targetY;             // a ladder is deliberate vertical travel
                    return _anchorY;

                default:
                    if (targetY > _anchorY + airRiseSlack) _anchorY = targetY - airRiseSlack;
                    else if (targetY < _anchorY - airFallSlack) _anchorY = targetY + airFallSlack;
                    return _anchorY;
            }
        }

        /// <summary>Half the visible world size at the gameplay plane, for the current zoom.</summary>
        public Vector2 ViewHalfExtents(float zoom)
        {
            float height = EffectiveViewHeight * zoom;
            float aspect = _camera != null && _camera.aspect > 0f ? _camera.aspect : 1.777f;
            return new Vector2(height * aspect * 0.5f, height * 0.5f);
        }

        Vector2 ClampToBorders(Vector2 focus, float zoom)
        {
            if (!useBorders) return focus;

            Vector2 half = ViewHalfExtents(zoom);
            float minX = bordersMin.x + half.x, maxX = bordersMax.x - half.x;
            float minY = bordersMin.y + half.y, maxY = bordersMax.y - half.y;

            focus.x = minX > maxX
                ? (centreWhenSmallerThanView ? (bordersMin.x + bordersMax.x) * 0.5f : focus.x)
                : Mathf.Clamp(focus.x, minX, maxX);
            focus.y = minY > maxY
                ? (centreWhenSmallerThanView ? (bordersMin.y + bordersMax.y) * 0.5f : focus.y)
                : Mathf.Clamp(focus.y, minY, maxY);

            return focus;
        }

        Vector3 Shake(float dt)
        {
            if (_trauma <= 0f) return Vector3.zero;

            // Trauma squared: a big hit is violent, a small one is barely there.
            float power = _trauma * _trauma;
            float t = Time.time * shakeFrequency + _shakeSeed;
            return new Vector3((Mathf.PerlinNoise(t, 0f) * 2f - 1f) * shakeAmplitude * power,
                               (Mathf.PerlinNoise(0f, t) * 2f - 1f) * shakeAmplitude * power,
                               (Mathf.PerlinNoise(t, t) * 2f - 1f) * shakeRoll * power);
        }

        void Place(Vector2 focus, float zoom, Vector3 shake)
        {
            float halfFov = Mathf.Deg2Rad * fieldOfView * 0.5f;
            float pitch = Mathf.Deg2Rad * pitchDegrees;

            // Distance that makes the plane show exactly visibleWorldHeight * zoom metres,
            // corrected for the tilt.
            float distance = EffectiveViewHeight * zoom * 0.5f * Mathf.Cos(pitch) / Mathf.Tan(halfFov);

            Quaternion rotation = Quaternion.Euler(pitchDegrees, 0f, shake.z);
            Vector3 focusPoint = new Vector3(focus.x + shake.x, focus.y + shake.y, planeZ);

            transform.SetPositionAndRotation(focusPoint - rotation * Vector3.forward * distance,
                                             rotation);
            if (_camera != null) _camera.fieldOfView = fieldOfView;
        }

        /// <summary>Jumps straight to the target. Call on spawn and after a teleport.</summary>
        public void SnapToTarget()
        {
            if (target == null) return;

            _lookAhead = 0f;
            _lookAheadVelocity = 0f;
            _focusVelocity = Vector2.zero;
            _trauma = 0f;
            _anchorY = target.position.y;

            Vector2 aim = AimPoint(1f, out float z);
            _zoom = Mathf.Clamp(z, minZoom, maxZoom);
            _focus = ClampToBorders(aim, _zoom);
            Place(_focus, _zoom, Vector3.zero);
        }

        /// <summary>Sets the arena borders at runtime, e.g. from the generated arena (docs/05).</summary>
        public void SetBorders(Vector2 min, Vector2 max)
        {
            bordersMin = Vector2.Min(min, max);
            bordersMax = Vector2.Max(min, max);
        }

        /// <summary>Fits the borders around every SimCollider in the scene, plus headroom.</summary>
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
                minX = Mathf.Min(minX, r.xMin); minY = Mathf.Min(minY, r.yMin);
                maxX = Mathf.Max(maxX, r.xMax); maxY = Mathf.Max(maxY, r.yMax);
            }
            SetBorders(new Vector2(minX, minY), new Vector2(maxX, maxY + 3f));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(new Vector3((bordersMin.x + bordersMax.x) * 0.5f,
                                            (bordersMin.y + bordersMax.y) * 0.5f, planeZ),
                                new Vector3(bordersMax.x - bordersMin.x,
                                            bordersMax.y - bordersMin.y, 0.05f));

            if (!drawZones) return;

            Vector2 half = ViewHalfExtents(Application.isPlaying ? _zoom : 1f);
            Vector2 focus = Application.isPlaying ? _focus
                          : (target != null ? (Vector2)target.position : Vector2.zero);
            Vector3 c = new Vector3(focus.x, focus.y, planeZ);

            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);   // the whole view
            Gizmos.DrawWireCube(c, new Vector3(half.x * 2f, half.y * 2f, 0.05f));

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);  // hard edge
            Gizmos.DrawWireCube(c, new Vector3(half.x * hardEdgeX * 2f, half.y * hardEdgeY * 2f, 0.05f));

            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.8f);   // dead zone
            Gizmos.DrawWireCube(c, new Vector3(half.x * deadZoneX * 2f, half.y * deadZoneY * 2f, 0.05f));
        }
    }
}

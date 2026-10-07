using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>Which way the character model looks in its own local space.</summary>
    public enum ModelForward
    {
        /// <summary>Unity's humanoid rigs face +Z; this is almost always the right answer.</summary>
        PlusZ = 0,
        MinusZ = 1,
        PlusX = 2,
        MinusX = 3
    }

    /// <summary>
    /// Drives one player: samples input, steps the deterministic simulation at a fixed 60 Hz,
    /// and renders the result.
    ///
    /// The split matters. Everything that decides the outcome of the match lives in
    /// <see cref="PlayerMotor"/>, which knows nothing about Unity. This class only feeds it and
    /// draws it. When rollback arrives (docs/06) it replaces the loop below and nothing else.
    ///
    /// There is deliberately no Rigidbody: the simulation does its own collision, because
    /// PhysX does not give bit-identical results across devices.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Wiring (assign these in the inspector)")]
        [Tooltip("The input source component. Assign it: nothing in this project is wired up " +
                 "by code at runtime.")]
        public MonoBehaviour inputSourceBehaviour;

        [Tooltip("The SimWorldBaker in the scene.")]
        public SimWorldBaker worldBaker;

        [Tooltip("The shared round state: which chests are open. Leave empty to use the one in " +
                 "the scene. Without it this player simply cannot open anything.")]
        public MatchState matchState;

        [Tooltip("0 for player one, 1 for player two. A chest records which player is opening " +
                 "it, so the two of them must not share an index.")]
        public int playerIndex;

        [Tooltip("Transform that carries the mesh. Leave empty to move this object directly.")]
        public Transform visualRoot;

        [Header("Tuning")]
        public MovementTuning tuning = new MovementTuning();

        [Header("Placement")]
        [Tooltip("The single gameplay plane. The arena is 3D; the fight is not (docs/02).")]
        public float planeZ;

        [Tooltip("Take the plane's depth from wherever this object is placed in the scene, " +
                 "instead of from the field above.\n\n" +
                 "On a hand-built location the fighter belongs where the level artist put him, " +
                 "and a configured depth silently teleports him somewhere else the moment you " +
                 "press play. With this on, Plane Z is filled in from the scene and shows you " +
                 "the depth you are actually on.")]
        public bool depthFromScene = true;

        [Tooltip("Follow a ladder's own depth while climbing it.\n\n" +
                 "A ladder modelled in front of the platform it serves is climbed in front of " +
                 "it too; staying on the plane would draw the body inside the floor it is " +
                 "climbing past. 0 snaps.")]
        public float depthSmoothTime = 0.12f;

        [Tooltip("Distance from this object's origin down to the feet, because the simulation " +
                 "tracks the feet. For the scaled capsule this is 0.9, half the body height.")]
        public float visualYOffset = 0.9f;

        [Tooltip("Squash the visual when crouching. Leave off for a rigged character — the " +
                 "crouch clip conveys it, and squashing a skeleton reads as a bug.")]
        public bool squashOnCrouch = true;

        [Header("Facing")]
        [Tooltip("Which way the model looks in its own space. A Unity humanoid rig must face +Z " +
                 "for its avatar to be valid, so that is the default. Get this wrong and the " +
                 "character turns towards and away from the camera instead of left and right.")]
        public ModelForward modelForward = ModelForward.PlusZ;

        [Tooltip("Seconds to turn around. 0 snaps. A short turn reads as a character changing " +
                 "direction; an instant flip reads as a sprite.")]
        [Range(0f, 0.4f)] public float turnSmoothTime = 0.07f;

        [Tooltip("Which way this player looks when the round starts. Set player two to -1 so the " +
                 "two of them face each other.")]
        public int spawnFacing = 1;

        [Tooltip("Turn to face the ladder while climbing instead of staying side-on. The climb " +
                 "clips are authored with the character facing the rungs, so played side-on they " +
                 "read as someone climbing sideways through thin air.")]
        public bool faceLadderWhenClimbing = true;

        [Tooltip("Degrees turned back towards the camera while on a ladder. Square to the ladder " +
                 "is a flat back and a dead silhouette; a few degrees of angle keeps the body " +
                 "readable without losing the sense that the character is facing the rungs.")]
        [Range(0f, 60f)] public float ladderViewAngle = 25f;

        [Header("Safety")]
        [Tooltip("At spawn, seat the player on the first floor below instead of dropping them " +
                 "onto it. A spawn that begins with a fall reads as broken collision.")]
        public bool snapToGroundOnSpawn = true;

        [Tooltip("How far down to look for that floor, in metres.")]
        public float snapSearchDistance = 6f;

        [Tooltip("Fall below this height and the player is returned to the spawn point. " +
                 "Without it, a missing floor looks like an endless fall with no explanation.")]
        public float killY = -25f;

        [Header("Debug")]
        public bool drawBody = true;

        /// <summary>Raised on any tick that made a noise. Loudness is simulation state (docs/01).</summary>
        public event Action<NoiseLevel, Vector2> Noise;

        /// <summary>Raised on the tick a fall ends, with the impact speed in m/s.</summary>
        public event Action<float> Landed;

        /// <summary>Raised on the tick something is taken off the floor.</summary>
        public event Action<ItemId> PickedUp;

        /// <summary>Raised on the tick something is pushed out of a slot to make room.</summary>
        public event Action<ItemId> Dropped;

        /// <summary>Raised on the tick this player is hurt, with the damage that got through.</summary>
        public event Action<int> Hurt;

        /// <summary>Raised on the tick this player dies.</summary>
        public event Action Died;

        /// <summary>Feet height of the last ground stood on. The camera anchors to this.</summary>
        public float GroundedY { get; private set; }

        public PlayerSimState State => _state;
        public MoveConfig Config => _config;
        public SimWorld World => _world;
        /// <summary>Simulated feet position in world units.</summary>
        public Vector2 FeetPosition => new Vector2(_state.Position.X.Raw / (float)Fix.RawOne,
                                                   _state.Position.Y.Raw / (float)Fix.RawOne);

        IPlayerInputSource _input;
        SimWorld _world;
        MoveConfig _config;
        PlayerSimState _state;
        PlayerSimState _previous;

        float _accumulator;
        InputFlags _held;             // what is held right now
        InputFlags _pending;          // held, plus anything tapped since the last tick
        Vector3 _baseVisualScale = Vector3.one;
        Vector2 _spawnPoint;
        bool _warnedEmptyWorld;
        float _yaw;
        float _yawVelocity;

        const float TickSeconds = 1f / PlayerMotor.TicksPerSecond;
        const int MaxTicksPerFrame = 8;   // never let a hitch turn into a spiral of death

        void Awake()
        {
            _config = tuning.ToConfig();

            _input = inputSourceBehaviour as IPlayerInputSource;
            if (_input == null)
            {
                _input = GetComponentInChildren<IPlayerInputSource>(true);
                if (_input != null)
                    Debug.LogWarning($"{name}: Input Source Behaviour is empty, falling back to " +
                                     "a component on this object. Assign it in the inspector.", this);
                else
                    Debug.LogError($"{name}: no input source. Add a PlayerInputRouter " +
                                   "(or DeviceInputSource / TouchInputSource) and assign it.", this);
            }

            if (worldBaker == null)
            {
                worldBaker = SimWorldBaker.Instance;
                if (worldBaker != null)
                    Debug.LogWarning($"{name}: World Baker is empty, using the one in the scene. " +
                                     "Assign it in the inspector.", this);
                else
                    Debug.LogError($"{name}: no SimWorldBaker in the scene. The player will " +
                                   "fall forever.", this);
            }
            _world = worldBaker != null ? (worldBaker.World ?? worldBaker.Bake()) : new SimWorld();

            if (matchState == null) matchState = MatchState.Instance;

            if (visualRoot == null) visualRoot = transform;
            _baseVisualScale = visualRoot.localScale;

            Vector3 p = transform.position;
            if (depthFromScene) planeZ = p.z;
            _depth = planeZ;
            _spawnPoint = new Vector2(p.x, p.y - visualYOffset);
            SpawnAt(_spawnPoint, spawnFacing);
        }

        void SpawnAt(Vector2 feet, int facing)
        {
            FixVec2 position = new FixVec2(ToFix(feet.x), ToFix(feet.y));

            if (snapToGroundOnSpawn && _world != null &&
                PlayerMotor.TryFindGroundBelow(position, _world, in _config,
                                               ToFix(snapSearchDistance), out Fix groundY))
            {
                position.Y = groundY;
            }

            _state = PlayerSimState.Spawn(position, (sbyte)(facing >= 0 ? 1 : -1), in _config);
            _previous = _state;
            _accumulator = 0f;

            // Start already facing the right way: a spin on spawn looks like a glitch.
            _yaw = TargetYaw(_state.Facing, FacingLadder(_state.Mode));
            _yawVelocity = 0f;
            GroundedY = position.Y.Raw / (float)Fix.RawOne;

            Render(1f);
        }

        void Update()
        {
            // Sample once per frame. _held is what is down now; _pending also carries anything
            // tapped and released between two ticks, so a fast tap on a phone is never lost.
            if (_input != null)
            {
                _held = _input.Read();
                _pending |= _held;
            }

            _accumulator += Time.deltaTime;

            int ticks = 0;
            while (_accumulator >= TickSeconds && ticks < MaxTicksPerFrame)
            {
                _accumulator -= TickSeconds;
                ticks++;
                Tick(_pending);
                _pending = _held;          // taps are consumed, held buttons stay held
            }

            if (ticks == MaxTicksPerFrame) _accumulator = 0f;

            // Interpolate so the view is smooth above 60 fps without the simulation varying.
            Render(Mathf.Clamp01(_accumulator / TickSeconds));
        }

        void Tick(InputFlags input)
        {
            _previous = _state;
            if (worldBaker != null && worldBaker.World != null) _world = worldBaker.World;

            if (_world == null || _world.Solids.Length == 0)
            {
                // Ask for one rebake before giving up: if this player awoke before the arena
                // was ready, the world is simply stale rather than wrong.
                if (!_warnedEmptyWorld && worldBaker != null) _world = worldBaker.Bake();

                if (!_warnedEmptyWorld && (_world == null || _world.Solids.Length == 0))
                {
                    _warnedEmptyWorld = true;
                    Debug.LogError(
                        $"{name}: the simulated world has no solid boxes, so there is nothing to " +
                        "stand on.\n" +
                        (worldBaker != null ? worldBaker.LastReport : "no SimWorldBaker assigned"),
                        this);
                }
            }

            bool wasAirborne = _state.Mode == MoveMode.Airborne;
            float fallSpeed = -_state.Velocity.Y.Raw / (float)Fix.RawOne;

            // The fight decides what the body is allowed to do before it moves: a swing is a
            // commitment to where you are standing, and that is what makes reach matter.
            InputFlags moveInput = input;
            if (matchState != null)
                moveInput = CombatMotor.Step(ref _state, input, matchState.Combat, in _config);

            PlayerMotor.Step(ref _state, moveInput, _world, in _config);

            if (_state.WasHit) Hurt?.Invoke(_state.DamageTaken);
            if (_state.JustDied) Died?.Invoke();

            // Loot after movement: where the body ended up this tick decides which chest it is
            // standing at. Movement never depends on the inventory, so the order is not a choice
            // that can be got wrong later.
            if (matchState != null)
            {
                // Loot in the air belongs to nobody, so it steps once for the whole match rather
                // than once per player. Whoever asks first drives it, from inside this same loop.
                matchState.AdvanceShared(this, _world);

                LootMotor.Step(ref _state, playerIndex, input, _world, matchState.ChestStates,
                               matchState.GroundItems, matchState.Config, matchState.Drops,
                               in _config);

                if (_state.OpenedChest >= 0) matchState.ReportOpened(playerIndex, _state.OpenedChest);
                if (_state.PickedUp != ItemId.None) PickedUp?.Invoke(_state.PickedUp);
                if (_state.Dropped != ItemId.None) Dropped?.Invoke(_state.Dropped);
            }

            if (_state.Mode == MoveMode.Grounded)
            {
                GroundedY = _state.Position.Y.Raw / (float)Fix.RawOne;
                if (wasAirborne) Landed?.Invoke(Mathf.Max(0f, fallSpeed));
            }

            if (_state.Noise != NoiseLevel.Silent)
                Noise?.Invoke(_state.Noise, FeetPosition);

            if (_state.Position.Y.Raw < ToFix(killY).Raw)
            {
                Debug.LogWarning($"{name}: fell past killY, returning to the spawn point.", this);
                SpawnAt(_spawnPoint, _state.Facing);
            }
        }

        float _depth;
        float _depthVelocity;

        void Render(float alpha)
        {
            Vector2 a = ToVector(_previous.Position);
            Vector2 b = ToVector(_state.Position);
            Vector2 p = Vector2.Lerp(a, b, alpha);

            transform.position = new Vector3(p.x, p.y + visualYOffset, Depth());

            // The visual root keeps whatever local offset it was authored with: a capsule sits
            // centred on this object, while a character model hangs from it by its feet.
            Transform v = visualRoot != null ? visualRoot : transform;

            float target = TargetYaw(_state.Facing, FacingLadder(_state.Mode));
            _yaw = turnSmoothTime <= 0.001f
                ? target
                : Mathf.SmoothDampAngle(_yaw, target, ref _yawVelocity, turnSmoothTime,
                                        Mathf.Infinity, Time.deltaTime);
            v.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            if (squashOnCrouch)
            {
                float squash = _state.Crouching ? tuning.crouchHeight / tuning.bodyHeight : 1f;
                v.localScale = new Vector3(_baseVisualScale.x,
                                           _baseVisualScale.y * squash,
                                           _baseVisualScale.z);
            }
        }

        /// <summary>
        /// What depth to draw at this frame: the ladder's while climbing one, the plane's
        /// otherwise, eased between the two so the step out of a climb is a step and not a cut.
        /// </summary>
        float Depth()
        {
            float want = planeZ;
            int ladder = _state.LadderIndex;
            if (ladder >= 0 && worldBaker != null &&
                ladder < worldBaker.LadderDepths.Length)
                want = worldBaker.LadderDepths[ladder];

            if (depthSmoothTime <= 0.001f) { _depth = want; return _depth; }

            _depth = Mathf.SmoothDamp(_depth, want, ref _depthVelocity, depthSmoothTime,
                                      Mathf.Infinity, Time.deltaTime);
            return _depth;
        }

        /// <summary>Teleports the simulation. Use this for spawning, not transform.position.</summary>
        public void Teleport(Vector2 feetPosition, int facing = 1)
        {
            _spawnPoint = feetPosition;
            SpawnAt(feetPosition, facing);
        }

        /// <summary>Re-reads the inspector tuning. Handy while tuning in play mode.</summary>
        public void ApplyTuning() => _config = tuning.ToConfig();

        /// <summary>
        /// Replaces this player's whole simulation state.
        ///
        /// Only <see cref="MatchState"/> calls it, and only to write back the result of resolving
        /// hits for the match. It exists because the players still own their own state while two
        /// of them tick independently; the match ticker that rollback needs (docs/06) takes that
        /// ownership away, and this method goes with it.
        /// </summary>
        public void OverwriteState(in PlayerSimState state) => _state = state;

        /// <summary>Back on their feet at the spawn point, whole. One call is a new round.</summary>
        public void Respawn() => SpawnAt(_spawnPoint, spawnFacing);

        /// <summary>
        /// Yaw correction for a model that does not look along +Z. Everything else is expressed
        /// as a world direction and then offset by this, so there is one place that knows which
        /// way a given character faces.
        /// </summary>
        float ModelYawOffset => modelForward switch
        {
            ModelForward.PlusZ => 0f,
            ModelForward.MinusZ => 180f,
            ModelForward.PlusX => -90f,
            _ => 90f
        };

        /// <summary>
        /// Whether the character should be turned towards the rungs. The grab counts: starting the
        /// turn with the reach means the character is already facing the ladder by the time it is
        /// on it, instead of swinging round afterwards. The climb-out does not — it steps off
        /// sideways, and that is the direction it should be looking.
        /// </summary>
        static bool FacingLadder(MoveMode mode) =>
            mode == MoveMode.Climbing || mode == MoveMode.Mounting;

        /// <summary>
        /// Yaw that points the model where it should look.
        ///
        /// Walking, that is +X or -X — the only two directions that exist on the gameplay plane.
        /// Rotating a +Z-facing model by 0 or 180 degrees, the obvious-looking thing, turns it
        /// towards and away from the camera instead, which is no turn at all.
        ///
        /// Climbing, it is into the screen: the character turns its back to the camera and faces
        /// the rungs, angled a little so the silhouette still reads as a body.
        /// </summary>
        float TargetYaw(int facing, bool climbing)
        {
            if (climbing && faceLadderWhenClimbing)
            {
                // 0 degrees of world yaw points along +Z, away from the camera and into the
                // ladder. The skew leans back towards whichever side the character came from.
                float side = facing >= 0 ? 1f : -1f;
                return ModelYawOffset + ladderViewAngle * side;
            }

            float walkYaw = facing >= 0 ? 90f : -90f;    // +X or -X in world yaw
            return ModelYawOffset + walkYaw;
        }

        static Fix ToFix(float metres) => Fix.FromMilli(Mathf.RoundToInt(metres * 1000f));

        static Vector2 ToVector(FixVec2 v) =>
            new Vector2(v.X.Raw / (float)Fix.RawOne, v.Y.Raw / (float)Fix.RawOne);

        void OnDrawGizmosSelected()
        {
            if (!drawBody) return;

            float h = Application.isPlaying && _state.Crouching ? tuning.crouchHeight : tuning.bodyHeight;
            Vector3 feet = Application.isPlaying
                ? new Vector3(FeetPosition.x, FeetPosition.y, planeZ)
                : new Vector3(transform.position.x, transform.position.y - visualYOffset, planeZ);

            // The simulated body: this box, and nothing about the mesh, is what collides.
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(feet + new Vector3(0f, h * 0.5f, 0f),
                                new Vector3(tuning.bodyWidth, h, 0.05f));

            // Where the model actually starts and ends, so a mismatch is visible rather than
            // guessed at. Only the height is drawn: a T-pose's width spans both arms and would
            // say nothing useful about how wide the character is.
            if (visualRoot != null && visualRoot != transform)
            {
                Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                    Gizmos.color = new Color(0.8f, 0.8f, 0.8f, 0.6f);
                    float w = tuning.bodyWidth * 0.8f;
                    Gizmos.DrawLine(new Vector3(feet.x - w, b.min.y, planeZ),
                                    new Vector3(feet.x + w, b.min.y, planeZ));
                    Gizmos.DrawLine(new Vector3(feet.x - w, b.max.y, planeZ),
                                    new Vector3(feet.x + w, b.max.y, planeZ));
                }
            }

            // Jump apex, so the "a jump must not clear a floor" rule is visible while authoring.
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawLine(feet + new Vector3(-0.5f, tuning.JumpApex, 0f),
                            feet + new Vector3(0.5f, tuning.JumpApex, 0f));
        }
    }
}

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

        [Tooltip("Seconds to ease the drawn height onto a step the simulation has already " +
                 "taken. 0 draws every step the instant it happens.\n\n" +
                 "An AABB world has no slopes, so every ramp in the game is a staircase — the " +
                 "plank in Arena_Demo is seven steps of 11 cm. Walked at running speed that is " +
                 "a 11 cm jump ten times a second, and it reads as exactly what it is: a " +
                 "character going up stairs that are not drawn. The simulation keeps its exact " +
                 "heights, because collision and rollback depend on them; only the drawing is " +
                 "eased, and at this length the steps blend into the straight line the plank " +
                 "actually is.")]
        [Range(0f, 0.3f)] public float stepSmoothTime = 0.07f;

        [Tooltip("The largest height change this will ease, in metres. Anything bigger is drawn " +
                 "at once.\n\n" +
                 "A step is eased; a fall is not. Smoothing a two-metre drop would float the " +
                 "character down after the simulation has already landed him, so this stays " +
                 "just above the step-up height and no higher.")]
        public float stepSmoothMax = 0.4f;

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

        [Tooltip("Lean the body along a ladder that leans.\n\n" +
                 "A ladder propped against a wall is climbed at its own angle. Standing bolt " +
                 "upright on one puts the character's legs through the rungs at one end and " +
                 "leaves them in the air at the other, which is the whole of \"he climbs into " +
                 "the texture\". The angle is measured from the ladder's own geometry at the " +
                 "bake, so there is nothing to keep in step by hand: stand a ladder up and the " +
                 "lean is zero.")]
        public bool matchLadderTilt = true;

        [Tooltip("How much of a leaning ladder's angle the body actually takes, 0 to 1. " +
                 "1 lies the body exactly along the rungs; a little less keeps a climb up a " +
                 "steep ladder from reading as a crawl.")]
        [Range(0f, 1f)] public float ladderTiltAmount = 1f;

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

            // The model has to stand where the fighter stands. Render writes the simulated
            // position — including which slice of the level the body is on — onto THIS
            // object every frame, so an offset on the visual root underneath it is not a
            // presentation choice: it is the model and the body coming apart. The fighter
            // then collides where the simulation says and is DRAWN somewhere else, standing
            // on nothing and swinging from somewhere he is not. In this scene one fighter's
            // model was 2.92 m in front of his body and the other was 3.28 m to the side and
            // 3.3 m in the air, which is every "he is standing on invisible geometry" report
            // at once.
            //
            // visualYOffset is the one knob for this, and it is on the controller, where the
            // simulation can see it: use it for a model that hangs from its root by the
            // middle rather than by the feet.
            if (visualRoot != transform &&
                visualRoot.localPosition.sqrMagnitude > 0.000001f)
            {
                Vector3 lp = visualRoot.localPosition;
                Debug.LogWarning(
                    $"{name}: the visual root sits ({lp.x:0.00}, {lp.y:0.00}, {lp.z:0.00}) " +
                    "away from the body the simulation moves, so the fighter would be drawn " +
                    "somewhere he is not. Zeroed. Move the fighter itself, and use Visual Y " +
                    "Offset for a model that hangs by its middle.", this);
                visualRoot.localPosition = Vector3.zero;
            }

            Vector3 p = transform.position;
            if (depthFromScene) planeZ = p.z;
            _depth = planeZ;
            _spawnPoint = new Vector2(p.x, p.y - visualYOffset);
            SpawnAt(_spawnPoint, spawnFacing);
        }

        void SpawnAt(Vector2 feet, int facing)
        {
            FixVec2 position = new FixVec2(ToFix(feet.x), ToFix(feet.y));

            // The slice comes first, because the seating below is only meaningful on one. The
            // search used to run over every baked box: in a location whose backdrop stands a
            // metre taller than the platform in front of it, that seated the fighter on the
            // backdrop, a box their own lane cannot touch, and the match opened with them
            // falling through the floor.
            Fix depth = ToFix(planeZ);

            if (snapToGroundOnSpawn && _world != null &&
                PlayerMotor.TryFindGroundBelow(position, depth, _world, in _config,
                                               ToFix(snapSearchDistance), out Fix groundY))
            {
                position.Y = groundY;
            }

            _state = PlayerSimState.Spawn(position, (sbyte)(facing >= 0 ? 1 : -1), in _config);
            _state.Depth = depth;

            // Never start inside the level. One box marked solid that should not have been —
            // the wall behind a location, say — lands across the floor, the fighter spawns in
            // it, and because nothing pushes a body out of a box it already overlaps, they
            // fall through the world instead. Loudly, with the offending box named, because
            // the symptom on its own points nowhere.
            if (_world != null &&
                PlayerMotor.TryLiftClear(ref _state, _world, in _config, out Aabb stuckIn))
            {
                Debug.LogWarning(
                    $"{name}: spawned inside a baked solid " +
                    $"(x {stuckIn.MinX.ToMilli() / 1000f:0.00}..{stuckIn.MaxX.ToMilli() / 1000f:0.00}, " +
                    $"y {stuckIn.MinY.ToMilli() / 1000f:0.00}..{stuckIn.MaxY.ToMilli() / 1000f:0.00}) " +
                    $"and was lifted out to y {_state.Position.Y.ToMilli() / 1000f:0.00}. " +
                    "Something in the scene is marked solid that should not be: check that box " +
                    "against the gameplay plane, or mark it Ignore.\n" +
                    (worldBaker != null ? worldBaker.LastReport : "no baker"), this);
            }
            else if (_world != null && _world.Solids.Length > 0 &&
                     !PlayerMotor.TryFindGroundBelow(_state.Position, _state.Depth, _world,
                                                     in _config,
                                                     ToFix(snapSearchDistance), out _))
            {
                Debug.LogWarning(
                    $"{name}: nothing to stand on within {snapSearchDistance} m of the spawn " +
                    $"at ({_state.Position.X.ToMilli() / 1000f:0.00}, " +
                    $"{_state.Position.Y.ToMilli() / 1000f:0.00}), so this fighter will fall.\n" +
                    (worldBaker != null ? worldBaker.LastReport : "no baker"), this);
            }

            _previous = _state;
            _accumulator = 0f;
            _drawnYValid = false;                      // a spawn is not a step

            // Start already facing the right way: a spin on spawn looks like a glitch.
            _yaw = TargetYaw(_state.Facing, FacingLadder(in _state));
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
        float _drawnY;
        float _drawnYVelocity;
        bool _drawnYValid;

        void Render(float alpha)
        {
            Vector2 a = ToVector(_previous.Position);
            Vector2 b = ToVector(_state.Position);
            Vector2 p = Vector2.Lerp(a, b, alpha);

            transform.position = new Vector3(p.x, DrawnY(p.y) + visualYOffset, Depth(alpha));

            // The visual root keeps whatever local offset it was authored with: a capsule sits
            // centred on this object, while a character model hangs from it by its feet.
            Transform v = visualRoot != null ? visualRoot : transform;

            float target = TargetYaw(_state.Facing, FacingLadder(in _state));
            _yaw = turnSmoothTime <= 0.001f
                ? target
                : Mathf.SmoothDampAngle(_yaw, target, ref _yawVelocity, turnSmoothTime,
                                        Mathf.Infinity, Time.deltaTime);
            v.localRotation = LadderTilt() * Quaternion.Euler(0f, _yaw, 0f);

            if (squashOnCrouch)
            {
                float squash = _state.Crouching ? tuning.crouchHeight / tuning.bodyHeight : 1f;
                v.localScale = new Vector3(_baseVisualScale.x,
                                           _baseVisualScale.y * squash,
                                           _baseVisualScale.z);
            }
        }

        /// <summary>
        /// The lean of the ladder being climbed, as a rotation to lay the body along it.
        ///
        /// Taken from the ladder's own baked lean line, so an upright ladder gives identity
        /// and nothing changes. Rotating about world Z tips the model's up axis towards world
        /// X, which is the plane the fight and the lean both live in; the body keeps facing
        /// the rungs while it does it.
        /// </summary>
        Quaternion LadderTilt()
        {
            if (!matchLadderTilt || ladderTiltAmount <= 0.001f) return Quaternion.identity;
            if (!FacingLadder(in _state)) return Quaternion.identity;

            int i = _state.LadderIndex;
            if (_world == null || i < 0 || i >= _world.Ladders.Length ||
                i >= _world.LadderLean.Length)
                return Quaternion.identity;

            Aabb box = _world.Ladders[i];
            float height = (box.MaxY - box.MinY).Raw / (float)Fix.RawOne;
            if (height <= 0.01f) return Quaternion.identity;

            Span lean = _world.LadderLean[i];
            float run = (lean.Max - lean.Min).Raw / (float)Fix.RawOne;
            if (Mathf.Abs(run) < 0.001f) return Quaternion.identity;

            float degrees = Mathf.Atan2(run, height) * Mathf.Rad2Deg * ladderTiltAmount;
            return Quaternion.AngleAxis(-degrees, Vector3.forward);
        }

        /// <summary>
        /// What height to draw at this frame.
        ///
        /// The simulation's own height, eased by a few hundredths of a second while the
        /// fighter is on the ground. That is the whole fix for a ramp that is really a
        /// staircase: the body steps up 11 cm ten times a second and the drawing follows it
        /// as a line. Nothing here feeds back into the simulation — it keeps its exact
        /// heights, because collision and rollback are decided on them.
        ///
        /// Only while grounded, and only for a step-sized change: a fall is drawn as it
        /// happens, or the character would float down after already having landed.
        /// </summary>
        float DrawnY(float simY)
        {
            bool eased = _drawnYValid
                         && stepSmoothTime > 0.001f
                         && _state.Mode == MoveMode.Grounded
                         && Mathf.Abs(simY - _drawnY) <= stepSmoothMax;

            if (!eased)
            {
                _drawnY = simY;
                _drawnYVelocity = 0f;
                _drawnYValid = true;
                return _drawnY;
            }

            _drawnY = Mathf.SmoothDamp(_drawnY, simY, ref _drawnYVelocity, stepSmoothTime,
                                       Mathf.Infinity, Time.deltaTime);
            return _drawnY;
        }

        /// <summary>
        /// What depth to draw at this frame: the ladder's while climbing one, the plane's
        /// otherwise, eased between the two so the step out of a climb is a step and not a cut.
        /// </summary>
        float Depth(float alpha)
        {
            // The simulation owns this now: which slice of the level a body is on decides
            // which boxes are in its way, so it cannot be a presentation flourish. All that
            // is left here is the same interpolation x and y get between ticks.
            float a = _previous.Depth.ToMilli() / 1000f;
            float b = _state.Depth.ToMilli() / 1000f;
            float want = Mathf.Lerp(a, b, alpha);

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
        /// <summary>
        /// Should the model be facing the rungs this frame.
        ///
        /// A climb-out counts when it goes FORWARD — onto the floor the ladder ends at, which
        /// in this location is every ladder, since they all stand in front of the walkway they
        /// serve. Dropping the ladder facing the moment the climb-out began turned the body
        /// side-on while it was still on the rungs, and the whole move then read as a fighter
        /// hauling himself out sideways onto a platform that is in fact straight ahead of him.
        /// A hatch climb-out really is sideways, and that one still turns.
        /// </summary>
        static bool FacingLadder(in PlayerSimState s) =>
            s.Mode == MoveMode.Climbing || s.Mode == MoveMode.Mounting ||
            (s.Mode == MoveMode.Mantling && s.ScriptForward);

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

using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
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

        [Tooltip("Transform that carries the mesh. Leave empty to move this object directly.")]
        public Transform visualRoot;

        [Header("Tuning")]
        public MovementTuning tuning = new MovementTuning();

        [Header("Placement")]
        [Tooltip("The single gameplay plane. The arena is 3D; the fight is not (docs/02).")]
        public float planeZ;

        [Tooltip("Lift applied to the visual because the simulation tracks the feet. " +
                 "For the default 1 m sphere this is 0.5.")]
        public float visualYOffset = 0.5f;

        [Tooltip("Squash the visual when crouching.")]
        public bool squashOnCrouch = true;

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

            if (visualRoot == null) visualRoot = transform;
            _baseVisualScale = visualRoot.localScale;

            Vector3 p = transform.position;
            _spawnPoint = new Vector2(p.x, p.y - visualYOffset);
            SpawnAt(_spawnPoint, 1);
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

            if (!_warnedEmptyWorld && (_world == null || _world.Solids.Length == 0))
            {
                _warnedEmptyWorld = true;
                Debug.LogError(
                    $"{name}: the simulated world has no solid boxes, so there is nothing to " +
                    "stand on. Every platform needs a collider on a layer listed in the " +
                    "SimWorldBaker's Solid Layers.", this);
            }

            PlayerMotor.Step(ref _state, input, _world, in _config);

            if (_state.Noise != NoiseLevel.Silent)
                Noise?.Invoke(_state.Noise, FeetPosition);

            if (_state.Position.Y.Raw < ToFix(killY).Raw)
            {
                Debug.LogWarning($"{name}: fell past killY, returning to the spawn point.", this);
                SpawnAt(_spawnPoint, _state.Facing);
            }
        }

        void Render(float alpha)
        {
            Vector2 a = ToVector(_previous.Position);
            Vector2 b = ToVector(_state.Position);
            Vector2 p = Vector2.Lerp(a, b, alpha);

            transform.position = new Vector3(p.x, p.y + visualYOffset, planeZ);

            if (visualRoot != null && visualRoot != transform)
                visualRoot.localPosition = Vector3.zero;

            Transform v = visualRoot != null ? visualRoot : transform;
            v.localRotation = Quaternion.Euler(0f, _state.Facing >= 0 ? 0f : 180f, 0f);

            if (squashOnCrouch)
            {
                float squash = _state.Crouching ? tuning.crouchHeight / tuning.bodyHeight : 1f;
                v.localScale = new Vector3(_baseVisualScale.x,
                                           _baseVisualScale.y * squash,
                                           _baseVisualScale.z);
            }
        }

        /// <summary>Teleports the simulation. Use this for spawning, not transform.position.</summary>
        public void Teleport(Vector2 feetPosition, int facing = 1)
        {
            _spawnPoint = feetPosition;
            SpawnAt(feetPosition, facing);
        }

        /// <summary>Re-reads the inspector tuning. Handy while tuning in play mode.</summary>
        public void ApplyTuning() => _config = tuning.ToConfig();

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

            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(feet + new Vector3(0f, h * 0.5f, 0f),
                                new Vector3(tuning.bodyWidth, h, 0.05f));

            // Jump apex, so the "a jump must not clear a floor" rule is visible while authoring.
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawLine(feet + new Vector3(-0.5f, tuning.JumpApex, 0f),
                            feet + new Vector3(0.5f, tuning.JumpApex, 0f));
        }
    }
}

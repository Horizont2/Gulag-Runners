using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// One action's effect: a particle system already in the scene, and where to put it.
    ///
    /// Already in the scene, and never instantiated, for the same reason every other view in
    /// this project works that way: a designer has to be able to see the whole set in the
    /// inspector and select one to tune it, and a tick that allocates is a tick the garbage
    /// collector can interrupt at a different moment on each device (docs/06). Leaving one
    /// empty is how you say "this action has no effect yet"; it costs nothing and warns about
    /// nothing.
    /// </summary>
    [Serializable]
    public class ActionEffect
    {
        [Tooltip("The system to play. Leave empty for an action with no effect yet.")]
        public ParticleSystem particles;

        [Tooltip("Optional. Moved here before playing, which is what lets one system serve " +
                 "both feet, or follow a weapon tip. Empty plays it where it is parented.")]
        public Transform at;

        [Tooltip("Shortest gap between two plays, in seconds. Footfalls at a run would " +
                 "otherwise restart the same system every three frames and never be seen.")]
        [Range(0f, 1f)] public float minInterval;

        float _last = -999f;

        /// <summary>Fires it, unless it fired too recently. Safe on an empty slot.</summary>
        public void Play()
        {
            if (particles == null) return;
            if (Time.time - _last < minInterval) return;
            _last = Time.time;

            if (at != null) particles.transform.position = at.position;
            particles.Play(true);
        }

        /// <summary>Holds it on or off, for the ones that run while something lasts.</summary>
        public void Hold(bool on)
        {
            if (particles == null) return;

            if (on)
            {
                if (at != null) particles.transform.position = at.position;
                if (!particles.isPlaying) particles.Play(true);
            }
            else if (particles.isPlaying)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    /// <summary>
    /// Every effect the fighter can throw, in one place, with a field per action.
    ///
    /// docs/01 and docs/02 make the same demand of this that they make of armour: what a
    /// fighter is doing has to be readable from across the arena, on a phone, in a tenth of a
    /// second. A parry and a blocked hit cannot look alike, because the whole reason to hold a
    /// guard up is that the difference is worth reading. Noise is the only way to find the
    /// other player, so a loud footfall has to look loud.
    ///
    /// Everything here is driven from the SIMULATION, not from the animation: the events this
    /// subscribes to fire inside the tick that caused them, which is the only place a frame
    /// that happens to run two ticks or none cannot double or lose them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEffects : MonoBehaviour
    {
        [Header("Wiring")]
        public PlayerController player;

        [Tooltip("The feet. Footfalls, landings and dust go here.")]
        public Transform feet;

        [Tooltip("Where a hit lands on this body — roughly the chest. Blood, sparks, guard " +
                 "flashes.")]
        public Transform body;

        [Tooltip("The business end of whatever is being held. Swing trails and impacts.")]
        public Transform weaponTip;

        [Header("Movement")]
        [Tooltip("A quiet step: crouched, or barely moving. docs/01 makes this the difference " +
                 "between being found and not.")]
        public ActionEffect stepQuiet = new ActionEffect { minInterval = 0.12f };
        public ActionEffect stepLoud = new ActionEffect { minInterval = 0.12f };

        [Tooltip("Landing softly, and landing hard enough to hurt. Two effects on purpose: " +
                 "the fall that costs health has to look like it did.")]
        public ActionEffect landSoft = new ActionEffect();
        public ActionEffect landHard = new ActionEffect();

        public ActionEffect jump = new ActionEffect();

        [Tooltip("The dodge. Reads as the one movement that costs stamina.")]
        public ActionEffect dodge = new ActionEffect();

        [Header("Fight")]
        [Tooltip("The swing leaving the hand, on the frame the hitbox goes live.")]
        public ActionEffect swing = new ActionEffect();

        [Tooltip("Optional. A trail on the weapon, emitting for the length of a swing and " +
                 "nothing else.\n\n" +
                 "The cheapest thing in this component and the one that reads from furthest " +
                 "away: an arc through the air says a swing happened even when the fighter is " +
                 "forty pixels tall. Emitting is switched, never the renderer — switching the " +
                 "renderer makes the tail vanish instead of fading, which looks like a bug.")]
        public TrailRenderer swingTrail;

        [Tooltip("A hit that got through: this fighter is hurt.")]
        public ActionEffect hurt = new ActionEffect();

        [Tooltip("A hit the guard ate. Must not look like Hurt — docs/02 wants the difference " +
                 "readable across the arena, because it is the reason to hold a guard at all.")]
        public ActionEffect blocked = new ActionEffect();

        [Tooltip("A hit turned back on its owner. The loudest thing a defender can do, so the " +
                 "biggest effect in this list.")]
        public ActionEffect parried = new ActionEffect();

        [Tooltip("The guard breaking under pressure.")]
        public ActionEffect guardBroken = new ActionEffect();

        public ActionEffect death = new ActionEffect();

        [Header("Loot")]
        [Tooltip("Held on for as long as a chest is being forced — this is the tell that gives " +
                 "the opener away.")]
        public ActionEffect chestWorking = new ActionEffect();

        public ActionEffect chestOpened = new ActionEffect();
        public ActionEffect pickUp = new ActionEffect();
        public ActionEffect drop = new ActionEffect();

        [Header("Climb")]
        public ActionEffect ladderGrab = new ActionEffect();
        public ActionEffect mantle = new ActionEffect();

        [Header("Debug")]
        public bool logEvents;

        MoveMode _mode = MoveMode.Grounded;
        bool _swinging;
        bool _subscribed;

        void Reset()
        {
            player = GetComponentInParent<PlayerController>();
            feet = transform;
        }

        void OnEnable()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (player == null || _subscribed) return;

            player.Noise += OnNoise;
            player.Landed += OnLanded;
            player.Hurt += OnHurt;
            player.Blocked += OnBlocked;
            player.Parried += OnParried;
            player.Died += OnDied;
            player.PickedUp += OnPickedUp;
            player.Dropped += OnDropped;
            _subscribed = true;
        }

        void OnDisable()
        {
            if (player == null || !_subscribed) return;

            player.Noise -= OnNoise;
            player.Landed -= OnLanded;
            player.Hurt -= OnHurt;
            player.Blocked -= OnBlocked;
            player.Parried -= OnParried;
            player.Died -= OnDied;
            player.PickedUp -= OnPickedUp;
            player.Dropped -= OnDropped;
            _subscribed = false;
        }

        /// <summary>
        /// The ones with no event of their own, read off the state as it changes.
        ///
        /// LateUpdate, after the player has ticked this frame, so a mode that lasted one tick
        /// is still the mode being read.
        /// </summary>
        void LateUpdate()
        {
            if (player == null) return;
            PlayerSimState s = player.State;

            if (s.Mode != _mode)
            {
                // Leaving the ground upwards is a jump; any other way of leaving it is a fall,
                // and a fall is announced by its landing instead.
                if (_mode == MoveMode.Grounded && s.Mode == MoveMode.Airborne &&
                    s.Velocity.Y > Fix.Zero)
                    Fire(jump, "jump");

                if (s.Mode == MoveMode.Dodging) Fire(dodge, "dodge");
                if (s.Mode == MoveMode.Climbing) Fire(ladderGrab, "ladder grab");
                if (s.Mode == MoveMode.Mantling) Fire(mantle, "mantle");

                _mode = s.Mode;
            }

            // There are no phases to watch for any more. A swing is the tip actually
            // moving fast enough to hurt somebody, which is the same threshold the damage
            // uses -- so the sound fires exactly when the blade became dangerous, rather
            // than when a state machine said so.
            bool swinging = s.SwingShare > 0;
            if (swinging && !_swinging) Fire(swing, "swing");
            _swinging = swinging;

            // The trail runs for as long as the blade is live, which is now a real
            // statement about the weapon rather than a window around an animation.
            if (swingTrail != null)
                swingTrail.emitting = swinging && s.HitStopTimer == 0;

            // A clash: two blades met. The old guard-break sound is the right one for it.
            if (s.WasParried) Fire(guardBroken, "weapons clashed");

            // Held, not fired: the point of this one is that it lasts as long as the work does.
            chestWorking.Hold(s.OpeningChest >= 0);

            if (s.OpenedChest >= 0) Fire(chestOpened, "chest opened");
        }

        void OnNoise(NoiseLevel level, Vector2 where)
        {
            // Only footfalls: the other noises in the game have effects of their own above,
            // and playing dust for a parry would be worse than playing nothing.
            if (player.State.Mode != MoveMode.Grounded) return;
            Fire(level >= NoiseLevel.Medium ? stepLoud : stepQuiet, $"step {level}");
        }

        void OnLanded(float speed)
        {
            float hard = player != null ? player.tuning.hardLandSpeed : 8f;
            Fire(speed >= hard ? landHard : landSoft, $"land at {speed:0.0} m/s");
        }

        void OnHurt(int damage) => Fire(hurt, $"hurt {damage}");
        void OnBlocked(int through) => Fire(blocked, $"blocked, {through} through");
        void OnParried() => Fire(parried, "parried");
        void OnDied() { chestWorking.Hold(false); Fire(death, "death"); }
        void OnPickedUp(ItemId item) => Fire(pickUp, $"picked up {item}");
        void OnDropped(ItemId item) => Fire(drop, $"dropped {item}");

        void Fire(ActionEffect effect, string what)
        {
            effect.Play();
            if (logEvents)
                Debug.Log($"{name}: {what}" +
                          (effect.particles == null ? "  (no effect wired)" : ""), this);
        }
    }
}

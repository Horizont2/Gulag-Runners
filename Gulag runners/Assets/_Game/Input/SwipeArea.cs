using UnityEngine;
using UnityEngine.EventSystems;

namespace GulagRunners.Game
{
    /// <summary>
    /// The attack thumb: a region of the screen that turns a flick into a swing WITH A
    /// DIRECTION.
    ///
    /// A button can only say "attack". On a phone the right thumb is free and a flick is the
    /// most natural thing it can do, and the direction of that flick is the whole of what
    /// separates this fight from two people taking turns walking into each other: the left
    /// thumb says where the body goes, the right one says where the blade goes, and they are
    /// allowed to disagree. Running left while striking right is a fighter backing off with
    /// his guard up, and he is drawn running backwards because that is what he is doing.
    ///
    /// A flick with no sideways travel is still an attack — it just aims itself, the way the
    /// button always did. Nothing here is required: a scene with only an attack button plays
    /// exactly as it did before.
    ///
    /// Reports its result for ONE poll and then forgets it, because a swipe is an event and
    /// the simulation reads a tick of held state. The swing it starts is latched by the
    /// combat motor, so one frame is all it has to survive.
    /// </summary>
    public sealed class SwipeArea : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        [Header("What counts as a swipe")]
        [Tooltip("How far the finger must travel for the flick to be read as aimed, as a " +
                 "fraction of the SHORTER screen edge.\\n\\n" +
                 "A fraction rather than pixels, because a phone and a tablet are not the " +
                 "same number of pixels and a thumb is the same thumb. Under this it is " +
                 "still an attack; it just aims where the fighter is already looking.")]
        [Range(0.01f, 0.4f)] public float minTravel = 0.06f;

        [Tooltip("How much steeper than sideways a flick may be and still count as aimed. " +
                 "1 is 45 degrees: anything flatter than a diagonal is a sideways flick.")]
        [Range(0.2f, 3f)] public float sidewaysBias = 1f;

        [Tooltip("Longest a touch may last and still be a flick, in seconds. A finger held " +
                 "down and dragged around is somebody steering, not somebody striking.")]
        [Range(0.05f, 1.5f)] public float maxSeconds = 0.5f;

        [Tooltip("Swing the moment the flick passes Min Travel, rather than waiting for the " +
                 "finger to come up.\\n\\n" +
                 "On by default, and it is most of what makes the controls feel immediate: " +
                 "waiting for release puts the player's own reaction time plus the length " +
                 "of their flick in front of every blow.")]
        public bool fireOnThreshold = true;

        [Tooltip("Read a flick right as aiming LEFT. Here because which way a thumb flick " +
                 "ought to read is a matter of taste and of which hand holds the phone, and " +
                 "it is one tick to find out rather than an argument.")]
        public bool invertHorizontal;

        [Header("Debug")]
        [Tooltip("Where the last flick started and ended, in pixels, and what it was read as.")]
        public bool logSwipes;

        Vector2 _from;
        float _started;
        bool _down;
        bool _fired;

        /// <summary>A swing is waiting to be read. Cleared by <see cref="Take"/>.</summary>
        public bool HasSwing { get; private set; }

        /// <summary>-1, 0 or +1: which way the waiting swing is aimed.</summary>
        public int Aim { get; private set; }

        /// <summary>
        /// The swing and its aim, once. Returns false when there is nothing waiting.
        /// </summary>
        public bool Take(out int aim)
        {
            aim = Aim;
            bool had = HasSwing;
            HasSwing = false;
            Aim = 0;
            return had;
        }

        public void OnPointerDown(PointerEventData e)
        {
            _from = e.position;
            _started = Time.unscaledTime;
            _down = true;
            _fired = false;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_down || _fired || !fireOnThreshold) return;
            if (Time.unscaledTime - _started > maxSeconds) return;

            if (Reading(e.position, out int aim))
            {
                Fire(aim, e.position);
                _fired = true;
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_down) return;
            _down = false;

            // Already swung on the way: the release is the end of that flick, not a second one.
            if (_fired) return;
            if (Time.unscaledTime - _started > maxSeconds) return;

            // A tap is an attack with no opinion about direction, which is the button's
            // behaviour and the one every player will try first.
            Reading(e.position, out int aim);
            Fire(aim, e.position);
        }

        void OnDisable()
        {
            _down = false;
            _fired = false;
            HasSwing = false;
            Aim = 0;
        }

        /// <summary>
        /// What this finger position says, measured from where the touch began. False means
        /// "not far enough, or too steep, to be aimed" — and 0 is then a perfectly good aim.
        /// </summary>
        bool Reading(Vector2 at, out int aim)
        {
            aim = 0;

            Vector2 travel = at - _from;
            float edge = Mathf.Min(Screen.width, Screen.height);
            if (edge <= 1f) return false;

            if (travel.magnitude < minTravel * edge) return false;

            // Sideways enough to be a sideways flick: a thumb that went mostly up or down was
            // not aiming left or right, whatever its last few pixels did.
            if (Mathf.Abs(travel.x) < Mathf.Abs(travel.y) * sidewaysBias) return false;

            aim = travel.x > 0f ? 1 : -1;
            if (invertHorizontal) aim = -aim;
            return true;
        }

        void Fire(int aim, Vector2 at)
        {
            HasSwing = true;
            Aim = aim;

            if (logSwipes)
                Debug.Log($"{name}: swipe {_from} -> {at} read as " +
                          (aim == 0 ? "an unaimed attack" : aim > 0 ? "aimed right" : "aimed left"),
                          this);
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

namespace GulagRunners.Game
{
    /// <summary>
    /// The on-screen stick from docs/02: one stick for movement, nothing else.
    /// Built on plain uGUI events so it needs no extra package and works on any backend.
    /// Put it on a UI Image; assign the knob child to <see cref="knob"/>.
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        [Tooltip("Child graphic that follows the thumb. Optional.")]
        public RectTransform knob;

        [Tooltip("Radius in pixels at which the stick reads as fully pushed.")]
        public float radius = 110f;

        [Tooltip("Fraction of the radius that must be crossed before a direction registers.")]
        [Range(0.05f, 0.9f)] public float deadZone = 0.35f;

        [Tooltip("The stick re-centres under the thumb wherever the player first touches it.")]
        public bool floating = true;

        RectTransform _rect;
        Vector2 _origin;
        Vector2 _value;
        bool _held;

        /// <summary>-1..1 on both axes, already dead-zoned.</summary>
        public Vector2 Value => _value;

        void Awake() => _rect = (RectTransform)transform;

        public void OnPointerDown(PointerEventData e)
        {
            _held = true;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rect, e.position, e.pressEventCamera, out Vector2 local);
            _origin = floating ? local : Vector2.zero;
            UpdateValue(local);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_held) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rect, e.position, e.pressEventCamera, out Vector2 local);
            UpdateValue(local);
        }

        public void OnPointerUp(PointerEventData e)
        {
            _held = false;
            _value = Vector2.zero;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }

        void UpdateValue(Vector2 local)
        {
            Vector2 delta = local - _origin;
            if (delta.magnitude > radius) delta = delta.normalized * radius;

            Vector2 raw = delta / radius;
            _value = raw.magnitude < deadZone ? Vector2.zero : raw;

            if (knob != null) knob.anchoredPosition = delta;
        }
    }
}

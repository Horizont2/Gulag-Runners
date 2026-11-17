using UnityEngine;
using UnityEngine.UI;

namespace GulagRunners.Game
{
    /// <summary>
    /// Somewhere for a bool to land.
    ///
    /// The menu already has <see cref="MenuLabel"/>, which is where a string lands: a
    /// UnityEvent needs a METHOD on a component, and Text has no settable property a
    /// persistent listener can reach. A bool had nowhere at all, so two things the shop
    /// knows could not be drawn: whether the one button can be pressed, and whether the
    /// owned-only filter is on -- that is, whether the screen is being the inventory.
    ///
    /// One component rather than two, because both are the same shape: something is on or
    /// off, and up to three pieces say so -- a control that stops taking taps, a face that
    /// changes colour, and a knob that slides. Every piece is optional; a switch with only
    /// a face is a light, and a switch with only a target is a lock.
    ///
    /// Nothing animates. The screens this serves are read at a glance by a thumb that is
    /// already moving, and a 150 ms fade on a state the player caused is 150 ms of not
    /// knowing whether the tap landed.
    /// </summary>
    public sealed class MenuSwitch : MonoBehaviour
    {
        [Tooltip("Stops taking taps while off. Optional.")]
        public Selectable target;

        [Tooltip("Takes the on or off colour. Optional.")]
        public Graphic face;

        [Tooltip("Takes the on or off colour too, for the word on the control. Optional.")]
        public Graphic text;

        [Tooltip("Slides between the two x positions below. Optional.")]
        public RectTransform knob;

        [Header("What on and off look like")]
        public Color onFace = Color.white;
        public Color offFace = Color.grey;
        public Color onText = Color.white;
        public Color offText = Color.grey;

        [Tooltip("Where the knob sits, in units from the left edge of its parent.")]
        public float knobOff;
        public float knobOn;

        [Tooltip("What the switch shows before anything sets it.")]
        public bool startOn;

        void Awake() => Set(startOn);

        /// <summary>The whole of it. A UnityEvent&lt;bool&gt; calls this.</summary>
        public void Set(bool on)
        {
            if (target != null) target.interactable = on;
            if (face != null) face.color = on ? onFace : offFace;
            if (text != null) text.color = on ? onText : offText;
            if (knob != null)
            {
                Vector2 at = knob.anchoredPosition;
                knob.anchoredPosition = new Vector2(on ? knobOn : knobOff, at.y);
            }
        }
    }
}

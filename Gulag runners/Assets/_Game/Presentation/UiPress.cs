using UnityEngine;
using UnityEngine.EventSystems;

namespace GulagRunners.Game
{
    /// <summary>
    /// A button that answers the thumb.
    ///
    /// The combat HUD has had this since it was built -- HudView sinks a ring and brightens
    /// it while it is held, and the note there is worth repeating: a control that does not
    /// move under a thumb reads as a control that did not take the press, and the player's
    /// first instinct is to press it again. Everything OUTSIDE the arena was left on
    /// Unity's own Button transition, which is a colour tint: on a dark interface with
    /// already-dark buttons that is a change of a few per cent that nobody sees on a phone
    /// in daylight.
    ///
    /// So: it shrinks. A press is a thing you do with a finger and the obvious answer to a
    /// finger is the surface giving way under it. Scale is also the one property that
    /// survives every skin -- it does not care what colour the button is, whether it has a
    /// sprite, or whether somebody swaps the art tomorrow.
    ///
    /// ON THE BUTTON ITSELF, not swept up by something on the canvas. A canvas-level hunt
    /// for whatever is under the pointer has to re-raycast the interface every frame and
    /// then guess which of the things it hit is the thing being pressed; this gets told, by
    /// the event system, exactly once, which is both cheaper and right.
    ///
    /// UNSCALED TIME. Half the buttons in this game are on a screen that is up while the
    /// outcome sequence is holding time still -- the result panel's Again and Menu -- and a
    /// button that waits out a freeze before it moves is a button that looks broken at the
    /// exact moment the player is most impatient with it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("How far it sinks, as a share of its size. 0.04 is four per cent, which " +
                 "is under what anybody can name and well over what they can feel.")]
        [Range(0f, 0.2f)] public float sink = 0.045f;

        [Tooltip("Seconds to go down. Short: this is the surface giving way, and a slow " +
                 "give is a button that feels sticky.")]
        [Range(0.01f, 0.3f)] public float down = 0.045f;

        [Tooltip("And seconds to come back, which is longer. Going down is the press and " +
                 "coming up is the release settling -- a release as fast as the press " +
                 "reads as a twitch rather than as a thing springing back.")]
        [Range(0.02f, 0.5f)] public float up = 0.12f;

        [Tooltip("Optional. What actually moves — leave empty and it is this object.\n\n" +
                 "Set it to a CHILD when the button's own rect is doing layout work: " +
                 "scaling a rect that something else is anchored to moves that too.")]
        public RectTransform target;

        bool _held;
        float _at;
        Vector3 _rest = Vector3.one;
        bool _known;

        void Awake()
        {
            if (target == null) target = transform as RectTransform;
            Remember();
        }

        void OnEnable()
        {
            // A button switched off mid-press never gets its pointer up, so it would come
            // back pressed. The panels in this menu are switched off and on constantly.
            _held = false;
            _at = 0f;
            Remember();
            Write();
        }

        /// <summary>
        /// The scale it was authored at, taken once and never read back.
        ///
        /// Read back it would compound: the rest pose would become whatever the last frame
        /// of the last press left, and a button pressed twenty times would be gone.
        /// </summary>
        void Remember()
        {
            if (_known || target == null) return;
            _rest = target.localScale;
            _known = true;
        }

        public void OnPointerDown(PointerEventData _) => _held = true;

        public void OnPointerUp(PointerEventData _) => _held = false;

        void Update()
        {
            if (target == null) return;

            float want = _held ? 1f : 0f;
            float seconds = _held ? down : up;

            if (Mathf.Approximately(_at, want)) return;

            _at = seconds <= 0f
                ? want
                : Mathf.MoveTowards(_at, want, Time.unscaledDeltaTime / seconds);
            Write();
        }

        void Write()
        {
            if (target == null) return;
            target.localScale = _rest * (1f - sink * _at);
        }
    }
}

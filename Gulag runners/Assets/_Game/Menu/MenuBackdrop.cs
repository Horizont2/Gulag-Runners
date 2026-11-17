using UnityEngine;
using UnityEngine.UI;

namespace GulagRunners.Game
{
    /// <summary>
    /// The stills behind the search screen, cross-faded.
    ///
    /// Two images and an alpha, not one image swapped: a hard cut on a screen somebody is
    /// already waiting on reads as a stutter, and the whole job of these pictures is to
    /// make the wait feel like it is going somewhere.
    ///
    /// The pictures themselves are photographs of the arena, dropped into the array in the
    /// inspector. Nothing here loads anything, generates anything, or knows their names.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuBackdrop : MonoBehaviour
    {
        [Tooltip("The two layers it fades between. Both full-bleed, stacked, same rect.")]
        public Image back;
        public Image front;

        [Tooltip("The stills, in the order they come round. One is fine; none is fine too.")]
        public Sprite[] stills = new Sprite[0];

        [Tooltip("Seconds a cross-fade takes.")]
        [Range(0.1f, 4f)] public float fadeSeconds = 1.2f;

        [Tooltip("Slowly push in while a still is up. A still image under a progress bar " +
                 "reads as a frozen game; a hair of movement reads as a game that is " +
                 "thinking. Zero switches it off.")]
        [Range(0f, 0.15f)] public float drift = 0.04f;

        int _at = -1;
        float _fade = 1f;
        float _drifted;

        void Reset()
        {
            Image[] found = GetComponentsInChildren<Image>(true);
            if (found.Length > 0) back = found[0];
            if (found.Length > 1) front = found[1];
        }

        void OnEnable()
        {
            _at = -1;
            _drifted = 0f;
            if (stills != null && stills.Length > 0) Next();
        }

        /// <summary>Bring the next still up over the one showing.</summary>
        public void Next()
        {
            if (stills == null || stills.Length == 0 || front == null) return;

            // Whatever is on the front becomes the one behind, and the new one fades in
            // over it. One array, two images, no third state to get out of step.
            if (back != null)
            {
                back.sprite = front.sprite;
                back.enabled = back.sprite != null;
                Tint(back, 1f);
            }

            _at = (_at + 1) % stills.Length;
            front.sprite = stills[_at];
            front.enabled = true;
            _fade = back != null && back.sprite != null ? 0f : 1f;
            _drifted = 0f;
            Tint(front, _fade);
        }

        void Update()
        {
            if (front == null) return;

            if (_fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + Time.deltaTime / Mathf.Max(0.1f, fadeSeconds));
                Tint(front, _fade);
                if (_fade >= 1f && back != null) back.enabled = false;
            }

            if (drift <= 0f) return;
            _drifted += Time.deltaTime;
            float push = 1f + drift * Mathf.Min(1f, _drifted / 12f);
            front.rectTransform.localScale = new Vector3(push, push, 1f);
        }

        static void Tint(Image image, float alpha)
        {
            if (image == null) return;
            Color c = image.color;
            if (!Mathf.Approximately(c.a, alpha)) image.color = new Color(c.r, c.g, c.b, alpha);
        }
    }
}

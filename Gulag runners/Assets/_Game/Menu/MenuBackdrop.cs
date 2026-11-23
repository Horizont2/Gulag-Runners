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
    /// The pictures themselves are renders of the arena, dropped into the array in the
    /// inspector. Nothing here loads anything, generates anything, or knows their names.
    ///
    /// IT COVERS THE SCREEN RATHER THAN BEING STRETCHED ONTO IT. Reported as "the pictures
    /// look stretched and pixellated", and both halves were true and measurable: the Image
    /// components had PreserveAspect off on a full-bleed rect, so a 2.44:1 render was being
    /// squeezed onto a 2.17:1 phone -- and onto a 1.78:1 editor view, which is a 37 per cent
    /// vertical stretch. Turning PreserveAspect on would fit it INSIDE the screen instead,
    /// with bars, which is the other wrong answer for a backdrop.
    ///
    /// What a backdrop wants is COVER: keep the picture's own shape and let the overflow go
    /// off the edge. So the rect is grown in whichever direction is short and the parent
    /// clips it. The pixellation is the other half and is not fixable here -- the stills are
    /// 1024 x 420 and their importer is capped at 1024, so on a 2400-pixel phone they are
    /// being blown up 2.3 times whatever this does with their shape.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuBackdrop : MonoBehaviour
    {
        [Tooltip("The two layers it fades between. Both full-bleed, stacked, same rect.")]
        public Image back;
        public Image front;

        [Tooltip("The stills, in the order they come round. One is fine; none is fine too.")]
        public Sprite[] stills = new Sprite[0];

        [Tooltip("Keep each still's own shape and let what does not fit go off the edge, " +
                 "rather than squeezing it onto the screen. Off is the old behaviour: a " +
                 "2.44:1 render stretched onto whatever shape the screen happens to be.")]
        public bool cover = true;

        [Tooltip("Seconds a cross-fade takes.")]
        [Range(0.1f, 4f)] public float fadeSeconds = 1.2f;

        [Tooltip("Slowly push in while a still is up. A still image under a progress bar " +
                 "reads as a frozen game; a hair of movement reads as a game that is " +
                 "thinking. Zero switches it off.")]
        [Range(0f, 0.15f)] public float drift = 0.04f;

        int _at = -1;
        float _fade = 1f;
        float _drifted;
        float _lastWide = -1f;

        void Reset()
        {
            Image[] found = GetComponentsInChildren<Image>(true);
            if (found.Length > 0) back = found[0];
            if (found.Length > 1) front = found[1];
        }

        void OnEnable()
        {
            // The cover deliberately makes the picture bigger than the screen, so something
            // has to cut the overflow off or it draws across the whole canvas. Added here
            // rather than asked for in the scene: it is a consequence of `cover`, not a
            // decision anybody should have to remember to make.
            if (cover && GetComponent<RectMask2D>() == null)
                gameObject.AddComponent<RectMask2D>();

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
            Cover(front);
            Cover(back);
        }

        /// <summary>
        /// Size this image so it covers its parent at the sprite's own shape.
        ///
        /// Anchors stay stretched, so the rect starts exactly as big as the parent, and
        /// sizeDelta then grows it in the ONE direction that is short. Growing both would
        /// zoom the picture; growing neither is the stretch this exists to stop.
        /// </summary>
        void Cover(Image image)
        {
            if (image == null || image.sprite == null) return;

            RectTransform rt = image.rectTransform;
            RectTransform parent = rt.parent as RectTransform;
            if (parent == null) return;

            image.preserveAspect = false;        // the rect does this job, not the Image
            if (!cover) { rt.sizeDelta = Vector2.zero; return; }

            Rect box = parent.rect;
            if (box.width < 1f || box.height < 1f) return;

            Rect art = image.sprite.rect;
            if (art.height < 1f) return;

            float want = art.width / art.height;
            float have = box.width / box.height;

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            rt.sizeDelta = want > have
                ? new Vector2(box.width * (want / have - 1f), 0f)   // wider: spill sideways
                : new Vector2(0f, box.height * (have / want - 1f));  // taller: spill up and down
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

            // The screen can change shape under it -- a rotation, a resize in the editor,
            // a notch cutout coming and going -- and a cover worked out for the old shape is
            // the stretch again.
            if (cover)
            {
                RectTransform parent = front.rectTransform.parent as RectTransform;
                if (parent != null && !Mathf.Approximately(parent.rect.width, _lastWide))
                {
                    _lastWide = parent.rect.width;
                    Cover(front);
                    Cover(back);
                }
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

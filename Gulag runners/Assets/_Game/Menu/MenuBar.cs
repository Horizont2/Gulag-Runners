using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// A bar that fills from 0 to 1, by SCALING rather than by Image.fillAmount.
    ///
    /// fillAmount needs the sprite imported as Filled, which makes a nine-sliced bar stop
    /// being nine-sliced -- the ends stretch with the middle and a 5 px corner becomes a
    /// taper. Scaling the fill rect keeps the slice intact, costs nothing, and is the same
    /// thing HudView does to the health bars for the same reason.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuBar : MonoBehaviour
    {
        [Tooltip("The rect that grows. Its pivot has to be on the end it grows FROM.")]
        public RectTransform fill;

        [Range(0f, 1f)] public float preview = 0.62f;

        void Reset() => fill = transform.childCount > 0
            ? transform.GetChild(0) as RectTransform : null;

        [Tooltip("Seconds the bar takes to catch up with a new value. 0 snaps, which is " +
                 "what this did.\n\n" +
                 "A bar that jumps is a number with a rectangle drawn round it. A bar that " +
                 "RUNS is the only thing on a loading screen that tells the player the game " +
                 "is still doing something -- and the search screen's bar is a real " +
                 "measurement, so it is worth being able to watch it move.\n\n" +
                 "Short: a tenth of a second is enough to be a movement and not enough to " +
                 "lag behind a value that is changing every frame.")]
        [Range(0f, 0.6f)] public float catchUp = 0.12f;

        float _want = -1f;

        /// <summary>Where the bar is ASKED to be. It gets there over Catch Up seconds.</summary>
        public void Fill(float share)
        {
            _want = Mathf.Clamp01(share);
            if (fill == null) return;

            // Snapped when there is no time to ease over, and on the first value ever set:
            // a bar that animates up from nothing every time a screen opens is a bar that
            // lies about the first thing it says.
            if (catchUp <= 0f || !Application.isPlaying || _shown < 0f)
            {
                _shown = _want;
                Write(_shown);
            }
        }

        float _shown = -1f;

        void Update()
        {
            if (fill == null || _want < 0f || _shown < 0f) return;
            if (Mathf.Approximately(_shown, _want)) return;

            // Unscaled, because the one bar that matters most is on the search screen and
            // the one after that is on the result screen, where the outcome sequence is
            // holding time still.
            _shown = Ease.Towards(_shown, _want, catchUp, Time.unscaledDeltaTime);
            Write(_shown);
        }

        void Write(float s)
        {
            Vector3 scale = fill.localScale;
            if (!Mathf.Approximately(scale.x, s))
                fill.localScale = new Vector3(s, scale.y, scale.z);
        }

        void OnValidate()
        {
            _shown = -1f;
            Fill(preview);
        }
    }
}

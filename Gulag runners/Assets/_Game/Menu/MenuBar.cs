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

        public void Fill(float share)
        {
            if (fill == null) return;
            float s = Mathf.Clamp01(share);
            Vector3 scale = fill.localScale;
            if (!Mathf.Approximately(scale.x, s))
                fill.localScale = new Vector3(s, scale.y, scale.z);
        }

        void OnValidate() => Fill(preview);
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace GulagRunners.Game
{
    /// <summary>
    /// The league badge: six sprites, one shown.
    ///
    /// Keyed off <see cref="GulagRunners.Meta.League"/> rather than off a string, because
    /// a string would have to be parsed back out of "Platinum II" and the parse is the bug.
    /// The order is the enum's own, so adding a league above Runner some day means adding
    /// one sprite here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuRankBadge : MonoBehaviour
    {
        [Tooltip("The image the badge is drawn into.")]
        public Image target;

        [Tooltip("Bronze, Silver, Gold, Platinum, Diamond, Runner — in that order.")]
        public Sprite[] badges = new Sprite[6];

        [Tooltip("Optional. Shown while the player is still in their first five matches " +
                 "and has no league at all. docs/09 says those are TRAINING and says so " +
                 "out loud.")]
        public Sprite unranked;

        void Reset() => target = GetComponent<Image>();

        /// <summary>-1 for no league yet; otherwise the League enum's value.</summary>
        public void Show(int league)
        {
            if (target == null) target = GetComponent<Image>();
            if (target == null) return;

            Sprite want = league >= 0 && league < badges.Length ? badges[league] : unranked;
            if (want != null && target.sprite != want) target.sprite = want;
        }
    }
}

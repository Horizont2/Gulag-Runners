using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Shows one slot of the loot pool.
    ///
    /// Bound to a pool index for the whole match, so an item never changes which object is
    /// drawing it half way through its arc. While the slot is empty every model under it is
    /// switched off and nothing is drawn.
    ///
    /// docs/03 asks that loot be readable from across the room by tier and by silhouette, and
    /// that is what the two things here do: the model says what it is, the glow says what it is
    /// worth. The bob is not decoration — it is what separates something you can take from
    /// something that is scenery.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundItemView : MonoBehaviour
    {
        [Header("Models (one per item this can show)")]
        public ItemModel[] models;

        [Header("Look")]
        [Tooltip("What bobs and turns. Leave empty to move this object.")]
        public Transform spinRoot;

        [Tooltip("Renderer tinted by tier. Leave empty to take the first one under the model.")]
        public Renderer tintTarget;

        public Color[] tierColours = ItemModels.DefaultPalette();

        [Tooltip("Emission multiplier for the tier glow, so a gold item reads at a distance.")]
        [Range(0f, 4f)] public float glow = 1.4f;

        [Header("Motion")]
        [Tooltip("How far it rides up and down, in metres. Small: it marks the item as takeable, " +
                 "it is not a firework.")]
        public float bobHeight = 0.07f;
        public float bobSpeed = 2.2f;
        public float spinSpeed = 55f;

        [Tooltip("Seconds of squash when it is taken, so a pickup is seen and not just heard.")]
        [Range(0f, 0.4f)] public float takePop = 0.18f;

        MaterialPropertyBlock _block;
        ItemId _shown = ItemId.None;
        GameObject _shownRoot;
        float _takeTimer;

        public bool Visible { get; private set; }

        void Reset() => spinRoot = transform;

        void Awake() => Hide();

        /// <summary>Puts this view on an item. Phase keeps a row of them from bobbing in time.</summary>
        public void Show(in GroundItem item, float planeZ, int phase)
        {
            if (_shown != item.Item)
            {
                _shownRoot = ItemModels.Show(models, item.Item);
                _shown = item.Item;
                _block = null;

                Renderer target = tintTarget != null
                    ? tintTarget
                    : (_shownRoot != null ? _shownRoot.GetComponentInChildren<Renderer>() : null);
                ItemModels.Tint(target, ref _block, ItemModels.Tier(tierColours, item.Item), glow);
            }

            Visible = _shownRoot != null;
            if (!Visible) return;

            float x = item.Position.X.Raw / (float)Fix.RawOne;
            float y = item.Position.Y.Raw / (float)Fix.RawOne;

            // Loot in the air does not bob: it is already moving, and a wobble on top of an arc
            // reads as a bug rather than as a flourish.
            bool resting = item.State == GroundItemState.Resting;
            float t = Time.time * bobSpeed + phase * 0.7f;
            float bob = resting ? Mathf.Sin(t) * bobHeight : 0f;

            transform.position = new Vector3(x, y + bob, planeZ);

            Transform spin = spinRoot != null ? spinRoot : transform;
            if (resting) spin.localRotation = Quaternion.Euler(0f, (Time.time * spinSpeed + phase * 40f) % 360f, 0f);

            // A locked item is one you cannot take yet. Dimming it says so without a label.
            float lit = item.PickupLock > 0 ? 0.45f : 1f;
            Renderer r = tintTarget != null
                ? tintTarget
                : (_shownRoot != null ? _shownRoot.GetComponentInChildren<Renderer>() : null);
            ItemModels.Tint(r, ref _block, ItemModels.Tier(tierColours, item.Item) * lit, glow * lit);
        }

        public void Hide()
        {
            if (_takeTimer > 0f) return;             // let the pickup pop finish first
            ItemModels.Show(models, ItemId.None);
            _shown = ItemId.None;
            _shownRoot = null;
            Visible = false;
        }

        /// <summary>Called when this slot's item was taken, so the pickup is seen.</summary>
        public void PlayTaken()
        {
            if (takePop <= 0f) { Hide(); return; }
            _takeTimer = takePop;
        }

        void LateUpdate()
        {
            if (_takeTimer <= 0f) return;

            _takeTimer -= Time.deltaTime;
            Transform spin = spinRoot != null ? spinRoot : transform;
            float k = Mathf.Clamp01(_takeTimer / Mathf.Max(0.001f, takePop));
            spin.localScale = Vector3.one * k;

            if (_takeTimer > 0f) return;

            _takeTimer = 0f;
            spin.localScale = Vector3.one;
            Hide();
        }
    }
}

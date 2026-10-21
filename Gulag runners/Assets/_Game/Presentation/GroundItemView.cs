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

        [Tooltip("Shown for an item that has no model in the list above.\n\n" +
                 "Armour and the bandage have no mesh in the weapon pack, so without this a " +
                 "third of what the chests pay out lands on the floor invisible — which reads " +
                 "as \"that chest gave me nothing\" and is the worst possible lie for a game " +
                 "whose whole scavenge phase is deciding what to spend time on. A stand-in " +
                 "cube is not art; it is the truth until there is art.")]
        public GameObject fallbackModel;


        [Header("Size")]
        [Tooltip("How long a weapon is drawn, as a share of the reach it ADDS over bare " +
                 "hands, in metres. 0 draws everything at the fallback length instead.\n\n" +
                 "Weapon packs are authored for a 1.8 m human; this fighter is 0.91 m, so a " +
                 "pack greataxe in his hand is longer than he is tall. Rather than guess one " +
                 "number for one pack, each model's own mesh is measured and its longest axis " +
                 "scaled to this. Taking the length from reach keeps the picture and the " +
                 "hitbox agreeing: docs/02 makes reach the whole difference between two " +
                 "weapons, and a spear drawn dagger-sized is that difference thrown away.")]
        [Range(0f, 3f)] public float reachToLength = 0.73f;

        [Tooltip("The grip and head every weapon has before it has any reach at all, in " +
                 "metres. Must match the fighter's own Base Of Body times its height, or a " +
                 "weapon changes size when it is picked up.")]
        [Range(0f, 0.8f)] public float baseLength = 0.246f;

        [Tooltip("Shortest a weapon may be drawn, in metres. A dagger adds almost nothing to " +
                 "bare reach, and a dagger you cannot see is not a weapon.")]
        [Range(0f, 0.6f)] public float minLength = 0.28f;

        [Tooltip("Longest a weapon may be drawn, in metres. The map from reach to length is " +
                 "linear, which over-rewards the two polearms: left uncapped a scythe comes " +
                 "out nearly twice the fighter's height.")]
        [Range(0f, 3f)] public float maxLength = 1.3f;

        [Tooltip("Longest axis, in metres, for anything with no reach of its own — armour, " +
                 "the bandage, a stand-in cube.\n\n" +
                 "A suit of armour on the floor is the thing a player decides about from " +
                 "across the room, and at 0.30 m beside a 0.91 m fighter it was a third of " +
                 "him and read as a scrap. Half his height is a garment.")]
        [Range(0f, 2f)] public float fallbackLength = 0.45f;

        [Header("Look")]
        [Tooltip("What bobs and turns. Leave empty to move this object.")]
        public Transform spinRoot;

        [Tooltip("Optional. A light the rarity colours and pulses, so a find reads from " +
                 "across the arena rather than only once you are standing over it.\n\n" +
                 "A light and not just emission, for two reasons. Emission through a " +
                 "MaterialPropertyBlock does nothing AT ALL unless the material has its " +
                 "Emission tick box on — a block can set a property but cannot enable a " +
                 "shader keyword, and the weapon pack's material ships with emission " +
                 "compiled out, so every tier colour this view has ever written went " +
                 "nowhere. And even switched on, a 30 cm object glowing faintly is not " +
                 "something anybody notices from the other end of a room. A light is.\n\n" +
                 "Tools > Gulag Runners > Light up the loot adds one and switches the " +
                 "materials' emission on.")]
        public Light rarityLight;

        [Tooltip("Brightness of that light for a COMMON find. Rare and legendary are " +
                 "multiples of it, decided by the rarity rather than typed again here.")]
        [Range(0f, 8f)] public float lightStrength = 0.9f;

        [Tooltip("How far it reaches, in metres, for a common find.")]
        [Range(0.2f, 8f)] public float lightRange = 1.6f;

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

        [Header("Standing over it")]
        [Tooltip("How much further it rides up while a fighter is stood over it with no room " +
                 "to take it, in metres.\n\n" +
                 "This is the contextual button of docs/03 with no button drawn: walking over " +
                 "something with a free slot takes it and costs nothing, so the only case " +
                 "worth saying anything about is the one where pressing the action key would " +
                 "SWAP. The item itself says it, by lifting and brightening.")]
        public float offeredRise = 0.09f;

        [Tooltip("Extra glow while it is being offered, as a multiple.")]
        [Range(1f, 4f)] public float offeredGlow = 1.8f;

        [Tooltip("How quickly it rises and settles. 0 snaps.")]
        [Range(0f, 0.4f)] public float offeredEase = 0.08f;

        MaterialPropertyBlock _block;
        ItemId _shown = ItemId.None;
        GameObject _shownRoot;
        float _takeTimer;
        bool _offered;
        float _offer;              // 0 to 1, eased
        float _offerVelocity;

        public bool Visible { get; private set; }

        /// <summary>
        /// A fighter is stood over this and the action button would swap for it. Set every
        /// frame by <see cref="LootView"/>; it decays on its own the frame nobody says so.
        /// </summary>
        public void Offer(bool offered) => _offered = offered;

        void Reset() => spinRoot = transform;

        void Awake()
        {
            Hide();
        }

        /// <summary>
        /// Sized once, and in Start for the same reason the fighter's loadout is: by then
        /// every Awake and OnEnable has run, so the parent scale the fit divides out is the
        /// one this model will actually hang under.
        ///
        /// A weapon lying on the floor has to read at the same size as one in a hand, or
        /// picking it up changes what it is — which is exactly what happened while the hand
        /// divided out the fighter's 0.5 and the floor did not.
        /// </summary>
        void Start()
        {
            ItemModels.FitAll(models, reachToLength, minLength, maxLength, fallbackLength,
                              baseLength);
            if (fallbackModel != null)
                ItemModels.Fit(fallbackModel, ItemId.None, 0f, minLength, maxLength,
                               fallbackLength);
        }

        /// <summary>Puts this view on an item. Phase keeps a row of them from bobbing in time.</summary>
        public void Show(in GroundItem item, float planeZ, int phase)
        {
            // A slot can be re-filled while the pickup pop from the LAST item is still
            // playing: the pool hands the same index straight back out. Left alone, the pop
            // finishes, calls Hide, and switches off a model that is live — and because the
            // item has not CHANGED, the branch below never runs again and that slot stays
            // invisible for the rest of the match while still being pickable.
            if (_takeTimer > 0f)
            {
                _takeTimer = 0f;
                Transform popped = spinRoot != null ? spinRoot : transform;
                popped.localScale = Vector3.one;
            }

            if (_shown != item.Item || _shownRoot == null)
            {
                _shownRoot = ItemModels.Show(models, item.Item);

                // Nothing authored for it: show the stand-in rather than nothing at all.
                if (fallbackModel != null)
                {
                    bool need = _shownRoot == null && item.Item != ItemId.None;
                    if (fallbackModel.activeSelf != need) fallbackModel.SetActive(need);
                    if (need) _shownRoot = fallbackModel;
                }

                _shown = item.Item;
                _block = null;

                // No tint here: the end of this method does it every frame, including this
                // one, and a second copy was two places that had to agree about what a
                // rarity looks like.
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

            float want = _offered && resting ? 1f : 0f;
            _offer = offeredEase <= 0.001f
                ? want
                : Mathf.SmoothDamp(_offer, want, ref _offerVelocity, offeredEase);
            _offered = false;                       // said again next frame, or it lapses

            transform.position = new Vector3(x, y + bob + offeredRise * _offer, planeZ);

            Transform spin = spinRoot != null ? spinRoot : transform;
            if (resting) spin.localRotation = Quaternion.Euler(0f, (Time.time * spinSpeed + phase * 40f) % 360f, 0f);

            // A locked item is one you cannot take yet. Dimming it says so without a label.
            float lit = item.PickupLock > 0 ? 0.45f : 1f;
            float offered = Mathf.Lerp(1f, offeredGlow, _offer);
            Renderer r = tintTarget != null
                ? tintTarget
                : (_shownRoot != null ? _shownRoot.GetComponentInChildren<Renderer>() : null);
            // Offset by the item's own id so a row of three legendaries breathes out of
            // step rather than blinking together like a string of fairy lights.
            float beat = Time.time + (int)item.Item * 0.37f;
            Color colour = ItemModels.RarityGlow(tierColours, item.Item, beat, glow,
                                                 out float rarity);
            ItemModels.Tint(r, ref _block, colour * lit, rarity * lit * offered);

            if (rarityLight != null)
            {
                // Off the same number the emission uses, so the lamp and the model cannot
                // come to disagree about what this item is worth.
                float strength = rarity / Mathf.Max(0.0001f, glow);
                rarityLight.color = colour;
                rarityLight.intensity = lightStrength * strength * lit * offered;
                rarityLight.range = lightRange * Mathf.Sqrt(Mathf.Max(0.01f, strength));
            }
        }

        public void Hide()
        {
            if (_takeTimer > 0f) return;             // the pop finishes first, then it calls back
            ItemModels.Show(models, ItemId.None);
            if (fallbackModel != null && fallbackModel.activeSelf) fallbackModel.SetActive(false);
            _shown = ItemId.None;
            _shownRoot = null;
            if (rarityLight != null) rarityLight.intensity = 0f;   // or an empty slot burns
            Visible = false;
        }

        /// <summary>Called when this slot's item was taken, so the pickup is seen.</summary>
        public void PlayTaken()
        {
            // Not visible from this moment on, whatever the pop is still doing. LootView asks
            // "was it there a frame ago" every frame, and while this said yes it re-armed the
            // pop over and over — the timer never ran out and the item never left the floor.
            Visible = false;

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

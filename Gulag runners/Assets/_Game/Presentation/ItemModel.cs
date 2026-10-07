using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// One item's model, authored in the scene and switched on when that item is the one being
    /// shown. Nothing is instantiated: every model a view can ever need already exists under it,
    /// switched off, which is also the only way a designer can see them all in the inspector.
    /// </summary>
    [Serializable]
    public class ItemModel
    {
        public ItemId item = ItemId.None;
        public GameObject root;
    }

    /// <summary>Shared helper: show one model out of a set and hide the rest.</summary>
    public static class ItemModels
    {
        public static GameObject Show(ItemModel[] models, ItemId item)
        {
            GameObject shown = null;
            if (models == null) return null;

            for (int i = 0; i < models.Length; i++)
            {
                ItemModel m = models[i];
                if (m == null || m.root == null) continue;

                bool on = m.item == item && item != ItemId.None;
                if (on && shown == null) shown = m.root;
                else on = false;                         // one model at a time, whatever the list says

                if (m.root.activeSelf != on) m.root.SetActive(on);
            }

            return shown;
        }

        /// <summary>Tier colour, clamped to whatever palette the view was given.</summary>
        public static Color Tier(Color[] palette, ItemId item)
        {
            if (palette == null || palette.Length == 0) return Color.white;
            int tier = Mathf.Clamp(ItemTable.Get(item).Tier, 0, palette.Length - 1);
            return palette[tier];
        }

        /// <summary>The project's tier palette: none, white, blue, gold (docs/03).</summary>
        public static Color[] DefaultPalette() => new[]
        {
            new Color(0.55f, 0.55f, 0.58f),
            new Color(0.90f, 0.90f, 0.92f),
            new Color(0.35f, 0.62f, 1.00f),
            new Color(1.00f, 0.78f, 0.25f)
        };

        /// <summary>
        /// Puts the tier on a renderer without instantiating a material for it.
        ///
        /// The tier is a GLOW and nothing else — which is what ItemDef.Tier has always said it
        /// was, and which this used not to do: it also wrote the colour into the albedo, so
        /// every weapon came out a flat white, blue or gold blob instead of the model somebody
        /// made. With real art in the game that is the difference between reading "a gold
        /// item" and reading "a gold-coloured nothing".
        ///
        /// Pass tintAlbedo for a greybox stand-in that has no look of its own to keep.
        /// </summary>
        public static void Tint(Renderer target, ref MaterialPropertyBlock block, Color colour,
                                float emission, bool tintAlbedo = false)
        {
            if (target == null) return;
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            if (tintAlbedo)
            {
                block.SetColor("_BaseColor", colour);
                block.SetColor("_Color", colour);
            }
            block.SetColor("_EmissionColor", colour * emission);
            target.SetPropertyBlock(block);
        }
    }
}

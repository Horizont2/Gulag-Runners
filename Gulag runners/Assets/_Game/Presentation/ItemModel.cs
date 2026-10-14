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

        /// <summary>
        /// Sizes a model to the fighter who uses it, from the model's own mesh.
        ///
        /// Weapon packs are authored for a 1.8 m human. This fighter is 0.91 m, and a pack
        /// greataxe dropped into his hand at the scale it shipped at is 2.2 m long — longer
        /// than he is tall, sticking out across the arena. The first attempt at this was a
        /// constant in the wiring tool (0.5, being 0.91/1.8), which is a guess about ONE pack
        /// and silently wrong for the next one, and which never reached the scene at all
        /// because the tool had already been run.
        ///
        /// So it is measured instead: the longest axis of the model's own mesh is scaled to
        /// the length this item should read as. The length comes from the item's REACH — the
        /// number the hitbox is built from — which means the picture and the mechanic can no
        /// longer disagree. docs/02 makes reach the whole difference between two weapons, and
        /// a spear that looks like a dagger is that difference thrown away.
        ///
        /// Not reach itself, though: reach is measured from the middle of the body, so it
        /// already contains half a torso and an arm. Bare hands have a reach too, and it is
        /// exactly that — so what a weapon is drawn as is the reach it ADDS over bare hands.
        /// Read out of the table rather than written down here, so retuning fists moves every
        /// weapon with it. Measured straight off reach instead, a sword came out as long as the
        /// fighter is tall and a scythe half again as long.
        ///
        /// Measured off sharedMesh rather than Renderer.bounds, because every one of these
        /// models is switched off when this runs and an inactive renderer has no bounds.
        /// </summary>
        public static void Fit(GameObject root, ItemId item, float reachToLength,
                               float minLength, float maxLength, float fallback)
        {
            if (root == null) return;

            float want = fallback;
            if (reachToLength > 0f)
            {
                Fix reach = ItemTable.Get(item).Reach;
                Fix arm = ItemTable.Get(ItemId.Fists).Reach;
                if (reach.Raw > arm.Raw)
                {
                    want = (reach - arm).Raw / (float)Fix.RawOne * reachToLength;
                    want = Mathf.Max(minLength, want);
                    if (maxLength > 0f) want = Mathf.Min(maxLength, want);
                }
            }
            if (want <= 0f) return;

            if (!LocalBounds(root, out Bounds local)) return;

            float longest = Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z));
            if (longest <= 0.0001f) return;

            root.transform.localScale *= want / longest;
        }

        /// <summary>
        /// The model's extent in its own root's space, from the meshes rather than from the
        /// renderers, so it can be measured while the whole thing is switched off.
        /// </summary>
        static bool LocalBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                any |= Accumulate(ref bounds, any, mf.sharedMesh.bounds,
                                  toRoot * mf.transform.localToWorldMatrix);
            }

            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                any |= Accumulate(ref bounds, any, smr.sharedMesh.bounds,
                                  toRoot * smr.transform.localToWorldMatrix);
            }

            return any;
        }

        static bool Accumulate(ref Bounds bounds, bool any, Bounds mesh, Matrix4x4 m)
        {
            Vector3 c = mesh.center, e = mesh.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any && i == 0) bounds = new Bounds(p, Vector3.zero);
                else bounds.Encapsulate(p);
            }
            return true;
        }

        /// <summary>Sizes every model in a set, once. Call it from Awake.</summary>
        public static void FitAll(ItemModel[] models, float reachToLength, float minLength,
                                  float maxLength, float fallback)
        {
            if (models == null) return;
            foreach (ItemModel m in models)
                if (m != null && m.root != null)
                    Fit(m.root, m.item, reachToLength, minLength, maxLength, fallback);
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

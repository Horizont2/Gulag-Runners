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
                               float minLength, float maxLength, float fallback,
                               float baseLength = 0f)
        {
            if (root == null) return;

            float want = fallback;
            if (reachToLength > 0f)
            {
                Fix reach = ItemTable.Get(item).Reach;
                Fix arm = ItemTable.Get(ItemId.Fists).Reach;
                if (reach.Raw > arm.Raw)
                {
                    // A HAFT plus the reach it adds, not the reach alone.
                    //
                    // Measured straight off the added reach, the family came out wrong at
                    // both ends at once. The dagger adds 0.10 m, which drew it at the
                    // minimum length and put it in a heap with the mace and the gladius --
                    // four weapons that all read as the same small stick. The warhammer
                    // adds 0.80 m, which drew it at 119% of the fighter's own height, and
                    // the two polearms ran into the cap at 143%. A real warhammer is half
                    // its owner's height and a real spear is about 120% of it.
                    //
                    // Every weapon has a grip and a head before it has any reach at all, so
                    // the straight line through the origin was the mistake. With a base,
                    // the same table draws 35% for a dagger and 127% for a scythe, in
                    // order, with nothing clamped at either end.
                    want = baseLength +
                           (reach - arm).Raw / (float)Fix.RawOne * reachToLength;
                    want = Mathf.Max(minLength, want);
                    if (maxLength > 0f) want = Mathf.Min(maxLength, want);
                }
            }
            if (want <= 0f) return;

            if (!LocalBounds(root, out Bounds local)) return;

            float longest = Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z));
            if (longest <= 0.0001f) return;

            // Out of the PARENT's scale, because the length wanted is in world metres and
            // the scale set here is local.
            //
            // This is why weapons were small specifically in the hand. The fighter object
            // is scaled to 0.5, the model hangs off a hand bone beneath it, and a local
            // scale that made the model 0.32 m of its parent's space made it 0.16 m of the
            // world -- half of every length this function had just worked out, and only
            // for the models that are held. The same weapon lying on the floor, under an
            // unscaled view, came out right, so the two disagreed by exactly the fighter's
            // own scale and picking one up halved it.
            root.transform.localScale *= want / (longest * ParentScale(root.transform));
        }

        /// <summary>
        /// The scale of everything ABOVE a transform, as one number.
        ///
        /// Taken as lossy over local rather than walked up the hierarchy, so it is right
        /// however deep the thing is parented and whatever the bones in between do. A
        /// component at or near zero is a scale nothing can be fitted against, so it
        /// contributes 1 rather than an infinity.
        /// </summary>
        static float ParentScale(Transform t)
        {
            Vector3 lossy = t.lossyScale, local = t.localScale;
            float sum = 0f;
            int counted = 0;

            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(local[i]) < 0.0001f) continue;
                float above = Mathf.Abs(lossy[i] / local[i]);
                if (above < 0.0001f) continue;
                sum += above;
                counted++;
            }

            return counted == 0 ? 1f : sum / counted;
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
                                  float maxLength, float fallback, float baseLength = 0f)
        {
            if (models == null) return;
            foreach (ItemModel m in models)
                if (m != null && m.root != null)
                    Fit(m.root, m.item, reachToLength, minLength, maxLength, fallback,
                        baseLength);
        }

        /// <summary>Tier colour, clamped to whatever palette the view was given.</summary>
        public static Color Tier(Color[] palette, ItemId item)
        {
            if (palette == null || palette.Length == 0) return Color.white;
            int tier = Mathf.Clamp(ItemTable.Get(item).Tier, 0, palette.Length - 1);
            return palette[tier];
        }

        /// <summary>
        /// How hard a rarity glows at rest.
        ///
        /// Three steps and a wide gap between them, because the whole job of this is to be
        /// answered across an arena in half a second: is that worth walking to. A common
        /// find is barely lit — it is the baseline, and if everything shouts then nothing
        /// does. A legendary is three times as bright as the common and the only thing in
        /// the room that moves.
        /// </summary>
        public static float RarityEmission(ItemId item) =>
            ItemTable.Get(item).Rarity switch
            {
                Sim.Rarity.Legendary => 2.6f,
                Sim.Rarity.Rare => 1.3f,
                _ => 0.8f
            };

        /// <summary>
        /// And how it moves, as a multiplier on the emission, 0 to 1 of a cycle.
        ///
        /// Movement is what the eye catches, so it is spent where it is worth catching. A
        /// common item is steady. A rare one breathes, slowly and shallowly — enough to be
        /// seen, not enough to nag. A legendary pulses twice as deep and half again as
        /// fast, and that is deliberately the only thing in the arena doing it.
        ///
        /// Driven by a time the caller passes in rather than by Time.time read here, so a
        /// view can offset it per item and a row of three legendaries does not blink in
        /// unison like a string of fairy lights.
        /// </summary>
        public static float RarityPulse(ItemId item, float time) =>
            ItemTable.Get(item).Rarity switch
            {
                Sim.Rarity.Legendary =>
                    1f + 0.45f * Mathf.Sin(time * 2.1f * Mathf.PI),
                Sim.Rarity.Rare =>
                    1f + 0.18f * Mathf.Sin(time * 1.4f * Mathf.PI),
                _ => 1f
            };

        /// <summary>
        /// The whole look of one item's rarity: its colour, and how brightly it is lit this
        /// frame. One function so a weapon in a hand, a weapon on the floor and a suit of
        /// armour cannot drift into three different ideas of what "legendary" looks like.
        /// </summary>
        public static Color RarityGlow(Color[] palette, ItemId item, float time,
                                       float strength, out float emission)
        {
            emission = strength * RarityEmission(item) * RarityPulse(item, time);
            return Tier(palette, item);
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

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
        /// Measured straight off reach instead, a sword came out as long as the fighter is
        /// tall and a scythe half again as long.
        ///
        /// That rule is no longer written here. It lives in <see cref="ArmMotor.BladeLength"/>,
        /// because the simulation needs the same answer and the two must not be able to
        /// disagree: the blade that is swung has to be the blade that is seen. It was written
        /// here alone for a while and they did disagree — by about 1.5x on all twelve weapons,
        /// which is most of why the fight played badly.
        ///
        /// Measured off sharedMesh rather than Renderer.bounds, because every one of these
        /// models is switched off when this runs and an inactive renderer has no bounds.
        /// </summary>
        public static void Fit(GameObject root, ItemId item, in MoveConfig move,
                               in ArmConfig arm, float fallback)
        {
            if (root == null) return;

            Fix blade = ArmMotor.BladeLength(in move, in arm, ItemTable.Get(item).Reach);
            Fit(root, blade.Raw > 0 ? blade.Raw / (float)Fix.RawOne : fallback);
        }

        /// <summary>Scales a model's longest axis to <paramref name="want"/> world metres.</summary>
        public static void Fit(GameObject root, float want)
        {
            if (root == null) return;
            if (want <= 0f) return;

            if (!LocalBounds(root, out Bounds local)) return;

            if (local.size.sqrMagnitude <= 0.0000001f) return;

            // In WORLD metres, which means through the root's own scale AND everything
            // above it. Two separate faults lived in not doing this.
            //
            // Weapons were small in the HAND because the fighter object is scaled to 0.5
            // and the model hangs off a hand bone beneath it: a local scale that made the
            // model 0.32 m of its parent's space made it 0.16 m of the world. The same
            // weapon on the floor, under an unscaled view, came out right, so picking one
            // up halved it.
            //
            // And ARMOUR on the floor shrank, because the greybox models carry their whole
            // shape in the ROOT's own scale -- Chainmail is a unit cube at (0.34, 0.26,
            // 0.20). Measured in the root's frame that cube is 1 x 1 x 1, so "longest" was
            // 1, and multiplying the local scale by want/1 threw the author's proportions
            // away and replaced them with a uniform shrink to 45% of what was drawn.
            //
            // The root's lossy scale answers both at once: it is the size the model is
            // ACTUALLY drawn at, whatever carries it.
            Vector3 world = Vector3.Scale(local.size, Abs(root.transform.lossyScale));
            float drawn = Mathf.Max(world.x, Mathf.Max(world.y, world.z));
            if (drawn <= 0.0001f) return;

            root.transform.localScale *= want / drawn;
        }

        static Vector3 Abs(Vector3 v) =>
            new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

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
        public static void FitAll(ItemModel[] models, in MoveConfig move, in ArmConfig arm,
                                  float fallback)
        {
            if (models == null) return;
            foreach (ItemModel m in models)
                if (m != null && m.root != null)
                    Fit(m.root, m.item, in move, in arm, fallback);
        }

        /// <summary>Sizes a set that has no reach of its own — armour, a stand-in cube.</summary>
        public static void FitAll(ItemModel[] models, float length)
        {
            if (models == null) return;
            foreach (ItemModel m in models)
                if (m != null && m.root != null)
                    Fit(m.root, length);
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

        /// <summary>
        /// The rarity ramp, by tier: nothing, common, rare, legendary.
        ///
        /// SATURATED on purpose, and that is the whole of why it was coming out white.
        /// These colours are multiplied by an emission of up to 2.6 and the result is
        /// tone-mapped, so every channel that was already near 1 clips to 1 and the hue
        /// goes with it. Common used to be (0.90, 0.90, 0.92) — a colour whose hue IS
        /// white, before any multiplier touches it — and rare at (0.35, 0.62, 1.00) still
        /// had two channels over 1 by the time it was lit, which is pale blue heading for
        /// white. A glow only keeps its colour if the channels it is NOT made of stay near
        /// zero, so the ramp is green, blue and violet with their other channels low.
        ///
        /// Green, blue, violet rather than white, blue, gold: three hues nobody confuses at
        /// a glance on a phone, in the order every player already knows, and none of them
        /// the colour of the arena's own cold green-grey fog.
        /// </summary>
        public static Color[] DefaultPalette() => new[]
        {
            new Color(0.40f, 0.42f, 0.45f),      // nothing in the slot: grey, barely lit
            new Color(0.25f, 0.90f, 0.35f),      // common    — green
            new Color(0.20f, 0.55f, 1.00f),      // rare      — blue
            new Color(0.70f, 0.25f, 1.00f)       // legendary — violet
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

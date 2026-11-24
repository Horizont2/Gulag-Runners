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

        [Tooltip("Which way this model points, in its own space. Leave it at zero and it is " +
                 "MEASURED off the model's own meshes, which is right for every weapon in " +
                 "the pack this game ships — see ModelShape for how, and for the two rules " +
                 "that were not.\n\n" +
                 "Fill it in for a model the measurement cannot read: one authored along a " +
                 "diagonal, or one as wide as it is long. (0, 1, 0) is a weapon held at the " +
                 "bottom and pointing up its own +Y, which is how a weapon pack normally " +
                 "authors one.")]
        public Vector3 bladeAxis;

        [Tooltip("And the normal of its flat — the direction you would look at the blade " +
                 "from. Zero measures it, and a measurement of zero means the model is " +
                 "round and its roll is nobody's business.\n\n" +
                 "This is what stops a sword being drawn edge-on to the camera: the hand is " +
                 "turned so this axis faces the player.")]
        public Vector3 flatAxis;
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
            if (blade.Raw > 0) FitBlade(root, blade.Raw / (float)Fix.RawOne);
            else Fit(root, fallback);
        }

        /// <summary>
        /// Scales a weapon so the steel PAST THE GRIP is <paramref name="want"/> world metres.
        ///
        /// Which is a different number to the one this used to fit, and the difference was
        /// visible in the one place it must not be: a weapon's whole model was scaled to the
        /// blade length, butt included, so every weapon in the game was drawn SHORTER than
        /// the segment the simulation swings -- by 14% for the sword, 26% for the axe and
        /// 33% for the spear, whose model is held a third of the way up its own haft.
        ///
        /// A tip that is a third of a blade short of where it hits is not a cosmetic
        /// complaint. It is the fight: the player reads the distance off the picture, steps
        /// to what looks like just-out-of-range, and is hit. "Hard to fight with" was the
        /// report, and it was right.
        ///
        /// So the thing being matched is the reach out of the fist, measured along the
        /// model's own blade axis, and the butt is left to hang out the back of the hand
        /// where it belongs.
        ///
        /// It scales along the MEASURED axis, never an authored bladeAxis, because the row
        /// that carries one is not in scope here. On the one weapon in the pack that has
        /// an authored axis -- the scythe, whose snath is curved -- the two lines differ by
        /// 16.7 degrees and the extents they give differ by 4.6%: three centimetres on a
        /// weapon two thirds of a metre long. Worth knowing, not worth threading the row
        /// through for; if a pack ever arrives where most weapons need an authored axis,
        /// that is the moment to.
        /// </summary>
        public static void FitBlade(GameObject root, float want)
        {
            if (root == null || want <= 0f) return;

            if (!ModelShape.Measure(root, out ModelShape.Grip grip))
            {
                // No clear blade. The longest axis is the older, blunter answer and still
                // better than leaving a pack weapon at the size it shipped at.
                Fit(root, want);
                return;
            }

            // In WORLD metres, through the root's own scale AND the hand and the fighter
            // above it -- same reason as Fit, and the same two faults if it is skipped.
            Vector3 scale = Abs(root.transform.lossyScale);
            float along = Mathf.Abs(Vector3.Dot(Vector3.Scale(grip.Blade, scale), grip.Blade));
            float drawn = grip.Reach * along;
            if (drawn <= 0.0001f) return;

            root.transform.localScale *= want / drawn;

            // After the multiply, never before: Narrow reads the scale the model is
            // ACTUALLY drawn at, and that is the one the line above just changed.
            Narrow(root, in grip, want);
        }

        /// <summary>
        /// How far past the hitbox tip the drawn steel may reach, as a multiple of it.
        ///
        /// A segment hitbox and a model with a head on it cannot agree exactly: an axe's
        /// blade corner hangs off the side of its haft, so the furthest point of the model
        /// is diagonally further from the hand than the tip of the segment it is drawn
        /// along. Some overhang is honest. A QUARTER is not: measured on the pack this game
        /// ships, the axe reaches 125% of its own hitbox and the scythe 108%, which is steel
        /// on the screen that touches nobody -- the player sees the blade land and takes no
        /// damage, which is the one kind of wrong practice does not fix.
        ///
        /// 1.05 is socket_check's own line, so the rule and the check that polices it are
        /// one number. Raise it if an axe head comes out too thin for the art; nothing about
        /// the fight changes either way, because the hitbox was never the thing moving.
        /// </summary>
        public const float SteelPastTip = 1.05f;

        /// <summary>
        /// Squeezes the head ACROSS the blade until the steel stops overhanging the hitbox.
        ///
        /// The alternative was to scale the whole model down until its furthest corner sat
        /// on the tip, and that is the trade the wrong way round: it shortens the HAFT, so
        /// the weapon is drawn shorter than the segment it swings -- which is the exact
        /// fault FitBlade above was written to kill, and a worse one, because a weapon that
        /// looks short gets you hit from a distance you thought was safe.
        ///
        /// So the length stays exact and the width gives way. Only the across axis the
        /// overhang is actually ON, never both: the axe's head hangs out in X and its
        /// thickness is Z, and squeezing both would have thinned the haft as well as
        /// narrowed the head for no reason. Of the eleven weapons in the pack this touches
        /// two -- the axe, whose head goes from 46 cm to 20 cm on a 91 cm fighter, and the
        /// scythe, 77 to 71 -- and leaves the other nine at exactly the size they were.
        ///
        /// Nothing here moves the hitbox. The segment is ArmMotor's and is built from the
        /// item's reach; this only stops the picture claiming more of it than there is.
        /// </summary>
        static void Narrow(GameObject root, in ModelShape.Grip grip, float want)
        {
            if (SteelPastTip <= 1f || want <= 0.0001f) return;
            if (grip.Corner.sqrMagnitude <= 1e-12f) return;

            // The corner as it is drawn now, split into the part along the blade and the
            // two across it. Along is what the hitbox covers and may not be touched.
            //
            // In WORLD metres, through the whole chain above the model, for the same reason
            // FitBlade measures its reach that way: the fighter is scaled to a half and the
            // model hangs off a hand bone beneath him, so a length in the model's own space
            // is not a length anybody sees.
            Vector3 drawn = Vector3.Scale(grip.Corner, Abs(root.transform.lossyScale));
            float along = Vector3.Dot(drawn, grip.Blade);
            Vector3 across = drawn - grip.Blade * along;
            if (across.sqrMagnitude <= 1e-10f) return;

            float cap = want * SteelPastTip;
            if (drawn.magnitude <= cap) return;

            // Which of the two across axes carries the overhang. Blade is one of the
            // model's own axes, so the other two are axes as well and "the dominant one"
            // is simply the larger component.
            int blade = Longest(grip.Blade);
            int a = (blade + 1) % 3, b = (blade + 2) % 3;
            int wide = Mathf.Abs(across[a]) >= Mathf.Abs(across[b]) ? a : b;
            int thin = wide == a ? b : a;

            float big = Mathf.Abs(across[wide]);
            float small = Mathf.Abs(across[thin]);
            if (big <= 1e-6f) return;

            // What the wide component has to come down to for the corner to sit on the cap,
            // with the other two left exactly as they are.
            float room = cap * cap - along * along - small * small;
            if (room <= 0f) return;        // the cap is inside the haft; nothing to squeeze

            float squeeze = Mathf.Clamp(Mathf.Sqrt(room) / big, 0.2f, 1f);
            if (squeeze >= 0.999f) return;

            Vector3 local = root.transform.localScale;
            local[wide] *= squeeze;
            root.transform.localScale = local;
        }

        /// <summary>Which component of an axis-aligned unit vector is the non-zero one.</summary>
        static int Longest(Vector3 v)
        {
            float x = Mathf.Abs(v.x), y = Mathf.Abs(v.y), z = Mathf.Abs(v.z);
            return x >= y && x >= z ? 0 : (y >= z ? 1 : 2);
        }

        /// <summary>Scales a model's longest axis to <paramref name="want"/> world metres.</summary>
        public static void Fit(GameObject root, float want)
        {
            if (root == null) return;
            if (want <= 0f) return;

            if (!ModelShape.Box(root, out Bounds local)) return;

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
        public static Color[] DefaultPalette() => UiPalette.Ramp();

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

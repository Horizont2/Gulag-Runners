using System;
using UnityEngine;
using UnityEngine.UI;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Every colour the HUD is allowed to use, in one place.
    ///
    /// The rarity three are not typed in: <see cref="PullFromSimulation"/> copies them out
    /// of <see cref="ItemModels.DefaultPalette"/>, which is the same ramp the loot on the
    /// floor and the armour on the body already use. A HUD that disagrees with the world
    /// about what "rare" looks like is worse than a HUD with no colour at all.
    /// </summary>
    [Serializable]
    public sealed class HudPalette
    {
        [Header("Fighters")]
        [Tooltip("The bar belonging to whoever is holding this phone. Blue on BOTH " +
                 "screens.\n\n" +
                 "The question a player asks the top of the screen, every match, in half " +
                 "a second, is \"which one is me\". If the answer is the seat the server " +
                 "happened to give them it has to be learned again every match, and under " +
                 "pressure it is read wrong. Keyed to the viewer instead, it is never read " +
                 "wrong, at the cost of the two players seeing mirrored colours -- which " +
                 "neither of them can tell, because neither sees the other's screen.")]
        public Color self = new Color(0.28f, 0.60f, 1f);

        [Tooltip("The other fighter's bar. Red on both screens, for the same reason.\n\n" +
                 "Deliberately a deeper red than Danger, so that an opponent dropping into " +
                 "desperation still visibly BRIGHTENS rather than staying the red it " +
                 "already was.")]
        public Color enemy = new Color(0.82f, 0.18f, 0.22f);

        [Header("Interface")]
        [Tooltip("Every line of the interface. Alpha is carried here, not per element.")]
        public Color line = new Color(1f, 1f, 1f, 0.8f);

        [Tooltip("A guard that cannot be raised, and anything else switched off.")]
        [Range(0f, 1f)] public float disabledAlpha = 0.28f;

        [Tooltip("A stamina charge already spent.")]
        [Range(0f, 1f)] public float spentAlpha = 0.28f;

        [Header("Rarity")]
        public Color common = new Color(0.25f, 0.90f, 0.35f);
        public Color rare = new Color(0.20f, 0.55f, 1.00f);
        public Color legendary = new Color(0.70f, 0.25f, 1.00f);
        public Color empty = new Color(0.40f, 0.42f, 0.45f);

        [Header("Danger")]
        [Tooltip("A weapon about to break, and health below the share where bare hands " +
                 "start hitting harder. One colour for both, because both mean the same " +
                 "thing to the player: this is about to be decided.")]
        public Color danger = new Color(1f, 0.25f, 0.2f);

        /// <summary>How brightly the glow behind a slot burns, by rarity.</summary>
        public float GlowAlpha(Rarity rarity) => rarity switch
        {
            Rarity.Legendary => 0.75f,
            Rarity.Rare => 0.55f,
            _ => 0.30f
        };

        public Color Of(Rarity rarity) => rarity switch
        {
            Rarity.Legendary => legendary,
            Rarity.Rare => rare,
            _ => common
        };

        public Color Of(ItemId item) =>
            item == ItemId.None ? empty : Of(ItemTable.Get(item).Rarity);

        /// <summary>
        /// Takes the rarity ramp from the simulation's own palette, so the two cannot
        /// drift. Run it after changing <see cref="ItemModels.DefaultPalette"/>.
        /// </summary>
        public void PullFromSimulation()
        {
            Color[] ramp = ItemModels.DefaultPalette();
            if (ramp == null || ramp.Length < 4) return;
            empty = ramp[0];
            common = ramp[1];
            rare = ramp[2];
            legendary = ramp[3];
        }
    }

    /// <summary>One fighter's health bar and stamina row.</summary>
    [Serializable]
    public sealed class HudBar
    {
        [Tooltip("The outline. Turns danger-red when the fighter drops into desperation.")]
        public Graphic frame;

        [Tooltip("The bar inside it.")]
        public Graphic fill;

        [Tooltip("Optional. Set it and the fill is scaled to the health left; leave it " +
                 "empty and only the colour is driven, which is what a static mock wants.\n\n" +
                 "Anchor it to the side health drains FROM: left for player one, right for " +
                 "player two, so the two bars empty towards the middle of the screen.")]
        public RectTransform fillRect;

        [Tooltip("Stamina charges, in order. One per charge the fighter can hold.")]
        public Graphic[] pips = Array.Empty<Graphic>();
    }

    /// <summary>One square of the equipment column, and which slot it is reading.</summary>
    [Serializable]
    public sealed class HudSlot
    {
        [Tooltip("Which inventory slot this square shows. Weapon, Helmet, Armour, Belt " +
                 "or Utility — the five of docs/02.")]
        public ItemKind slot = ItemKind.Weapon;

        [Tooltip("The square itself.")]
        public Graphic frame;

        [Tooltip("Optional soft halo behind it. Its ALPHA is the rarity, not its colour.")]
        public Graphic glow;

        [Tooltip("Optional item silhouette. Hidden while the slot is empty.")]
        public Graphic icon;

        [Tooltip("Optional. Switched on while pressing ACTION would replace THIS slot, so " +
                 "a swap is a decision rather than a surprise.")]
        public GameObject swapArrow;
    }

    /// <summary>One touch control: a ring and a glyph that dim together.</summary>
    [Serializable]
    public sealed class HudButton
    {
        public Graphic ring;
        public Graphic glyph;
    }

    /// <summary>
    /// Paints the combat HUD from the simulation, and nothing else.
    ///
    /// It creates nothing, finds nothing and lays nothing out: every square, ring and bar
    /// is already in the scene, wired into the fields below, where a designer can see what
    /// is connected to what. All this does is decide what colour each of them is this
    /// frame. docs/27 is the matching export sheet, and the reason every sprite on disk is
    /// WHITE: colour is a decision taken here, once, against one palette — not twenty-nine
    /// files that quietly disagree with the C# the day somebody changes a hue.
    ///
    /// It references exactly one uGUI type, <see cref="Graphic"/>, which is the base of
    /// Image, RawImage, Text and TMP_Text alike — so one field accepts whichever of them
    /// the interface is eventually drawn with. <see cref="MenuUI"/> avoids uGUI entirely
    /// and routes its readouts through UnityEvents; that is right for a menu, where there
    /// are six numbers. A HUD has forty coloured things and changes them every frame, and
    /// forty UnityEvents wired by hand is not a design, it is a chore with a bug in it.
    ///
    /// Nothing here allocates per frame and nothing here is read by the simulation: this is
    /// presentation, downstream of the tick, exactly like PlayerAnimator.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HudView : MonoBehaviour
    {
        [Header("Who it is reading")]
        [Tooltip("The fighter on the LEFT bar. Leave empty and the left bar is not driven.")]
        public PlayerController player1;

        [Tooltip("The fighter on the RIGHT bar.")]
        public PlayerController player2;

        [Tooltip("For the clock and the ground items a swap would take. Found through " +
                 "MatchState.Instance when empty.")]
        public MatchState matchState;

        [Tooltip("Which fighter is holding THIS phone. 0 is player one.\n\n" +
                 "Everything keyed to a point of view reads this: whose equipment column " +
                 "is drawn, which bar is blue, and whose stamina is hidden.")]
        [Range(0, 1)] public int localPlayer;

        [Tooltip("Hide the opponent's stamina charges. Their health is public -- a fight " +
                 "where neither side can see the damage is not a fight -- but what they " +
                 "have left to spend is not.\n\n" +
                 "The pips stay in the scene and are painted to nothing, so switching this " +
                 "off puts them straight back without rebuilding the HUD.")]
        public bool hideOpponentStamina = true;

        [Header("Palette")]
        public HudPalette palette = new HudPalette();

        [Header("Top bar")]
        public HudBar bar1 = new HudBar();
        public HudBar bar2 = new HudBar();

        [Tooltip("Optional. Goes danger-red in sudden death, where bare hands hit twice " +
                 "as hard and the match is about to be decided on health.")]
        public Graphic clock;

        [Header("Equipment column")]
        [Tooltip("One entry per square. Order on screen is yours; which slot each reads " +
                 "is the Slot field, not the position in this list.")]
        public HudSlot[] slots = Array.Empty<HudSlot>();

        [Tooltip("Hits left in a weapon at which the slot starts pulsing danger-red. The " +
                 "same number PlayerLoadoutView uses for the weapon in the hand, so the " +
                 "square and the stick agree about when it is nearly spent.")]
        public int wornAt = 3;

        [Tooltip("Pulses per second while a weapon is nearly spent.")]
        [Range(0.5f, 8f)] public float wornPulse = 3f;

        [Header("Touch controls")]
        public HudButton attack = new HudButton();

        [Tooltip("Dimmed for the 0.7 s after the guard breaks — the one moment the player " +
                 "has to be told they cannot defend.")]
        public HudButton block = new HudButton();
        public HudButton action = new HudButton();
        public HudButton jump = new HudButton();

        [Header("Everything else on one colour")]
        [Tooltip("Joystick, sound arc, mannequin, leader lines — anything that is simply " +
                 "a white line and never changes meaning. Drag them all in here.")]
        public Graphic[] lineArt = Array.Empty<Graphic>();

        [Tooltip("How faint the joystick and the other passive lines sit, against the " +
                 "palette's own line alpha.")]
        [Range(0f, 1f)] public float lineArtAlpha = 0.75f;

        [Header("Editing")]
        [Tooltip("Keep the HUD painted while you lay it out, not only in play mode.\n\n" +
                 "This is a live PREVIEW and nothing more: it writes colours without " +
                 "marking the scene dirty, so they are not saved. The button in the " +
                 "inspector is what commits them, with an undo step. Switch this off if " +
                 "you ever want to set a colour by hand and have it stay.")]
        public bool paintInEditMode = true;

        void Reset() => matchState = MatchState.Instance;

        void LateUpdate()
        {
            if (!Application.isPlaying && !paintInEditMode) return;
            if (matchState == null) matchState = MatchState.Instance;

            PaintBar(bar1, player1, localPlayer == 0);
            PaintBar(bar2, player2, localPlayer == 1);
            PaintClock();
            PaintColumn();
            PaintControls();

            foreach (Graphic g in lineArt) Tint(g, Fade(palette.line, lineArtAlpha));
        }

        // ---------------------------------------------------------------- the top bar

        void PaintBar(HudBar bar, PlayerController player, bool mine)
        {
            if (bar == null) return;

            Color accent = mine ? palette.self : palette.enemy;

            int max = MaxHealth;
            int health = max;
            int charges = int.MaxValue;

            if (player != null && Application.isPlaying)
            {
                PlayerSimState s = player.State;
                health = Mathf.Clamp(s.Health, 0, max);
                charges = s.StaminaCharges;
            }

            // Desperation is a MECHANIC, not a mood: below this share docs/02 makes bare
            // hands hit 40% harder. A player who is not shown it finds out by losing.
            bool critical = health * 1000 <= max * DesperationPermille;

            // Pulsed rather than simply recoloured, because the enemy bar is ALREADY red
            // and a red bar turning a different red says nothing. Movement does: whichever
            // bar it is, the one that starts breathing is the one near the threshold.
            Color barColour = accent;
            if (critical)
            {
                float beat = 0.5f + 0.5f * Mathf.Sin(Time.time * wornPulse * Mathf.PI * 2f);
                barColour = Color.Lerp(accent, palette.danger, Mathf.Lerp(0.45f, 1f, beat));
            }

            Tint(bar.frame, barColour);
            Tint(bar.fill, barColour);

            if (bar.fillRect != null && max > 0)
            {
                float share = Mathf.Clamp01(health / (float)max);
                Vector3 scale = bar.fillRect.localScale;
                if (!Mathf.Approximately(scale.x, share))
                    bar.fillRect.localScale = new Vector3(share, scale.y, scale.z);
            }

            // What the opponent has left to spend is THEIR information. Shown, it turns
            // every exchange into arithmetic: wait for the fourth pip to go and walk in.
            // Hidden, the same read has to be made off their body and their mistakes,
            // which is the fight docs/02 describes.
            float visible = !mine && hideOpponentStamina ? 0f : 1f;
            for (int i = 0; i < bar.pips.Length; i++)
                Tint(bar.pips[i], Fade(i < charges ? accent : Fade(accent, palette.spentAlpha),
                                       visible));
        }

        void PaintClock()
        {
            if (clock == null) return;
            bool sudden = matchState != null && Application.isPlaying &&
                          matchState.Match.Phase == MatchPhase.SuddenDeath;
            Tint(clock, sudden ? palette.danger : palette.line);
        }

        // ---------------------------------------------------------------- the column

        void PaintColumn()
        {
            PlayerController owner = localPlayer == 0 ? player1 : player2;
            bool live = owner != null && Application.isPlaying;
            Inventory inv = live ? owner.State.Inventory : default;
            ItemKind offered = live ? OfferedKind(owner) : ItemKind.None;

            foreach (HudSlot slot in slots)
            {
                if (slot == null) continue;

                ItemId item = live ? inv.SlotContents(slot.slot) : ItemId.None;
                Rarity rarity = item == ItemId.None
                    ? Rarity.Common : ItemTable.Get(item).Rarity;

                Color colour = item == ItemId.None ? palette.empty : palette.Of(rarity);
                float glow = item == ItemId.None ? 0f : palette.GlowAlpha(rarity);

                // A weapon on its last few swings outranks what it is worth. Lerped on the
                // same sine PlayerLoadoutView pulses the stick with, so the square in the
                // corner and the thing in his hand breathe together.
                if (live && slot.slot == ItemKind.Weapon && item != ItemId.None &&
                    inv.WeaponDurability > 0 && inv.WeaponDurability <= Mathf.Max(1, wornAt))
                {
                    float beat = 0.5f + 0.5f * Mathf.Sin(Time.time * wornPulse * Mathf.PI * 2f);
                    colour = Color.Lerp(colour, palette.danger, Mathf.Lerp(0.5f, 1f, beat));
                    glow = Mathf.Max(glow, 0.55f);
                }

                Tint(slot.frame, colour);
                Tint(slot.glow, Fade(colour, glow));
                Tint(slot.icon, item == ItemId.None ? Fade(palette.line, 0f) : palette.line);

                bool wouldSwap = live && offered != ItemKind.None && offered == slot.slot;
                if (slot.swapArrow != null && slot.swapArrow.activeSelf != wouldSwap)
                    slot.swapArrow.SetActive(wouldSwap);
            }
        }

        /// <summary>
        /// Which slot pressing ACTION would replace, or None.
        ///
        /// StandingOn is an index into the ground pool rather than a slot, because the
        /// simulation has no opinion about the interface. Resolving it here keeps that
        /// true and still lets the square say so.
        /// </summary>
        ItemKind OfferedKind(PlayerController owner)
        {
            if (matchState == null) return ItemKind.None;
            int index = owner.State.StandingOn;
            GroundItem[] pool = matchState.GroundItems;
            if (pool == null || index < 0 || index >= pool.Length) return ItemKind.None;

            GroundItem item = pool[index];
            if (!item.Live || item.Item == ItemId.None) return ItemKind.None;
            return ItemTable.KindOf(item.Item);
        }

        // ---------------------------------------------------------------- the controls

        void PaintControls()
        {
            bool guardBroken = false;
            PlayerController owner = localPlayer == 0 ? player1 : player2;
            if (owner != null && Application.isPlaying)
                guardBroken = owner.State.GuardBreakTimer > 0;

            PaintButton(attack, false);
            PaintButton(block, guardBroken);
            PaintButton(action, false);
            PaintButton(jump, false);
        }

        void PaintButton(HudButton button, bool disabled)
        {
            if (button == null) return;
            Color colour = disabled ? Fade(palette.line, palette.disabledAlpha) : palette.line;
            Tint(button.ring, colour);
            Tint(button.glyph, colour);
        }

        // ---------------------------------------------------------------- the reset button

        /// <summary>
        /// Paints every wired element the colour it has at the first frame of a match:
        /// both fighters whole, every charge full, every slot empty, every button live.
        ///
        /// This is the setup tool. Wiring forty graphics and then picking a colour for
        /// each one by hand is forty chances to use the wrong blue, and the wrong blue is
        /// invisible until someone compares two screenshots. Drag them in, press this, and
        /// the mock is correct by construction.
        ///
        /// Safe to run in play mode too — the next LateUpdate simply paints over it.
        /// </summary>
        [ContextMenu("Paint everything to the starting colours")]
        public void PaintStartingColours()
        {
            PaintBarAtStart(bar1, localPlayer == 0);
            PaintBarAtStart(bar2, localPlayer == 1);
            Tint(clock, palette.line);

            foreach (HudSlot slot in slots)
            {
                if (slot == null) continue;
                Tint(slot.frame, palette.empty);
                Tint(slot.glow, Fade(palette.empty, 0f));
                Tint(slot.icon, Fade(palette.line, 0f));
                if (slot.swapArrow != null && slot.swapArrow.activeSelf)
                    slot.swapArrow.SetActive(false);
            }

            PaintButton(attack, false);
            PaintButton(block, false);
            PaintButton(action, false);
            PaintButton(jump, false);

            foreach (Graphic g in lineArt) Tint(g, Fade(palette.line, lineArtAlpha));
        }

        void PaintBarAtStart(HudBar bar, bool mine)
        {
            if (bar == null) return;
            Color accent = mine ? palette.self : palette.enemy;
            Tint(bar.frame, accent);
            Tint(bar.fill, accent);
            if (bar.fillRect != null)
            {
                Vector3 s = bar.fillRect.localScale;
                bar.fillRect.localScale = new Vector3(1f, s.y, s.z);
            }
            float visible = !mine && hideOpponentStamina ? 0f : 1f;
            foreach (Graphic pip in bar.pips) Tint(pip, Fade(accent, visible));
        }

        /// <summary>Copies the rarity ramp out of the simulation's own palette.</summary>
        [ContextMenu("Pull the rarity ramp from the simulation")]
        public void PullRarityRamp()
        {
            palette.PullFromSimulation();
            PaintStartingColours();
        }

        // ---------------------------------------------------------------- plumbing

        /// <summary>
        /// Assigns a colour, and only when it actually changed.
        ///
        /// Writing Graphic.color marks the canvas dirty whether or not the value moved, and
        /// a HUD that rebuilds its canvas sixty times a second for no reason is a frame
        /// budget spent on nothing. Forty graphics, most of them constant: the compare is
        /// free and the rebuild is not.
        /// </summary>
        static void Tint(Graphic target, Color colour)
        {
            if (target == null || target.color == colour) return;
            target.color = colour;
        }

        static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, c.a * alpha);

        int MaxHealth => matchState != null ? Mathf.Max(1, matchState.combat.maxHealth) : 100;

        int DesperationPermille => matchState != null
            ? Mathf.RoundToInt(Mathf.Clamp01(matchState.combat.desperationHealth) * 1000f)
            : 300;
    }
}

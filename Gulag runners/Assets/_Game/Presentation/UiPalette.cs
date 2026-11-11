using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Every colour the interface is allowed to be, in one place.
    ///
    /// WHY IT EXISTS. The exported art is deliberately white -- frames, fills, glyphs, plates,
    /// all of it -- so that the game decides the colour and not the export. That only works
    /// if there IS a decision: before this, the colours lived as literals in four builders,
    /// in a serialised palette on every HudView, and in a rarity ramp in ItemModel, and
    /// "the amber we use" was a number somebody typed again each time (0.91, 0.64, 0.24 in
    /// the menu, 1.00, 0.82, 0.42 for the action glyph, two different ambers nobody chose).
    ///
    /// THE RULE IT KEEPS, from docs/26: the interface is ONE colour -- a cool near-white --
    /// plus one accent for value, and nothing else is coloured. Everything here that is not
    /// Ink or Amber is not interface: it is a fighter, a rarity, or a verb under a thumb,
    /// and each of those earns its colour by being read in half a second under pressure.
    ///
    /// Two of these are changes rather than copies, and both are about collision:
    ///
    ///   COMMON WAS GREEN. The arena's one green is the rising poison, and it owns that hue
    ///   by right: it is the thing that kills you. A common item glowing the same green two
    ///   metres from a flood of it is "ordinary" and "death" in one frame, so a common is
    ///   plain steel now, which is also what a common item is.
    ///
    ///   RARE WAS THE SELF BLUE. 0.20, 0.55, 1.00 against 0.28, 0.60, 1.00: within a few
    ///   percent of the colour that means "this bar is mine". The ramp keeps its shape as
    ///   steel to cyan to violet, and blue goes back to meaning the player.
    /// </summary>
    public static class UiPalette
    {
        // ---------------------------------------------------------------- interface

        /// <summary>Every line, outline and glyph of the interface. One colour, by rule.</summary>
        public static readonly Color Ink = new Color(0.91f, 0.93f, 0.96f, 0.85f);

        /// <summary>The same ink for what is secondary: a label beside a number.</summary>
        public static readonly Color InkSoft = new Color(0.91f, 0.93f, 0.96f, 0.45f);

        /// <summary>Ink on a filled shape, which is the only place dark text happens.</summary>
        public static readonly Color InkOnAmber = new Color(0.10f, 0.08f, 0.05f, 1f);

        /// <summary>
        /// The wash that puts the game behind a panel. Nearly black and nearly opaque: the
        /// arena is still there, which is the point of a wash rather than a curtain.
        /// </summary>
        public static readonly Color Scrim = new Color(0.05f, 0.055f, 0.07f, 0.80f);

        /// <summary>The pill behind a number -- the top bar's plates, a slot's backing.</summary>
        public static readonly Color Plate = new Color(0.09f, 0.10f, 0.12f, 0.72f);

        // ---------------------------------------------------------------- value

        /// <summary>
        /// The one accent: gold, prices, the single filled button on a screen.
        ///
        /// One amber, not the three that were in the project. Warm enough to read as metal
        /// against cold stone, dark enough that white text on it is still white.
        /// </summary>
        public static readonly Color Amber = new Color(0.95f, 0.68f, 0.26f, 1f);

        // ---------------------------------------------------------------- the two fighters

        /// <summary>The bar belonging to whoever holds this phone. Blue on BOTH screens.</summary>
        public static readonly Color Self = new Color(0.28f, 0.60f, 1.00f, 1f);

        /// <summary>The other fighter's bar. Red on both screens, for the same reason.</summary>
        public static readonly Color Enemy = new Color(0.82f, 0.18f, 0.22f, 1f);

        /// <summary>A weapon about to break, and health low enough that fists hit harder.</summary>
        public static readonly Color Danger = new Color(1.00f, 0.27f, 0.22f, 1f);

        // ---------------------------------------------------------------- what a find is worth

        /// <summary>A slot with nothing in it. Grey, barely lit.</summary>
        public static readonly Color Empty = new Color(0.40f, 0.42f, 0.45f, 1f);

        /// <summary>Common: plain steel. See the note above about the green.</summary>
        public static readonly Color Common = new Color(0.74f, 0.78f, 0.80f, 1f);

        /// <summary>Rare: cold cyan, which is not the blue that means "me".</summary>
        public static readonly Color Rare = new Color(0.30f, 0.78f, 0.98f, 1f);

        /// <summary>Legendary: violet, the one hue nothing else in the game uses.</summary>
        public static readonly Color Legendary = new Color(0.72f, 0.30f, 1.00f, 1f);

        // ---------------------------------------------------------------- the verbs

        /// <summary>
        /// The glyph inside each control ring. The rings stay Ink -- they are interface --
        /// and the glyph is not: it is the verb, and a verb under a thumb is found by
        /// colour before it is found by shape.
        ///
        /// None of the four is the poison's green or the amber of money: a button is not a
        /// price, and nothing on a control should flicker in the corner of the eye as the
        /// flood comes up.
        /// </summary>
        public static readonly Color Attack = new Color(0.95f, 0.35f, 0.26f, 1f);

        public static readonly Color Block = new Color(0.40f, 0.68f, 1.00f, 1f);
        public static readonly Color Action = new Color(0.93f, 0.84f, 0.62f, 1f);
        public static readonly Color Jump = new Color(0.55f, 0.86f, 0.96f, 1f);

        // ---------------------------------------------------------------- reserved

        /// <summary>
        /// The rising poison's green. RESERVED: nothing in the interface may wear it.
        ///
        /// It is here to be avoided rather than used. The flood is the one thing in the
        /// game that is green, and it has to stay the one thing -- a green pip or a green
        /// glyph in the corner of the screen is a player checking the water level and
        /// finding a button.
        /// </summary>
        public static readonly Color Poison = new Color(0.33f, 0.72f, 0.26f, 1f);

        /// <summary>The rarity ramp, in the order the serialised arrays store it.</summary>
        public static Color[] Ramp() => new[] { Empty, Common, Rare, Legendary };

        /// <summary>The same colour at another alpha, for one-off fades.</summary>
        public static Color Fade(Color colour, float alpha)
        {
            colour.a *= alpha;
            return colour;
        }
    }
}

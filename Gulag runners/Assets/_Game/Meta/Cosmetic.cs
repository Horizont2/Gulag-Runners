using System;
using System.Collections.Generic;
using UnityEngine;

namespace GulagRunners.Meta
{
    /// <summary>
    /// The six things gold may buy, and nothing else is allowed in this enum.
    ///
    /// docs/09 names them: runner skins, weapon skins, emotes, graffiti on the arena
    /// walls, knockout effects and profile frames. The list is short on purpose and the
    /// iron rule is above it -- "gold never buys power" -- so this type is the fence:
    /// a thing that cannot be named here cannot be priced, and a thing that cannot be
    /// priced cannot be sold.
    ///
    /// One slot each, in this order, and the order is saved: PlayerProgress.equipped is
    /// one id per slot, indexed by this enum. Adding a slot therefore goes on the END.
    /// </summary>
    public enum Slot : byte
    {
        /// <summary>The fighter's own model. MenuFighter already holds them all, switched off.</summary>
        Runner = 0,

        /// <summary>
        /// How every weapon is finished -- rust, bone, gilt. ONE finish across the whole
        /// rack rather than a skin per weapon: the weapon in your hand is whatever the
        /// arena gave you, so a skin for one of twelve is a thing you would see about
        /// twice an evening. The weapon it IS does not change.
        /// </summary>
        Weapon = 1,

        /// <summary>What happens on screen when the other man goes down.</summary>
        Knockout = 2,

        /// <summary>The plate the player's name sits on, in the menu and on both HUDs.</summary>
        Plate = 3,

        /// <summary>
        /// The face of a lock (docs/04). You look at one every time you pick a chest
        /// instead of forcing it, several times a match, and nobody else ever sees it.
        /// </summary>
        Lock = 4,

        /// <summary>How the fighter stands in the menu, and how he wins on the result screen.</summary>
        Pose = 5,
    }

    /// <summary>
    /// The pieces a lock is built from, and the only part of the wardrobe that works this
    /// way.
    ///
    /// The lock is the player's own thing. It is the one object in the game that nobody
    /// else ever sees -- the fighter is seen at the fight, the name plate on both HUDs, the
    /// knockout by whoever just lost -- and it is in front of the player several times a
    /// match, for three seconds at a time, by choice. That makes it the one place where
    /// buying a PIECE rather than a whole look is worth the extra machinery: a lock gets
    /// better slowly, a corner at a time, and it ends up looking like nobody else's.
    ///
    /// It is also the cheapest thing in the game to make. Every piece here is a UI sprite.
    ///
    /// THE RULE THAT KEEPS IT HONEST: not one of these may change how READABLE the puzzle
    /// is. Everything that carries information -- the rune, the arrow, the gear, the tick
    /// on a touch already made, the red of a lock that is sulking, and how many pips are
    /// lit -- is NOT for sale and is drawn by <see cref="PuzzleView"/> over whatever the
    /// player has bought. A lock that is easier to read is a lock that opens faster, and
    /// gold does not buy that (docs/09).
    /// </summary>
    public enum LockPart : byte
    {
        /// <summary>The body it is all built on. Rusted iron, temple stone, bone, brass.</summary>
        Plate = 0,

        /// <summary>The rim around that body.</summary>
        Bezel = 1,

        /// <summary>The FACE of the touch targets. Not what is drawn on them.</summary>
        Keys = 2,

        /// <summary>Corner ornament. The one piece that carries nothing at all.</summary>
        Bolts = 3,

        /// <summary>The light behind the lock while it is being worked on.</summary>
        Glow = 4,
    }

    /// <summary>
    /// How a cosmetic is priced, and the only three prices in the game.
    ///
    /// docs/09 fixes the ends of the range: a T1 skin is 800 gold and a T3 is 3000, with
    /// an active player earning 250-350 a day -- "three to ten days without paying". The
    /// middle is the geometric step between them rounded to a hundred, so the ladder of
    /// prices is a ladder rather than three numbers somebody liked.
    ///
    /// The tier is also what the shop colours the item with, and it reuses the rarity ramp
    /// the arena already uses for loot (UiPalette.Ramp): steel, cyan, violet. One ramp in
    /// the game means a player who has learnt what violet means in a chest has learnt what
    /// it means in the shop.
    /// </summary>
    public enum Tier : byte
    {
        Common = 0,
        Rare = 1,
        Legendary = 2,
    }

    /// <summary>One thing in the shop.</summary>
    [Serializable]
    public class Cosmetic
    {
        [Tooltip("The key, in the catalogue, in the save file and in the shop. Lower case, " +
                 "no spaces, no '|' -- the save joins ids with it. It only has to agree " +
                 "with itself, so renaming one is renaming it in a player's save too: " +
                 "treat an id as permanent once it has shipped.")]
        public string id = "";

        [Tooltip("What the player reads. This one may change freely; nothing is keyed on it.")]
        public string shown = "";

        [Tooltip("Which of the six docs/09 allows. The slot decides where it is worn and " +
                 "what replaces what: one runner skin at a time, one frame at a time.")]
        public Slot slot = Slot.Runner;

        [Tooltip("Common, Rare or Legendary -- the price and the colour both come from it.")]
        public Tier tier = Tier.Common;

        [Tooltip("What it costs. Leave it at zero to take the tier's price, which is what " +
                 "nearly everything should do: a catalogue where every line carries its " +
                 "own number is a catalogue nobody can keep in step with docs/09.")]
        public int price;

        [Tooltip("Free and already owned by everybody. Exactly one of these per slot is " +
                 "the default look, and it is what the shop shows as equipped before the " +
                 "player has bought anything.")]
        public bool isDefault;

        [Tooltip("Optional. The key the APPLIER uses, when it is not the id: a runner skin " +
                 "names a MenuSkin on the fighter, a plate names a sprite row. Empty means " +
                 "the id is the key.")]
        public string applies = "";

        [Tooltip("Lock slot only: WHICH PIECE of the lock this is. The lock is the one " +
                 "thing in the game that belongs to the player and to nobody else, so it " +
                 "is improved a piece at a time rather than replaced whole.")]
        public LockPart part = LockPart.Plate;

        [Tooltip("Optional. The colour this is, for anything that is a COLOURWAY rather " +
                 "than a model.\n\n" +
                 "Most of what this shop sells is already named as a colour -- ASH, " +
                 "COPPER, NIGHTJAR, BONE, RIVER STONE, GOLD LEAF -- and a colourway needs " +
                 "no new mesh, no new texture and no new prefab in the scene. Which " +
                 "matters more than it sounds: the menu carries exactly ONE runner model, " +
                 "so without this, three of the four runners on sale could be bought and " +
                 "then not seen, and a shop that sells what it cannot show is a shop that " +
                 "is lying.\n\n" +
                 "Alpha ZERO means no tint: the thing keeps the colours it was made with, " +
                 "which is what every default should be.")]
        public Color tint = new Color(1f, 1f, 1f, 0f);

        /// <summary>Whether this is a colourway at all. Alpha zero is "leave it alone".</summary>
        public bool Tints => tint.a > 0.001f;

        /// <summary>What it costs, with the tier's price standing in for a zero.</summary>
        public int Cost => price > 0 ? price : Prices.Of(tier);

        /// <summary>The applier's key: its own, or the id.</summary>
        public string Key => string.IsNullOrEmpty(applies) ? id : applies;
    }

    /// <summary>
    /// docs/09's prices, in one place, so the catalogue cannot drift from the economy.
    ///
    /// The day's earnings are here too, because the only thing that makes 3000 gold a
    /// price rather than a number is what a day pays -- and the test that matters to a
    /// designer is "how many days is this", which is arithmetic nobody should do twice.
    /// </summary>
    public static class Prices
    {
        public const int Common = 800;
        public const int Rare = 1600;
        public const int Legendary = 3000;

        /// <summary>docs/09: "an active player earns 250-350 gold a day".</summary>
        public const int ADayLow = 250;
        public const int ADayHigh = 350;

        public static int Of(Tier tier) => tier switch
        {
            Tier.Rare => Rare,
            Tier.Legendary => Legendary,
            _ => Common,
        };

        /// <summary>Days of play to afford it, at the slow end and the fast end.</summary>
        public static (int slow, int quick) Days(int price)
        {
            if (price <= 0) return (0, 0);
            return ((price + ADayLow - 1) / ADayLow, (price + ADayHigh - 1) / ADayHigh);
        }
    }

    /// <summary>
    /// Everything that can be bought, as one asset.
    ///
    /// An asset and not a folder of files: the shop has to show a CATEGORY in an order
    /// somebody chose, and a folder has no order. It is also the one list a check can read
    /// to say "every id is unique, every slot has exactly one default, no price disagrees
    /// with docs/09" -- shop_check does exactly that, and none of it is possible when the
    /// catalogue is a search over the project.
    ///
    /// Make one with Assets > Create > Gulag Runners > Cosmetic catalogue.
    /// </summary>
    [CreateAssetMenu(menuName = "Gulag Runners/Cosmetic catalogue", fileName = "Cosmetics")]
    public sealed class CosmeticCatalogue : ScriptableObject
    {
        [Tooltip("Everything on sale, in the order the shop lists it within each slot.")]
        public Cosmetic[] items = Array.Empty<Cosmetic>();

        /// <summary>The character an id may not contain, because the save joins on it.</summary>
        public const char Separator = '|';

        public Cosmetic Find(string id)
        {
            if (string.IsNullOrEmpty(id) || items == null) return null;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].id == id) return items[i];
            return null;
        }

        /// <summary>Everything in one slot, in catalogue order.</summary>
        public List<Cosmetic> InSlot(Slot slot)
        {
            List<Cosmetic> found = new List<Cosmetic>();
            if (items == null) return found;
            foreach (Cosmetic c in items)
                if (c != null && c.slot == slot) found.Add(c);
            return found;
        }

        /// <summary>The free one in that slot: what is worn before anything is bought.</summary>
        public Cosmetic DefaultIn(Slot slot)
        {
            if (items == null) return null;
            foreach (Cosmetic c in items)
                if (c != null && c.slot == slot && c.isDefault) return c;
            return null;
        }

        /// <summary>How many slots there are, once, so nothing counts the enum by hand.</summary>
        public static readonly Slot[] Slots =
        {
            Slot.Runner, Slot.Weapon, Slot.Knockout, Slot.Plate, Slot.Lock, Slot.Pose,
        };

        /// <summary>The pieces of a lock, in the order the save indexes them by.</summary>
        public static readonly LockPart[] Parts =
        {
            LockPart.Plate, LockPart.Bezel, LockPart.Keys, LockPart.Bolts, LockPart.Glow,
        };

        /// <summary>What to call a piece of the lock, on its row.</summary>
        public static string Name(LockPart part) => part switch
        {
            LockPart.Plate => "BODY",
            LockPart.Bezel => "RIM",
            LockPart.Keys => "KEYS",
            LockPart.Bolts => "BOLTS",
            _ => "GLOW",
        };

        /// <summary>Everything in the Lock slot that is this piece, in catalogue order.</summary>
        public List<Cosmetic> InPart(LockPart part)
        {
            List<Cosmetic> found = new List<Cosmetic>();
            if (items == null) return found;
            foreach (Cosmetic c in items)
                if (c != null && c.slot == Slot.Lock && c.part == part) found.Add(c);
            return found;
        }

        /// <summary>The free piece everybody starts with, for one part of the lock.</summary>
        public Cosmetic DefaultIn(LockPart part)
        {
            if (items == null) return null;
            foreach (Cosmetic c in items)
                if (c != null && c.slot == Slot.Lock && c.part == part && c.isDefault)
                    return c;
            return null;
        }

        /// <summary>What to call a slot on a tab.</summary>
        public static string Name(Slot slot) => slot switch
        {
            Slot.Runner => "RUNNERS",
            Slot.Weapon => "WEAPONS",
            Slot.Knockout => "KNOCKOUTS",
            Slot.Plate => "PLATES",
            Slot.Lock => "LOCKS",
            _ => "POSES",
        };
    }
}

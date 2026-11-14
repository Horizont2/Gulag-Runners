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

        /// <summary>What a weapon looks like in the hand. The weapon it IS does not change.</summary>
        Weapon = 1,

        /// <summary>A gesture, fired between rounds and at the result screen.</summary>
        Emote = 2,

        /// <summary>A mark left on the arena's wall. Seen by both players.</summary>
        Graffiti = 3,

        /// <summary>What happens on screen when the other man goes down.</summary>
        Knockout = 4,

        /// <summary>The border around the player's name and league in the menu.</summary>
        Frame = 5,
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
                 "names a MenuSkin on the fighter, a frame names a sprite row. Empty means " +
                 "the id is the key.")]
        public string applies = "";

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
            Slot.Runner, Slot.Weapon, Slot.Emote, Slot.Graffiti, Slot.Knockout, Slot.Frame,
        };

        /// <summary>What to call a slot on a tab.</summary>
        public static string Name(Slot slot) => slot switch
        {
            Slot.Runner => "RUNNERS",
            Slot.Weapon => "WEAPONS",
            Slot.Emote => "EMOTES",
            Slot.Graffiti => "GRAFFITI",
            Slot.Knockout => "KNOCKOUTS",
            _ => "FRAMES",
        };
    }
}

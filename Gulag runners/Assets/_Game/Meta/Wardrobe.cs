using System.Collections.Generic;

namespace GulagRunners.Meta
{
    /// <summary>What a buy or an equip did, so the screen can say it in one line.</summary>
    public enum Deal
    {
        Bought,
        Equipped,
        AlreadyOwned,
        NotEnoughGold,
        NoSuchItem,
        NotOwned,
    }

    /// <summary>
    /// Owning, buying and wearing. The whole economy of the shop, and no Unity in it.
    ///
    /// Plain C# on purpose, the way <see cref="Purse"/> and <see cref="Glicko2"/> are: the
    /// rules here decide whether a player can have a thing, and a rule that can only be
    /// exercised by pressing a button in a running game is a rule nobody checks. Everything
    /// below takes the catalogue and the two lists out of <see cref="PlayerProgress"/> and
    /// gives an answer; the MonoBehaviour that owns the screen does the saving.
    ///
    /// Four rules, and they are the whole of it:
    ///
    ///   1. The DEFAULT of a slot is owned by everybody and costs nothing. It is what is
    ///      worn before anything is bought, and it can always be equipped again -- a shop
    ///      that can put a hat on you and not take it off is a trap.
    ///   2. You cannot buy what you own, and buying what you cannot afford changes nothing.
    ///      Both are answers, not exceptions: the screen shows the reason.
    ///   3. Buying EQUIPS. docs/09 sells a look, and a player who has just spent three
    ///      days of gold wants to see it on, not to find a second button.
    ///   4. You cannot wear what you do not own. The try-on is a different thing entirely
    ///      (see ShopUI.Preview) and never touches the save.
    /// </summary>
    public static class Wardrobe
    {
        /// <summary>Does the player have it? Defaults are owned by everyone.</summary>
        public static bool Owns(CosmeticCatalogue catalogue, List<string> owned, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null) return false;
            if (item.isDefault) return true;
            return owned != null && owned.Contains(id);
        }

        /// <summary>
        /// What is worn in that slot: the saved id, or the slot's default when nothing is.
        ///
        /// The fallback is here rather than in the save so that an id the catalogue has
        /// dropped -- an item pulled from sale, a renamed key -- reads as the default look
        /// instead of as a fighter with no model.
        /// </summary>
        public static string WornIn(CosmeticCatalogue catalogue, List<string> equipped,
                                    Slot slot)
        {
            int at = (int)slot;
            if (equipped != null && at < equipped.Count)
            {
                string saved = equipped[at];
                if (!string.IsNullOrEmpty(saved) && catalogue != null
                    && catalogue.Find(saved) != null)
                    return saved;
            }
            Cosmetic fallback = catalogue != null ? catalogue.DefaultIn(slot) : null;
            return fallback != null ? fallback.id : "";
        }

        /// <summary>True when that exact id is what the slot is wearing.</summary>
        public static bool IsWorn(CosmeticCatalogue catalogue, List<string> equipped,
                                  string id)
        {
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null) return false;
            return WornIn(catalogue, equipped, item.slot) == id;
        }

        /// <summary>
        /// Puts it on, if it is owned. The list grows to hold every slot, because a save
        /// written before a slot existed is shorter than the enum.
        /// </summary>
        public static Deal Equip(CosmeticCatalogue catalogue, List<string> owned,
                                 List<string> equipped, string id)
        {
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null) return Deal.NoSuchItem;
            if (!Owns(catalogue, owned, id)) return Deal.NotOwned;

            int at = (int)item.slot;
            while (equipped.Count <= at) equipped.Add("");
            equipped[at] = id;
            return Deal.Equipped;
        }

        /// <summary>
        /// Spends the gold and puts it on. The gold is passed by reference because the
        /// purse belongs to PlayerProgress and this does the arithmetic -- one place that
        /// can take money, and it is the same place that hands over the goods.
        /// </summary>
        public static Deal Buy(CosmeticCatalogue catalogue, ref int gold, List<string> owned,
                               List<string> equipped, string id)
        {
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null) return Deal.NoSuchItem;
            if (Owns(catalogue, owned, id)) return Deal.AlreadyOwned;
            if (gold < item.Cost) return Deal.NotEnoughGold;

            gold -= item.Cost;
            owned.Add(id);
            Equip(catalogue, owned, equipped, id);
            return Deal.Bought;
        }

        /// <summary>What the one button at the bottom of the shop should say right now.</summary>
        public static string Verb(CosmeticCatalogue catalogue, List<string> owned,
                                  List<string> equipped, string id)
        {
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null) return "";
            if (IsWorn(catalogue, equipped, id)) return "WORN";
            if (Owns(catalogue, owned, id)) return "EQUIP";
            return "BUY";
        }

        /// <summary>How many of a slot's items are owned, for "7 / 12" on a tab.</summary>
        public static int OwnedIn(CosmeticCatalogue catalogue, List<string> owned, Slot slot)
        {
            int n = 0;
            if (catalogue == null) return 0;
            foreach (Cosmetic c in catalogue.InSlot(slot))
                if (Owns(catalogue, owned, c.id)) n++;
            return n;
        }
    }
}

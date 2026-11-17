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
        ///
        /// The saved id has to BELONG to the slot it was found in, and that is not
        /// belt-and-braces: the save is a list indexed by the enum, so the day the enum is
        /// reordered -- as it was when emotes and graffiti came off sale -- every old save
        /// has its ids one or two places out. Checking the slot turns that from a knockout
        /// effect worn as a lock face into the default, which is what a save written for a
        /// shop that no longer exists should read as.
        /// </summary>
        public static string WornIn(CosmeticCatalogue catalogue, List<string> equipped,
                                    Slot slot)
        {
            int at = (int)slot;
            if (equipped != null && at < equipped.Count)
            {
                string saved = equipped[at];
                Cosmetic found = !string.IsNullOrEmpty(saved) && catalogue != null
                    ? catalogue.Find(saved) : null;
                if (found != null && found.slot == slot)
                    return saved;
            }
            Cosmetic fallback = catalogue != null ? catalogue.DefaultIn(slot) : null;
            return fallback != null ? fallback.id : "";
        }

        /// <summary>
        /// Which piece is fitted to one part of the lock, or that part's free default.
        ///
        /// The lock is the only thing in the wardrobe built out of PIECES rather than worn
        /// whole, and the reason is in <see cref="LockPart"/>: it belongs to the player and
        /// to nobody else, and it is in front of them several times a match by choice. So
        /// it is improved a corner at a time, and the list it is saved in is its own.
        /// </summary>
        public static string FittedTo(CosmeticCatalogue catalogue, List<string> parts,
                                      LockPart part)
        {
            int at = (int)part;
            if (parts != null && at < parts.Count)
            {
                string saved = parts[at];
                Cosmetic found = !string.IsNullOrEmpty(saved) && catalogue != null
                    ? catalogue.Find(saved) : null;
                if (found != null && found.slot == Slot.Lock && found.part == part)
                    return saved;
            }
            Cosmetic fallback = catalogue != null ? catalogue.DefaultIn(part) : null;
            return fallback != null ? fallback.id : "";
        }

        /// <summary>True when that exact piece is the one fitted.</summary>
        public static bool IsFitted(CosmeticCatalogue catalogue, List<string> parts, string id)
        {
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null || item.slot != Slot.Lock) return false;
            return FittedTo(catalogue, parts, item.part) == id;
        }

        /// <summary>Fits a piece to the lock, if it is owned.</summary>
        public static Deal Fit(CosmeticCatalogue catalogue, List<string> owned,
                               List<string> parts, string id)
        {
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null || item.slot != Slot.Lock) return Deal.NoSuchItem;
            if (!Owns(catalogue, owned, id)) return Deal.NotOwned;

            int at = (int)item.part;
            while (parts.Count <= at) parts.Add("");
            parts[at] = id;
            return Deal.Equipped;
        }

        /// <summary>How many pieces of one part of the lock are owned, for "2 / 4".</summary>
        public static int OwnedIn(CosmeticCatalogue catalogue, List<string> owned,
                                  LockPart part)
        {
            int n = 0;
            if (catalogue == null) return 0;
            foreach (Cosmetic c in catalogue.InPart(part))
                if (Owns(catalogue, owned, c.id)) n++;
            return n;
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
                                 List<string> equipped, List<string> parts, string id)
        {
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null) return Deal.NoSuchItem;
            if (!Owns(catalogue, owned, id)) return Deal.NotOwned;

            // A piece of the lock goes on the lock. One way in, so that nothing has to
            // remember which of the six slots is the one built out of pieces.
            if (item.slot == Slot.Lock) return Fit(catalogue, owned, parts, id);

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
                               List<string> equipped, List<string> parts, string id)
        {
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null) return Deal.NoSuchItem;
            if (Owns(catalogue, owned, id)) return Deal.AlreadyOwned;
            if (gold < item.Cost) return Deal.NotEnoughGold;

            gold -= item.Cost;
            owned.Add(id);
            Equip(catalogue, owned, equipped, parts, id);
            return Deal.Bought;
        }

        /// <summary>What the one button at the bottom of the shop should say right now.</summary>
        public static string Verb(CosmeticCatalogue catalogue, List<string> owned,
                                  List<string> equipped, List<string> parts, string id)
        {
            Cosmetic item = catalogue != null ? catalogue.Find(id) : null;
            if (item == null) return "";

            // A piece of the lock is FITTED rather than worn, and the button says so: you
            // are not putting the lock on, you are building it.
            if (item.slot == Slot.Lock)
            {
                if (IsFitted(catalogue, parts, id)) return "FITTED";
                return Owns(catalogue, owned, id) ? "FIT" : "BUY";
            }

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

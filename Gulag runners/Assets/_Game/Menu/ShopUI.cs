using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using GulagRunners.Meta;

namespace GulagRunners.Game
{
    /// <summary>
    /// The shop and the inventory, which are one screen.
    ///
    /// They were two in the first sketch and that was the mistake: a player who owns a
    /// thing and a player who does not are looking at the same list, in the same order,
    /// wanting the same picture of themselves in it. So there is one screen with one
    /// button at the bottom of it, and the button says BUY, EQUIP or WORN depending on
    /// which of the three is true (<see cref="Wardrobe.Verb"/>). The inventory is this
    /// screen with the "owned only" filter on.
    ///
    /// THE SHAPE, which is the one asked for: the fighter stands on the LEFT at full
    /// height, the list is on the RIGHT with its categories, and the one button sits under
    /// the list at the bottom right, where the thumb of the hand holding the phone already
    /// is. One tap on an item TRIES IT ON -- the model changes and nothing is spent, which
    /// is the whole reason the fighter is on this screen at all.
    ///
    /// This class is the view-model, in the idiom MenuUI set: it holds no Text, no Image
    /// and no layout, raises UnityEvents a builder wires to whatever draws them, and can
    /// be driven from the inspector while nothing is drawn at all. ShopBuilder makes the
    /// screen; this makes it mean something.
    ///
    /// Nothing here can sell power. It is typed on <see cref="Cosmetic"/> and the only
    /// things that exist are the six slots docs/09 allows, so there is no method that
    /// could take a weapon's damage or a fighter's health even if somebody wanted one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopUI : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Everything on sale. Assets > Create > Gulag Runners > Cosmetic catalogue.")]
        public CosmeticCatalogue catalogue;

        [Tooltip("The saved purse and wardrobe. Found in the scene when empty.")]
        public PlayerProgress progress;

        [Tooltip("The fighter standing in the menu, so a tap can dress him. Optional: " +
                 "without one the screen still buys and equips, it just shows nothing.")]
        public MenuFighter fighter;

        [Header("What the screen shows")]
        [Tooltip("Which category is open. The tabs set it; the list reads it.")]
        public Slot slot = Slot.Runner;

        [Tooltip("The id under the cursor -- what the button at the bottom would act on. " +
                 "Set by tapping a row, and it is also what the fighter is wearing for the " +
                 "look of it (the try-on), which is why it is not the same as what is " +
                 "EQUIPPED.")]
        public string picked = "";

        [Tooltip("Show only what is owned. This, and nothing else, is \"the inventory\".")]
        public bool ownedOnly;

        [Header("Readouts")]
        [Tooltip("The purse, as text.")]
        public UnityEvent<string> goldChanged;

        [Tooltip("What the picked item is called.")]
        public UnityEvent<string> nameChanged;

        [Tooltip("Its price as text, or OWNED when it is already bought.")]
        public UnityEvent<string> priceChanged;

        [Tooltip("BUY, EQUIP or WORN -- what the button at the bottom right says now.")]
        public UnityEvent<string> verbChanged;

        [Tooltip("Whether that button can be pressed at all: false while the picked item " +
                 "costs more gold than there is.")]
        public UnityEvent<bool> canActChanged;

        [Tooltip("The open category's name, for the tab strip's title.")]
        public UnityEvent<string> slotChanged;

        [Tooltip("\"7 / 12\" -- owned against everything in the open category.")]
        public UnityEvent<string> countedChanged;

        [Tooltip("The tier of the picked item, 0 common, 1 rare, 2 legendary. Wire it to " +
                 "whatever tints the frame; UiPalette.Ramp is the one ramp in the game.")]
        public UnityEvent<int> tierChanged;

        [Tooltip("What just happened, in one line: \"BOUGHT\", \"NOT ENOUGH GOLD\". The " +
                 "screen says it under the button and clears it on the next pick.")]
        public UnityEvent<string> saidChanged;

        [Tooltip("Fired after a buy or an equip with the whole list's state changed, so a " +
                 "list that draws rows can redraw them. No argument: what to read is here.")]
        public UnityEvent listChanged;

        /// <summary>What the open category holds, in catalogue order, after the filter.</summary>
        public List<Cosmetic> Showing()
        {
            List<Cosmetic> all = catalogue != null
                ? catalogue.InSlot(slot) : new List<Cosmetic>();
            if (!ownedOnly) return all;

            List<Cosmetic> mine = new List<Cosmetic>();
            foreach (Cosmetic c in all)
                if (progress != null && Wardrobe.Owns(catalogue, progress.owned, c.id))
                    mine.Add(c);
            return mine;
        }

        void Awake()
        {
            if (progress == null) progress = FindFirstObjectByType<PlayerProgress>();
            if (fighter == null) fighter = FindFirstObjectByType<MenuFighter>();
        }

        void OnEnable()
        {
            // Opening the shop picks what is already worn in the open category, so the
            // screen opens on the player rather than on the first thing for sale.
            if (string.IsNullOrEmpty(picked))
                picked = Wardrobe.WornIn(catalogue, progress != null ? progress.equipped : null,
                                         slot);
            Show();
        }

        /// <summary>Opens a category. The pick moves to whatever is worn in it.</summary>
        public void OpenSlot(int which)
        {
            if (which < 0 || which >= CosmeticCatalogue.Slots.Length) return;
            slot = CosmeticCatalogue.Slots[which];
            picked = Wardrobe.WornIn(catalogue, progress != null ? progress.equipped : null,
                                     slot);
            Say("");
            Show();
            Preview();
        }

        /// <summary>The inventory toggle: everything, or only what is owned.</summary>
        public void ShowOwnedOnly(bool only)
        {
            ownedOnly = only;
            Show();
        }

        /// <summary>
        /// One tap on a row: pick it and WEAR it for the look of it.
        ///
        /// Nothing is spent and nothing is saved. The fighter is the price tag here -- a
        /// shop that makes you buy a thing to find out whether it suits you is a shop
        /// people stop opening.
        /// </summary>
        public void Pick(string id)
        {
            picked = id;
            Say("");
            Show();
            Preview();
        }

        /// <summary>
        /// The one button at the bottom right. It buys, or it equips, by what is true.
        ///
        /// One button and not two, because at any moment exactly one of them is possible,
        /// and a screen that shows a greyed-out EQUIP beside a live BUY is asking the
        /// player to work out which of two dead ends they are in.
        /// </summary>
        public void Act()
        {
            if (progress == null || catalogue == null) return;
            Cosmetic item = catalogue.Find(picked);
            if (item == null) return;

            if (Wardrobe.IsWorn(catalogue, progress.equipped, picked))
            {
                Say("ALREADY WORN");
                return;
            }

            Deal what;
            if (Wardrobe.Owns(catalogue, progress.owned, picked))
            {
                what = Wardrobe.Equip(catalogue, progress.owned, progress.equipped, picked);
            }
            else
            {
                int gold = progress.gold;
                what = Wardrobe.Buy(catalogue, ref gold, progress.owned, progress.equipped,
                                    picked);
                progress.gold = gold;
            }

            if (what == Deal.Bought || what == Deal.Equipped)
            {
                // Saved on the spot. A cosmetic bought and then lost to a crash is the
                // one bug in a shop nobody forgives.
                if (progress.persist) progress.Save();
                Wear();
            }

            Say(what switch
            {
                Deal.Bought => "BOUGHT",
                Deal.Equipped => "EQUIPPED",
                Deal.NotEnoughGold => "NOT ENOUGH GOLD",
                Deal.AlreadyOwned => "ALREADY OWNED",
                Deal.NotOwned => "NOT OWNED",
                _ => "",
            });
            Show();
            listChanged?.Invoke();
        }

        /// <summary>Puts the picked look on the fighter, owned or not. Saves nothing.</summary>
        public void Preview()
        {
            if (fighter == null || catalogue == null) return;
            Cosmetic item = catalogue.Find(picked);
            if (item == null || item.slot != Slot.Runner) return;
            fighter.Wear(item.Key);
        }

        /// <summary>Puts on what is actually EQUIPPED. Used after a buy and on close.</summary>
        public void Wear()
        {
            if (fighter == null || progress == null || catalogue == null) return;
            string worn = Wardrobe.WornIn(catalogue, progress.equipped, Slot.Runner);
            Cosmetic item = catalogue.Find(worn);
            if (item != null) fighter.Wear(item.Key);
        }

        /// <summary>Everything the screen draws, from the state, in one place.</summary>
        public void Show()
        {
            Cosmetic item = catalogue != null ? catalogue.Find(picked) : null;
            List<string> owned = progress != null ? progress.owned : null;
            List<string> worn = progress != null ? progress.equipped : null;

            goldChanged?.Invoke(progress != null ? progress.gold.ToString("N0") : "0");
            slotChanged?.Invoke(CosmeticCatalogue.Name(slot));

            int have = Wardrobe.OwnedIn(catalogue, owned, slot);
            int all = catalogue != null ? catalogue.InSlot(slot).Count : 0;
            countedChanged?.Invoke($"{have} / {all}");

            if (item == null)
            {
                nameChanged?.Invoke("");
                priceChanged?.Invoke("");
                verbChanged?.Invoke("");
                canActChanged?.Invoke(false);
                tierChanged?.Invoke(0);
                return;
            }

            nameChanged?.Invoke(item.shown);
            tierChanged?.Invoke((int)item.tier);

            bool mine = Wardrobe.Owns(catalogue, owned, item.id);
            priceChanged?.Invoke(mine ? "OWNED" : item.Cost.ToString("N0"));
            verbChanged?.Invoke(Wardrobe.Verb(catalogue, owned, worn, item.id));

            bool canAfford = progress != null && progress.gold >= item.Cost;
            canActChanged?.Invoke(mine
                ? !Wardrobe.IsWorn(catalogue, worn, item.id)
                : canAfford);
        }

        void Say(string line) => saidChanged?.Invoke(line);
    }
}

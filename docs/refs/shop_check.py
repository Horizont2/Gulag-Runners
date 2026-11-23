"""Does the shop obey docs/09, and can anything in it actually be worn?

docs/09 is unusually specific for a design note, and every number in it is checkable:

    Gold is spent on ONLY cosmetics: runner skins, weapon finishes, knockout effects,
    name plates, lock faces, menu poses.
    A T1 skin costs 800, a T3 skin 3000. An active player earns 250-350 a day, so a
    cosmetic is three to ten days away without paying.
    IRON RULE: gold never buys power.

So this reads the catalogue and the code and says whether that is still true:

  * every id is unique, lower case, and free of the character the save joins on -- an id
    with a '|' in it takes the rest of the wardrobe with it when PlayerProgress writes;
  * every slot has exactly ONE default: the free thing worn before anything is bought.
    None and the player opens the shop wearing nothing; two and which one is a coin toss;
  * every price is one of docs/09's three, and the days-to-afford it implies stays inside
    the three-to-ten the note promises;
  * the slots are the six the note allows and no more -- a seventh is how "gold never buys
    power" is broken, one innocent-looking enum value at a time;
  * what is OWNED is separate from what is EQUIPPED, and the save carries both;
  * and the honest part: how many of the catalogue's rows anything can actually put on a
    fighter today. A shop can sell a look it cannot show, and that is a list, not a bug --
    but it has to be a list somebody can read rather than a surprise in a screenshot.

    python3 shop_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
GAME = os.path.join(ASSETS, "_Game")
CATALOGUE = os.path.join(GAME, "Shop", "Cosmetics.asset")
SCENES = os.path.join(ASSETS, "Scenes")

# docs/09, to the coin.
PRICES = {0: 800, 1: 1600, 2: 3000}
A_DAY_LOW, A_DAY_HIGH = 250, 350
DAYS_LOW, DAYS_HIGH = 3, 10

# The six that are actually made and seen, in the order the save indexes them by.
#
# docs/09 named six categories and this is not the same six: emotes and graffiti came off
# sale, locks and poses went on. The reason is docs/01 -- the other player cannot be seen
# for most of a match, so a gesture and a mark on a far wall are bought by nobody, while a
# lock face is looked at several times a match by the one person paying for it. The RULE
# the list enforces is unchanged and is the only one that matters: six, and a seventh is
# how "gold never buys power" goes, one innocent enum value at a time.
SLOTS = ("Runner", "Weapon", "Knockout", "Plate", "Lock", "Pose")

# And the Lock slot is the one built out of PIECES, in the order its own save indexes
# them by. The lock belongs to the player and to nobody else, so it is improved a corner
# at a time rather than replaced whole.
LOCK_PARTS = ("Plate", "Bezel", "Keys", "Bolts", "Glow")


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def catalogue():
    """The asset as a list of dicts, in order."""
    if not os.path.exists(CATALOGUE):
        return None
    rows, current = [], None
    for line in read(CATALOGUE).splitlines():
        m = re.match(r"\s*- id: (.*)$", line)
        if m:
            current = {"id": m.group(1).strip()}
            rows.append(current)
            continue
        if current is None:
            continue
        m = re.match(r"\s*(shown|slot|tier|price|isDefault|applies|part|tint): (.*)$",
                     line)
        if m:
            current[m.group(1)] = m.group(2).strip()
    return rows


def menu_skins():
    """The runner skins actually wired on the MenuFighter, by id."""
    path = os.path.join(SCENES, "Menu.unity")
    if not os.path.exists(path):
        return set()
    text = read(path)
    found = set()
    for block in re.finditer(r"(?ms)^--- !u!114 &-?\d+\n(.*?)(?=^--- !u!|\Z)", text):
        body = block.group(1)
        if "skins:" not in body or "poseState" not in body:
            continue
        for m in re.finditer(r"- id: ([^\n]*)\n    root: \{fileID: (-?\d+)\}", body):
            if m.group(2) != "0":
                found.add(m.group(1).strip())
    return found


def main():
    problems, notes = [], []

    rows = catalogue()
    if rows is None:
        print(f"  there is no {os.path.relpath(CATALOGUE, ASSETS)}, so the shop has "
              f"nothing to sell. Assets > Create > Gulag Runners > Cosmetic catalogue.")
        print("shop_check: 1 problem(s)")
        return 1

    notes.append(f"{len(rows)} row(s) in the catalogue")

    # ---- ids
    seen = {}
    for r in rows:
        rid = r["id"]
        if rid in seen:
            problems.append(f"the id {rid!r} is in the catalogue twice; an id is the key "
                            f"in the save file, so the second one silently owns the first")
        seen[rid] = r
        if rid != rid.lower():
            problems.append(f"the id {rid!r} has capitals in it; ids are compared as "
                            f"written and a save is forever, so they are lower case")
        if "|" in rid:
            problems.append(f"the id {rid!r} contains '|', which PlayerProgress joins the "
                            f"owned list with -- saving it would eat the rest of the "
                            f"wardrobe")

    # ---- slots and defaults
    for i, name in enumerate(SLOTS):
        mine = [r for r in rows if r.get("slot") == str(i)]
        defaults = [r for r in mine if r.get("isDefault") == "1"]
        notes.append(f"{name}: {len(mine)} row(s), {len(defaults)} default")
        if not mine:
            problems.append(f"the {name} slot has nothing in it at all, so its tab opens "
                            f"on an empty list")
            continue
        # The lock is the one slot built out of pieces, so it has one free piece per PART
        # rather than one per slot. That is checked below, where the parts are.
        if name == "Lock":
            continue
        if len(defaults) != 1:
            problems.append(
                f"the {name} slot has {len(defaults)} default(s) and needs exactly one: "
                f"the free look worn before anything is bought. None and the shop opens "
                f"on a player wearing nothing; two and which one is a coin toss")

    strays = [r for r in rows if r.get("slot") not in {str(i) for i in range(len(SLOTS))}]
    for r in strays:
        problems.append(f"{r['id']} is in slot {r.get('slot')}, which is not one of the six "
                        f"docs/09 allows. A seventh slot is how the iron rule goes")

    # ---- prices
    for r in rows:
        tier = int(r.get("tier", "0") or 0)
        own = int(r.get("price", "0") or 0)
        default = r.get("isDefault") == "1"
        price = own if own > 0 else PRICES.get(tier, 0)

        if default and price and own:
            problems.append(f"{r['id']} is a default and carries a price of {own}; the "
                            f"default of a slot is what everybody already has")
        if default:
            continue
        if own and own not in PRICES.values():
            problems.append(f"{r['id']} is priced at {own}, which is not one of docs/09's "
                            f"three ({', '.join(str(p) for p in PRICES.values())}). A "
                            f"catalogue of one-off numbers is an economy nobody can tune")
        slow = (price + A_DAY_LOW - 1) // A_DAY_LOW
        quick = (price + A_DAY_HIGH - 1) // A_DAY_HIGH

        # The note's own numbers give 2.3 days for the 800 and 12 for the 3000, and it
        # calls that "three to ten" -- so the promise is that an item LANDS somewhere in
        # that window, not that it does at both ends of the earning rate. An item is
        # wrong when its whole range misses the window: too dear to be reached in ten
        # days of playing well, or so cheap that three days of playing badly buys it.
        if quick > DAYS_HIGH or slow < DAYS_LOW:
            problems.append(
                f"{r['id']} costs {price}, which is {quick} to {slow} days of play. "
                f"docs/09 promises three to ten: outside that it is either a reward for "
                f"turning up or a thing nobody buys")

    # ---- what can be worn
    skins = menu_skins()
    notes.append(f"the MenuFighter in Menu.unity carries {len(skins)} runner skin(s): "
                 f"{', '.join(sorted(skins)) if skins else 'none'}")
    missing = []
    for r in rows:
        if r.get("slot") != "0":
            continue
        key = (r.get("applies") or "").strip() or r["id"]
        if key not in skins:
            missing.append(f"{r['id']} (wants a MenuSkin called {key!r})")
    if missing:
        notes.append(f"{len(missing)} runner skin(s) are on sale with no model to show:")
        for m in missing:
            notes.append(f"    {m}")

    for i, name in enumerate(SLOTS):
        if i == 0:
            continue
        mine = [r for r in rows if r.get("slot") == str(i) and r.get("isDefault") != "1"]
        if mine:
            notes.append(f"{name}: {len(mine)} priced row(s), and nothing applies them yet "
                         f"-- the slot sells and saves, the look is still to be built")

    # ---- the save carries both halves
    progress = read(os.path.join(GAME, "Meta", "PlayerProgress.cs"))
    for field in ("owned", "equipped"):
        if not re.search(r"public List<string> " + field + r"\b", progress):
            problems.append(f"PlayerProgress has no {field} list, so the wardrobe is not "
                            f"saved and every cosmetic is bought again on the next launch")
    if 'PlayerPrefs.SetString(Key + "owned"' not in progress:
        problems.append("PlayerProgress never writes the owned list to PlayerPrefs")
    if 'PlayerPrefs.SetString(Key + "worn"' not in progress:
        problems.append("PlayerProgress never writes what is equipped to PlayerPrefs")

    # ---- the lock, which is the one slot built out of PIECES
    #
    # It is the player's own thing: nobody else ever sees it, and it is in front of them
    # several times a match by choice, so it is improved a corner at a time. Everything
    # below is what keeps that from turning into an advantage.
    lock = [r for r in rows if r.get("slot") == "4"]
    notes.append(f"the lock is built from {len(LOCK_PARTS)} part(s), {len(lock)} piece(s) "
                 f"in all")
    for i, name in enumerate(LOCK_PARTS):
        mine = [r for r in lock if (r.get("part") or "0") == str(i)]
        free = [r for r in mine if r.get("isDefault") == "1"]
        notes.append(f"    {name}: {len(mine)} piece(s), {len(free)} free")
        if not mine:
            problems.append(f"the lock's {name} has no pieces at all, so that corner of "
                            f"the lock cannot be drawn")
            continue
        if len(free) != 1:
            problems.append(
                f"the lock's {name} has {len(free)} free piece(s) and needs exactly one. "
                f"A part with none is a lock with a hole in it before anything is bought; "
                f"a part with two is a coin toss")

    strays = [r for r in lock
              if (r.get("part") or "0") not in {str(i) for i in range(len(LOCK_PARTS))}]
    for r in strays:
        problems.append(f"{r['id']} is part {r.get('part')} of the lock, which is not one "
                        f"of the {len(LOCK_PARTS)}")

    outside = [r for r in rows
               if r.get("slot") != "4" and (r.get("part") or "0") != "0"]
    for r in outside:
        problems.append(f"{r['id']} is not a lock piece but carries a part. Only the lock "
                        f"is built out of pieces, and a part on anything else is a second "
                        f"meaning for a field that already has one")

    # And the rule that keeps the whole idea honest: a piece of the lock may decorate it
    # and may not make it easier to READ. Everything that carries information is drawn by
    # PuzzleView over whatever was bought, so the check is that PuzzleView still owns
    # those layers -- a lock that is easier to read is a lock that opens faster, and gold
    # does not buy that.
    view = os.path.join(GAME, "Presentation", "PuzzleView.cs")
    if os.path.exists(view):
        drawn = read(view)
        for layer, why in (("optionGlyphs", "the rune, the arrow and the gear ARE the "
                                            "puzzle"),
                           ("optionMarks", "the tick says which half of the answer is in"),
                           ("pips", "the pips say how hard it is before you start")):
            if layer not in drawn:
                problems.append(
                    f"PuzzleView no longer draws {layer}, so nothing stops a bought piece "
                    f"of the lock from drawing it instead -- and {why}. Gold does not buy "
                    f"a lock that is easier to read (docs/09)")

    # ---- the save is indexed by the enum, so the enum moving is a real event
    wardrobe = read(os.path.join(GAME, "Meta", "Wardrobe.cs"))
    worn = re.search(r"(?s)public static string WornIn\(.*?\n        \}", wardrobe)
    if not worn or not re.search(r"\.slot\s*==\s*slot", worn.group(0)):
        problems.append(
            "Wardrobe.WornIn does not check that a saved id BELONGS to the slot it was "
            "found in. The worn list is indexed by the Slot enum, so the day that enum is "
            "reordered -- as it was when emotes and graffiti came off sale -- every old "
            "save is one or two places out, and a knockout effect is worn as a lock face")

    # ---- can a thumb actually REACH everything on sale
    #
    # The shop draws a fixed number of rows -- built once, because a screen that
    # instantiates rows is a screen that allocates while a thumb is moving -- and for a
    # long while nothing filled them at all: every row read the same name and price
    # whatever category was open, and ShopUI.listChanged went nowhere, so the list did not
    # redraw after a purchase either.
    #
    # The arithmetic that matters is the one the builder's own comment got wrong. It says
    # the widest category holds four and nothing is hidden; the catalogue has since grown
    # a Locks slot of twenty -- five parts with four faces each -- so sixteen of them had
    # nowhere to appear. A list longer than its window has to move, and that is checked
    # here rather than assumed, because the catalogue will grow again.
    builder = read(os.path.join(GAME, "Editor", "ShopBuilder.cs"))
    lister = read(os.path.join(GAME, "Menu", "ShopList.cs"))

    rows_made = re.search(r"const int Rows = (\d+)", builder or "")
    rows_made = int(rows_made.group(1)) if rows_made else 0
    counts = [len([r for r in rows if r.get("slot") == str(i)])
              for i in range(len(SLOTS))]
    widest = max(counts) if counts else 0

    if not lister:
        problems.append(
            "ShopList is gone, so nothing puts the catalogue into the shop's rows and "
            "every row shows whatever the builder typed into it")
    else:
        if "Showing()" not in lister:
            problems.append(
                "ShopList no longer reads ShopUI.Showing(), so the rows are not the "
                "catalogue any more -- they are decoration that happens to look right")
        # The SUBSCRIPTION, not the word. "listChanged" alone passes against a file where
        # every mention has been renamed to listChangedSomething, which is exactly what my
        # own fault injection did -- a rule that cannot be broken is not a rule.
        if "listChanged.AddListener" not in lister:
            problems.append(
                "ShopList no longer listens for listChanged, so the list does not redraw "
                "after a buy or an equip: the thing just bought still says its price")

        if rows_made and widest > rows_made:
            if "Dragged(" not in lister or "Step(" not in lister:
                problems.append(
                    f"the widest category holds {widest} and the shop draws {rows_made} "
                    f"rows, so {widest - rows_made} of them cannot be reached -- and "
                    f"ShopList has no way to move the window over the list")
            else:
                notes.append(f" the widest category holds {widest} against {rows_made} "
                             f"rows, so the list scrolls")
        elif rows_made:
            notes.append(f" every category fits in {rows_made} rows; the list never "
                         f"scrolls")

    # ---- can what is sold actually be SEEN
    #
    # The one thing a shop must never do. Of the thirty-nine things on sale, five were
    # showable -- the defaults -- and the rest were bought and then invisible, because the
    # menu carries exactly one runner model and ShopUI.Preview handled one slot and
    # returned for the other five.
    #
    # Two slots have an applier now and neither needed new art. A COLOURWAY is the model
    # already there, painted, which is what ASH, COPPER and NIGHTJAR were always named as;
    # a POSE is the name of a state on the controller the fighter already carries. So for
    # those two a cosmetic that cannot be shown is a hole in the DATA and is failed on.
    # For the four slots with no applier the honest report is a count, not a failure:
    # nothing is broken, the work is simply not done.
    shop_ui = read(os.path.join(GAME, "Menu", "ShopUI.cs")) or ""
    fighter = read(os.path.join(GAME, "Menu", "MenuFighter.cs")) or ""
    controller = read(os.path.join(GAME, "Animation", "GR_Player.controller")) or ""
    states = set(re.findall(r"^  m_Name: (.+)$", controller, re.M))
    models = set(menu_skins() or [])

    applied = set()
    if "item.slot == Slot.Runner" in shop_ui and "Paint(" in fighter:
        applied.add("Runner")
    if "item.slot == Slot.Pose" in shop_ui:
        applied.add("Pose")

    waiting = {}
    for i, name in enumerate(SLOTS):
        mine = [r for r in rows if r.get("slot") == str(i)]
        if name not in applied:
            waiting[name] = len(mine) - len([r for r in mine if r.get("isDefault") == "1"])
            continue

        for r in mine:
            if r.get("isDefault") == "1":
                continue
            rid = r["id"]
            key = r.get("applies") or rid

            if name == "Runner":
                tinted = r.get("tint", "").find("a: 1") >= 0
                if not tinted and key not in models:
                    problems.append(
                        f"{rid} is a runner with no model called {key!r} in the menu and no "
                        f"colourway either, so buying it changes nothing on screen. Give it "
                        f"a tint -- docs/refs/cosmetic_appliers.py -- or a model")
            elif name == "Pose":
                if key not in states:
                    problems.append(
                        f"{rid} is a pose naming the state {key!r}, which GR_Player has "
                        f"not got, so the fighter holds whatever pose he arrived in. The "
                        f"controller's states are the vocabulary")

    if waiting:
        notes.append(" nothing applies these yet, so they are bought and not seen: "
                     + ", ".join(f"{k} {v}" for k, v in sorted(waiting.items()) if v))
    if applied:
        notes.append(" shown on the fighter: " + ", ".join(sorted(applied)))

    # ---- and the iron rule, as a rule about the code rather than about intentions
    for word in ("Damage", "Health", "Armour", "Speed", "Reach"):
        if re.search(r"\b" + word + r"\b", wardrobe):
            problems.append(f"Wardrobe mentions {word}. Gold never buys power (docs/09), "
                            f"and the way that rule survives is that the code which spends "
                            f"gold cannot reach a number the fight reads")

    for n in notes:
        print(("  " + n) if n.startswith(" ") else ("  note: " + n))
    for p in problems:
        print("  " + p)
    print(f"shop_check: {len(problems)} problem(s) across {len(rows)} row(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

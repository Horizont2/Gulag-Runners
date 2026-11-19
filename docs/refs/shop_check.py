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
        m = re.match(r"\s*(shown|slot|tier|price|isDefault|applies|part): (.*)$", line)
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

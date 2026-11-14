"""Does the shop obey docs/09, and can anything in it actually be worn?

docs/09 is unusually specific for a design note, and every number in it is checkable:

    Gold is spent on ONLY cosmetics: runner skins, weapon skins, emotes, graffiti on the
    arena walls, knockout effects, profile frames.
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

# The six it allows, in the order the save indexes them by.
SLOTS = ("Runner", "Weapon", "Emote", "Graffiti", "Knockout", "Frame")


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
        m = re.match(r"\s*(shown|slot|tier|price|isDefault|applies): (.*)$", line)
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

    # ---- and the iron rule, as a rule about the code rather than about intentions
    wardrobe = read(os.path.join(GAME, "Meta", "Wardrobe.cs"))
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

#!/usr/bin/env python3
"""
figma_shop_screens.py — the Screens page of the shop file, generated from the catalogue.

WHY THIS IS A FILE AND NOT A MESSAGE. The last version of this script existed only in a
conversation, was described in a task note as "written and ready", and was gone by the time
the Figma rate limit lifted -- so the work had to be worked out again from scratch. A script
that only exists in something that gets summarised is a script you have not written.

It also means the screens cannot drift from the game. Every name, price and count below is
read out of Gulag runners/Assets/_Game/Shop/Cosmetics.asset and
Gulag runners/Assets/_Game/Meta/Purse.cs, so a cosmetic renamed in Unity is renamed in the
mock-up the next time this runs. The old Screens page was wrong precisely because it had
been typed by hand from a catalogue that then changed.

    cd docs/refs && python3 figma_shop_screens.py          # print the JS
    cd docs/refs && python3 figma_shop_screens.py --check  # just the figures, no JS

Then hand the JS to the Figma MCP's use_figma on file LODg3OYK5LqmdlEmZAAZKd. It is a TEXT
pass: the eleven frames, their rail order, their geometry and their components are already
right (checked against get_metadata -- six tabs on every screen, four rows everywhere except
POSES, which has the three the catalogue has). What it writes is what the rows SAY.
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game")
CATALOGUE = os.path.join(GAME, "Shop", "Cosmetics.asset")
PURSE = os.path.join(GAME, "Meta", "Purse.cs")

FILE_KEY = "LODg3OYK5LqmdlEmZAAZKd"
SCREENS_PAGE = "8:2"

# The rail, top to bottom, as Cosmetic.Slot orders it. The tab glyph names come from the
# Components page, which is already on this list.
SLOTS = [
    (0, "RUNNERS", "THE FIGHTER HIMSELF"),
    (1, "WEAPONS", "ONE FINISH ACROSS THE WHOLE RACK"),
    (2, "KNOCKOUTS", "WHAT HAPPENS WHEN HE GOES DOWN"),
    (3, "PLATES", "THE NUMBER OVER YOUR CELL"),
    (4, "LOCKS", "THE PADLOCK ON YOUR OWN DOOR"),
    (5, "POSES", "HOW HE STANDS WHILE YOU CHOOSE"),
]


def read(path):
    try:
        return open(path, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def tiers():
    """The tier -> price ladder, from Purse.SkinPrice rather than from memory."""
    src = read(PURSE)
    block = re.search(r"SkinPrice\(int tier\) => tier switch\s*\{(.*?)\};", src, re.S)
    if not block:
        return {0: 800, 1: 800, 2: 1600, 3: 3000}

    ladder = {}
    for cond, price in re.findall(r"(<?=?\s*\d+|_)\s*=>\s*(\d+)", block.group(1)):
        cond = cond.strip()
        if cond == "_":
            ladder["rest"] = int(price)
        elif cond.startswith("<="):
            ladder["upto" + cond[2:].strip()] = int(price)
        else:
            ladder[int(cond)] = int(price)
    return ladder


def price_of(tier, ladder):
    for key, value in ladder.items():
        if isinstance(key, str) and key.startswith("upto") and tier <= int(key[4:]):
            return value
    if tier in ladder:
        return ladder[tier]
    return ladder.get("rest", 3000)


def items():
    """Every cosmetic, in catalogue order: (slot, part, id, shown, tier, default)."""
    src = read(CATALOGUE)
    found = re.findall(
        r"- id: (\S+)\s*\n\s*shown: (.*?)\n\s*slot: (\d+)\s*\n\s*tier: (\d+)\s*\n"
        r"\s*price: (\d+)\s*\n\s*isDefault: (\d)\s*\n\s*applies: (.*?)\n\s*part: (\d+)",
        src)
    out = []
    for ident, shown, slot, tier, price, default, _applies, part in found:
        out.append({
            "id": ident,
            "shown": shown.strip(),
            "slot": int(slot),
            "part": int(part),
            "tier": int(tier),
            "price": int(price),
            "default": default == "1",
        })
    return out


def rows_for(slot, pool, ladder):
    """
    What one tab shows: four rows, which is what the sheet has room for.

    docs/36 did the arithmetic -- 864 units of screen, less the header, less the bottom
    bar, is 552 for the list, and a row is 132 plus 8 of air. The locks are the one slot
    with more than four, so they are shown by PART; the game's own tab does the same.
    """
    mine = [i for i in pool if i["slot"] == slot]
    if slot == 4:
        mine = [i for i in mine if i["part"] == 0]

    rows = []
    for item in mine[:4]:
        cost = item["price"] if item["price"] > 0 else price_of(item["tier"], ladder)
        rows.append({
            "name": item["shown"],
            "cost": "OWNED" if item["default"] else str(cost),
            "tier": item["tier"],
        })
    return rows


def script(pool, ladder):
    """The JavaScript use_figma runs. One page switch, text only, IDs returned."""
    import json

    plan = []
    for slot, title, said in SLOTS:
        plan.append({
            "frame": "Shop — " + title,
            "category": title,
            "said": said,
            "rows": rows_for(slot, pool, ladder),
        })

    # The five that are states rather than tabs. They borrow a tab's rows and change one
    # thing each, which is the whole point of having them: the difference is the subject.
    runners = rows_for(0, pool, ladder)
    plan.append({"frame": "Inventory — OWNED ONLY", "category": "RUNNERS",
                 "said": "ONLY WHAT YOU OWN", "rows": [r for r in runners if r["cost"] == "OWNED"][:2]})
    plan.append({"frame": "Inventory — NOTHING BOUGHT YET", "category": "RUNNERS",
                 "said": "ONLY WHAT YOU OWN", "rows": runners[:1]})
    plan.append({"frame": "Shop — NOT ENOUGH GOLD", "category": "LOCKS",
                 "said": "NOT ENOUGH GOLD FOR THAT ONE", "rows": rows_for(4, pool, ladder)})
    plan.append({"frame": "Shop — BOUGHT AND WORN", "category": "PLATES",
                 "said": "BOUGHT AND WORN", "rows": rows_for(3, pool, ladder)})
    plan.append({"frame": "Shop — WHILE THE THUMB IS DOWN", "category": "RUNNERS",
                 "said": "EVERY PRESSED STATE AT ONCE", "rows": runners})

    return (
        "const PLAN = " + json.dumps(plan, ensure_ascii=False) + ";\n\n"
        + r"""
const page = await figma.getNodeByIdAsync("%s");
await figma.setCurrentPageAsync(page);

// Every font on the page, loaded before a single character is written. Rule 8: the recipe
// is load -> await -> mutate, and the style string is taken from the node rather than
// guessed ("Semi Bold", not "SemiBold").
const fonts = new Map();
for (const t of page.query("TEXT").toArray())
  for (const seg of t.getStyledTextSegments(["fontName"]))
    fonts.set(seg.fontName.family + "|" + seg.fontName.style, seg.fontName);
await Promise.all([...fonts.values()].map(f => figma.loadFontAsync(f)));

const touched = [];
const missing = [];

for (const want of PLAN) {
  const frame = page.children.find(n => n.name === want.frame);
  if (!frame) { missing.push(want.frame); continue; }

  const cat = frame.query("TEXT[name=category]").first();
  if (cat) { cat.characters = want.category; touched.push(cat.id); }

  const said = frame.query("TEXT[name=said]").first();
  if (said) { said.characters = want.said; touched.push(said.id); }

  const rows = frame.query("INSTANCE[name*=Item row]").toArray().sort((a, b) => a.y - b.y);
  rows.forEach((row, i) => {
    const want_row = want.rows[i];
    if (!want_row) { row.visible = false; touched.push(row.id); return; }
    row.visible = true;

    // The row's texts in reading order: the name is the widest, the cost sits right.
    const texts = row.query("TEXT").toArray().sort((a, b) => a.x - b.x);
    if (texts[0]) { texts[0].characters = want_row.name; touched.push(texts[0].id); }
    const last = texts[texts.length - 1];
    if (last && last !== texts[0]) { last.characters = want_row.cost; touched.push(last.id); }
  });

  const owned = want.rows.filter(r => r.cost === "OWNED").length;
  const count = frame.query("TEXT[name=owned count]").first();
  if (count) {
    count.characters = owned + " OF " + want.rows.length + " OWNED";
    touched.push(count.id);
  }
}

return { mutatedNodeIds: touched, frames: PLAN.length, missing };
""" % SCREENS_PAGE)


def main(argv):
    pool = items()
    ladder = tiers()

    if not pool:
        print("figma_shop_screens: Cosmetics.asset gave no items, so there is nothing to "
              "write. Check the field names in the asset against the regex here.")
        return 1

    if "--check" in argv:
        print("figma_shop_screens: %d cosmetics, tier prices %s" % (len(pool), ladder))
        for slot, title, _ in SLOTS:
            rows = rows_for(slot, pool, ladder)
            print("  %-10s %d row(s): %s" % (
                title, len(rows), ", ".join("%s %s" % (r["name"], r["cost"]) for r in rows)))
        return 0

    print(script(pool, ladder))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

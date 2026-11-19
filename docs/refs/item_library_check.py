"""Do the item DATA FILES still agree with the simulation's own table?

ItemTable.cs says what exists. Assets/_Game/Items holds one ScriptableObject per item, and
ItemLibrary applies all of them over the table before the first tick — so whatever is in
those files is what a BUILD actually plays, and the code table is only what the tests and
the mirrors read.

That gap is not theoretical. The shield was deleted from the simulation months ago, and
docs say so in a comment next to the hole it left at id 23; its data file stayed behind, the
library kept applying it, and so id 23 was alive in every build and in no test. Nothing in
the project could see it, because the only tools that look at balance read the C# and the
only tool that reads the assets is Unity.

So this reads both and fails on the three ways they can disagree that matter:

  * a file names an item the simulation does not have (the shield);
  * an item the simulation DOES have has no file, so half the table is authored and half
    is code and nobody can tell which the build used;
  * two files claim the same item, or a file is missing from the library.

Numbers are allowed to differ — that is what the files are FOR — but a difference is
printed as a note, because the usual reason for one is that somebody changed the code
table and forgot that the assets win.
"""
import os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
GAME = os.path.join(ROOT, "Gulag runners", "Assets", "_Game")
ITEMS = os.path.join(GAME, "Items")
SIM = os.path.join(GAME, "Sim", "Items")


def ids():
    """ItemId.cs: name -> number, for everything but None."""
    src = open(os.path.join(SIM, "ItemId.cs"), encoding="utf-8").read()
    body = src[src.index("public enum ItemId"):]
    body = body[:body.index("}")]
    out = {}
    for name, num in re.findall(r"^\s*(\w+)\s*=\s*(\d+)\s*,?\s*$", body, re.M):
        if name != "None":
            out[name] = int(num)
    return out


KINDS = {"None": 0, "Weapon": 1, "Armour": 2, "Utility": 3, "Helmet": 4, "Belt": 5}


def table():
    """ItemTable.Build(): id name -> {kind, tier, weight, ...numbers}."""
    src = open(os.path.join(SIM, "ItemTable.cs"), encoding="utf-8").read()
    out = {}
    for name, body in re.findall(
            r"t\[\(int\)ItemId\.(\w+)\]\s*=\s*new ItemDef\s*\{(.*?)\};", src, re.S):
        if name == "None":
            continue
        d = {}
        for field, value in re.findall(r"(\w+)\s*=\s*([^,\n]+)", body):
            value = value.strip()
            if field == "Kind":
                d["kind"] = KINDS.get(value.split(".")[-1], -1)
            elif field == "Id":
                continue
            else:
                m = re.match(r"Fix\.FromMilli\((-?\d+)\)", value)
                d[field] = int(m.group(1)) if m else (
                    int(value) if re.fullmatch(r"-?\d+", value) else value)
        out[name] = d
    return out


def assets():
    """One dict per .asset in Items, plus its guid."""
    out = []
    for f in sorted(os.listdir(ITEMS)):
        if not f.endswith(".asset") or f == "ItemLibrary.asset":
            continue
        src = open(os.path.join(ITEMS, f), encoding="utf-8").read()
        d = {"file": f}
        for k, v in re.findall(r"^  (\w+): (-?[\d.]+)$", src, re.M):
            d[k] = float(v) if "." in v else int(v)
        meta = os.path.join(ITEMS, f + ".meta")
        d["guid"] = re.search(r"^guid: ([0-9a-f]+)", open(meta, encoding="utf-8").read(),
                              re.M).group(1) if os.path.exists(meta) else None
        out.append(d)
    return out


def library():
    src = open(os.path.join(ITEMS, "ItemLibrary.asset"), encoding="utf-8").read()
    return re.findall(r"- \{fileID: 11400000, guid: ([0-9a-f]+), type: 2\}", src)


def main():
    by_id = {v: k for k, v in ids().items()}
    tab = table()
    files = assets()
    listed = library()

    problems, notes = [], []
    seen = {}

    for a in files:
        item = a.get("item")
        name = by_id.get(item)
        if name is None:
            problems.append(f"{a['file']}: item {item} is not in ItemId any more — "
                            f"the library still applies it, so it is live in a build")
            continue
        if name in seen:
            problems.append(f"{a['file']}: item {item} ({name}) is also "
                            f"{seen[name]}")
        seen[name] = a["file"]

        if a["guid"] is None:
            problems.append(f"{a['file']}: no .meta, so Unity will mint a new guid and "
                            f"the library reference will dangle")
        elif a["guid"] not in listed:
            problems.append(f"{a['file']}: not listed in ItemLibrary.asset, so the code "
                            f"table is used for it and the file is a lie")

        want = tab.get(name)
        if want is None:
            problems.append(f"{a['file']}: ItemTable has no row for {name}")
            continue
        if a.get("kind") != want.get("kind", 0):
            problems.append(f"{a['file']}: kind {a.get('kind')} against the table's "
                            f"{want.get('kind')} — this decides which SLOT it goes in")
        for field, key, scale in (("tier", "Tier", 1), ("weight", "Weight", 1),
                                  ("damageReduction", "DamageReductionPermille", 1000),
                                  ("stunResist", "StunResistPermille", 1000),
                                  ("guardHold", "GuardHoldPermille", 1000)):
            got = round(a.get(field, 0) * scale)
            exp = want.get(key, 0)
            if got != exp:
                notes.append(f"{a['file']}: {field} {got} against the table's {exp}")

    for name, d in tab.items():
        if d.get("kind", 0) == 0:
            continue
        if name not in seen:
            problems.append(f"{name}: in ItemTable but has no data file, so it is the only "
                            f"item in the game the library does not override")

    for guid in listed:
        if not any(a["guid"] == guid for a in files):
            problems.append(f"ItemLibrary lists {guid}, which is no file in Items/")

    for n in notes:
        print(f"  note - {n}")
    for p in problems:
        print(f"  {p}")
    print(f"item_library_check: {len(problems)} problem(s) across {len(files)} data file(s), "
          f"{len(tab)} item(s) in the table")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

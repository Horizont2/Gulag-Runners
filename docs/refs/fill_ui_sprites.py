"""Fills the sprite set from the art the project actually has.

TWO EXPORTS LIVE IN THIS PROJECT and nothing had ever put either of them into the tools:

  _Game/UI/        the older export, complete and named exactly after the rows -- every
                   HUD row, all sixteen menu rows, all five search rows, at 4x the design
                   size. 49 files, and the sprite set beside it with all 28 rows empty.
  UI/              the newer one off the UI branch: 24 files, all deliberately WHITE so the
                   game decides the colour (see UiPalette), named after what they look like
                   rather than after the rows -- BarFrame, EQFrame, ControlBackground.

So the rule here is the one that was asked for: take the NEW white file for a row wherever
there is one, fall back to the older export where there is not, and leave a row empty only
when neither has anything -- because an empty row is what the builders already handle
(they draw a flat colour or generate a stand-in) and because an empty row is the only
honest way for the tool to show that a piece is still missing.

Every row that ends up on the old export is listed by the check that follows this script
(ui_assets_check), so "what still needs the white version" is a list rather than a memory.

    python3 fill_ui_sprites.py
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SET = os.path.join(ASSETS, "_Game", "UI", "UI Sprites.asset")
OLD_SET = os.path.join(ASSETS, "_Game", "UI", "HUD Sprites.asset")

NEW = "UI"                      # the white export
OLD = os.path.join("_Game", "UI")   # the older one, named after the rows

# key -> (the white file, the older file). Either may be None.
#
# Written out rather than matched, because the two exports disagree about naming on purpose:
# the white one is named after what a thing LOOKS like and the old one after what it IS.
# A matcher that bridged that would be a matcher nobody could predict, and this table is
# read by a person deciding what to draw next.
MAP = {
    # ---- the combat HUD
    "hud/slot/frame":          ("Frames/EQFrame.png",            "HUD/slot/frame.png"),
    "hud/slot/frame-dashed":   (None,                            "HUD/slot/frame-dashed.png"),
    "hud/slot/glow":           (None,                            "HUD/generated/glow.png"),
    "hud/slot/swap-arrow":     (None,                            "HUD/slot/swap-arrow.png"),
    "hud/bar/frame":           ("Frames/BarFrame.png",           "HUD/bar/frame.png"),
    "hud/bar/fill":            ("Fill/BarFill.png",              "HUD/bar/fill.png"),
    # The white export's RoundIcon.png is a TROPHY -- a cup with a stem, 25 px of drawing
    # in a 75 px box -- and this row is the little lamp that lights up for a round won and
    # for each point of stamina. Four narrow cups in a row where four dots belong is the
    # one mapping in this table that was wrong rather than borrowed, and ui_fit_check found
    # it by measuring the ink: 33% of the file across and 89% down is not a dot.
    # The older export's pip.png is a diamond that fills its own box, so it wins here.
    "hud/pip":                 (None,                            "HUD/pip.png"),
    "hud/button/ring-154":     ("Controls/ControlBackground.png", "HUD/button/ring-154.png"),
    "hud/button/ring-130":     (None,                            "HUD/button/ring-130.png"),
    "hud/button/ring-101":     (None,                            "HUD/button/ring-101.png"),
    # No glyph-attack row, and the file in the export is why this is worth a line: the
    # attack button became a STICK (docs/31 -- a weapon is stopped by another weapon, so
    # the guard button went too), and a stick has a knob rather than a verb printed on it.
    # HUD/button/glyph-attack.png is art for a control the game no longer has.
    "hud/button/glyph-block":  ("Controls/ControlsIcons/Guard.png", "HUD/button/glyph-block.png"),
    "hud/button/glyph-action": ("Controls/ControlsIcons/PickUp.png", "HUD/button/glyph-action.png"),
    "hud/button/glyph-jump":   ("Controls/ControlsIcons/Jump.png", "HUD/button/glyph-jump.png"),
    "hud/joystick/ring":       (None,                            "HUD/joystick/ring.png"),
    "hud/joystick/knob":       ("Controls/ControlsIcons/Joystick.png", "HUD/joystick/knob.png"),
    # The two files in HUD/sound are called Vector and Vector-1, which is Figma's doing.
    # Vector-1 is 135x140, square, so it is the chest mark; Vector is the wide one, so it
    # is the arc. Sizes rather than names, because the names say nothing.
    "hud/sound/arc":           (None,                            "HUD/sound/Vector.png"),
    "hud/sound/source-chest":  (None,                            "HUD/sound/Vector-1.png"),
    "hud/icon/coif":           (None,                            "HUD/icon/coif.png"),
    "hud/icon/great-helm":     (None,                            "HUD/icon/great-helm.png"),
    "hud/icon/chainmail":      (None,                            "HUD/icon/chainmail.png"),
    "hud/icon/plate":          (None,                            "HUD/icon/plate.png"),
    "hud/icon/club":           (None,                            "HUD/icon/club.png"),
    "hud/icon/sword":          (None,                            "HUD/icon/sword.png"),
    "hud/icon/belt":           (None,                            "HUD/icon/belt.png"),
    "hud/icon/bandage":        (None,                            "HUD/icon/bandage.png"),
    "hud/mannequin":           (None,                            "HUD/mannequin.png"),
    "hud/leaders":             (None,                            "HUD/generated/leaders.png"),
    "hud/leaders-mirrored":    (None,                            "HUD/generated/leaders-mirrored.png"),

    # ---- the menu
    "menu/icon/shield":        ("Icons/TrophyIcon.png",          "Menu/icon/shield.png"),
    "menu/icon/coin":          ("Icons/CoinIcon.png",            "Menu/icon/coin.png"),
    "menu/icon/contract":      (None,                            "Menu/icon/contract.png"),
    "menu/icon/gear":          ("Icons/SettingsIcon.png",        "Menu/icon/gear.png"),
    "menu/icon/people":        (None,                            "Menu/icon/people.png"),
    "menu/rank/bronze":        (None,                            "Menu/rank/bronze.png"),
    "menu/rank/silver":        (None,                            "Menu/rank/silver.png"),
    "menu/rank/gold":          (None,                            "Menu/rank/gold.png"),
    "menu/rank/platinum":      (None,                            "Menu/rank/platinum.png"),
    "menu/rank/diamond":       (None,                            "Menu/rank/diamond.png"),
    "menu/rank/runner":        (None,                            "Menu/rank/runner.png"),
    "menu/bar/track":          ("Backgrounds/BarBackground 1.png", "Menu/bar/track.png"),
    "menu/bar/fill":           ("Fill/BarFill.png",              "Menu/bar/fill.png"),
    "menu/plate":              ("Frames/MoneyFrame.png",         "Menu/plate.png"),
    "menu/button/play":        ("Backgrounds/PlayBackground.png", "Menu/button/play.png"),
    "menu/button/round":       ("Frames/IconFrame.png",          "Menu/button/round.png"),

    # ---- the search panel
    "search/button/close":     (None,                            "Search/button/close.png"),
    "search/icon/close":       (None,                            "Search/icon/close.png"),
    "search/bar/track":        ("Backgrounds/BarBackground 1.png", "Search/bar/track.png"),
    "search/bar/fill":         ("Fill/BarFill.png",              "Search/bar/fill.png"),
    "search/scrim":            (None,                            "Search/scrim.png"),

    # ---- the result screen, which is new and has no export of its own
    #
    # dim and banner stay EMPTY on purpose: the builder lays the dimmer in as a flat
    # UiPalette.Scrim and the headline as text on nothing, which is what they are meant to
    # be until somebody draws them. An empty row is the tool saying so.
    "result/dim":              (None,                            None),
    "result/panel":            ("Backgrounds/ProfileBackground.png", "Menu/plate.png"),
    "result/banner":           (None,                            None),
    "result/divider":          (None,                            "HUD/Vector.png"),
    "result/button/primary":   ("Backgrounds/Button.png",        "Menu/button/play.png"),
    "result/button/secondary": ("Backgrounds/Button.png",        "Menu/button/play.png"),
    "result/icon/gold":        ("Icons/CoinIcon.png",            "Menu/icon/coin.png"),
    "result/icon/league":      ("Icons/TrophyIcon.png",          "Menu/rank/silver.png"),
}

SET_SCRIPT = "6b200c6401b54646802321c0d4b7c229"   # UiSpriteSet.cs
SET_GUID = "a1f3c08e5b7d4e9a86c2b4f1d70e5c33"


def guid_of(path):
    meta = os.path.join(ASSETS, path + ".meta")
    if not os.path.exists(meta):
        return None
    m = re.search(r"guid: (\w+)", io.open(meta, encoding="utf-8", errors="replace").read())
    return m.group(1) if m else None


def main():
    rows, took_new, took_old, left = [], [], [], []

    for key, (new, old) in MAP.items():
        guid = None
        if new is not None:
            guid = guid_of(os.path.join(NEW, new))
            if guid:
                took_new.append((key, new))
        if guid is None and old is not None:
            guid = guid_of(os.path.join(OLD, old))
            if guid:
                took_old.append((key, old))
        if guid is None:
            left.append(key)
            rows.append(f"  - key: {key}\n    sprite: {{fileID: 0}}")
        else:
            # 21300000 is the sprite inside a texture importer's asset, which is what a
            # Sprite reference points at -- not 2800000, the texture itself. A set full of
            # textures loads as a set full of nulls.
            rows.append(f"  - key: {key}\n"
                        f"    sprite: {{fileID: 21300000, guid: {guid}, type: 3}}")

    body = ("%YAML 1.1\n"
            "%TAG !u! tag:unity3d.com,2011:\n"
            "--- !u!114 &11400000\n"
            "MonoBehaviour:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n"
            "  m_GameObject: {fileID: 0}\n"
            "  m_Enabled: 1\n"
            "  m_EditorHideFlags: 0\n"
            f"  m_Script: {{fileID: 11500000, guid: {SET_SCRIPT}, type: 3}}\n"
            "  m_Name: UI Sprites\n"
            "  m_EditorClassIdentifier: Assembly-CSharp-Editor::GulagRunners.GameEditor.UiSpriteSet\n"
            "  slots:\n" + "\n".join(rows) + "\n")

    io.open(SET, "w", encoding="utf-8").write(body)
    io.open(SET + ".meta", "w", encoding="utf-8").write(
        "fileFormatVersion: 2\n"
        f"guid: {SET_GUID}\n"
        "NativeFormatImporter:\n"
        "  externalObjects: {}\n"
        "  mainObjectFileID: 11400000\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n")

    # The old set is replaced by this one: two sets with the same rows is a tool that
    # opens the wrong one half the time.
    for path in (OLD_SET, OLD_SET + ".meta"):
        if os.path.exists(path):
            os.remove(path)

    print(f"  {len(MAP)} row(s): {len(took_new)} from the white export, "
          f"{len(took_old)} from the older one, {len(left)} left empty")
    print("  from the white export:")
    for key, f in took_new:
        print(f"    {key:28s} <- UI/{f}")
    print("  still on the older export:")
    for key, f in took_old:
        print(f"    {key:28s} <- _Game/UI/{f}")
    if left:
        print("  empty, and meant to be for now: " + ", ".join(left))
    print(f"  wrote _Game/UI/UI Sprites.asset and removed the empty HUD Sprites.asset")


if __name__ == "__main__":
    sys.exit(main())

using UnityEngine;
using UnityEngine.UI;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The lock, drawn where the fighter is standing (docs/04).
    ///
    /// It appears when the local fighter starts working on a chest that has one and goes away
    /// the moment he stops, and it never takes the screen: docs/04 rule 5 says the opponent
    /// does not disappear while a puzzle is on, so this is a panel over the arena and not a
    /// mode. You can be hit in the middle of it. The price is your attention.
    ///
    /// It follows the fighter rather than tracking the chest's own box, because when he is
    /// working on a chest he IS at the chest, and a panel that needs the chest's world
    /// position needs the baked arena, the camera and a transform chain, all to land two
    /// hands away from where the fighter already is.
    ///
    /// Presentation only. Nothing here writes to the simulation (docs/06): a tap goes to
    /// <see cref="PuzzlePad"/>, which answers the next time the simulation asks its sources
    /// what the thumbs are doing, exactly like the jump button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleView : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The round. Leave empty to find the one in the scene.")]
        public MatchState matchState;

        [Tooltip("The camera the arena is seen through. Leave empty for Camera.main.")]
        public Camera view;

        [Tooltip("The canvas this panel lives on, for turning a world point into a UI one.")]
        public RectTransform canvasRect;

        [Header("Pieces")]
        public RectTransform plate;
        public Image plateFace;
        public Image[] pips = new Image[0];
        public RectTransform[] optionRects = new RectTransform[0];
        public Image[] optionFaces = new Image[0];
        public Image[] optionGlyphs = new Image[0];
        public Image[] optionMarks = new Image[0];

        [Header("Glyphs")]
        [Tooltip("One rune. The odd one out is the same rune TURNED -- docs/04 #1 asks for a "
                 + "rotation, an extra stroke or another colour, and a rotation needs one "
                 + "sprite instead of six.")]
        public Sprite rune;
        public Sprite turnLeft;
        public Sprite turnRight;
        public Sprite gear;

        [Header("Placing")]
        [Tooltip("How far above the fighter the panel sits, in world metres.")]
        public float heightAboveFighter = 2.2f;

        [Tooltip("Units of the design sheet to keep between the panel and the screen edge.")]
        public float margin = 40f;

        PuzzlePad _pad;
        int _chest = -1;

        void Reset()
        {
            plate = transform as RectTransform;
        }

        void Awake()
        {
            if (matchState == null) matchState = FindFirstObjectByType<MatchState>();
            if (view == null) view = Camera.main;
            if (canvasRect == null)
            {
                Canvas c = GetComponentInParent<Canvas>();
                if (c != null) canvasRect = c.transform as RectTransform;
            }
            Hide();
        }

        void LateUpdate()
        {
            PlayerController fighter = Local();
            if (fighter == null) { Hide(); return; }

            int chest = fighter.State.OpeningChest;
            ChestSimState[] states = matchState != null ? matchState.ChestStates : null;
            if (states == null || chest < 0 || chest >= states.Length) { Hide(); return; }

            Puzzle puzzle = states[chest].Puzzle;
            if (!puzzle.Exists || states[chest].Opened) { Hide(); return; }

            if (_chest != chest)
            {
                // A different lock under the hands: whatever was half-tapped on the last one
                // is not an answer to this one.
                _chest = chest;
                if (_pad != null) _pad.Forget();
            }

            Show(in puzzle, in states[chest]);
            Place(fighter);
        }

        PlayerController Local()
        {
            if (matchState == null || matchState.fighters == null) return null;
            int at = matchState.LocalFighter;
            if (at < 0 || at >= matchState.fighters.Count) return null;

            PlayerController f = matchState.fighters[at];
            if (f == null) return null;
            if (_pad == null) _pad = f.GetComponentInChildren<PuzzlePad>(true);
            return f;
        }

        void Hide()
        {
            _chest = -1;
            if (_pad != null) _pad.Forget();
            if (plate != null && plate.gameObject.activeSelf) plate.gameObject.SetActive(false);
        }

        void Show(in Puzzle puzzle, in ChestSimState state)
        {
            if (plate == null) return;
            if (!plate.gameObject.activeSelf) plate.gameObject.SetActive(true);

            // The pips say how hard it is BEFORE the first touch, so the player can decide
            // whether it is worth the three seconds (docs/04 rule 7).
            for (int i = 0; i < pips.Length; i++)
            {
                if (pips[i] == null) continue;
                bool lit = i < puzzle.Pips;
                pips[i].gameObject.SetActive(true);
                pips[i].color = lit ? UiPalette.Amber : UiPalette.Fade(UiPalette.Ink, 0.15f);
            }

            // Sulking after a wrong touch: the lock goes red and takes nothing for a second.
            bool sulking = state.PuzzleStun > 0;
            if (plateFace != null)
                plateFace.color = sulking
                    ? UiPalette.Fade(UiPalette.Danger, 0.35f)
                    : UiPalette.Plate;

            int shown = puzzle.Options;
            for (int i = 0; i < optionRects.Length; i++)
            {
                bool live = i < shown;
                if (optionRects[i] != null && optionRects[i].gameObject.activeSelf != live)
                    optionRects[i].gameObject.SetActive(live);
                if (!live) continue;

                int option = i + 1;
                bool used = Used(in puzzle, state.PuzzleDone, option);

                if (optionFaces.Length > i && optionFaces[i] != null)
                    optionFaces[i].color = sulking
                        ? UiPalette.Fade(UiPalette.Ink, 0.08f)
                        : (used ? UiPalette.Fade(UiPalette.Amber, 0.3f)
                                : UiPalette.Fade(UiPalette.Ink, 0.16f));

                if (optionMarks.Length > i && optionMarks[i] != null)
                    optionMarks[i].gameObject.SetActive(used);

                Dress(i, option, in puzzle);
            }
        }

        /// <summary>
        /// Which glyph sits on an option, and which way up.
        ///
        /// This is the only place the three mechanics differ at all: the simulation sees one
        /// lock with a few right answers, and what the player is LOOKING at is dealt in
        /// <see cref="Puzzle.Shape"/>, which the simulation never reads.
        /// </summary>
        void Dress(int slot, int option, in Puzzle puzzle)
        {
            if (optionGlyphs.Length <= slot || optionGlyphs[slot] == null) return;
            Image g = optionGlyphs[slot];
            float turn = 0f;
            Sprite sprite = rune;

            switch (puzzle.Kind)
            {
                case PuzzleKind.OddOneOut:
                    // Four of the same rune and one of them turned. Which rune family the
                    // plate carries is the shape; the odd one is turned a quarter past it.
                    sprite = rune;
                    turn = puzzle.Shape * 15f;
                    if (option == puzzle.A) turn += 90f;
                    break;

                case PuzzleKind.Rings:
                    // Six options: each ring, each way. The arrow says which way it turns and
                    // the pair it sits in says which ring.
                    sprite = (option % 2) == 1 ? turnLeft : turnRight;
                    break;

                case PuzzleKind.Gears:
                    // Four gears. The ones in the chain stand out; the ones already off do
                    // not, which is the whole of what the player is counting.
                    sprite = gear;
                    bool engaged = (puzzle.Shape & (1 << (option - 1))) != 0;
                    g.color = engaged ? UiPalette.Ink : UiPalette.Fade(UiPalette.Ink, 0.3f);
                    break;
            }

            if (sprite != null) g.sprite = sprite;
            g.rectTransform.localRotation = Quaternion.Euler(0f, 0f, turn);
            if (puzzle.Kind != PuzzleKind.Gears) g.color = UiPalette.Ink;
        }

        static bool Used(in Puzzle puzzle, int done, int option)
        {
            for (int i = 0; i < puzzle.Needed; i++)
                if (puzzle.Wants(i) == option && (done & (1 << i)) != 0) return true;
            return false;
        }

        void Place(PlayerController fighter)
        {
            if (plate == null || canvasRect == null || view == null || fighter == null) return;

            Vector3 world = fighter.transform.position + Vector3.up * heightAboveFighter;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(view, world);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screen, null, out Vector2 local))
                return;

            // Kept on the sheet. A lock that opens half off the edge of the phone is a lock
            // the player has to walk the fighter sideways to finish.
            Rect sheet = canvasRect.rect;
            float halfW = plate.rect.width * 0.5f + margin;
            float halfH = plate.rect.height * 0.5f + margin;
            local.x = Mathf.Clamp(local.x, sheet.xMin + halfW, sheet.xMax - halfW);
            local.y = Mathf.Clamp(local.y, sheet.yMin + halfH, sheet.yMax - halfH);
            plate.anchoredPosition = local;
        }

        /// <summary>What a button on the lock calls. The option is 1-based (docs/04).</summary>
        public void Touch(int option)
        {
            if (_pad == null) Local();
            if (_pad != null) _pad.Touch(option);
        }
    }
}

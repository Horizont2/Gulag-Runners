using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GulagRunners.Meta;

namespace GulagRunners.Game
{
    /// <summary>
    /// Puts the catalogue into the shop's four rows, and lets a thumb reach the rest.
    ///
    /// THE SHOP WAS A MOCK-UP. ShopBuilder lays out four rows and says so in a comment --
    /// "built once and filled by whatever lists the catalogue at run time" -- and the thing
    /// that was supposed to fill them was never written. So every row read ASH / COMMON /
    /// 800 whatever category was open, nothing could be picked, and ShopUI.listChanged
    /// went nowhere, which is the shop not redrawing after a purchase. A screen that sells
    /// fifteen things and shows the same one four times.
    ///
    /// FOUR ROWS, AND THE LIST IS LONGER THAN FOUR. The builder's own comment says the
    /// widest category holds four and nothing is hidden; counted against the catalogue as
    /// it stands, Locks hold TWENTY -- five parts with four faces each -- so sixteen of
    /// them had nowhere to appear. Rather than build more rows, which is a screen that
    /// allocates while a thumb is moving, the four are a window onto the list and a drag
    /// moves the window. Every other category still fits in one screenful and never
    /// scrolls at all.
    ///
    /// A DRAG IS NOT A TAP, and on a list they start identically. The rule here is the
    /// usual one and it is worth stating: a press becomes a scroll the moment it has moved
    /// further than a finger wobbles, and a press that never does is a pick. Without it,
    /// every attempt to scroll buys something.
    ///
    /// It finds its own rows and attaches its own touch handling, for the same reason
    /// UiType and NetNotice do: the scene was built by a tool that predates this, and a
    /// screen that only works after somebody re-runs a menu item is a screen that is
    /// broken in the build.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopList : MonoBehaviour
    {
        [Tooltip("The shop this lists. Leave empty to find the one in the scene.")]
        public ShopUI shop;

        [Tooltip("The rows, top first. Leave empty and the children called Row 1..Row N " +
                 "are used, which is what ShopBuilder calls them.")]
        public RectTransform[] rows;

        [Tooltip("How far a press has to move before it is a scroll rather than a pick, " +
                 "in pixels of a 1080-wide screen. Below this a thumb is holding still " +
                 "and meaning to.")]
        [Range(4f, 60f)] public float dragStarts = 14f;

        [Tooltip("How far the list moves per row, in pixels. The rows are 132 tall with " +
                 "8 of air, which is what ShopBuilder spaces them at.")]
        [Range(40f, 400f)] public float rowPitch = 140f;

        readonly List<Cosmetic> _showing = new List<Cosmetic>();
        int _top;
        float _carried;              // drag distance not yet worth a whole row
        bool _scrolling;
        Vector2 _from;               // where the finger landed
        Vector2 _was;                // and where it was last frame

        /// <summary>
        /// Puts one on the shop in a scene that has not got one.
        ///
        /// The same floor as UiType and NetNotice, for the same reason: Menu.unity was
        /// built before this existed, and a shop that only lists its stock after somebody
        /// re-runs a menu item is a shop that is a mock-up in the build. ShopBuilder adds
        /// it too, and a scene that has one is left alone.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => Fit();
            Fit();
        }

        static void Fit()
        {
            if (FindFirstObjectByType<ShopList>(FindObjectsInactive.Include) != null) return;

            ShopUI shop = FindFirstObjectByType<ShopUI>(FindObjectsInactive.Include);
            if (shop == null) return;

            // Switched off while it is dressed: AddComponent wakes a behaviour then and
            // there, and this one looks for its rows in Awake.
            bool was = shop.gameObject.activeSelf;
            shop.gameObject.SetActive(false);
            ShopList list = shop.gameObject.AddComponent<ShopList>();
            list.shop = shop;
            shop.gameObject.SetActive(was);
        }

        void Awake()
        {
            if (shop == null) shop = FindFirstObjectByType<ShopUI>(FindObjectsInactive.Include);
            if (rows == null || rows.Length == 0) rows = FindRows();
            Arm();
        }

        void OnEnable()
        {
            if (shop != null) shop.listChanged.AddListener(Redraw);
            Redraw();
        }

        void OnDisable()
        {
            if (shop != null) shop.listChanged.RemoveListener(Redraw);
        }

        /// <summary>
        /// The rows ShopBuilder made, by the names it gave them.
        ///
        /// By name rather than by order, because the shop's root holds the title, the tab
        /// strip and the bottom bar as well, and "the first four children" is a rule that
        /// breaks the next time anything is added above them.
        /// </summary>
        RectTransform[] FindRows()
        {
            List<RectTransform> found = new List<RectTransform>();
            for (int i = 1; i <= 16; i++)
            {
                Transform row = Deep(transform, $"Row {i}");
                if (row == null) break;
                found.Add((RectTransform)row);
            }
            return found.ToArray();
        }

        static Transform Deep(Transform where, string name)
        {
            foreach (Transform t in where.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>Gives each row something that notices a finger on it.</summary>
        void Arm()
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] == null) continue;
                ShopRowTouch touch = rows[i].GetComponent<ShopRowTouch>();
                if (touch == null) touch = rows[i].gameObject.AddComponent<ShopRowTouch>();
                touch.list = this;
                touch.index = i;

                // A row with nothing raycastable on it cannot be pressed at all. The strip
                // is raycastTarget in the builder, but a scene built before that -- or one
                // where the sprite is missing -- would silently take no input.
                Image strip = rows[i].GetComponent<Image>();
                if (strip != null) strip.raycastTarget = true;
            }
        }

        // ------------------------------------------------------------------- the filling

        public void Redraw()
        {
            if (shop == null || rows == null) return;

            _showing.Clear();
            _showing.AddRange(shop.Showing());

            int most = Mathf.Max(0, _showing.Count - rows.Length);
            _top = Mathf.Clamp(_top, 0, most);

            for (int i = 0; i < rows.Length; i++)
            {
                RectTransform row = rows[i];
                if (row == null) continue;

                int at = _top + i;
                bool has = at >= 0 && at < _showing.Count;
                if (row.gameObject.activeSelf != has) row.gameObject.SetActive(has);
                if (!has) continue;

                Fill(row, _showing[at]);
            }
        }

        void Fill(RectTransform row, Cosmetic item)
        {
            bool owned = shop.progress != null
                         && Wardrobe.Owns(shop.catalogue, shop.progress.owned, item.id);
            bool picked = item.id == shop.picked;

            Write(row, "Name", item.shown);
            Write(row, "Kind", owned ? "OWNED" : item.tier.ToString().ToUpperInvariant());
            Write(row, "Price", owned ? "" : item.Cost.ToString("N0"));

            Color tier = UiPalette.Ramp()[Mathf.Clamp((int)item.tier + 1, 0, 3)];
            Tint(row, "Rarity", tier);
            Tint(row, "Kind", owned ? UiPalette.InkSoft : tier);

            // The picked row is the only one with a lit face, because a list where two
            // things look chosen is a list where nothing does.
            Image strip = row.GetComponent<Image>();
            if (strip != null)
                strip.color = picked ? UiPalette.Fade(UiPalette.Amber, 0.18f)
                                     : UiPalette.Fade(UiPalette.Ink, 0.06f);
        }

        void Write(RectTransform row, string child, string words)
        {
            Transform t = row.Find(child);
            if (t == null) return;
            TMP_Text text = t.GetComponent<TMP_Text>();
            if (text != null && text.text != words) text.text = words;
        }

        void Tint(RectTransform row, string child, Color colour)
        {
            Transform t = row.Find(child);
            if (t == null) return;

            Graphic g = t.GetComponent<Graphic>();
            if (g != null && g.color != colour) g.color = colour;
        }

        // -------------------------------------------------------------------- the finger

        /// <summary>A row was pressed and the finger never wandered.</summary>
        public void Tapped(int index)
        {
            if (_scrolling || shop == null) return;
            int at = _top + index;
            if (at < 0 || at >= _showing.Count) return;
            shop.Pick(_showing[at].id);
            Redraw();
        }

        /// <summary>A finger landed on a row.</summary>
        public void Pressed(Vector2 at)
        {
            _from = at;
            _was = at;
            _scrolling = false;
            _carried = 0f;
        }

        /// <summary>
        /// And moved. Positions only, never PointerEventData.delta or pressPosition:
        /// Input/ reads e.position and keeps its own start (see SwipeArea), and the
        /// reference assemblies this project compiles against do not carry the other two
        /// at all -- so the pattern that is already here is also the only one available.
        /// </summary>
        public void Dragged(Vector2 at)
        {
            if (rows == null || _showing.Count <= rows.Length) { _was = at; return; }

            // A press is a scroll once it has moved further than a thumb wobbles. Measured
            // from where it LANDED rather than frame to frame, so a slow drag counts as
            // much as a flick -- and so a hand that trembles on a tap does not scroll.
            if (!_scrolling && (at - _from).sqrMagnitude < dragStarts * dragStarts)
            {
                _was = at;
                return;
            }
            _scrolling = true;

            _carried += at.y - _was.y;
            _was = at;

            // Downward on the screen moves the window UP the list, which is the direction
            // paper moves under a finger.
            while (_carried >= rowPitch) { _carried -= rowPitch; Step(-1); }
            while (_carried <= -rowPitch) { _carried += rowPitch; Step(1); }
        }

        void Step(int by)
        {
            int most = Mathf.Max(0, _showing.Count - rows.Length);
            int want = Mathf.Clamp(_top + by, 0, most);
            if (want == _top) return;
            _top = want;
            Redraw();
        }

        /// <summary>
        /// One row's share of the touch handling: notices a press that stayed still.
        ///
        /// Its own component rather than a Button, because a Button swallows the press and
        /// the list above would never see the drag -- and a list that cannot be scrolled
        /// is the whole problem on a category of twenty.
        ///
        /// On the UP rather than on a click, because the click handler is what the rest of
        /// this project avoids as well: every control in Input/ is built on down and up
        /// (see VirtualButton), and a list whose rows answered to a different event than
        /// the buttons beside them would behave differently under the same thumb.
        /// </summary>
        public sealed class ShopRowTouch : MonoBehaviour,
            IPointerDownHandler, IPointerUpHandler, IDragHandler
        {
            [HideInInspector] public ShopList list;
            [HideInInspector] public int index;

            public void OnPointerDown(PointerEventData e)
            {
                if (list != null) list.Pressed(e.position);
            }

            // The drag is taken HERE and handed up, rather than being left to a handler on
            // the list. Unity delivers a pointer event to the first object up the
            // hierarchy that handles it, so a row that answers the press and not the drag
            // would leave the list with half a gesture.
            public void OnDrag(PointerEventData e)
            {
                if (list != null) list.Dragged(e.position);
            }

            public void OnPointerUp(PointerEventData e)
            {
                if (list != null) list.Tapped(index);
            }
        }
    }
}

// Stand-ins for two Unity PACKAGES, which ship no reference assemblies to compile against:
// the UI event interfaces and the Input System. Only the surface this project touches, and
// only so the rest of the project can be type-checked — nothing here runs.
namespace UnityEngine.EventSystems
{
    public class PointerEventData
    {
        public Vector2 position;
        public int pointerId;
        public Camera pressEventCamera;
        public Camera enterEventCamera;
    }

    public interface IPointerDownHandler { void OnPointerDown(PointerEventData eventData); }
    public interface IPointerUpHandler { void OnPointerUp(PointerEventData eventData); }
    public interface IDragHandler { void OnDrag(PointerEventData eventData); }
    public interface IBeginDragHandler { void OnBeginDrag(PointerEventData eventData); }
    public interface IEndDragHandler { void OnEndDrag(PointerEventData eventData); }

    /// <summary>The scene's one input router. Builders add it beside an EventSystem.</summary>
    public class BaseInputModule : MonoBehaviour { }

    public class EventSystem : MonoBehaviour
    {
        public GameObject firstSelectedGameObject;
        public static EventSystem current { get; set; }
    }
}

namespace UnityEngine.InputSystem
{
    public class ButtonControl { public bool isPressed; public bool wasPressedThisFrame; }

    public class Keyboard
    {
        public static Keyboard current;
        public ButtonControl aKey, bKey, cKey, dKey, eKey, fKey, gKey, hKey, iKey, jKey, kKey,
            lKey, mKey, nKey, oKey, pKey, qKey, rKey, sKey, tKey, uKey, vKey, wKey, xKey, yKey,
            zKey, spaceKey, enterKey, escapeKey, tabKey, backspaceKey,
            leftShiftKey, rightShiftKey, leftCtrlKey, rightCtrlKey, leftAltKey, rightAltKey,
            leftArrowKey, rightArrowKey, upArrowKey, downArrowKey,
            numpad0Key, numpad1Key, numpad2Key, numpad3Key, numpad4Key, numpad5Key,
            numpad6Key, numpad7Key, numpad8Key, numpad9Key, numpadEnterKey, numpadPeriodKey;
    }

    public class Mouse
    {
        public static Mouse current;
        public ButtonControl leftButton, rightButton, middleButton;
    }

    public class StickControl
    {
        public Vector2 ReadValue() => default;
        public ButtonControl up, down, left, right;
    }

    public class Gamepad
    {
        public static Gamepad current;
        public static System.Collections.Generic.IReadOnlyList<Gamepad> all =
            new System.Collections.Generic.List<Gamepad>();
        public StickControl leftStick, rightStick;
        public ButtonControl buttonSouth, buttonNorth, buttonEast, buttonWest,
            leftShoulder, rightShoulder, leftTrigger, rightTrigger, startButton, selectButton;
        public object dpad;
    }
}

// uGUI is a PACKAGE, not an engine module, so UnityEngine.Modules on NuGet does not carry
// it and the real Graphic is not available here. Only HudView touches uGUI, and it touches
// exactly one type: Graphic, the base of Image, RawImage, Text and TMP_Text alike, which is
// why one field can accept whichever of them the HUD is eventually drawn with.
//
// Stubbed to the surface that file uses and no further, which is the rule for everything in
// here: a stub that is larger than the code's real appetite stops being a check and starts
// being a second, quieter engine.
namespace UnityEngine
{
    /// <summary>
    /// Not in the NuGet reference assemblies, because Sprite belongs to the 2D module.
    /// Only what the builders read: the pixel rect, which is how they measure an export,
    /// and the border, which is the 9-slice.
    /// </summary>
    public class Sprite : Object
    {
        public Rect rect;
        public Vector4 border;
        public float pixelsPerUnit;
        public Texture2D texture;
    }
}

namespace UnityEngine.UI
{
    public class Graphic : MonoBehaviour
    {
        public Color color;
        public bool raycastTarget;
        public RectTransform rectTransform;
    }

    // Enough of each to type-check the casts HudView makes. It holds its references as
    // Graphic on purpose -- the base of Image, Text and TMP_Text alike -- and reaches for
    // the derived type only where it needs something Graphic does not have: the sprite on
    // a slot square, the string on the clock.
    public class Image : Graphic
    {
        public Sprite sprite;
        public bool preserveAspect;
        public Type type;
        public bool fillCenter;
        public float fillAmount;
        public float pixelsPerUnitMultiplier;

        public enum Type { Simple, Sliced, Tiled, Filled }
    }

    public class Text : Graphic
    {
        public string text;
        public int fontSize;
        public Font font;
        public FontStyle fontStyle;
        public TextAnchor alignment;
        public float lineSpacing;
        public HorizontalWrapMode horizontalOverflow;
        public VerticalWrapMode verticalOverflow;
        public bool supportRichText;
        public bool resizeTextForBestFit;
    }

    public class RawImage : Graphic { public Texture texture; }

    /// <summary>uGUI's own button. The menu and search screens wire theirs in the editor.</summary>
    public class Button : Selectable
    {
        public UnityEngine.Events.UnityEvent onClick = new UnityEngine.Events.UnityEvent();
    }

    public class Selectable : MonoBehaviour
    {
        public bool interactable;
        public Graphic targetGraphic;
    }

    public class CanvasScaler : MonoBehaviour
    {
        public ScaleMode uiScaleMode;
        public Vector2 referenceResolution;
        public ScreenMatchMode screenMatchMode;
        public float matchWidthOrHeight;
        public float referencePixelsPerUnit;

        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
    }

    public class GraphicRaycaster : MonoBehaviour { }
    public class Mask : MonoBehaviour { public bool showMaskGraphic; }

    public class LayoutGroup : MonoBehaviour
    {
        public RectOffset padding;
        public TextAnchor childAlignment;
    }

    public class HorizontalOrVerticalLayoutGroup : LayoutGroup
    {
        public float spacing;
        public bool childForceExpandWidth;
        public bool childForceExpandHeight;
        public bool childControlWidth;
        public bool childControlHeight;
    }

    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }

    public class LayoutElement : MonoBehaviour
    {
        public float minWidth, minHeight, preferredWidth, preferredHeight;
        public float flexibleWidth, flexibleHeight;
        public bool ignoreLayout;
    }

    public class ContentSizeFitter : MonoBehaviour
    {
        public FitMode horizontalFit;
        public FitMode verticalFit;

        public enum FitMode { Unconstrained, MinSize, PreferredSize }
    }
}

namespace UnityEngine.InputSystem.UI
{
    using UnityEngine.EventSystems;

    /// <summary>
    /// The Input System's event module. The project is on activeInputHandler 1, so this
    /// is the ONLY module that works -- the old StandaloneInputModule silently does
    /// nothing there -- and every builder in Assets/_Game/Editor adds this one.
    /// </summary>
    public class InputSystemUIInputModule : BaseInputModule { }
}

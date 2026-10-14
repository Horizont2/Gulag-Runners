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

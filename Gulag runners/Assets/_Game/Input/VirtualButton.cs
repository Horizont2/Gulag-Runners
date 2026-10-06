using UnityEngine;
using UnityEngine.EventSystems;

namespace GulagRunners.Game
{
    /// <summary>
    /// A held on-screen button. Unity's Button only fires on release, which is useless for
    /// jump, so this tracks the press itself.
    /// </summary>
    public sealed class VirtualButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        bool _pressed;

        /// <summary>True for every frame the finger is down.</summary>
        public bool Pressed => _pressed;

        public void OnPointerDown(PointerEventData e) => _pressed = true;
        public void OnPointerUp(PointerEventData e) => _pressed = false;

        void OnDisable() => _pressed = false;
    }
}

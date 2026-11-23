using UnityEngine;
using NomadUI.Core;

namespace NomadUI.Input
{
    /// <summary>
    /// Most (Adapter) pomiędzy dowolnym systemem wejścia (Input System) a serwerem UI.
    /// Oddziela on logikę wciskania klawiszy od samej nawigacji w UI.
    /// </summary>
    public class UIInputRouter : MonoBehaviour
    {
        private IUIService _uiService;

        /// <summary>
        /// Inicjalizacja routera, zazwyczaj z poziomu Bootstrappera.
        /// </summary>
        public void Initialize(IUIService uiService)
        {
            _uiService = uiService;
        }

        /// <summary>
        /// Tę metodę powinieneś podpiąć pod zdarzenie wciśnięcia ESC / Przycisku B na padzie.
        /// Np. w komponencie PlayerInput (Unity Event) lub z poziomu skryptu nasłuchującego.
        /// </summary>
        public void TriggerBack()
        {
            if (_uiService != null)
            {
                _uiService.Back();
            }
            else
            {
                Debug.LogWarning("[NomadUI] TriggerBack called, but UIInputRouter is not initialized with IUIService.");
            }
        }
    }
}

using UnityEngine;

namespace NomadUI.Core
{
    public abstract class UIView : MonoBehaviour
    {
        /// <summary>
        /// Wywoływane po utworzeniu instancji (przed Initialized).
        /// </summary>
        public virtual void Created() { }

        /// <summary>
        /// Wywoływane podczas wstrzykiwania danych i pierwszego setupu.
        /// </summary>
        public virtual void Initialized() { }

        /// <summary>
        /// Wywoływane, gdy panel ma się pojawić na ekranie.
        /// </summary>
        public virtual void Shown() { }

        /// <summary>
        /// Wywoływane, gdy panel zostaje ukryty, ale niekoniecznie zniszczony.
        /// </summary>
        public virtual void Hidden() { }

        /// <summary>
        /// Wywoływane bezpośrednio przed całkowitym usunięciem widoku z pamięci.
        /// </summary>
        public virtual void Dispose() { }
    }
}

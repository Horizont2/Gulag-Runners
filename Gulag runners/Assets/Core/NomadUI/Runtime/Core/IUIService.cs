namespace NomadUI.Core
{
    public interface IUIService
    {
        /// <summary>
        /// Otwiera widok, zgodnie z jego UIViewDefinition (Push, Replace, None, Modal, HUD).
        /// </summary>
        void Open<T>() where T : UIView;

        /// <summary>
        /// Jawnie zamyka konkretny widok (kończy jego sesję). CachePolicy decyduje co dalej (Hide czy Dispose).
        /// Nie mylić z Back().
        /// </summary>
        void Close<T>() where T : UIView;

        /// <summary>
        /// Semantyczna nawigacja wstecz (zamknięcie najwyższego modala lub pop ze stosu).
        /// Reakcja na klawisz ESC / Gamepad B.
        /// </summary>
        void Back();

        /// <summary>
        /// Informuje NomadUI o zmianie kontekstu gry (np. Hub -> Match).
        /// Powoduje czyszczenie widoków z CachePolicy.UntilContextChange.
        /// </summary>
        void ChangeContext(string contextId);
    }
}

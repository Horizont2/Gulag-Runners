using NomadUI.Core;

namespace NomadUI.Navigation
{
    public class NavigationEntry
    {
        public UIView View { get; }
        public NavigationPolicy Policy { get; }
        public object Metadata { get; }

        public NavigationEntry(UIView view, NavigationPolicy policy, object metadata = null)
        {
            View = view;
            Policy = policy;
            Metadata = metadata;
        }
    }
}

using System;
using NomadUI.Core;
using NomadUI.Layers;
using NomadUI.Navigation;

namespace NomadUI.Registry
{
    public class UIViewDefinition
    {
        public string Id { get; }
        public Type ViewType { get; }
        public string Address { get; }
        public UILayer Layer { get; }
        public NavigationPolicy Navigation { get; }
        public CachePolicy Cache { get; }

        public UIViewDefinition(string id, Type viewType, string address, UILayer layer, NavigationPolicy navigation, CachePolicy cache)
        {
            Id = id;
            ViewType = viewType;
            Address = address;
            Layer = layer;
            Navigation = navigation;
            Cache = cache;
        }
    }
}

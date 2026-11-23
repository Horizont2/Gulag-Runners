using System;
using System.Collections.Generic;

namespace NomadUI.Registry
{
    public class UIViewRegistry
    {
        private readonly Dictionary<Type, UIViewDefinition> _definitions = new Dictionary<Type, UIViewDefinition>();

        public void Register(UIViewDefinition definition)
        {
            if (_definitions.ContainsKey(definition.ViewType))
            {
                throw new InvalidOperationException($"View {definition.ViewType} is already registered.");
            }
            _definitions[definition.ViewType] = definition;
        }

        public UIViewDefinition Get<T>() where T : Core.UIView
        {
            return Get(typeof(T));
        }

        public UIViewDefinition Get(Type viewType)
        {
            if (!_definitions.TryGetValue(viewType, out var definition))
            {
                throw new KeyNotFoundException($"No UIViewDefinition found for type {viewType}. Did you register it?");
            }
            return definition;
        }
    }
}

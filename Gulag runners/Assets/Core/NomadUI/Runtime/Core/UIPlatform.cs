using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using NomadUI.Registry;
using NomadUI.Navigation;
using NomadUI.Loading;
using NomadUI.Layers;

namespace NomadUI.Core
{
    public class UIPlatform : IUIService
    {
        private readonly UIViewRegistry _registry;
        private readonly IUIViewLoader _loader;
        private readonly UINavigationStack _navigationStack;
        private readonly UILayerController _layerController;
        
        // Cache trzymające aktywne referencje do widoków
        private readonly Dictionary<Type, UIView> _activeViews = new Dictionary<Type, UIView>();
        
        private string _currentContext = "Global";

        public UIPlatform(UIViewRegistry registry, IUIViewLoader loader, UILayerController layerController)
        {
            _registry = registry;
            _loader = loader;
            _layerController = layerController;
            _navigationStack = new UINavigationStack(OnHideRequested);
        }

        public async void Open<T>() where T : UIView
        {
            Type viewType = typeof(T);
            UIViewDefinition def = _registry.Get(viewType);

            // Jeśli widok jest już otwarty (zcashowany i aktywny), ponowne otwarcie go może być zignorowane lub po prostu pokazane
            if (!_activeViews.TryGetValue(viewType, out UIView view))
            {
                view = await _loader.LoadAsync(def);
                if (view == null) return; // Fail gracefully
                
                _activeViews[viewType] = view;
                _layerController.SetParent(view, def.Layer);
                view.Created();
                view.Initialized();
            }

            view.gameObject.SetActive(true);
            view.Shown();

            // Obsługa stosu nawigacji w zależności od definicji
            if (def.Navigation == NavigationPolicy.Push)
            {
                _navigationStack.Push(new NavigationEntry(view, def.Navigation));
            }
            else if (def.Navigation == NavigationPolicy.Replace)
            {
                _navigationStack.Replace(new NavigationEntry(view, def.Navigation));
            }
        }

        public void Close<T>() where T : UIView
        {
            Type viewType = typeof(T);
            if (_activeViews.TryGetValue(viewType, out UIView view))
            {
                UIViewDefinition def = _registry.Get(viewType);
                PerformClose(view, def);
            }
        }

        public void Back()
        {
            if (_navigationStack.HasEntries)
            {
                NavigationEntry popped = _navigationStack.Pop();
                if (popped != null)
                {
                    UIViewDefinition def = _registry.Get(popped.View.GetType());
                    PerformClose(popped.View, def);
                }

                NavigationEntry next = _navigationStack.Peek();
                if (next != null)
                {
                    next.View.gameObject.SetActive(true);
                    next.View.Shown();
                }
            }
        }

        public void ChangeContext(string contextId)
        {
            _currentContext = contextId;
            
            // Czyszczenie widoków z CachePolicy = UntilContextChange
            List<Type> toRemove = new List<Type>();
            foreach (var kvp in _activeViews)
            {
                UIViewDefinition def = _registry.Get(kvp.Key);
                if (def.Cache == CachePolicy.UntilContextChange)
                {
                    kvp.Value.Hidden();
                    kvp.Value.Dispose();
                    _loader.Release(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var type in toRemove)
            {
                _activeViews.Remove(type);
            }
        }

        private void OnHideRequested(UIView view)
        {
            if (view != null && view.gameObject.activeSelf)
            {
                view.Hidden();
                view.gameObject.SetActive(false);
            }
        }

        private void PerformClose(UIView view, UIViewDefinition def)
        {
            OnHideRequested(view);

            if (def.Cache == CachePolicy.None)
            {
                _activeViews.Remove(view.GetType());
                view.Dispose();
                _loader.Release(view);
            }
        }
    }
}

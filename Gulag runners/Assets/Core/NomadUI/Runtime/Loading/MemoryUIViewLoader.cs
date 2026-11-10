using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using NomadUI.Core;
using NomadUI.Registry;

namespace NomadUI.Loading
{
    public class MemoryUIViewLoader : IUIViewLoader
    {
        private readonly Dictionary<string, UIView> _prefabMap = new Dictionary<string, UIView>();

        public void RegisterPrefab(string address, UIView prefab)
        {
            _prefabMap[address] = prefab;
        }

        public Task<UIView> LoadAsync(UIViewDefinition definition)
        {
            if (_prefabMap.TryGetValue(definition.Address, out UIView prefab))
            {
                UIView instance = UnityEngine.Object.Instantiate(prefab);
                return Task.FromResult(instance);
            }
            
            Debug.LogError($"[NomadUI] Failed to load view at address {definition.Address}. Not registered in MemoryUIViewLoader.");
            return Task.FromResult<UIView>(null);
        }

        public void Release(UIView view)
        {
            if (view != null)
            {
                UnityEngine.Object.Destroy(view.gameObject);
            }
        }
    }
}

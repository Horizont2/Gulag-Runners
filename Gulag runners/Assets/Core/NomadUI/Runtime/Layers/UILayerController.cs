using System.Collections.Generic;
using UnityEngine;

namespace NomadUI.Layers
{
    public class UILayerController
    {
        private readonly Dictionary<UILayer, Transform> _layerRoots = new Dictionary<UILayer, Transform>();

        public void RegisterLayer(UILayer layer, Transform root)
        {
            _layerRoots[layer] = root;
        }

        public void SetParent(Core.UIView view, UILayer layer)
        {
            if (_layerRoots.TryGetValue(layer, out var root))
            {
                view.transform.SetParent(root, false);
                // Reset transform
                view.transform.localPosition = Vector3.zero;
                view.transform.localScale = Vector3.one;
            }
            else
            {
                Debug.LogWarning($"[NomadUI] Layer {layer} is not registered in UILayerController.");
            }
        }
    }
}

using System;
using System.Threading.Tasks;
using NomadUI.Core;
using NomadUI.Registry;

namespace NomadUI.Loading
{
    public interface IUIViewLoader
    {
        Task<UIView> LoadAsync(UIViewDefinition definition);
        void Release(UIView view);
    }
}

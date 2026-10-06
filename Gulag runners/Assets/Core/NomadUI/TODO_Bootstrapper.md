# TODO: Skrypt Rozruchowy (Bootstrapper)

NomadUI składa się z klas C#, które nie dziedziczą po `MonoBehaviour` (np. `UIPlatform`, `UIViewRegistry`). Nie stworzą się one automatycznie po uruchomieniu gry w Unity.

Musisz stworzyć skrypt rozruchowy (tzw. Composition Root / Bootstrapper), który fizycznie połączy te klasy i przypisze je do odpowiednich warstw na scenie.

## Przykładowy skrypt do wrzucenia na główny Canvas (np. prefab NomadUIRoot):

```csharp
using UnityEngine;
using NomadUI.Core;
using NomadUI.Registry;
using NomadUI.Loading;
using NomadUI.Layers;

public class NomadUIBootstrapper : MonoBehaviour
{
    [Header("Przypisz obiekty ze sceny (Puste obiekty w Canvasie)")]
    public Transform screenLayer;
    public Transform modalLayer;
    public Transform overlayLayer;
    public Transform hudLayer;
    public Transform topLayer;

    // Globalny dostęp do interfejsu (Singleton).
    // Jeśli wolisz VContainer / Zenject, wystaw to przez wstrzykiwanie zależności.
    public static IUIService UI { get; private set; }

    void Start()
    {
        // 1. Tworzymy rdzeń
        var registry = new UIViewRegistry();
        var loader = new MemoryUIViewLoader();
        var layerController = new UILayerController();

        // 2. Podpinamy warstwy
        layerController.RegisterLayer(UILayer.Screen, screenLayer);
        layerController.RegisterLayer(UILayer.Modal, modalLayer);
        layerController.RegisterLayer(UILayer.Overlay, overlayLayer);
        layerController.RegisterLayer(UILayer.HUD, hudLayer);
        layerController.RegisterLayer(UILayer.Top, topLayer);

        // 3. TUTAJ W PRZYSZŁOŚCI BĘDZIEMY REJESTROWAĆ WIDOKI
        // registry.Register(new UIViewDefinition("Inv", typeof(InventoryView), "UI_Inventory", UILayer.Screen, NavigationPolicy.Push, CachePolicy.KeepAlive));

        // 4. Budujemy silnik
        UI = new UIPlatform(registry, loader, layerController);
    }
}
```

Dzięki temu jednemu skryptowi reszta gry może wywoływać `NomadUIBootstrapper.UI.Open<InventoryView>()`, a sam framework NomadUI pozostaje niezależny od logiki startowej sceny.

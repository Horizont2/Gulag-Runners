# Jak dodać nowy panel w systemie NomadUI (Walkthrough)

Ten poradnik opisuje krok po kroku, jak zaimplementować nowy widok (np. "InventoryView" lub "SpaceHamsterBreedingView") z zachowaniem zasad systemu NomadUI.

## KROK 1: Skrypt Widoku (Prezentacja)
Stwórz klasę dziedziczącą po `UIView`. Pamiętaj: ta klasa nie robi żadnej logiki biznesowej. Wystawia tylko metody do wypełnienia danymi i eventy (gdy gracz coś kliknie).

```csharp
using NomadUI.Core;
using UnityEngine;
using UnityEngine.UI;
using System;

public class MyNewView : UIView
{
    public Button closeButton;
    public event Action OnCloseClicked;

    public override void Initialized()
    {
        // Podpięcie przycisku do C# eventu (nie do UIPlatform.Back!)
        closeButton.onClick.AddListener(() => OnCloseClicked?.Invoke());
    }

    public void UpdateData(string someData)
    {
        // Aktualizacja np. tekstu
    }
}
```

## KROK 2: Stworzenie Prefaba (Unity)
1. W Unity, na Scenie stwórz odpowiedni panel UI.
2. Dodaj do niego skrypt `MyNewView`.
3. Podepnij referencje do przycisków w Inspektorze.
4. Zapisz obiekt jako Prefab w folderze projektu (np. w `Assets/Features/MyFeature/UI/MyNewView.prefab`).
5. Usuń obiekt ze Sceny.

## KROK 3: Rejestracja Widoku (Bootstrapper)
Każdy widok musi zostać przedstawiony rdzeniowi NomadUI. Robisz to w skrypcie rozruchowym (np. `NomadUIBootstrapper.cs`).

Jeśli używasz `MemoryUIViewLoader` (bez Addressables), najpierw musisz dodać prefab do loadera:
```csharp
// To zrobisz tylko dla MemoryLoadera. Jeśli wejdziesz w Addressables, wystarczy znać ścieżkę tekstową.
loader.RegisterPrefab("UI_MyNewView", myNewViewPrefabReference);
```

Następnie rejestrujesz definicję:
```csharp
registry.Register(new UIViewDefinition(
    id: "MyNewView", 
    viewType: typeof(MyNewView), 
    address: "UI_MyNewView", 
    layer: UILayer.Screen, 
    navigation: NavigationPolicy.Push, 
    cache: CachePolicy.KeepAlive
));
```

## KROK 4: Prezenter (Biznes Logika)
Tworzysz prezenter, czyli logikę danego feature'a, która zepnie ten "głupi" widok z danymi w grze (np. `GunsmithPresenter.cs`). Zazwyczaj ten kod wykonuje się po otwarciu ekranu.

W przypadku prostej architektury możesz po prostu otworzyć widok z dowolnego miejsca w grze:
```csharp
// Dowolny moment w grze
NomadUIBootstrapper.UI.Open<MyNewView>();
```

## PODSUMOWANIE
1. Tworzysz `Klasa : UIView`.
2. Zapisujesz Prefab.
3. Rejestrujesz `UIViewDefinition` z parametrami, w tym np. `NavigationPolicy.Push`.
4. Wywołujesz `UI.Open<Klasa>()` z Twojego kodu (System sam wrzuci go na warstwę Screen i doda na Stos Nawigacji!).

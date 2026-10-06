# 08 — Промти для генерації арен

Промти англійською (моделі працюють із нею точніше), коментарі українською.
Усі промти націлені на **концепт-арт цілої арени у вигляді збоку** — тобто саме те, що
художник/левел-дизайнер потім розбиває на 9 модулів.

---

## Як цим користуватись

### 1. Блок стилю (STYLE CORE) — вставляй у кожен промт

```
stylized 3D game art, Unity URP mobile render, hand-painted PBR textures,
chunky readable shapes, strong silhouette hierarchy, desaturated background
layers with saturated interactive props, soft rim light on gameplay layer,
baked lighting, clean edges, no visual noise in the central band,
AAA mobile production quality, concept art sheet
```

### 2. Блок камери (CAMERA CORE) — найважливіший, не змінюй

```
strict side elevation view, camera perpendicular to a single flat gameplay plane,
near-orthographic narrow FOV, side-scrolling platformer composition,
entire arena visible left to right in one frame, three stacked horizontal tiers
of platforms, parallax depth layers receding behind the play plane,
foreground elements only at the extreme left and right edges
```

### 3. Негативний промт

```
no text, no letters, no watermark, no UI, no HUD, no isometric view, no top-down,
no 3/4 view, no fisheye, no wide-angle distortion, no photorealism, no clutter
over the gameplay plane, no characters blocking the center, no tilted horizon,
no gore, no real-world insignia, no flags, no swastikas, no political symbols
```

### 4. Технічні параметри

| Ціль | Співвідношення | Нотатки |
|---|---|---|
| Вся арена (3 екрани) | `--ar 32:9` або `--ar 21:9` | головний формат для левел-дизайну |
| Один модуль 6×3 м | `--ar 2:1` | для набору модулів |
| Beauty shot для сторів | `--ar 16:9` | з бійцями в кадрі |

- **Midjourney:** додавай `--ar 32:9 --style raw --stylize 250`.
- **Flux / SDXL:** негативний промт окремим полем; `--ar` замінюй на роздільність (напр. 2048×576).
- Для серії консистентних арен: зафіксуй `--seed` і міняй **тільки біомний блок**.

---

## Біом 1 — Тайговий ліс / лісоповал  🌲

```
A side-scrolling 3D arena concept for a mobile 1v1 brawler: an abandoned northern
taiga logging camp at cold dawn. Three stacked tiers: muddy forest floor with tree
stumps and a shallow stream on the bottom tier, fallen giant pine trunks and a
collapsed timber walkway forming the middle tier, thick horizontal branches and a
rotting watchtower platform on the top tier. A rusted electrified chain-link fence
glows pale blue down the exact center of the arena, splitting it in two mirrored
halves. Wooden supply crates and a riveted steel safe with a round dial lock sit on
the platforms, each glowing faintly. Low ground fog, god rays through conifers,
dark green and grey-brown palette with one cold cyan accent from the fence.
[STYLE CORE] [CAMERA CORE] --ar 32:9
```

**Тюнінг:** `at cold dawn` → `in heavy rain` / `at dusk with orange sky` для варіантів освітлення.

---

## Біом 2 — Шахта / вапнякова печера  🕳️

```
A side-scrolling 3D arena concept for a mobile 1v1 brawler: a flooded limestone cave
crossed by an abandoned mine shaft. Three stacked tiers: black underground water with
a half-sunken mine cart on the bottom tier, broken wooden mine scaffolding and rail
tracks on narrow ledges forming the middle tier, stalactite shelves and a rusted ore
bucket on a cable on the top tier. Most of the cave is near-black; light comes only
from scattered hanging lanterns, glowing turquoise mineral veins, and a single shaft
of daylight from a collapsed ceiling in the center. An electrified barrier of blue
arcs runs down the exact center. Wooden crates and a crystal-lit lock-safe glow in
the darkness. Palette: near-black, wet grey limestone, turquoise and warm amber
lantern pools. High contrast, deep shadows, visible light volumes.
[STYLE CORE] [CAMERA CORE] --ar 32:9
```

**Тюнінг:** «реальна темрява» — фішка біому. Проси `large areas of unlit darkness between
pools of light`, інакше модель освітлює все.

---

## Біом 3 — Барачна зона / двір  ❄️

```
A side-scrolling 3D arena concept for a mobile 1v1 brawler: a snowbound prison camp
yard of a fictional industrial sector, night. Three stacked tiers: trampled snow and
frozen mud with overturned oil drums on the bottom tier, a long concrete loading dock
and the roofs of low wooden barracks forming the middle tier, a catwalk between two
timber guard towers on the top tier. Harsh white searchlight cones sweep from the
towers; coils of barbed wire, floodlight poles, blank corrugated walls. An electrified
fence of humming blue light cuts the exact center. Supply crates and a padlocked
steel locker glow against the snow. Deliberately open and exposed layout with very
few hiding spots. Palette: desaturated blue-white snow, black silhouettes, cold white
searchlights, one sodium-orange lamp. No real-world insignia or flags of any kind.
[STYLE CORE] [CAMERA CORE] --ar 32:9
```

---

## Біом 4 — Збагачувальний завод  ⚙️

```
A side-scrolling 3D arena concept for a mobile 1v1 brawler: the interior of a derelict
ore processing plant. Three stacked tiers: a flooded concrete floor with a running
conveyor belt and a huge hydraulic press on the bottom tier, steel grate catwalks and
rusted pipe bundles forming the middle tier, an overhead crane rail and the cab of a
magnet crane on the top tier. Steam vents hiss from broken pipes, sparks fall from a
dangling cable, dust hangs in the beams of broken skylights. An electrified barrier of
blue arcs runs down the exact center. Red gas cylinders, wooden crates, and a safe with
a glowing pipe-puzzle lock sit on the catwalks. Palette: rust orange, oxidized teal
metal, dirty yellow warning paint, sodium work lights.
[STYLE CORE] [CAMERA CORE] --ar 32:9
```

---

## Біом 5 — Затоплене болото / покинуте село  🌫️

```
A side-scrolling 3D arena concept for a mobile 1v1 brawler: a drowned village swallowed
by a peat swamp, overcast day. Three stacked tiers: brown-green stagnant water with
reeds and sucking mud pits on the bottom tier, rotting wooden boardwalks and the tilted
roof of a half-sunken izba forming the middle tier, a leaning telegraph pole line and a
rope bridge between dead trees on the top tier. Thick low mist, dead birch trunks,
floating debris, swarms of midges. An electrified fence glows cold blue down the exact
center. Mossy crates and a swollen wooden chest with a rope-and-pulley lock glow softly.
Palette: olive, peat brown, bone-grey wood, pale sky, one cyan fence accent.
[STYLE CORE] [CAMERA CORE] --ar 32:9
```

---

## Біом 6 — Мерзла залізнична станція  🚂

```
A side-scrolling 3D arena concept for a mobile 1v1 brawler: a frozen freight railway
depot at night, snow falling. Three stacked tiers: snow-covered rail tracks with a
working track that a locomotive periodically runs along on the bottom tier, the flat
roofs of frost-covered boxcars and a loading ramp forming the middle tier, a water
tower gantry and a signal bridge on the top tier. Frozen semaphore lights glow red and
green, icicles hang from every edge, headlamp glare from a distant locomotive. An
electrified barrier hums blue down the exact center. Crates lashed to flatcars and a
riveted strongbox with a gear-puzzle lock glow faintly. Palette: deep night blue, white
snow, black iron, red and green signal lights.
[STYLE CORE] [CAMERA CORE] --ar 32:9
```

---

## Біом 7 — Сірчаний кар'єр  🌋

```
A side-scrolling 3D arena concept for a mobile 1v1 brawler: an open sulfur quarry
baking in harsh midday heat. Three stacked tiers: cracked yellow terraces with boiling
mud pools and steam geysers venting upward on the bottom tier, stepped rock ledges and
rusted mining platforms on cables forming the middle tier, a wrecked excavator arm
bridging the gap on the top tier. Heat shimmer, drifting yellow haze, scorched black
mineral crusts, bleached bones of machinery. An electrified barrier glows blue down the
exact center. Crates and a heat-warped safe with a glowing valve-gauge lock. Palette:
sulfur yellow, charcoal black, bleached ochre, hot white sky, cyan fence accent.
[STYLE CORE] [CAMERA CORE] --ar 32:9
```

---

## Біом 8 — Бункер / тунель метро  🔩

```
A side-scrolling 3D arena concept for a mobile 1v1 brawler: a cramped abandoned
underground bunker and metro tunnel, emergency power only. Three stacked tiers: a
flooded tunnel floor with a dead rail car on the bottom tier, a concrete service
walkway and a row of blast doors forming the middle tier, a cable duct shelf and a
collapsed ventilation shaft on the top tier. Tight claustrophobic spacing, low
ceilings, dripping water, flickering green emergency strips, a single rotating amber
alarm light. An electrified barrier arcs down the exact center. Military crates and a
vault door with a glowing symbol-dial lock. Palette: wet grey concrete, institutional
green, amber alarm, cold cyan.
[STYLE CORE] [CAMERA CORE] --ar 32:9
```

---

## Додаткові типи промтів

### A. Лейаут-схема для левел-дизайну (greybox)

```
Top-priority readability layout diagram for a side-scrolling 1v1 arena: flat
near-orthographic side view, grey untextured blockout geometry, three stacked tiers of
platforms across a 3x3 module grid, clearly marked spawn points at far left and far
right, an electrified divider at the exact center, loot chest positions marked as
glowing colored cubes (white = tier 1, blue = tier 2, gold = tier 3), one hazard zone
highlighted in red, traversal routes drawn as thin white arrows. Clean technical
illustration, flat lighting, no decoration, no textures. --ar 32:9
```

### B. Набір модулів (kit sheet) — для продакшену

```
A modular asset kit sheet for a stylized 3D mobile game: nine separate 6x3 meter
side-view level modules of a [БІОМ] arena, arranged in a neat 3x3 grid on a flat
neutral background, each module self-contained with matching edge heights so any two
can snap together horizontally, consistent scale and lighting across all nine,
labeled with simple numbered tags. [STYLE CORE] --ar 1:1
```

### C. Beauty shot для сторів / трейлера

```
Dramatic key art for a mobile 1v1 brawler: side view of a [БІОМ] arena mid-fight. Two
stylized escapee fighters clash on the middle tier — one lit with cool cyan rim light
swinging a rusty pipe, the other with warm orange rim light throwing a brick. Impact
flash, debris, motion smears. A golden loot chest glows on the platform behind them.
Dynamic, high contrast, cinematic. [STYLE CORE] side view composition --ar 16:9
```

### D. Скрині й замки (пропси)

```
A prop sheet of 7 loot containers for a stylized 3D mobile game, side view, arranged in
a row on a neutral background, consistent scale and lighting: (1) small wooden supply
crate with white glow, (2) tall dented steel locker with blue glow, (3) riveted safe
with a glowing three-ring dial puzzle lock, (4) a suspicious crate with claw marks and
a faint red seam hinting it is a trap, (5) a padlocked chest with a large keyhole, (6) a
heavy golden central vault with a rotating beacon, (7) an air-dropped container with a
torn parachute. Hand-painted stylized PBR, chunky readable silhouettes, clearly
distinguishable from each other at thumbnail size. --ar 16:9
```

### E. Освітлювальні варіації одного біому

Зафіксуй сід і підміняй один рядок:
`at cold blue dawn with ground fog` · `in heavy grey rain, wet surfaces` ·
`at amber sunset with long shadows` · `at night lit only by searchlights` ·
`in a dust storm, low visibility`

---

## Чеклист: чи придатний згенерований арт

- [ ] Видно **всю арену** від краю до краю, камера перпендикулярна площині?
- [ ] Читаються **три яруси** платформ?
- [ ] Центральна смуга екрана **чиста** — там, де будуть бійці, нема шуму?
- [ ] Фон **десатурований** відносно переднього плану?
- [ ] Скрині **видно одразу**, вони світяться?
- [ ] Половини **однакові за складністю**, але різні за виглядом?
- [ ] Силуети платформ читаються, якщо зменшити картинку до 300 px?
- [ ] Нема реальної історичної символіки?

Якщо хоч один пункт «ні» — це арт для муд-борду, не для левел-дизайну.

# 08 — Промти для генерації арен

> **Оновлено під нотатки Miro:** стиль змінено з hand-painted PBR на **stylized low poly**
> (під безкоштовні пакети з дошки), а композицію — з пласкої смуги на **розріз із кімнатами**
> (бо гравці не мають бачити одне одного).

Промти англійською — моделі працюють із нею точніше. Коментарі українською.

---

## Як цим користуватись

### 1. STYLE CORE — вставляй у кожен промт

```
stylized low poly 3D game art, faceted flat-shaded geometry, flat saturated color
blocks with almost no texture detail, clean hard edges, chunky readable shapes,
strong silhouette hierarchy, desaturated background layers with saturated interactive
props, soft rim light on the gameplay layer, warm key light and cool bounce light,
baked lighting, simple gradient sky, mobile game concept art, Unity URP look
```

### 2. CAMERA CORE — найважливіший блок, не змінюй

```
side elevation cutaway view of a multi-floor structure, like a dollhouse cross-section,
camera perpendicular to a flat gameplay plane with a slight 12 degree downward tilt,
entire arena visible in one frame, three stacked floors connected by ladders hatches
and doorways, separate enclosed rooms that block line of sight between opposite ends,
parallax depth layers receding behind the play plane, foreground elements only at the
extreme left and right edges
```

> 🔑 Ключове слово — **cutaway / dollhouse cross-section**. Саме воно дає кімнати, в яких
> гравці одне одного не бачать. Без нього модель малює пласку смугу.

### 3. Негативний промт

```
no text, no letters, no watermark, no UI, no HUD, no isometric view, no top-down view,
no 3/4 perspective, no fisheye, no wide-angle distortion, no photorealism, no high
polygon detail, no realistic textures, no clutter over the gameplay plane, no characters
blocking the center, no tilted horizon, no gore, no real-world insignia, no flags,
no political symbols
```

### 4. Технічні параметри

| Ціль | Формат | Нотатки |
|---|---|---|
| Вся арена (3 крила × 3 поверхи) | `--ar 21:9` | головний формат для левел-дизайну |
| Одне крило | `--ar 3:2` | деталізація кімнат |
| Один модуль-кімната | `--ar 2:1` | для kit sheet |
| Beauty shot | `--ar 16:9` | з бійцями |

- **Midjourney:** `--ar 21:9 --style raw --stylize 200`. Low poly краще виходить при низькому stylize.
- **Flux / SDXL:** негативний промт окремим полем, `--ar` → роздільність (напр. 1792×768).
- Для консистентної серії біомів: **фіксуй seed, міняй тільки біомний блок**.

---

# Середньовічний режим

## Біом 1 — Тайговий лісоповал  🌲  ⭐ MVP

```
A side-view cutaway concept for a mobile 1v1 arena game: an abandoned northern taiga
logging camp built into a hillside, cold dawn. Bottom floor: a muddy dugout tunnel and
two iron holding cages at the far left and far right, their doors raised open. Middle
floor: enclosed log cabin rooms with doorways between them, a collapsed timber walkway,
a sawmill hall in the center with a tall vaulted ceiling and a glowing golden chest.
Top floor: open rooftops, thick horizontal pine branches, a rotting watchtower platform
under an open gradient sky. Ladders and hatches connect the floors. Wooden supply crates
glow white and a riveted safe with a rope-and-pin lock glows blue. Low ground fog, god
rays through conifers, dark green and grey-brown palette with one cold cyan accent.
[STYLE CORE] [CAMERA CORE] --ar 21:9
```

## Біом 2 — Шахта / вапнякова печера  🕳️

```
A side-view cutaway concept for a mobile 1v1 arena game: a limestone cave system crossed
by an abandoned mine. Bottom floor: black flooded water, a half-sunken mine cart, two
barred holding cages at the far ends. Middle floor: separate rock chambers linked by
narrow tunnels, broken wooden scaffolding, rail tracks, a central cathedral cavern with
a golden chest on a stone plinth. Top floor: stalactite shelves and an ore bucket on a
cable, with a shaft of pale daylight from a collapsed ceiling. Most of the cave is unlit —
large areas of pure darkness between small pools of light from hanging lanterns and
glowing turquoise mineral veins. Palette: near-black, wet grey limestone, turquoise,
warm amber lantern pools. High contrast, visible light volumes.
[STYLE CORE] [CAMERA CORE] --ar 21:9
```

## Біом 3 — Руїни фортеці  🏰

```
A side-view cutaway concept for a mobile 1v1 arena game: the ruined keep of a fictional
mountain fortress at overcast midday. Bottom floor: a vaulted dungeon with two iron
holding cages at the far ends, a flooded cistern, and a raisable portcullis in the middle.
Middle floor: separate stone chambers and a great hall in the center with a golden chest
on a dais, linked by spiral stairs and arrow-slit corridors. Top floor: broken battlements,
a collapsed tower, wooden hoardings, open sky with drifting clouds. Moss on cracked
granite, tattered blank banners with no symbols, scattered crates and a bound wooden
chest with a glowing rune-plate lock. Palette: cold grey stone, moss green, oak brown,
one gold accent.
[STYLE CORE] [CAMERA CORE] --ar 21:9
```

## Біом 4 — Затоплене болото / покинуте село  🌫️

```
A side-view cutaway concept for a mobile 1v1 arena game: a drowned village sunk into a
peat swamp, overcast and misty. Bottom floor: brown-green stagnant water, reeds, sucking
mud pits, two rusted cages half-submerged at the far ends. Middle floor: the interiors of
tilted wooden izba houses cut open in cross-section, rotting boardwalks connecting them,
a sunken chapel in the center holding a golden chest. Top floor: steep mossy roofs, a
rope bridge strung between dead birches, a leaning telegraph pole line, pale flat sky.
Thick low mist, floating debris, mossy crates and a swollen chest with a rope-and-pulley
lock. Palette: olive, peat brown, bone-grey wood, pale sky, one cyan accent.
[STYLE CORE] [CAMERA CORE] --ar 21:9
```

---

# Вогнепальний режим

## Біом 5 — Барачна зона / двір  ❄️

```
A side-view cutaway concept for a mobile 1v1 arena game: a snowbound camp of a fictional
industrial sector, night. Bottom floor: a service basement and boiler room with two steel
holding cells at the far ends, pipes along the walls. Middle floor: the cut-open interiors
of low wooden barracks — bunk rooms, a mess hall, a store room — linked by doorways, with
a long open assembly yard in the center holding a golden supply crate. Top floor: flat
snow-covered roofs and a catwalk between two timber guard towers, searchlight cones
sweeping the snow. Barbed wire coils, floodlight poles, corrugated walls. Palette:
desaturated blue-white snow, black silhouettes, cold white searchlights, one sodium
orange lamp. No real-world insignia or flags of any kind.
[STYLE CORE] [CAMERA CORE] --ar 21:9
```

## Біом 6 — Збагачувальний завод  ⚙️

```
A side-view cutaway concept for a mobile 1v1 arena game: a derelict ore processing plant
cut open in cross-section. Bottom floor: a flooded concrete sump with a running conveyor
belt, a huge hydraulic press, and two cage-like maintenance pens at the far ends. Middle
floor: separate machine rooms and control booths linked by steel grate catwalks and pipe
bundles, with a tall central silo hall holding a golden container. Top floor: an overhead
crane rail, the cab of a magnet crane, broken skylights letting in dusty god rays.
Hissing steam vents, sparks from a dangling cable, red gas cylinders, crates, and a safe
with a glowing pipe-puzzle lock. Palette: rust orange, oxidized teal metal, dirty yellow
warning paint, sodium work lights.
[STYLE CORE] [CAMERA CORE] --ar 21:9
```

## Біом 7 — Мерзла залізнична станція  🚂

```
A side-view cutaway concept for a mobile 1v1 arena game: a frozen freight railway depot
at night, snow falling. Bottom floor: snow-covered tracks with a live track running
through, an inspection pit, and two locked boxcars acting as cages at the far ends.
Middle floor: the cut-open interiors of frost-covered freight wagons and a brick station
building — a waiting room, a signal office — connected by loading ramps, with a central
depot hall holding a golden strongbox. Top floor: a water tower gantry, a signal bridge,
and the flat roofs of the wagons. Red and green semaphore lights, icicles on every edge,
distant locomotive headlamp glare. Palette: deep night blue, white snow, black iron,
red and green signal lights.
[STYLE CORE] [CAMERA CORE] --ar 21:9
```

## Біом 8 — Бункер / тунель метро  🔩

```
A side-view cutaway concept for a mobile 1v1 arena game: an abandoned underground bunker
joined to a metro tunnel, emergency power only, cut open in cross-section. Bottom floor:
a flooded tunnel with a dead rail car and two barred detention cells at the far ends.
Middle floor: a warren of small concrete rooms behind blast doors — a dormitory, a radio
room, a filter plant — linked by narrow service corridors, with a central command hall
holding a golden vault crate. Top floor: a cable duct shelf, a collapsed ventilation
shaft, and a maintenance crawlway. Cramped claustrophobic spacing, low ceilings, dripping
water, flickering green emergency strips, one rotating amber alarm light. Palette: wet
grey concrete, institutional green, amber alarm, cold cyan.
[STYLE CORE] [CAMERA CORE] --ar 21:9
```

---

# Інші типи промтів

## A. Greybox-схема для левел-дизайну

```
A technical layout diagram for a side-view 1v1 arena: flat near-orthographic cutaway
elevation, grey untextured blockout geometry, three stacked floors and three wings,
clearly separated enclosed rooms, ladders and hatches drawn as thin connectors, two
spawn cages marked at the far left and far right, a central hall in the middle, loot
chest positions marked as glowing colored cubes (white = tier 1, blue = tier 2,
gold = tier 3), one hazard zone highlighted in red, traversal routes drawn as thin white
arrows. Clean technical illustration, flat lighting, no decoration, no textures. --ar 21:9
```

## B. Kit sheet модулів-кімнат (для продакшену)

```
A modular asset kit sheet for a stylized low poly mobile game: nine separate side-view
room modules of a [БІОМ] arena, arranged in a neat 3x3 grid on a flat neutral background,
each room self-contained with matching doorway heights and floor levels so any two can
snap together, consistent scale and lighting across all nine, simple numbered tags.
[STYLE CORE] --ar 1:1
```

## C. Beauty shot для сторів / трейлера

```
Dramatic key art for a mobile 1v1 arena brawler: side view of two stylized low poly
escapees clashing inside a [БІОМ] room — one with a cool cyan rim light raising a wooden
shield, the other with a warm orange rim light swinging an axe. Impact flash, splintered
debris, motion smears. A golden chest glows on a ledge behind them, a doorway leads into
a dark unseen room. Dynamic, high contrast, cinematic, flat-shaded faceted geometry.
[STYLE CORE] side view composition --ar 16:9
```

## D. Скрині й замки (пропси)

```
A prop sheet of seven loot containers for a stylized low poly mobile game, side view,
in a row on a flat neutral background, consistent scale and lighting: (1) small wooden
crate with white glow, (2) tall dented steel locker with blue glow, (3) riveted safe
with a glowing three-ring dial puzzle lock, (4) a suspicious crate with claw marks and
a faint red seam hinting it is a trap, (5) a padlocked chest with a large keyhole,
(6) a heavy golden central vault with a rotating beacon, (7) an air-dropped supply cage
with a torn parachute. Faceted flat-shaded geometry, chunky silhouettes, each clearly
distinguishable from the others at thumbnail size. --ar 16:9
```

## E. Персонажі (під пакети з дошки)

```
Character sheet for a stylized low poly mobile arena game: two escapee fighters in torn
prison-issue clothing of a fictional sector, side view and three-quarter view, barehanded
idle pose. One has a cool cyan accent on his rags and a cyan rim light, the other a warm
orange accent and orange rim light. Simple faceted geometry, flat colors, no facial
detail, extremely readable silhouettes at small size, built for a mobile game at
17 percent of screen height. Neutral background, consistent scale. --ar 16:9
```

## F. Освітлювальні варіації одного біому

Фіксуй seed і підміняй один рядок:
`at cold blue dawn with ground fog` · `in heavy grey rain, wet surfaces` ·
`at amber sunset with long shadows` · `at night lit only by searchlights` ·
`in a dust storm, low visibility`

## G. Скайбокс (якщо пакетів з дошки не вистачить)

```
A seamless stylized low poly skybox panorama for a mobile game: flat gradient sky with
simple faceted clouds, [cold pale dawn / overcast grey / amber sunset / deep blue night],
no sun disc, no detail near the horizon line, soft color banding, painterly simplicity.
Equirectangular panorama --ar 2:1
```

---

## Чеклист: чи придатний згенерований арт

- [ ] Видно **всю арену** в одному кадрі, камера перпендикулярна площині?
- [ ] Читаються **три поверхи** і **окремі закриті кімнати** (а не одна пласка смуга)?
- [ ] Є **видимі з'єднання** — драбини, люки, двері?
- [ ] Чи справді з одного кінця **не видно** іншого? (інакше фог-ов-вор зламаний)
- [ ] Центральна смуга екрана **чиста** — там, де будуть бійці, нема шуму?
- [ ] Фон **десатурований** відносно переднього плану?
- [ ] Скрині **видно одразу**, вони світяться за тіром?
- [ ] Крила **однакові за складністю**, але різні за виглядом?
- [ ] Силуети читаються, якщо зменшити картинку до 300 px?
- [ ] Це **low poly**, а не реалізм із пласким фільтром?
- [ ] Нема реальної історичної символіки?

Якщо хоч один пункт «ні» — це арт для муд-борду, не для левел-дизайну.

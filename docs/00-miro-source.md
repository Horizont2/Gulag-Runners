# 00 — Вихідні нотатки з Miro (розшифровка)

Дошка `Gulag runner concept`. Нижче — дослівний зміст стікерів і що з них випливає.

## Стікери

**Game**
> 3d game for 2 players, camera from top/side, stylized low poly style

**Storytelling**
> We start in cages, doors start opening and 2 players, that dont see each other have to
> explore the map (arena) to find armor, melee or guns (in other more modern mode).
> Players have to kill each other to survive.

**Movement**
> We have 1 joystick, that controls movement (top/down/left/right), Button that control
> attacks and little button that controls jump.
> **Idea to add block button for more medieval mode, cuz its not necessery in gun mode**

**Cash management and ranking**
> We add skins for players to motivate them to earn gold and ranking system for more
> balanced fight, so professionals wont fight with starting players all the game

## Референси на дошці

| Референс | Що з нього беремо |
|---|---|
| 2D сайд-скрол шутер зі стік-менами, ящиками й трасерами («Refreshing Shooting Experience») | Підтверджує: **стрільба вздовж площини збоку** читабельна й працює |
| Стилізована low-poly арена серед літаючих руїн (2 кадри, широка камера) | Відкрите небо + скайбокс, low poly, фігури малі в кадрі |
| Мобільна покрокова RPG з щільним HUD | Референс **чого не робити**: такий HUD уб'є читабельність бою |

## Пакети Unity Asset Store, позначені на дошці

Безкоштовні: `32 RPG Animations`, `Stylized Skyboxes`, `Midgard Skybox Prime`,
`Low Poly FPS Weapons Lite`, `GenSa FREE Modular Character`, `Free Pack — Stick Man 3D
Characters`, `Lowpoly Mercenary Company`, `Low Poly Weapons VOL.1`, `Stylized Viking Characters`.

**Висновок:** команда прототипує на безкоштовних low-poly пакетах → арт-напрям мусить бути
**stylized low poly з пласкими кольорами**, а не hand-painted PBR. Усі промти оновлено
відповідно. Розкладку пакетів по етапах див. у [07-roadmap-and-risks.md](07-roadmap-and-risks.md).

## Що з нотаток суперечило першій версії концепту

| Нотатка з Miro | Було в першій версії | Як вирішено |
|---|---|---|
| «2 players that **dont see each other**» | Гравці бачать силует суперника крізь сітку | Прибрано. Замість зору — **драбина непрямої інформації** (звук → пік нашийника → промені замків → радар-імпульс). Див. [01](01-concept.md) |
| «We start **in cages**» | Електрична сітка посеред арени | Клітки і є старт. Сітка прибрана |
| «camera from **top/side**» | Жорстка ортографія збоку | **Гібрид: смугова арена** — бій завжди на одній площині збоку, але арена складається з кількох з'єднаних смуг/кімнат. Див. [02](02-combat-and-controls.md) |
| «block button for **medieval mode**», «not necessery in **gun mode**» | Один набір правил бою | **Два режими**: Середньовічний (блок/парирування) і Вогнепальний (нема блоку, є ухил). Різні лут-таблиці |
| «**armor**, melee or guns» | Броня була дрібною утилітою | Броня — **окремий слот** і окрема категорія луту |
| «earn **gold** and **ranking** system» | Тільки «косметика, без прогресу» | Додано економіку золота й ліги. Див. [09](09-economy-and-ranking.md) |
| «stylized **low poly**» | hand-painted PBR, AAA mobile | Увесь арт-напрям і всі промти переписані під low poly |

# 26 — Усі екрани UI і промти до них

> Повний набір. До цього промти жили в чаті, а в [23](23-menu-scene-and-ui.md) лежали
> тільки два — головне меню й HUD. Тут усі одинадцять, в одному стилі й з однаковим
> блоком негативу.

## Стиль — один на всі екрани

Фінальним прийнятий стиль **мокапу HUD**: тонкі лінії поверх гри, а не меблі. Він
скасовує ранню версію головного меню з дубовими дошками й залізними смугами — та версія
лишилась у [23](23-menu-scene-and-ui.md) як історія ітерації, в білд іде ця.

**Кожен промт нижче несе цей блок у собі**, бо генератор не пам'ятає попередній кадр:

| Правило | |
|---|---|
| Кадр | 20:9 ландшафт, усе важливе всередині центральних 16:9 |
| Сцена позаду | low poly, пласкі фасети, без текстур, холодний сірий камінь, тепле світло факела |
| Інтерфейс | тонкі лінії 2px, пласкі силуети, голі гліфи. Білий на 80% непрозорості |
| Чого нема | панелей, рамок, коробок, дерев'яних дошок, залізних заклепок, каменю під іконками, градієнтів, тіней |
| Відступ | 4% від кожного краю |
| Колір в UI | **тільки** рідкість (зелений / синій / фіолетовий, зі світінням), блакитний/помаранчевий гравців, і тепле золото — на числі золота |
| Цифри | у бою нуль. У меню можна |
| Домінанта | на кожному екрані рівно одна — і вона єдина залита, решта контурна |

Палітра рідкості — та сама, що в коді (`ItemModels.DefaultPalette()`):
**зелений common / синій rare / фіолетовий legendary**, і світиться тільки вона.

---

## 1. Головне меню

Екран, який бачать у 90% сесій. Домінанта — PLAY.

```
A stylized low poly medieval dungeon cell seen from inside, 20:9
ultrawide, all important content inside the central 16:9.

A single escaped prisoner stands in the centre of the frame, facing the
camera, IN FRONT of the bars and closer to the camera than any of them.
He fills 60% of the frame height, knees to head in the lower two thirds.
Torn linen shirt, scuffed leather jerkin, cloth wraps on the shins,
holding a medieval wooden club in his right hand, its tip resting on the
flagstones beside his boot. Weight on one leg, shoulders square, calm.

Camera low, at the figure's chest height, looking very slightly up.

Behind him a heavy shut iron portcullis in a stone arch. Beyond the
portcullis, pale and out of focus, a wide arena floor in cold fog.
Thick iron bars fill the left and right thirds of the frame, nearer the
camera than the arch but further than the figure.

A burning torch in an iron wall bracket on the LEFT wall at head height
is the key light ON THE FIGURE: warm amber from the front left. Cold
blue moonlight from a small barred window high on the right is the fill.

A wooden weapon rack with two spears against the right wall. A straw
pallet and two barrels against the left wall, a coil of rope on the
floor. Damp flagstones with shallow puddles reflecting the torch.
Dark foreground, pale background, three clear depth planes.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Every element is thin 2px white lines,
flat silhouettes and bare glyphs over the room, inset from every edge by
4% of the frame. Nothing overlaps the figure's head or chest.

ONE element dominates, bottom right: a PLAY button, a wide thin-lined
rectangle with softly rounded corners, and the ONLY filled shape in the
whole interface — filled with warm amber, the word PLAY cut out of the
fill in the stone colour. It is twice the height of anything else.
Directly under it, very small, thin grey letters: MEDIEVAL.

TOP LEFT, stacked, all thin white lines, no boxes:
  a small shield-shaped league glyph with the words SILVER III beside it;
  under them, a long thin horizontal progress bar, 60% filled, no number;
  under that, an even smaller line: SEASON 1, and a second, shorter bar
  with four small diamond milestones along it, two of them filled.

TOP RIGHT, stacked, right-aligned:
  a small coin glyph and the number 1 240, the coin and the digits in
  warm amber, the only other colour in the interface;
  under it, a small scroll glyph with 1/3 beside it in white.

BOTTOM LEFT, two small circles side by side, thin outlines, no fill:
  one with a gear glyph, one with a two-person glyph.

UNDER THE FIGURE, centred and small: the name RUNNER_7 in thin white
letters, inside a simple thin hexagonal outline that frames only the
text.

Palette: cold grey stone, warm amber torchlight and the one amber
button, white interface. Nothing else is coloured.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `wooden UI panels, stone UI frames, iron rivets, ornate borders, rounded
rectangle panels behind icons, news banners, promotional artwork, loot box, multiple
large buttons, glowing buttons, lens flare, text in the world`.

---

## 2. Магазин

Сітка 3×2, ціни в золоті, сезонна пропозиція згори. Нуль лутбоксів — ціна й усе.
Домінанта — сезонна пропозиція.

```
A stylized low poly medieval dungeon cell seen from inside, 20:9
ultrawide, all important content inside the central 16:9, the same room
as the main menu but with the figure moved to the LEFT third of the
frame and turned three quarters away, smaller, filling 45% of the frame
height, so the right two thirds are clear.

A burning torch in an iron wall bracket on the left wall is the key
light. Cold blue fill from a barred window. Low poly, flat faceted
shading, no textures, damp flagstones, dark foreground, pale background.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Thin 2px white lines, flat silhouettes
and bare glyphs, inset from every edge by 4%.

Across the RIGHT TWO THIRDS, a grid of six items, three across and two
down, each one a plain thin-lined square with a tiny flat monochrome
silhouette of the item centred in it and NOTHING else inside: a hooded
cloak, a curved sword, a raised fist, a hand print, a skull, a
hexagonal frame. Under each square, outside it, a small coin glyph and
a price in warm amber: 800, 1200, 400, 400, 3000, 600. One square, the
skull, has a thin diagonal line through its corner and the word OWNED
in tiny grey letters instead of a price.

ABOVE the grid, spanning its full width, ONE dominant element: a wide
thin-lined rectangle, the only FILLED shape on the screen, filled with
warm amber, holding the words SEASON OFFER cut out of the fill, a tiny
hourglass glyph, and 2 D 14 H in smaller cut-out letters at its right
end.

TOP RIGHT above everything: a small coin glyph and 1 240 in warm amber.
TOP LEFT: a small circle with a left-arrow glyph, thin outline, no fill.

Four small thin-lined tab labels sit in a row directly under the season
offer and above the grid, left aligned, no boxes around them: SKINS,
WEAPONS, EMOTES, FRAMES. The word SKINS has a short thin underline; the
other three do not.

Palette: cold grey stone, warm amber for prices and the one filled
banner, white interface. Nothing else is coloured.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `loot box, treasure chest opening, card pack, rarity gacha, percentages, odds
table, stat bars on items, +damage, +health, pay to win, wooden UI panels, stone UI
frames, ornate borders, glowing buttons, lens flare`.

> **Чому в негативі «stat bars on items»:** золото ніколи не купує силу (docs/09,
> червоне правило). Щойно в магазині з'явиться смужка стату — гра продає перевагу.

---

## 3. Щоденні завдання

Три на день, по 40 золота. Вони **вчать механікам**, тому формулювання — дієслова.
Домінанта — завдання, яке готове забрати.

```
A stylized low poly medieval dungeon cell, 20:9 ultrawide, all important
content inside the central 16:9, the same room as the main menu, the
prisoner standing far LEFT and small, filling 40% of the frame height,
turned away, so the centre and right are clear.

Torch key light from the left, cold blue fill from a barred window. Low
poly, flat faceted shading, no textures, dark foreground, pale
background.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Thin 2px white lines, flat silhouettes
and bare glyphs, inset from every edge by 4%.

Three rows stacked in the right two thirds, evenly spaced, each one a
single horizontal thin line with content sitting ON it, not inside a
box:
  ROW 1: a small padlock glyph, the words OPEN 5 SAFES WITH A PUZZLE, a
  short thin progress bar 60% filled, the figures 3/5, and a coin glyph
  with 40 in warm amber at the right end.
  ROW 2: a small fist glyph, the words WIN WITHOUT A TIER 3 WEAPON, a
  progress bar 0% filled, 0/1, and 40.
  ROW 3: a small spike-pit glyph, the words KILL WITH A HAZARD, a
  progress bar completely filled, 1/1, and instead of a price a wide
  thin-lined button at the right end — the ONLY filled shape on the
  screen — filled warm amber with the word CLAIM cut out of the fill.

ABOVE the three rows, small and left aligned, thin grey letters: DAILY
— RESETS IN 6 H 12 M.

TOP RIGHT: a small coin glyph and 1 240 in warm amber.
TOP LEFT: a small circle with a left-arrow glyph, thin outline, no fill.

Palette: cold grey stone, warm amber for rewards and the one CLAIM
button, white interface. Nothing else is coloured.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `calendar grid, login streak popup, chest rewards, spinning wheel, confetti,
wooden UI panels, stone UI frames, ornate borders, multiple filled buttons, glowing
buttons, lens flare`.

---

## 4. Сезон

Шість тижнів, смужка з віхами, два треки. Домінанта — поточна віха.

```
A stylized low poly medieval dungeon corridor seen from the side, 20:9
ultrawide, all important content inside the central 16:9. A long stone
passage receding to the right, lit by four torches in iron brackets at
even intervals along the left wall, each one smaller and dimmer than the
last, so the corridor reads as a distance to travel. Cold blue fog at
the far end. No figure in frame.

Low poly, flat faceted shading, no textures, damp flagstones, dark
foreground, pale background.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Thin 2px white lines, flat silhouettes
and bare glyphs, inset from every edge by 4%.

Running horizontally across the middle of the frame, a single long thin
white line with six small diamond nodes evenly spaced along it. The
first two diamonds are filled solid white, the third is filled warm
amber and twice the size of the others — the ONLY filled coloured shape
on the screen, the current milestone — and the last three are hollow
outlines. The line itself is solid white up to the amber diamond and
thin grey after it.

Under each diamond, a tiny flat monochrome silhouette of its reward: a
coin, a hooded cloak, a raised fist, a curved sword, a hand print, a
hexagonal frame. Under the amber one only, a small label: WEEK 3.

Directly BELOW that line, a second thinner line with the same six
positions, its nodes all hollow and slightly smaller, with a tiny key
glyph at its left end. A small grey label at the left of the upper line
reads FREE and at the left of the lower line reads PASS.

TOP LEFT: SEASON 1 in thin white letters, and under it, smaller and
grey: 23 DAYS LEFT.
TOP RIGHT: a small coin glyph and 1 240 in warm amber.
A small circle with a left-arrow glyph, thin outline, top left corner.

Palette: cold grey stone, warm amber torchlight and the one amber
milestone, white interface. Nothing else is coloured.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `battle pass tier grid, fifty numbered levels, premium upsell popup, price
tags, countdown banner, wooden UI panels, stone UI frames, ornate borders, glowing
buttons, lens flare`.

---

## 5. Профіль

Історія матчів, статистика, збережені сіди арен. Домінанта — остання ліга.

```
A stylized low poly medieval dungeon cell, 20:9 ultrawide, all important
content inside the central 16:9, the same room as the main menu. The
prisoner stands in the LEFT third, facing the camera, filling 55% of the
frame height, arms at his sides, no weapon.

Torch key light from the front left, cold blue fill from a barred
window. Low poly, flat faceted shading, no textures, dark foreground,
pale background.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Thin 2px white lines, flat silhouettes
and bare glyphs, inset from every edge by 4%.

UNDER THE FIGURE: the name RUNNER_7 in thin white letters inside a
simple thin hexagonal outline that frames only the text.

CENTRE, the one dominant element: a large thin-lined shield glyph,
filled warm amber, with the Roman numeral III cut out of the fill, and
beside it in thin white capitals SILVER III. Under it a long thin
progress bar 60% filled and, in small grey letters, PEAK: GOLD I.

RIGHT THIRD, upper: four short statistics, each one a number in white
above a tiny grey caption, in a row with no boxes and no lines between
them: 312 MATCHES, 54% WINS, 1 847 CHESTS, 2:04 BEST.

RIGHT THIRD, lower: a short list of five recent matches, each one a
single thin horizontal line with, from left to right, a small W or L
glyph (W a thin white circle, L a thin grey circle), an arena name in
small white letters — LOGGING SITE, FLOODED TERRACES, LOGGING SITE,
QUARRY, LOGGING SITE — a duration like 2:04, and at the right end a
small hollow heart glyph. One of the five hearts is filled white.

A tiny grey caption under the list reads SAVED SEEDS: 1.

TOP LEFT: a small circle with a left-arrow glyph, thin outline, no fill.
TOP RIGHT: a small coin glyph and 1 240 in warm amber.

Palette: cold grey stone, warm amber for the league shield and the gold
count, white interface. Nothing else is coloured.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `MMR number, elo rating number, leaderboard table, dense statistics grid,
graphs, charts, wooden UI panels, stone UI frames, ornate borders, glowing buttons,
lens flare`.

> **Чому в негативі «MMR number»:** ліга показується, MMR — ні (docs/09: приховане
> число знижує токсичність).

---

## 6. Налаштування

Обов'язковий список із [02](02-combat-and-controls.md): розмір і позиція кнопок,
ліворукість, вібровідгук, вимкнення тряски камери, **напрямний індикатор звуку**,
режим дальтонізму. Індикатор звуку — дефолт, а не опція, тож він стоїть УВІМКНЕНИМ.
Домінанта — редактор розкладки, бо це єдине налаштування, яке треба бачити, а не читати.

```
A stylized low poly medieval dungeon cell, 20:9 ultrawide, all important
content inside the central 16:9, the same room as the main menu, empty
of any figure, lit by one torch on the left wall and cold blue fill from
a barred window. Low poly, flat faceted shading, no textures, dark
foreground, pale background, the room deliberately quiet and out of
focus.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Thin 2px white lines, flat silhouettes
and bare glyphs, inset from every edge by 4%.

LEFT HALF, the dominant element: a live preview of the game's own touch
controls laid out over a dimmed, greyed-out miniature of a gameplay
frame — a small side-view dungeon cutaway with two tiny fighters. On it,
a thin-lined joystick ring at the lower left and a cluster of four
thin-lined circles at the lower right with crossed-swords, shield, open
hand and up-arrow glyphs. One of those circles, the crossed swords, is
FILLED warm amber and has four tiny corner handles on it, as if
selected and draggable. A small grey caption under the preview reads
DRAG TO MOVE, PINCH TO RESIZE.

RIGHT HALF, a column of settings, each one a single thin horizontal line
with a label on the left and its control on the right, no boxes:
  BUTTON SIZE        a thin slider line with a small circle at 70%
  LEFT HANDED        a thin pill outline with the knob at the left, off
  HAPTICS            a thin pill outline with the knob at the right, on
  CAMERA SHAKE       a thin pill outline with the knob at the left, off
  SOUND DIRECTION    a thin pill outline with the knob at the right, on
  COLOURBLIND        the word DEUTERAN in white with small left and
                     right arrow glyphs either side of it
  LANGUAGE           the word ENGLISH with the same arrows
  STICK SENSITIVITY  a thin slider line with a small circle at 55%

Beside the SOUND DIRECTION row only, a tiny thin white arc glyph, the
same arc the game draws in play, so the setting is recognisable from the
thing it controls.

TOP LEFT: a small circle with a left-arrow glyph, thin outline, no fill.
BOTTOM RIGHT, small and grey, not a button: ACCOUNT · RESTORE PURCHASES.

Palette: cold grey stone, warm amber for the one selected button in the
preview, white interface. Nothing else is coloured.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `long scrolling list, dense checkbox form, operating system settings look,
wooden UI panels, stone UI frames, ornate borders, glowing buttons, lens flare`.

---

## 7. Друзі й приватний матч

Список, запрошення, і **збережений сід** — приватний матч із другом на тій самій арені
(docs/09: дешева фіча з великою вірусністю). Домінанта — кнопка запрошення.

```
A stylized low poly medieval dungeon cell, 20:9 ultrawide, all important
content inside the central 16:9, the same room as the main menu. TWO
prisoners stand together in the LEFT third, side by side, three quarters
to the camera, each filling 45% of the frame height, one with a club and
one bare handed, both calm. Torch key light from the front left, cold
blue fill from a barred window.

Low poly, flat faceted shading, no textures, dark foreground, pale
background.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Thin 2px white lines, flat silhouettes
and bare glyphs, inset from every edge by 4%.

RIGHT TWO THIRDS, a list of four friends, each one a single thin
horizontal line with, from left to right: a small hexagonal outline with
one letter inside it, a name in white letters — BORYS, KATYA, MARKO,
STEFAN — a small round status dot, and at the right end a small
thin-lined circle with a crossed-swords glyph. The dots: the first two
filled white for online, the third hollow for offline, the fourth filled
white with the small grey word IN MATCH beside the name instead of a
challenge circle.

ABOVE the list, the one dominant element: a wide thin-lined rectangle,
the ONLY filled shape on the screen, filled warm amber, with the words
INVITE A FRIEND cut out of the fill and a small plus glyph at its left
end.

BELOW the list, separated by a thin grey rule, a single row: a small
heart glyph, the words LOGGING SITE #4821 in white, a tiny grey caption
SAVED ARENA under them, and at the right end a thin-lined circle with a
play-triangle glyph.

TOP LEFT: a small circle with a left-arrow glyph, thin outline, no fill.
TOP RIGHT: a small coin glyph and 1 240 in warm amber.

Palette: cold grey stone, warm amber for the one invite button and the
gold count, white interface. Nothing else is coloured.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `chat window, message bubbles, social feed, avatars with photographs, wooden
UI panels, stone UI frames, ornate borders, glowing buttons, lens flare`.

---

## 8. Завантаження і пошук суперника

Один екран на дві роботи: поки шукається суперник, він же і є завантаженням. Правила
пошуку з [09](09-economy-and-ranking.md) — ±100 MMR, кожні 5 секунд ширше, пінг важливіший
за точність після 20 секунд. Перші п'ять матчів — боти, **чесно підписані**. Домінанта —
піднята ґратчаста решітка: гравець дивиться туди, куди зараз піде.

```
A stylized low poly medieval scene, 20:9 ultrawide, all important
content inside the central 16:9: a heavy iron portcullis in a stone
arch, seen straight on from inside a dark cell, HALF RAISED, with cold
pale fog and an out-of-focus arena floor beyond it. The raised gate is
the brightest thing in the frame and sits dead centre.

A single prisoner stands in silhouette at the bottom centre, seen from
behind, very small, only his head and shoulders in frame against the
light, facing the gate.

A torch on the left wall throws warm amber across the near stone; the
light through the gate is cold blue. Low poly, flat faceted shading, no
textures, heavy depth: dark cell in the foreground, bright fog beyond.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Thin 2px white lines, flat silhouettes
and bare glyphs, inset from every edge by 4%.

ACROSS THE BOTTOM, centred, a single long very thin horizontal line, 40%
of it filled solid white from the left, the rest thin grey. No
percentage, no number.

ABOVE that line, centred and small: SEARCHING in thin white capitals,
and under it in smaller grey letters: SILVER III · 12 S.

TOP LEFT, small and left aligned, three short lines of thin grey text,
the kind of thing read once and never again:
  A SIREN AT 0:25 GIVES A DIRECTION, NEVER A POSITION.
with a tiny horn glyph at the start of the line.

TOP RIGHT, small: a thin-lined circle with an X glyph, no fill.

No other interface anywhere on the screen.

Palette: cold grey stone and cold blue fog, warm amber torchlight on the
near wall only, white interface. Nothing else is coloured.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `spinning loading wheel, percentage counter, versus screen with two portraits,
opponent name card, tip carousel with illustrations, progress bar with numbers, wooden
UI panels, ornate borders, glowing buttons, lens flare`.

> **Варіант для матчів 1–5** (боти): замінити `SEARCHING` на `TRAINING` і рядок під ним
> на `PRACTICE OPPONENT`. Підписувати чесно — вимога docs/09, і це не косметика: без
> підпису новачок вважає, що виграв у людини.

---

## 9. HUD бою

Фінальний, прийнятий. П'ять слотів спорядження, вісім елементів усього — стеля з
[02](02-combat-and-controls.md). Домінанти нема й бути не може: домінанта тут — сама гра.

```
A gameplay screenshot of a mobile 3D side-view fighting game, 20:9
ultrawide phone screen held horizontally, all important content inside
the central 16:9.

THE GAME FIRST. This is a real frame of play, not a UI illustration.

A cutaway through a stone dungeon, seen strictly from the side like a
doll's house with the front wall removed. Three floors stacked directly
above each other, connected by wooden ladders. Narrow perspective, about
a 30 degree field of view, camera tilted down by 12 degrees, so the
floors have thickness and nothing is distorted.

On the middle floor, two lean fighters face each other on the same flat
line, mid-exchange, about two metres apart. Each is 18% of the screen
height. The LEFT fighter is rimmed in cold cyan, barefoot, bare-headed,
swinging a wooden club. The RIGHT fighter is rimmed in warm orange, in a
mail coif and chainmail with a rope belt, guard raised. Both read as
clean silhouettes first: the weapon's shape alone says what it is.

The background is desaturated cold grey stone, slightly out of focus.
Everything interactive is saturated and glows faintly: a wooden crate on
the floor above, a dagger lying on the floor below in a soft GREEN halo,
a ladder in warm wood. Low poly, flat faceted shading, no textures, no
text anywhere in the world.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Every element is made of thin 2px
lines, flat silhouettes and bare glyphs over the game, inset from every
edge by 4% of the frame. Nothing overlaps the two fighters.

TOP EDGE, one quiet row:
  far left, a horizontal health bar in cold cyan, a thin unfilled
  outline behind it, no number;
  under it, four small cyan diamonds in a row, three lit, one dim:
  stamina charges;
  far right, the same mirrored in warm orange for the other fighter;
  dead centre, the round clock as thin white digits, 1:24, small, with
  no box around it.

LEFT EDGE, lower third: a floating virtual joystick, just two thin white
circles, a large ring and a small filled dot pushed to its upper right.
Barely there, 60% opacity.

RIGHT EDGE, lower third: four round buttons as thin white outlines, no
fill, no panel, arranged in a cluster for the right thumb:
  the largest, lowest right, a crossed-swords glyph: ATTACK;
  above it, a smaller circle with a shield glyph: BLOCK;
  to their left, a smaller circle with an open-hand glyph: ACTION;
  above that, the smallest circle with a single up arrow: JUMP.

LEFT EDGE, middle, the EQUIPMENT COLUMN: a small human figure drawn in
thin white line art only, a wire mannequin, no fill, no face, only 22%
of the screen height, standing upright. Five small square outlined slots
are placed around it, each joined to the body by a short thin leader
line:
  one above the head: a mail coif, outlined in BLUE;
  one at the right hand: a club, outlined in GREEN;
  one at the chest: chainmail, outlined in BLUE;
  one at the waist: a rope belt, outlined in GREEN;
  one just below the waist: an empty slot, a bare thin grey square
  outline with nothing in it.
Each filled slot holds a tiny flat icon of the item and nothing else:
no numbers, no names, no bars.

The rarity outline is the only coloured thing in the whole interface and
it GLOWS: a soft halo of its own colour bleeding 2 to 3 pixels outward
from the square, brightest on the blue slots, barely there on the green
ones, and none at all on the empty grey one. Green is a common find,
blue is rare, violet would be legendary. The glow is soft and even, not
a hard neon stroke, not a lens flare, and it does not light anything
around it.

LOWER LEFT, above the joystick: the sound direction indicator, a thin
white arc segment pointing up and to the left, fading at its ends, with
a small chest glyph at its tip: something was opened on the floor above,
off screen.

Palette: cold grey stone, cyan for player one, orange for player two,
warm wood only where the world is interactive. The interface is white at
80% opacity and nothing else.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `wooden UI panels, stone UI frames, iron rivets, ornate borders, rounded
rectangle panels behind icons, large portrait of the character, inventory grid, numbers
over health bars, damage numbers, item names, tooltips, minimap, isometric or
three-quarter camera, text in the world, neon glow on every UI element, glowing buttons,
glowing health bars, lens flare`.

---

## 10. Фазові сповіщення в бою

Матч має п'ять фаз ([17](17-match-round-start-and-end.md)), і про три з них треба
сказати. Це **не окремий екран** — це той самий HUD на три секунди. Ключове правило:
сповіщення не має власної панелі й не перекриває бійців, бо фаза міняється тоді, коли
гравець найменше може дозволити собі відвести очі.

```
A gameplay screenshot of a mobile 3D side-view fighting game, 20:9
ultrawide phone screen held horizontally, all important content inside
the central 16:9. The same cutaway stone dungeon as the main HUD, three
floors, wooden ladders, 30 degree field of view, camera tilted down 12
degrees.

THIS IS THE MOMENT THE SIREN SOUNDS, 25 SECONDS IN.

One lean fighter, rimmed in cold cyan, stands alone on the middle floor,
18% of the screen height, head turned to the right, caught mid-step. The
other fighter is NOT in frame. An opened wooden crate lies beside him
with its lid thrown back.

Every stone surface in the room is washed with a faint warm amber pulse
from the right hand side, as if a light had flared once off screen —
stronger on the right walls, almost nothing on the left. The background
is otherwise desaturated cold grey, slightly out of focus. Low poly,
flat faceted shading, no textures, no text anywhere in the world.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
Thin 2px white lines, flat silhouettes and bare glyphs. No panels, no
boxes, no frames behind anything. Nothing overlaps the fighter.

The usual HUD is present but quieter than normal, at 45% opacity: a cyan
health bar and four cyan diamonds top left, an orange bar top right, the
clock reading 0:25 dead centre, a thin joystick ring lower left, four
thin-outlined buttons lower right, and the five-slot equipment column
with its wire mannequin on the left edge.

ONE new element, and it is the only thing at full brightness: a wide
thin horizontal arrow glyph pointing RIGHT, drawn in white lines,
floating just above the fighter's head at a third of the screen width,
with two short motion lines trailing behind it. Under it, small white
capitals with no box: HE IS EAST. Under that, in smaller grey letters:
DIRECTION ONLY.

Nothing else is added. No full-screen flash, no vignette, no banner
across the frame.

Palette: cold grey stone, a single warm amber wash from the right, cyan
and orange on the bars, white interface.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `full screen banner, red alert overlay, screen-wide text, exclamation marks,
blood splatter vignette, minimap, enemy position marker, arrow pointing at an exact
spot, wooden UI panels, ornate borders, lens flare`.

> **Два інші сповіщення — той самий промт із заміненим центральним елементом:**
>
> * **Стулення (1:15).** Арена: крайні кімнати перекриті залізними заслінками, що
>   опускаються, ліва й права третини кадру темніші. Елемент: дві тонкі білі дужки,
>   що сходяться до центру, і під ними `THE WALLS ARE CLOSING`.
> * **Раптова смерть (2:20).** Арена: одна платформа, усе довкола — провал у туман,
>   обидва бійці в кадрі впритул. Елемент: тонке біле коло, вписане в центр кадру, і
>   `SUDDEN DEATH`, а на кнопці атаки — маленький гліф подвоєного кулака: кулаки б'ють
>   удвічі сильніше ([18](18-the-last-minute.md)).

---

## 11. Екран після матчу

Шість секунд, не більше — довший ламає цикл «ще раз» (docs/09). Домінанта — `AGAIN`.

```
A stylized low poly medieval scene, 20:9 ultrawide, all important
content inside the central 16:9: the camera has pushed in close on a
single stone platform suspended in cold fog, seen from the side. One
lean fighter stands at the centre of it, rimmed in cold cyan, chest
heaving, a wooden club hanging from his right hand, his head down. A
second fighter lies motionless at his feet, rimmed in warm orange, face
down.

Shallow depth: the platform and the two figures are sharp, everything
beyond is pale cold fog with the faint outlines of a collapsed arena.
One warm amber light from the upper left rakes across the standing
figure. Low poly, flat faceted shading, no textures, no text anywhere in
the world.

THE INTERFACE IS A THIN LIGHT OVERLAY, NOT FURNITURE.
No wooden planks, no iron straps, no stone slabs, no rounded panel
boxes, no frames behind anything. Thin 2px white lines, flat silhouettes
and bare glyphs, inset from every edge by 4%.

TOP CENTRE, large but still thin-lined and unfilled: the word VICTORY in
white capitals, letter-spaced wide, with a single thin horizontal rule
under it the same width as the word.

UPPER LEFT, a short column, each line thin and small:
  a coin glyph with + 25 in warm amber;
  a shield glyph with SILVER III, and beside it a thin progress bar 60%
  filled with a small white + 18 at its right end.

UPPER RIGHT, a short column, right aligned:
  LOGGING SITE #4821 in small white letters, with a hollow heart glyph
  beside it;
  under it, in grey: YOU 4 CHESTS · 2 BY PUZZLE — THEM 6.

BOTTOM RIGHT, the one dominant element: a wide thin-lined rectangle,
the ONLY filled shape on the screen, filled warm amber, the word AGAIN
cut out of the fill. To its LEFT and clearly smaller, a second
thin-lined rectangle with no fill at all, the word MENU in thin white
letters inside it.

BOTTOM LEFT, very small and grey: a thin horizontal line draining from
right to left, the six seconds running out. No digits.

Palette: cold grey stone and cold fog, warm amber for the reward and the
one button, cyan and orange on the two figures, white interface.

Crisp, readable at phone size, clean flat vector interface over low poly
3D, no photoreal materials, no drop shadows, no gradients in the UI.
```

Негатив: `rating screen with stars, MMR number, experience bar filling with particles,
confetti, fireworks, reward chest popup, advertisement, interstitial, share buttons,
wooden UI panels, ornate borders, glowing buttons, lens flare`.

> **Поразка** — той самий промт із трьома замінами: `VICTORY` → `DEFEAT`, боєць, що
> стоїть, рімлений помаранчевим, а лежить — блакитним, `+ 25` → `+ 10` (docs/09 платить
> за зіграний матч, а не лише за перемогу), `+ 18` → `− 14`. Кнопка `AGAIN` лишається
> тією самою домінантою — у цьому весь сенс екрана.

---

## Чого немає в наборі, і навмисно

| Екран | Чому його нема |
|---|---|
| **Екіпірування перед матчем** | обоє починають з нуля — перший стовп унікальності (docs/01). Екран «вибери стартовий ніж» ламає гру, навіть якщо ніж однаковий |
| **Лутбокс / відкриття скрині з косметикою** | docs/09: косметика — сітка з цінником, і все |
| **Новини, банери, івент-анонси** | цикл гри — «ще раз», а не «почитати» |
| **Реклама між матчами** | пряма заборона docs/09 |
| **Таблиця лідерів із MMR** | ліга показується, число — ні |
| **Вибір режиму як вкладка** | вогнепальний режим буде пізніше (docs/07); поки це рядок під PLAY, а не вкладка |

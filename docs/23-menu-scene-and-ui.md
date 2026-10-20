# 23 — Сцена меню і її UI

> Відповідь на «напиши декілька промтів сцени (разом з UI) і подумай що має бути на UI».
> Промти написані за шаблоном, який вижив після двох тестових рендерів у
> [08-arena-art-prompts.md](08-arena-art-prompts.md) — ті самі правила проти
> «намальованого концепт-арту» і проти об'єктів, що висять у повітрі.

## Головна ідея: меню — це клітка

Пітч документа 01 починається так: «Двоє прокидаються в клітках на різних краях арени.
Двері відчиняються.»

Отже меню не має бути абстрактним екраном з кнопками на фоні. **Меню — це той момент до
того, як двері відчинились.** Твій втікач стоїть у своїй клітці, за ґратами видно табір,
двері зачинені. Кнопка PLAY їх відчиняє — і це ж і є перехід у матч, без окремої
заставки.

Що це дає, крім настрою:

* **безкоштовний перехід** — камера їде за спиною втікача крізь двері, матч починається
  з тієї самої фази клітки, яку описує документ 17;
* **вітрина скінів задарма** — єдине, що продається за золото, це косметика (документ 09),
  і вона тут стоїть посеред екрана в зріст, освітлена, повільно обертається;
* **масштаб** — гравець бачить свого бійця поруч із ґратами й ящиками того ж розміру, що
  в матчі, і розуміє пропорції до того, як почне грати.

## Що має бути в UI

За частотою використання, а не за важливістю. 90% сесій — це «відкрив, натиснув PLAY».

### Перший екран

| Елемент | Де | Чому саме так |
|---|---|---|
| **PLAY** | правий нижній кут, найбільша річ на екрані | великий палець правої руки в ландшафті. Усе інше може бути меншим |
| Режим під кнопкою | дрібним під PLAY | документ 01 має два режими, вогнепальний — пізніше. Поки один рядок, не вкладка |
| **Ліга + прогрес** | лівий верхній | документ 09: ліги з MMR. Смужка до наступного рангу — єдине число, по якому гравець розуміє, нащо він грає |
| **Золото** | правий верхній | валюта зліва від кнопки магазину, завжди |
| **Щоденні ×3** | правий верхній, під золотом, значком зі лічильником `1/3` | 40 золота кожне й вони **вчать механікам** (документ 09). Якщо їх не видно — вони не вчать нікого |
| **Втікач** | центр, у повний зріст | він і є магазин |
| Нік + рамка профілю | під бійцем, дрібно | рамка — теж косметика |
| ⚙ Налаштування | лівий нижній, маленька | звук, мова, чутливість, керування |
| 👤 Друзі / приватний матч | лівий нижній поруч | «зберегти сід» з документа 09 живе тут |

### Друга глибина (окремі панелі, не окремі сцени)

* **Магазин** — скіни втікачів, скіни зброї, емоції, графіті, ефекти нокауту, рамки.
  Сітка 3×2, ціни в золоті. Сезонна пропозиція згори.
* **Сезон / бойова пропускна** — 6 тижнів (документ 09), смужка з віхами.
* **Профіль** — історія матчів, статистика, збережені сіди арен.

### Чого на першому екрані бути НЕ має

* **Новин і банерів на пів екрана.** Цикл цієї гри — «ще раз», а не «почитати».
* **Жодної реклами між матчами** — це пряма вимога документа 09.
* **Жодного лутбокса з предметами.** Косметика — сітка з цінником, і все.
* **Жодного екіпірування перед матчем.** Обоє починають з нуля — це перший зі стовпів
  унікальності. Екран «вибери стартовий ніж» ламає гру, навіть якщо ніж однаковий.

### Після матчу

Документ 09 уже задає цей екран і дає йому **6 секунд**, бо довший ламає «ще раз».
Кнопки `[ ЩЕ РАЗ ]` і `[ МЕНЮ ]`, і `ЩЕ РАЗ` — більша.

## Промти сцени

Три правила, без яких виходить не те:

1. **Блок стилю** — інакше модель малює концепт-арт, а не геометрію, яку можна повторити
   в Unity. Той самий блок, що вижив після двох тестових рендерів у документі 08.
2. **UI прямо в кадрі** — це **мокап**, а не фон. Без цього не видно, чи лишилося місце
   під кнопки і чи не губиться боєць за ними.
3. **Середньовіччя** — підземелля замку, а не табір ХХ століття. Це єдина епоха, що
   збігається з тим, чим б'ються: булава, спис, бойовий молот, кольчуга, лати.

```
STYLE BLOCK (до кожного):
stylized low poly, rendered in Blender, flat-shaded faceted geometry,
visible flat polygon facets, hard edges between facets, no smooth shading,
clean silhouettes readable at phone size, 16:9 landscape,
a 1.8 meter tall human for scale,
everything rests on a solid surface, nothing floats in mid-air

UI BLOCK (до кожного — це мокап):
game UI overlaid as flat vector shapes on top of the scene:
bottom right a large rounded rectangle button reading PLAY,
top left a small portrait frame with a horizontal progress bar beside it,
top right a coin icon with a short bar, and under it three small square
icons in a row, bottom left two small circular icons.
UI is flat, semi transparent, warm off-white on dark panels, no readable
paragraphs of text, labels shown as simple light bars.

NEGATIVE (до кожного):
painterly, digital painting, concept art, photorealistic, smooth shading,
noisy textures, garbled text, paragraphs of text, watermarks,
real-world flags, historical insignia, dates, military regalia,
modern objects, barbed wire, electric lights, gore
```

Останні два рядки негативу — не смак: документ 01 вимагає **нуль реальної історичної
символіки**, а «modern objects / barbed wire / electric lights» прибирають табір ХХ
століття, в який модель з'їжджає від слова «prison».

---

### Промт A — «Камера підземелля» (основний) — **v2, після тестового рендеру**

![тестовий рендер меню v1](refs/test-menu-v1.webp)

**Що вийшло і варто зберегти:** палітра й розподіл світла — теплий бурштин смолоскипа
проти холодного синього з вікна; фасетна геометрія читається як low poly, а не як
малюнок; ґрати обрамлюють центр і лишають кути вільними; калюжі з відображенням;
бочки й мотузка стоять на підлозі; **UI сів рівно туди, куди просили** — портрет і
смужки зліва вгорі, монета і три щоденні справа вгорі, дві круглі кнопки зліва внизу,
PLAY справа внизу, підписи смужками замість нечитабельного тексту.

**Що зламано і чим лікується:**

| Проблема | Причина | Рядок, що лікує |
|---|---|---|
| **Боєць замалий** — займає ~40% висоти, скін не роздивитись | нема вказівки на розмір у кадрі | `the figure fills 60% of the frame height, knees to head in the lower two thirds` |
| Стоїть **за ґратами**, у прорізі — читається як «чоловік у дверях» | ґрати описані і ліворуч-праворуч, і позаду | `the figure stands IN FRONT of the bars, closer to the camera than any bar` |
| **Пласке світло на фігурі**, він блідо-сірий, а не в бурштині | смолоскип описаний, але не сказано, що він ключове світло саме на бійця | `the torch is the key light ON THE FIGURE, warm amber from above and in front` |
| **Без зброї** — незрозуміло, у що це гра | не просили | `holding a medieval club in his right hand, tip resting on the floor` |
| Смолоскип **прямо над головою** і дрібний | нема позиції | `the torch bracket is on the left wall at head height, not above him` |
| **Порожня нижня третина** — суцільна підлога | камера зависоко | `camera low, at the figure's chest height` |
| **Мертві зони** ліворуч і праворуч від фігури | нема чим зайняти | `a wooden weapon rack against the right wall, a straw pallet on the left` |
| За ґратами **просто склепіння**, не видно, куди ведуть двері | фон описаний як інтер'єр | `beyond the gate a wide arena floor in pale fog, hinting at a larger space` |
| UI **тьмяний**, губиться на тлі | нема контрасту в описі | `UI panels are dark charcoal with a warm gold stroke, clearly readable` |
| Телефон — не 16:9, боки обріжуться | просили 16:9 | `20:9 ultrawide, all important content inside the central 16:9` |

---

#### Рендер v2 і що з ним не так

![тестовий рендер меню v2](refs/test-menu-v2.webp)

**Сцена готова.** Боєць великий і попереду ґрат, бурштин з лівого смолоскипа ліпить
його, за опускними ґратами блідий туман арени, стійка зі списами праворуч, солом'яник
і бочки ліворуч, калюжі, низька камера. Цю половину промта більше не чіпаємо.

**UI виглядає накладеним зверху** — і причина не в смаку, а в тому, що кожен елемент
оформлений однаково:

| Проблема | Чому так вийшло | Що робити |
|---|---|---|
| Усе в **однаковій золотій рамці** з однаковим обведенням і радіусом | у промті один опис панелі на всі елементи | різні матеріали для різних ролей: PLAY — дерево з залізом, валюта — латунь, решта — **без рамки взагалі** |
| **Нема ієрархії**: лічильник монет важить стільки ж, скільки PLAY | не сказано, що головне | `PLAY is the single heaviest element; every other element is quiet` |
| Блок зліва вгорі **завеликий**, конкурує з бійцем | дві довгі смужки + велика рамка | `compact, one short bar` |
| Елементи **в самих кутах**, як дебаг-оверлей | нема полів | `inset from every edge by 4% of the frame` |
| **Золото скрізь**, тож акценту не лишилось | один колір на все | золото **тільки** на PLAY і монеті |
| Іконки **нічого не кажуть про цю гру** | generic cog/shield/person | ковальський молот, шолом, монета з карбом |
| UI **нічим не пов'язаний зі сценою** | описаний як плаский оверлей | те саме фасетне затінення, **те саме світло смолоскипа**, тепла тінь збоку |

Головне з цього — останнє. Накладений вигляд лікується не «зробити гарніше», а тим,
щоб UI був **з того самого матеріалу і під тим самим світлом**, що й підземелля.

---

#### Повний промт v3 — той самий кадр, інший UI

```
A stylized low poly medieval dungeon cell seen from inside, 20:9 ultrawide,
all important content inside the central 16:9.

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

THE INTERFACE IS PART OF THE ROOM, NOT A LAYER ON TOP OF IT.
It is built from the same materials as the dungeon, shaded with the same
flat facets, and lit by the same torch from the front left, so every
piece carries a warm edge on its left side and a cool shadow on its right.
Inset from every edge by 4% of the frame. Nothing overlaps the figure.

One element dominates: bottom right, a PLAY button made of a thick oak
plank bound in black iron straps with riveted corners, the word PLAY
burned into the wood, lit by the torch. It is the single heaviest,
brightest and largest piece of interface in the frame.

Everything else is quiet and has NO panel or box behind it:
- top left, a small carved stone plaque with a portrait relief, and
  beside it one short horizontal progress bar inlaid like a groove in
  the stone. Compact, a quarter the size of the PLAY button.
- top right, a single aged brass coin with a struck design, and a short
  bar beside it. Under it, three small flat iron icons in a row with no
  frames: a blacksmith hammer, a helmet, a coin.
- bottom left, two small iron icons with no frames.

Colour discipline: warm off-white for every label and bar. ONE gold
accent, used only on the PLAY button and the coin, nowhere else.
All other interface is dark iron and weathered stone, nearly as dark as
the scene, readable only by its warm lit edge.
Labels as simple light bars, no readable paragraphs of text.
A soft dark gradient along the top and bottom edges of the frame instead
of panels, so the interface sits on shadow rather than on boxes.

stylized low poly, rendered in Blender, flat-shaded faceted geometry,
visible flat polygon facets, hard edges between facets, no smooth shading,
clean silhouettes readable at phone size,
everything rests on a solid surface, nothing floats in mid-air

--no painterly, digital painting, concept art, photorealistic, smooth
shading, noisy textures, garbled text, paragraphs of text, watermarks,
real-world flags, historical insignia, dates, military regalia, modern
objects, barbed wire, electric lights, gore, UI covering the character,
rounded rectangle panels, glassmorphism, drop shadows, uniform gold
outlines on every element, generic mobile game UI kit
```

Останні два рядки негативу — найважливіші в усьому промті. `rounded rectangle panels`
і `uniform gold outlines on every element` — це буквально те, що вийшло в v2, і модель
туди з'їжджає від самого слова «UI».

---

#### Повний промт v2 (попередній)

```
A stylized low poly medieval dungeon cell seen from inside, 20:9 ultrawide,
all important content inside the central 16:9.

A single escaped prisoner stands in the centre of the frame, facing the
camera, IN FRONT of the bars and closer to the camera than any of them.
He fills 60% of the frame height, knees to head in the lower two thirds.
Torn linen shirt, scuffed leather jerkin, cloth wraps on the shins,
holding a medieval wooden club in his right hand, its tip resting on the
flagstones beside his boot. Weight on one leg, shoulders square, calm.

Camera low, at the figure's chest height, looking very slightly up.

Behind him a heavy shut iron portcullis in a stone arch. Beyond the
portcullis, pale and out of focus, a wide arena floor in cold fog,
hinting at a much larger space than this cell.
Thick iron bars fill the left and right thirds of the frame, nearer the
camera than the arch but further than the figure.

A burning torch in an iron wall bracket on the LEFT wall at head height
is the key light ON THE FIGURE: warm amber from the front left, carving
his face and the club out of the dark. Cold blue moonlight from a small
barred window high on the right is the fill, and it never reaches him.

A wooden weapon rack with two spears against the right wall.
A straw pallet and two barrels against the left wall, a coil of rope on
the floor. Everything rests flat on damp flagstones with shallow puddles
that reflect the torch.

Dark foreground, pale background, strong value separation, three clear
depth planes: bars, figure, lit gate.

game UI overlaid as flat vector shapes on top of the scene:
bottom right a large rounded rectangle button reading PLAY in a gold
stroke, top left a square portrait frame with two horizontal progress
bars beside it, top right a coin icon with a short bar and under it
three small square icons in a row, bottom left two small circular icons.
UI panels are dark charcoal with a warm gold stroke, clearly readable
against the scene, no readable paragraphs of text, labels shown as
simple light bars. Nothing overlaps the figure.

stylized low poly, rendered in Blender, flat-shaded faceted geometry,
visible flat polygon facets, hard edges between facets, no smooth shading,
clean silhouettes readable at phone size,
everything rests on a solid surface, nothing floats in mid-air

--no painterly, digital painting, concept art, photorealistic, smooth
shading, noisy textures, garbled text, paragraphs of text, watermarks,
real-world flags, historical insignia, dates, military regalia, modern
objects, barbed wire, electric lights, gore, UI covering the character
```

---

### Промт A — «Камера підземелля» (v1, початковий)

```
A stylized low poly medieval dungeon cell seen from inside, wide landscape view.
Thick iron bars fill the left and right thirds of the frame; the centre is
open flagstone floor with a single escaped prisoner standing on it, facing
the camera, in torn linen and a leather jerkin.
Behind the bars, pale and out of focus: a vaulted stone undercroft with
round arches, a stone staircase, more cell doors along the far wall.
A heavy iron portcullis directly behind the figure, shut.
One burning torch in an iron bracket above him casting a warm amber cone.
Flagstones flat and horizontal, damp, reflecting the torch.
Two wooden barrels and a coil of rope against the left wall, resting on the floor.
Cool blue moonlight from a barred window high on the right, warm torchlight
on the figure. Dark foreground, pale background, strong value separation.
+ STYLE BLOCK
+ UI BLOCK
```

**Чому:** ґрати обрамлюють центр і лишають кути вільними рівно під UI; смолоскип робить
вітрину скіна; опускні ґрати за спиною — те, що відчиняє PLAY.

---

### Промт B — «Двір замку на світанку»

```
A stylized low poly medieval castle bailey at dawn, wide landscape view,
camera at ground level looking slightly up.
In the centre foreground a single escaped prisoner stands on flat wet
cobblestones, full body, facing the camera.
Behind: a stone keep with narrow arrow slits, a round tower on the left,
a timber palisade on the right, a row of shut iron cell doors along the
back wall, a raised portcullis in a stone arch.
Pale cold sky, low horizon, thin ground fog only behind the figure.
Wooden barrels, a hay cart and crates on the ground, each sitting flat.
Warm low sun from the right rim-lighting the figure; everything behind
desaturated grey-green.
+ STYLE BLOCK
+ UI BLOCK
```

**Чому:** більше світу і місце під широку нижню панель. Мінус — бійця видно гірше, ніж у A.

---

### Промт C — «Розріз вежі» (екран режиму / сезону)

```
A stylized low poly cross-section of a three storey medieval stone tower,
sliced open like a doll house, wide landscape view, seen from the side.
Three flat horizontal floors of flagstone connected by visible wooden
ladders and floor hatches.
Iron-bound chests and wooden crates stand on the floors, each resting flat.
Two 1.8 meter human silhouettes on different floors for scale.
Cold grey-green stone palette, pale fog behind the tower only.
Warm amber glow from the chests and from torches on the walls.
The left and right wings have the same number of rooms but different
shapes, not a mirror image.
+ STYLE BLOCK
+ UI BLOCK
```

**Чому:** та сама мова, що в документі 08, тож екран режиму — обіцянка того, що буде в
матчі. Для головного меню фокус на персонажі заслабкий.

---

### Промт D — «Силует» (запасний, найдешевший)

```
A stylized low poly medieval scene, wide landscape view, almost a silhouette.
One escaped prisoner standing in the centre on flat flagstones, lit only
from behind by a tall arched window, so the figure reads as a dark shape
with a bright rim.
Behind: a portcullis and a stone arch as flat dark shapes against pale mist.
Three values only: near black foreground, mid grey figure, pale grey background.
No detail anywhere except the figure's rim light and the torch flame.
+ STYLE BLOCK
+ UI BLOCK
```

**Чому:** якщо часу мало. Три площини й одне світло, працює на слабких телефонах і ніколи
не сперечається з UI.

## За що відповідає кожен елемент

![тестовий рендер меню v3](refs/test-menu-v3.webp)

Нижче — специфікація того, що на рендері v3. Для кожного: що це, що всередині, і чому
воно взагалі на першому екрані. «Чому» завжди має документ — екран меню не місце для
елементів, які просто гарно виглядають.

---

### 1. PLAY — дубова дошка, справа внизу

**Єдине, заради чого 90% гравців відкривають гру.**

* **Дія:** стає в чергу матчмейкінгу. Опускні ґрати за бійцем повзуть угору, камера їде
  вперед крізь них, завантаження ховається за цим рухом, і матч починається з фази
  клітки (документ 17).
* **Усередині дошки:** слово PLAY. Під час пошуку те саме місце показує таймер і
  `СКАСУВАТИ` — не окреме вікно: друга кнопка на екрані, де все вирішує одна, це вже дві
  кнопки.
* **Під дошкою, дрібно:** назва режиму. Документ 01 має два режими, вогнепальний —
  пізніше; поки це рядок, а не вкладка.
* **Чому так велико:** документ 09 ставить цикл «ще раз» в основу утримання. Усе інше на
  екрані може бути меншим.

### 2. Кам'яна табличка з портретом — зліва вгорі

**Хто ти і куди рухаєшся.**

* **Портрет:** твій втікач **у тому скіні, який на тобі зараз**. Це єдине місце, де
  косметика показана як обличчя, а не як товар.
* **Смужка 1 — ліга.** MMR і прогрес до наступного рангу (документ 09). Це **єдине
  число, яке пояснює, нащо грати далі**, бо золото не купує силу.
* **Смужка 2 — сезон.** Бойова пропускна, 6 тижнів (документ 09).
* **Дія:** відкриває **Профіль** — історія матчів, статистика, збережені сіди арен,
  сходинка ліги, і лічильник захисту новачка для перших 20 матчів (документ 09 називає
  це ключовим).
* **Розмір:** чверть від PLAY. Вона важлива, але її читають раз на сесію, а не щоразу.

### 3. Латунна монета — справа вгорі

**Скільки в тебе золота.**

* **Усередині:** число. **Не смужка** — смужка означає стелю, а в золота стелі нема; на
  рендері там смужка, і це єдине місце, де мокап бреше про механіку.
* **Дія:** відкриває **Крамницю**.
* **Чому вгорі справа:** валюта завжди поруч із тим, на що витрачається, і ніколи не
  поруч із PLAY, щоб їх не плутали пальцем.

### 4. Три залізні іконки — під монетою

На рендері це молот, шолом і монета, і саме тут іконка каже не те, що має робити кнопка.
Три абстрактні значки читаються як **три різні розділи**, а задумані були як
**три щоденні завдання**.

**Що пропоную замість:**

| | Було на рендері | Має бути | Що всередині |
|---|---|---|---|
| 1 | молот | **Щоденні** — сувій із бейджем `1/3` | три завдання по 40 золота, з галочкою на виконаних. Документ 09: вони **вчать механікам** («відкрий 5 сейфів загадкою», «виграй без зброї T3») |
| 2 | шолом | **Кузня** — молот | крамниця косметики: скіни втікачів, скіни зброї, емоції, графіті, ефекти нокауту, рамки профілю. Сітка 3×2, ціни в золоті |
| 3 | монета | **Сезон** — щит із номером | бойова пропускна: віхи, нагороди, скільки лишилось тижнів |

Три значки лишаються трьома, але кожен веде кудись, а не два з них дублюють сусідні
елементи. Монета під монетою — це та сама кнопка двічі.

### 5. Дві іконки — зліва внизу

На рендері — силует людини і щит. Теж не те.

| | Було | Має бути | Що всередині |
|---|---|---|---|
| 1 | силует людини | **Налаштування** — шестерня | звук окремо для музики й ефектів, мова, чутливість стіка, розкладка керування, обліковий запис |
| 2 | щит | **Друзі / приватний матч** — дві фігури | список друзів, запрошення, і **«зберегти сід»** з документа 09: приватний матч на тій самій арені, «дешева фіча з великою вірусністю» |

Силует людини для налаштувань і щит для друзів — рівно навпаки до того, що ці значки
означають усюди. Шестерня і дві фігури читаються без підпису.

---

### Чого тут свідомо нема

* **Екрана екіпірування.** Обоє починають з нуля — це перший зі стовпів унікальності
  (документ 01). «Вибери стартовий ніж» ламає гру, навіть якщо ніж однаковий у всіх.
  Шолом на рендері небезпечний саме цим: він обіцяє спорядження, якого не буде.
* **Новин і банерів.** Цикл — «ще раз», а не «почитати».
* **Реклами між матчами і лутбоксів з предметами** — пряма заборона документа 09.
* **Другої валюти на першому екрані.** Преміум існує (документ 09), але живе всередині
  крамниці, а не поруч із золотом: дві валюти в шапці — це вже магазин, а не гра.

### Скільки чого видно

| Елемент | Як часто торкаються | Розмір на екрані |
|---|---|---|
| PLAY | щоразу | найбільший, один такий |
| Щоденні | раз на сесію | дрібна іконка + бейдж |
| Крамниця / Кузня | раз на кілька сесій | дрібна іконка |
| Профіль / ліга | раз на сесію, поглядом | табличка, чверть від PLAY |
| Сезон | раз на кілька днів | дрібна іконка |
| Налаштування | раз на тиждень | найдрібніше |
| Друзі | рідко, але це віральність | найдрібніше |

## Туман у меню працює ТІЛЬКИ над терейном

Записано, бо на цьому вже один раз згорів цілий коміт.

`PureVolumetricFog.UpdateTerrainBounds()` будує весь свій об'єм із меж терейна і
закінчується рядком:

```csharp
if (!_hasTerrainBounds)
    return;
```

Немає терейна — **немає туману взагалі**. Не слабкого, не тьмяного: жодного. Помилки
теж немає, ефект просто мовчки нічого не робить, і це найгірший вид поломки.

Звідси три правила для сцени меню:

1. **Терейн у сцені обов'язковий і видаляти його не можна.** Він не залишок з арени, він
   ґрунт цього ефекту.
2. **`Find Active Terrains` лишається ввімкненим.** У списку `terrains` зараз один
   порожній слот `{fileID: 0}` — він виглядає як заповнений список, а не дає нічого, тож
   щойно авто-пошук вимкнуть, туман зникне.
3. **`Horizontal Padding` не можна зменшувати під розмір кімнати.** Об'єм це межі терейна
   **плюс** цей відступ, а терейн стоїть збоку від камери: з відступом 8 м кімната
   опиняється поза власним туманом. Тримати 40.

Ще дві цифри компонент тихо обрізає, тож у файлі може стояти одне, а працювати інше:
`targetStepLength` має `Min(2)`, `density` має `Range(0, 0.3)`.

`docs/refs/menu_check.py` перевіряє все це поза Unity — і зеленіє тільки тоді, коли туману
справді є з чого будуватись.

### Дрібниця щодо розміщення

Терейн стоїть у `(-7.57, -0.95, +1.74)`, а кімната займає `z -3.2 … +1.5`. Терейн в Unity
росте від свого початку у **+x і +z** і ніколи в мінус, тож **кімната лежить за його
краєм по z**. Туман до неї дістає лише завдяки відступу в 40 м, а висота ґрунтового шару
під кімнатою береться з краю терейна, а не з-під неї.

Працює, але `Ground Fog Height` при цьому означає не те, що написано. Якщо зсунути терейн
так, щоб кімната опинилась над ним (початок приблизно в `x -20, z -20`), число почне
значити свою висоту над підлогою камери.

## Повний розбір сцени меню

Зроблено за станом сцени, не на око: прочитані її налаштування, камера, світло, URP-ассети.

### Що вже добре

Кімната складена, пропорції правильні (4.0 × ~2.5 × 5 м), камера на висоті грудей,
туман має з чого будуватись, два світла стоять, реквізит на підлозі. Геометрія і
композиція — готові.

### Що знайдено, за силою впливу

**1. Пост-обробка була вимкнена. Це найбільша втрата з усіх.**

У камери стояло `m_RenderPostProcessing: 0`, а в сцені не було жодного Volume. Отже:

* **немає тонмапінгу** — а `m_SupportsHDR: 1`, тож усе, що яскравіше за 1, зрізалось у
  чистий білий. Це та сама причина, через яку раніше світіння рідкості виходило білим;
* **немає блуму** — полум'я смолоскипа і туман не світяться, а мають тверді краї. Факел
  без блуму в темній кімнаті виглядає як помаранчевий трикутник;
* немає віньєтки, немає корекції кольору, немає згладжування на жорстких low-poly краях.

Додано `Settings/Menu_PostProcess.asset` і `Global Volume` у сцені: Tonemapping
**Neutral**, Bloom (поріг 0.85, сила 0.9), Vignette 0.3, Color Adjustments
(+0.2 EV, +12 контрасту, +6 насиченості). На камері ввімкнено пост-обробку і FXAA.
Профіль **окремий** від арени, щоб не чіпати бій.

**2. На мобільному ассеті додаткові світла не кидають тіней.**

`Mobile_RPAsset`: `m_AdditionalLightShadowsSupported: 0`, `m_SoftShadowsSupported: 0`.
Тобто **смолоскип на телефоні не кидає жодної тіні** — а тінь від ґрат і є те, що робить
факел факелом. На `PC_RPAsset` вони ввімкнені, тож у редакторі все добре, а на пристрої
картинка інша.

Варіанти: увімкнути тіні додаткових світел на мобільному (коштує), або лишити як є і
покласти всю тіньову роботу на Directional, підкрутивши його напрямок так, щоб ґрати
кидали смуги.

**3. У кімнати немає стелі.**

Світло і небо ллються згори, і Directional конкурує зі смолоскипом. Темна стеля (бодай
площина) зробить факел головним джерелом, а це вся суть кадру.

**4. Камера дивиться вниз на 15°.**

`m_LocalRotation` дає нахил униз ≈14.9°, тоді як промт просив «ледь угору». Через це в
кадрі багато підлоги, і фігура здається нижчою.

**5. Терейн не під кімнатою.**

Деталі вище, у розділі про туман: кімната лежить за краєм терейна по z.

**6. Немає Reflection Probe.**

В інтер'єрі URP без пробу все дзеркальне відбиває скайбокс. Один бокс-проб на кімнату
прибере «пластикові» відблиски з каменю.

### Чого НЕ треба купувати: факел уже є

У проєкті **два** факели:

* `Dungeon Pack V.1.1/DungeonPack/Prefabs/Hanging Torch.prefab` — меш **плюс
  ParticleSystem**, тобто вже оживлений, готовий до перетягування;
* `Updated Modular Dungeon - May 2019/FBX/Torch.fbx` і `Woodfire.fbx` (Quaternius, CC0) —
  меші без вогню.

І три готові полум'я з Polyart: `PF_FireSmall`, `P_Fire`, `P_CandleFlame`.

Отже найкоротший шлях: взяти `Hanging Torch`, повісити на ліву стіну на висоті голови і
**зробити `Torch Key` його дитиною**, щоб світло й полум'я були одним об'єктом.

## Як збирати в Unity

Меню — **окрема сцена**, і її UI авторений у ній, а не створений у рантаймі: це та сама
вимога, що й для всього іншого в цьому проєкті (усе має бути видно в інспекторі).
Правило «рантайм UI не додавати» стосувалося HUD у матчі — меню це не скасовує, воно
просто мусить бути зібране руками в сцені.

Практично:

1. Сцена `Menu`, камера нерухома, один Canvas у Screen Space - Camera.
2. Втікач — той самий префаб, що в матчі, з тим самим `GR_Player` аніматором, але без
   `PlayerController`: тільки Animator на idle-клипі. Так скін одразу видно в тій самій
   позі, в якій його побачать у бою.
3. Фон — **геометрія**, не картинка: промт дає референс, а не ассет. Інакше він не
   працюватиме на інших співвідношеннях сторін телефонів.
4. Освітлення — те саме `SceneLighting`, що й в арені, щоб скін у меню і скін у бою були
   одного кольору. Скін, який у магазині яскравіший, ніж у грі, — це скарга у відгуках.
5. Перехід у матч: ворота з'їжджають, камера рушає вперед, завантаження ховається за цим
   рухом.

## Факели: що насправді було не так

Скарга була «факели просто як обʼєкти». Перше, що показав вимір, — вони **не** голі
палиці. `Torch.fbx` має **два** матеріали, `DarkMetal` і `Fire`, і другий — це конус
полумʼя з 24 полігонів заввишки 0.21 м у чашці. Вогонь у моделі вже був.

Бракувало трьох речей, і саме в такому порядку важливості:

1. **конус не світиться.** Матеріал `Fire` згенерований імпортером і лежить **усередині**
   `Torch.fbx`, тому редагувати його неможливо; і емісію не можна увімкнути ззовні —
   `_EMISSION` вшитий у матеріал, а `MaterialPropertyBlock` не вмикає ключові слова
   шейдера. Це вже коштувало одного коміту на світінні луту. Тому тул підставляє **наш**
   матеріал у слот `Fire`, а слот шукає **за назвою**, ніколи за індексом — індекс тут
   випадково 1, і типізований у тулі індекс — це баг, який чекає на переекспорт моделі.
2. **нічого не рухається.** `PF_FireSmall` з Polyart, масштаб 0.22 — приблизно розмір
   власного конуса факела; більше, і частинки зʼїдають сам факел.
3. **на стіну нічого не падає.** Вогонь без плями світла, що тремтить поруч, читається як
   наклейка.

### Чому світло отримують три факели з шести

Обидва pipeline-ассети тримають `m_AdditionalLightsPerObjectLimit: 4`. Шість світел на
факелах плюс `Gate Fill` — це сім, а понад ліміт URP **мовчки викидає зайві, по обʼєкту і
по кадру**, тож пʼяте світло в одній кімнаті виглядає не як пʼяте світло, а як мерехтіння
інших чотирьох. Три плюс `Gate Fill` — рівно чотири, і не викидається нічого.

Які саме три — виміряно, а не обрано. Правило: **у кадрі важливіше за поза кадром, далі
ближче до бійця важливіше за далі.** На камері меню (поз. `-0.498, 1.15, -2.22`, нахил
14.9° вниз, FOV 60 по вертикалі) з шести факелів у кадр потрапляють **лише два** — ті, що
біля арки, на `ndc x = ±0.72` при 16:9. Решта чотири стоять збоку й позаду камери: у
портреті в кадрі немає жодного, у ландшафті — тільки ці два. Третім іде `Torch (2)`:
1.94 м до бійця, найближчий з тих, що поза кадром, і **єдиний із трьох, що стоїть перед
ним** — тому саме він ключове світло, а не контровий обвід.

### `Torch Key` прибрано

Стенд-ін лампа висіла в повітрі за метр від найближчого факела і в сцені була
**вимкнена** (`m_IsActive: 0`) — тобто теплого світла в меню не було взагалі, звідси і
«сцена занадто темна». Тепер світло має джерело, і четверта тепла лампа все одно вивела б
підлогу за ліміт.

### Дрібниці, які коштували б вечора

* `PF_FireSmall` має **чотири** системи частинок на `scalingMode: Local` — кожна читає
  власний трансформ і **ігнорує батьківський**, тож масштабування кореня префаба розсуває
  шари, не зменшуючи жодного. Тул і сцена переводять усі чотири на `Hierarchy`, щоб одна
  ручка масштабу означала одну річ.
* Факели стоять з масштабом **40** (fbx у сантиметрах, виставлені на око), тому одна
  локальна одиниця під факелом — це сорок метрів. `Torch Fire` гасить це масштабом 1/40, і
  все під ним — у метрах. `TorchLight` ділить свій `drift` на `lossyScale` батька з тієї
  ж причини: без цього дрейф 0.015 кинув би світло на 0.6 м через кімнату.
* Обʼєкт, доданий **під префаб-інстанс**, має бути записаний у `m_AddedGameObjects` цього
  інстансу. Одного `m_Father` досить, щоб він малювався на місці, і недосить, щоб Unity
  його зберегла: при першій звірці префаба він опиниться в корені сцени. Без попередження.
  `scene_check` тепер це ловить.
* Модель Z-up: шахта факела йде вздовж локального **+Z**, а не +Y. Це видно з того, що всі
  інші пропи Quaternius у цій сцені поставлені з поворотом −90° по X.

### Що перевіряється

`menu_check` рахує додаткові світла проти ліміту пайплайна, звіряє кількість факелів із
кількістю вогнів на них і сварить за `Light` на `Torch Fire` без `TorchLight` — бо
нерухоме світло на рухомому полумʼї і є те, через що вони читалися як пропи.

На телефоні `Mobile_RPAsset` має `m_AdditionalLightShadowsSupported: 0`, тож мʼяка тінь
ключового факела — це прев'ю в редакторі й на ПК, не на пристрої.

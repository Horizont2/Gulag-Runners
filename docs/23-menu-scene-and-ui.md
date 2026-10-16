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

### Промт A — «Камера підземелля» (основний)

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

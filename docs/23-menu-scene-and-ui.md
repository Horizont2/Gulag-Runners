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

Спільні правила для всіх трьох (без них виходить 2D-малюнок, а не сцена, яку можна
зібрати в Unity):

```
STYLE BLOCK (додавати до кожного):
stylized low poly, rendered in Blender, flat-shaded faceted geometry,
visible flat polygon facets, hard edges between facets, no smooth shading,
no texture detail, clean silhouettes readable at phone size,
a 1.8 meter tall human silhouette for scale,
everything rests on a solid surface, nothing floats in mid-air

NEGATIVE (до кожного):
painterly, digital painting, concept art, photorealistic, smooth shading,
noisy textures, text, logos, watermarks, real-world flags, historical insignia,
dates, military regalia, gore
```

Останній рядок негативу — не стилістика, а вимога стору: документ 01 вимагає
**нуль реальної історичної символіки**.

---

### Промт A — «Клітка» (основний, рекомендую)

> Вертикальна композиція під бійця в центрі, місце під UI по кутах.

```
A stylized low poly holding cell seen from inside, wide landscape framing.
Heavy iron bars fill the left and right thirds of the frame; the centre is
open floor with a single escapee standing on it, facing the camera.
Behind the bars, out of focus and pale: a muddy prison yard, wooden
watchtowers, a chain fence, scaffolding, cold grey-green fog.
A shut sliding door directly behind the figure, lit from above by one
hanging lamp with a warm amber cone.
The floor is flat, horizontal, wet concrete with puddles reflecting the lamp.
Two wooden crates and a coil of rope against the left wall, resting on the floor.
Cool teal rim light from the yard, warm key light from the lamp above the figure.
Dark foreground, pale background, strong value separation.
+ STYLE BLOCK
```

**Чому саме це:** ґрати обрамлюють центр і лишають порожні кути під UI; тепле світло
згори робить вітрину скіна; холодний фон — глибину. Двері за спиною — те, що відчиняє
PLAY.

---

### Промт B — «Двір перед боєм»

> Якщо хочеться менше клаустрофобії і більше показати світ.

```
A stylized low poly prison yard at dawn, wide landscape framing,
camera at ground level looking slightly up.
In the centre foreground a single escapee stands on flat wet ground,
full body, facing the camera.
Behind: a long low barrack with a sloped roof, a wooden watchtower on the
left, a tall chain fence on the right, a row of shut cage doors along the back wall.
Pale cold sky, low horizon, thin ground fog only behind the figure, never in front.
Scattered crates and barrels on the ground, each sitting flat.
Warm low sun from the right rim-lighting the figure; everything behind is
desaturated grey-green.
+ STYLE BLOCK
```

**Чому:** дає місце під широку нижню панель (магазин/сезон) і читається як «перед
виходом». Мінус — бійця видно гірше, ніж у A.

---

### Промт C — «Розріз арени» (для екрана вибору режиму / сезону)

```
A stylized low poly cross-section of a three storey wooden structure,
sliced open like a doll house, wide landscape framing, seen from the side.
Three flat horizontal floors connected by visible ladders and floor hatches.
Small chests and crates stand on the floors, each resting flat.
Two 1.8 meter human silhouettes on different floors for scale.
Cold green-grey palette, pale fog behind the structure only.
Teal glowing outlines on the chests.
The left and right wings have the same number of rooms but different shapes,
not a mirror image.
+ STYLE BLOCK
```

**Чому:** це та сама мова, що в документі 08, тож екран режиму виглядає як обіцянка того,
що буде в матчі. Для головного меню — заслабкий фокус на персонажі, тому як фон другого
рівня.

---

### Промт D — «Силует» (запасний, найдешевший у виробництві)

```
A stylized low poly scene, wide landscape framing, almost a silhouette.
One escapee standing in the centre on flat ground, lit only from behind by
a single tall floodlight, so the figure reads as a dark shape with a bright rim.
Behind: a chain fence and a watchtower as flat dark shapes against pale fog.
Three values only: near black foreground, mid grey figure, pale grey background.
No detail anywhere except the figure's rim light.
+ STYLE BLOCK
```

**Чому:** якщо часу мало. Такий фон робиться з трьох площин і одного світла, працює на
слабких телефонах і ніколи не сперечається з UI.

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

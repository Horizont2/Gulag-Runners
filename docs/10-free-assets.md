# 10 — Безкоштовні асети під наші біоми

> Перевірено пошуком у жовтні 2026. Частину сайтів (kenney.nl, quaternius.com,
> kaylousberg.itch.io) не вдалося відкрити напряму з цієї сесії — мережевий проксі їх блокує,
> тому склад безкоштовних тірів треба **перевірити власноруч перед завантаженням**:
> автори час від часу переносять моделі між free та extra тірами.

## Головне рішення: один автор як база стилю

Найбільша помилка з безкоштовними асетами — зібрати локацію з п'яти різних паків і отримати
візуальну кашу: різна товщина фасок, різна сатурація, різний масштаб.

> **Бери один пак як базу стилю, решту — тільки на те, чого в базі нема.**

| Автор | Стиль | Чому | Ліцензія |
|---|---|---|---|
| ⭐ **KayKit** (Kay Lousberg) | чистий low poly, м'які фаски, насичена палітра | Найповніша екосистема під наш MVP: ліс + підземелля + персонажі зі зброєю, **все в одному стилі**, спільний атлас → мало дроколів | CC0 |
| **Kenney** | кубічніший, «іграшковий» | Єдиний, у кого є **модульна печера** й **конвеєрний кіт**. Безмежна кількість кітів | CC0 |
| **Quaternius** | плаский low poly, ближчий до Kenney | 70+ паків, анімовані персонажі, модульне підземелля й сай-фай | CC0 |
| **Synty POLYGON Starter** | «дорослий» low poly, той, що в референсах з Miro | Безкоштовний семпл великої серії; якщо потім купувати — стиль уже узгоджений | Unity Asset Store EULA |

**Рекомендація для MVP (Лісоповал + середньовічний режим): база — KayKit.**
Один стиль, CC0, FBX/GLTF/OBJ, спільний атлас, явно заявлена придатність для мобілок.

---

## Чесно: готових локацій під нашу арену не існує

Ми робимо **розріз із кімнатами у вигляді збоку**. Такого готового немає ніде — усі
безкоштовні «локації» це або відкриті 3D-сцени від першої/третьої особи, або 2D-тайлсети.

Що реально дають паки: **модульні блоки** — стіни, підлоги, сходи, драбини, двері, опори,
скелі, дерева. А наш арена-кіт із 9 кімнат ([05](05-arena-generation.md)) все одно збирається
руками в Unity. Це нормально: з модульного кіта такий розріз збирається за кілька годин,
бо з нього й було задумано збирати рівні.

---

## Розкладка по біомах

### Біом 1 — Тайговий лісоповал ⭐ MVP

| Пак | Що бере на себе | Ліц. |
|---|---|---|
| [KayKit — Forest Nature Pack](https://kaylousberg.itch.io/kaykit-forest) | **основа біому**: 100+ моделей у free тірі — дерева, каміння, кущі, трава; у extra ще й модульний рельєф | CC0 |
| [KayKit — Dungeon Remastered](https://kaylousberg.itch.io/kaykit-dungeon-remastered) | **200+ модульних** стін, підлог, сходів, дверей + **скрині, ящики, бочки, пастки** — це наші скрині й підземні дугаути | CC0 |
| [KayKit — Medieval Builder Pack](https://opengameart.org/content/kaykit-medieval-builder-pack-10) | дерев'яні балки, опори, паркани — лісоповал і платформи в кроні | CC0 |
| [Kenney — Nature Kit](https://opengameart.org/content/nature-kit) | пні, колоди, скелі, паркани, намети — добивання лісоповалу | CC0 |
| [Low Poly Nature Pack (svartskogen)](https://svartskogen.itch.io/low-poly-nature-pack) | 31 префаб у Lite, уже з LOD-ами під мобілки, .unitypackage під URP | CC0 |
| [Free Forest Kit (Asset Quest)](https://assetquest.itch.io/free-forest-kit) | запасний варіант дерев | CC0 |

### Біом 2 — Шахта / печера

| Пак | Що бере на себе | Ліц. |
|---|---|---|
| [Kenney — Modular Cave Kit](https://opengameart.org/content/modular-cave-kit) | **40 модульних сегментів печери** — практично єдиний безкоштовний модульний печерний кіт | CC0 |
| [Free Mine Assets Pack (rubberduck)](https://opengameart.org/content/free-mine-assets-pack) | **ідеальне влучання**: 35+ дерев'яних кріплень, сталактити, рейки, вагонетка, лампи, скриня | CC0 |
| [Modular Mines (Elbolilloduro)](https://elbolilloduro.itch.io/mine) | ще модульні шахтні ходи | CC0 |
| [Quaternius — Modular Dungeon Pack](https://quaternius.itch.io/lowpoly-modular-dungeon-pack) | 45+ модульних блоків для кімнат усередині породи | CC0 |

### Біом 3 — Руїни фортеці

| Пак | Ліц. |
|---|---|
| [KayKit — Dungeon Remastered](https://kaylousberg.itch.io/kaykit-dungeon-remastered) — основа | CC0 |
| [Kenney — Castle Kit](https://opengameart.org/content/castle-kit) — модульні мури, вежі, зубці | CC0 |
| [Low poly castle (Cosmo Art)](https://cosmo-art.itch.io/low-poly-castle) | CC0 |

### Біом 4 — Болото / затоплене село

| Пак | Ліц. |
|---|---|
| [KayKit — Forest Nature Pack](https://kaylousberg.itch.io/kaykit-forest) + [Kenney Nature Kit](https://opengameart.org/content/nature-kit) — рослинність | CC0 |
| [Swamp City (dencg)](https://dencg.itch.io/swamp-city) — болотна забудова | free / PWYW |

### Біом 5 — Барачна зона

| Пак | Ліц. |
|---|---|
| [Synty — POLYGON Starter Pack](https://assetstore.unity.com/packages/3d/props/polygon-starter-pack-156819) — пропси, персонажі, оточення | Unity EULA |
| [3D Retro Shacks (chilly_durango)](https://chilly-durango.itch.io/3dretroshacks) — сараї, бетонні платформи, **сітчастий паркан**, бочки, барикади | CC0 |

### Біом 6 — Збагачувальний завод

| Пак | Ліц. |
|---|---|
| [Kenney — Conveyor Kit](https://kenney-assets.itch.io/conveyor-kit) — **60+ моделей конвеєрів і заводського обладнання**, точно під наш хазард | CC0 |
| [Low Poly Industrial Pack — PolyWorks](https://vector-cmdr.itch.io/low-poly-industrial-pack-polyworks) | free / PWYW |
| [Modular lowpoly warehouse exterior](https://afghan-goat.itch.io/modular-lowpoly-warehouse-exterior) | free |

### Біом 7 — Мерзла залізнична станція

| Пак | Ліц. |
|---|---|
| [Low Poly Railway Pack (siris-pendrake)](https://siris-pendrake.itch.io/low-poly-railway-pack) — локомотиви, вагони, рейки, стрілки, семафори | перевірити |
| [Transit Cliffs — Low-Poly Train](https://dumivid.itch.io/transit-cliffs-low-poly-modern-train-and-mountain) | CC0 |
| [Low Poly Winter Pack (brokenvector)](https://brokenvector.itch.io/low-poly-winter-pack) — сніг і зимові пропси | free |

### Біом 8 — Бункер / метро

| Пак | Ліц. |
|---|---|
| [Quaternius — Modular Sci-Fi MegaKit](https://opengameart.org/content/lowpoly-modular-sci-fi-environments) — **270+ моделей**: стіни, двері, колони, пропси. Бетонний бункер із нього збирається напряму | CC0 |
| [KayKit — Dungeon Remastered](https://kaylousberg.itch.io/kaykit-dungeon-remastered) — коридори й блок-двері | CC0 |

---

## Персонажі та анімації

| Пак | Що дає | Ліц. |
|---|---|---|
| ⭐ [KayKit — Character Pack: Adventurers](https://kaylousberg.itch.io/) | 5 ріггед + анімованих персонажів і **25 одиниць зброї**. Той самий стиль, що й наші локації | CC0 |
| [Quaternius — Ultimate Animated Character Pack](https://opengameart.org/content/lowpoly-nature-pack) | 50+ анімованих персонажів — найбільший безкоштовний набір | CC0 |
| [Mixamo](https://www.mixamo.com/) | тисячі анімацій під будь-який humanoid-ріг; працює, але в режимі підтримки | free з акаунтом Adobe |
| `Free Pack — Stick Man 3D Characters` (з дошки Miro) | найдешевші боввани під **M0 greybox** | Unity EULA |
| `32 RPG Animations (FREE)` (з дошки Miro) | базовий мілі-мувсет під M0 | Unity EULA |

## Зброя

| Пак | Ліц. |
|---|---|
| [Quaternius — Medieval Weapons](https://quaternius.itch.io/lowpoly-medieval-weapons) — 22 одиниці | CC0 |
| [Low Poly Weapon Pack (Kickin It Studios)](https://kickin-it-studios.itch.io/low-poly-weapon-pack) — 37 одиниць | free |
| Зброя всередині KayKit Adventurers / Dungeon | CC0 |
| `Low Poly FPS Weapons Lite`, `Low Poly Weapons VOL.1` (з дошки Miro) — під вогнепальний режим | Unity EULA |

## Пошук окремих пропсів, коли в паках чогось бракує

- [Poly Pizza](https://poly.pizza/) — ~7000+ low-poly моделей, здебільшого CC0/CC BY, спадкоємець Google Poly
- [OpenGameArt](https://opengameart.org/) — великий архів CC0, саме там лежать паки Kenney і Quaternius
- [itch.io → Game assets → 3D](https://itch.io/game-assets/free/tag-3d) — найживіше джерело
- Sketchfab з фільтром CC0
- Unity Asset Store, розділ Free

---

## ⚠️ Ліцензії: три речі, які справді важливі

1. **CC0** — роби що хочеш, атрибуція не потрібна, можна комітити в репозиторій. Це KayKit,
   Kenney, Quaternius, більшість з OpenGameArt.
2. **CC BY** — те саме, **але вимагає згадки автора**. Трапляється на itch (напр. деякі
   замкові паки). Веди файл `CREDITS.md` із першого дня, інакше потім не згадаєш, що звідки.
3. **Unity Asset Store (навіть безкоштовні)** — працюють за Asset Store EULA: використовувати
   в грі можна, **перепоширювати не можна**.
   > 🔴 Тобто паки з Asset Store (Synty Starter, Stick Man, 32 RPG Animations) **не можна
   > комітити в публічний GitHub-репозиторій** — це перепоширення. Клади їх у `_ThirdParty/`
   > і додай у `.gitignore`, а в `CREDITS.md` запиши, що саме треба доставити вручну.

## Технічна вимога під наш бюджет

У [06-tech-and-netcode.md](06-tech-and-netcode.md) бюджет — **≤ 120 дроколів**. Його тримає
не полігонаж, а **кількість матеріалів**. Тому:

- бери паки зі **спільним атласом на весь пак** (KayKit, Synty, Quaternius — саме такі);
- усі модулі одного біому мусять ділити **один матеріал** → статичне батчування склеїть
  арену в кілька дроколів;
- якщо пак дає окремий матеріал на кожну модель — або переатласуй, або не бери.

## Що завантажити цього тижня під M0

1. [KayKit Dungeon Remastered](https://kaylousberg.itch.io/kaykit-dungeon-remastered) — модулі кімнат, драбини, двері, **скрині**
2. [KayKit Forest Nature Pack](https://kaylousberg.itch.io/kaykit-forest) — дерева, каміння
3. [KayKit Character Pack: Adventurers](https://kaylousberg.itch.io/) — персонажі з анімаціями та зброєю
4. [Kenney Modular Cave Kit](https://opengameart.org/content/modular-cave-kit) — під біом 2, наперед
5. [Quaternius Medieval Weapons](https://quaternius.itch.io/lowpoly-medieval-weapons) — зброя

Цього вистачає, щоб зібрати всю арену M0 **без жодної власної моделі**.

## Зброя і броня під бій (M0/M1, середньовічний режим)

Під таблицю з [15-combat.md](15-combat.md): кинджал, дрючок, меч, сокира, спис, кістень,
двуручна сокира + три тіри броні. Треба **силуети**, що читаються з 6 дюймів, а не полігони.

| Пак | Що дає | Чому саме він | Ліцензія |
|---|---|---|---|
| **Lowpoly Modular Armors — FREE PACK** (Polytope Studio) | 6 модульних сетів броні + **11 одиниць зброї** + 1 скриня + оточення | **Перший кандидат.** Єдиний безкоштовний пак, який закриває і броню, і зброю одним стилем. Броня **модульна** — саме те, що треба під три тіри на одному рігу. Mecanim/Mixamo-сумісний, URP. Без анімацій | Unity EULA |
| **Free Pack of Medieval Weapons** | мечі (одно- і дворучні), сокири, лук зі стрілами | Закриває прогалини по зброї, PBR, під мобілку | Unity EULA |
| **LOW POLY — Weapons Copper Pack** | кинджали, мечі, сокири, булави, **списи**, алебарди, **щити** | Єдиний зі списком, що майже один-в-один збігається з нашою таблицею. 150–350 трикутників | перевірити |
| **Modular Knights Character Set** (itch.io) | шоломи, броня, зброя на одному гуманоїдному рігу + 50 анімацій | Запасний варіант, якщо Polytope не ляже на стикмена | перевірити на itch |

**Порядок дій:** спершу Polytope FREE PACK — якщо його броня сідає на наш ріг, він закриває
обидві задачі одразу. Зброю добираємо з Copper Pack під конкретні силуети.

> **Увага щодо нашого персонажа.** Стикмен — не гуманоїдний ріг із пальцями, тож зброя кріпиться
> до кістки руки як простий дочірній об'єкт. `PlayerLoadoutView` уже це вміє: поклади модель під
> кістку, вимкни її, додай у список. Інспектор назве кожен предмет, у якого ще немає моделі.

---

# Платні локації в Asset Store

> Перевірено пошуком у жовтні 2026. Сам `assetstore.unity.com` із цієї сесії не відкривається
> (проксі ріже домен), тож склад і **ціни треба звірити на сторінці перед покупкою** — вони
> змінюються, і частина даних нижче з кешованих сніпетів, а не з живої сторінки.

## Спершу — метр. Інакше купувати нема сенсу

Поміряно по сцені:

| | у метрах | у зростах бійця |
|---|---|---|
| боєць | **0.91 м** | 1.0 |
| поверх (підлога → поміст) | 3.71 м | **4.1** |
| ширина драбини | 0.58 м | 0.64 |
| скриня | 0.70 × 0.50 м | 0.77 × 0.55 |
| уся арена | 32.5 м | 36 |

Для людини 1.80 м та сама арена читається **нормально**: поверх — 2.1 зросту (норма 1.7–1.9),
скриня — по коліно. Тобто **арена вже побудована в людському масштабі, а боєць — ні**: він
удвічі менший за все навколо.

Це не косметика. **Кожен пак в Asset Store зроблено під людину 1.8 м.** Купити локацію й
покласти в неї нашого бійця — це рівно та сама історія, що зі зброєю, тільки на весь рівень:
модуль стіни 2×2 м виявиться вдвічі вищим за бійця, двері — втричі.

Два виходи, і вони не рівноцінні:

1. **Підняти бійця до ~1.8 м** і перетюнити `MoveConfig` (висота тіла, швидкості, стрибок,
   гравітація — усе там, у метрах). Арена лишається як є, бо вона вже в цьому масштабі. Після
   цього **будь-який пак зі стора встає в рідному масштабі**. Разова робота, і вона відкриває
   весь магазин.
2. Масштабувати кожну куплену локацію на ~0.5. Дешево на вигляд і дорого потім: модульна сітка
   2 м стає 1 м, усі пропорції пака ламаються, і кожен наступний пак доведеться масштабувати
   знову.

**Рекомендація: (1).** Це єдина річ, яку варто зробити **до** покупки.

## Готової локації під нас не існує — і ось чому

Ми дивимось на рівень **збоку, у розрізі**: камера на z −9 дивиться в +Z з полем 30°. Майже
весь енвайрмент у сторі зроблено під вид від першої/третьої особи — кімнати це закриті
коробки, і з розрізу ти дивишся на **зворотний бік стіни**.

Пошук це підтвердив: усе, що знаходиться за запитом «side scroller environment», — це **2D
спрайтові** тайлсети, а `Corgi Engine` — це контролер, а не арт (і нам не потрібен: у нас свій
детермінований мотор).

> Тож критерій покупки не «локація», а **модульний кіт на сітці**: окремі стіни, підлоги,
> сходи, драбини, двері. З нього розріз збирається просто тим, що **ближню стіну не ставлять**.

## Короткий список

| | Пак | Чому саме він | Застереження |
|---|---|---|---|
| ⭐ 1 | [**STYL-Underground Prison**](https://assetstore.unity.com/packages/3d/environments/dungeons/styl-underground-prison-stylized-modular-dungeon-kit-312229) | Найточніше влучання в тему. Модульна сітка **2×2 м**: 21 стіна, 3 підлоги, 23 колони, двері, сходи + 36 пропів, серед яких **тюремні ґрати й клітка для в'язня** — буквально наші клітки зі старту раунду. Built-in / URP / HDRP | Ціни й полігонажу знайти не вдалось — звірити на сторінці |
| 2 | [**POLYGON Dungeon Realms**](https://assetstore.unity.com/packages/3d/environments/dungeons/polygon-dungeon-realms-low-poly-3d-art-by-synty-189093) (Synty) | Безпечний вибір: **1118+ префабів**, модульні секції, 19 персонажів у комплекті (замінили б Stickman), URP, Unity 2021.3+. І головне — далі **весь каталог Synty в тому ж стилі** | $99.99 зі знижки зі $199.99 *на момент зрізу*. Важкий; під мобілку профілювати самому |
| 3 | [**Stylized Modular Prison Dungeon**](https://assetstore.unity.com/packages/3d/environments/dungeons/stylized-modular-prison-dungeon-327461) | Найдешевший спосіб побачити гру одягненою: ~€13.80, URP + HDRP, Unity 6 | Built-in **не** підтримано. Менший за обсягом |
| 4 | [**Modular Dungeon & Castle Pack**](https://assetstore.unity.com/packages/3d/environments/dungeons/modular-dungeon-castle-pack-stylized-low-poly-349790) | Запас, якщо треба більше «замку», ніж «тюрми». Built-in / URP / HDRP, Unity 6000.2 | 370 МБ |

Тема docs/01 — «Сектор», радянсько-табірна. Під неї найближче **1**: підземна тюрма з ґратами.
Synty (**2**) дає фентезійне підземелля — якщо брати його, тему доведеться трохи зсунути, зате
отримуєш один візуальний словник на всю гру наперед.

## Чого бракує в проєкті, щоб куплена локація встала швидко

Бейкер уже вміє все потрібне: читає `SimCollider`, ріже пандуси на сходинки, відкидає задник,
фільтрує по смузі глибини, малює гізмо й друкує звіт. Чого **нема** — це **позначити** сотні
префабів куплено локації. Зараз `SimCollider` вішається руками на кожен шматок (у docs/16 видно
ціну: ручна локація це 17 маркерів після того, як їх було 51).

Отже перед імпортом варто зробити **редакторний інструмент розмітки**: виділив об'єкти → позначив
як Solid / OneWay / Ladder / Ignore; «усе за площиною — Ignore»; і аудит, який покаже колайдери
без маркера. Це перетворює імпорт локації з дня на кілька хвилин.

---

# Безкоштовне: що брати

> Перевірено пошуком у жовтні 2026. З цієї сесії **не відкривається** ні `assetstore.unity.com`,
> ні `itch.io`, ні `opengameart.org` — проксі ріже домени, тож усе нижче зі сніпетів пошуку.
> Склад безкоштовних тірів і сумісність **звіряй на сторінці перед завантаженням**.

Масштаб із розділу вище діє й тут, навіть сильніше: безкоштовні паки так само зроблено під
людину 1.8 м. **Метр вирішується до того, як щось качати.**

## А. CC0 — найкраща ліцензія, і вже рекомендовано вище

| Пак | Що дає | Перевірено |
|---|---|---|
| ⭐ [**KayKit — Dungeon Remastered**](https://kaylousberg.itch.io/kaykit-dungeon-remastered) | **200+** стилізованих підземних моделей: модульні **стіни, підлоги, сходи, двері**. У чейнджлозі безкоштовного тіру — модульні сходинки й стелі. На превʼю видно **камери з ґратчастими дверима** — це буквально наші клітки | CC0, вільно комерційно, без атрибуції. Дзеркало в Godot Asset Library підтверджує ліцензію |
| [KayKit — Medieval Builder](https://opengameart.org/content/kaykit-medieval-builder-pack-10) | балки, опори, паркани | CC0 |
| [Kenney — Modular Cave Kit](https://opengameart.org/content/modular-cave-kit) | 40 модульних сегментів печери | CC0 |
| [Quaternius — Modular Dungeon](https://quaternius.itch.io/lowpoly-modular-dungeon-pack) | 45+ модульних блоків | CC0 |

CC0 означає: комерційно можна, атрибуція не потрібна, жодного EULA магазину. Для проєкту, який
колись піде в стор, це суттєво менше паперу, ніж Asset Store EULA.

## Б. Безкоштовне в самому Asset Store

Зручніше (ставиться прямо в проєкт), але ліцензія — Asset Store EULA, не CC0.

| Пак | Чому | Застереження |
|---|---|---|
| [**Free Low Poly Dungeon Pack**](https://assetstore.unity.com/packages/3d/environments/dungeons/free-low-poly-dungeon-pack-398788) | Найсвіжіший безкоштовний підземний пак, який знайшовся: Built-in + **URP**, Unity 6000.3.2f1, оновлено 24.08.2026 | Видавця й склад на другому проході підтвердити не вдалось |
| [New Dungeon Pack Modular Low Poly (Free)](https://assetstore.unity.com/packages/3d/environments/dungeons/new-dungeon-pack-modular-low-poly-free-version-174858) | 5 структурних моделей + 5 декоративних, серед них **драбина** | Це пробник, а не кіт |
| [POLYGON — Starter Pack](https://assetstore.unity.com/packages/3d/props/polygon-starter-pack-156819) (Synty) | Безкоштовний семпл серії: пропи, персонажі, енвайрмент, демосцена. URP | Synty самі пишуть: **у Unity 6 вся демосцена рожева**, пак треба конвертувати в URP. Модульність не підтверджена |
| Ultimate Low Poly Dungeon (Broken Vector) | — | **Тільки Built-in, URP не підтримано** → нам не підходить |

Траплявся ще «Modular Prison Asset Pack» як безкоштовний і тюремний (URP+HDRP, оновлення 2022),
але на повторному пошуку підтвердити його не вдалось — перевіряй сам.

> **Не брати** з дзеркал типу `unityassetcollection` та подібних: на їхніх же сторінках
> написано «NOT FOR COMMERCIAL PURPOSES».

## Що брати

**KayKit — Dungeon Remastered як база.** Бо: CC0; модульний на сітці (а розріз збирається саме
з модулів — просто не ставиш ближню стіну); є сходи; є **ґратчасті двері камер**, тобто наші
клітки; і docs/10 вище вже обрав KayKit як базу стилю, тож персонажі, зброя й ліс потім
збігаються. Безкоштовне з Asset Store — тільки добирати те, чого в базі бракує.

---

## Ширше за підземелля: критерій — структура, а не тема

Перший список був про підземелля, бо це тема docs/01. Це була помилка кадру. Для **розрізу
збоку** вирішує не тема, а **структура**: що краще, то менше в локації закритих коробок і то
більше відкритих каркасів, які видно наскрізь.

> Кімната з розрізу вимагає прибрати ближню стіну. **Містку, риштуванню чи платформі не треба
> робити нічого** — вони вже такі.

І окремо: наша арена **вже й є містки** — три відрізки помосту на y 3.59 з драбинами. Тобто
індустріальний каркас лягає на неї один в один, і в тему «Сектора» влучає навіть точніше, ніж
фентезійне підземелля.

### Рівень 1 — зроблено рівно під таку камеру

| Пак | Чому саме це | Перевірено |
|---|---|---|
| ⭐ [**Modular Industrial Catwalk Kit [Free]**](https://assetstore.unity.com/packages/3d/props/industrial/modular-industrial-catwalk-kit-free-287064) | Містки, поручні, опори — **відкрита сталь, яку видно наскрізь**. Шматки стикуються, і **всі на одному матеріалі** — тобто один дроколл, що для мобілки золото | Безкоштовний, **URP**, Unity 2021.3.8f1, останній реліз 09.06.2024 |
| ⭐ [**Kenney — Platformer Kit**](https://kenney-assets.itch.io/platformer-kit) | **140+** моделей, і пак прямо називається «environment and objects for platformer games»; ранні версії віддавали навіть **sideview-рендери**. Блоки, плити, перешкоди | CC0, OBJ/FBX/glTF. Драбини в списку не названо — звірити |

### Рівень 2 — модульні кіти, з яких розріз збирається (не ставиш ближню стіну)

| Пак | Що дає |
|---|---|
| [KayKit — Dungeon Remastered](https://kaylousberg.itch.io/kaykit-dungeon-remastered) | стіни, підлоги, сходи, двері + **ґратчасті двері камер** = наші клітки. CC0 |
| [Kenney — Castle Kit](https://kenney-assets.itch.io/castle-kit) | 70+: стіни, вежі, брами, облогові машини. CC0, модульний |
| [Kenney — Pirate Kit](https://opengameart.org/content/pirate-kit) | 70+, **кораблі**: розріз галеона — це класичний бічний рівень, палуби одна над одною. CC0 |
| [Kenney — Modular Cave Kit](https://opengameart.org/content/modular-cave-kit) · [Quaternius Modular Dungeon](https://quaternius.itch.io/lowpoly-modular-dungeon-pack) | 40 і 45+ модульних сегментів. CC0 |

### Рівень 3 — обстановка, не структура

| Пак | Що дає |
|---|---|
| [Low-poly Industrial Pack](https://assetstore.unity.com/packages/3d/props/low-poly-industrial-pack-397642) | ящики (наші скрині), труби, бочки, цистерни. Безкоштовний, **URP**, Unity 2022.3.12 |
| [Low Poly Factory Machine Pack Demo](https://assetstore.unity.com/packages/3d/props/industrial/low-poly-factory-machine-pack-demo-272637) | станки, конвеєри. Безкоштовний, URP |

### Відпало, і чому

- **Modular Sci-Fi Interior Pack — Lite** — свіжий (серпень 2026) і безкоштовний, але **тільки
  Built-in, URP не підтримано**
- **Modular Sci-Fi Corridor** (MagixBox) — безкоштовний, але 2019 рік і URP не підтверджено
- **Free Unity low poly sci-fi corridors** (Delthor, itch) — у коментарях пишуть, що моделі
  вантажаться сірими без кольору
- **Ultimate Low Poly Dungeon** — Built-in only

### Що брати

**Містки + індустріальна обстановка.** `Modular Industrial Catwalk Kit` на структуру (він лягає
просто на наш поміст), `Low-poly Industrial Pack` на обстановку, `Kenney Platformer Kit` на блоки
й перешкоди. Усі безкоштовні, усі URP або CC0.

KayKit лишається найкращим вибором, **якщо** йти в кам'яне підземелля — ради ґратчастих камер.
Але під «Сектор» і під нашу камеру індустріальний каркас точніший.


---

## Ще більші паки

Попередні списки — це кіти по 40–150 моделей. Тут те саме питання, але з іншим фільтром:
**скільки всього в паку**. Критерій структури (відкритий каркас, модульність на сітці) нікуди
не дівається — просто серед великих паків шукаємо ті, що його теж проходять.

> Застереження на весь розділ: `assetstore.unity.com`, `itch.io` і `opengameart.org` із цієї
> сесії не резолвляться (DNS), тож усі цифри нижче — зі сніпетів пошуку. Склад, ціну й URP
> звіряй на сторінці пака перед покупкою.

### Найбільше за обсяг

| Пак | Обсяг | Ліцензія / ціна |
|---|---|---|
| ⭐ [**Kenney Game Assets All-in-1**](https://kenney.itch.io/kenney-game-assets) | **60 000+** ассетів, 456 МБ: 3D, 2D, UI, звук, шрифти | **$19.95**, необмежені комерційні проєкти, **без атрибуції**. Нові паки власникам безкоштовно |
| [Quaternius — усі паки](https://quaternius.itch.io/) ([сайт](https://quaternius.com/)) | 70+ паків, ~2 000 моделей сумарно | **CC0**. Кожен пак — pay-what-you-want: «Standard» безкоштовно, «Pro» від $9.99, «Source» від $14.99 |

**Kenney All-in-1 — це відповідь на «більше».** Platformer Kit, Castle Kit, Pirate Kit і
Modular Cave Kit, які вже стоять у списках вище окремо, — усі в ньому. Тобто за $19.95 береться
весь рівень 2 і половина рівня 1 одним файлом, плюс UI, шрифти й звук, плюс усе, що Kenney
випустить далі. Для проєкту, де ще не вирішено, яка саме локація, це найдешевший спосіб
перестати вибирати.

### Найбільші поодинокі кіти

| Пак | Обсяг | Чому підходить |
|---|---|---|
| ⭐ [Quaternius — Modular Sci-Fi MegaKit](https://quaternius.itch.io/modular-sci-fi-megakit) | **190** безкоштовно (270+ у Source) | Модульний на сітці, CC0. Standard 46 МБ безкоштовно, Pro від $9.99, Source 899 МБ від $14.99 (там уже готові Unity/Unreal/Godot-проєкти й колізії) |
| [Quaternius — Medieval Village MegaKit](https://quaternius.itch.io/medieval-village-megakit) | **176** безкоштовно (300+ у Source) | Стіни, підлоги, **сходи**, дахи, усе по сітці → розріз збирається. CC0. Standard 153 МБ безкоштовно; Source від $14.99 містить **готовий Unity URP-проєкт** |
| [Obstacle Course Kit](https://laplahce.itch.io/obstacle-course-kit) | **470** префабів, 7 демо-рівнів, 16 скриптів, 32 текстури | Перешкоди — це наш геймплей, а не декор. Усе модульне. За посиланням — інтерактивне демо; сторінку в Asset Store знайти не вдалось, ціну й URP звіряй там |
| [Quaternius — Ultimate Platformer Pack](https://quaternius.itch.io/ultimate-platformer-pack) | **100+** | Прямо під платформер збоку: модульні платформи, анімований персонаж (18 анімацій), 4 вороги. Один zip 17 МБ, CC0, pay-what-you-want |

### Великі й безкоштовні в Asset Store

| Пак | Що дає | Застереження |
|---|---|---|
| [**3D Free Modular Kit**](https://assetstore.unity.com/packages/3d/environments/3d-free-modular-kit-85732) (Barking Dog) | Модульні інтер'єри, **URP**, оновлено вересень 2026 | Кількість префабів у сніпеті не названа |
| [**CG Cookie — Sci-Fi Modular Level Kit**](https://cgcookie.com/downloads/sci-fi-modular-level-kit) | Плитки підлоги, кути, стіни + **платформи й мости** — тобто саме відкритий каркас | Роздається поза Asset Store, перевір формат і ліцензію |
| [Free Fantasy Medieval Houses and Props](https://emaceart.itch.io/free-fantasy-medieval-houses-and-props-pack) (EmacEArt) | Pay-what-you-want, усередині `EA03 Slavic Village_Free.unitypackage` | Це безкоштовний зріз. Повний [Slavic Medieval Village](https://assetstore.unity.com/packages/3d/environments/fantasy/slavic-medieval-village-town-interior-and-exterior-pack-environm-137794) коштує **$44** (зате **URP**, версія 1.7 від 12.01.2026) |

### Відпало, і чому

- **Dark Fantasy Kit (Lite)** — лише 20 префабів і Unity 5.6–2018. Не «більше» й не наша версія
- [**3D Modular Sci-Fi Environment Pack**](https://assetstore.unity.com/packages/3d/environments/sci-fi/3d-modular-sci-fi-environment-pack-57957) — **490+ префабів**, 97 FBX, 28 типів підлоги, 44 стін, 36 вікон, двері, **сходи**, колони. Найбільший поодинокий кіт, що знайшовся, і за складом ідеальний. Але: $25, версія 1.0 від **29.03.2016**, вихідний Unity **4.6** → URP там бути не може, усі матеріали доведеться перебирати руками. Якщо візьмешся — дивись сусідній [Sci-Fi Modular Environment](https://assetstore.unity.com/packages/3d/environments/sci-fi/sci-fi-modular-environment-257602) (257602), він молодший і заявляє Built-in + URP
- **Sci-Fi Underground Lab** (PolyArt 3d) — модульний і URP, але платний і без видимої цифри складу
- **RPG Stylized Fantasy Environment** — «200+ префабів, оптимізовано під URP», але трапився лише
  на сторонньому сайті-дзеркалі. Ліцензія не підтверджена → не брати, поки не знайдеться оригінал

### Що брати з великого

1. **Kenney Game Assets All-in-1** за $19.95 — якщо готовий платити хоч щось. Це не «ще один
   пак», це заміна всьому рівню 2 разом.
2. **Quaternius Modular Sci-Fi MegaKit** (190, CC0) — якщо тільки безкоштовне. Найбільший
   безкоштовний модульний кіт із тих, що знайшлись, і сітка в ньому та сама логіка, що й у KayKit.
3. **Modular Industrial Catwalk Kit** лишається в наборі в обох випадках — ні Kenney, ні
   Quaternius не дають готових містків із поручнями, а наша арена — це три містки.

> І метр усе ще перший. Ці паки теж авторські під людину 1.8 м, а наш боєць — 0.91 м. Поки
> це не вирішено, будь-який купленим пак доведеться масштабувати наосліп.

---

## Серйозніше за лоу-полі

> **Відхилено.** Гра має бути стилізована, не реалістична, і лише на безкоштовному.
> Розділ лишається як запис того, що дивились і чому не взяли. Чинне рішення — у «Стиль:
> між Kenney і PBR» нижче.


Попередні списки — кіти з плоскими кольорами (Kenney, Quaternius, KayKit). Тут те саме питання,
але PBR: метал з іржею, бетон із патьоками, нормалмапи. Критерій структури лишається той самий,
але додаються два нові.

**Бюджет.** Серйозні паки везуть текстури 2K–4K і на порядок більше трикутників. Для мобілки це
зазвичай вирок — **але не для нашої камери**. Вона зафіксована збоку на відомій відстані, тобто:
геометрію видно завжди з тих самих ракурсів, світло печеться наперед, усе за бойовою площиною —
декор без колайдерів (у нас для цього вже є `Depth` і `Span`), а текстури ріжуться до 1024 прямо
на імпорті. Серйозний пак тут обійдеться дешевше, ніж у звичайній 3D-грі.

**Метр стає критичним.** Лоу-полі прощає масштаб, PBR — ні. У реалістичному паку двері 2.1 м,
поручні 1.1 м, сходинка 0.17 м — усе під людину 1.8 м. Наш боєць 0.91 м поруч із ними читатиметься
як дитина, і це буде видно одразу. **Перед покупкою серйозного пака метр треба закрити.**

### Рівень 1 — каркас, прямо під нашу камеру

| Пак | Що дає | Стан |
|---|---|---|
| ⭐ [**Modular Industrial Catwalk**](https://assetstore.unity.com/packages/slug/287148) | **24 модулі**: прямі, повороти, хрести, Т-подібні, **сходи, драбини, рампи**, кожен у варіанті з поручнями і без | **URP**, Unity 2021.3.8f1, і єдиний у цьому розділі, який у себе на сторінці пише **«optimized for VR and mobile»**. Платний апгрейд безкоштовного [287064](https://assetstore.unity.com/packages/3d/props/industrial/modular-industrial-catwalk-kit-free-287064) |
| [Modular Scaffolding Pack](https://assetstore.unity.com/packages/slug/157986) | 23 модулі риштування по сітці, PBR | **$14.99**, але матеріали **Built-in** → конвертувати в URP |
| [Scaffolding Kit](https://assetstore.unity.com/packages/3d/props/industrial/scaffolding-kit-117715) | PBR metallic/roughness, повний набір мап | **$45**, останній реліз 2019, URP не заявлено |

### Рівень 2 — повні локації

| Пак | Обсяг | Стан |
|---|---|---|
| ⭐ [**Old Industry**](https://assetstore.unity.com/packages/3d/environments/old-industry-212485) | **450+ унікальних префабів**, сталеливарний завод. Модульні, «easy to snap on grid» | **URP + HDRP**. Найбільший серйозний пак, що знайшовся. Ціни зі сніпета не видно |
| ⭐ [**Modular Abandoned Factory**](https://assetstore.unity.com/packages/slug/315386) | **135 префабів**: стіни, підлоги, стелі, колони, двері, **сходи, поручні** + машини, крани, уламки | **URP**, Unity **6000.0.42f1** (найсвіжіша сумісність у списку), HDRP ні. **~$33** замість $66. Текстури 2048 |
| [Factory/Warehouse Interior Modular Pack](https://assetstore.unity.com/packages/slug/224512) (Cybernetic Walrus) | 90 мешів / 89 префабів; є [бандл з екстер'єром](https://assetstore.unity.com/packages/3d/props/factory-interior-factory-exterior-bundle-260331) на 236 | Усередині окремі unitypackage під Standard, URP і HDRP — тобто конвертувати не треба |
| [Modular Sewer Pack](https://assetstore.unity.com/packages/3d/environments/modular-sewer-pack-277454) | **300+ моделей** зі снапінгом, 42 текстури 2K | **URP**. Колектор у розрізі — це тунель, видно наскрізь |
| [Modular Abandoned Shelter](https://assetstore.unity.com/packages/3d/environments/industrial/modular-abandoned-shelter-311075) | Модулі підземного бункера | Built-in + **URP** + HDRP, але скріни зняті в HDRP → у URP виглядатиме інакше |

### Якщо лишаємось у темі «Сектора» — тюрма

| Пак | Обсяг | Стан |
|---|---|---|
| ⭐ [**Prison Modular Pack**](https://assetstore.unity.com/packages/3d/environments/prison-modular-pack-206713) | 41 меш/префаб, 17 текстур **4K** | **Built-in + URP + HDRP**, **$18.99**. Найдешевший чистий URP тут |
| [Prison — Big Building, Characters and Props](https://assetstore.unity.com/packages/3d/environments/industrial/prison-big-building-characters-and-props-152316) | Триповерхова будівля: камери, допитна, інші кімнати, **+ персонажі й пропи** | **За замовчуванням усе вже налаштовано під URP**, HDRP ні |
| [Prison Cell — Interior and Props](https://assetstore.unity.com/packages/3d/props/interior/prison-cell-interior-and-props-144752) | 3 кімнати + пропи | URP, але видавець сам попереджає про рожеву сцену → перемкнути рендер |
| [Prison Interior Vol1](https://assetstore.unity.com/packages/3d/environments/urban/prison-interior-vol1-88726) · [Vol2](https://assetstore.unity.com/packages/3d/environments/urban/prison-interior-vol2-106888) | Модульні, PBR metallic/roughness | **$19.99** кожен, **URP не заявлено** |

### Безкоштовного серйозного практично нема

Прогнав окремий пошук — жодного безкоштовного PBR-модульного паку під URP не знайшлось.
Найближче: [Abandoned Factory Environment](https://assetstore.unity.com/packages/3d/environments/industrial/abandoned-factory-environment-83392) (83392)
має **Free Lite** версію, але в примітках пише Built-in. Тобто серйозний вигляд тут коштує грошей,
і це нормальна ціна питання: $19–33 за локацію.

### Що брати з серйозного

1. **Modular Abandoned Factory** (~$33) як базу — найсвіжіша URP-сумісність (Unity 6000.0.42f1),
   і в складі одразу сходи й поручні, тобто наш поміст збирається з коробки.
2. **Modular Industrial Catwalk** (287148) на каркас — єдиний, хто заявляє мобільну оптимізацію,
   і він лягає прямо на наші три містки на y 3.59.
3. **Old Industry** (450+ префабів) — якщо хочеться запас на кілька арен, а не на одну.
4. **Prison Modular Pack** ($18.99) — якщо локація має читатись саме як тюрма.

> Сторінки я відкрити не можу (`assetstore.unity.com` із сесії не резолвиться), тож ціни, версії
> Unity та URP у таблицях — зі сніпетів пошуку. Звіряй на сторінці перед покупкою.

---

## Стиль: між Kenney і PBR

Рішення по стилю: **стилізовано, але не плоско**. Тобто:

```
Kenney / Quaternius  ──  KayKit / Synty  ──  hand-painted  ──  PBR
    плоский колір         ◄── ЦІЛЬ ──►        мальовані        метал
    без деталей           силует є,           текстури         з іржею
                          текстура проста
     занадто просто                                        занадто серйозно
```

Ліва межа — те, що вже стоїть у списках вище: Kenney, Quaternius, базовий Kenney-блок. Там
колір плоский і силует кубічний. Права — попередній розділ, PBR. Ціль посередині: форма
вирізана, є фаски й пропорції, але текстура лишається умовною.

Плюс жорстка умова: **тільки безкоштовне**.

### Проблема, яку варто назвати прямо

Перетин «стилізовано-не-плоско × безкоштовно × URP × індустріальне» на Asset Store практично
порожній. Усе, що я знайшов із потрібним рівнем деталі та заявленим URP, — платне, а всі
безкоштовні роздачі Synty (City, Icon, Prototype, Battle Royale) давно закінчились. Тож вибір
звужується до двох джерел, і обидва — не Asset Store.

### Що лишається (усе безкоштовне)

| Джерело | Чому це саме наша смуга | Ліцензія |
|---|---|---|
| ⭐ [**KayKit**](https://kaylousberg.itch.io/) (Kay Lousberg) | На щабель вище за Kenney: градієнтні текстури замість плоского кольору, фаски, впізнавані силуети — і при цьому все ще лоу-полі. Увесь каталог безкоштовний | **CC0**, без атрибуції, можна комерційно |
| [**POLYGON Starter Pack**](https://assetstore.unity.com/packages/3d/props/polygon-starter-pack-156819) (Synty) | Семпл серії POLYGON — еталон «стилізовано, але не плоско». Пропи, персонажі, енвайрмент, демосцена. **URP заявлено** | Asset Store EULA, безкоштовно |

**KayKit — база.** Це збігається з тим, що docs/10 обрав раніше, але тепер уже за стилем, а не
лише за наявністю ґратчастих дверей. Паки, які є безкоштовно: Dungeon Remastered, Adventurers,
Skeletons, Character Animations, Forest Nature, Platformer Pack,
[Medieval Builder](https://opengameart.org/content/kaykit-medieval-builder-pack-10) (**200+**
об'єктів), Furniture Bits, City Builder Bits, Space Base Bits, Prototype Bits. Персонажі, зброя,
локація й анімації — від одного автора, тобто стиль зійдеться без підгонки.

**POLYGON Starter Pack — для звірки.** Поставити поруч із KayKit і подивитись, який із двох
читається краще на нашій камері. Застереження лишається те саме, що й раніше: Synty самі пишуть,
що **в Unity 6 демосцена рожева** — пак треба конвертувати в URP.

### Чого в обох бракує

Ні KayKit, ні Starter Pack не дають **містків і риштування**, а наша арена — це три помости на
y 3.59. Закриває це той самий
[Modular Industrial Catwalk Kit [Free]](https://assetstore.unity.com/packages/3d/props/industrial/modular-industrial-catwalk-kit-free-287064):
він безкоштовний, URP, і пофарбований рівним індустріальним помаранчевим — тобто читається як
стилізований, а не як фотометал, і з KayKit стикується нормально.

### Збірка

| Роль | Пак |
|---|---|
| Структура локації, клітки, сходи | **KayKit Dungeon Remastered** (CC0) |
| Містки, поручні, драбини | **Modular Industrial Catwalk Kit [Free]** (URP) |
| Персонажі, зброя, анімації | **KayKit Adventurers + Skeletons + Character Animations** (CC0) |
| Дрібні пропи, ящики, меблі | **KayKit Furniture / City Builder / Space Base Bits** (CC0) |
| Перевірка стилю | **POLYGON Starter Pack** (безкоштовно, URP) |

Нуль витрат, один автор на 80% контенту, URP скрізь.

### Якщо колись буде бюджет

Два платні влучають у стиль точно, тож хай лежать записані:

- [**STYL-Underground Prison**](https://assetstore.unity.com/packages/3d/environments/dungeons/styl-underground-prison-stylized-modular-dungeon-kit-312229) —
  стилізований **тюремний** модульний кіт: 21 стіна, 3 підлоги, 23 колони, двері, **сходи**,
  36 пропів, сітка **2×2 м**, Built-in + URP + HDRP. За темою «Сектора» — найточніше попадання
- [**POLYGON Construction**](https://assetstore.unity.com/packages/3d/environments/industrial/polygon-construction-low-poly-3d-art-by-synty-168036) —
  **550+ префабів**, і серед них саме риштування, балки, драбини, будівельні ліфти, крани. URP,
  Unity 2022.3.56, v1.5.1 від 04.06.2026. ~$27.49 замість $54.99

> Сторінки відкрити не можу (`assetstore.unity.com` та `itch.io` із сесії не резолвляться) —
> склад і ліцензії зі сніпетів пошуку, звіряй на місці.

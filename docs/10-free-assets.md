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

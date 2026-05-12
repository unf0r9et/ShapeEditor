# Рефакторинг ShapeEditor: одна библиотека фигур и регистрация типов

## Зачем вообще что-то меняли

Раньше весь код фигур лежал внутри основного проекта редактора, и окно напрямую создавало `new PolygonShape()` и т.п. Сейчас **вся логика фигур** живёт в **одной** библиотеке **`ShapesLibrary`** (`ShapesLibrary.dll`): там же и **`ShapeBase`**, и **интерфейсы** (`ShapeContracts.cs`), и **реализации** (`PolygonShape`, `EllipseShape`, `CompoundShape`, пресеты). Отдельного проекта `ShapeEditor.Core` **нет** — в решении только **`ShapesLibrary`** и **`ShapeEditor`**.

Приложение **ссылается** на `ShapesLibrary` как на обычный проект (чтобы компилировались типы `ShapeBase`, `IEllipseShape` и т.д.). Дополнительно есть **API загрузки и регистрации** сборок (`DllOpen`, `AddLibrary`), чтобы можно было открыть DLL с диска и зарегистрировать в ней помеченные классы фигур.

---

## Условия: `DllOpen`, `AddLibrary`, «у кого проверка, у кого нет», экспорт

### Выполняются ли они?

**Да**, в коде это так устроено (файл `ShapeEditor/ShapeLoader.cs` и атрибуты в `ShapesLibrary/`).

| Что нужно | Как сделано |
|-----------|-------------|
| **Функция `DllOpen`** | Статический метод `ShapeLoader.DllOpen(string path, out string? errorMessage)`: нормализует путь, проверяет что файл есть, вызывает `Assembly.LoadFrom`, при ошибках возвращает `null` и текст в `errorMessage` (нет файла, плохой образ, ошибка загрузки). Сборку **только открывает**, в реестр типы **не** добавляет. |
| **Функция `AddLibrary`** | `ShapeLoader.AddLibrary(Assembly assembly, out string? errorMessage)`: обходит `assembly.GetExportedTypes()`, для каждого типа смотрит: класс, не абстрактный, наследует `ShapeBase`. **Если атрибута `[ExportedShape]` нет** — тип **просто пропускается** (никакой дополнительной «проверки что за класс» для регистрации нет, в фабрику он не попадает). **Если атрибут есть** — тогда проверяются: публичный конструктор без параметров, непустое имя в атрибуте, нет дубликата уже зарегистрированного ключа; при ошибке возвращается `false` и сообщение. Успешные пары «строка из JSON → `Type`» попадают во внутренний словарь и в **`ShapePluginContext.Factory`** (`ReflectionShapeFactory`). |
| **Где «экспорт» библиотеки** | Экспорт задаётся **в самой `ShapesLibrary`**: класс **`ExportedShapeAttribute`** (`ShapesLibrary/ExportedShapeAttribute.cs`) и пометка **`[ExportedShape("ИмяДляJson")]`** на типах. Примеры: `PolygonShape`, `EllipseShape`, `CompoundShape`, файл **`ExportedPresetShapes.cs`** (`RectangleShape`, `TriangleShape`, …, `CustomShape`, `CircleShape`). Именно эти имена совпадают с полем **`"type"`** в JSON и с ключами в **`ShapeLoader.CreateRectangle()`** и т.п. |

### Где библиотека используется

- **`App.xaml.cs`** — при старте вызывается **`ShapeLoader.TryLoadShapesPlugin()`** (сейчас это **`ClearShapeTypeRegistry`** + **`AddLibrary(typeof(ShapeBase).Assembly, …)`** — регистрация типов из **уже загруженной** при запуске сборки `ShapesLibrary`, без второго `LoadFrom` той же DLL с другого пути, чтобы не плодить два экземпляра одной сборки в процессе).
- **`ShapeLoader`** — обёртки `CreateRectangle`, `CreateEllipseToolbarDefault`, … и **`CreateFromPersistedType`** → фабрика из **`ShapePluginContext.Factory`**.
- **`ShapeBase.LoadFromFile`** (в `ShapesLibrary`) — создаёт экземпляр через **`ShapePluginContext.Factory.Create(typeName)`** по строке из JSON.
- **`MainWindow.xaml.cs`** — не импортирует конкретные классы фигур из библиотеки по именам типов; работа с **`ShapeBase`** и интерфейсами **`IEllipseShape` / `IPolygonShape` / `ICompoundShape`**, создание через **`ShapeLoader`**.

Цель **`CopyShapesPlugin`** в `ShapeEditor.csproj` по-прежнему **копирует** `ShapesLibrary.dll` в папку **`plugins`** и рядом с exe (удобно для доставки); стартовая регистрация идёт через сборку из ссылки. При необходимости **вручную** можно вызвать **`DllOpen(путь)`** и затем **`AddLibrary(сборка)`** для другой DLL с тем же контрактом (осознанно, с учётом загрузки типов в .NET).

---

## Как устроено в каталогах

```
ShapesLibrary/     →  ShapesLibrary.dll  (ShapeBase, контракты, ExportedShapeAttribute, все фигуры)
ShapeEditor/       →  ShapeEditor.exe    (WPF, ShapeLoader, ReflectionShapeFactory, MainWindow)
```

---

## Как добавить новую фигуру

1. В **`ShapesLibrary`** — класс, наследник **`ShapeBase`**, нужные интерфейсы.
2. Повесить **`[ExportedShape("ВашКлюч")]`** (ключ должен совпадать с тем, что пишется в JSON в **`"type"`**, если фигура сохраняется под этим именем).
3. Публичный конструктор **без параметров** (иначе `AddLibrary` вернёт ошибку для этого типа).
4. В **`ShapeEditor`** при необходимости добавить вызов в **`ShapeLoader`** (например новый `Create…`), если нужна кнопка в UI.

Отдельного `switch` в **`ShapePluginEntry`** больше нет — список типов задаётся **атрибутами** и сканированием в **`AddLibrary`**.

---

## Сборка и запуск

1. Открыть **`ShapeEditor.slnx`**, стартовый проект **`ShapeEditor`**.
2. Нужны **Windows** и **.NET 9** (целевой фреймворк `net9.0-windows7.0`).

---

## Итог одной фразой

**Одна библиотека `ShapesLibrary` хранит базу, контракты и фигуры; экспорт в фабрику помечается атрибутом `[ExportedShape]`; приложение вызывает `DllOpen` для открытия DLL с диска и `AddLibrary` для регистрации только помеченных классов (остальные публичные типы в сборке без атрибута не регистрируются); при старте фабрика поднимается через `TryLoadShapesPlugin` из уже подключённой сборки `ShapesLibrary`.**

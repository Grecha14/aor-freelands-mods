# Памятка для ИИ-помощника: установка и проверка модов

Этот репозиторий — готовые к установке моды для Age of Reforging: The Freelands (Steam):
DemonLook 0.22.0, ItemForge 2.0.0, EncounterScale 1.2.0 и русский перевод. Исходников и
сборки здесь нет: нужно только поставить и проверить. Отвечайте человеку по-русски.

## Что лежит в репозитории

```
release/BepInEx/plugins/DemonLook/DemonLook.dll
release/BepInEx/plugins/ItemForge/ItemForge.dll
release/BepInEx/plugins/ItemForge/LocalizationPatch/   dialogue_ru.tsv, items_ru.tsv,
                                                        spells_ru.tsv, translations.txt, ui_ru.tsv
release/BepInEx/plugins/EncounterScale/EncounterScale.dll
```

То же одним архивом — `aor-freelands-mods-v1.0.zip` в выпуске v1.0 (Releases).

## Установка по шагам (Windows)

1. **Найти папку игры** — ту, где лежит `Age of Reforging The Freelands.exe`. Обычно
   `C:\Program Files (x86)\Steam\steamapps\common\Age of Reforging The Freelands`; если нет —
   в других библиотеках Steam (`<диск>:\SteamLibrary\steamapps\common\...`) или спросить
   человека: Steam → игра → «Управление» → «Просмотреть локальные файлы».
2. **Копия сохранений** — перед установкой скопировать папку
   `%USERPROFILE%\AppData\LocalLow\PersonaeGames\Age of Reforging The Freelands\Save`
   куда-нибудь рядом. Сохранения с модами без модов могут не загрузиться.
3. **Игра должна быть закрыта.** Проверка: `tasklist | findstr /i reforging` — пусто.
   Запускать и закрывать игру сами не надо: попросите человека.
4. **BepInEx 5.4.23 x64.** Если в папке игры нет `BepInEx\core\BepInEx.dll`:
   - скачать `BepInEx_win_x64_5.4.23.x.zip` (самую новую 5.4.23.x) со страницы
     <https://github.com/BepInEx/BepInEx/releases>. Не BepInEx 6 и не x86;
   - распаковать в папку игры: рядом с `.exe` должны появиться `BepInEx\`, `winhttp.dll`,
     `doorstop_config.ini`;
   - попросить человека запустить игру до главного меню и закрыть: BepInEx создаст
     `BepInEx\plugins` и `BepInEx\config`.
5. **Моды.** Скопировать содержимое `release\` в папку игры со слиянием папок (или
   распаковать туда архив из выпуска). Каждый мод — в своей папке `BepInEx\plugins\<Мод>\`.
   Нельзя класть DLL прямо в `BepInEx\plugins`: ItemForge ищет перевод рядом с собой.
6. **Проверка файлов:** есть `BepInEx\plugins\DemonLook\DemonLook.dll`,
   `BepInEx\plugins\ItemForge\ItemForge.dll`, `BepInEx\plugins\ItemForge\LocalizationPatch\`
   (5 файлов), `BepInEx\plugins\EncounterScale\EncounterScale.dll`.

## Проверка, что моды встали

Попросите человека запустить игру и дойти до главного меню, затем прочтите
`BepInEx\LogOutput.log` в папке игры. Должно быть:

- `Demon Race v0.22.0 loaded` и строка `Правок поставлено: N` (DemonLook);
- `Item Forge v2.0.0 loaded` и `Патчей поставлено: N` (ItemForge);
- `Encounter Scale v1.2.0 loaded`;
- строка вида `Перевод прочитан из «...\ItemForge\LocalizationPatch»` — перевод найден.

Если рядом с «поставлено» есть «не встало: K» — строкой выше написано, какая правка и
почему. Мод при этом работает, без одной этой части.

## Частые беды

| Что видно | В чём дело | Что делать |
|---|---|---|
| В журнале нет ни одного мода, `LogOutput.log` нет | BepInEx не запустился | проверить `winhttp.dll` и `doorstop_config.ini` рядом с `.exe`; версия должна быть 5.4.23 x64 |
| Моды загрузились, перевода нет | DLL ItemForge не в своей папке или нет `LocalizationPatch` | разложить по шагу 5 |
| «Не удалось заменить файл» при копировании | игра запущена | закрыть игру и повторить |
| После обновления игры моды падают или не грузятся | игра поменяла свой код | сообщить автору модов, версию игры взять из Steam |
| Старое сохранение не грузится без модов | в нём вещи и жители из модов | вернуть копию сохранений из шага 2 |
| Розовые предметы или куски в игре | не нашёлся материал для отрисовки | сообщить автору модов, приложив `LogOutput.log` |

Ещё один журнал — Unity: `%USERPROFILE%\AppData\LocalLow\PersonaeGames\Age of Reforging The Freelands\Player.log`.
В нём исключения и ошибки шейдеров. Его и `LogOutput.log` стоит прикладывать, когда пишете
автору модов.

## Настройки

`BepInEx\config\aor.demonlook.cfg`, `aor.itemforge.cfg`, `aor.encounterscale.cfg` появляются
при первом запуске. У каждой настройки есть описание; почти у каждого правила есть
`Enabled = true`. Менять — при закрытой игре. Свои данные моды хранят там же, по имени
сохранения: `aor.*.<имя сохранения>.txt` — их не трогать.

## Удаление

Удалить папки `DemonLook`, `ItemForge`, `EncounterScale` из `BepInEx\plugins`; при желании
и `aor.*` из `BepInEx\config`. Для игры без модов — вернуть копию сохранений.

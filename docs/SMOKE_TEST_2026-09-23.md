# WhisperMd — отчёт проверки от 23 сентября 2026 года

Текст расшифровки и сам аудиофайл в отчёт и архив исходников не включены.

## 1. Проверка среды

Команда из `C:\Base\Transcription`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-environment.ps1
```

Код выхода `0`. Все компоненты получили `OK`: модель `small`, CPU- и Vulkan-версии `whisper-cli.exe`, FFmpeg, скрипт транскрибации, исходный проект, GUI-релиз, SHA-256 модели и .NET SDK `10.0.401`.

## 2. Сборка

```powershell
dotnet build .\Transcription.sln -c Release
```

Итог: код выхода `0`, сборка `WhisperMd.dll` для `net8.0-windows`, предупреждений `0`, ошибок `0`. Первая попытка в ограниченной песочнице завершилась отказом в чтении каталога Microsoft SDKs профиля Windows; повтор с обычным доступом прошёл успешно. Это ограничение среды запуска проверки, а не ошибка проекта.

## 3. Реальная запись: CPU и Vulkan

В качестве входа взят существующий локальный `Recording 20260908114710.m4a` длительностью `100.454042` с. Проверены отдельные запуски `transcribe.ps1` с `-Backend cpu` и `-Backend vulkan`, прочие параметры — штатные (`ru`, 8 потоков, модель `small`).

| Режим | Код выхода | TXT | SRT | JSON | Текст TXT |
| --- | ---: | ---: | ---: | ---: | --- |
| CPU | 0 | 2710 байт | 3499 байт | 140796 байт | непустой |
| Vulkan | 0 | 2671 байт | 3346 байт | 142327 байта | непустой |

Оба запуска сообщили `Done. Backend: ...` и создали результаты в разных каталогах `output`. Исходная запись не удалена. Незначительные различия размеров выходных файлов между backend не оценивались на качество; для smoke-теста подтверждены успешное выполнение и непустой результат.

Команды проверки:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\transcribe.ps1 -InputPath 'C:\Users\ku2dm\Downloads\Recording 20260908114710.m4a' -Backend cpu
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\transcribe.ps1 -InputPath 'C:\Users\ku2dm\Downloads\Recording 20260908114710.m4a' -Backend vulkan
```

Функциональность GUI в этой проверке не тестировалась; проверены сборка и оба пути скрипта транскрибации.

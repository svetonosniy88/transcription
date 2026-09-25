# Разработка WhisperMd

## Начало работы

1. Запустить `scripts/check-environment.ps1`.
2. Открыть `Transcription.sln` в Visual Studio или перейти в `src/WhisperMd`.
3. Собрать проект командой `dotnet build`.
4. Для ручной проверки выбрать короткую аудиозапись и убедиться, что появились TXT, SRT, JSON и Markdown.

## Основные файлы

- `Program.cs` — точка входа Windows Forms.
- `MainForm.cs` — интерфейс, запуск PowerShell и обработка результата.
- `transcribe.ps1` — подготовка аудио, выбор backend и запуск модели.

## Сборка

Обычная проверочная сборка:

```powershell
dotnet build .\Transcription.sln -c Release
```

Автономный одиночный EXE:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-gui.ps1
```

Более компактная сборка, требующая установленный .NET Desktop Runtime:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-gui.ps1 -FrameworkDependent
```

## Правила изменений

- Не добавлять модель, аудиозаписи, SDK и готовые сборки в Git.
- Не дублировать логику запуска Whisper в GUI: параметры и fallback должны оставаться в `transcribe.ps1`.
- Новые настройки GUI передавать сценарию явными аргументами.
- Ошибка не должна удалять исходную запись или диагностический WAV.
- Перед релизом проверить CPU и Vulkan хотя бы на коротком файле.

## Ближайшие направления

- вынести язык, число потоков и backend в элементы интерфейса;
- добавить поле контекстного prompt;
- показывать прогресс и длительность обработки;
- позволить выбирать папку результата;
- добавить очередь нескольких файлов;
- покрыть разбор вывода процесса и поиск результатов тестами.

## Backend protocol

Начиная с этапа 3 WPF должен получать результат и состояние только через `WHISPERMD_EVENT <json>`. Не добавлять новый парсинг human-readable stdout (`Results:`, `Done.` и т.п.). Схема событий описана в `docs/BACKEND_PROTOCOL.md`.

Для одной записи использовать `WhisperBackendClient`. Последовательность нескольких записей должна строиться поверх него, а не переноситься внутрь `transcribe.ps1`.

Автоматическая локальная проверка protocol для короткой записи:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-backend-protocol.ps1 -InputPath 'C:\path\to\short-recording.m4a'
```

Она выполняет отдельные CPU/Vulkan прогоны, проверяет JSON events, progress, result paths, `-Prompt` и сохранённую legacy-строку `Results:`.

## Исполнение очереди WPF

Начиная с этапа 4 последовательность нескольких записей реализуется в `Execution/TranscriptionJobRunner`.

Правила слоя:

- `WhisperBackendClient` остаётся примитивом ровно одной записи;
- `transcribe.ps1` не должен знать о пользовательской очереди или группах заметок;
- ViewModel не должна содержать цикл вызовов PowerShell напрямую;
- ошибки отдельных элементов возвращаются как `TranscriptionItemRunResult` и не прерывают очередь;
- `TranscriptionJobRunResult.Job` хранит immutable snapshot настроек/порядка, использованных именно в этом запуске; будущая Markdown-агрегация должна опираться на него, а не на потенциально изменённый UI после завершения.

## Автоматическая проверка persistence / history / settings

После stages 7–9 основной быстрый локальный прогон:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-stage6-9.ps1
```

Он последовательно:

1. собирает WPF;
2. прогоняет verifier Markdown stage6;
3. проверяет JSON settings, передачу backend/language/threads, routing в library и SQLite history/search/persistence.

Для source snapshot использовать `scripts/package-source.ps1`. В отличие от раннего временного скрипта он исключает только корневую папку `models` с весами и **не исключает** `src\WhisperMd.App\Models`.

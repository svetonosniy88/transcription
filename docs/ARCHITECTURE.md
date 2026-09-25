# Архитектура проекта

## Поток данных

```text
аудиофайл(ы)
  -> WPF: TranscriptionJob / очередь
  -> transcribe.ps1 для каждой записи
  -> FFmpeg: WAV, 16 кГц, mono
  -> whisper.cpp: выбранный backend, auto = Vulkan -> CPU fallback
  -> TXT + SRT + JSON
  -> MarkdownNoteService
  -> Markdown-библиотека
  -> SQLite-индекс истории
```

## Слои

- `src/WhisperMd.App` — основной WPF v1: UI, очередь, notes/history/settings application-layer.
- `src/WhisperMd` — legacy WinForms, сохранённый как рабочая точка возврата.
- `transcribe.ps1` — единая точка оркестрации FFmpeg и `whisper.cpp`.
- `app/whisper.cpp` — внешний движок и локальные сборки.
- `models` — веса модели; сейчас используется мультиязычная `small`.
- `tools` — внешние бинарные зависимости.
- `inbox`, `working`, `output`, `archive` — локальные рабочие данные.
- `scripts` — обслуживание среды и сборка приложения.
- `releases` — готовые локальные версии GUI.

GUI ищет корень проекта вверх от своей папки по наличию `transcribe.ps1` и `models/ggml-small.bin`. При необходимости путь можно задать переменной `WHISPERMD_ROOT`.

## Параметры v1

- язык, backend и число потоков задаются в WPF Settings и фиксируются в immutable `TranscriptionJob`;
- defaults: `ru`, `auto`, `8`;
- `auto`: сначала Vulkan, затем CPU при ошибке;
- модель: `ggml-small.bin`;
- backend-результаты: TXT, SRT и JSON;
- пользовательский результат: Markdown;
- режим декодирования: параметры `whisper.cpp` по умолчанию, без явно заданного beam size.

## Границы репозитория

В Git входят исходники приложения, сценарии и документация. Модель, движок, SDK, готовые EXE, аудио, временные WAV и результаты транскрибации остаются локальными и восстанавливаются по документации.

## Backend protocol (v1, stage 3)

Новый WPF-клиент не разбирает human-readable строки `transcribe.ps1`. Машинные события передаются отдельными UTF-8 строками вида `WHISPERMD_EVENT <json>` и документированы в `docs/BACKEND_PROTOCOL.md`.

`WhisperBackendClient` запускает одну запись, потоково читает stdout/stderr, типизирует protocol events и отдельно сохраняет технический лог. Оркестрация очереди `TranscriptionJob` остаётся следующим слоем и не входит в backend client.

Строка `Results:` пока сохранена только для совместимости со старым WinForms и не является частью нового контракта.

## Application execution layer (stage 4)

Очередь не реализована внутри `transcribe.ps1` и не принадлежит WPF ViewModel.
Текущий путь исполнения нового приложения:

```text
WPF View / ViewModel
  -> immutable TranscriptionJob
  -> TranscriptionJobRunner
  -> IWhisperBackendClient
  -> WhisperBackendClient (одна запись)
  -> transcribe.ps1
  -> FFmpeg + whisper.cpp
```

`TranscriptionJobRunner` выполняет элементы строго последовательно и продолжает очередь после ошибки отдельной записи. Он сообщает application-level updates через `IProgress<TranscriptionRunUpdate>` и возвращает `TranscriptionJobRunResult`, содержащий точный snapshot запущенной задачи и результаты каждой записи.

Таким образом UI можно перерабатывать независимо от PowerShell/FFmpeg/whisper.cpp, а реализацию одиночного backend можно впоследствии заменить через `IWhisperBackendClient` без изменения оркестрации очереди.

## Markdown notes layer (stage 6)

Технический backend по-прежнему создаёт TXT/SRT/JSON на каждую запись. Пользовательский Markdown формируется **после** завершения application-level `TranscriptionJob` отдельным слоем и не входит в `transcribe.ps1`:

```text
TranscriptionJobRunResult
  -> MarkdownNoteBuilder
  -> MarkdownNoteDraft[]
  -> MarkdownNoteWriter
  -> *.md
```

`Separate`, `Combined` и `CustomGroups` интерпретируются на application-layer. Это сохраняет backend независимым от понятия заметок и позволяет позже заменить UI или хранилище без изменения FFmpeg/Whisper pipeline.

Начиная со stage8 Markdown сохраняется в выбранную пользователем библиотеку (default `Documents\WhisperMd`). Старый fallback рядом с исходной записью сохранён только для snapshot/test-контрактов без `LibraryDirectory`. Существующий `.md` не перезаписывается молча: решение передаётся через `INoteConflictResolver`.

## Persistence and library layer (stages 7–9)

Начиная со stages 7–9 пользовательские параметры и история отделены от WPF Views:

```text
WPF Views
  -> ViewModels
  -> AppSettingsService -----------------> settings.json
  -> TranscriptionJob (settings snapshot)
  -> TranscriptionJobRunner -> backend
  -> MarkdownNoteService -> library/*.md
  -> NoteHistoryService -----------------> history.db (SQLite)
```

`AppSettingsService` хранит постоянные настройки в `%LOCALAPPDATA%\WhisperMd\settings.json`.
Каждый новый `TranscriptionJob` получает копию backend/language/threads/library path, поэтому изменение настроек после Start не меняет выполняющуюся очередь.

`NoteHistoryService` индексирует только уже сохранённые Markdown. SQLite не является владельцем пользовательского текста: основной артефакт остаётся обычным `.md`, а БД содержит индекс/метаданные/копию текста для поиска. Удаление записи из истории не удаляет Markdown с диска.

Папка Markdown задаётся настройкой библиотеки. По умолчанию используется `Documents\WhisperMd`.

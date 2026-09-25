# Transcription

Единый локальный проект для разработки приложения транскрибации и расшифровки записей через `whisper.cpp`. Внутри находятся исходники Windows-приложения, модель, обе сборки движка, FFmpeg, Vulkan SDK, рабочие данные и готовый автономный релиз.

## Быстрый запуск

Запустить `Запустить WhisperMd.cmd` из корня проекта либо напрямую:

```text
releases\WhisperMd-v0.0.1\WhisperMd.exe
```

Legacy WinForms позволяет обработать один файл и сохраняется как рабочая точка возврата. Новый WPF-интерфейс v1 поддерживает очередь, группы, progress/cancellation, Markdown-библиотеку, историю и постоянные настройки.

## Поток работы

1. Скачать файл из Telegram в `inbox` или выбрать его в оконном приложении.
2. Запустить `WhisperMd` либо `transcribe.ps1`, передав путь к записи.
3. Проверить результаты в `output`.
4. Перенести полезный текст в Obsidian.
5. После проверки удалить запись либо перенести её в `archive`.

## Папки

- `src/WhisperMd.App` — новый WPF-интерфейс v1 (очередь, группы и application-layer исполнения).
- `src/WhisperMd` — legacy Windows Forms-приложение, сохраняемое как рабочая точка возврата.
- `releases` — локальные готовые сборки приложения.
- `app/whisper.cpp` — исходники и собранные CPU/Vulkan-версии `whisper.cpp`.
- `models` — мультиязычная модель Whisper `small` (`ggml-small.bin`).
- `inbox` — новые записи.
- `working` — временные WAV 16 кГц mono.
- `output` — расшифровки TXT, SRT и JSON.
- `archive` — проверенные исходные записи, если их нужно сохранить.
- `tools` — локальные FFmpeg и Vulkan SDK.
- `scripts` — проверка среды, сборка и запуск GUI.
- `docs` — архитектура и инструкция разработчика.

## Запуск

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\transcribe.ps1 -InputPath .\inbox\lecture.m4a
```

Сценарий сам преобразует запись в рабочий WAV, создаёт отдельную папку результата и сохраняет в ней:

- обычный текст `.txt`;
- субтитры с временными метками `.srt`;
- подробные данные `.json`.

По умолчанию используется русский язык и Vulkan-сборка, если она успешно установлена. CPU-сборку можно выбрать явно:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\transcribe.ps1 -InputPath .\inbox\lecture.m4a -Backend cpu
```

Исходная запись не удаляется автоматически. Временный WAV удаляется только после успешной транскрибации.

Для записи не на русском языке укажите язык вручную, например `-Language en`. Чтобы сохранить рабочий WAV для диагностики, добавьте `-KeepWav`.

Версии компонентов и результаты самопроверки записаны в `INSTALLATION.md`.

## Разработка

Открыть `Transcription.sln` в Visual Studio либо выполнить:

```powershell
dotnet build .\Transcription.sln -c Release
```

Проверка всей локальной среды:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-environment.ps1
```

Публикация новой автономной сборки в `releases\current`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-gui.ps1
```

Подробности: `docs\DEVELOPMENT.md` и `docs\ARCHITECTURE.md`.

### Новый WPF-интерфейс

Проверочная сборка нового приложения:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-wpf.ps1
```

WPF формирует immutable `TranscriptionJob` и исполняет записи последовательно через `TranscriptionJobRunner -> IWhisperBackendClient -> WhisperBackendClient -> transcribe.ps1`. Stage5 добавляет progress/cancellation, stage6 формирует Markdown в режимах Separate / Combined / CustomGroups, stages7–9 добавляют SQLite-историю, постоянные настройки, библиотеку Markdown и UX-доработки. После их Windows-проверки до v1 остаются installer и финальный release smoke-test.

Быстрая проверка текущего WPF/application-layer:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-stage6-9.ps1

# либо двойным кликом:
.\scripts\verify-stage6-9.cmd
```

Постоянные пользовательские данные WPF:

- `%LOCALAPPDATA%\WhisperMd\settings.json` — настройки;
- `%LOCALAPPDATA%\WhisperMd\history.db` — SQLite-индекс истории;
- `Documents\WhisperMd` — Markdown-библиотека по умолчанию (можно изменить в настройках).


## Сборка v1 installer

Финальный Windows-релиз собирается из локально установленных runtime-зависимостей проекта:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

Скрипт публикует WPF как self-contained `win-x64`, проверяет SHA-256 `ggml-small.bin`, формирует минимальный release payload и вызывает Inno Setup 6. Итог: `release/WhisperMd-Setup-1.0.0.exe`.

Если Inno Setup 6 не установлен, его можно установить через `winget install --id JRSoftware.InnoSetup -e`, затем повторить сборку.

В установленном приложении модель, `whisper.cpp`, FFmpeg и `transcribe.ps1` находятся рядом с `WhisperMd.exe`, а временные WAV и технические TXT/SRT/JSON WPF пишет в `%LOCALAPPDATA%\WhisperMd\runtime`. Markdown-библиотека и история остаются пользовательскими данными и не удаляются при деинсталляции.

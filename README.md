# Transcription

Единый локальный проект для разработки приложения транскрибации и расшифровки записей через `whisper.cpp`. Внутри находятся исходники Windows-приложения, модель, обе сборки движка, FFmpeg, Vulkan SDK, рабочие данные и готовый автономный релиз.

## Быстрый запуск

Запустить `Запустить WhisperMd.cmd` из корня проекта либо напрямую:

```text
releases\WhisperMd-v0.0.1\WhisperMd.exe
```

Оконное приложение позволяет выбрать аудиофайл, запускает локальную модель и сохраняет Markdown рядом с исходной записью.

## Поток работы

1. Скачать файл из Telegram в `inbox` или выбрать его в оконном приложении.
2. Запустить `WhisperMd` либо `transcribe.ps1`, передав путь к записи.
3. Проверить результаты в `output`.
4. Перенести полезный текст в Obsidian.
5. После проверки удалить запись либо перенести её в `archive`.

## Папки

- `src/WhisperMd` — исходный код Windows Forms-приложения.
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

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

# WhisperMd — контекст для продолжения разработки

Срез состояния: **24 сентября 2026 года**.

- stages 0–4 закрыты на Windows;
- stages 5–9 фактически Windows-verified; остаётся один безопасный ручной acceptance check: удалить тестовую запись из History и подтвердить, что `.md` остался на диске;
- stages 10–11 реализованы в исходниках и ожидают финальную Windows-сборку installer + installed release smoke-test.

Главные источники: `docs/WORKLOG_2026-09-23.md`, `docs/V1_ROADMAP.md`, `docs/RELEASE_CHECKLIST_V1.md`.

## Цель v1

Локальное Windows-приложение для пакетной транскрибации лекций через `whisper.cpp`: очередь, группы, contextual prompt, progress/ETA/cancellation, Markdown, SQLite-history/search, persistent settings и installer. Real-time не входит в v1.

## Архитектура

```text
WPF Views -> ViewModels -> immutable TranscriptionJob
                       -> TranscriptionJobRunner
                       -> IWhisperBackendClient
                       -> WhisperBackendClient
                       -> transcribe.ps1
                       -> FFmpeg + whisper.cpp

TranscriptionJobRunResult -> MarkdownNoteService -> Markdown library
                                             -> NoteHistoryService -> SQLite
AppSettingsService -> settings.json
```

UI не знает CLI/Vulkan/FFmpeg деталей; backend не знает о WPF/history/groups.

## Пути installed-mode

| Объект | Путь |
| --- | --- |
| Default install | `%LOCALAPPDATA%\Programs\WhisperMd` |
| Settings | `%LOCALAPPDATA%\WhisperMd\settings.json` |
| SQLite history | `%LOCALAPPDATA%\WhisperMd\history.db` |
| Runtime working | `%LOCALAPPDATA%\WhisperMd\runtime\working` |
| Runtime outputs | `%LOCALAPPDATA%\WhisperMd\runtime\output` |
| Default Markdown library | `Documents\WhisperMd` |

Source/dev manual `transcribe.ps1` without runtime path arguments keeps project-local `working/output`.

## Stages 5–9 — Windows verification

Confirmed:

- WPF Release 0 warnings / 0 errors;
- stage6 and stage7–9 automatic verifiers PASS;
- real queue, progress, elapsed, ETA;
- Markdown/group order and conflict copy;
- History persistence, Cyrillic title/content search, open file/folder;
- settings persistence and reset;
- cancellation kills process tree, pending items do not start, no Markdown on cancelled run, repeat Start works.

Only remaining manual acceptance check: remove a **test** History entry and confirm its physical Markdown file remains. Code path deletes only SQLite metadata.

## Stages 10–11 — implemented, Windows verification pending

### Release build

`build-release-payload.ps1` publishes WPF self-contained `win-x64`, verifies model SHA-256 and stages only runtime dependencies. `build-installer.ps1` calls Inno Setup 6 and should produce:

`release\WhisperMd-Setup-1.0.0.exe`

Convenience launcher: `scripts\build-v1-release.cmd`.

If Inno Setup is absent:

```powershell
winget install --id JRSoftware.InnoSetup -e
```

### Final verification

After installing to a fresh/custom directory, run `scripts/verify-installed-release.ps1 -InstallDirectory <dir> -InputPath <short recording>` from the source tree. It validates installed payload + model hash + CPU/Vulkan backend without relying on source runtime assets.

Then complete `docs/RELEASE_CHECKLIST_V1.md`, especially GUI launch from installed shortcut, queue/notes/history/settings persistence and clean uninstall behavior.

## Important release principles

- Installer does not contain source tree, SDK, recordings or dev outputs.
- Vulkan SDK is build-time only; runtime Vulkan comes from graphics drivers. Auto retains CPU fallback.
- User history/settings/Markdown must not be deleted by uninstall.
- `transcribe.ps1` remains the single orchestration layer.
- Current threads contract is **1–32** end-to-end.
- GitHub is still not the source of truth until v1 release is frozen.

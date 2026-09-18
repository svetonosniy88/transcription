# Локальные релизы

- `current/WhisperMd.exe` — актуальная автономная сборка из `src/WhisperMd`.
- `WhisperMd-v0.0.1/WhisperMd.exe` — резервная исходная сборка, перенесённая из `C:\Program Files\Programs\WhisperMd` 18 сентября 2026 года.

EXE-файлы не входят в Git. Новая версия создаётся скриптом `scripts/build-gui.ps1`; корневой файл `Запустить WhisperMd.cmd` сначала ищет сборку в `current`, затем использует резервную.

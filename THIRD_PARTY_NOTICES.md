# WhisperMd — third-party components

WhisperMd v1.0 bundles or relies on the following third-party components in its release payload:

- **whisper.cpp** — local Whisper inference engine. Preserve the upstream license file from the local `app/whisper.cpp` checkout in release builds when present.
- **OpenAI Whisper small multilingual model** — `ggml-small.bin`.
- **FFmpeg** — audio conversion. Preserve license/copying files from the local FFmpeg distribution when present.
- **Microsoft.Data.Sqlite / SQLitePCLRaw** — SQLite access used by the WPF application. Their NuGet runtime files are included by `dotnet publish`.
- **.NET 8 runtime** — included in the self-contained Windows publish.

The release build script copies available upstream license/notice files into `third-party-licenses`. Before public redistribution, review the exact licenses of the dependency versions being shipped and retain all required notices.

# WhisperMd v1.0 — release checklist

## Build and installer

- [x] `scripts\build-installer.ps1` completes successfully.
- [x] `release\WhisperMd-Setup-1.0.0.exe` is produced.
- [x] Installer works without the source repository.
- [x] Per-user installation succeeds without elevation.
- [x] Custom installation directory works.
- [x] Start Menu shortcut is created and points to the installed EXE.
- [x] Optional desktop shortcut is created and launches the app.
- [x] Application starts after installation.
- [x] Uninstall removes program files but does not delete `%LOCALAPPDATA%\WhisperMd` or the user's Markdown library.

## Installed runtime

- [x] `scripts\verify-installed-release.ps1` passes payload verification.
- [x] Installed CPU backend transcribes a short real recording.
- [x] Installed Vulkan backend transcribes a short real recording on supported hardware.
- [x] Application runtime WAV/output are written under `%LOCALAPPDATA%\WhisperMd\runtime`, not the installation directory.
- [x] No source tree, SDK, `.git`, `bin/obj`, test recordings or development output are bundled (payload verifier and spot check).

## Functional v1 regression

- [x] One short file — Vulkan (installed backend verifier).
- [x] One short file — CPU (installed backend verifier).
- [ ] Auto backend / Vulkan→CPU fallback: Auto selected Vulkan in the installed GUI; fallback was not naturally triggered.
- [x] Multiple files execute sequentially.
- [ ] Separate notes mode.
- [x] Combined note mode.
- [ ] Custom groups mode.
- [ ] Queue reordering.
- [ ] Context prompt.
- [x] Cancellation stops the process tree and pending items.
- [ ] One failed file does not prevent later queue items.
- [ ] Existing Markdown conflict dialog: replace / copy / cancel.
- [x] Restart preserves settings and history.
- [x] History content search works with uppercase Cyrillic matching.
- [ ] Open note / show in folder works.
- [ ] Removing an entry from History leaves the `.md` file untouched.

## Release acceptance

- [x] WPF build: 0 warnings / 0 errors.
- [x] Stage6 verifier: PASS.
- [x] Stage7–9 verifier: PASS.
- [x] Installer build: PASS.
- [x] Installed payload/backend verifier: PASS.
- [x] No critical UI/runtime defects found in the installed build.
- [x] WORKLOG records the final installer path, size, installation directory and smoke-test results.

The unchecked functional items belong to stages 0–9, which were accepted before this release pass; they were not repeated on the installed copy. The stage 10–11 release acceptance above passed. **WhisperMd v1.0**.

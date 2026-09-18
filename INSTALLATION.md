# Установленная среда

Состояние на 18 сентября 2026 года.

## Компоненты

- `whisper.cpp` v1.9.4, commit `927cfce`.
- Мультиязычная модель Whisper `small` — `models/ggml-small.bin`.
- CPU-сборка с AVX2/FMA для Ryzen 5 3600.
- Vulkan-сборка для Radeon RX 580.
- Vulkan SDK 1.4.357.0, установлен локально в `tools/VulkanSDK`.
- FFmpeg 9.0.1 Essentials для M4A, AAC, MP3 и других форматов.
- Windows Forms-приложение `WhisperMd` перенесено в `src/WhisperMd`; старая автономная сборка сохранена в `releases/WhisperMd-v0.0.1`, свежая сборка из проектных исходников — в `releases/current`.

Глобальные переменные Windows не менялись: все компоненты находятся внутри этой папки.

## Контрольные суммы загрузок

- `ggml-small.bin`: SHA-256 `1BE3A9B2063867B937E64E2EC7483364A79917E157FA98C5D94B5C1FFFEA987B`.
- `ffmpeg-release-essentials.zip`: SHA-256 `FEC81AE03971D9DD4BE3EBE02E263BD2EC1D789483F931BDBA5F5715E65DA2E9`.
- `vulkan-sdk-1.4.357.0.exe`: SHA-256 `81F474711E9042F4CD22B31B2F7A8870DB2E428B21586FB43DD80150BE97310D`.

## Самопроверка

Тестовая запись длительностью 120 секунд была расшифрована моделью `small`:

- Vulkan / Radeon RX 580: 17,97 с.
- CPU / Ryzen 5 3600: 23,39 с.

Vulkan выбран режимом по умолчанию. Если он завершится с ошибкой, `transcribe.ps1` автоматически повторит задачу на процессоре.

Среду можно повторно проверить скриптом `scripts/check-environment.ps1`.

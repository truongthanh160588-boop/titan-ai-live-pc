# Titan AI Live PC (TitanAILivePC)

Windows desktop application for simulated livestream selling support with OBS overlay, AI reply generation, and text-to-speech.

## Tech Stack

- Windows
- .NET 8
- WPF
- MVVM-style structure (`Core`, `Models`, `Services`, `ViewModels`)

## Features (v1)

1. Simulated live comment reader
2. AI reply engine (OpenAI key or offline demo mode)
3. Built-in Titan knowledge base
4. OBS browser overlay server (`http://localhost:8787/overlay`)
5. Text-to-speech with mute/unmute
6. OBS WebSocket connection placeholder and status logging

## Run

1. Open `TitanAILivePC.sln` in Visual Studio 2022 or newer.
2. Ensure .NET 8 SDK and WPF workload are installed.
3. Set `App.Wpf` as startup project.
4. Run the application.

## One Build Folder (avoid testing wrong build)

Use this script to always build to one fixed test location:

`.\build-release-one.ps1`

It will:

- clean solution in `Release`
- publish `App.Wpf` to `TitanAILivePC\_build\release\App.Wpf`
- publish `TitanCameraServer` to `TitanAILivePC\_build\release\TitanCameraServer`
- launch app from that fixed folder

## OBS Overlay Setup

1. Run TitanAILivePC.
2. Click **Start Overlay Server**.
3. Open OBS.
4. Add **Browser Source**.
5. URL: `http://localhost:8787/overlay`.
6. Width `1920`, Height `1080`.

## Notes

- If OpenAI API key is empty, the app automatically uses offline demo reply mode.
- `ObsWebSocketService` is intentionally a placeholder in v1.

## OCR Setup (Tesseract)

To use OCR chat capture reliably (Vietnamese only):

1. Create a `tessdata` folder in one of these locations:
   - `App.Wpf/bin/Debug/net8.0-windows/tessdata`
   - `App.Wpf/tessdata`
2. Copy `vie.traineddata` into that `tessdata` folder.
3. Start/restart the app.
4. In app, click **Check OCR Setup**.
5. Expected status: `OCR tiếng Việt sẵn sàng`.

If missing, status shows:

- `Thiếu dữ liệu OCR tiếng Việt: vie.traineddata`

You can click **Open tessdata folder** to open/create the runtime folder quickly.

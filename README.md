# 🎵 YTM Lossless Hybrid Client

![License: MIT](https://img.shields.io/badge/License-MIT-red.svg)
![NET](https://img.shields.io/badge/.NET-8.0-blue.svg)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)
![Audio](https://img.shields.io/badge/Audio-WASAPI%20Exclusive%20(Bit--Perfect)-brightgreen.svg)

**YTM Lossless Hybrid Client** is an open-source, high-fidelity desktop client designed for audiophiles. It combines the familiar user experience of YouTube Music with a low-level C# audio engine (`NAudio`) that streams bit-perfect, lossless audio directly to your DAC or audio hardware via **WASAPI Exclusive mode**.

---

## 🌟 Key Features

- 🔊 **WASAPI Exclusive Bit-Perfect Audio:** Completely bypasses the Windows Software Audio Mixer (`AudioSrv`) and sample rate converters to deliver pure, unadulterated audio directly to your sound hardware.
- 🎛️ **Modular Lossless Resolver (`ILosslessProvider`):** Features a pluggable provider pipeline that resolves lossless audio streams (FLAC/Hi-Res) via APIs like **Jamendo** or your **Local FLAC Library**.
- ⚡ **Smart Fallback Engine:** Automatically detects if a track is unavailable in lossless format and seamlessly restores YouTube Music's web audio stream so your music never stops.
- 🛡️ **API & Network Level Ad Blocking:** Hooks into YouTube Music's internal `fetch` API payloads to strip ad placement objects before rendering, backed by C# web resource request filters.
- 🔄 **Bidirectional Player Sync:** Real-time synchronization of playback states (**Play**, **Pause**, and **Seek / Position Changes**) between the YouTube Music frontend and the C# NAudio engine.
- ⚙️ **Configurable UI & Settings:** Built-in WPF Settings dialog backed by persistent `appsettings.json` configuration for custom directories, API keys, and audio modes.

---

## 🏗️ Architecture Overview

The application utilizes a hybrid architecture bridging **Microsoft WebView2** (Chromium-based frontend) and **.NET C# WPF** (Low-level backend) through Inter-Process Communication (IPC).

```
┌─────────────────────────────────────────────────────────┐
│            YouTube Music Web UI (WebView2)              │
│  - Chromium JS Interceptor & MutationObserver           │
│  - Fetch/XHR Payload Hooking (Ad Removal)               │
└────────────────────────────┬────────────────────────────┘
                             │
                  IPC (WebMessage / JSON)
                             │
┌────────────────────────────▼────────────────────────────┐
│                    C# Hybrid Engine                     │
│  - LosslessResolver Pipeline (Jamendo / Local FLAC)     │
│  - Smart Fallback & Web Mute Controller                 │
└────────────────────────────┬────────────────────────────┘
                             │
                NAudio WASAPI Engine
                             │
┌────────────────────────────▼────────────────────────────┐
│          Hardware Sound Card / External DAC             │
└─────────────────────────────────────────────────────────┘
```

---

## 🚀 Getting Started

### Prerequisites

- **Windows 10/11** (64-bit)
- **.NET 8.0 SDK** or later
- **Visual Studio 2022** (with *.NET Desktop Development* workload)
- **WebView2 Runtime** (pre-installed on Windows 11)

### Installation & Building from Source

1. **Clone the repository:**
   ```bash
   git clone https://github.com/egehansimarikk/YtmHybridClient.git
   cd YtmHybridClient
   ```

2. **Open the solution:**
   Launch `YtmHybridClient.sln` in Visual Studio 2022.

3. **Restore NuGet Packages:**
   Visual Studio will automatically restore the required dependencies:
   - `NAudio` (WASAPI Engine)
   - `Microsoft.Web.WebView2` (Chromium Runtime)
   - `System.Text.Json`

4. **Build and Run:**
   Press `F5` to build and launch the application in Debug mode, or build as `Release` for optimized performance.

---

## ⚙️ Configuration

The client automatically creates and manages `appsettings.json` located at `%AppData%\YtmHybridClient\appsettings.json`. You can also configure these settings directly through the in-app **⚙ Settings** button in the top bar:

| Setting Key | Type | Description | Default |
| :--- | :--- | :--- | :--- |
| `LocalFlacFolder` | `string` | Local directory scanned for offline FLAC matches. | `C:\Music\FLAC` |
| `JamendoClientId` | `string` | API Key for Jamendo royalty-free FLAC resolution. | `6a4bec36` |
| `UseWasapiExclusive` | `bool` | Enables WASAPI Exclusive hardware lock. | `true` |
| `EnableSmartFallback` | `bool` | Unmutes YTM audio if no external lossless source is found. | `true` |

---

## 🛠️ Extending Lossless Providers

You can add custom lossless providers (e.g., Tidal, Subsonic, Navidrome, Plex) by implementing the `ILosslessProvider` interface in C#:

```csharp
public interface ILosslessProvider
{
    string Name { get; }
    Task<string?> GetFlacStreamUrlAsync(string title, string artist, string album);
}
```

Once implemented, register your provider inside `LosslessResolver.cs` to integrate it into the fallback resolution chain.

---

## 🤝 Contributing

Contributions are welcome! Feel free to open issues or submit Pull Requests for bug fixes, new features, or additional `ILosslessProvider` integrations.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📜 License

Distributed under the **MIT License**. See `LICENSE` for more information.
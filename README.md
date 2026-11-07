# YTM Lossless Hybrid Client

A Windows desktop client for YouTube Music that can play a track from another audio source
(Jamendo, Audius, SoundCloud, your own FLAC files) when a match is found, and falls back to YouTube Music otherwise.
A badge in the top bar always shows where the sound is coming from.

🇹🇷 [Türkçe README](README.tr.md)

## Features

- YouTube Music in an embedded WebView2 browser (your own login, your own library)
- Detects track changes and looks for the same song in other sources
- Audio sources: **Jamendo**, **Audius**, **SoundCloud**, **Local FLAC**, **JioSaavn** (experimental, see below)
- Source badge: `🔍 Searching`, `⏳ opening`, `🎵 <source>`, `▶ YouTube Music`, `🔇 lossless not found`
- **Quick source toggle**: click the badge or 🔄 to cycle the current song through Jamendo → Audius → SoundCloud → YouTube Music
- **Strict matching** so the wrong song is not played
- **WASAPI Exclusive / Shared** output, with automatic fallback to Shared
- **Audio output device** selection
- **External source gain boost** (+1.5 dB by default) with a soft limiter, so Jamendo, Audius and SoundCloud sound as loud as YouTube Music
- **Smart Fallback**: play YouTube Music's own audio when nothing better is found (or stay silent)
- Turkish and English interface

## Requirements

- Windows 10 or 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  (already installed on most up-to-date Windows 10/11 systems)

## Installation

1. Download the latest installer (`.exe`) from the [Releases](../../releases) page and run it.
2. Launch the app after the installation.
3. Sign in to YouTube Music in the app window.
4. Open **Settings** and choose your sources.

## Build from source

```powershell
git clone https://github.com/egehansimarikk/YtmHybridClient
cd YtmHybridClient
dotnet build -c Release
```

Or open the solution in Visual Studio 2022 or newer (.NET 9 SDK) and build in **Release**.

## Audio sources

| Source | What it is | Notes |
|---|---|---|
| **Jamendo** | Free music released under Creative Commons licenses | Needs a free API Client ID (see below). Independent artists, small catalog |
| **Audius** | Open music platform with artist-uploaded tracks | No API key. Mostly electronic, hip-hop and independent music |
| **SoundCloud** | Music streaming platform | Unofficial web API, no account needed. Only fully playable tracks with a direct MP3 stream are used (typically 128 kbps, **not** lossless). Can break when SoundCloud changes its site |
| **Local FLAC** | FLAC files in a folder you choose | The only way to get true lossless playback of your favorite songs |
| **JioSaavn** | Unofficial community APIs | **Unreliable.** Public instances are rate-limited or offline; not recommended |

> **Expect a lot of "YouTube Music".** Jamendo and Audius are independent catalogs. Most mainstream
> songs are not there and will play from YouTube Music. That is by design.

### Jamendo Client ID

Create a free application at [developer.jamendo.com](https://developer.jamendo.com/) and paste your
Client ID into **Settings → Jamendo API Client ID**. Using your own ID avoids shared rate limits.

## Settings

| Setting | Description |
|---|---|
| Language | Turkish or English |
| Primary / Secondary source | Order in which sources are tried |
| External source gain | Boost for Jamendo, Audius and SoundCloud in dB (0 = off, default +1.5). Not applied to Local FLAC. Applied live while you move the slider |
| Audio output device | Output device for external sources. *System default* if empty |
| Local FLAC folder | Folder searched by the Local FLAC source |
| Jamendo API Client ID | Your Jamendo API key |
| Strict matching | Require title **and** artist to match. Turn off to get more matches at the risk of wrong songs |
| WASAPI Exclusive | Bit-perfect output that locks the device. Falls back to Shared if it fails |
| Smart Fallback | Play YouTube Music's audio when no lossless match is found. If off, those songs stay silent |

Settings are stored in `%AppData%\YtmHybridClient\appsettings.json`.
The browser profile (login, cookies) is stored in `%AppData%\YtmHybridClient\WebView2Profile`.

Changing the output device or WASAPI mode is applied right away to a song playing from an external source. Audio from the
"YouTube Music" fallback always uses the Windows default device.

## Troubleshooting

- **Badge stays on "Searching..."** – Update to v1.2.0 or newer.
- **Source never shows up** – Check that the song exists in that catalog. Try turning Strict matching off.
- **No sound at all** – Smart Fallback may be off, or the selected output device is in use by another
  application in Exclusive mode. Try turning Exclusive off.
- **Output device does not change** – The device setting only applies to songs played by this app
  (Jamendo, Audius, SoundCloud, Local FLAC). Songs with the "▶ YouTube Music" badge play through the
  browser component and use the Windows default device. To send them elsewhere, open Windows
  **Settings → System → Sound → Volume mixer** and choose the output device for this app / *Microsoft Edge WebView2*.
- **Logs** – When running a **Debug** build from Visual Studio, the Output window shows lines such as
  `[Resolver]`, `[Jamendo]`, `[Audius]`, `[SoundCloud]`, `[AudioEngine]` that explain every decision.
  Release builds do not write these logs.

## Disclaimer

- This project is **not affiliated with** Google, YouTube, Jamendo, Audius or JioSaavn.
- The app hides and filters ads on the YouTube Music page. This may violate YouTube's Terms of Service.
  Use it at your own risk.
- Jamendo tracks are released under Creative Commons licenses; check each track's license before using
  them for anything beyond personal listening. Audius asks apps to credit artists.
- The SoundCloud source uses SoundCloud's unofficial web API with a client ID read from the public website.
  This is not covered by SoundCloud's API terms and may stop working at any time. Previews and restricted
  tracks are skipped.
- The JioSaavn source relies on unofficial APIs and may use content without a license. It is kept only for
  compatibility and is not recommended.

## License

Add a `LICENSE` file of your choice (for example MIT) before publishing.

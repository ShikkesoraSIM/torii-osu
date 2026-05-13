<p align="center">
  <img width="220" alt="Torii Nova" src="assets/torii-512.png">
</p>

<h1 align="center">Torii Nova</h1>

<p align="center">
  <em>A community fork of <a href="https://github.com/ppy/osu">osu! (lazer)</a> with a private server, extra
  client polish, and experimental rendering work.</em>
</p>

<p align="center">
  <a href="https://github.com/ShikkesoraSIM/torii-osu/releases/latest">
    <img alt="Latest release" src="https://img.shields.io/github/v/release/ShikkesoraSIM/torii-osu?include_prereleases&label=latest&color=ff66ab">
  </a>
  <a href="https://github.com/ShikkesoraSIM/torii-osu/releases">
    <img alt="Downloads" src="https://img.shields.io/github/downloads/ShikkesoraSIM/torii-osu/total?color=ff66ab">
  </a>
</p>

---

> [!IMPORTANT]
> **Torii Nova is an unofficial fork of [osu!](https://github.com/ppy/osu).**
>
> - We are **not affiliated with, endorsed by, or supported by ppy Pty Ltd**, peppy, or the osu! development team.
> - **osu!**, the **"osu!"** name, the **"lazer"** codename, and all related branding are trademarks of **ppy Pty Ltd**. Torii Nova is distributed under a different name (**Torii Nova**) and uses its own assets and identity.
> - **Please support the original project.** osu! is free, lovingly maintained, and the only reason this fork can exist. If you enjoy playing rhythm games, the best way to thank the original devs is to:
>   - Play on the [official osu! servers](https://osu.ppy.sh) (your scores there count for the real leaderboards).
>   - Become an [osu! supporter](https://osu.ppy.sh/home/support) — it directly funds upstream development.
>   - File issues / PRs on the [upstream repo](https://github.com/ppy/osu) instead of pinging the upstream team about Torii.
> - Torii Nova exists to experiment with ideas that may not fit upstream's roadmap, run a small community server, and to learn. **It is not a replacement for osu! and we do not want it to be.** If you're new to the game, please install [official osu!](https://osu.ppy.sh/home/download) first.

---

## What Torii Nova is

A buildable client + a small private server. The client is forked from osu! lazer with extra UI surfaces, a few new gameplay-adjacent systems, performance work, and an experimental Direct3D 12 renderer. The server is a separate project (`g0v0-server`) and hosts scores, leaderboards, multiplayer rooms, chat, and presence for users who choose to play there.

**You do not need to use the Torii server to use the Torii client.** You can sign in against the official osu! server at any time — the client lets you switch via Settings → Torii → Server.

## What it is **not**

- It is not "modded osu!" or "cheat osu!". No relaxation of anti-cheat, no rank-skipping, no PP injection. Submitting scores to the official osu! server while running this client is unsupported and is explicitly discouraged by both us and upstream — the official server's anti-cheat will detect modified clients and restrict you.
- It is not a "private osu!" replacement aimed at general players. We do not advertise the server, we do not link the client in upstream issues / Discord, and we don't try to pull anyone away from official osu!. If you found Torii on your own and want to make it your main rhythm-game home, that's entirely your call — we just won't be the ones recruiting people here.

---

## Features (top-to-bottom)

### Client identity & UI

- **Torii Nova brand**: a gradient torii-gate logo across the window, taskbar, file associations, badges, and shortcuts. Distinctly *not* the osu! logo.
- **Torii toolbar** — a Torii-themed alpha indicator and quick-access strip alongside the upstream toolbar.
- **Settings → Torii section**, where every Torii-specific knob lives instead of being scattered across upstream panels:
  - Server selection (Torii vs. official) + auth flow
  - Briefing overlay defaults (the daily-portal greeting screen)
  - Aura presets (see below)
  - Gameplay tweaks specific to Torii
  - Storage management + Android-specific options
  - Experimental flags (renderer choice, latency mode, etc.)

### Auras

Cosmetic effects rendered around your username + avatar in user panels. Currently registered presets:

- **Admin / Dev / Mod / QAT** — staff-only auras, server-assigned
- **Supporter** — for community supporters
- **Bug Finder** — for users who land merged bug fixes / detailed reports
- **Goof** — for the goofs

Auras are server-attested. The client renders whatever the server says you're entitled to via `GET /api/v2/me/aura-catalog`; you can pick which one to display from the in-game settings picker.

### Verified-client badge

A small gradient torii-gate icon shown next to online users in the Friends / Online overlays — but **only** when the Torii server has matched their build hash to a CI-registered release. This means you can tell at a glance whether someone is on a verified Torii build vs. just any custom client connected to the server. The badge is not granted by self-attestation — it requires the executable's `osu.Game.dll` md5 to match a hash registered by CI on release.

### Torii Briefing

A daily portal overlay greeting users on first login of the day. Shows server status, what's new since last login, friend activity, and quick-jump entries. Designed so the home screen of a Torii session has more "warmth" than an empty Lazer main menu.

### Performance work

- **`net10.0` chain** across `osu.Desktop`, `osu.Game`, and every ruleset / test / tournament / benchmark project, with **CoreCLR runtime** updates. JIT improvements, lower-allocation GC, and SIMD improvements that matter most for gameplay frame pacing.
- **Server GC + Concurrent GC** enabled in `osu.Desktop.csproj` — collections run on dedicated background threads so generation 0/1 pauses stay off the audio + update threads. Standard tuning for realtime workloads.
- **Hiccup queue** — gameplay frame-time outliers are detected, snapshotted, and (with explicit opt-in) uploaded to the Torii server so we can see real-world stutter patterns instead of just our own profiles. Hiccup polling is paused during gameplay so we never *cause* a hiccup measuring one.
- **Low latency providers** — NVIDIA Reflex (on RTX) and AMD Anti-Lag 2 (on RDNA) are wired into the Direct3D 11 backend, with a settings panel that shows which one applies to the current GPU.

### Audio

Audio packages are pinned to ppy's own `ppy.ManagedBass*` forks, matching official osu! 1-for-1 — meaning all of upstream's BASS audio fixes (WASAPI exclusive-mode handling, audio-device hot-plug, MIDI backend patches) ship in Torii Nova too. We deliberately don't run on vanilla ManagedBass, because vanilla is missing those patches.

### Skinning extras

#### Per-combo-color hitcircle textures (legacy skins)

Drop `hitcircle1.png` through `hitcircle8.png` next to your `hitcircle.png`. Torii will use the variant whose number matches the currently-active combo color slot from `skin.ini`'s `[Colours]` section (`hitcircle1.png` ↔ `Combo1`, `hitcircle2.png` ↔ `Combo2`, …).

- The combo color is still applied as a tint over the variant — paint your variants in white/grayscale to let `skin.ini`'s combo colors show through, or in pre-tinted mid-tones for a colored-on-colored effect. Hidden / Flashlight / dim / fade animations all keep working because the tint pipeline is unchanged.
- `@2x` HD textures work the same way they do everywhere else in legacy skins (`hitcircle1@2x.png`, etc.). No extra config.
- **Missing slots fall back to `hitcircle.png`.** Ship only the variants you care about. If you ship `hitcircle1.png` and `hitcircle3.png`, then Combo1 + Combo3 use their variants and Combo2/4/5/6/7/8 use the regular `hitcircle.png` (tinted by their respective combo colors).
- `hitcircleoverlay1.png` etc. work the same way for the overlay layer (the ring drawn over the circle body), as do `sliderstartcircle1..8.png` and `sliderendcircle1..8.png` for slider start/end circles — anywhere `LegacyMainCirclePiece` would have rendered the single base texture, it now also looks for a per-slot variant first.
- This is purely additive — skins that don't ship any numbered variants render exactly as they always have.

The slot index resolves the same way the combo color itself does: `comboIndexWithOffsets mod (number of Combo entries in skin.ini)`. So a `skin.ini` with `Combo1..Combo4` cycles `hitcircle1..hitcircle4` (the variants past index 4 are inert files); a `skin.ini` with `Combo1..Combo8` exposes all eight slots.

### Direct3D 12 backend (experimental)

We maintain a **Direct3D 12** backend for the Veldrid rendering library (in [`torii-veldrid`](https://github.com/ShikkesoraSIM/torii-veldrid)) and wire it into osu-framework's renderer fallback list. The backend works but is still bedding in:

- It is **deliberately hidden from the Settings → Graphics dropdown** to keep curious users from picking it on a hunch.
- Power users can opt in by editing `framework.ini` (`%APPDATA%\osu-torii\framework.ini`) and setting `Renderer = Deferred_Direct3D12`.
- Direct3D 11 (Deferred) remains the default and is the renderer we ship as production.

### Renderer dropdown labels (Nova)

In `Torii Nova` builds, the renderer dropdown re-labels `Deferred_*` entries as `... (Nova)` (e.g. `Direct3D 11 (Nova)`) so users can clearly tell when they're on the experimental deferred path vs. the upstream immediate path.

### Velopack updater with channel routing

Torii uses [Velopack](https://github.com/velopack/velopack) for updates. The release pipeline tags releases with `-torii` for stable (master) and `-nova` for experimental (nova branch) streams, and the in-client Updater pins users to their chosen stream — Nova users only see `-nova` prereleases, stable users only see `-torii` releases. Switching streams is one click in Settings.

### Legacy launcher shim

To keep old `osu-torii.exe` taskbar pins / desktop shortcuts working after the May 2026 binary rename (`osu-torii.exe` → `torii.exe`), Windows releases ship a tiny `osu-torii.exe` next to `torii.exe`. Pre-existing pins keep launching the game transparently for at least a few release cycles, then we'll drop the shim once telemetry confirms nobody relies on it anymore.

---

## The Torii server

`g0v0-server` is a Python (FastAPI) server that talks osu!'s v2 API + spectator + chat protocols. It hosts:

- Scores + leaderboards (per-beatmap + global + country, with daily-challenge support)
- Multiplayer rooms (osu!, taiko, catch, mania)
- Chat (channels + PMs)
- User presence + spectator
- Beatmap proxy + tag/rating systems
- A small admin dashboard for restriction / moderation

The server is **private** and **invite-only** at the moment. If you have an account, you can sign in by switching the server endpoint in `Settings → Torii → Server`. We do not advertise the server publicly and do not accept open signups via the client.

---

## Download

> [!WARNING]
> **Do NOT install Torii Nova if you have not already played osu!.** This is a fork, not the real thing. Install [official osu!](https://osu.ppy.sh/home/download) first. If you decide you want Torii anyway, install it *alongside* official osu! — they use separate data directories and don't conflict.

Grab the latest release from the [Releases page](https://github.com/ShikkesoraSIM/torii-osu/releases). Each release ships:

| Platform | File |
|---|---|
| Windows (x64) installer | `install-win-x64.exe` |
| Windows (ARM64) installer | `install-win-arm64.exe` |
| Windows portable | `portable-win-x64.zip` / `portable-win-arm64.zip` |
| Linux AppImage | `torii-linux-x64.AppImage` / `torii-linux-arm64.AppImage` |
| Linux portable tarball | `portable-linux-x64.tar.gz` / `portable-linux-arm64.tar.gz` |
| macOS (Intel) | `osu.app.Intel.zip` |
| macOS (Apple Silicon) | `osu.app.Apple.Silicon.zip` |
| Android | `torii.apk` |

### Update / migration notes

- Existing installs auto-update via Velopack. Data directory is preserved (`%APPDATA%\osu-torii\` on Windows, equivalent on other platforms), so beatmaps, scores, replays, and settings carry over.
- After the May 2026 rebrand, the Windows binary is `torii.exe`. If you had `osu-torii.exe` pinned to your taskbar, re-pin from the Start menu after first launch (the legacy shim keeps the old pin working short-term, but the canonical pin should target `torii.exe`).

---

## Building from source

This repository is a fork of [`ppy/osu`](https://github.com/ppy/osu). You need three sibling checkouts to build:

```
parent-dir/
├── torii-osu          # this repository
├── torii-framework    # https://github.com/ShikkesoraSIM/torii-framework  (osu-framework fork)
└── torii-veldrid      # https://github.com/ShikkesoraSIM/torii-veldrid    (Veldrid fork with D3D12)
```

`torii-resources` (a fork of `osu-resources`) is pulled as a NuGet package — you don't need a local checkout unless you're modifying resources.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (osu.Desktop targets `net10.0` in Torii Nova; CI also installs .NET 8 for any legacy MSBuild tasks)
- An IDE that supports C# 12+ — [Visual Studio 2022 17.8+](https://visualstudio.microsoft.com/), [JetBrains Rider](https://www.jetbrains.com/rider/), or [VS Code](https://code.visualstudio.com/) with the C# Dev Kit
- Windows is the primary supported dev platform. Linux + macOS builds work via the same `dotnet` CLI but get less day-to-day testing.

### Quick start

```shell
git clone https://github.com/ShikkesoraSIM/torii-framework.git
git clone https://github.com/ShikkesoraSIM/torii-veldrid.git
git clone https://github.com/ShikkesoraSIM/torii-osu.git

cd torii-veldrid
dotnet pack src/Veldrid/Veldrid.csproj -c Release -o bin/Packages/Release
dotnet pack src/Veldrid.MetalBindings/Veldrid.MetalBindings.csproj -c Release -o bin/Packages/Release
dotnet pack src/Veldrid.OpenGLBindings/Veldrid.OpenGLBindings.csproj -c Release -o bin/Packages/Release

cd ../torii-osu
dotnet run --project osu.Desktop -c Debug -f net10.0
```

The first run takes a few minutes (restore + initial build). Subsequent runs are fast.

### Building the legacy launcher shim (Windows release builds only)

The Windows release pipeline also publishes `osu.Desktop.Shim` → `osu-torii.exe` and ships it alongside `torii.exe`. To replicate locally:

```shell
dotnet publish osu.Desktop.Shim/osu.Desktop.Shim.csproj --runtime win-x64 --self-contained -c Release
```

The output ends up at `osu.Desktop.Shim/bin/Release/net10.0/win-x64/publish/osu-torii.exe`.

---

## Contributing

Contributions are welcome, but **please direct general osu! improvements at [upstream](https://github.com/ppy/osu) instead.** Anything that isn't Torii-specific (rulesets, beatmap features, mod logic, core gameplay) lands upstream first; Torii merges upstream regularly. Submitting it here means it only ever ships to Torii users, which isn't what most contributors want.

**Torii-specific work is welcome here**: Torii Nova settings, the briefing overlay, the aura system, performance telemetry, server integration tweaks, D3D12 backend fixes.

Before opening a PR:

- Run `dotnet format` on changed files.
- Don't bump upstream-shared package versions without a good reason (we try to stay close to upstream so merges don't conflict).
- If a change touches behaviour visible to a server, it probably needs a coordinated change in [`g0v0-server`](https://github.com/GooGuTeam/g0v0-server) — flag that in the PR.

---

## License

The Torii Nova client source is licensed under the [MIT License](LICENCE), inheriting from upstream osu!. See the [LICENCE](LICENCE) file for details.

> [!NOTE]
> **MIT does NOT cover the "osu!" / "ppy" / "lazer" branding** — those are trademarks of ppy Pty Ltd. This fork distributes binaries under the "Torii Nova" name and uses its own logo + iconography precisely to stay clear of that trademark. Please do not redistribute Torii Nova under the osu! name.
>
> Game resources (sound effects, fonts, default skin assets) inherited from upstream are covered by the separate [ppy/osu-resources](https://github.com/ppy/osu-resources) license. Torii-original assets (the gradient torii-gate logo, Torii-specific UI graphics) are © Torii Team and available under the same MIT license as the source.

---

## Credits

- [ppy](https://github.com/ppy) and the osu! lazer team — without their work, none of this would exist. Please support [osu!](https://osu.ppy.sh) and consider becoming a [supporter](https://osu.ppy.sh/home/support).
- [shigetiro](https://github.com/shigetiro) — earlier fork work that Torii Nova builds on.
- [GooGuTeam](https://github.com/GooGuTeam) — `g0v0-server` upstream + ongoing collaboration on the server side.
- The Torii community — for bug reports, ideas, and putting up with experimental builds.

---

<p align="center">
  <sub>Made with respect for the original project. Please support <a href="https://osu.ppy.sh">osu!</a>.</sub>
</p>

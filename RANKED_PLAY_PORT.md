# Ranked Play port — work in progress

This branch (`feature/ranked-play`) is the working tree for porting upstream's
"Ranked Play" / "Matchmaking" feature into Torii. **Do not merge into master
until the Open Items below are resolved.**

## Status snapshot

| Layer | State | Notes |
|---|---|---|
| Data models (DTOs) | ✅ Ported | `Online/Matchmaking/Requests/`, `Online/Matchmaking/Responses/`, `Online/API/Requests/Responses/APIMatchmakingPool.cs`, `Online/API/Requests/Responses/APIUserMatchmakingStatistics.cs`, `Online/Matchmaking/MatchmakingRoomInvitationParams.cs` |
| Existing matchmaking core | ✅ Already in repo | The base hub interfaces and `MatchTypes/Matchmaking/*` shipped in earlier syncs |
| **`MatchmakingJoinLobbyWithParams` / `MatchmakingRoomInvitedWithParams` interface methods** | ⚠️ Implementation stubs needed | `IMatchmakingClient` and `IMatchmakingServer` were re-fetched but `MultiplayerClient`, `OnlineMultiplayerClient`, and `TestMultiplayerClient` need new abstract overrides + SignalR wiring |
| Ranked Play screens (`Screens/OnlinePlay/Matchmaking/RankedPlay/*`, ~70 files) | ❌ Not ported | Heavy dependency cascade — see Open Items |
| Wedge refactor (`Screens/Select/BeatmapTitleWedge.*`, `BeatmapMetadataWedge.*`) | ❌ Not ported | Required by `GameplayWarmupScreen.*` and `ResultsScreen.ScoreStatisticsDisplay`. Pulls in `ISongSelect`, `BeatmapSetLookupResult` from upstream's SongSelect refactor |
| Matchmaking profile widgets (`Overlays/Profile/Header/Components/MatchmakingStats*`) | ❌ Not ported | Depends on `APIUser.MatchmakingStatistics` (new field) |
| Localisation strings (`MultiplayerMatchStrings.SearchingForOpponents`, `MatchIsReady`, `ButtonSystemStrings.RankedPlay`, etc.) | ❌ Not ported | Need to copy from `upstream master` |
| `SamplePlaybackHelper` static class | ❌ Not ported | Used throughout new RP screens. Located at `osu.Game/Audio/SamplePlaybackHelper.cs` upstream |
| `PreviewTrack.Looping`, `PreviewTrackManager.IsPlayingPreview` | ❌ Not ported | Audio API additions |
| `UpdateableAvatar.DelayedLoad` | ❌ Not ported | New ctor param |
| `APIUser.UnknownUser` static placeholder | ❌ Not ported | Used by intro screen for absent users |
| `ConfirmExitMultiplayerMatchDialog` | ❌ Not ported | Confirm-exit dialog |
| `ScreenIntro(MatchmakingPoolType)` constructor | ⚠️ Signature drift | Our copy still takes the old parameter list |
| Spectator-side Ranked Play hub | ❌ Not ported | Spectator (m1pp) needs `IRankedPlayServer` + the Ranked Play game state machine |

## What works in this branch

- The original Matchmaking hub code (already shipping on master) keeps building.
- New typed request/response DTOs are now available so any future server
  changes that adopt `MatchmakingJoinLobbyWithParams` etc. will deserialize
  cleanly on the client.
- `feature/ranked-play` cleanly builds (`dotnet build -c Debug osu.Desktop/osu.Desktop.csproj` returns 0 errors).

## Open items, in dependency order

1. **Pull `osu.Game/Audio/SamplePlaybackHelper.cs` from `ppy/master`.** Several
   Ranked Play screens reference it directly. It is a dependency-light static
   utility — should drop in cleanly.

2. **Pull `osu.Game/Audio/PreviewTrack.cs` and `osu.Game/Audio/PreviewTrackManager.cs`
   updates from `ppy/master`** to introduce the `Looping` property and
   `IsPlayingPreview` accessor. Verify our song-select preview code still
   compiles after the diff.

3. **Bring `APIUser.UnknownUser` over.** Static read-only placeholder used
   for "absent users" in the Ranked Play intro. Self-contained.

4. **Stub `MatchmakingJoinLobbyWithParams` and `MatchmakingRoomInvitedWithParams`**
   in `MultiplayerClient`, `OnlineMultiplayerClient`, and `TestMultiplayerClient`.
   Wiring shape mirrors the existing `MatchmakingJoinLobby` /
   `MatchmakingRoomInvited` calls.

5. **Decide on the Wedge refactor.** Upstream's `BeatmapTitleWedge` /
   `BeatmapMetadataWedge` family lives in `osu.Game/Screens/Select/` and is
   used by Ranked Play's gameplay-warmup screen. Pulling them in transitively
   requires upstream's `SongSelect` refactor (`ISongSelect`,
   `BeatmapSetLookupResult`), which conflicts with the heavy Torii song-select
   customisations (Stable / Solo song select forks). Two options:
   - **(a) Skip the warmup screen.** Drop
     `Screens/OnlinePlay/Matchmaking/RankedPlay/GameplayWarmupScreen.*` and
     `ResultsScreen.ScoreStatisticsDisplay` from the port. Ranked Play will
     work without them; users skip straight from pick to gameplay. Most
     pragmatic.
   - **(b) Port the wedges + SongSelect refactor as a separate PR.** Larger
     surface area but unlocks several upstream UI niceties. Recommended for
     a follow-up branch.

6. **Port the Ranked Play screen cluster** (~70 files under
   `Screens/OnlinePlay/Matchmaking/RankedPlay/`). Order:
   - Components (`Card/`, `Components/`, `Hand/`)
   - Screens (`PickScreen`, `OpponentPickScreen`, `DiscardScreen`,
     `EndedScreen`, `GameplayScreen`, `ResultsScreen`)
   - Top-level (`RankedPlayScreen`, `RankedPlaySubScreen`, etc.)
   - Background music & overlay (`BackgroundMusicManager`, `RankedPlayBackground*`)
   - Navigation entry point (`RankedPlayBottomOrnament` from main menu)

7. **Spectator-side ranked-play hub.** `osu-server-spectator` (we use the
   M1PPosu fork at `spectator-m1pp/`) needs the matchmaking and ranked-play
   server-side state machine. The reference implementation is in
   `osu-server-spectator/Hubs/RankedPlay/` upstream — fork to track and merge.

8. **Resources.** Upstream commit `88f0be8b22 Update resources` ships the
   ranked-play card art, sprites, and sound effects. Bump
   `torii-resources` to a commit that includes those (or pull the assets
   directly under `torii-resources/Resources/`).

9. **Framework.** Inspect `ppy/osu-framework` since the matchmaking PR
   (`#34815`) was merged. Any framework-side touch points (DrawSize fillers,
   audio routing) need to come along; otherwise the screens will misbehave
   even if they compile.

## Recommended next session

Option **(5a) — skip the warmup screen** keeps the scope manageable:

```sh
git checkout feature/ranked-play
git checkout ppy/master -- \
  osu.Game/Audio/SamplePlaybackHelper.cs \
  osu.Game/Audio/PreviewTrack.cs \
  osu.Game/Audio/PreviewTrackManager.cs \
  osu.Game/Online/API/Requests/Responses/APIUser.cs \
  'osu.Game/Online/Matchmaking/' \
  'osu.Game/Online/Multiplayer/MatchTypes/Matchmaking/' \
  'osu.Game/Online/Multiplayer/MatchTypes/RankedPlay/' \
  'osu.Game/Online/RankedPlay/' \
  'osu.Game/Screens/OnlinePlay/Matchmaking/'

# remove the wedge-coupled files
rm osu.Game/Screens/OnlinePlay/Matchmaking/RankedPlay/GameplayWarmupScreen.*
rm osu.Game/Screens/OnlinePlay/Matchmaking/RankedPlay/ResultsScreen.ScoreStatisticsDisplay.cs
```

Then iterate the remaining build errors one cluster at a time
(localisation strings, MatchmakingStats, etc). Expect ~2–4 hours of
careful conflict resolution before the screen cluster builds.

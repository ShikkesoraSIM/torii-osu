# Audio audit — May 2026

Snapshot of what I found while investigating "audio feels weird in
Torii" reports. **No code changes were made.** Each finding below is
either evidence of a pre-existing fork divergence or a user-reported
symptom that needs an experiment we haven't run yet.

The intent is to ship audio fixes in the next client release, bundled
with other improvements, so this memo exists to (a) document the
state of the fork right now and (b) be the starting point for that
next batch of work.

## User-reported symptoms

1. **Offset slider lags the game when adjusted right before a play.**
   Adjusting the audio offset on the song-select / pre-gameplay panel
   produces a perceptible hitch when the song goes to start. Upstream
   does not exhibit this.
2. **"Calibrate from recent plays" never accumulates.** The notch
   container on `AudioOffsetAdjustControl` never shows entries; the
   hint text stays at "play a few maps". This implies
   `SessionAverageHitErrorTracker.AverageHitErrorHistory` is empty
   even after a normal session.
3. **"Automatically adjust beatmap offset" toggle has no effect.**
   `BeatmapOffsetControl` reads `OsuSetting.AutomaticallyAdjustBeatmapOffset`
   but the offset is not auto-applied after plays.
4. **WASAPI "doesn't feel the same as official osu!"** for a subset
   of users (anecdotal). One user gets glitching/popping that resolves
   when WASAPI is enabled, then changes character; another reports
   ambient feel changes.

## Findings (fork divergences vs. ppy/osu)

### 1. ManagedBass package family is the community build, not ppy's

`osu-framework/osu.Framework/osu.Framework.csproj` declares the vanilla
community `ManagedBass 4.0.1` packages where upstream uses ppy's
in-house `ppy.ManagedBass 2022.1216.0` family:

```
+ <PackageReference Include="ManagedBass" Version="4.0.1" />
+ <PackageReference Include="ManagedBass.Fx" Version="4.0.1" />
+ <PackageReference Include="ManagedBass.Mix" Version="4.0.1" />
+ <PackageReference Include="ManagedBass.Wasapi" Version="4.0.1" />
+ <PackageReference Include="ManagedBass.Asio" Version="4.0.1" />
- <PackageReference Include="ppy.ManagedBass" Version="2022.1216.0" />
- <PackageReference Include="ppy.ManagedBass.Fx" Version="2022.1216.0" />
- <PackageReference Include="ppy.ManagedBass.Mix" Version="2022.1216.0" />
- <PackageReference Include="ppy.ManagedBass.Wasapi" Version="2022.1216.0" />
```

The bundled native BASS DLLs (`bass.dll`, `bass_fx.dll`,
`bassmix.dll`, `basswasapi.dll`) ARE the upstream ones — verified by
git ls-tree hash comparison: identical blobs. So the audio rendering
path is unchanged at the native layer.

Where this can still matter:
- The managed wrapper's method signatures and default flag values can
  differ between ppy's fork and the community release.
- ppy.ManagedBass may expose APIs the framework code calls
  conditionally; with the community package those would silently no-op
  if the method-info lookup fails (e.g. via reflection or interop).
- `ppy.Veldrid` and other ppy.* packages still use the ppy versions
  in the same csproj, so it's not a uniform "switch everything to
  community" policy — somebody specifically picked these out.

The package family was swapped in commit
`8afa4dfea — "fix: restore upstream audio device and WASAPI behaviour"`
(2026-04-26), which simultaneously removed ~1500 lines of custom
audio-thread / WASAPI / ASIO management from `AudioThread.cs` and
`AudioManager.cs`. So the swap and a large code revert happened
together; whether the swap was incidental to the revert (the previous
custom code may have depended on ppy.ManagedBass internals) or
deliberate is unclear from the diff alone.

**Suggested experiment.** Switch the four `ManagedBass*` references
back to the `ppy.ManagedBass*` 2022.1216.0 family. The Asio code is
now disconnected from the audio thread anyway (see next finding) so
nothing in the live audio path needs the community-only
`ManagedBass.Asio`. If pre-play offset hitch + WASAPI feel issues
clear up, that's the answer; if not, the wrapper isn't the cause.

### 2. ASIO support is dead code that still ships

`osu.Framework/Audio/Asio/AsioConfig.cs` and `AsioDeviceManager.cs`
(432 lines) remain in the fork. The 2026-04-26 revert removed every
caller from `AudioThread.cs` and `AudioManager.cs`. Result: the code
compiles, links into `osu.Framework.dll`, ships with the client, but
nothing invokes it.

Cost:
- ~50 KB of bytecode in the final assembly per user (negligible).
- Adds `ManagedBass.Asio 4.0.1` to the dependency graph (real, see
  finding 1).
- The native `bassasio.dll` (29 KB) is bundled into the
  `osu.Framework.NativeLibs` runtime tree alongside upstream's
  unmodified bass DLLs — added by the fork, not upstream.

**Suggested experiment.** Remove the Asio/ directory, drop the
`ManagedBass.Asio` package reference, remove `bassasio.dll` from the
NativeLibs runtimes. If the offset hitch is partly a JIT / loader
side-effect of an extra runtime native, this might surface it; if
not, at least the dependency graph gets honest.

### 3. SubmittingPlayer was rewritten to avoid audio pops

`osu.Game/Screens/Play/SubmittingPlayer.cs` carries +157 / −52 lines
versus upstream. The change of interest: `handleTokenRetrieval` used
to block the load thread waiting for the API roundtrip
(`tcs.Task.Wait(30000)`). The fork makes it async and explains why:

> "Blocking the load thread on retry caused audible audio pops while
> the new player instance was being prepared."

i.e. the load thread is shared with the audio engine for new-track
preparation, and a long-running blocking call there causes audible
glitches. This change LOOKS sound, but it's a workaround for a
deeper coupling — `LoadAsyncComplete` doing network I/O on a thread
the audio engine relies on. If audio glitches persist intermittently,
this is a place to look harder (other long-running operations on
`LoadAsyncComplete` paths could produce the same artefact).

### 4. AudioDevicesSettings carries a one-shot config migration

`osu.Game/Overlays/Settings/Sections/Audio/AudioDevicesSettings.cs`
adds a cleanup block:

> "If a previous Torii session saved a WASAPI-prefixed device name
> (e.g. 'WASAPI Shared: Headphones') and that device is not present in
> the current enumeration, reset to the system default. This keeps
> shared configs compatible after moving back to the upstream
> experimental-WASAPI behaviour."

This confirms that the pre-2026-04-26 fork had a custom WASAPI
implementation that enumerated devices with "WASAPI Shared:" /
"WASAPI Exclusive:" prefixes. Users from that period have those
device names in their saved configs. The cleanup reads "if the saved
device doesn't exist anymore, reset to default" — which is correct
but doesn't tell the user that's why their device settings changed.

Not a bug per se; just context for "WASAPI feels different now" —
some users may have had a custom WASAPI device selected before, lost
it silently, and are now on the system default without realising.

### 5. PreviewTrack lost the `Looping` property

`osu.Game/Audio/PreviewTrack.cs` is 21 lines lighter than upstream;
the entire `Looping` bindable and its setter wiring are removed.
This is unrelated to the offset / WASAPI issues but means
song-select preview audio doesn't loop where it would upstream. If
users have complained about previews abruptly cutting, this is why.

## Next steps (for next release batch)

In order of confidence-to-effort:

1. **Remove the dead Asio/ directory + `ManagedBass.Asio` ref + the
   bundled `bassasio.dll`.** Pure cleanup, no risk to user-facing
   audio behaviour. (Finding 2)
2. **Swap `ManagedBass*` → `ppy.ManagedBass*` family back.** Most
   likely root cause of subtle WASAPI / offset feel issues; reverts
   one specific decision rather than reverting all of `8afa4dfea`.
   (Finding 1)
3. **Restore the `Looping` property on `PreviewTrack`.** Trivial,
   user-visible. (Finding 5)
4. **Investigate the calibrate-from-plays empty-history symptom.**
   `SessionAverageHitErrorTracker.calculateAverageHitError` filters
   on `HitEvents.Count >= 50`; if the deep-clone in
   `SubmittingPlayer.OnExiting` is losing `HitEvents` on the
   background `Task.Run`, the tracker never sees them. Add a
   `Logger.Log` in `calculateAverageHitError` to confirm before
   touching anything. (Symptom 2)
5. **Investigate the offset-slider pre-play hitch.** Profile a frame
   trace from "slider value changes" to "song starts" and see what's
   stalling. Hypothesis: a synchronous BASS device reconfigure on the
   audio thread when the offset changes, which is upstream behaviour
   but cheaper on `ppy.ManagedBass` than on community ManagedBass.
   (Symptom 1)

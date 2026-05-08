# Changelog

All notable changes to osu! Torii. The "Latest" section here is what
ships in the body of the next GitHub Release; once a tag goes out, the
section is renamed with the version number and a new "Latest" is
opened on top.

---

## Latest — May 8, 2026

### New

- **Dashboard → User Search tab.** The "Friends" / "Currently online"
  bar in the Home overlay grows a third tab for searching users by
  name. Same UI as upstream osu! lazer's Dashboard search; backed by
  the existing Torii navbar search SQL, which now also speaks the
  official osu! API search shape so the lazer client uses it
  unchanged.
- **Force SDL3 backend toggle (Linux / macOS).** Settings → Graphics
  → Renderer gets a new "Force SDL3 backend" checkbox on platforms
  where osu-framework defaults to SDL2. Toggling it sets `OSU_SDL3=1`
  on the next launch and triggers a real game restart (via Velopack,
  same path the renderer-switch dialog uses) — no manual relaunch.
  Hidden on Windows and mobile, where SDL3 is already the default.

### Polish

- **Torii Briefing — Liquid Glass redesign.** The post-login briefing
  overlay gets an Apple-polish pass:
    - **Unified glass material.** Every card and the panel itself now
      share a single layered surface (translucent gradient base +
      specular top-edge highlight + hairline stroke + soft tinted
      shadow), so the overlay reads as one system rather than five
      patches. Corners use a 2.4 squircle exponent matching iOS / SwiftUI
      curvature.
    - **One accent treatment per card, not five.** The old design layered
      a coloured strip + horizontal wash + circular icon puck + tinted
      border + tinted shadow — all redundant. New cards use a single
      iOS-Settings-style coloured icon tile, plus the kicker text and
      shadow tint, and that's it.
    - **Recalculation card cleanup.** Drops the duplicate "BEST GAIN" /
      "WORST LOSS" footer (both rows were already the first entries in
      their respective gain / loss lists) and replaces the unicode
      ▲ / ▼ arrows with proper caret icons that scale with the type.
    - **8-pt grid.** Spacing, type sizes, and corner radii all snap to
      a unified scale (4 / 8 / 16 / 24 / 32) instead of the previous
      mix of 3 / 14 / 18 / 22 / 34. Cards now auto-size to content
      rather than living at hard-coded 126 / 142 px heights.
    - **Sequenced entrance.** Cards stagger-fade in by 70 ms × index
      with a spring-y panel scale-in (slight overshoot on entrance,
      eased dismiss) instead of the previous flat fade.
    - **Floating close button.** Replaces the bare top-right `×` icon
      with a soft glass tile that lifts on hover and dims on press,
      matching the rest of the briefing's vocabulary.

### Fixes

- **Song Select scrolling: 2-10ms scheduler savings on large beatmap
  libraries.** Backports the upstream perf fix
  ([ppy/osu#37666](https://github.com/ppy/osu/pull/37666)) for the
  rank display panel: replaces an expensive Realm filter on linked
  objects with a flat-field query plus .NET-side narrowing. Also
  fixes the secondary bug where the previous beatmap's rank briefly
  flashed during transitions on huge databases. (We deliberately
  skip the `[Indexed]` attribute the upstream PR adds, to avoid
  bumping the Realm schema and breaking vanilla-osu!-lazer
  compatibility on shared realm folders.)
- **Pitch Adjust mod: extended-range values now survive replays.** A
  score submitted with *Extended limits* on and a pitch-shift outside
  the safe 0.5×–2.0× band (e.g. 3× chipmunk, 0.2× sub-bass) no longer
  silently clamps to 2× when reloaded as a replay or shown in the
  leaderboard. The fix mirrors the deserialisation guard the
  Difficulty Adjust mod already had upstream.
- **No more spurious "imported beatmap does not match online version"
  toasts.** Importing a daily-challenge beatmap from one of our mirrors
  used to surface that warning (sometimes twice) even when the file
  was perfectly fine — the map opened, leaderboard rendered, ranked
  badge attached. Caused by a race where the availability tracker ran
  its checksum query before the metadata-lookup pipeline had populated
  the online hash. Now the warning waits 1.5s for the lookup to settle
  and dedupes itself across the two callback paths that can fire it.

### Android — one-time reinstall required

The Android APK pipeline now signs every build with a permanent
keystore stored in GitHub Secrets, replacing the throw-away keystore
that was regenerated on each CI run. Concretely: this fixes the
"App not installed" / "package conflicts with existing one" errors
that forced you to uninstall the previous Torii build before
installing every update.

To make the switch, **this update requires uninstalling your
existing Torii build first, then installing the new APK**. From this
release onward, every Android update installs in place automatically
— no more uninstall dance.

iOS / desktop builds are unaffected; this is purely an Android
signing-identity change.

---

## v2026.507.9-lazer — May 7, 2026

### New

- **Pitch Adjust mod (PA).** New Fun mod that shifts the song's pitch
  up or down without changing playback speed. Slider defaults to a
  safe range (0.5× to 2.0×); flip the new *Extended limits* toggle
  inside the mod's settings to widen it to a 0.1×–3.0× range for
  novelty / chipmunk / sub-bass-warble territory. Conflicts with the
  rate-adjust family (DT, HT, NC, DC, Wind Up/Down, Adaptive Speed)
  since they all touch the same audio adjustments.
- **Strictly vertical Song Select UI.** Optional toggle under
  *Settings → User Interface → Song Select*. Re-renders the slanted
  wedges, leaderboard rows, dropdowns, and metadata panel as plain
  rectangles. The slanted (sheared) style stays the default; this is
  opt-in.
- **Realm v52 → v51 migration tool.** Auto-prompts at startup if your
  local database is on the now-defunct v52 schema. Creates a verified
  SHA-256 backup, rebuilds the realm at v51, and runs a 7-layer
  verifier (deep-equality recursion, RealmFile orphan parity, file-usage
  hash sampling) before any irreversible step. Restores compatibility
  with vanilla osu! lazer on shared folders.

### Android

- **CoreCLR runtime + .NET 10.** Replaces the legacy Mono interpreter
  with the same JIT-backed runtime the desktop builds use. Roughly
  3× speedup on hot paths (difficulty calculator, audio mixer).
- **arm64-only APK.** Fixes the "App not installed as package appears
  to be invalid" sideload errors that some phones threw on the
  multi-architecture package.
- **New package id `sh.shikkesora.torii`** (was `com.googuteam.osu`).
  Side-by-side install with vanilla osu! lazer is supported; users
  on the old package need to uninstall before installing this build.
- **Rewritten APK signing pipeline.** Explicit zipalign + apksigner
  with v1+v2+v3 signature schemes plus a hard-fail verify step. No
  more unsigned APKs slipping into a release.
- **Native libraries patched at packaging time.** The CoreCLR RID
  resolver was bundling Linux-flavoured `libbass.so` / `libffmpeg.so`
  in the APK; the build now substitutes the Android-bionic builds
  from the framework checkout before signing.

### Fixes

- File-store cleanup no longer deletes user content during the realm
  migration. The deep-equality verifier explicitly checks that every
  destination `RealmFile` has at least one inbound `RealmNamedFileUsage`
  reference and aborts the swap if it doesn't.

---

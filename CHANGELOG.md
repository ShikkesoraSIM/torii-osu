# Changelog

All notable changes to osu! Torii. The "Latest" section here is what
ships in the body of the next GitHub Release; once a tag goes out, the
section is renamed with the version number and a new "Latest" is
opened on top.

---

## Latest — May 8, 2026

### Fixes

- **Pitch Adjust mod: extended-range values now survive replays.** A
  score submitted with *Extended limits* on and a pitch-shift outside
  the safe 0.5×–2.0× band (e.g. 3× chipmunk, 0.2× sub-bass) no longer
  silently clamps to 2× when reloaded as a replay or shown in the
  leaderboard. The fix mirrors the deserialisation guard the
  Difficulty Adjust mod already had upstream.

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

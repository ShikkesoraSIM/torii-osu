# Realm v52 → v51 downgrade — design doc

**Goal**: produce a Realm at schema v51 (vanilla osu! lazer compatible)
from a user's existing Realm at schema v52 (Torii). Lossless. With
zero margin for catastrophic failure.

**Why this is necessary**: `e3618b5` bumped Torii's Realm schema 51→52
to add `SkinInfo.Pinned`. Realm refuses to open files at a higher schema
than the code expects (by design — protects data). Torii's first-run
wizard points the user's storage at vanilla's `client.realm` (it doesn't
copy, it shares — `OsuStorage.ChangeDataPath`), so users who used the
wizard now have a v52 file in their vanilla folder that vanilla can't
open. We can't downgrade in-place — Realm framework forbids it. Only
viable approach: full export-reimport, rebuilding the Realm at v51.

## Hard safety guarantees

These are non-negotiable. The downgrade must:

1. **Never modify or delete the source file** until the new file has
   been fully written, fully verified, and fsync'd.
2. **Always have a recoverable backup** at every point during the
   operation. If the process is killed at ANY moment (power loss, crash,
   app close), the user must end up with either:
   - A working v52 realm at the original path (operation never started
     or rolled back), OR
   - A working v51 realm at the original path (operation completed and
     verified), AND a `client.realm.v52.bak.<timestamp>` next to it.
   Never both invalid. Never just one invalid.
3. **Verify counts and sample objects** after the new realm is built.
   If any class has a different object count, or any sampled object
   doesn't match its source, the operation is rolled back and the user
   keeps their original realm.
4. **Side-car the `Pinned` skin GUIDs** before opening the v52 realm
   for any export work, so even if the entire operation fails halfway
   through, pin state is preserved.
5. **Be opt-in only**. Never run automatically. The user clicks a
   button after reading a clear explanation. This is a user-initiated
   migration, not a silent upgrade path.

## Phases

### Phase 0 — Pre-flight

- Resolve the active realm path via `OsuStorage`.
- Verify the file exists, is at v52, and is not in use (no live
  `RealmAccess` instance holding it).
- Check available disk space ≥ 3 × `client.realm` size (backup + temp
  new realm + headroom).
- Compute SHA-256 of the source file for forensic comparison if
  anything goes wrong.

### Phase 1 — Side-car save of pinned skins

- Open the source realm at v52 read-only.
- Enumerate `SkinInfo.Pinned == true` GUIDs.
- Write the list to `<storage>/torii/pinned-skins.json` atomically
  (write to `.tmp`, fsync, rename).
- Close the realm.

This runs first so that even if EVERYTHING ELSE fails, the user's pin
state is preserved when they open the post-fix Torii build (which reads
pin state from this file, not from the realm column).

### Phase 2 — Backup

- Copy `client.realm` to `client.realm.v52.bak.<timestamp>` on the
  same volume (so the OS can use a copy-on-write fast path if the
  filesystem supports it).
- fsync the backup.
- Verify the backup's SHA-256 matches the source.
- Also copy any sibling `.note`, `.lock`, `.management/` artifacts —
  conservative, in case the user wants to do a full rollback to the
  exact pre-downgrade state.

### Phase 3 + 4 — Streaming copy (no intermediate in-memory dump)

The original plan called for exporting everything to an in-memory
dict-of-dicts in phase 3 then re-importing in phase 4. That risks
`OutOfMemoryException` on a hardcore user's library (100k beatmaps,
200k scores). The implementation streams source→destination
directly via `DynamicApi`, so RAM usage is bounded by Realm's own
mmap caching plus a single batch worth of cursor state.

Two-pass model:

- **Pass A (skeletons)**: for every top-level class, create a
  destination object keyed by the same primary key (or no PK for
  classes without one) and populate every primitive-typed property
  AND every embedded object property. References (links to other
  top-level objects) and lists/sets of references are deferred.
- **Pass B (relationships)**: re-iterate every object and fill in
  object references, lists/sets of references, and backed-by-PK
  relationships. Every target object now exists in the destination
  so lookups always succeed.

Both passes commit in batches of 5000 objects to keep the
destination realm's transaction log from ballooning. Inside a
batch a crash leaves the temp file at the previous batch boundary,
which the runner deletes on rollback anyway.

- Skip the `SkinInfo.Pinned` column — we already side-car'd it.
- Skip `LinkingObjects` (computed backlinks).
- Capture per-class object counts as we go for the verifier.

### Phase 4 — (merged into 3 above)

- Compute target path: `client.realm.v51.tmp` (NOT the active path).
  The active path stays untouched until Phase 6.
- Open a fresh Realm at the temp path with:
  - `SchemaVersion = 51`
  - `Schema` containing the vanilla classes (no `SkinInfo.Pinned`)
- Insert objects in topological order:
  1. Standalone leaves: `RealmFile`, `BeatmapMetadata`, `RealmUser`,
     `RulesetInfo`
  2. `BeatmapSetInfo` (refs `RealmFile`, `BeatmapMetadata`)
  3. `BeatmapInfo` (refs `BeatmapSetInfo`, `RulesetInfo`,
     `BeatmapMetadata`)
  4. `ScoreInfo` (refs `BeatmapInfo`, `RulesetInfo`, `RealmUser`,
     `RealmFile`)
  5. `SkinInfo` (refs `RealmFile`)
  6. `BeatmapCollection` (no Realm refs — uses md5 strings)
  7. `ModPreset` (refs `RulesetInfo`)
  8. `RealmKeyBinding` (PK only)
  9. `RealmRulesetSetting` (refs `RulesetName`)
- All inserts in a single write transaction so a crash mid-insert
  leaves an empty realm rather than a partial one.

### Phase 5 — Verify

After the temp realm is built and committed:

- Re-open the temp realm read-only.
- For every class, count objects. Counts must match the source-side
  counts captured in Phase 3 EXACTLY.
- Sample 10 random objects per class (or all if < 10). For each, read
  every property and compare against the source-side captured value.
- Verify the temp realm's `SchemaVersion` is exactly 51 and that the
  schema does NOT contain `SkinInfo.Pinned`.

If ANY check fails, abort: delete the temp realm, do not touch the
active path, leave the backup in place, surface a detailed error log
to the user.

### Phase 6 — Atomic swap

- Verify backup still exists and matches its SHA from Phase 2.
- Rename `client.realm` → `client.realm.swap.<timestamp>` (deferred
  delete, kept for one boot cycle).
- Rename `client.realm.v51.tmp` → `client.realm`.
- fsync the directory entry.
- Re-open at v51 to confirm. If THIS open fails for any reason,
  rename swap → original to roll back.
- Delete the swap copy after the next clean app boot proves the v51
  realm is healthy. (Or leave forever — disk is cheap, this is one
  user's data.)

### Phase 7 — Communicate

- Show the user a success dialog with:
  - "Done. Your realm is now at v51 and openable from vanilla osu!"
  - Backup location
  - Reminder: pinned skins migrated to side-car JSON
- Log every step verbosely to `logs/realm-downgrade-<timestamp>.log`
  for forensic recovery if anything ever goes wrong.

## Failure-mode matrix

| Failure point                   | State of disk            | Recovery action                                       |
|---------------------------------|--------------------------|-------------------------------------------------------|
| Phase 0 fails                   | Untouched                | None needed — operation never started                 |
| Phase 1 fails (sidecar write)   | Untouched                | Surface error, abort                                  |
| Phase 2 fails (backup)          | Sidecar may exist        | Abort. Sidecar is harmless even if Phase 6 never runs |
| Phase 3 fails (export)          | Backup exists            | Abort. Source intact                                  |
| Phase 4 fails (build new)       | Backup + temp partial    | Delete temp. Source intact                            |
| Phase 5 fails (verify)          | Backup + temp built      | Delete temp. Source intact                            |
| Phase 6 fails BEFORE rename     | Backup + temp ok         | Delete temp. Source intact                            |
| Phase 6 fails DURING rename     | Backup + half-swapped    | Roll back via swap copy. Source restored              |
| Phase 6 fails AFTER swap, open  | Backup + new at active   | Roll back via swap copy. Source restored              |
| Power loss anywhere             | Backup ALWAYS exists     | Manual restore from backup if active is broken        |

## Components

```
osu.Game/Database/RealmDowngrader/
  RealmDowngradeRunner.cs          — orchestrates phases 0-7
  RealmDowngradeBackup.cs          — atomic backup + restore
  RealmDowngradeExporter.cs        — phase 3, dynamic-api dump
  RealmDowngradeImporter.cs        — phase 4, build v51 realm
  RealmDowngradeVerifier.cs        — phase 5, count + sample checks
  PortableRealmObject.cs           — in-memory representation
  RealmDowngradeReport.cs          — per-step result + log

osu.Game/Skinning/
  PinnedSkinsStore.cs              — side-car JSON for skin GUIDs

osu.Game/Overlays/Settings/Sections/Maintenance/
  RealmDowngradeSection.cs         — opt-in button + dialog

osu.Game/Database/v51Schemas/
  (mirrors of v52 realm classes WITHOUT SkinInfo.Pinned, used as
  the destination schema for phase 4)
```

## What this does NOT cover

- Migrating files in `<storage>/files/` — those are content-addressed
  by SHA-256, the realm just holds references. They survive any realm
  rebuild as long as we preserve the `RealmFile` objects (we do).
- Settings stored outside Realm (`framework.ini`, `storage.ini`,
  collection imports, screenshots) — untouched.
- The pinned-skins side-car carries forward only. Future "redowngrade
  after a future schema bump" cycles need their own analysis.

## Test plan (before shipping)

1. Run against my own realm. Open in vanilla osu! — must work, all
   data visible, no corruption.
2. Run against a stress realm: 5000+ beatmaps, 50000+ scores. Verify
   every count matches.
3. Kill the process at every phase boundary. Verify recovery: either
   a working v52 (rolled back) or a working v51 (completed). Never a
   broken realm.
4. Run twice in a row — second run should detect "already at v51" and
   no-op.
5. Run with no disk space — should fail in Phase 0 cleanly.
6. Run with the realm currently locked by another process — should
   fail in Phase 0 cleanly.

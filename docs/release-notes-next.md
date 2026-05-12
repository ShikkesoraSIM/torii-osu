# Torii Release Notes Draft

This draft is intended as the base for the next public Torii Nova release (post the May 2026 rebrand, where the binary was renamed `osu-torii.exe` → `torii.exe` and the public brand became "Torii Nova").

It intentionally excludes the unfinished stable song select prototype and any half-finished experimental navigation work.

## Headline

This update focuses on making Torii feel faster, clearer, and more alive:

- a new Torii briefing overlay gives players a proper daily portal when they log in
- gameplay exit stutter and PP projection overhead were reduced
- Torii settings are cleaner and more useful
- Torii-specific visuals and PP identity are more consistent across the client

## Included In This Release

### Torii Briefing

- Added the new **Torii briefing overlay** as a first-class in-game experience.
- Added a daily-portal style summary that can surface:
  - rank movement
  - top-play recalculations
  - radar-style account updates
  - sync/session state
  - message/chat context
- Added a **Generate new briefing** action in Torii settings so the briefing can be refreshed on demand.
- Switched the briefing header to use the **Torii logo asset** instead of a placeholder letter.
- Improved recalc presentation so changes feel meaningful at a glance:
  - top recalculated scores are listed directly
  - biggest PP gain is highlighted
  - biggest PP loss is highlighted
  - positive and negative movement is colour-coded more clearly

### Performance And Responsiveness

- Reduced song-select and overlay hitching by pinning `BeatmapDifficultyCache` to a safer worker strategy.
- Debounced PP projection refreshes so rapid ruleset/mod/state changes do not spam recalculation.
- Reduced quit/escape hitching during gameplay by:
  - skipping online submission on quit
  - moving heavy score cloning off the update thread
  - avoiding unnecessary spectator-frame flushing on quit/fail
- Overall result: leaving a map should feel less sticky and less likely to glitch audio.

### Torii Settings Improvements

- Added a dedicated **Torii briefing** settings subsection.
- Added a dedicated **Torii server** settings subsection.
- Added a direct **API server URL** field in Torii settings.
- Added one-click presets for popular Torii-compatible servers:
  - Torii
  - g0v0
  - vipsu
  - m1pp
- Runtime endpoint switching is applied immediately when safe; otherwise the UI explains when restart is required.
- Official `ppy` endpoints are guarded against accidental use from Torii-specific settings.
- Kept Torii storage/migration helpers grouped in the same section so the Torii page feels useful rather than empty.

### Torii Visual Identity

- The Torii settings section now uses the **Torii logo** as its icon.
- Changelog toolbar handling was hardened so the entry stays visible even if a custom glyph is too faint or missing.
- Continued polishing Torii title and username presentation:
  - top-priority Torii title colours propagate more consistently
  - group badge styling is cleaner
  - unnecessary badge borders were removed

### PP System Direction

- Promoted **pp-dev** to Torii's primary PP system in the client path this branch is targeting.
- Removed the old dual-system toggle model so the PP experience is less ambiguous.
- Continued surfacing pp-dev context more clearly in player-facing UI where appropriate.

## Player-Facing Summary

If you only care about what this feels like in practice:

- the client should feel less hitchy when backing out of maps
- PP widgets should feel calmer and less spammy
- Torii finally has a proper in-game briefing/home-style moment
- Torii settings are more useful and less "dead space"
- Torii's identity is more visible in the client without feeling like a random skin

## Intentionally Not Included

These exist locally or experimentally, but should **not** ship as part of this release:

- the stable song select prototype
- unfinished stable-style carousel/layout experiments
- any incomplete alpha navigation / navbar experiments
- any release candidate that still depends on hidden config flags to access unstable screens

## Suggested Short Release Blurb

> Added the new Torii briefing overlay, reduced gameplay exit hitching, cleaned up Torii settings, and continued unifying Torii's PP + visual identity across the client.

## Suggested Longer Release Intro

> This update is about polish and trust. We added the new Torii briefing so players get a proper daily portal on login, reduced several sources of hitching and recalculation overhead, cleaned up the Torii settings surface with direct server controls, and kept pushing Torii's own PP and visual identity deeper into the client. Experimental stable song select work is still excluded from this release on purpose.

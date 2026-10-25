# Changelog

## Latest

- Fixed ranked play results not showing the two scores, ranks and HP damage.
- Fixed the renamed damage sound and kept compatibility with older resources.
- Fixed late score requests trying to load results after leaving the round.
- Includes the merged fixes for modded star rating estimates in song select.

## Previous Nova Runtime Update

- Nova now runs on .NET 11 RC2. Desktop builds include Satori 11, built from a
  pinned source commit and checked for the correct architecture and file hashes.
- Windows, Linux and macOS include x64 and ARM64 builds. Android stays on CoreCLR
  and moves to the .NET 11 Android workload.
- Existing profiles, settings, beatmaps, skins and scores stay where they are.
- Android update detection and version registration fixes are included.

This is an experimental Nova update for wider testing, not a Stable update.
No benchmarked performance improvement is promised across all hardware.

Compatibility: x64 CPUs must support x86-64-v2 (including SSE4.2 and POPCNT).
The Android APK requires Android 7.0 or newer and an ARM64 device.
The macOS bundle requires macOS 14 or newer. Linux Satori binaries are built
on Ubuntu 22.04; use that or a compatible newer distribution.
Older unsupported devices can stay on the .NET 10 Torii stream.

# Satori 10 on Torii

The .NET 10 runtime source and build SDK are pinned in `.github/satori-net10.json`.
`satori-net10.yml` builds all six desktop runtimes and executes a GC/async/pinning
smoke test on each native architecture before uploading the runtime artifacts.
The client build verifies the source pin, native architecture and SHA256 hashes
before replacing CoreCLR, the JIT and CoreLib. Missing or mismatched files stop
the build; there is no silent fallback to Microsoft's stock GC.

Existing GC tuning is unchanged. Android uses the official .NET 10 CoreCLR
workload, not the desktop Satori overlay.

The older `net10/` archives are retained for legacy fallbacks. A plain `dotnet publish`
produces stock CoreCLR; the overlay must happen after publishing and before packaging
or signing.

`prebuilt_run_id` in `build-gu.yml` reuses the verified runtime artifacts from
the initial source build across branches and tags. Every file and source pin is
checked again, and the native probe still runs. Expired artifacts fall back to
a source build; integrity or pin mismatches fail. Clear this input when changing
the runtime source or SDK pin, then set it to the newly validated build run.

# Satori 11 on Nova

The .NET 11 runtime source and build SDK are pinned in `.github/satori-net11.json`.
`satori-net11.yml` builds all six desktop runtimes and executes a GC/async/pinning
smoke test on each native architecture before uploading the runtime artifacts.
The client build verifies the source pin, native architecture and SHA256 hashes
before replacing CoreCLR, the JIT and CoreLib. Missing or mismatched files stop
the build; there is no silent fallback to Microsoft's stock GC.

Existing GC tuning is unchanged. Android uses the official .NET 11 CoreCLR
workload, not the desktop Satori overlay. The shared libraries can keep their
net10 target: all their managed code executes on the net11 entrypoint's runtime.

The older `net10/` archives are retained for .NET 10 builds. Never mix them with
the net11 runtime. A plain `dotnet publish` produces stock CoreCLR; the overlay
must happen after publishing and before packaging or signing.

`prebuilt_run_id` in `build-gu.yml` reuses the verified runtime artifacts from
the initial source build across branches and tags. Every file and source pin is
checked again, and the native probe still runs. Expired artifacts fall back to
a source build; integrity or pin mismatches fail. Clear this input when changing
the runtime source or SDK pin, then set it to the newly validated build run.

A manual `build-gu.yml` dispatch defaults to build-only. Set `publish_release`
only for a release; `-nova` tag pushes still use the normal release flow.
This change does not change application identity, storage, IPC or update feeds.

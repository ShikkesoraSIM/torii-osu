# Android Releases

`android_release.py version-code "$GITHUB_RUN_NUMBER"` supplies the internal APK
version code. The migration floor exceeds the old published codes; the shared
workflow counter increases across Torii/Nova and stays unchanged on a rerun.
Keep this counter when moving or replacing the release workflow. A reset needs
a new migration floor above the highest APK already published.

`android_release.py version-hash "$SEMVER"` mirrors the packed Android client's
version hash. Pass the exact `ApplicationDisplayVersion`, without the tag's `v`.

Tests: `python3 -m unittest discover -s .github/scripts -p 'test_android_release.py'`.

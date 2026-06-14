# Cut a Torii release for whatever is currently pushed on origin.
#
# Auto-tagging on push is off (auto-tag.yml is manual only now), so this is the
# release button. It works out the next dated version, tags the pushed tip of
# the branch, and pushes that tag. build-gu.yml sees the v* tag and publishes
# the installers.
#
#   .\release.ps1          release master  ->  v2026.MDD.N-torii  (stable)
#   .\release.ps1 -Nova    release nova    ->  v2026.MDD.N-nova   (prerelease)
#
# Version scheme matches .github/workflows/auto-tag.yml. If that ever changes,
# change it here too.

param([switch]$Nova)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if ($Nova) { $branch = "nova";   $stream = "nova"  }
else       { $branch = "master"; $stream = "torii" }

Write-Host "Fetching origin/$branch + tags..."
git fetch origin $branch --tags --quiet

# Dated base: UTC year . month(no pad) day(2 digit), same as auto-tag.yml.
$utc  = [DateTime]::UtcNow
$base = "{0}.{1}{2:d2}" -f $utc.Year, $utc.Month, $utc.Day

# Next N is shared across the lazer/torii/nova suffixes for the day so two
# releases on the same day never collide on a number.
$maxN = -1
foreach ($suf in @("lazer", "torii", "nova")) {
    foreach ($t in (git tag -l "v$base.*-$suf")) {
        if ($t -match ("^v" + [regex]::Escape($base) + "\.(\d+)-$suf$")) {
            $n = [int]$Matches[1]
            if ($n -gt $maxN) { $maxN = $n }
        }
    }
}
$tag = "v$base.$($maxN + 1)-$stream"

$sha = (git rev-parse "origin/$branch").Trim()

# Show EVERYTHING that will ship in this release: all commits since the previous
# release of this stream, not just the tip (which was misleading - it looked like
# only the last commit was going out).
$prevTag = (git describe --tags --match "v*-$stream" --abbrev=0 "origin/$branch" 2>$null)

Write-Host ""
Write-Host "  release:  $tag"
Write-Host "  branch:   origin/$branch"
Write-Host "  tip:      $($sha.Substring(0,10))"

if ($prevTag) {
    $commits = @(git log "$prevTag..origin/$branch" --pretty="    %h  %s")
    Write-Host "  changes since ${prevTag}:  $($commits.Count) commit(s)"
    if ($commits.Count -gt 0) { $commits | ForEach-Object { Write-Host $_ } }
    else { Write-Host "    (no new commits since last release - re-tagging the same tip)" }
}
else {
    Write-Host "  commit:   $((git log -1 --pretty=%s "origin/$branch").Trim())"
}
Write-Host ""
$ok = Read-Host "create + push this tag? this publishes a release. type 'yes'"
if ($ok -ne "yes") { Write-Host "cancelled, nothing pushed."; exit 1 }

git tag -a $tag $sha -m "Release $tag"
git push origin $tag

Write-Host ""
Write-Host "pushed $tag. build-gu is building the installers now, watch the Actions tab."

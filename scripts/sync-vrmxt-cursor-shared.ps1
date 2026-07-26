# Sync shared VRMXT Unity Cursor rules/skills from VRMXT Plugin for Warudo to sibling consumers.
#
# Run from VRMXT Plugin for Warudo repo root:
#   ./scripts/sync-vrmxt-cursor-shared.ps1           # dry-run (default)
#   ./scripts/sync-vrmxt-cursor-shared.ps1 -Apply    # copy into consumers
#   ./scripts/sync-vrmxt-cursor-shared.ps1 -Apply -HardLink  # same-volume hard links
#
# Pin profiles (unity-csharp-language) and host rules (*-repository, ui-labels, …)
# are never overwritten.
#
# Extended-UniVRM is an upstream UniVRM fork — do not put Cursor rules/skills there.

[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$HardLink,
    [string[]]$Consumer
)

$ErrorActionPreference = "Stop"

$canonRoot = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $canonRoot ".cursor\shared-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Missing manifest: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$siblingRoot = Split-Path $canonRoot -Parent
$mode = if ($Apply) { if ($HardLink) { "hardlink" } else { "copy" } } else { "dry-run" }

Write-Host "Canonical: $canonRoot"
Write-Host "Sibling root: $siblingRoot"
Write-Host "Mode: $mode"
Write-Host ""

function Ensure-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        if ($Apply) {
            New-Item -ItemType Directory -Force -Path $Path | Out-Null
        }
    }
}

function Sync-File {
    param(
        [string]$Source,
        [string]$Dest
    )
    if (-not (Test-Path -LiteralPath $Source)) {
        Write-Warning "Missing source: $Source"
        return
    }

    $destDir = Split-Path $Dest -Parent
    Ensure-Directory $destDir

    if (-not $Apply) {
        $srcHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
        if (Test-Path -LiteralPath $Dest) {
            $dstHash = (Get-FileHash -LiteralPath $Dest -Algorithm SHA256).Hash
            if ($srcHash -eq $dstHash) {
                Write-Host "  OK   $Dest"
            }
            else {
                Write-Host "  DIFF $Dest"
            }
        }
        else {
            Write-Host "  NEW  $Dest"
        }
        return
    }

    if (Test-Path -LiteralPath $Dest) {
        Remove-Item -LiteralPath $Dest -Force
    }

    if ($HardLink) {
        $null = New-Item -ItemType HardLink -Path $Dest -Target $Source -Force
        Write-Host "  LINK $Dest"
    }
    else {
        Copy-Item -LiteralPath $Source -Destination $Dest -Force
        Write-Host "  COPY $Dest"
    }
}

function Sync-SkillTree {
    param(
        [string]$SkillName,
        [string]$ConsumerRoot
    )
    $srcSkill = Join-Path $canonRoot ".cursor\skills\$SkillName"
    $dstSkill = Join-Path $ConsumerRoot ".cursor\skills\$SkillName"
    if (-not (Test-Path -LiteralPath $srcSkill)) {
        Write-Warning "Missing skill: $srcSkill"
        return
    }

    Get-ChildItem -LiteralPath $srcSkill -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($srcSkill.Length).TrimStart('\', '/')
        $dest = Join-Path $dstSkill $rel
        Sync-File -Source $_.FullName -Dest $dest
    }
}

$selected = $manifest.consumers
if ($Consumer -and $Consumer.Count -gt 0) {
    $selected = $manifest.consumers | Where-Object {
        $n = $_.name
        $p = $_.relativePath
        ($Consumer | Where-Object { $_ -eq $n -or $_ -eq $p }).Count -gt 0
    }
    if (-not $selected) {
        throw "No consumers matched -Consumer filter: $($Consumer -join ', ')"
    }
}

foreach ($c in $selected) {
    $consumerRoot = Join-Path $siblingRoot $c.relativePath
    Write-Host "=== $($c.name) ==="
    if (-not (Test-Path -LiteralPath $consumerRoot)) {
        Write-Warning "Consumer missing on disk: $consumerRoot"
        Write-Host ""
        continue
    }

    Ensure-Directory (Join-Path $consumerRoot ".cursor\rules")
    Ensure-Directory (Join-Path $consumerRoot ".cursor\skills")

    foreach ($rule in $manifest.sharedRules) {
        $src = Join-Path $canonRoot ".cursor\rules\$rule"
        $dst = Join-Path $consumerRoot ".cursor\rules\$rule"
        Sync-File -Source $src -Dest $dst
    }

    foreach ($skill in $manifest.sharedSkills) {
        Sync-SkillTree -SkillName $skill -ConsumerRoot $consumerRoot
    }

    Write-Host ""
}

if (-not $Apply) {
    Write-Host "Dry-run only. Re-run with -Apply to write. Optional: -HardLink (same volume)."
}
else {
    Write-Host "Done. Pin/host rules were not modified."
}

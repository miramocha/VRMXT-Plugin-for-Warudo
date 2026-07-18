# Local backup/restore for Assets/ExportSettings.asset (machine-specific; gitignored).
# UMod may wipe ExportSettings when Unity regains focus; it does not touch *.old.
#
# Usage (from repo root or umod/):
#   .\umod\export-settings.ps1 -Backup
#   .\umod\export-settings.ps1 -Restore
#   .\umod\export-settings.ps1 -Status

[CmdletBinding(DefaultParameterSetName = "Status")]
param(
    [Parameter(ParameterSetName = "Backup")]
    [switch] $Backup,

    [Parameter(ParameterSetName = "Restore")]
    [switch] $Restore,

    [Parameter(ParameterSetName = "Status")]
    [switch] $Status
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$asset = Join-Path $repoRoot "Assets\ExportSettings.asset"
$meta = Join-Path $repoRoot "Assets\ExportSettings.asset.meta"
$assetOld = Join-Path $repoRoot "Assets\ExportSettings.asset.old"
$metaOld = Join-Path $repoRoot "Assets\ExportSettings.asset.meta.old"

function Show-Status {
    $rows = @(
        [pscustomobject]@{ Path = "Assets/ExportSettings.asset"; Exists = (Test-Path $asset); Bytes = if (Test-Path $asset) { (Get-Item $asset).Length } else { $null } }
        [pscustomobject]@{ Path = "Assets/ExportSettings.asset.meta"; Exists = (Test-Path $meta); Bytes = if (Test-Path $meta) { (Get-Item $meta).Length } else { $null } }
        [pscustomobject]@{ Path = "Assets/ExportSettings.asset.old"; Exists = (Test-Path $assetOld); Bytes = if (Test-Path $assetOld) { (Get-Item $assetOld).Length } else { $null } }
        [pscustomobject]@{ Path = "Assets/ExportSettings.asset.meta.old"; Exists = (Test-Path $metaOld); Bytes = if (Test-Path $metaOld) { (Get-Item $metaOld).Length } else { $null } }
    )
    $rows | Format-Table -AutoSize
}

if ($Backup) {
    if (-not (Test-Path $asset)) {
        throw "Missing ExportSettings.asset - nothing to back up. Fill Warudo Mod Settings first, or copy umod/ExportSettings.example.asset."
    }
    Copy-Item -LiteralPath $asset -Destination $assetOld -Force
    if (Test-Path $meta) {
        Copy-Item -LiteralPath $meta -Destination $metaOld -Force
    }
    Write-Host "Backed up ExportSettings -> Assets/ExportSettings.asset.old"
    Show-Status
    exit 0
}

if ($Restore) {
    if (-not (Test-Path $assetOld)) {
        throw "Missing ExportSettings.asset.old - run -Backup after settings look correct."
    }
    Copy-Item -LiteralPath $assetOld -Destination $asset -Force
    if (Test-Path $metaOld) {
        Copy-Item -LiteralPath $metaOld -Destination $meta -Force
    }
    Write-Host "Restored Assets/ExportSettings.asset from .old (refocus Unity / Mod Settings to pick up)."
    Show-Status
    exit 0
}

Show-Status

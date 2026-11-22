<#
.SYNOPSIS
    stu/dio XR Toolkit & Template — 1-Click Pre-Flight Package Setup.
.DESCRIPTION
    Configures Packages/manifest.json with required UPM packages and the official
    Meta scoped package registry before opening Unity, avoiding all initial compile errors.
#>

$manifestPath = Join-Path $PSScriptRoot "Packages\manifest.json"

if (-not (Test-Path $manifestPath)) {
    Write-Error "[Studio XR] manifest.json not found at: $manifestPath"
    exit 1
}

Write-Host "== stu/dio XR Template: Pre-Flight Package Setup ==" -ForegroundColor Cyan

$requiredPackages = @{
    "com.unity.xr.interaction.toolkit"   = "3.5.1"
    "com.unity.xr.hands"                 = "1.8.1"
    "com.unity.xr.core-utils"            = "2.6.0"
    "com.unity.splines"                  = "2.9.1"
    "com.unity.xr.openxr"                = "1.17.1"
    "com.unity.xr.meta-openxr"           = "2.5.1"
    "com.unity.xr.management"            = "4.5.4"
    "com.unity.render-pipelines.universal" = "17.5.0"
    "com.unity.inputsystem"              = "1.20.0"
}

$rawJson = Get-Content -Path $manifestPath -Raw
$manifest = $rawJson | ConvertFrom-Json

# 1. Configure Meta Scoped Registry
$metaRegistryUrl = "https://npm.developer.oculus.com"
$hasMetaRegistry = $false

if ($null -ne $manifest.scopedRegistries) {
    foreach ($reg in $manifest.scopedRegistries) {
        if ($reg.url -eq $metaRegistryUrl) {
            $hasMetaRegistry = $true
            break
        }
    }
} else {
    $manifest | Add-Member -MemberType NoteProperty -Name "scopedRegistries" -Value @()
}

if (-not $hasMetaRegistry) {
    Write-Host "Adding Meta Scoped Registry (https://npm.developer.oculus.com)..." -ForegroundColor Yellow
    $metaRegistry = [PSCustomObject]@{
        name   = "Meta"
        url    = $metaRegistryUrl
        scopes = @("com.meta.xr")
    }
    $manifest.scopedRegistries += $metaRegistry
}

# 2. Add Missing Dependencies
$addedCount = 0
foreach ($pkg in $requiredPackages.GetEnumerator()) {
    $pkgId = $pkg.Key
    $pkgVer = $pkg.Value

    if (-not ($manifest.dependencies.PSObject.Properties.Name -contains $pkgId)) {
        Write-Host "Adding package: $pkgId ($pkgVer)..." -ForegroundColor Green
        $manifest.dependencies | Add-Member -MemberType NoteProperty -Name $pkgId -Value $pkgVer
        $addedCount++
    }
}

# 3. Save Updated manifest.json
$newJson = $manifest | ConvertTo-Json -Depth 10
Set-Content -Path $manifestPath -Value $newJson -Encoding UTF8

# 4. Deploy pre-configured OpenXR and XRI template settings to prevent worker crashes
$templateSettingsDir = Join-Path $PSScriptRoot "Assets\stu-dio XR Template\XR Assets\Template Settings"
if (Test-Path $templateSettingsDir) {
    $srcXr = Join-Path $templateSettingsDir "XR"
    $dstXr = Join-Path $PSScriptRoot "Assets\XR"
    $openXrAsset = Join-Path $dstXr "Settings\OpenXR Package Settings.asset"
    if ((Test-Path $srcXr) -and (-not (Test-Path $openXrAsset))) {
        Copy-Item -Path $srcXr -Destination $dstXr -Recurse -Force
        Write-Host "Deployed pre-configured OpenXR settings to Assets\XR." -ForegroundColor Cyan
    }

    $srcXri = Join-Path $templateSettingsDir "XRI"
    $dstXri = Join-Path $PSScriptRoot "Assets\XRI"
    $layerAsset = Join-Path $dstXri "Settings\Resources\InteractionLayerSettings.asset"
    if ((Test-Path $srcXri) -and (-not (Test-Path $layerAsset))) {
        Copy-Item -Path $srcXri -Destination $dstXri -Recurse -Force
        Write-Host "Deployed pre-configured XRI interaction layer settings to Assets\XRI." -ForegroundColor Cyan
    }
} else {
    $xrDir = Join-Path $PSScriptRoot "Assets\XR"
    if (-not (Test-Path $xrDir)) {
        New-Item -ItemType Directory -Path $xrDir -Force | Out-Null
        Write-Host "Created Assets\XR directory." -ForegroundColor Cyan
    }
}

Write-Host "`nSetup complete! Added $addedCount package(s)." -ForegroundColor Green
Write-Host "You can now safely open the project in Unity 6." -ForegroundColor Cyan

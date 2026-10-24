param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string] $BuildId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{7,40}$')]
    [string] $SourceCommit,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Development', 'Beta', 'Release')]
    [string] $BuildChannel,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string] $SemanticVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $repositoryRoot 'src/Kairix.QuickAVSync/Kairix.QuickAVSync.csproj'
$publishRoot = Join-Path $repositoryRoot 'artifacts/publish'
$packageDirectory = Join-Path $repositoryRoot 'artifacts/package'
$stagingRoot = Join-Path $repositoryRoot 'artifacts/staging'
$zipStagingDirectory = Join-Path $stagingRoot "$BuildId-$PID"
$canonicalHtml = Join-Path $repositoryRoot 'kairix_av_sync_test_signal_for_browser.html'

$selfContainedSource = Join-Path $publishRoot 'self-contained/Kairix.QuickAVSync.exe'
$selfContainedName = "Kairix.QuickAVSync-$BuildId-win-x64.exe"
$zipName = "Kairix-Quick-AV-Sync-$BuildId-win-x64.zip"
$selfContainedAsset = Join-Path $packageDirectory $selfContainedName
$zipAsset = Join-Path $packageDirectory $zipName
$htmlName = 'Kairix-AV-Sync-Test-Signal.html'
$htmlAsset = Join-Path $packageDirectory $htmlName
$checksumAsset = Join-Path $packageDirectory 'SHA256SUMS.txt'

function Assert-ChildPath([string] $Parent, [string] $Child, [string] $Description) {
    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $childPath = [System.IO.Path]::GetFullPath($Child)
    if (-not $childPath.StartsWith($parentPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description resolved outside its intended parent: $childPath"
    }
}

Assert-ChildPath $repositoryRoot $packageDirectory 'Package directory'
Assert-ChildPath $repositoryRoot $stagingRoot 'Staging root'
Assert-ChildPath $stagingRoot $zipStagingDirectory 'ZIP staging directory'

try {
    if (Test-Path -LiteralPath $packageDirectory) {
        Remove-Item -LiteralPath $packageDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $zipStagingDirectory -Force | Out-Null

    if (-not (Test-Path -LiteralPath $canonicalHtml -PathType Leaf) -or (Get-Item -LiteralPath $canonicalHtml).Length -le 0) {
        throw "Canonical browser test signal is missing or empty: $canonicalHtml"
    }

    $commonProperties = @(
        '-p:NuGetAudit=false',
        "-p:BuildId=$BuildId",
        "-p:SourceCommit=$SourceCommit",
        "-p:BuildChannel=$BuildChannel",
        "-p:SemanticVersion=$SemanticVersion",
        '-m:1',
        '-nodeReuse:false'
    )

    & dotnet publish $project '-p:PublishProfile=win-x64-self-contained' @commonProperties
    if ($LASTEXITCODE -ne 0) { throw "Self-contained publish failed with exit code $LASTEXITCODE." }

    if (-not (Test-Path -LiteralPath $selfContainedSource -PathType Leaf) -or
        (Get-Item -LiteralPath $selfContainedSource).Length -le 0) {
        throw "Published executable is missing or empty: $selfContainedSource"
    }

    Copy-Item -LiteralPath $selfContainedSource -Destination $selfContainedAsset
    Copy-Item -LiteralPath $canonicalHtml -Destination $htmlAsset
    if ((Get-FileHash -LiteralPath $canonicalHtml -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $htmlAsset -Algorithm SHA256).Hash) {
        throw 'Packaged browser test signal differs from the canonical repository file.'
    }
    Copy-Item -LiteralPath $selfContainedAsset -Destination (Join-Path $zipStagingDirectory $selfContainedName)
    Copy-Item -LiteralPath $htmlAsset -Destination (Join-Path $zipStagingDirectory $htmlName)
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $zipStagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $zipStagingDirectory
    Compress-Archive -Path (Join-Path $zipStagingDirectory '*') -DestinationPath $zipAsset -Force

    $publicAssets = @($selfContainedAsset, $zipAsset, $htmlAsset)
    $checksumLines = foreach ($asset in $publicAssets) {
        if (-not (Test-Path -LiteralPath $asset -PathType Leaf) -or (Get-Item -LiteralPath $asset).Length -le 0) {
            throw "Package asset is missing or empty: $asset"
        }
        $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($hash)) { throw "Checksum generation failed: $asset" }
        "$hash  $([System.IO.Path]::GetFileName($asset))"
    }
    Set-Content -LiteralPath $checksumAsset -Value $checksumLines -Encoding ascii

    if (-not (Test-Path -LiteralPath $checksumAsset -PathType Leaf) -or
        (Get-Item -LiteralPath $checksumAsset).Length -le 0 -or
        (Get-Content -LiteralPath $checksumAsset).Count -ne $publicAssets.Count) {
        throw 'SHA256SUMS.txt was not generated correctly.'
    }

    Write-Host "Packaged $BuildId ($BuildChannel, version $SemanticVersion) from commit $SourceCommit"
    Get-ChildItem -LiteralPath $packageDirectory -File | Select-Object Name, Length
}
finally {
    if (Test-Path -LiteralPath $zipStagingDirectory) {
        Remove-Item -LiteralPath $zipStagingDirectory -Recurse -Force
    }
    if ((Test-Path -LiteralPath $stagingRoot) -and -not (Get-ChildItem -LiteralPath $stagingRoot -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $stagingRoot -Force
    }
}

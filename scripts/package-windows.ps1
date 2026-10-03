param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string] $BuildId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{7,40}$')]
    [string] $SourceCommit,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Recommended', 'Development')]
    [string] $BuildChannel
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $repositoryRoot 'src/Kairix.QuickAVSync/Kairix.QuickAVSync.csproj'
$publishRoot = Join-Path $repositoryRoot 'artifacts/publish'
$packageDirectory = Join-Path $repositoryRoot 'artifacts/package'
$zipStagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "kairix-$BuildId-$PID"

$selfContainedSource = Join-Path $publishRoot 'self-contained/Kairix.QuickAVSync.exe'
$frameworkDependentSource = Join-Path $publishRoot 'framework-dependent/Kairix.QuickAVSync.exe'
$selfContainedName = "Kairix.QuickAVSync-$BuildId-win-x64.exe"
$frameworkDependentName = "Kairix.QuickAVSync-$BuildId-win-x64-framework-dependent.exe"
$zipName = "Kairix-Quick-AV-Sync-$BuildId-win-x64.zip"
$selfContainedAsset = Join-Path $packageDirectory $selfContainedName
$frameworkDependentAsset = Join-Path $packageDirectory $frameworkDependentName
$zipAsset = Join-Path $packageDirectory $zipName
$checksumAsset = Join-Path $packageDirectory 'SHA256SUMS.txt'

function Assert-ChildPath([string] $Parent, [string] $Child, [string] $Description) {
    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $childPath = [System.IO.Path]::GetFullPath($Child)
    if (-not $childPath.StartsWith($parentPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description resolved outside its intended parent: $childPath"
    }
}

Assert-ChildPath $repositoryRoot $packageDirectory 'Package directory'
Assert-ChildPath ([System.IO.Path]::GetTempPath()) $zipStagingDirectory 'ZIP staging directory'

try {
    if (Test-Path -LiteralPath $packageDirectory) {
        Remove-Item -LiteralPath $packageDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $zipStagingDirectory -Force | Out-Null

    $commonProperties = @(
        "-p:BuildId=$BuildId",
        "-p:SourceCommit=$SourceCommit",
        "-p:BuildChannel=$BuildChannel",
        '-m:1',
        '-nodeReuse:false'
    )

    & dotnet publish $project '-p:PublishProfile=win-x64-self-contained' @commonProperties
    if ($LASTEXITCODE -ne 0) { throw "Self-contained publish failed with exit code $LASTEXITCODE." }

    & dotnet publish $project '-p:PublishProfile=win-x64-framework-dependent' @commonProperties
    if ($LASTEXITCODE -ne 0) { throw "Framework-dependent publish failed with exit code $LASTEXITCODE." }

    foreach ($publishedExecutable in @($selfContainedSource, $frameworkDependentSource)) {
        if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf) -or
            (Get-Item -LiteralPath $publishedExecutable).Length -le 0) {
            throw "Published executable is missing or empty: $publishedExecutable"
        }
    }

    Copy-Item -LiteralPath $selfContainedSource -Destination $selfContainedAsset
    Copy-Item -LiteralPath $frameworkDependentSource -Destination $frameworkDependentAsset
    Copy-Item -LiteralPath $selfContainedAsset -Destination (Join-Path $zipStagingDirectory $selfContainedName)
    Copy-Item -LiteralPath $frameworkDependentAsset -Destination (Join-Path $zipStagingDirectory $frameworkDependentName)
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $zipStagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $zipStagingDirectory
    Compress-Archive -Path (Join-Path $zipStagingDirectory '*') -DestinationPath $zipAsset -Force

    $publicAssets = @($selfContainedAsset, $frameworkDependentAsset, $zipAsset)
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

    Write-Host "Packaged $BuildId ($BuildChannel) from commit $SourceCommit"
    Get-ChildItem -LiteralPath $packageDirectory -File | Select-Object Name, Length
}
finally {
    if (Test-Path -LiteralPath $zipStagingDirectory) {
        Remove-Item -LiteralPath $zipStagingDirectory -Recurse -Force
    }
}

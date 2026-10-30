param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ReleaseRoot = Join-Path $ProjectRoot 'artifacts/release'
$Output = Join-Path $ReleaseRoot 'Forma-1.0.0-win-x64'
if (Get-Process Forma.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $Output 'Forma.App.exe') }) { throw 'Close the portable Forma app before publishing.' }
New-Item -ItemType Directory -Force $Output | Out-Null
& $Dotnet publish (Join-Path $ProjectRoot 'src/Forma.App/Forma.App.csproj') -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'LICENSE'),(Join-Path $ProjectRoot 'README.md') -Destination $Output
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'docs') -Destination $Output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'worker/requirements.txt') -Destination (Join-Path $Output 'worker')
$LicenseFolder = Join-Path $Output 'licenses'
New-Item -ItemType Directory -Force $LicenseFolder | Out-Null
$Assets = Get-Content (Join-Path $ProjectRoot 'src/Forma.App/obj/project.assets.json') -Raw | ConvertFrom-Json
$PackageFolders = $Assets.packageFolders.PSObject.Properties.Name
$Inventory = @()
foreach ($Library in $Assets.libraries.PSObject.Properties) {
    if ($Library.Value.type -ne 'package') { continue }
    $Inventory += $Library.Name
    foreach ($PackageFolder in $PackageFolders) {
        $PackagePath = Join-Path $PackageFolder $Library.Value.path
        if (-not (Test-Path -LiteralPath $PackagePath)) { continue }
        $Destination = Join-Path $LicenseFolder ($Library.Name -replace '/', '-')
        New-Item -ItemType Directory -Force $Destination | Out-Null
        Get-ChildItem -LiteralPath $PackagePath -File | Where-Object { $_.Name -match '(?i)license|notice|copying|\.nuspec$' } | Copy-Item -Destination $Destination -Force
        foreach ($NoticePath in $Library.Value.files | Where-Object { $_ -match '(?i)(license|thirdpartynotices)[^/]*\.(txt|md|rtf)$' }) {
            $Source = Join-Path $PackagePath $NoticePath
            if (Test-Path -LiteralPath $Source) { Copy-Item -LiteralPath $Source -Destination (Join-Path $Destination ($NoticePath -replace '[/\\]', '_')) -Force }
        }
    }
}
$RuntimeConfig = Get-Content (Join-Path $Output 'Forma.App.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($Framework in $RuntimeConfig.runtimeOptions.includedFrameworks) {
    $PackageName = $Framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $Inventory += "$PackageName/$($Framework.version)"
    foreach ($PackageFolder in $PackageFolders) {
        $RuntimePath = Join-Path $PackageFolder "$PackageName/$($Framework.version)"
        if (Test-Path -LiteralPath $RuntimePath) {
            $Destination = Join-Path $LicenseFolder "$PackageName-$($Framework.version)"
            New-Item -ItemType Directory -Force $Destination | Out-Null
            Get-ChildItem -LiteralPath $RuntimePath -File | Where-Object { $_.Name -match '(?i)license|notice' } | Copy-Item -Destination $Destination -Force
        }
    }
}
$Inventory | Sort-Object | Set-Content (Join-Path $Output 'DependencyInventory.txt')
Compress-Archive -Path "$Output/*" -DestinationPath "$Output.zip" -CompressionLevel Optimal -Force
Get-FileHash -LiteralPath "$Output.zip" -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  Forma-1.0.0-win-x64.zip" } | Set-Content "$Output.zip.sha256"
$SourceFolder = Join-Path $ReleaseRoot 'Forma-1.0.0-source'
New-Item -ItemType Directory -Force $SourceFolder | Out-Null
foreach ($Name in 'src','worker','tests','scripts','docs') {
    $Base = Join-Path $ProjectRoot $Name
    foreach ($File in Get-ChildItem -LiteralPath $Base -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|__pycache__)[\\/]' }) {
        $Relative = $File.FullName.Substring($ProjectRoot.Length + 1)
        $Target = Join-Path $SourceFolder $Relative
        New-Item -ItemType Directory -Force (Split-Path -Parent $Target) | Out-Null
        Copy-Item -LiteralPath $File.FullName -Destination $Target -Force
    }
}
Get-ChildItem -LiteralPath $ProjectRoot -File | Copy-Item -Destination $SourceFolder -Force
Compress-Archive -Path "$SourceFolder/*" -DestinationPath "$SourceFolder.zip" -Force
Write-Output "Portable release: $Output.zip"

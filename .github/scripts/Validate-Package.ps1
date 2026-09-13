param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "Package not found: $PackagePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath))
try {
    # ZipArchive preserves the separator used by the platform that produced the
    # package. Normalize before validating so Windows-built packages and locally
    # built packages are checked identically.
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $required = @(
        'SentinelProfiles.dll',
        'SentinelProfiles.json',
        'SentinelProfiles.deps.json',
        'assets/icon.png'
    )

    foreach ($name in $required) {
        if ($names -notcontains $name) {
            throw "Package is missing required entry '$name'."
        }
    }

    if ($names | Where-Object { $_ -match '(^|/)(obj|bin)/' }) {
        throw 'Package contains build-directory content.'
    }

    $manifestEntry = $archive.Entries | Where-Object FullName -eq 'SentinelProfiles.json' | Select-Object -First 1
    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    if ($manifest.InternalName -ne 'SentinelProfiles') {
        throw "Unexpected InternalName '$($manifest.InternalName)'."
    }
    if ($manifest.AssemblyVersion -ne $ExpectedVersion) {
        throw "Manifest version '$($manifest.AssemblyVersion)' does not match '$ExpectedVersion'."
    }
    if ([int]$manifest.DalamudApiLevel -ne 15) {
        throw "Unexpected Dalamud API level '$($manifest.DalamudApiLevel)'."
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Validated SentinelProfiles package $ExpectedVersion at $PackagePath"

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -or -not (& dotnet --list-sdks)) {
    throw '.NET 8 SDK is required to rebuild MarksIO.exe.'
}

$watcher = [System.IO.FileSystemWatcher]::new($PSScriptRoot)
$watcher.NotifyFilter = [System.IO.NotifyFilters]::LastWrite -bor [System.IO.NotifyFilters]::FileName
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true

function Build-Exe {
    & dotnet publish .\MarksIO.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o .\dist
    if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed; watching for the next change.' -ForegroundColor Red }
}

try {
    Build-Exe
    Write-Host 'Watching application source and artwork. Press Ctrl+C to stop.'
    while ($true) {
        $change = $watcher.WaitForChanged([System.IO.WatcherChangeTypes]::All)
        if ($change.Name -notmatch '^(?:[^\\/]+\.(?:cs|csproj)|Assets[\\/][^\\/]+\.(?:png|ico))$') { continue }
        Start-Sleep -Milliseconds 300
        while (-not $watcher.WaitForChanged([System.IO.WatcherChangeTypes]::All, 0).TimedOut) { }
        Build-Exe
    }
}
finally {
    $watcher.Dispose()
}

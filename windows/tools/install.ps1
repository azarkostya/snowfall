# Builds Snowfall, installs it to %USERPROFILE%\Snowfall, puts a "Снег вкл-выкл" button on the desktop and starts it.
# CalWall expects the exe at %USERPROFILE%\Snowfall\Snowfall.exe (override with SNOWFALL_EXE).
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\Snowfall.csproj'
$dist = Join-Path $root 'dist'
$target = Join-Path $env:USERPROFILE 'Snowfall'

# A running copy (from dist or the install folder) locks the exe.
Get-Process Snowfall -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $dist -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $dist 'Snowfall.exe') $target -Force
$exe = Join-Path $target 'Snowfall.exe'

$desktop = [Environment]::GetFolderPath('DesktopDirectory')
$shell = New-Object -ComObject WScript.Shell
Remove-Item (Join-Path $desktop 'Снег вкл-выкл.lnk') -ErrorAction SilentlyContinue  # name used by 0.1
$buttons = @(
    @{ Name = 'Зима вкл-выкл.lnk'; Arg = '--toggle'; About = 'Включить или выключить всё: снег, сугробы, украшения' },
    @{ Name = 'Снегопад вкл-выкл.lnk'; Arg = '--snow'; About = 'Включить или выключить снегопад (сугробы остаются)' }
)
foreach ($b in $buttons) {
    $lnk = $shell.CreateShortcut((Join-Path $desktop $b.Name))
    $lnk.TargetPath = $exe
    $lnk.Arguments = $b.Arg
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = $b.About
    $lnk.Save()
}

# If autostart was on for an older path, point it at the installed exe.
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $run -Name Snowfall -ErrorAction SilentlyContinue) {
    Set-ItemProperty -Path $run -Name Snowfall -Value "`"$exe`""
}

# explorer.exe starts it outside any app package container, so %APPDATA%/%LOCALAPPDATA% are the real ones
Start-Process explorer.exe -ArgumentList "`"$exe`""
Write-Output "Installed $exe"

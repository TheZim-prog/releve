# Compile et lance le prototype de combat (Godot 4.7.2 .NET installé par winget).
# Usage, depuis la racine du dépôt :  .\run.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet build src/Game.Client/Game.Client.csproj --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Le vrai exécutable (le raccourci « godot » de winget fait planter la version .NET).
$godot = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\*\Godot_*_mono_win64.exe" |
    Where-Object Name -notlike '*console*' | Select-Object -First 1
if (-not $godot) { throw "Godot .NET introuvable : winget install GodotEngine.GodotEngine.Mono" }

& $godot.FullName --path src/Game.Client @args

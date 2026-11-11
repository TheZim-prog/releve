# Exporte le jeu pour Windows : un dossier autonome (ni .NET ni Godot à installer) et son zip, à envoyer aux amis.
# Usage, depuis la racine du dépôt :  .\export.ps1
# Résultat : publish\Releve\ (Releve.exe, ses fichiers et data\) et publish\Releve-windows.zip.
# Prérequis : les modèles d'export de Godot 4.7.2 .NET (éditeur Godot : Éditeur > Gérer les modèles d'export).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# La version « console » de Godot : elle attend la fin de l'export et affiche ses messages.
$godot = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\*\Godot_*_mono_win64_console.exe" |
    Select-Object -First 1
if (-not $godot) { throw "Godot .NET introuvable : winget install GodotEngine.GodotEngine.Mono" }

$publish = Join-Path $PSScriptRoot 'publish'
$output = Join-Path $publish 'Releve'
$zip = Join-Path $publish 'Releve-windows.zip'
$exe = Join-Path $output 'Releve.exe'

# Dossier vidé d'abord : un fichier supprimé du jeu ou de data/ n'y reste pas.
if (Test-Path $output) { Remove-Item -Recurse -Force $output }
if (Test-Path $zip) { Remove-Item -Force $zip }
New-Item -ItemType Directory -Force $output | Out-Null

# Godot n'exporte un projet C# que s'il trouve une solution à son nom à côté de lui. Le dépôt n'en a qu'une, à la
# racine (Releve.sln) : on en crée une le temps de l'export, puis on la retire (elle est aussi dans .gitignore). S'il
# y en a déjà une (créée par l'éditeur Godot, par exemple), on s'en sert sans y toucher.
$solution = Join-Path $PSScriptRoot 'src\Game.Client\Game.Client.sln'
$temporarySolution = -not (Test-Path $solution)
try {
    if ($temporarySolution) {
        dotnet new sln --format sln --name Game.Client --output src/Game.Client | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Création de la solution temporaire impossible." }
        dotnet sln $solution add src/Game.Client/Game.Client.csproj | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Création de la solution temporaire impossible." }
    }

    # Les lignes de progression ([ 12% ] ...) sont masquées : restent les erreurs et les avertissements.
    & $godot.FullName --headless --path src/Game.Client --export-release 'Windows Desktop' $exe |
        Where-Object { $_ -notmatch '^\S*\[\s*\d+% \]' }
    $exitCode = $LASTEXITCODE
}
finally {
    if ($temporarySolution -and (Test-Path $solution)) { Remove-Item -Force $solution }
}

# Godot peut finir sans code d'erreur alors que l'export a échoué (modèles d'export absents, compilation C# en
# échec) : on vérifie que l'exécutable et le dossier des bibliothèques .NET sont bien là.
$dotnetFiles = Get-ChildItem $output -Directory -Filter 'data_*' | Select-Object -First 1
if ($exitCode -ne 0 -or -not (Test-Path $exe) -or -not $dotnetFiles) {
    throw "L'export a échoué (voir les messages de Godot ci-dessus ; modèles d'export installés ?)."
}

# Le contenu (les JSON de data/) voyage à côté de l'exécutable : le jeu exporté le lit là, et seulement là.
$data = Join-Path $PSScriptRoot 'data'
foreach ($file in Get-ChildItem $data -Recurse -File -Filter '*.json') {
    $target = Join-Path $output ('data' + $file.FullName.Substring($data.Length))
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item $file.FullName $target
}

# Le zip contient le dossier « Releve » lui-même. Le tar de Windows écrit un zip standard (Compress-Archive, sous
# Windows PowerShell, y met des « \ » que les outils hors Windows ne comprennent pas).
& "$env:SystemRoot\System32\tar.exe" -a -c -f $zip -C $publish (Split-Path $output -Leaf)
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $zip)) { throw "Création du zip impossible." }

$size = [math]::Round((Get-Item $zip).Length / 1MB)
Write-Host "Jeu exporté : $output"
Write-Host "À envoyer aux amis : $zip ($size Mo)."
Write-Host "Ils suppriment l'ancien dossier Releve s'ils en ont un, dézippent, puis lancent Releve\Releve.exe."

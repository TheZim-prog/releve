# releve

Jeu tactique coopératif de super-héros (Godot 4 .NET, C#). Conception : [docs/design.md](docs/design.md).

## Prérequis

- SDK .NET 10.
- Godot 4.7.2 **version .NET** (« Mono »), par exemple `winget install GodotEngine.GodotEngine.Mono`.

## Tests du cœur

```
dotnet test Releve.sln
```

## Lancer le prototype de combat

Le plus simple, depuis la racine du dépôt :

```
.\run.ps1
```

Sinon, compiler puis ouvrir le projet `src/Game.Client` dans Godot (bouton Lancer), ou en ligne de commande :

```
dotnet build src/Game.Client/Game.Client.csproj
<chemin vers>\Godot_v4.7.2-stable_mono_win64.exe --path src/Game.Client
```

Lancer l'exécutable réel de Godot, pas le raccourci `godot` créé par winget : la version .NET cherche ses
bibliothèques à côté de l'exécutable.

Le combat (scénario `data/scenarios/street.json`) oppose les six héros, tous commandés par le joueur, à une bande
de rue, avec des civils au milieu :

- **Clic gauche** : choisir un héros, puis une case (le fantôme montre où il ira) ; clic sur le fantôme ou
  **Entrée** pour un déplacement seul.
- **1-9** : armer une compétence, puis cliquer sa cible (unité ou case) ; **R** : relever un allié au contact.
- **Clic droit / Échap** : annuler ; **Tab** : héros prêt suivant.
- **Espace** : pause ; **I** : révéler les intentions ennemies ; **F5** : recommencer.

Options de développement (après `--`) : `--screenshot=fichier.png` (capture puis quitte, après `--at=<secondes>`),
`--select=<id>`, `--skill=<n>`, `--hover=x,y`, `--intentions=on`.

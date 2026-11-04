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

- **Choisir un héros** : clic sur lui, sur sa carte à gauche, **F1-F6**, ou **Tab** (prêt suivant).
- **Clic sur une case** : le fantôme montre où il ira ; re-clic sur le fantôme ou **Entrée** pour un déplacement seul.
- **Compétence** : bouton en bas ou **1-9**, puis clic sur la cible (unité ou case) ; **R** : relever un
  allié au contact.
- **Clic droit / Échap** : annuler.
- **Espace** : pause ; **vitesse** (50 / 75 / 100 %) : les deux touches à droite du 0, ou le pavé numérique - / + ;
  **I** : révéler les intentions ennemies ; **F9** : recommencer ; **H** : afficher l'aide.

Options de développement (après `--`) : `--screenshot=fichier.png` (capture puis quitte, après `--at=<secondes>`),
`--select=<id>`, `--skill=<n>`, `--hover=x,y`, `--intentions=on`.

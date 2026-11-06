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

Le jeu s'ouvre sur un menu :

- **Solo** : le combat (scénario `data/scenarios/street.json`) oppose les six héros, tous commandés par le joueur, à
  une bande de rue, avec des civils au milieu.
- **Héberger** : le jeu fait tourner le serveur et affiche les adresses où les amis peuvent le rejoindre (par
  Tailscale). **Rejoindre** : nom, adresse de l'hôte, port (7777 par défaut). Pour un serveur sans écran (Raspberry
  Pi), voir [docs/serveur-dedie.md](docs/serveur-dedie.md).
- **Salon** : chacun choisit un héros libre (un héros pris par un autre est grisé) ; les héros non choisis sont joués
  par l'IA ; n'importe qui lance quand tout le monde a choisi. En combat, chacun ne commande que son héros ; la pause
  vaut pour tout le monde ; un joueur déconnecté met le combat en pause et se reconnecte tout seul (sinon : Rejoindre
  avec le même nom).

En combat :

- **Choisir un héros** : clic sur lui, sur sa carte à gauche, **F1-F6**, ou **Tab** (prêt suivant).
- **Clic sur une case** : le fantôme montre où il ira ; **Entrée** (ou le bouton) pour un déplacement seul.
- **Compétence** : bouton en bas ou **1-9**, puis clic sur la cible (unité ou case) ; **R** : relever un
  allié au contact. Jauge pas encore pleine : l'ordre partira tout seul dès qu'elle le sera.
- **Clic droit / Échap** : annuler (y compris un ordre donné, tant qu'il n'est pas parti).
- **Espace** : pause ; **I** : révéler les intentions ennemies ; **H** : afficher l'aide ; **F10** : quitter.
- Solo seulement : **vitesse** (50 / 75 / 100 %) avec les deux touches à droite du 0, ou le pavé numérique - / + ;
  **F9** : recommencer (relit `data/`).

Options de développement (après `--`) :

- `--solo` : combat solo direct ; `--ai-heroes=on` : solo où l'IA joue aussi les héros.
- `--host` ou `--join=<adresse>`, avec `--name=`, `--port=`, `--pick=<n° du héros>`, `--start=<nb de joueurs>` :
  connexion, choix du héros et lancement automatiques (pour tester à plusieurs instances).
- `--screenshot=fichier.png` : capture puis quitte, après `--at=<secondes>` (4 par défaut).
- En combat : `--select=<id>`, `--skill=<n>`, `--hover=x,y`, `--intentions=on`.

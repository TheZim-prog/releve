# Serveur dédié

Le serveur (`src/Game.Server`) héberge une partie sans écran : salon, combat, pause, reconnexions. Les joueurs s'y connectent depuis le jeu, par Tailscale (aucun port à ouvrir sur la box).

## Lancer depuis les sources (PC)

```
dotnet run --project src/Game.Server -- --port 7777 --scenario street
```

Options : `--port` (7777 par défaut), `--scenario` (`street` par défaut), `--data <dossier>` (sinon le `data/` copié à côté du serveur, ou celui du dépôt).

## Raspberry Pi 5

1. Publier sur le PC, en version autonome (pas besoin d'installer .NET sur le Pi), dans un dossier vidé d'abord (sinon
   un fichier supprimé de `data/` y resterait) :
   ```
   Remove-Item -Recurse -Force publish/server-pi -ErrorAction SilentlyContinue
   dotnet publish src/Game.Server -c Release -r linux-arm64 --self-contained -o publish/server-pi
   ```
   Le dossier contient le serveur et une copie de `data/`.
2. Copier le dossier sur le Pi (ex. `scp -r publish/server-pi pi@<machine>:~/releve-server`).
3. Sur le Pi : `chmod +x ~/releve-server/Game.Server` puis `~/releve-server/Game.Server`.
4. Les joueurs se connectent à l'adresse Tailscale du Pi (`100.x.y.z` ou son nom MagicDNS), port 7777.

**Le contenu doit être le même partout** : le serveur refuse un joueur dont le `data/` diffère (empreinte affichée au démarrage du serveur). Après un changement d'équilibrage, republier le serveur et mettre à jour le jeu de chacun. De même pour le code : une version différente est détectée en combat (désynchronisation).

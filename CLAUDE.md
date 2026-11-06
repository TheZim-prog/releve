# Projet : jeu tactique coop super-héros

Jeu tactique coopératif (4-5 joueurs, un groupe d'amis), inspiré de Battle Brothers, avec un combat à jauges en temps réel façon ATB de Final Fantasy 4. Univers : super-héros sur une Terre inspirée de la nôtre, ton Invincible.

**Le document de conception complet est dans `docs/design.md`. Le consulter avant toute décision de gameplay.** Ne pas inventer de règle de jeu : si une règle manque ou est ambiguë, poser la question.

## Langue

- Échanges et documentation : **français**.
- Code, noms de types, de méthodes, de fichiers, commits : **anglais**.

## Stack

- **Godot 4, en C#** (.NET).
- Monorepo, une seule solution `.sln`.

## Structure du dépôt

```
CLAUDE.md
docs/design.md            # document de conception (source de vérité gameplay)
data/                     # contenu en données : héros, compétences, ennemis (JSON)
src/
  Game.Core/              # bibliothèque C# PURE : règles, simulation, protocole
  Game.Server/            # application console .NET (serveur dédié)
  Game.Client/            # projet Godot, référence Game.Core
tests/
  Game.Core.Tests/        # tests unitaires du cœur
```

## Principes d'architecture (non négociables)

1. **Game.Core n'a aucune dépendance à Godot.** Toute la logique de jeu y vit. Godot ne fait qu'afficher l'état et capter les entrées.
2. **Simulation déterministe** : jauges, PV, temps en **entiers** (pas de `float` dans la logique), aléatoire via un générateur à **graine contrôlée**. Même entrée = même résultat, toujours.
3. **Commandes et événements** : les joueurs (et l'IA) envoient des **commandes** ; la simulation les valide, les applique et produit des **événements**. Le client ne fait que les afficher. En réseau, le serveur diffuse le **journal de commandes** tick par tick et chaque client fait tourner la même simulation (déterminisme) ; il ne décide de rien.
4. **Serveur autoritaire** : seule la simulation côté serveur fait foi. En solo, le serveur tourne dans le processus du client (même code).
5. **Journal d'événements** : tout combat doit pouvoir être rejoué à l'identique depuis sa graine et sa liste de commandes (reconnexion, replays, débogage).
6. **Contenu en données** : héros, compétences, ennemis décrits dans `data/`, pas codés en dur.
7. **Prévoir les scripts de mission** dès l'architecture : actions imposées, événements déclenchés, alliés aux actions prédéfinies (nécessaire pour le tutoriel).

## Règles de combat clés (résumé, détails dans `docs/design.md` section 3)

- **Grille de carrés**. Diagonale = 1 case. **Pas de coupe d'angle** (déplacement et ligne de vue).
- **Temps réel** à ticks fixes (ex. 20 ticks/s, à confirmer). Chaque unité a une **jauge** qui se remplit selon son **initiative**.
- **Jauge pleine** → l'unité peut faire **un déplacement et/ou une action**, envoyés en **une seule commande atomique**. La jauge **reste pleine** tant qu'elle n'agit pas.
- Après l'action, la jauge redémarre selon le **poids** : déplacement seul ~50 %, action légère ~40 %, action lourde 0 % (valeurs à paramétrer).
- **Ordre à l'avance** (à l'essai) : un ordre peut être donné avant jauge pleine ; il part seul dès que l'unité peut agir, modifiable d'ici là, annulé s'il est devenu impossible.
- **Tout le monde agit en parallèle.** Conflit : la première commande validée gagne ; une commande devenue impossible est annulée et la jauge reste pleine.
- **Logique instantanée**, animations purement visuelles. Exception : **attaques chargées** (zone affichée, déclenchement différé, esquivables).
- **Temps de recharge** des compétences en temps de combat ; **pas de mana**.
- **États** : étourdi (jauge gelée), déséquilibré (jauge ralentie).
- **Attaques d'opportunité** globales, 1 par cycle (rechargée quand l'unité rejoue).
- **Intentions ennemies** : l'IA fixe son action + cible avant jauge pleine (ex. à mi-jauge), ne la change que si elle devient impossible. Cachées par défaut, révélables.
- Stats séparées : **initiative** (vitesse de jauge) et **mobilité** (distance de déplacement).
- **Pause manuelle** déclenchable par n'importe quel joueur.

## Objectif actuel : prototype du combat libre

Valider que le combat ATB temps réel sur grille est amusant, **avant** tout le reste (pas de réseau, pas de carte du monde, pas de tutoriel scripté pour l'instant).

Étapes :
1. **Game.Core + tests** : grille, déplacements (pathfinding sans coupe d'angle), jauges et ticks, commandes atomiques, poids des actions, recharges, états, attaques d'opportunité, attaques chargées, intentions de l'IA.
2. **Contenu** : les 6 héros (colosse, speedster, énergétique, télépathe, tech, combattant) et les ennemis du prologue (cogneur, tireur, brute, caïd), en données. Kits détaillés dans `docs/design.md` section 3.
3. **Affichage Godot en solo** : 3D, caméra fixe plongeante, **capsules colorées** sur la grille. Prévisualisation avec fantôme, zones au sol, jauges visibles.
4. Ensuite seulement : réseau, puis le reste.

## Façon de travailler

- Écrire les **tests** de Game.Core en même temps que la logique.
- Petites étapes, chacune jouable ou testable.
- Toute valeur d'équilibrage (durées, pourcentages, dégâts) est un **paramètre en données**, jamais une constante enfouie dans le code.
- Mettre à jour `docs/design.md` quand une décision de gameplay change (avec mon accord).

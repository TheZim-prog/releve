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

## Objectif actuel : des missions jouables

Le combat est en place ; on construit maintenant ce qui en fait des **missions** (objectifs, scripts), toujours sans carte du monde ni campagne.

Déjà fait :
1. **Combat libre** : Game.Core + tests (grille, déplacements, jauges et ticks, commandes atomiques, états, attaques d'opportunité, attaques chargées, intentions de l'IA), affichage Godot en solo (3D, capsules colorées, prévisualisation, jauges).
2. **Contenu** : les 6 héros et les ennemis du prologue, et les **6 arbres de compétences complets** (toutes les compétences, variantes et passifs codés), en données.
3. **Réseau** : serveur dédié, salon, combat en réseau par journal de commandes, export Windows.
4. **Objectifs de mission** (`docs/design.md` 3.3 et 3.5), décrits par le scénario : élimination, sauvetage, poursuite, course contre la montre, protection (personnes ou structure), objectifs principaux et secondaires, chefs dont les sbires fuient ou se rendent, vagues de renforts. Mode d'emploi dans `data/README.md`.
5. **Missions d'essai** : un scénario court par type d'objectif et un scénario combiné (`data/scenarios/objectives/`), choisissables dans le menu, joués jusqu'au bout par les tests avec des héros IA.

6. **Scripts de mission** (principe 7), décrits par le scénario : déclencheurs (temps, unité tombée, unité prête, déclencheur terminé), messages qui peuvent figer le combat, actions imposées aux héros, actions ordonnées aux unités du jeu (allié scénarisé, coup réservé au script, décor qui s'effondre), départ d'une unité, verrou du tutoriel (personne n'agit hors du script). Démonstrations dans `data/scenarios/scripts/`, mode d'emploi dans `data/README.md`.
7. **Mission 1 du prologue** (`docs/design.md` 6.2) : le tutoriel scénarisé puis le champ libre (`data/scenarios/prologue/mission-1.json`), jouée jusqu'au bout par les tests avec des héros IA.

8. **Dégâts collatéraux et bilan de réputation** (`docs/design.md` 3.4) : le combat relève le décor cassé par les héros et les civils touchés ; le bilan de la mission (valeurs dans `data/reputation.json`) s'affiche à la fin. La réputation du groupe, qui cumule les bilans, viendra avec la campagne.

## Objectif suivant : la base et le monde (`docs/design.md` sections 4 et 5)

On construit la campagne avant de finir le prologue (dont les missions passent par le QG et l'académie). Étapes :
1. ✅ **État de campagne** (`Game.Core/Campaign`) : joueurs reconnus par un identifiant unique, leurs héros, pot commun, réputation bornée lue en paliers, date de chaque joueur, chapitre ; tenu et sauvegardé par le serveur (`--campaign`, ou la partie hébergée), bilan de chaque combat ajouté, réputation affichée au salon.
2. ✅ **Académie** (`Game.Core/Campaign/Academy.cs`) : chaque joueur crée son duo (archétype libre, nom, points de compétence de départ dans les paliers ouverts), depuis le salon d'une partie avec campagne.
3. **QG minimal**, en trois morceaux :
   - 3a ✅ **Quêtes avec ses héros** : le salon d'une campagne est le QG (panneau de quêtes de `data/quests/`, héros déployés, lancement) ; les scénarios de quête ont des `heroSlots` ; le lancement transporte les fiches des héros déployés. Héberger : la campagne ou un combat libre.
   - 3b ✅ **Récompenses** (`QuestRewards`) : XP à chaque héros parti (table de niveaux et part en cas de défaite dans `progression.json`), matériaux au pot commun si victoire, niveaux gagnés ; le QG montre XP, points à dépenser, pot et dernière quête.
   - 3c ✅ **Entraînement à l'académie** (`Training`) : dépenser les points gagnés (stats, cases de l'arbre) et changer l'équipement (variantes, passif), vérifié par les règles de progression.
   - 3d ✅ **Campagne solo** : « Campagne en solo » au menu, un serveur dans le jeu qui n'écoute que ce PC, sur la même sauvegarde que la campagne hébergée.
4. ✅ **Chutes et mort des héros** (`Injuries`, `docs/design.md` 1.4) : chaque chute compte (une demie si secourue), mort à la 3e, récupération au fil des quêtes sans lui, mémorial au QG.
5. **Carte du monde** : chemins, points d'intérêt, crochets qui aggravent la mission.
   - 5a ✅ **Cœur** (`Game.Core/World`, `data/world/`) : carte, événements (scène à choix, test du meilleur héros), trajet (`Travel`), gravité par crochet (`GravityStep` : renforts, civils perdus, compte à rebours), bonus de quête (`QuestModifiers` : jauge, armure, stats).
   - 5b ✅ **Serveur** (`ServerSession.Travel.cs`) : la phase de trajet entre le lancement d'une quête et son combat (`TravelTo`, `ResolveEvent`), décidée par le joueur qui a choisi la quête ; tirages à graine ; trajet gardé dans la sauvegarde et repris au redémarrage.
   - 5c **Client** : l'écran de la carte.
6. **Quêtes en parallèle** en réseau, calendrier propre à chaque joueur.

Ensuite : la mission 2 du prologue (`docs/design.md` 6.2) et la fin du prologue.

## Façon de travailler

- Écrire les **tests** de Game.Core en même temps que la logique.
- Petites étapes, chacune jouable ou testable.
- Toute valeur d'équilibrage (durées, pourcentages, dégâts) est un **paramètre en données**, jamais une constante enfouie dans le code.
- Mettre à jour `docs/design.md` quand une décision de gameplay change (avec mon accord).

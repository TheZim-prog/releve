# Contenu du jeu

Tout ce qui se règle sans toucher au code. Les fichiers sont du JSON avec commentaires (`//`) ; ils sont tous vérifiés
au lancement du jeu, du serveur et des tests, et une erreur dit quel fichier ou quel identifiant est en cause.

Les identifiants et les noms de fichiers sont en anglais ; les noms affichés (`name`) sont en français.

## Organisation

| Dossier ou fichier | Contenu |
|---|---|
| `combat-rules.json` | Réglages du combat : jauge, poids des actions, états, couverture, valeur d'un point de stat. |
| `progression.json` | Niveaux, points de stats et de compétences, emplacements, forme des arbres. |
| `skills/` | La bibliothèque de compétences : chacune est décrite **une seule fois**, dans le fichier de son archétype et de sa branche. |
| `units/` | Les types d'unités : archétypes de héros (stats de base), ennemis, gadgets, civils. |
| `trees/` | Un arbre de compétences par archétype : branches, paliers, variantes, passifs. |
| `heroes/` | Des héros prêts à jouer : ceux du prologue (`prologue.json`) et des héros d'essai (`trial.json`). Niveau, points de stats, cases achetées, équipement. |
| `maps/` | Des cartes partagées entre scénarios (`"mapId": "street"` dans un scénario, au lieu d'écrire `"map"`). |
| `scenarios/` | Les combats : la carte, les unités et les héros placés dessus, et les objectifs. Le scénario se choisit dans le menu du jeu. |

## Qui désigne qui

Rien n'est copié : on désigne par identifiant.

- Une **unité** liste ses compétences : `"skills": ["punch"]`.
- Une **compétence** d'invocation désigne l'unité qu'elle pose : `"summon": "turret"`.
- Un **arbre** désigne ses compétences et leurs variantes : `{ "skill": "dome", "variant": "personal-shield" }`.
- Un **héros** désigne son archétype, les cases qu'il a achetées et ce qu'il équipe.
- Un **scénario** place soit une unité (`"unit": "brawler"`), soit un héros (`"hero": "prologue-colossus"`).

## Gestes courants

**Régler une valeur** (dégâts, portée, recharge, durée) : la changer dans le fichier de la compétence, sous `skills/`.
Les dégâts écrits sont ceux d'une recrue ; la puissance du héros les multiplie.

**Ajouter une compétence prévue par un arbre** :

1. la décrire dans `skills/<archétype>/<branche>.json` ;
2. la retirer de la liste `"planned"` de l'arbre (le jeu refuse de se lancer tant qu'elle est aux deux endroits) ;
3. un héros peut alors l'équiper.

**Ajouter un passif** : il faut du code (`PassiveKind`), puis retirer `"planned": true` de sa case dans l'arbre.

**Essayer une compétence ou une variante en jeu** : la mettre dans `equipped` chez un héros de `heroes/trial.json`,
puis lancer le scénario « Essai des nouvelles compétences ». Le jeu relit `data/` à chaque lancement d'un solo ou
d'une partie hébergée, et F9 le relit sans quitter un combat solo. Ce fichier est fait pour être modifié : aucun test
ne dépend de ce que ces héros équipent.

**Donner des objectifs à un combat** : une liste `"objectives"` dans le scénario, par exemple
`[ { "kind": "eliminate" } ]`. Ajouter `"secondary": true` à un objectif qui ne compte que pour le bilan ; il faut au
moins un objectif principal. Sans liste, le combat est une élimination de tous les ennemis. Dans tous les cas, perdre
tous les héros est une défaite. Types disponibles : `eliminate` (plus aucun ennemi debout : ceux qui sont enfuis ou en fuite ne comptent plus) ;
`rescue` avec `"minimum": 2` (évacuer au moins 2 civils ; raté dès qu'il n'en reste plus assez ; quand plus aucun
ennemi n'est en lice, et à la victoire, les civils encore sur la carte comptent comme évacués) ;
`pursuit` avec `"exits": [ { "x": 17, "y": 0 } ]` (le vilain marqué `"pursued": true` dans les unités court dès le
début vers ces sorties, qui ne doivent pas être sa case de départ ; réussi s'il est vaincu, à terre ou rendu, raté s'il
en atteint une ou s'il court encore à la victoire ; une seule poursuite et un seul vilain poursuivi par combat). Les
sorties sont surlignées en violet au sol ;
`countdown` avec `"durationMs": 60000` et `"devices": [ { "x": 8, "y": 4, "interactions": 3 } ]` (neutraliser chaque
objet, posé sur une case libre, en autant d'interactions au contact, avant la fin du compte à rebours ; les héros y
travaillent avec R, « interagir » ; le poids de l'interaction est `operateWeight` dans `combat-rules.json`) ;
`protect` avec `"until": "waves"` (réussi quand toutes les vagues sont arrivées et vaincues) ou `"until": "time"` et
`"durationMs": 60000` (réussi quand le temps est tenu) : les unités marquées `"protected": true` (des alliés du camp 0
commandés par le jeu, pas des héros) doivent rester debout ; une seule à terre et c'est raté. Leur type d'unité dit
comment elles réagissent : `"behavior": "basic"` pour qu'elles se défendent, `"behavior": "cautious"` pour qu'elles
reculent loin des ennemis quand on les attaque (sans jamais frapper). Les ennemis les visent en priorité quand ils
peuvent les frapper. Pour protéger un **lieu**, on protège une structure : un type d'unité avec `"isStructure": true`
(comportement `inert`, initiative et mobilité à 0, ni compétence ni attaque d'opportunité, retirée à 0 PV). Elle ne
bouge pas, ne subit pas d'états, ne se répare pas ; personne ne traverse sa case, mais on voit par-dessus.

**Faire arriver des renforts** : une liste `"waves"` dans le scénario, par exemple
`[ { "atMs": 20000, "units": [ { "unit": "brawler", "team": 1, "x": 17, "y": 3 } ] } ]`. Chaque vague arrive au
moment dit ; une unité dont la case est prise arrive sur la case libre la plus proche. Tant qu'une vague doit encore
amener des ennemis, une élimination n'est pas finie.

**Désigner le chef d'une bande** : ajouter `"leader": "flee"` ou `"leader": "surrender"` à une unité du scénario (pas un
héros ; un seul chef par camp). Quand il tombe (vaincu, à terre ou enfui), les autres unités de son camp prennent la
fuite (`flee`) ou se rendent et quittent le combat (`surrender`). Dans les deux cas, elles ne comptent plus : abattre
le chef suffit à gagner une élimination.

**Essayer les objectifs en jeu** : les scénarios de `scenarios/objectives/` (« Objectifs : … » dans le menu) en
montrent un par type (élimination avec chef et renforts, sauvetage, poursuite, bombes, protection d'un témoin et d'un
relais), plus une mission combinée. Les personnes à protéger sont dans `units/allies.json`, les structures dans
`units/structures.json`. Leurs valeurs sont des valeurs d'essai, à ajuster en jouant.

**Changer le kit d'un héros prêt à jouer** : modifier `learned`, `equipped` et `passive` dans `heroes/`. Les règles de
progression sont vérifiées : pas plus de cases que de points de compétence, un palier ouvert par les précédents, et
seulement des compétences déjà codées dans l'équipement.

# Contenu du jeu

Tout ce qui se règle sans toucher au code. Les fichiers sont du JSON avec commentaires (`//`) ; ils sont tous vérifiés
au lancement du jeu, du serveur et des tests, et une erreur dit quel fichier ou quel identifiant est en cause.

Les identifiants et les noms de fichiers sont en anglais ; les noms affichés (`name`) sont en français.

## Organisation

| Dossier ou fichier | Contenu |
|---|---|
| `combat-rules.json` | Réglages du combat : jauge, poids des actions, états, couverture, valeur d'un point de stat. |
| `reputation.json` | Bilan de réputation d'une mission : ce que coûte chaque dégât collatéral, ce que rapportent les civils sauvés et les objectifs secondaires. |
| `progression.json` | Niveaux, points de stats et de compétences, emplacements, forme des arbres. |
| `skills/` | La bibliothèque de compétences : chacune est décrite **une seule fois**, dans le fichier de son archétype et de sa branche. |
| `units/` | Les types d'unités : archétypes de héros (stats de base), ennemis, gadgets, civils. |
| `trees/` | Un arbre de compétences par archétype : branches, paliers, variantes, passifs. |
| `heroes/` | Des héros prêts à jouer : ceux du prologue (`prologue.json`) et des héros d'essai (`trial.json`). Niveau, points de stats, cases achetées, équipement. |
| `maps/` | Des cartes partagées entre scénarios (`"mapId": "street"` dans un scénario, au lieu d'écrire `"map"`). |
| `quests/` | Les quêtes du panneau du QG : leur scénario, leur difficulté affichée, leurs récompenses. |
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
(comportement `inert`, initiative et mobilité à 0, ni attaque d'opportunité ni compétence sauf celles réservées au
script, retirée à 0 PV). Elle ne
bouge pas, ne subit pas d'états, ne se répare pas ; personne ne traverse sa case, mais on voit par-dessus.

**Régler la réputation** : `reputation.json` dit ce que vaut chaque fait dans le bilan de fin de mission (affiché au
journal) : décor détruit par les héros, civils blessés ou perdus par leur faute, civils perdus face aux ennemis (malus,
négatifs) ; civils sauvés et objectifs secondaires réussis (bonus, positifs). Le jeu refuse un malus positif ou un bonus
négatif. Sa partie `scale` règle la réputation du groupe, qui cumule les bilans : ses bornes, sa valeur de départ et
ses paliers nommés (chacun commence à sa valeur `from`, le premier au minimum).

**Tenir une campagne** : le serveur dédié la tient avec `--campaign <fichier>` (créé s'il n'existe pas) ; une partie
hébergée depuis le jeu tient celle de l'hôte (`campaign.json` dans les données du jeu sur son PC). Chaque joueur y est
inscrit à son arrivée, et le bilan de réputation de chaque combat terminé s'ajoute à celle du groupe, affichée au salon.
Le salon d'une partie avec campagne est le **QG** : le panneau de quêtes, le héros que chacun déploie (ou il reste au
QG), et l'académie, où chacun crée ses héros : jusqu'à `heroesPerPlayer` héros vivants (`progression.json`, le duo),
chacun avec son archétype, son nom et ses points de compétence de départ dans son arbre. Au menu du jeu, « Héberger la
campagne » ouvre ce QG ; « Héberger un combat libre » joue le scénario choisi, sans toucher à la campagne.

**Écrire une quête** : un scénario avec des cases pour les héros des joueurs (`"heroSlots": [ { "x": 1, "y": 4 }, … ]`,
une par héros, dans l'ordre de leur déploiement ; prévoir cinq cases pour un groupe complet), rangé dans
`scenarios/quests/`, puis une entrée dans un fichier de `quests/` : `id`, `name`, `description`, `scenario`,
`difficulty` (`veryEasy`, `easy`, `hard`, `veryHard` : le libellé affiché), `xp` (reçue par chaque héros participant) et
`materials` (versés au pot commun, ex. `{ "materials": 3 }`). Ces scénarios ne sont pas proposés en combat libre.

**Régler l'XP et les niveaux** (`progression.json`) : `xpToReach` donne l'XP totale de chaque niveau, du 2e au dernier ;
une quête gagnée donne son XP à chaque héros qui y est parti, et ses matériaux au pot commun ; perdue, seulement
`defeatXpPercent` % de son XP, et pas de matériaux. Les points des niveaux gagnés attendent d'être dépensés à l'académie :
le bouton « Entraîner » d'un héros y met un point de stats dans les PV, la puissance ou l'initiative, apprend une case
d'un palier ouvert (définitif), et change son équipement (une compétence ou sa variante par emplacement, et son passif).

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

**Écrire un script de mission** : donner un repère aux unités dont le script parle (`"tag": "kingpin"` dans les unités
ou les vagues, unique dans le scénario), puis une liste `"script"` de déclencheurs :

```json
"script": [
  { "id": "briefing", "do": [ { "speaker": "Télépathe", "say": "Restez groupés.", "freeze": true } ] },
  { "id": "taunt", "when": { "atMs": 6000 }, "do": [ { "speaker": "Le caïd", "say": "Partez !" } ] },
  { "id": "first-down", "when": { "unitDown": "first-thug", "after": "briefing" }, "do": [ { "say": "Un de moins." } ] }
]
```

Un déclencheur part une seule fois, quand **toutes** ses conditions sont réunies (sans `when` : dès le début) :
`atMs` (temps de combat écoulé, le temps figé ne compte pas), `unitDown` (l'unité de ce repère est à terre ou hors
du combat), `unitReady` (l'unité de ce repère est prête à agir : debout, jauge pleine, ni étourdie ni en train de
charger), `after` (cet autre déclencheur est terminé). Ses effets se jouent dans l'ordre. Pour l'instant, un effet est
un message : `say` (le texte), `speaker` (qui parle ; sans lui, c'est une indication du jeu) et `"freeze": true` pour
**figer le combat** jusqu'à ce qu'un joueur clique « Continuer » (ou Entrée) ; les effets suivants attendent. Un
déclencheur est terminé quand tous ses effets le sont. Le script s'arrête à la fin du combat : un message prévu pour la
chute du dernier ennemi ne s'affichera pas. Exemple jouable : « Script : messages » (`scenarios/scripts/`).

**Imposer des actions** (tutoriel) : un effet `"impose"` donne à des héros repérés chacun une action, à faire en même
temps ; `say` (et `speaker`) en donne la consigne, affichée jusque-là :

```json
{ "say": "Combattant : étourdis-le. Speedster : va en (6, 5).", "impose": [
    { "unit": "fighter", "skill": "stunning-blow", "target": "thug" },
    { "unit": "speedster", "moveTo": { "x": 6, "y": 5 } } ] }
```

Le combat est figé, ces héros ont la jauge pleine et ne peuvent faire **que** l'action demandée ; le temps reprend quand
toutes sont faites. `skill` est une compétence du héros, visée comme elle le demande : `target` (un repère) pour une
compétence sur une unité, plus `cell` pour celles qui la déplacent quelque part (télékinésie) ; `cell` seule pour une
zone ; rien pour une compétence sur soi (le jeu refuse au lancement une visée qui ne va pas). Le déplacement, lui, peut
rester libre : `moveTo` impose la case d'arrivée, et sans lui le héros se place où il veut avant d'agir ; sans `skill`,
c'est un déplacement seul. Une action devenue impossible (cible tombée, héros étourdi, confus ou dominé) est abandonnée
et le héros retrouve sa jauge d'avant ; un héros joué par l'IA fait la sienne lui-même. Exemple jouable : « Script :
actions imposées ».

**Faire agir une unité du jeu** (allié scénarisé, coup scénarisé d'un ennemi) : un effet `"act"`, avec la même forme
d'actions que `"impose"`, mais pour n'importe quelle unité repérée ; `say` y est une réplique dite juste avant :

```json
{ "speaker": "Le caïd", "say": "Tu vieillis !", "act": [ { "unit": "kingpin", "skill": "treacherous-shot", "target": "telepath" } ] }
```

L'unité agit **aussitôt**, sans attendre sa jauge ni la recharge de la compétence, puis sa jauge repart comme après
toute action ; elle se déplace d'abord s'il le faut (sauf `moveTo` imposé). Le reste des règles compte : à terre,
étourdie, en charge, en fuite, désarmée, immobilisée, ou sans ligne de vue, elle échoue (le script continue, et le
journal le dit). Un sauvetage ou une télékinésie vise une unité et la case où elle va (`target` et `cell`). Pour un
allié qui n'agit **que** sur ordre, donner `"behavior": "scripted"` à son entrée dans le scénario : un héros ainsi
marqué n'est pas jouable, et il ne suffit pas à éviter la défaite si tous les autres tombent (`behavior` remplace le
comportement de n'importe quelle unité, renforts compris, s'il convient à son type). Une compétence marquée `"scriptOnly": true` (comme le Tir traître du caïd)
n'est jamais choisie par l'IA d'elle-même. Exemple jouable : « Script : allié et coups scénarisés ».

**Faire partir une unité** : un effet `"leave"` fait quitter le combat aux unités du jeu repérées (pas aux héros),
aussitôt, comme un fuyard qui atteint le bord : enfuies, pas vaincues (elles ne comptent plus pour une élimination, et
un chef qui part ainsi entraîne ses sbires) ; `say` y est une réplique dite juste avant :
`{ "speaker": "Le caïd", "say": "On se reverra !", "leave": ["kingpin"] }`.

**Faire s'effondrer le décor** : une structure peut porter des compétences réservées au script (`"scriptOnly": true`,
dans `skills/scenery.json`), qu'elle n'utilise que sur un ordre `"act"`. La Façade instable (`unstable-facade`) a
l'Effondrement : une zone chargée qui touche tout le monde, affichée au sol, bloquée par un dôme. Elle ne sert qu'une
fois (`"maxActivations": 1`) puis disparaît, ce qu'un déclencheur `unitDown` peut attendre pour passer à la suite.
Une telle compétence frappe sans déplacer la structure ni rien poser (le jeu refuse au lancement un mouvement, une
invocation, un dôme…). Attention : si l'ordre échoue (case visée hors de portée ou de vue après une retouche de la
carte), la structure ne tombe pas, et un verrou levé par sa chute ne le serait jamais ; les tests des scénarios
d'essai le vérifient (aucun ordre du script ne doit échouer).
Exemple jouable : « Script : effondrement et fuite » (le dôme du colosse sauve les civils pendant que le caïd file).

**Verrouiller le combat** (tutoriel) : un effet `{ "lock": true }`, seul dans son effet, met le verrou ;
`{ "lock": false }` le lève. Sous le verrou, le temps s'écoule (les jauges se remplissent), mais personne n'agit hors
du script : les héros ne font que leurs actions imposées (un ordre à l'avance attend la levée du verrou), les ennemis
et les alliés du jeu que les actions ordonnées, et les civils et les fuyards (qui ne prennent pas d'ordres) restent sur
place. Les gadgets agissent comme d'habitude, et ce qui ne se décide pas suit son cours : une attaque chargée déjà
lancée part, les attaques d'opportunité jouent. Une condition `unitReady` sur une unité hors de combat est remplie
(elle ne sera jamais prête : l'action imposée qui suit est abandonnée), pour que le script ne reste pas bloqué. Avec
`unitReady`, c'est ce qui permet de montrer les jauges : le speedster rejoue deux fois avant que le colosse soit prêt.
Exemple jouable : « Script : verrou et jauges ».

**Les missions du prologue** sont dans `scenarios/prologue/` (« Prologue 1 : La rue » dans le menu) : le tutoriel
scénarisé sous le verrou, puis le champ libre. Les héros sans joueur y sont joués par l'IA, qui fait aussi leurs
actions imposées. Un test la joue jusqu'au bout et vérifie chaque étape : après une retouche (carte, dégâts, jauges),
le lancer dit si une action imposée ou un ordre du script ne passe plus.

**Changer le kit d'un héros prêt à jouer** : modifier `learned`, `equipped` et `passive` dans `heroes/`. Les règles de
progression sont vérifiées : pas plus de cases que de points de compétence, un palier ouvert par les précédents, et
seulement des compétences déjà codées dans l'équipement.

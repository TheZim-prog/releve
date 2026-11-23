using Game.Core.Campaign;
using Game.Core.Combat;
using Game.Core.Reputation;

namespace Game.Core.Network;

// Le QG d'une partie avec campagne : académie, panneau de quêtes, héros déployés, lancement d'une quête, et ce qu'un
// combat terminé laisse à la campagne. Chaque joueur est inscrit dans la campagne à son arrivée (cf. Join).
public sealed partial class ServerSession
{
    private string? _questId;

    /// <summary>
    /// La quête en cours, ses héros de campagne en combat, et les joueurs partis (même ceux dont le héros est mort en
    /// route : ils avancent de sa durée), ou <c>null</c>.
    /// </summary>
    private (QuestDefinition Quest, IReadOnlyList<(string Hero, UnitId Unit)> Deployed, IReadOnlyList<string> Party)? _questInCombat;

    /// <summary>Ce qu'a rapporté la dernière quête depuis le lancement du serveur, ou <c>null</c>.</summary>
    private QuestResult? _lastQuest;

    /// <summary>Le salon est le QG d'une campagne (sinon, il joue le scénario du serveur).</summary>
    private bool IsCampaign => _campaign is not null;

    private QuestDefinition? ChosenQuest => _questId is { } id ? _content.Quests.GetValueOrDefault(id) : null;

    /// <summary>L'académie : le joueur recrute un héros dans la campagne (au salon seulement).</summary>
    private void RecruitFor(Player player, Recruit? recruit)
    {
        if (!InCampaignLobby(player))
            return;
        if (_content.Heroes is not { } catalog)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NoCampaign));
            return;
        }
        if (recruit is null)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.UnknownCommand));
            return;
        }
        if (Academy.Check(_campaign!.State, catalog, player.Uid, recruit) is { } refusal)
        {
            player.Peer!.Send(new RecruitRefused(refusal));
            return;
        }
        SaveOrTell(player, state => Academy.Enlist(state, catalog, player.Uid, recruit));
        BroadcastLobby();
    }

    /// <summary>L'académie : le joueur entraîne un de ses héros (points, cases, équipement), au salon seulement.</summary>
    private void TrainFor(Player player, Func<CampaignState, Progression.HeroCatalog, TrainingResult> train)
    {
        if (!InCampaignLobby(player))
            return;
        if (_content.Heroes is not { } catalog)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NoCampaign));
            return;
        }
        // Appliqué à l'état même qui est sauvegardé ; refusé, il ne change rien (et rien n'est réécrit).
        TrainingResult? result = null;
        SaveOrTell(player, state =>
        {
            result = train(state, catalog);
            return result.State ?? state;
        });
        if (result!.Refusal is { } refusal)
        {
            player.Peer!.Send(new TrainingRefused(refusal));
            return;
        }
        BroadcastLobby();
    }

    /// <summary>N'importe quel joueur choisit la quête du groupe (ou aucune).</summary>
    private void ChooseQuestFor(Player player, string? questId)
    {
        if (!InCampaignLobby(player))
            return;
        // Seulement une quête de son panneau : parue à sa date, pas encore jouée.
        if (questId is not null && Calendar.Board(_campaign!.State, _content.Quests.Values, player.Uid).All(q => q.Id != questId))
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.UnknownQuest));
            return;
        }
        _questId = questId;
        _questChooser = questId is null ? null : player.Uid;
        BroadcastLobby();
    }

    /// <summary>Le joueur part avec l'un de ses héros vivants, ou reste au QG.</summary>
    private void DeployFor(Player player, string? heroId)
    {
        if (!InCampaignLobby(player))
            return;
        if (heroId is not null && HeroRefusal(player, heroId) is { } refusal)
        {
            player.Peer!.Send(new RequestRefused(refusal));
            return;
        }
        player.Deployed = heroId;
        BroadcastLobby();
    }

    /// <summary>
    /// Lance la quête choisie avec les héros déployés : le trajet sur la carte, s'il y en a une, sinon le combat tout
    /// de suite ; ou explique au joueur qui l'a demandé pourquoi c'est impossible. Le combat est construit avant que rien
    /// ne change : une fiche de héros que les données ne permettent plus (une compétence renommée depuis son recrutement)
    /// est refusée sans laisser de trace, et sans faire le trajet pour rien.
    /// </summary>
    private void StartQuest(Player player)
    {
        if (QuestHeroes(player) is not { } heroes)
            return;
        var quest = ChosenQuest!;
        var launch = LaunchOf(quest, heroes);
        if (Build(launch, reason => player.Peer!.Send(new RequestRefused(reason))) is not { } setup)
            return;
        if (HasRoute(quest))
        {
            Depart(player, quest, heroes);
            return;
        }
        SaveOrTell(player, state => Leaving(state, quest, heroes));
        LaunchQuest(quest, heroes, launch, setup, PartyOf(heroes));
    }

    /// <summary>
    /// Les joueurs de ces héros partent ensemble : chacun saute à la date la plus avancée d'entre eux, jamais avant la
    /// parution de la quête (cf. 4.7).
    /// </summary>
    private CampaignState Leaving(CampaignState state, QuestDefinition quest, IReadOnlyList<CampaignHero> heroes) =>
        _content.Heroes?.Rules is { } rules ? Calendar.Depart(state, PartyOf(heroes), quest, rules) : state;

    /// <summary>Les joueurs de ces héros.</summary>
    private static IReadOnlyList<string> PartyOf(IEnumerable<CampaignHero> heroes) => heroes.Select(h => h.Owner).Distinct().ToList();

    /// <summary>Le combat de ce lancement, ou <c>null</c> si les données ne permettent plus un héros (dit à <paramref name="tell"/>).</summary>
    private CombatSetup? Build(CombatLaunch launch, Action<RequestRefusal> tell)
    {
        try
        {
            return launch.BuildSetup(_content);
        }
        catch (ArgumentException)
        {
            tell(RequestRefusal.HeroUnavailable);
            return null;
        }
    }

    /// <summary>Le combat de la quête commence (au départ du QG, ou à l'arrivée au bout du trajet).</summary>
    private void LaunchQuest(QuestDefinition quest, IReadOnlyList<CampaignHero> heroes, CombatLaunch launch, CombatSetup setup,
        IReadOnlyList<string> party)
    {
        foreach (var assignment in launch.Heroes)
            _players.Single(p => p.Id == assignment.Player).Hero = assignment.Unit;
        var scenario = _content.Scenarios[quest.Scenario];
        _questInCombat = (quest, heroes.Select((h, i) => (h.Id, scenario.DeployedUnitId(i))).ToList(), party);
        _lastEvent = null; // ce que la route a donné n'appartient plus qu'à la quête d'avant
        Launch(launch, setup);
        // Un joueur parti en route garde sa place : le combat l'attend.
        if (!AllCombatantsConnected)
            SetPaused(true, byPlayer: null);
    }

    /// <summary>
    /// Le combat de la quête avec ces héros, sur les cases du scénario dans cet ordre : chacun est joué par son joueur
    /// s'il est dans la partie, sinon par l'IA.
    /// </summary>
    private CombatLaunch LaunchOf(QuestDefinition quest, IReadOnlyList<CampaignHero> heroes)
    {
        var scenario = _content.Scenarios[quest.Scenario];
        var assignments = heroes
            .Select((h, i) => _players.Find(p => p.Uid == h.Owner) is { } player ? new HeroAssignment(scenario.DeployedUnitId(i), player.Id) : null)
            .OfType<HeroAssignment>()
            .ToList();
        return new CombatLaunch(quest.Scenario, assignments, heroes.Select(h => h.Sheet).ToList());
    }

    /// <summary>
    /// Les héros déployés pour la quête choisie (dans l'ordre d'arrivée des joueurs), ou <c>null</c> (le joueur qui a
    /// demandé le lancement sait pourquoi). Rien n'est encore changé.
    /// </summary>
    private IReadOnlyList<CampaignHero>? QuestHeroes(Player player)
    {
        if (ChosenQuest is not { } quest)
            return Refuse(player, RequestRefusal.NoQuestChosen);
        var going = _players.Where(p => p.Deployed is not null).ToList();
        if (going.Count == 0)
            return Refuse(player, RequestRefusal.NoHeroDeployed);
        if (going.Count > _content.Scenarios[quest.Scenario].HeroSlots!.Count)
            return Refuse(player, RequestRefusal.TooManyHeroes);
        // Revérifiés : un héros a pu mourir entre son déploiement et le lancement.
        if (going.Select(p => HeroRefusal(p, p.Deployed!)).FirstOrDefault(r => r is not null) is { } refusal)
            return Refuse(player, refusal);
        return going.Select(p => _campaign!.State.Heroes.First(h => h.Id == p.Deployed)).ToList();
    }

    /// <summary>Pourquoi ce joueur ne peut pas déployer ce héros (pas le sien, mort), ou <c>null</c>.</summary>
    private RequestRefusal? HeroRefusal(Player player, string heroId) =>
        _campaign!.State.Heroes.FirstOrDefault(h => h.Id == heroId) switch
        {
            null => RequestRefusal.NotYourHero,
            { } hero when hero.Owner != player.Uid => RequestRefusal.NotYourHero,
            { IsDead: true } => RequestRefusal.HeroUnavailable,
            _ => null,
        };

    private static IReadOnlyList<CampaignHero>? Refuse(Player player, RequestRefusal reason)
    {
        player.Peer!.Send(new RequestRefused(reason));
        return null;
    }

    /// <summary>Une demande du QG n'a de sens qu'avec une campagne, hors trajet et hors combat ; sinon, le joueur sait pourquoi.</summary>
    private bool InCampaignLobby(Player player)
    {
        if (!IsCampaign)
            player.Peer!.Send(new RequestRefused(RequestRefusal.NoCampaign));
        else if (InProgress)
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInLobby));
        return IsCampaign && !InProgress;
    }

    /// <summary>Applique et sauvegarde un changement ; si la sauvegarde échoue, le joueur le sait et l'erreur remonte.</summary>
    private void SaveOrTell(Player player, Func<CampaignState, CampaignState> change)
    {
        try
        {
            _campaign!.Update(change);
        }
        catch
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.CampaignUnavailable));
            throw;
        }
    }

    /// <summary>
    /// La quête terminée laisse à la campagne, en une sauvegarde : son bilan de réputation (s'il y a des règles), l'XP et
    /// les niveaux de ses héros, ses matériaux au pot si elle est gagnée, puis les chutes de ses héros (et leur mort) ;
    /// ses joueurs avancent de sa durée (leurs autres héros se reposent), et elle quitte le panneau de tout le monde. Un
    /// héros mort n'est plus déployé.
    /// </summary>
    private void RecordInCampaign(CombatSimulation simulation)
    {
        if (_campaign is null || _questInCombat is not { } inCombat || simulation.Outcome is not { } victory)
            return;
        var (quest, deployed, party) = inCombat;
        var heroIds = deployed.Select(d => d.Hero).ToList();
        var halfFalls = deployed.ToDictionary(d => d.Hero,
            d => simulation.FindUnit(d.Unit) is { } unit ? Injuries.HalfFallsOf(unit.Falls, unit.RescuedFalls) : 0);
        var report = _content.Reputation is { } rules ? ReputationReport.Of(simulation, rules) : null;
        var progression = _content.Heroes?.Rules;
        var alive = _campaign.State.Heroes.Where(h => !h.IsDead).Select(h => h.Id).ToHashSet();
        _campaign.Update(state =>
        {
            if (report is not null)
                state = state.WithMissionReport(report, _content.Reputation!.Scale);
            state = state with { DoneQuests = state.DoneQuests.Append(quest.Id).ToList() };
            if (progression is null)
                return state;
            state = Injuries.Apply(QuestRewards.Grant(state, quest, heroIds, victory, progression), halfFalls, progression);
            return Calendar.Return(state, party, quest.DurationHours, heroIds, progression);
        });
        if (_questId == quest.Id)
            _questId = null; // jouée : elle n'est plus au panneau
        // Seulement une fois sauvegardé : le QG ne doit pas annoncer des récompenses qui n'ont pas été versées.
        var deaths = _campaign.State.Heroes.Where(h => h.IsDead && alive.Contains(h.Id)).ToList();
        _lastQuest = new QuestResult(quest.Name, victory, report?.Total ?? 0,
            progression is null ? 0 : QuestRewards.XpFor(quest, victory, progression),
            victory ? quest.Materials : new Dictionary<string, int>())
        {
            Deaths = deaths.Select(h => h.Name).ToList(),
        };
        foreach (var player in _players.Where(p => deaths.Any(h => h.Id == p.Deployed)))
            player.Deployed = null;
    }

    /// <summary>
    /// Ce que le joueur <paramref name="viewer"/> voit de la campagne : réputation, dernière quête, héros (les siens
    /// marqués, avec leur XP et leurs points à dépenser), panneau, pot commun.
    /// </summary>
    private CampaignInfo? CampaignSummary(string viewer)
    {
        if (_campaign?.State is not { } state)
            return null;
        var progression = _content.Heroes?.Rules;
        var heroes = state.Heroes
            .Select(h => new HeroInfo(h.Id, h.Name, h.Sheet.Archetype, h.Sheet.Level, state.PlayerOf(h.Owner)?.Name ?? "?",
                Mine: h.Owner == viewer, h.IsDead, h.Xp,
                progression?.XpForNextLevel(h.Sheet.Level),
                progression is null ? 0 : QuestRewards.UnspentStatPoints(h.Sheet, progression),
                progression is null ? 0 : QuestRewards.UnspentSkillPoints(h.Sheet, progression),
                Sheet: h.Owner == viewer ? h.Sheet : null,
                HalfFalls: h.HalfFalls,
                HalfFallsToDeath: progression is null ? 0 : Injuries.HalfFallsToDeath(progression)))
            .ToList();
        var board = Calendar.Board(state, _content.Quests.Values, viewer)
            .Select(q => new QuestInfo(q.Id, q.Name, q.Description, q.Difficulty, q.Xp, q.Materials,
                _content.Scenarios[q.Scenario].HeroSlots!.Count, q.DurationHours))
            .ToList();
        return new CampaignInfo
        {
            Reputation = state.Reputation,
            Hour = state.PlayerOf(viewer)?.Hour ?? 0,
            Tier = _content.Reputation?.Scale.TierOf(state.Reputation).Name ?? "",
            LastQuest = _lastQuest,
            Heroes = heroes,
            Board = board,
            ChosenQuest = _questId,
            Materials = state.Materials,
            KnownPois = state.KnownPois,
            ExhaustedPois = state.ExhaustedPois,
            Trip = TripSummary(),
            LastEvent = _lastEvent,
        };
    }
}

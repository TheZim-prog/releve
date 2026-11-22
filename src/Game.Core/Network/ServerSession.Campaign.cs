using Game.Core.Campaign;
using Game.Core.Combat;
using Game.Core.Reputation;

namespace Game.Core.Network;

// Le QG d'une partie avec campagne : académie, panneau de quêtes, héros déployés, lancement d'une quête, et ce qu'un
// combat terminé laisse à la campagne. Chaque joueur est inscrit dans la campagne à son arrivée (cf. Join).
public sealed partial class ServerSession
{
    private string? _questId;

    /// <summary>La quête en cours et les héros de campagne qui y sont partis, ou <c>null</c>.</summary>
    private (QuestDefinition Quest, IReadOnlyList<(string Hero, UnitId Unit)> Deployed)? _questInCombat;

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
        if (questId is not null && !_content.Quests.ContainsKey(questId))
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.UnknownQuest));
            return;
        }
        _questId = questId;
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
    /// Lance la quête choisie avec les héros déployés, ou explique au joueur qui l'a demandé pourquoi c'est impossible.
    /// Le combat est construit avant que rien ne change : une fiche de héros que les données ne permettent plus (une
    /// compétence renommée depuis son recrutement) est refusée sans laisser de trace.
    /// </summary>
    private void StartQuest(Player player)
    {
        if (QuestLaunch(player) is not { } launch)
            return;
        CombatSetup setup;
        try
        {
            setup = launch.BuildSetup(_content);
        }
        catch (ArgumentException)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.HeroUnavailable));
            return;
        }
        var going = _players.Where(p => p.Deployed is not null).ToList();
        foreach (var assignment in launch.Heroes)
            going.Single(p => p.Id == assignment.Player).Hero = assignment.Unit;
        _questInCombat = (ChosenQuest!, going.Select(p => (p.Deployed!, p.Hero!.Value)).ToList());
        Launch(launch, setup);
    }

    /// <summary>
    /// Le lancement de la quête choisie, avec les héros déployés (dans l'ordre d'arrivée des joueurs, sur les cases du
    /// scénario), ou <c>null</c> (le joueur qui l'a demandé sait pourquoi). Rien n'est encore changé.
    /// </summary>
    private CombatLaunch? QuestLaunch(Player player)
    {
        if (ChosenQuest is not { } quest)
            return Refuse(player, RequestRefusal.NoQuestChosen);
        var going = _players.Where(p => p.Deployed is not null).ToList();
        if (going.Count == 0)
            return Refuse(player, RequestRefusal.NoHeroDeployed);
        var scenario = _content.Scenarios[quest.Scenario];
        if (going.Count > scenario.HeroSlots!.Count)
            return Refuse(player, RequestRefusal.TooManyHeroes);
        // Revérifiés : un héros a pu mourir entre son déploiement et le lancement.
        if (going.Select(p => HeroRefusal(p, p.Deployed!)).FirstOrDefault(r => r is not null) is { } refusal)
            return Refuse(player, refusal);

        var heroes = _campaign!.State.Heroes;
        return new CombatLaunch(quest.Scenario,
            going.Select((p, i) => new HeroAssignment(scenario.DeployedUnitId(i), p.Id)).ToList(),
            going.Select(p => heroes.First(h => h.Id == p.Deployed).Sheet).ToList());
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

    private static CombatLaunch? Refuse(Player player, RequestRefusal reason)
    {
        player.Peer!.Send(new RequestRefused(reason));
        return null;
    }

    /// <summary>Une demande du QG n'a de sens qu'avec une campagne, hors combat ; sinon, le joueur sait pourquoi.</summary>
    private bool InCampaignLobby(Player player)
    {
        if (!IsCampaign)
            player.Peer!.Send(new RequestRefused(RequestRefusal.NoCampaign));
        else if (Simulation is not null)
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInLobby));
        return IsCampaign && Simulation is null;
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
    /// les niveaux de ses héros, ses matériaux au pot si elle est gagnée, puis les chutes de ses héros (et leur mort) et
    /// le repos des autres. Un héros mort n'est plus déployé.
    /// </summary>
    private void RecordInCampaign(CombatSimulation simulation)
    {
        if (_campaign is null || _questInCombat is not { } inCombat || simulation.Outcome is not { } victory)
            return;
        var (quest, deployed) = inCombat;
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
            return progression is null
                ? state
                : Injuries.Apply(QuestRewards.Grant(state, quest, heroIds, victory, progression), halfFalls, progression);
        });
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
        var board = _content.Quests.Values
            .OrderBy(q => q.Difficulty).ThenBy(q => q.Name)
            .Select(q => new QuestInfo(q.Id, q.Name, q.Description, q.Difficulty, q.Xp, q.Materials,
                _content.Scenarios[q.Scenario].HeroSlots!.Count))
            .ToList();
        return new CampaignInfo
        {
            Reputation = state.Reputation,
            Tier = _content.Reputation?.Scale.TierOf(state.Reputation).Name ?? "",
            LastQuest = _lastQuest,
            Heroes = heroes,
            Board = board,
            ChosenQuest = _questId,
            Materials = state.Materials,
        };
    }
}

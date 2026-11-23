using Game.Core.Campaign;
using Game.Core.Combat;
using Game.Core.Reputation;

namespace Game.Core.Network;

// Le QG d'une partie avec campagne : académie, attente, héros déployés, et ce qu'un combat terminé laisse à la campagne.
// Chaque joueur est inscrit dans la campagne à son arrivée (cf. Join). Les groupes sont dans ServerSession.Groups.cs.
public sealed partial class ServerSession
{
    /// <summary>Ce qu'a rapporté la dernière quête de chaque joueur depuis le lancement du serveur, par identifiant.</summary>
    private readonly Dictionary<string, QuestResult> _lastQuests = new();

    /// <summary>Le salon est le QG d'une campagne (sinon, il joue le scénario du serveur).</summary>
    private bool IsCampaign => _campaign is not null;

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

    /// <summary>Le joueur attend une demi-journée au QG : sa date avance, ses héros vivants se reposent.</summary>
    private void WaitFor(Player player)
    {
        if (!InCampaignLobby(player))
            return;
        if (_content.Heroes?.Rules is not { } rules)
        {
            player.Peer!.Send(new RequestRefused(RequestRefusal.NoCampaign));
            return;
        }
        SaveOrTell(player, state => Calendar.PassTime(state, player.Uid, Calendar.HalfDay, Array.Empty<string>(), rules));
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

    /// <summary>Pourquoi ce joueur ne peut pas déployer ce héros (pas le sien, mort), ou <c>null</c>.</summary>
    private RequestRefusal? HeroRefusal(Player player, string heroId) =>
        _campaign!.State.Heroes.FirstOrDefault(h => h.Id == heroId) switch
        {
            null => RequestRefusal.NotYourHero,
            { } hero when hero.Owner != player.Uid => RequestRefusal.NotYourHero,
            { IsDead: true } => RequestRefusal.HeroUnavailable,
            _ => null,
        };

    /// <summary>
    /// Une demande du QG n'a de sens qu'avec une campagne, pour un joueur au QG (pas en route ni en combat avec son
    /// groupe) ; sinon, le joueur sait pourquoi.
    /// </summary>
    private bool InCampaignLobby(Player player)
    {
        if (!IsCampaign)
            player.Peer!.Send(new RequestRefused(RequestRefusal.NoCampaign));
        else if (Busy(player))
            player.Peer!.Send(new RequestRefused(RequestRefusal.NotInLobby));
        return IsCampaign && !Busy(player);
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
    private void RecordInCampaign(QuestFight fight, CombatSimulation simulation)
    {
        if (_campaign is null || simulation.Outcome is not { } victory)
            return;
        var (quest, deployed, party) = fight;
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
        // Seulement une fois sauvegardé : le QG ne doit pas annoncer des récompenses qui n'ont pas été versées.
        var deaths = _campaign.State.Heroes.Where(h => h.IsDead && alive.Contains(h.Id)).ToList();
        var result = new QuestResult(quest.Name, victory, report?.Total ?? 0,
            progression is null ? 0 : QuestRewards.XpFor(quest, victory, progression),
            victory ? quest.Materials : new Dictionary<string, int>())
        {
            Deaths = deaths.Select(h => h.Name).ToList(),
        };
        foreach (var uid in party)
            _lastQuests[uid] = result;
        UndeployTheDead();
    }

    /// <summary>Un héros mort (en route ou au combat) ne part plus : son joueur reste au QG.</summary>
    private void UndeployTheDead()
    {
        var dead = _campaign!.State.Heroes.Where(h => h.IsDead).Select(h => h.Id).ToHashSet();
        foreach (var player in _players.Where(p => p.Deployed is { } hero && dead.Contains(hero)))
            player.Deployed = null;
    }

    /// <summary>
    /// Ce que le joueur <paramref name="viewer"/> voit de la campagne : sa date, la réputation, sa dernière quête, les
    /// héros (les siens marqués, avec leur XP et leurs points à dépenser), son panneau, les groupes et le sien (avec son
    /// trajet), le pot commun.
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
        var board = Calendar.Board(state, _content.Quests.Values, viewer, TakenQuests)
            .Select(q => new QuestInfo(q.Id, q.Name, q.Description, q.Difficulty, q.Xp, q.Materials, SlotsOf(q), q.DurationHours))
            .ToList();
        var mine = PartyOf(viewer);
        return new CampaignInfo
        {
            Reputation = state.Reputation,
            Hour = state.PlayerOf(viewer)?.Hour ?? 0,
            Tier = _content.Reputation?.Scale.TierOf(state.Reputation).Name ?? "",
            LastQuest = _lastQuests.GetValueOrDefault(viewer),
            Heroes = heroes,
            Board = board,
            Groups = GroupInfos(state),
            MyGroup = mine?.Id,
            Materials = state.Materials,
            KnownPois = state.KnownPois,
            ExhaustedPois = state.ExhaustedPois,
            Trip = mine is null ? null : TripSummary(mine),
            LastEvent = _lastEvents.GetValueOrDefault(viewer),
        };
    }
}

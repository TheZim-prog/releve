using Game.Core.Campaign;
using Game.Core.Network;
using static Game.Core.Tests.Network.CampaignSessionTests;
using static Game.Core.Tests.Network.ServerSessionTests;

namespace Game.Core.Tests.Network;

/// <summary>
/// Les groupes du QG (docs/design.md 4.3) : un joueur accepte une quête et en est le chef, d'autres le rejoignent, le
/// chef lance ; plusieurs groupes jouent en même temps, chacun son combat (quêtes « q » et « q2 », trois héros chacune).
/// </summary>
public class GroupSessionTests
{
    private static ServerSession Session(TempCampaign campaign) => campaign.Session(CampaignContent(secondQuest: true));

    private static CampaignInfo Seen(FakeClient client) => client.Last<LobbyState>().Campaign!;

    [Fact]
    public void Players_JoinAGroup_UntilItIsFull_AndTheLeaderLeavingDissolvesIt()
    {
        using var campaign = new TempCampaign();
        var session = Session(campaign);
        var (alice, bob, carol, dave) = (Joined(session, "Alice"), Joined(session, "Bob"), Joined(session, "Carol"), Joined(session, "Dave"));
        alice.Say(new AcceptQuest("q"));

        foreach (var player in new[] { bob, carol, dave })
            player.Say(new JoinGroup(1));

        Assert.Equal(new RequestRefused(RequestRefusal.TooManyHeroes), dave.Received[^1]); // trois cases
        Assert.Equal(new[] { "Alice", "Bob", "Carol" }, Seen(dave).Groups.Single().Members);
        alice.Say(new AcceptQuest("q2"));
        Assert.Equal(new RequestRefused(RequestRefusal.AlreadyInGroup), alice.Received[^1]);
        dave.Say(new JoinGroup(7));
        Assert.Equal(new RequestRefused(RequestRefusal.UnknownGroup), dave.Received[^1]);

        bob.Say(new LeaveGroup());
        Assert.Equal(new[] { "Alice", "Carol" }, Seen(bob).Groups.Single().Members);
        bob.Say(new LeaveGroup());
        Assert.Equal(new RequestRefused(RequestRefusal.NotInGroup), bob.Received[^1]);
        alice.Say(new LeaveGroup());

        Assert.Empty(Seen(carol).Groups); // dissous
        Assert.Null(Seen(carol).MyGroup);
        Assert.Equal(new[] { "q", "q2" }, Seen(carol).Board.Select(q => q.Id).Order()); // la quête revient au panneau
    }

    [Fact]
    public void OnlyTheLeader_Launches_OnceEveryoneHasAHero()
    {
        using var campaign = new TempCampaign();
        var session = Session(campaign);
        var (alice, bob) = (Joined(session, "Alice"), Joined(session, "Bob"));
        RecruitAndDeploy(alice);
        bob.Say(new JoinGroup(1));

        alice.Say(new StartCombatRequest());
        Assert.Equal(new RequestRefused(RequestRefusal.HeroesMissing), alice.Received[^1]); // Bob n'a pas de héros
        bob.Say(new RecruitHero(Rookie));
        bob.Say(new DeployHero(Seen(bob).Heroes.Single(h => h.Mine).Id));
        bob.Say(new StartCombatRequest());
        Assert.Equal(new RequestRefused(RequestRefusal.NotTheLeader), bob.Received[^1]);

        alice.Say(new StartCombatRequest());

        Assert.Equal(2, bob.Last<CombatState>().Launch.Heroes.Count);
        Assert.Equal(GroupPhase.Fighting, Seen(bob).Groups.Single().Phase);
    }

    [Fact]
    public void TwoGroups_PlayAtTheSameTime_EachItsOwnCombat()
    {
        using var campaign = new TempCampaign();
        var session = Session(campaign);
        var (alice, bob) = (Joined(session, "Alice"), Joined(session, "Bob"));
        RecruitAndDeploy(alice, "q");
        RecruitAndDeploy(bob, "q2");
        alice.Say(new StartCombatRequest());
        bob.Say(new StartCombatRequest());
        Assert.Equal(2, session.Combats.Count);

        // Alice met le sien en pause : celui de Bob continue.
        alice.Say(new SetPause(true));
        session.Tick();
        Assert.Equal((0, 1), (session.Combats[0].Step, session.Combats[1].Step));
        Assert.Empty(alice.Received.OfType<TickPlayed>()); // les pas de Bob ne la concernent pas
        Assert.Single(bob.Received.OfType<TickPlayed>());
        alice.Say(new SetPause(false));

        // Un nouveau venu arrive au QG pendant ce temps.
        var carol = Joined(session, "Carol");
        Assert.IsType<Welcome>(carol.Received[0]);

        for (var i = 0; i < 4000 && session.Combats.Count > 0; i++)
            session.Tick();

        Assert.Single(alice.Received.OfType<CombatOver>());
        Assert.Single(bob.Received.OfType<CombatOver>());
        Assert.Empty(carol.Received.OfType<CombatState>());
        var saved = CampaignStore.Load(campaign.File);
        Assert.Equal(new[] { "q", "q2" }, saved.DoneQuests.Order());
        Assert.All(saved.Players.Where(p => p.Name != "Carol"), p => Assert.Equal(24, p.Hour));
        Assert.Empty(Seen(carol).Groups);
    }

    [Fact]
    public void JoiningAGroupAhead_MakesThePlayerJumpToItsDate()
    {
        using var campaign = new TempCampaign();
        var session = Session(campaign);
        var (alice, bob) = (Joined(session, "Alice"), Joined(session, "Bob"));
        bob.Say(new WaitHalfDay());
        bob.Say(new WaitHalfDay()); // Bob est au jour 2
        RecruitAndDeploy(bob);
        RecruitAndJoin(alice, group: 1);

        bob.Say(new StartCombatRequest());

        Assert.All(CampaignStore.Load(campaign.File).Players, p => Assert.Equal(24, p.Hour));
    }

    [Fact]
    public void ALeaderWhoDisconnectsBeforeLeaving_DissolvesTheGroup()
    {
        using var campaign = new TempCampaign();
        var session = Session(campaign);
        var (alice, bob) = (Joined(session, "Alice"), Joined(session, "Bob"));
        alice.Say(new AcceptQuest("q"));
        bob.Say(new JoinGroup(1));

        session.Disconnect(alice);

        Assert.Empty(Seen(bob).Groups);
        Assert.Single(bob.Last<LobbyState>().Players); // Alice, partie du QG, a quitté la partie
    }
}

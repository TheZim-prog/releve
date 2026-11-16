using Game.Core.Combat;
using Game.Core.Grids;
using Game.Core.Network;
using static Game.Core.Tests.ContentTestKit;

namespace Game.Core.Tests;

/// <summary>
/// Les compétences des arbres ajoutées au contenu réel. Elles n'ont pas de code à elles : ces tests vérifient que
/// leurs données font ce que le document de conception décrit (qui elles touchent, quel état elles posent).
/// (Celles qui déplacent quelqu'un : <see cref="MoveContentTests"/>.)
/// </summary>
public class TrialContentTests
{
    [Fact]
    public void TheTrialScenario_PlaysToTheEnd()
    {
        // Personne ne joue : tous les héros d'essai sont confiés à l'IA de base.
        var setup = new CombatLaunch("trial", Array.Empty<HeroAssignment>()).BuildSetup(Content);
        var sim = new CombatSimulation(setup);
        var maxTicks = Rules.TicksPerSecond * 60 * 10;
        while (sim.CurrentTick < maxTicks && sim.Outcome is null)
            sim.Tick();

        Assert.True(sim.Outcome is not null, "The trial combat is still running.");
        Assert.Equal(sim.EventLog, CombatSimulation.Replay(setup, sim.CommandLog, sim.CurrentTick).EventLog);
    }

    [Theory]
    // Un coup sur une cible : au contact ou à distance, avec ou sans état.
    [InlineData("colossus", "hammer-blow", 1, StatusKind.OffBalance)]
    [InlineData("speedster", "harassment", 1, StatusKind.OffBalance)]
    [InlineData("energetic", "melting-point", 1, null)]
    [InlineData("energetic", "discharge", 1, StatusKind.Stunned)]
    [InlineData("energetic", "fire-lance", 6, null)]
    [InlineData("energetic", "ice-shard", 5, StatusKind.OffBalance)]
    [InlineData("telepath", "projectile", 5, null)]
    [InlineData("tech", "designation-shot", 5, StatusKind.Exposed)]
    [InlineData("tech", "suppression-shot", 5, StatusKind.OffBalance)]
    [InlineData("tech", "electroshock", 2, StatusKind.Stunned)]
    // Les deux états ajoutés avec les arbres : immobilisé et désarmé.
    [InlineData("colossus", "arm-lock", 1, StatusKind.Immobilized)]
    [InlineData("colossus", "dismantle", 1, null)] // un simple coup contre ce qui n'est pas un robot
    [InlineData("energetic", "freeze", 5, StatusKind.Immobilized)]
    [InlineData("telepath", "vise", 5, StatusKind.Immobilized)]
    [InlineData("telepath", "wrench", 5, StatusKind.Disarmed)]
    [InlineData("tech", "net", 2, StatusKind.Immobilized)]
    [InlineData("speedster", "pickpocket", 1, StatusKind.Disarmed)]
    public void ATargetedSkill_HitsItsTargetAtItsFullRange_WithItsStatus(string caster, string skillId, int range, StatusKind? status)
    {
        var skill = Content.Skills[skillId];
        var sim = Fight(caster, skillId, (0, 2), ("brute", 1, range, 2));

        var events = Use(sim, skillId, target: 2);

        Assert.Equal(range, skill.Range);
        Assert.InRange(events.OfType<UnitDamaged>().Single().Amount, skill.MinDamage, skill.MaxDamage);
        Assert.Equal(status is null ? Array.Empty<StatusKind>() : new[] { status.Value }, events.OfType<StatusApplied>().Select(e => e.Status));
    }

    [Theory]
    // Zones qui ne touchent que les ennemis : un allié dans la zone n'a rien.
    [InlineData("fighter", "sweep", StatusKind.OffBalance, true)]
    [InlineData("telepath", "torpor", StatusKind.OffBalance, false)]
    [InlineData("telepath", "panic", StatusKind.Confused, false)]
    public void AnEnemiesOnlyZone_SparesTheAlliesInIt(string caster, string skillId, StatusKind status, bool dealsDamage)
    {
        // Le lanceur en (1,2), un allié (unité 2) et un ennemi (unité 3) à son contact, un ennemi (unité 4) loin.
        var sim = Fight(caster, skillId, (1, 2), ("fighter", 0, 2, 2), ("brute", 1, 1, 1), ("brute", 1, 8, 4));
        var onSelf = Content.Skills[skillId].Range == 0;

        var events = Use(sim, skillId, cell: onSelf ? (1, 2) : (1, 1));

        Assert.Equal(new[] { 3 }, Affected(events, status));
        Assert.Equal(dealsDamage ? new[] { 3 } : Array.Empty<int>(), Damaged(events));
    }

    [Fact]
    public void ControlledBlast_IsChargedAndSparesTheAlliesInItsZone()
    {
        var sim = Fight("energetic", "controlled-blast", (0, 2), ("fighter", 0, 4, 2), ("brute", 1, 5, 2));

        var events = Use(sim, "controlled-blast", cell: (5, 2));

        Assert.Contains(events, e => e is ChargeStarted);
        Assert.Equal(new[] { 3 }, Damaged(events));
    }

    [Theory]
    // Zones qui touchent tout le monde (règle par défaut), alliés compris, jamais le lanceur.
    [InlineData("colossus", "quake", 1, 2, StatusKind.Stunned)]        // autour de lui
    [InlineData("energetic", "absolute-zero", 3, 2, StatusKind.Stunned)] // grande zone chargée, centrée à distance
    public void ASquareZone_HitsEveryoneInIt_AlliesIncluded(string caster, string skillId, int centerX, int centerY, StatusKind status)
    {
        var sim = Fight(caster, skillId, (1, 2),
            ("fighter", 0, centerX + 1, centerY), ("brute", 1, centerX, centerY - 1), ("brute", 1, 8, 4));

        var events = Use(sim, skillId, cell: (centerX, centerY));

        Assert.Equal(new[] { 2, 3 }, Affected(events, status)); // l'allié et l'ennemi dans la zone, pas l'ennemi au loin
        Assert.Equal(new[] { 2, 3 }, Damaged(events));
    }

    [Theory]
    [InlineData("fighter", "volley", 3, null)]                     // courte ligne
    [InlineData("energetic", "electric-arc", 6, StatusKind.Stunned)] // longue ligne, court étourdissement
    public void ALine_HitsTheEnemiesItCrosses_UpToItsLength_NotTheAllies(string caster, string skillId, int length, StatusKind? status)
    {
        // Sur la ligne : un allié juste devant, un ennemi au bout de la ligne, un ennemi une case trop loin.
        var sim = Fight(caster, skillId, (0, 2), ("fighter", 0, 1, 2), ("brute", 1, length, 2), ("brute", 1, length + 1, 2));

        var events = Use(sim, skillId, cell: (1, 2));

        Assert.Equal(new[] { 3 }, Damaged(events));
        Assert.Equal(status is null ? Array.Empty<int>() : new[] { 3 }, status is null ? Array.Empty<int>() : Affected(events, status.Value));
    }

    [Fact]
    public void Disarm_LeavesItsTargetWithItsBasicAttackOnly_AndDoesNoDamage()
    {
        var sim = Fight("fighter", "disarm", (0, 2), ("gunner", 1, 1, 2));

        var events = Use(sim, "disarm", target: 2);

        Assert.Equal(new[] { 2 }, Affected(events, StatusKind.Disarmed));
        Assert.Empty(Damaged(events));
        var gunner = sim.FindUnit(new UnitId(2))!;
        Assert.Null(gunner.StatusBlocking(gunner.FindSkill("gunner-shot")!));
        Assert.Equal(RejectionReason.Disarmed, gunner.StatusBlocking(gunner.FindSkill("aimed-burst")!));
    }

    [Fact]
    public void Dismantle_IsAPlainHit_ExceptOnARobot()
    {
        var skill = Content.Skills["dismantle"];
        var colossus = Content.Units["colossus"];
        CombatSimulation Against(bool robot) => new(new CombatSetup(Rules, new Grid(3, 1), new[]
        {
            (colossus with { Skills = colossus.Skills.Append(skill).ToList() }).ToSetup(new UnitId(1), 0, new Cell(0, 0), Rules.GaugeCapacity),
            (Content.Units["brute"] with { Id = "target", IsRobot = robot, Behavior = null }).ToSetup(new UnitId(2), 1, new Cell(1, 0)),
        }, Seed: 1));

        var onAThug = Use(Against(robot: false), "dismantle", target: 2);
        var onARobot = Use(Against(robot: true), "dismantle", target: 2);

        Assert.InRange(onAThug.OfType<UnitDamaged>().Single().Amount, skill.MinDamage, skill.MaxDamage);
        Assert.DoesNotContain(onAThug, e => e is StatusApplied);
        Assert.True(onARobot.OfType<UnitDamaged>().Single().Amount > skill.MaxDamage);
        Assert.Equal(new[] { 2 }, Affected(onARobot, StatusKind.Stunned));
    }

    [Theory]
    [InlineData("stimulation", StatusKind.Hasted)]
    [InlineData("focus", StatusKind.Empowered)]
    public void TheTelepath_StrengthensAnAlly(string skillId, StatusKind status)
    {
        var sim = Fight("telepath", skillId, (0, 2), ("fighter", 0, 4, 2), ("brute", 1, 6, 2));

        var events = Use(sim, skillId, target: 2);

        var applied = events.OfType<StatusApplied>().Single();
        var duration = Content.Skills[skillId].Effects.Single().DurationMs;
        Assert.Equal((new UnitId(2), status, 1 + Ticks(duration)), (applied.Unit, applied.Status, applied.EndTick));
    }

    [Fact]
    public void CollectiveSurge_HastensTheAlliesAround_NotTheSpeedsterNorTheEnemies()
    {
        // Un allié à 2 cases (dans la zone), un allié à 5 cases (hors zone), un ennemi au contact.
        var sim = Fight("speedster", "collective-surge", (2, 2), ("fighter", 0, 4, 2), ("tech", 0, 7, 2), ("brute", 1, 1, 2));

        var events = Use(sim, "collective-surge", cell: (2, 2));

        Assert.Equal(new[] { 2 }, Affected(events, StatusKind.Hasted));
        Assert.Equal(SkillWeight.Heavy, Content.Skills["collective-surge"].Weight);
    }

    [Fact]
    public void DesignationShot_ExposesItsTarget_ForFourSeconds()
    {
        var sim = Fight("tech", "designation-shot", (0, 2), ("brute", 1, 4, 2));

        var exposed = Use(sim, "designation-shot", target: 2).OfType<StatusApplied>().Single();

        Assert.Equal((StatusKind.Exposed, 1 + Ticks(4000)), (exposed.Status, exposed.EndTick));
        Assert.True(sim.FindUnit(new UnitId(2))!.HasStatus(StatusKind.Exposed));
    }

    [Fact]
    public void Electroshock_IsStrongerAndLongerAgainstARobot()
    {
        var skill = Content.Skills["electroshock"];
        var tech = Content.Units["tech"];
        var robot = Content.Units["brute"] with { Id = "robot", IsRobot = true, Behavior = null };
        var sim = new CombatSimulation(new CombatSetup(Rules, new Grid(5, 1), new[]
        {
            (tech with { Skills = tech.Skills.Append(skill).ToList() }).ToSetup(new UnitId(1), 0, new Cell(0, 0), Rules.GaugeCapacity),
            robot.ToSetup(new UnitId(2), 1, new Cell(2, 0)),
        }, Seed: 1));

        var events = Use(sim, "electroshock", target: 2);

        var bonus = skill.VsRobots!;
        Assert.InRange(events.OfType<UnitDamaged>().Single().Amount, skill.MinDamage + bonus.MinDamage, skill.MaxDamage + bonus.MaxDamage);
        Assert.Equal(1 + Ticks(skill.Effects.Single().DurationMs * bonus.StatusDurationPercent / 100), events.OfType<StatusApplied>().Single().EndTick);
    }

    [Fact]
    public void Jamming_TakesGaugeFromItsTarget_AndInterferenceFromTheEnemiesOfASmallZone()
    {
        var jamming = Fight("tech", "jamming", (0, 2), ("brute", 1, 5, 2));
        jamming.FindUnit(new UnitId(2))!.Gauge = Rules.GaugeCapacity;
        var one = Use(jamming, "jamming", target: 2).OfType<GaugeLost>().Single();

        // Un allié (unité 2) et un ennemi (unité 3) dans la zone, un ennemi (unité 4) hors de la zone.
        var interference = Fight("tech", "interference", (0, 2), ("fighter", 0, 4, 2), ("brute", 1, 5, 2), ("brute", 1, 8, 4));
        foreach (var unit in interference.Units)
            unit.Gauge = Rules.GaugeCapacity;
        var several = Use(interference, "interference", cell: (5, 2)).OfType<GaugeLost>().ToList();

        Assert.Equal(Rules.GaugeAt(Content.Skills["jamming"].GaugeLossPercent), one.Amount);
        Assert.Equal(new[] { new UnitId(3) }, several.Select(e => e.Unit));
        Assert.True(several[0].Amount < one.Amount);
        Assert.True(Content.Skills["jamming"].VsRobots!.GaugeLossPercent > 0);
    }

    [Fact]
    public void Offbeat_TakesGaugeAndInterruptsCharges_InsteadOfDisarming()
    {
        var offbeat = Content.Skills["offbeat"];

        Assert.True(offbeat is { InterruptsCharge: true, GaugeLossPercent: > 0, Range: 1 });
        Assert.Empty(offbeat.Effects);
    }

    [Fact]
    public void Provocation_HitsAndTauntsTheEnemiesAround_ForTheirNextActivation()
    {
        // Un allié (unité 2) et un ennemi (unité 3) à 2 cases du colosse, un ennemi (unité 4) trop loin.
        var sim = FightAgainstAi("colossus", "provocation", (2, 2), ("fighter", 0, 4, 2), ("brute", 1, 2, 0), ("brute", 1, 7, 2));

        var events = Use(sim, "provocation", cell: (2, 2));

        Assert.Equal(new[] { 3 }, Damaged(events));
        Assert.Equal(new UnitTaunted(1, new UnitId(3), new UnitId(1), 1), events.OfType<UnitTaunted>().Single());
    }

    [Fact]
    public void Challenge_TauntsOneEnemyFromAfar_ForTwoActivations_WithoutDamage()
    {
        var sim = FightAgainstAi("colossus", "challenge", (0, 2), ("brute", 1, 5, 2));

        var events = Use(sim, "challenge", target: 2);

        Assert.Empty(Damaged(events));
        Assert.Equal(new UnitTaunted(1, new UnitId(2), new UnitId(1), 2), events.OfType<UnitTaunted>().Single());
    }

    [Fact]
    public void SteelSkin_GivesTheColossusHalfHisCurrentHp_AndAegisGivesThemToAnAdjacentAlly()
    {
        var hp = Content.Units["colossus"].MaxHp;
        var onSelf = Use(Fight("colossus", "steel-skin", (0, 2), ("brute", 1, 6, 2)), "steel-skin");
        var onAlly = Use(Fight("colossus", "aegis", (0, 2), ("fighter", 0, 1, 2), ("brute", 1, 6, 2)), "aegis", target: 2);

        Assert.Equal((new UnitId(1), hp / 2), (onSelf.OfType<TempHpGained>().Single().Unit, onSelf.OfType<TempHpGained>().Single().Amount));
        Assert.Equal((new UnitId(2), hp / 2), (onAlly.OfType<TempHpGained>().Single().Unit, onAlly.OfType<TempHpGained>().Single().Amount));
        Assert.Equal(1, Content.Skills["aegis"].Range); // un allié adjacent
        Assert.True(Content.Skills["steel-skin"].TempHp!.DurationMs < Content.Skills["steel-skin"].CooldownMs);
    }

    [Fact]
    public void TheShieldBeacon_GivesTemporaryHpToTheAlliesAroundIt_ThreeTimes()
    {
        var beacon = Content.Skills["shield-beacon"].Summon!;

        Assert.Equal((Behaviors.Beacon, 3, 0), (beacon.Behavior, beacon.MaxActivations, beacon.Mobility));
        Assert.True(beacon.Skills.Single() is { TempHp: not null, Zone.Affects: ZoneTargets.Allies });
    }

    [Fact]
    public void PreciseStrike_IgnoresProtections_AndPincerFlanksInstead()
    {
        Assert.True(Content.Skills["precise-strike"].IgnoresProtections);
        Assert.False(Content.Skills["pincer"].IgnoresProtections);
        Assert.True(Content.Skills["pincer"].FlankDamagePercent > 100);

        // Le colosse a sa Peau d'acier : la Frappe précise entame quand même ses PV.
        var colossus = Content.Units["colossus"];
        var fighter = Content.Units["fighter"];
        var sim = new CombatSimulation(new CombatSetup(Rules, new Grid(3, 1), new[]
        {
            (fighter with { Skills = fighter.Skills.Append(Content.Skills["precise-strike"]).ToList() })
                .ToSetup(new UnitId(1), 0, new Cell(0, 0), Rules.GaugeCapacity),
            (colossus with { Skills = colossus.Skills.Append(Content.Skills["steel-skin"]).ToList() })
                .ToSetup(new UnitId(2), 1, new Cell(1, 0), Rules.GaugeCapacity),
        }, Seed: 1));
        sim.Submit(new ActivateCommand(new UnitId(2), null, new SkillUse("steel-skin", null)));
        sim.Tick();

        var events = Use(sim, "precise-strike", target: 2);

        Assert.Single(events.OfType<UnitDamaged>());
        Assert.DoesNotContain(events, e => e is DamageAbsorbed);
    }

    [Fact]
    public void AVariant_SharesTheSlotOfItsSkill()
    {
        // Un héros qui a acheté le Coup de marteau peut équiper sa variante, le Séisme, à la place, pas les deux.
        var sheet = Content.Heroes!.Sheets["prologue-colossus"];

        Assert.Contains(Content.Heroes.Unit(sheet with { Equipped = new[] { "quake" } }).Definition.Skills, s => s.Id == "quake");
        Assert.Throws<ArgumentException>(() => Content.Heroes.Check(sheet with { Equipped = new[] { "hammer-blow", "quake" } }));
    }
}

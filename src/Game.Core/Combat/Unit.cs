using Game.Core.Grids;

namespace Game.Core.Combat;

public readonly record struct UnitId(int Value)
{
    public override string ToString() => $"#{Value}";
}

/// <summary>Ce que devient une unité à 0 PV.</summary>
public enum DefeatOutcome
{
    /// <summary>À terre : reste sur sa case et l'occupe, jauge gelée à sa valeur (héros, relevable plus tard).</summary>
    Downed,

    /// <summary>Retirée de la grille (ennemi vaincu).</summary>
    Removed,
}

public enum UnitStatus
{
    Active,
    Downed,
    Removed,
}

/// <summary>
/// Une unité au début du combat : son type (<paramref name="Definition"/>, issu des données) et son placement.
/// </summary>
/// <param name="Team">Les unités d'une même équipe sont alliées, les autres ennemies.</param>
/// <param name="InitialGauge">Jauge au début du combat, entre 0 et la capacité.</param>
/// <param name="Points">Points de stats de ce héros (<c>null</c> : aucun, les stats de son type).</param>
public sealed record UnitSetup(UnitId Id, int Team, Cell Position, UnitDefinition Definition, int InitialGauge = 0,
    StatPoints? Points = null)
{
    /// <summary>Chef de son camp : ce que font ses sbires quand il tombe (<c>null</c> : pas un chef).</summary>
    public MinionReaction? MinionsOnFall { get; init; }

    /// <summary>Vilain poursuivi (objectif Poursuite) : il fuit dès le début, vers les sorties de la mission.</summary>
    public bool IsPursued { get; init; }
}

/// <summary>
/// Intention d'une unité IA, fixée avant sa jauge pleine : la compétence prévue (<c>null</c> si aucune n'est
/// disponible : elle se rapprochera seulement) et sa cible. Cachée par défaut côté joueurs.
/// </summary>
public sealed record Intention(string? SkillId, UnitId Target);

/// <summary>
/// Attaque en cours de charge : cible (attaque ciblée) ou cases fixées au sol (zone), et tick de déclenchement.
/// Exactement l'un de <paramref name="Target"/> et <paramref name="Zone"/> est renseigné.
/// <paramref name="DamageFactor"/> : multiplicateur de dégâts du lanceur (puissance, surpuissant), fixé au lancement,
/// en dix-millièmes (10 000 = dégâts normaux).
/// <paramref name="ZoneOrigin"/> : d'où part la zone (centre d'un carré, lanceur pour une ligne), pour la couverture.
/// </summary>
public sealed record PendingCharge(SkillDefinition Skill, UnitId? Target, IReadOnlyList<Cell>? Zone, int ReleaseTick, int DamageFactor,
    Cell? ZoneOrigin = null);

/// <summary>État courant d'une unité. Seule la <see cref="CombatSimulation"/> le modifie.</summary>
public sealed class Unit
{
    private readonly CombatRules _rules;
    private readonly Dictionary<string, int> _cooldownEndTicks = new();
    private readonly SortedDictionary<StatusKind, int> _statusEndTicks = new();
    private readonly SortedDictionary<StatusKind, UnitId> _statusSources = new();

    /// <param name="powerPercent">Puissance imposée (gadget : celle de l'unité qui le pose), sinon celle de ses points.</param>
    internal Unit(UnitSetup setup, CombatRules rules, int? powerPercent = null)
    {
        var definition = setup.Definition;
        var points = setup.Points ?? StatPoints.None;
        Id = setup.Id;
        Team = setup.Team;
        OriginalTeam = setup.Team;
        Position = setup.Position;
        Definition = definition;
        Initiative = rules.InitiativeWith(definition.Initiative, points);
        Mobility = definition.Mobility;
        MaxHp = rules.MaxHpWith(definition.MaxHp, points);
        Hp = MaxHp;
        PowerPercent = powerPercent ?? rules.PowerPercentWith(points);
        Skills = definition.Skills;
        OnDefeat = definition.OnDefeat;
        Gauge = setup.InitialGauge;
        BasicAttack = definition.BasicAttack is null ? null : FindSkill(definition.BasicAttack);
        OpportunityAttacksPerCycle = definition.OpportunityAttacksPerCycle
            + (definition.Passive == PassiveKind.Vigilance ? rules.VigilanceExtraOpportunityAttacks : 0);
        OpportunityAttacksLeft = OpportunityAttacksPerCycle;
        Behavior = definition.Behavior;
        Passive = definition.Passive;
        MinionsOnFall = setup.MinionsOnFall;
        IsPursued = setup.IsPursued;
        IsFleeing = setup.IsPursued;
        _rules = rules;
        StatusEndTicks = _statusEndTicks.AsReadOnly();
    }

    public UnitId Id { get; }

    /// <summary>Camp actuel (change pendant une domination).</summary>
    public int Team { get; internal set; }

    /// <summary>Camp d'origine, retrouvé à la fin d'une domination.</summary>
    public int OriginalTeam { get; }

    /// <summary>Unité qui la domine, ou <c>null</c>.</summary>
    public UnitId? DominatedBy { get; internal set; }
    public Cell Position { get; internal set; }

    /// <summary>Type de l'unité (données) : nom, stats et compétences de départ.</summary>
    public UnitDefinition Definition { get; }

    /// <summary>Points de jauge gagnés par tick, points de stats compris.</summary>
    public int Initiative { get; }
    public int Mobility { get; }

    /// <summary>PV max, points de stats compris.</summary>
    public int MaxHp { get; }

    /// <summary>
    /// Puissance, en % (100 sans point de stat) : multiplie les dégâts et les soins de toutes ses compétences. Un gadget
    /// prend celle de l'unité qui le pose.
    /// </summary>
    public int PowerPercent { get; }

    /// <summary>
    /// Multiplicateur des dégâts qu'elle inflige en ce moment, en dix-millièmes (10 000 = dégâts normaux) : sa
    /// puissance (%) fois le bonus de l'état surpuissant (%), sans arrondi intermédiaire.
    /// </summary>
    public int DamageFactor => PowerPercent * (HasStatus(StatusKind.Empowered) ? _rules.EmpoweredDamagePercent : 100);

    /// <summary>
    /// Fourchette de dégâts de la compétence telle qu'elle la lancerait maintenant (puissance et état surpuissant
    /// compris ; hors vulnérabilité et couverture de la cible), pour l'affichage.
    /// </summary>
    public (int Min, int Max) DamageRange(SkillDefinition skill) =>
        ((int)((long)skill.MinDamage * DamageFactor / 10_000), (int)((long)skill.MaxDamage * DamageFactor / 10_000));

    /// <summary>Soin donné par cette unité : le tirage, multiplié par sa puissance.</summary>
    internal int HealWith(int roll) => (int)((long)roll * PowerPercent / 100);
    public int Hp { get; internal set; }

    /// <summary>PV temporaires : ils encaissent les dégâts avant les PV, et disparaissent à <see cref="TempHpEndTick"/>.</summary>
    public int TempHp { get; internal set; }

    /// <summary>Tick où les PV temporaires restants disparaissent.</summary>
    public int TempHpEndTick { get; internal set; }
    public IReadOnlyList<SkillDefinition> Skills { get; }
    public DefeatOutcome OnDefeat { get; }
    public UnitStatus Status { get; internal set; } = UnitStatus.Active;
    public int Gauge { get; internal set; }

    /// <summary>Attaque utilisée pour les attaques d'opportunité (<c>null</c> : aucune).</summary>
    public SkillDefinition? BasicAttack { get; }

    public int OpportunityAttacksPerCycle { get; }

    /// <summary>Attaques d'opportunité restantes ; rechargées quand l'unité rejoue.</summary>
    public int OpportunityAttacksLeft { get; internal set; }

    /// <summary>Réactions limitées à une par cycle déjà utilisées ; rechargées quand l'unité rejoue.</summary>
    public CycleReactions SpentReactions { get; internal set; }

    /// <summary>Garde en cours (Parade, Déviation, Maître d'armes), ou <c>null</c>.</summary>
    public GuardDefinition? Guard { get; internal set; }

    /// <summary>Tick où la garde tombe, ou <c>null</c> pour une garde qui tient jusqu'à la prochaine action.</summary>
    public int? GuardEndTick { get; internal set; }

    public bool IsGaugeFull => Gauge >= _rules.GaugeCapacity;

    /// <summary>
    /// Peut agir : debout, jauge pleine, pas étourdie, pas en train de charger. Le reste tant qu'elle n'a pas agi.
    /// </summary>
    public bool IsReady => Status == UnitStatus.Active && IsGaugeFull && !HasStatus(StatusKind.Stunned) && Charge is null;

    /// <summary>Attaque en cours de charge, ou <c>null</c>.</summary>
    public PendingCharge? Charge { get; internal set; }

    /// <summary>Comportement d'IA, ou <c>null</c> pour une unité commandée par un joueur.</summary>
    public string? Behavior { get; }

    /// <summary>Lanceur qui a posé cette unité (gadget), ou <c>null</c> pour une unité présente dès le début.</summary>
    public UnitId? SummonedBy { get; internal set; }

    /// <summary>Mise à l'abri à terre par un coéquipier (Sauvetage éclair) : compte pour la règle de campagne des chutes.</summary>
    public bool WasRescued { get; internal set; }

    /// <summary>Combattant debout : ni à terre, ni civil, ni gadget posé par un autre. C'est ce qui compte pour l'issue du combat.</summary>
    public bool IsStandingCombatant => Status == UnitStatus.Active && !IsCivilian && SummonedBy is null;

    /// <summary>Civil (neutre) : voir <see cref="UnitDefinition.IsCivilian"/>.</summary>
    public bool IsCivilian => Definition.IsCivilian;

    /// <summary>Chef de son camp : ce que font ses sbires quand il tombe (<c>null</c> : pas un chef).</summary>
    public MinionReaction? MinionsOnFall { get; }

    /// <summary>Chef déjà tombé une fois : ses sbires ont réagi (une seule fois, même s'il est relevé ensuite).</summary>
    public bool FellAsLeader { get; internal set; }

    /// <summary>
    /// En fuite (sous son seuil de PV, son chef tombé, ou poursuivi dès le début) : ne fait plus que courir vers le bord
    /// de carte (vers les sorties de la mission, s'il est poursuivi), jusqu'à la fin du combat.
    /// </summary>
    public bool IsFleeing { get; internal set; }

    /// <summary>Vilain poursuivi (objectif Poursuite) : il fuit dès le début, vers les sorties de la mission.</summary>
    public bool IsPursued { get; }

    /// <summary>Enfuie : a quitté le combat par le bord de carte (retirée, mais pas vaincue).</summary>
    public bool Escaped { get; internal set; }

    /// <summary>Rendue (son chef tombé) : a quitté le combat (retirée, mais pas vaincue).</summary>
    public bool Surrendered { get; internal set; }

    /// <summary>Activations déjà faites (pour les unités à nombre d'activations limité).</summary>
    public int Activations { get; internal set; }

    /// <summary>Unité qui l'a provoquée : tant que <see cref="TauntActionsLeft"/> n'est pas à 0, son IA ne vise qu'elle.</summary>
    public UnitId? TauntedBy { get; internal set; }

    /// <summary>Activations pendant lesquelles elle reste provoquée.</summary>
    public int TauntActionsLeft { get; internal set; }

    /// <summary>Intention en cours (unités IA), ou <c>null</c>.</summary>
    public Intention? Intention { get; internal set; }

    /// <summary>États en cours, avec le tick où chacun se termine (ordre fixe).</summary>
    public IReadOnlyDictionary<StatusKind, int> StatusEndTicks { get; }

    public bool HasStatus(StatusKind status) => _statusEndTicks.ContainsKey(status);

    /// <summary>Peut se déplacer d'elle-même (pas immobilisée). La déplacer de force reste possible.</summary>
    public bool CanMove => !HasStatus(StatusKind.Immobilized);

    /// <summary>Cases qu'elle peut parcourir d'elle-même en ce moment : sa mobilité, ou 0 si elle est immobilisée.</summary>
    public int Reach => CanMove ? Mobility : 0;

    /// <summary>
    /// Ce qui lui interdit cette compétence en ce moment, à cause de ses états, ou <c>null</c> : désarmée, elle n'a
    /// que son attaque de base ; immobilisée, aucune compétence qui la déplace. Il suffit d'attendre la fin de l'état.
    /// </summary>
    public RejectionReason? StatusBlocking(SkillDefinition skill) =>
        HasStatus(StatusKind.Disarmed) && skill != BasicAttack ? RejectionReason.Disarmed
        : skill.MovesCaster && !CanMove ? RejectionReason.Immobilized
        : null;

    /// <summary>
    /// Reste de la division du remplissage modifié (ralenti, accéléré), exprimé sur <see cref="FillRemainderScale"/>,
    /// reporté au tick suivant pour que la vitesse soit exacte en moyenne, même avec une petite initiative.
    /// </summary>
    internal int FillRemainder { get; set; }

    internal int FillRemainderScale { get; set; } = 1;

    /// <summary>Jauge promise par un passif pendant ce qu'elle est en train de faire, donnée dès qu'elle a fini.</summary>
    internal int PendingGauge { get; set; }

    /// <summary>
    /// Pose (ou prolonge) un état ; <paramref name="source"/> : l'unité dont vient sa durée actuelle, toujours donnée par
    /// la simulation (<c>null</c> seulement pour un état posé à la main par un test : il n'a pas d'auteur).
    /// </summary>
    internal void SetStatus(StatusKind status, int endTick, UnitId? source)
    {
        _statusEndTicks[status] = endTick;
        if (source is { } id)
            _statusSources[status] = id;
        else
            _statusSources.Remove(status);
    }

    internal void ClearStatus(StatusKind status)
    {
        _statusEndTicks.Remove(status);
        _statusSources.Remove(status);
    }

    /// <summary>L'unité qui lui a posé cet état (celle dont vient sa durée actuelle), ou <c>null</c> s'il n'est pas là.</summary>
    public UnitId? StatusSource(StatusKind status) => _statusSources.TryGetValue(status, out var source) ? source : null;

    /// <summary>Ange gardien : ce passif a déjà sauvé un allié dans ce combat (une fois par combat).</summary>
    public bool GuardianAngelUsed { get; internal set; }

    public SkillDefinition? FindSkill(string skillId) => Skills.FirstOrDefault(s => s.Id == skillId);

    /// <summary>Passif équipé, s'il change une règle.</summary>
    public PassiveKind? Passive { get; }

    public bool HasPassive(PassiveKind passive) => Passive == passive;

    /// <summary>
    /// Ce qui l'empêche d'utiliser cette compétence au tick donné (un état, sinon sa recharge), ou <c>null</c>. Dans
    /// tous les cas il suffit d'attendre. Sert à la validation des commandes comme aux décisions de l'IA.
    /// </summary>
    public RejectionReason? UnavailableAt(SkillDefinition skill, int tick) =>
        StatusBlocking(skill) ?? (tick < CooldownEndTick(skill.Id) ? RejectionReason.OnCooldown : null);

    /// <summary>Tick à partir duquel la compétence est de nouveau utilisable (0 si jamais utilisée).</summary>
    public int CooldownEndTick(string skillId) => _cooldownEndTicks.GetValueOrDefault(skillId);

    internal void StartCooldown(string skillId, int endTick) => _cooldownEndTicks[skillId] = endTick;

    /// <summary>Une de ses compétences est-elle en recharge au tick donné ?</summary>
    public bool HasCooldownRunning(int tick) => _cooldownEndTicks.Values.Any(end => tick < end);

    /// <summary>Toutes ses recharges en cours prennent fin (Rechargement).</summary>
    internal void ClearCooldowns() => _cooldownEndTicks.Clear();
}

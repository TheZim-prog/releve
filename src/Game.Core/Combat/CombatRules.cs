namespace Game.Core.Combat;

/// <summary>
/// Paramètres d'équilibrage du combat, chargés depuis <c>data/combat-rules.json</c>.
/// Toutes les valeurs sont des entiers (simulation déterministe).
/// </summary>
public sealed record CombatRules
{
    /// <summary>Fréquence de la simulation. Sert à convertir les durées en ticks.</summary>
    public required int TicksPerSecond { get; init; }

    /// <summary>Valeur d'une jauge pleine.</summary>
    public required int GaugeCapacity { get; init; }

    /// <summary>Pourcentage de la capacité dont repart la jauge après un déplacement seul.</summary>
    public required int MoveOnlyRestartPercent { get; init; }

    /// <summary>Idem après une action légère (avec ou sans déplacement).</summary>
    public required int LightActionRestartPercent { get; init; }

    /// <summary>Idem après une action lourde (avec ou sans déplacement).</summary>
    public required int HeavyActionRestartPercent { get; init; }

    /// <summary>Vitesse de remplissage de la jauge d'une unité déséquilibrée, en % de la normale.</summary>
    public required int OffBalanceFillPercent { get; init; }

    /// <summary>Vitesse de remplissage de la jauge d'une unité accélérée, en % de la normale (Hypervitesse : 300).</summary>
    public required int HastedFillPercent { get; init; }

    /// <summary>Dégâts infligés par une unité surpuissante, en % des dégâts normaux.</summary>
    public required int EmpoweredDamagePercent { get; init; }

    /// <summary>Dégâts subis par une unité vulnérable, en % des dégâts normaux.</summary>
    public required int ExposedDamageTakenPercent { get; init; }

    /// <summary>Durée de la vulnérabilité posée par le passif Point faible, en millisecondes de combat.</summary>
    public required int WeakPointExposedMs { get; init; }

    /// <summary>Durée des états néfastes posés par une unité qui a le passif Emprise, en % de la durée normale.</summary>
    public required int GripDurationPercent { get; init; }

    /// <summary>Durée de l'état désarmé sur un boss, en % de la durée normale (100 = inchangée).</summary>
    public required int DisarmedOnBossDurationPercent { get; init; }

    /// <summary>Attaques d'opportunité en plus par cycle pour une unité qui a le passif Vigilance.</summary>
    public required int VigilanceExtraOpportunityAttacks { get; init; }

    /// <summary>Jauge perdue par un boss, en % de ce que perdrait une autre unité.</summary>
    public required int BossGaugeLossPercent { get; init; }

    /// <summary>Voleur de temps : jauge retirée à la cible par chaque coup qui la blesse, en % de la capacité.</summary>
    public required int TimeThiefGaugeLossPercent { get; init; }

    /// <summary>Sur sa lancée : jauge gagnée en mettant un ennemi hors de combat, en % de la capacité.</summary>
    public required int MomentumGaugeGainPercent { get; init; }

    /// <summary>Récupération d'énergie : jauge gagnée pour chaque ennemi étourdi, en % de la capacité.</summary>
    public required int EnergyRecoveryGaugeGainPercent { get; init; }

    /// <summary>Sillage : jauge donnée à chaque allié au contact après une action, en % de la capacité.</summary>
    public required int SlipstreamGaugeGainPercent { get; init; }

    /// <summary>Portée, en cases, des auras de Commandement et de Champ de brouillage.</summary>
    public required int AuraRange { get; init; }

    /// <summary>Analyse tactique : portée, en cases, à laquelle les intentions des adversaires du tech sont révélées.</summary>
    public required int TacticalAnalysisRange { get; init; }

    /// <summary>Commandement : vitesse de jauge des alliés proches, en % de la normale (au moins 100).</summary>
    public required int CommandFillPercent { get; init; }

    /// <summary>Surcadençage : vitesse de jauge des gadgets du tech qui a ce passif, en % (plus de 100 : plus vite).</summary>
    public required int OverclockingFillPercent { get; init; }

    /// <summary>Double déploiement : nombre de gadgets du même type qu'un tech qui a ce passif peut avoir à la fois.</summary>
    public required int DoubleDeploymentCount { get; init; }

    /// <summary>Champ de brouillage : vitesse de jauge des ennemis proches, en % de la normale (au plus 100).</summary>
    public required int JammingFieldFillPercent { get; init; }

    /// <summary>Rage : bonus de dégâts à 0 PV, en % ; il grandit avec la part de PV perdue.</summary>
    public required int RageMaxDamageBonusPercent { get; init; }

    /// <summary>Opportuniste : dégâts contre une cible étourdie, déséquilibrée ou immobilisée, en % de la normale.</summary>
    public required int OpportunistDamagePercent { get; init; }

    /// <summary>Premier sang : dégâts contre un ennemi aux PV intacts, en % de la normale.</summary>
    public required int FirstBloodDamagePercent { get; init; }

    /// <summary>Seul contre tous : dégâts subis avec au moins deux ennemis au contact, en % de la normale.</summary>
    public required int AloneAgainstAllDamageTakenPercent { get; init; }

    /// <summary>Écran cinétique : dégâts subis d'un coup venu de loin, en % de la normale.</summary>
    public required int KineticScreenDamageTakenPercent { get; init; }

    /// <summary>Embusqué : dégâts de ses coups de loin quand il est collé au décor, en % de la normale.</summary>
    public required int AmbusherDamagePercent { get; init; }

    /// <summary>Démesure : dégâts de ses coups contre un adversaire, en % de la normale.</summary>
    public required int ExcessDamagePercent { get; init; }

    /// <summary>
    /// Démesure : PV que perd un obstacle bas qui couvre une cible de ses coups, en % de ce qu'il absorbe (200 : il
    /// s'use deux fois plus vite ; il n'absorbe pas davantage).
    /// </summary>
    public required int ExcessCoverWearPercent { get; init; }

    /// <summary>Fournaise : dégâts (avant la puissance) infligés aux ennemis à son contact après chaque action.</summary>
    public required int FurnaceMinDamage { get; init; }

    /// <summary>Fournaise : dégâts maximum.</summary>
    public required int FurnaceMaxDamage { get; init; }

    /// <summary>Réaction en chaîne : dégâts (avant la puissance) de l'explosion d'un ennemi mis hors de combat.</summary>
    public required int ChainReactionMinDamage { get; init; }

    /// <summary>Réaction en chaîne : dégâts maximum.</summary>
    public required int ChainReactionMaxDamage { get; init; }

    /// <summary>Impact : dégâts (avant la puissance) d'un ennemi qu'il déplace de force, à l'arrivée.</summary>
    public required int ImpactMinDamage { get; init; }

    /// <summary>Impact : dégâts maximum.</summary>
    public required int ImpactMaxDamage { get; init; }

    /// <summary>Présence : portée, en cases, à laquelle ses alliés récupèrent des PV après chacune de ses actions.</summary>
    public required int PresenceRange { get; init; }

    /// <summary>Présence : PV rendus (avant la puissance) à chaque allié proche.</summary>
    public required int PresenceMinHeal { get; init; }

    /// <summary>Présence : PV rendus maximum.</summary>
    public required int PresenceMaxHeal { get; init; }

    /// <summary>Ange gardien : portée, en cases, à laquelle il retient un allié qui devrait tomber.</summary>
    public required int GuardianAngelRange { get; init; }

    /// <summary>Givre : durée du ralentissement posé par son attaque de base, en millisecondes.</summary>
    public required int FrostDurationMs { get; init; }

    /// <summary>Carapace de givre : durée du ralentissement de celui qui le frappe au contact, en millisecondes.</summary>
    public required int FrostShellDurationMs { get; init; }

    /// <summary>Remplissage de la jauge (en %) à partir duquel une unité IA fixe son intention.</summary>
    public required int IntentionThresholdPercent { get; init; }

    /// <summary>PV d'un allié à terre relevé (action « interagir »), en % de ses PV max (au moins 1 PV).</summary>
    public required int ReviveHpPercent { get; init; }

    /// <summary>Poids de l'action « relever » (reprise de jauge du releveur, comme une compétence).</summary>
    public required SkillWeight ReviveWeight { get; init; }

    /// <summary>Poids d'une interaction avec un objet de mission (désamorcer, fermer un portail).</summary>
    public required SkillWeight OperateWeight { get; init; }

    /// <summary>Part des dégâts qu'absorbe un obstacle bas pour l'unité à couvert derrière lui, en %.</summary>
    public required int CoverDamageReductionPercent { get; init; }

    /// <summary>PV d'un obstacle bas : il encaisse les dégâts qu'il absorbe et devient du sol à 0.</summary>
    public required int LowObstacleHp { get; init; }

    /// <summary>Ce qu'ajoute un point de stat mis dans les PV, en % des PV de base du type d'unité.</summary>
    public required int HpPercentPerStatPoint { get; init; }

    /// <summary>Ce qu'ajoute un point de stat mis dans la puissance, en points de % (la puissance de base vaut 100 %).</summary>
    public required int PowerPercentPerStatPoint { get; init; }

    /// <summary>Ce qu'ajoute un point de stat mis dans l'initiative, en % de l'initiative de base du type d'unité.</summary>
    public required int InitiativePercentPerStatPoint { get; init; }

    public static CombatRules FromJson(string json)
    {
        var rules = DataJson.Deserialize<CombatRules>(json);
        rules.Validate();
        return rules;
    }

    /// <summary>Valeur de jauge correspondant à <paramref name="percent"/> % de la capacité.</summary>
    public int GaugeAt(int percent) => (int)((long)GaugeCapacity * percent / 100);

    /// <summary>PV max d'une unité : ceux de son type, augmentés par ses points de stats (arrondi au plus proche).</summary>
    public int MaxHpWith(int baseMaxHp, StatPoints points) => WithPoints(baseMaxHp, HpPercentPerStatPoint, points.Hp);

    /// <summary>Initiative d'une unité : celle de son type, augmentée par ses points de stats (arrondi au plus proche).</summary>
    public int InitiativeWith(int baseInitiative, StatPoints points) =>
        WithPoints(baseInitiative, InitiativePercentPerStatPoint, points.Initiative);

    /// <summary>Puissance d'une unité, en % : 100 sans point, puis ce qu'ajoutent ses points de stats.</summary>
    public int PowerPercentWith(StatPoints points) => 100 + PowerPercentPerStatPoint * points.Power;

    private static int WithPoints(int baseValue, int percentPerPoint, int points) =>
        (int)(((long)baseValue * (100 + (long)percentPerPoint * points) + 50) / 100);

    /// <summary>Durée en ticks, arrondie au tick supérieur.</summary>
    public int MillisecondsToTicks(int milliseconds) =>
        (int)(((long)milliseconds * TicksPerSecond + 999) / 1000);

    public void Validate()
    {
        if (TicksPerSecond <= 0)
            throw new ArgumentException($"{nameof(TicksPerSecond)} must be positive.");
        if (GaugeCapacity <= 0)
            throw new ArgumentException($"{nameof(GaugeCapacity)} must be positive.");
        ValidatePercent(MoveOnlyRestartPercent, nameof(MoveOnlyRestartPercent));
        ValidatePercent(LightActionRestartPercent, nameof(LightActionRestartPercent));
        ValidatePercent(HeavyActionRestartPercent, nameof(HeavyActionRestartPercent));
        ValidatePercent(OffBalanceFillPercent, nameof(OffBalanceFillPercent), max: 100);
        ValidatePercent(IntentionThresholdPercent, nameof(IntentionThresholdPercent), max: 100);
        ValidatePercent(ReviveHpPercent, nameof(ReviveHpPercent), max: 100, min: 1);
        if (!Enum.IsDefined(ReviveWeight))
            throw new ArgumentException($"{nameof(ReviveWeight)} is unknown.");
        if (!Enum.IsDefined(OperateWeight))
            throw new ArgumentException($"{nameof(OperateWeight)} is unknown.");
        ValidatePercent(CoverDamageReductionPercent, nameof(CoverDamageReductionPercent), max: 100); // 0 : pas de couverture
        if (LowObstacleHp <= 0)
            throw new ArgumentException($"{nameof(LowObstacleHp)} must be positive.");
        // Renforcements : au moins 100 % (sinon ce serait un affaiblissement), au plus x10 pour rester raisonnable.
        if (HastedFillPercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(HastedFillPercent)} must be between 100 and 1000.");
        if (EmpoweredDamagePercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(EmpoweredDamagePercent)} must be between 100 and 1000.");
        if (ExposedDamageTakenPercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(ExposedDamageTakenPercent)} must be between 100 and 1000.");
        if (WeakPointExposedMs <= 0)
            throw new ArgumentException($"{nameof(WeakPointExposedMs)} must be positive.");
        if (GripDurationPercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(GripDurationPercent)} must be between 100 and 1000.");
        ValidatePassives();
        ValidatePercent(BossGaugeLossPercent, nameof(BossGaugeLossPercent), max: 100);
        ValidatePercent(TimeThiefGaugeLossPercent, nameof(TimeThiefGaugeLossPercent), max: 100);
        ValidatePercent(MomentumGaugeGainPercent, nameof(MomentumGaugeGainPercent), max: 100);
        ValidatePercent(EnergyRecoveryGaugeGainPercent, nameof(EnergyRecoveryGaugeGainPercent), max: 100);
        ValidatePercent(SlipstreamGaugeGainPercent, nameof(SlipstreamGaugeGainPercent), max: 100);
        if (AuraRange is < 1 or > 20)
            throw new ArgumentException($"{nameof(AuraRange)} must be between 1 and 20.");
        if (TacticalAnalysisRange is < 1 or > 20)
            throw new ArgumentException($"{nameof(TacticalAnalysisRange)} must be between 1 and 20.");
        if (CommandFillPercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(CommandFillPercent)} must be between 100 and 1000.");
        if (OverclockingFillPercent is < 100 or > 1000)
            throw new ArgumentException($"{nameof(OverclockingFillPercent)} must be between 100 and 1000.");
        if (DoubleDeploymentCount is < 2 or > 5)
            throw new ArgumentException($"{nameof(DoubleDeploymentCount)} must be between 2 and 5.");
        ValidatePercent(JammingFieldFillPercent, nameof(JammingFieldFillPercent), max: 100, min: 1);
        ValidatePercent(DisarmedOnBossDurationPercent, nameof(DisarmedOnBossDurationPercent), max: 100, min: 1);
        if (VigilanceExtraOpportunityAttacks is < 1 or > 10)
            throw new ArgumentException($"{nameof(VigilanceExtraOpportunityAttacks)} must be between 1 and 10.");
        ValidatePercent(HpPercentPerStatPoint, nameof(HpPercentPerStatPoint), max: 100); // 0 : le point n'apporte rien
        ValidatePercent(PowerPercentPerStatPoint, nameof(PowerPercentPerStatPoint), max: 100);
        ValidatePercent(InitiativePercentPerStatPoint, nameof(InitiativePercentPerStatPoint), max: 100);
    }

    /// <summary>Passifs de dégâts : un bonus augmente (au moins 100 %), une réduction réduit (au plus 100 %).</summary>
    private void ValidatePassives()
    {
        ValidatePercent(RageMaxDamageBonusPercent, nameof(RageMaxDamageBonusPercent), max: 1000);
        foreach (var (value, name) in new[]
                 {
                     (OpportunistDamagePercent, nameof(OpportunistDamagePercent)), (FirstBloodDamagePercent, nameof(FirstBloodDamagePercent)),
                     (AmbusherDamagePercent, nameof(AmbusherDamagePercent)), (ExcessDamagePercent, nameof(ExcessDamagePercent)),
                     (ExcessCoverWearPercent, nameof(ExcessCoverWearPercent)),
                 })
            ValidatePercent(value, name, max: 1000, min: 100);
        ValidatePercent(AloneAgainstAllDamageTakenPercent, nameof(AloneAgainstAllDamageTakenPercent), max: 100, min: 1);
        ValidatePercent(KineticScreenDamageTakenPercent, nameof(KineticScreenDamageTakenPercent), max: 100, min: 1);
        if (FurnaceMinDamage < 0 || FurnaceMaxDamage < FurnaceMinDamage || ChainReactionMinDamage < 0 || ChainReactionMaxDamage < ChainReactionMinDamage)
            throw new ArgumentException("Furnace and chain reaction need 0 <= min damage <= max damage.");
        if (FrostDurationMs <= 0 || FrostShellDurationMs <= 0)
            throw new ArgumentException("Frost durations must be positive.");
        if (ImpactMinDamage < 0 || ImpactMaxDamage < ImpactMinDamage || PresenceMinHeal < 0 || PresenceMaxHeal < PresenceMinHeal)
            throw new ArgumentException("Impact and presence need 0 <= min <= max.");
        if (PresenceRange is < 1 or > 20 || GuardianAngelRange is < 1 or > 20)
            throw new ArgumentException("Presence and guardian angel ranges must be between 1 and 20.");
    }

    // Pour les reprises de jauge, 100 % est exclu (max 99 par défaut) : l'unité serait de nouveau
    // prête aussitôt et pourrait agir plusieurs fois dans le même tick.
    private static void ValidatePercent(int value, string name, int max = 99, int min = 0)
    {
        if (value < min || value > max)
            throw new ArgumentException($"{name} must be between {min} and {max}.");
    }
}

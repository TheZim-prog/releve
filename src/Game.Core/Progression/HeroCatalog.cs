using Game.Core.Combat;

namespace Game.Core.Progression;

/// <summary>Un héros prêt pour un combat : son unité (archétype, attaque de base, compétences et passif équipés) et ses points de stats.</summary>
public sealed record HeroUnit(UnitDefinition Definition, StatPoints Points);

/// <summary>
/// Les arbres de compétences et les héros prêts à jouer (data/heroes), avec de quoi transformer une fiche de héros en
/// unité de combat. Tout est vérifié à la construction : un arbre mal formé ou une fiche illégale est refusé.
/// </summary>
public sealed class HeroCatalog
{
    public HeroCatalog(ProgressionRules rules, IReadOnlyDictionary<string, UnitDefinition> units,
        IReadOnlyDictionary<string, SkillDefinition> skills, IEnumerable<SkillTree> trees, IEnumerable<HeroSheet> sheets)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(sheets);
        rules.Validate();
        Rules = rules;
        Units = units;
        Skills = skills;

        var byArchetype = new Dictionary<string, SkillTree>();
        foreach (var tree in trees)
        {
            SkillTreeValidator.Validate(tree, rules, skills);
            if (!units.ContainsKey(tree.Archetype))
                throw new ArgumentException($"Skill tree '{tree.Archetype}': unknown unit.");
            if (!byArchetype.TryAdd(tree.Archetype, tree))
                throw new ArgumentException($"Two skill trees for '{tree.Archetype}'.");
        }
        Trees = byArchetype;

        var byId = new Dictionary<string, HeroSheet>();
        foreach (var sheet in sheets)
        {
            Check(sheet);
            if (!byId.TryAdd(sheet.Id, sheet))
                throw new ArgumentException($"Duplicate hero id '{sheet.Id}'.");
        }
        Sheets = byId;
    }

    public ProgressionRules Rules { get; }

    /// <summary>Le catalogue d'unités avec lequel ce catalogue a été construit (les archétypes des héros en viennent).</summary>
    public IReadOnlyDictionary<string, UnitDefinition> Units { get; }

    /// <summary>La bibliothèque de compétences avec laquelle ce catalogue a été construit.</summary>
    public IReadOnlyDictionary<string, SkillDefinition> Skills { get; }

    /// <summary>Arbres de compétences, par identifiant d'archétype.</summary>
    public IReadOnlyDictionary<string, SkillTree> Trees { get; }

    /// <summary>Héros prêts à jouer, par identifiant.</summary>
    public IReadOnlyDictionary<string, HeroSheet> Sheets { get; }

    /// <summary>
    /// Vérifie une fiche (archétype connu, règles de progression respectées, unité de combat valide) ; lève une
    /// exception sinon.
    /// </summary>
    public void Check(HeroSheet sheet) => Unit(sheet);

    /// <summary>L'unité de combat d'un héros prêt à jouer.</summary>
    public HeroUnit Unit(string heroId) =>
        Sheets.TryGetValue(heroId, out var sheet) ? Build(sheet) : throw new ArgumentException($"Unknown hero '{heroId}'.");

    /// <summary>
    /// L'unité de combat d'une fiche : le type d'unité de son archétype (stats de base, attaque de base), avec les
    /// compétences et le passif équipés.
    /// </summary>
    public HeroUnit Unit(HeroSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (sheet.Archetype is null || !Trees.TryGetValue(sheet.Archetype, out var tree))
            throw new ArgumentException($"Hero '{sheet.Id}': no skill tree for archetype '{sheet.Archetype}'.");
        HeroSheetValidator.Validate(sheet, tree, Rules, Skills);
        return Build(sheet);
    }

    private HeroUnit Build(HeroSheet sheet)
    {
        var archetype = Units[sheet.Archetype];
        var passive = sheet.Passive is { } id && Passives.TryParse(id, out var kind) ? kind : archetype.Passive;
        var definition = archetype with
        {
            Skills = archetype.Skills.Concat(sheet.Equipped.Select(skill => Skills[skill])).ToList(),
            Passive = passive,
        };
        try
        {
            definition.Validate();
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"Hero '{sheet.Id}': {e.Message}", e);
        }
        return new HeroUnit(definition, sheet.Points);
    }
}

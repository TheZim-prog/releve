using Game.Core.Progression;
using Godot;

namespace Game.Client;

/// <summary>
/// Un arbre de compétences en boutons (affichage minimal) : une ligne par palier de chaque branche, une case par
/// compétence ou passif. Chaque case est prise (verte), proposée (cliquable) ou fermée (grisée).
/// </summary>
internal static class SkillTreeView
{
    /// <summary>État d'une case pour cet affichage.</summary>
    public enum Case
    {
        Taken,
        Open,
        Closed,
    }

    /// <summary>Remplit <paramref name="target"/> avec l'arbre ; <paramref name="pressed"/> reçoit la case cliquée.</summary>
    public static void Build(VBoxContainer target, HeroCatalog catalog, SkillTree tree, Func<string, Case> state, Action<string> pressed,
        bool takenClickable)
    {
        foreach (var child in target.GetChildren())
            child.QueueFree();
        foreach (var branch in tree.Branches)
        {
            target.AddChild(Ui.Text(branch.Name, 16));
            foreach (var tier in branch.Tiers)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 6);
                foreach (var id in tier.Actives.Select(a => a.Skill).Concat(tier.Passives.Select(p => p.Id)))
                    row.AddChild(CaseButton(id, CampaignTexts.NodeName(catalog, tree, id), state(id), pressed, takenClickable));
                target.AddChild(row);
            }
        }
    }

    private static Button CaseButton(string id, string label, Case state, Action<string> pressed, bool takenClickable)
    {
        var button = Ui.Button(label, () => pressed(id), 34);
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.Disabled = state == Case.Closed || (state == Case.Taken && !takenClickable);
        if (state == Case.Taken)
            button.Modulate = Ui.Taken;
        return button;
    }
}

using Godot;

namespace Game.Client;

/// <summary>Interface 2D par-dessus le combat : textes seulement, remplis par <see cref="CombatView"/>.</summary>
public partial class Hud : CanvasLayer
{
    private const int MaxLogLines = 8;

    private readonly Queue<string> _log = new();
    private Label _selection = null!;
    private Label _help = null!;
    private Label _logLabel = null!;
    private Label _hint = null!;
    private Label _banner = null!;

    public override void _Ready()
    {
        _selection = Panel(new Vector2(12, 12), new Vector2(420, 0), 17);
        _help = Panel(new Vector2(-12, 12), new Vector2(380, 0), 15, right: true);
        _logLabel = Panel(new Vector2(12, -12), new Vector2(380, 0), 13, bottom: true);
        _hint = Panel(new Vector2(0, -12), new Vector2(420, 0), 18, bottom: true, center: true);
        _banner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorRight = 1, AnchorBottom = 0.25f,
        };
        _banner.AddThemeFontSizeOverride("font_size", 48);
        _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        _banner.AddThemeConstantOverride("outline_size", 10);
        AddChild(_banner);

        SetStatus("");
    }

    private static readonly string HelpText = string.Join('\n',
        "Clic gauche : choisir un héros / une case / une cible",
        "Clic sur le fantôme ou Entrée : déplacement seul",
        "1-9 : compétence   R : relever un allié",
        "Clic droit / Échap : annuler   Tab : héros prêt suivant",
        "Espace : pause   I : intentions ennemies   F5 : recommencer");

    public void SetSelection(string text) => _selection.Text = text;

    public void SetHint(string text, Color color)
    {
        _hint.Text = text;
        _hint.GetParent<Control>().Visible = text.Length > 0;
        _hint.AddThemeColorOverride("font_color", color);
    }

    public void SetBanner(string text, Color color)
    {
        _banner.Text = text;
        _banner.AddThemeColorOverride("font_color", color);
    }

    /// <summary>État du combat (pause, temps, intentions révélées), sous l'aide des commandes.</summary>
    public void SetStatus(string text) => _help.Text = text.Length > 0 ? HelpText + "\n\n" + text : HelpText;

    public void Log(string line)
    {
        _log.Enqueue(line);
        while (_log.Count > MaxLogLines)
            _log.Dequeue();
        _logLabel.Text = string.Join('\n', _log);
    }

    public void ClearLog()
    {
        _log.Clear();
        _logLabel.Text = "";
    }

    /// <summary>Texte sur fond sombre, ancré dans un coin (ou en bas au centre), qui grandit avec son contenu.</summary>
    private Label Panel(Vector2 offset, Vector2 size, int fontSize, bool right = false, bool bottom = false, bool center = false)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.55f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var label = new Label { CustomMinimumSize = size, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        panel.AddChild(label);

        var horizontal = center ? 0.5f : right ? 1f : 0f;
        var vertical = bottom ? 1f : 0f;
        panel.AnchorLeft = panel.AnchorRight = horizontal;
        panel.AnchorTop = panel.AnchorBottom = vertical;
        panel.GrowHorizontal = center ? Control.GrowDirection.Both : right ? Control.GrowDirection.Begin : Control.GrowDirection.End;
        panel.GrowVertical = bottom ? Control.GrowDirection.Begin : Control.GrowDirection.End;
        panel.OffsetLeft = panel.OffsetRight = offset.X;
        panel.OffsetTop = panel.OffsetBottom = offset.Y;
        AddChild(panel);
        return label;
    }
}

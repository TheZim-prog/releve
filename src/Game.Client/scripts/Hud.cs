using Game.Core.Combat;
using Godot;

namespace Game.Client;

/// <summary>
/// Une carte de la barre des héros. <paramref name="SkillReadiness"/> : pour chaque compétence, la part de sa recharge
/// déjà écoulée (1 = prête).
/// </summary>
public sealed record HeroCard(UnitId Id, string Name, float Hp, string HpText, float Gauge, bool Ready, bool Selected, string State,
    IReadOnlyList<float> SkillReadiness);

/// <summary>
/// Un bouton d'action du héros choisi (compétence, relever ou déplacement seul). <paramref name="Cooldown"/> : part de
/// la recharge qu'il reste à attendre (0 = prête).
/// </summary>
public sealed record ActionButton(string Text, bool Enabled, bool Armed, float Cooldown = 0);

/// <summary>
/// Interface 2D par-dessus le combat, remplie par <see cref="CombatView"/> : le héros choisi et ses actions (boutons),
/// la barre des héros, l'aide, le journal, le message de visée et la bannière. Les clics sont renvoyés par événements.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const int MaxLogLines = 8;
    private static readonly string[] CardLooks = ["normal", "hover", "pressed"];

    private static readonly string HelpText = string.Join('\n',
        "Clic : héros, case (fantôme), cible",
        "Entrée ou bouton : déplacement seul (vers le fantôme)",
        "1-9 ou boutons : compétence   R : relever",
        "F1-F6 ou cartes à gauche : choisir un héros   Tab : prêt suivant",
        "Clic droit / Échap : annuler",
        "Espace : pause   Vitesse : les 2 touches à droite du 0, ou pavé - / +",
        "I : intentions   F9 : recommencer   H : masquer l'aide");

    private readonly Queue<string> _log = new();
    private readonly List<(Button Button, ColorRect Veil)> _actionButtons = new();
    private readonly List<CardView> _cards = new();
    private readonly List<UnitId> _cardIds = new();

    private Label _header = null!;
    private HBoxContainer _actions = null!;
    private Label _help = null!;
    private Label _logLabel = null!;
    private Label _hint = null!;
    private Label _banner = null!;
    private VBoxContainer _heroBar = null!;

    private readonly StyleBoxFlat _cardStyle = CardStyle(new Color(0, 0, 0, 0.6f), new Color(0.3f, 0.3f, 0.35f));
    private readonly StyleBoxFlat _cardReady = CardStyle(new Color(0.15f, 0.12f, 0, 0.7f), new Color(1f, 0.85f, 0.2f));
    private readonly StyleBoxFlat _cardSelected = CardStyle(new Color(0.1f, 0.12f, 0.2f, 0.8f), new Color(0.5f, 0.75f, 1f));

    /// <summary>Clic sur le bouton d'action n° i (compétence i, puis relever, puis déplacement seul).</summary>
    public event Action<int>? ActionPressed;

    /// <summary>Clic sur la carte d'un héros.</summary>
    public event Action<UnitId>? HeroPressed;

    public override void _Ready()
    {
        // À gauche : les héros, en colonne. Bande transparente : seules les cartes captent la souris.
        _heroBar = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _heroBar.AddThemeConstantOverride("separation", 6);
        AddChild(Panel(_heroBar, new Vector2(12, 12), Corner.TopLeft, transparent: true));

        // En bas : le héros choisi et ses actions, en rangée.
        var selection = new VBoxContainer();
        _header = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _header.AddThemeFontSizeOverride("font_size", 17);
        _actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _actions.AddThemeConstantOverride("separation", 6);
        selection.AddChild(_header);
        selection.AddChild(_actions);
        AddChild(Panel(selection, new Vector2(0, -12), Corner.BottomCenter));

        _help = TextLabel(14, 0);
        AddChild(Panel(_help, new Vector2(-12, 12), Corner.TopRight));
        _logLabel = TextLabel(13, 260);
        AddChild(Panel(_logLabel, new Vector2(-12, -125), Corner.BottomRight)); // au-dessus de la rangée d'actions

        _hint = TextLabel(17, 0);
        AddChild(Panel(_hint, new Vector2(0, -122), Corner.BottomCenter));

        _banner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorRight = 1, AnchorBottom = 0.25f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _banner.AddThemeFontSizeOverride("font_size", 48);
        _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        _banner.AddThemeConstantOverride("outline_size", 10);
        AddChild(_banner);
        SetStatus("");
    }

    // --- Héros choisi ---

    public void SetSelection(string header, IReadOnlyList<ActionButton> actions)
    {
        _header.Text = header;
        while (_actionButtons.Count < actions.Count)
        {
            var index = _actionButtons.Count;
            var button = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 50) };
            button.AddThemeFontSizeOverride("font_size", 14);
            button.Pressed += () => ActionPressed?.Invoke(index);
            // Voile de recharge : couvre la part de la recharge qui reste, et rétrécit jusqu'à disparaître.
            var veil = new ColorRect
            {
                Color = new Color(0f, 0f, 0f, 0.6f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorBottom = 1,
            };
            button.AddChild(veil);
            _actions.AddChild(button);
            _actionButtons.Add((button, veil));
        }
        for (var i = 0; i < _actionButtons.Count; i++)
        {
            var (button, veil) = _actionButtons[i];
            button.Visible = i < actions.Count;
            if (!button.Visible)
                continue;
            button.Text = actions[i].Text;
            button.Disabled = !actions[i].Enabled;
            button.Modulate = actions[i].Armed ? new Color(1f, 0.85f, 0.4f) : Colors.White;
            veil.AnchorRight = Math.Clamp(actions[i].Cooldown, 0f, 1f);
            veil.Visible = actions[i].Cooldown > 0;
        }
    }

    // --- Barre des héros ---

    public void SetHeroes(IReadOnlyList<HeroCard> heroes)
    {
        while (_cards.Count < heroes.Count)
            AddCard();
        _cardIds.Clear();
        for (var i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            card.Button.Visible = i < heroes.Count;
            if (!card.Button.Visible)
                continue;
            var hero = heroes[i];
            _cardIds.Add(hero.Id);
            card.Name.Text = $"F{i + 1}  {hero.Name}";
            card.Hp.Value = hero.Hp;
            card.HpText.Text = hero.HpText;
            card.Gauge.Value = hero.Gauge;
            card.State.Text = hero.State;
            SetSkillBars(card, hero.SkillReadiness);
            // Le style ne change qu'avec l'état de la carte : le réappliquer à chaque image relancerait la mise en page.
            var style = hero.Selected ? _cardSelected : hero.Ready ? _cardReady : _cardStyle;
            if (card.Button.GetThemeStylebox("normal") != style)
                foreach (var look in CardLooks)
                    card.Button.AddThemeStyleboxOverride(look, style);
        }
    }

    /// <summary>Une petite jauge par compétence : elle se remplit pendant la recharge et devient verte une fois prête.</summary>
    private static void SetSkillBars(CardView card, IReadOnlyList<float> readiness)
    {
        while (card.SkillBars.Count < readiness.Count)
        {
            var bar = Bar(new Color(0.35f, 0.9f, 0.4f));
            bar.CustomMinimumSize = new Vector2(22, 6);
            bar.MouseFilter = Control.MouseFilterEnum.Ignore;
            card.Skills.AddChild(bar);
            card.SkillBars.Add(bar);
        }
        for (var i = 0; i < card.SkillBars.Count; i++)
        {
            var bar = card.SkillBars[i];
            bar.Visible = i < readiness.Count;
            if (!bar.Visible)
                continue;
            bar.Value = readiness[i];
            bar.Modulate = readiness[i] >= 1f ? Colors.White : new Color(0.55f, 0.55f, 0.6f);
        }
    }

    /// <summary>Héros de la carte n° i (pour les touches F1-F6), ou <c>null</c>.</summary>
    public UnitId? HeroAt(int index) => index >= 0 && index < _cardIds.Count ? _cardIds[index] : null;

    private void AddCard()
    {
        var index = _cards.Count;
        var button = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(165, 98) };
        button.Pressed += () =>
        {
            if (HeroAt(index) is { } id)
                HeroPressed?.Invoke(id);
        };
        // La jauge d'action remplit le fond de la carte, de gauche à droite, derrière le contenu.
        var gauge = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Step = 0, ShowPercentage = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        gauge.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        gauge.OffsetLeft = gauge.OffsetTop = 2; // à l'intérieur de la bordure
        gauge.OffsetRight = gauge.OffsetBottom = -2;
        gauge.AddThemeStyleboxOverride("background", new StyleBoxEmpty());
        gauge.AddThemeStyleboxOverride("fill", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.45f, 0.85f, 0.55f),
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        button.AddChild(gauge);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        box.OffsetLeft = 8;
        box.OffsetRight = -8;
        box.OffsetTop = 4;
        var name = TextLabel(14, 0);
        var hp = Bar(new Color(0.85f, 0.25f, 0.25f));
        var hpText = TextLabel(12, 0);
        var skills = new HBoxContainer();
        skills.AddThemeConstantOverride("separation", 3);
        var state = TextLabel(12, 0);
        foreach (var child in new Control[] { name, hp, hpText, skills, state })
        {
            child.MouseFilter = Control.MouseFilterEnum.Ignore;
            box.AddChild(child);
        }
        button.AddChild(box);
        _heroBar.AddChild(button);
        _cards.Add(new CardView(button, name, hp, hpText, gauge, skills, state));
    }

    private sealed record CardView(Button Button, Label Name, ProgressBar Hp, Label HpText, ProgressBar Gauge,
        HBoxContainer Skills, Label State)
    {
        public List<ProgressBar> SkillBars { get; } = new();
    }

    // --- Textes ---

    public void SetHint(string text, Color color)
    {
        _hint.Text = text;
        _hint.AddThemeColorOverride("font_color", color);
        _hint.GetParent<Control>().Visible = text.Length > 0;
    }

    public void SetBanner(string text, Color color)
    {
        _banner.Text = text;
        _banner.AddThemeColorOverride("font_color", color);
    }

    /// <summary>Aide des commandes affichée ou repliée (touche H).</summary>
    public bool ShowHelp { get; set; }

    /// <summary>État du combat (pause, vitesse, temps, intentions révélées), sous l'aide des commandes si elle est affichée.</summary>
    public void SetStatus(string text) =>
        _help.Text = (ShowHelp ? HelpText : "H : afficher l'aide") + (text.Length > 0 ? "\n\n" + text : "");

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

    // --- Construction ---

    private enum Corner
    {
        TopLeft,
        TopRight,
        BottomRight,
        BottomCenter,
    }

    /// <summary>Contenu sur fond sombre, ancré dans un coin (ou en bas au centre), qui grandit avec son contenu.</summary>
    private static PanelContainer Panel(Control content, Vector2 offset, Corner corner, bool transparent = false)
    {
        var panel = new PanelContainer { MouseFilter = transparent ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, transparent ? 0f : 0.55f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        panel.AddChild(content);

        var (horizontal, vertical) = corner switch
        {
            Corner.TopLeft => (0f, 0f),
            Corner.TopRight => (1f, 0f),
            Corner.BottomRight => (1f, 1f),
            _ => (0.5f, 1f),
        };
        panel.AnchorLeft = panel.AnchorRight = horizontal;
        panel.AnchorTop = panel.AnchorBottom = vertical;
        panel.GrowHorizontal = horizontal switch
        {
            0f => Control.GrowDirection.End,
            1f => Control.GrowDirection.Begin,
            _ => Control.GrowDirection.Both,
        };
        panel.GrowVertical = vertical == 0f ? Control.GrowDirection.End : Control.GrowDirection.Begin;
        panel.OffsetLeft = panel.OffsetRight = offset.X;
        panel.OffsetTop = panel.OffsetBottom = offset.Y;
        return panel;
    }

    private static Label TextLabel(int fontSize, float width)
    {
        var label = new Label { CustomMinimumSize = new Vector2(width, 0) };
        if (width > 0)
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static ProgressBar Bar(Color fill)
    {
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Step = 0, ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 7),
        };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.1f) });
        return bar;
    }

    private static StyleBoxFlat CardStyle(Color background, Color border) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
    };
}

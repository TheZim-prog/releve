using Game.Core.Combat;
using Godot;

namespace Game.Client;

/// <summary>
/// Une unité à l'écran : capsule colorée, nom et PV, barres de PV et de jauge, états en cours, et son intention si
/// elle est révélée. Elle ne décide rien : elle recopie l'état de l'unité à chaque image.
/// </summary>
public partial class UnitView : Node3D
{
    private const float BarWidth = 0.9f;

    private readonly StandardMaterial3D _bodyMaterial = new();
    private readonly StandardMaterial3D _gaugeMaterial = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    private MeshInstance3D _body = null!;
    private MeshInstance3D _hpFill = null!;
    private MeshInstance3D _gaugeFill = null!;
    private MeshInstance3D _ring = null!;
    private Label3D _title = null!;
    private Label3D _details = null!;
    private Color _color;

    public UnitId Id { get; private set; }

    public void Setup(Unit unit, Color color, bool small)
    {
        Id = unit.Id;
        _color = color;
        _bodyMaterial.AlbedoColor = color;
        var scale = small ? 0.65f : 1f;
        _body = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.3f * scale, Height = 1.3f * scale },
            MaterialOverride = _bodyMaterial,
            Position = new Vector3(0, 0.65f * scale, 0),
        };
        AddChild(_body);

        _ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.38f, OuterRadius = 0.46f },
            MaterialOverride = BoardView.Translucent(new Color(1f, 0.95f, 0.4f, 0.9f)),
            Position = new Vector3(0, 0.03f, 0),
            Visible = false,
        };
        AddChild(_ring);

        AddChild(Bar(new Color(0.1f, 0.1f, 0.1f), 1.78f));
        _hpFill = Bar(new Color(0.85f, 0.2f, 0.2f), 1.78f, fill: true);
        AddChild(_hpFill);
        AddChild(Bar(new Color(0.1f, 0.1f, 0.1f), 1.68f));
        _gaugeFill = Bar(Colors.White, 1.68f, fill: true, material: _gaugeMaterial);
        AddChild(_gaugeFill);

        _title = Text(2.0f, 26);
        _details = Text(2.45f, 24);
        AddChild(_title);
        AddChild(_details);
        Position = BoardView.ToWorld(unit.Position);
    }

    /// <summary>Recopie l'état de l'unité ; la capsule glisse vers sa case (la logique, elle, est instantanée).</summary>
    public void Sync(Unit unit, int gaugeCapacity, bool selected, bool hovered, string? intention, double delta)
    {
        Visible = unit.Status != UnitStatus.Removed;
        if (!Visible)
            return;

        var target = BoardView.ToWorld(unit.Position);
        Position = Position.DistanceTo(target) > 4f ? target : Position.Lerp(target, (float)Math.Min(1, delta * 14));

        var downed = unit.Status == UnitStatus.Downed;
        _body.RotationDegrees = downed ? new Vector3(90, 0, 0) : Vector3.Zero;
        _bodyMaterial.AlbedoColor = downed ? _color.Darkened(0.6f) : _color;
        _ring.Visible = selected;

        SetFill(_hpFill, unit.MaxHp == 0 ? 0 : (float)unit.Hp / unit.MaxHp);
        SetFill(_gaugeFill, gaugeCapacity == 0 ? 0 : Math.Min(1f, (float)unit.Gauge / gaugeCapacity));
        _gaugeMaterial.AlbedoColor = unit.IsReady ? new Color(1f, 0.85f, 0.2f)
            : unit.HasStatus(StatusKind.Stunned) || unit.Charge is not null ? new Color(0.5f, 0.5f, 0.6f)
            : new Color(0.3f, 0.7f, 1f);

        // Les PV chiffrés seulement pour l'unité survolée ou choisie : sinon les noms de voisins se chevauchent.
        _title.Text = selected || hovered ? $"{unit.Definition.Name}  {unit.Hp}/{unit.MaxHp}" : unit.Definition.Name;
        var details = new List<string>();
        if (downed)
            details.Add("À TERRE");
        details.AddRange(unit.StatusEndTicks.Keys.Select(Texts.Status));
        if (unit.Charge is { } charge)
            details.Add($"charge : {charge.Skill.DisplayName}");
        if (unit.IsFleeing)
            details.Add("en fuite");
        if (intention is not null)
            details.Add(intention);
        _details.Text = string.Join(" · ", details);
    }

    private static void SetFill(MeshInstance3D fill, float ratio)
    {
        ratio = Math.Clamp(ratio, 0f, 1f);
        fill.Scale = new Vector3(Math.Max(ratio, 0.001f), 1, 1);
        fill.Position = new Vector3(-BarWidth / 2 * (1 - ratio), fill.Position.Y, fill.Position.Z);
    }

    /// <summary>Barre horizontale (fond, ou remplissage un peu en avant) au-dessus de la tête.</summary>
    private static MeshInstance3D Bar(Color color, float height, bool fill = false, StandardMaterial3D? material = null) => new()
    {
        Mesh = new BoxMesh { Size = new Vector3(BarWidth, 0.07f, fill ? 0.09f : 0.08f) },
        MaterialOverride = material ?? new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        Position = new Vector3(0, height, fill ? 0.005f : 0),
    };

    private static Label3D Text(float height, int size) => new()
    {
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        FontSize = size,
        PixelSize = 0.006f,
        OutlineSize = 8,
        NoDepthTest = true,
        Position = new Vector3(0, height, 0),
    };
}

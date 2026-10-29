using Godot;
using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.GodotApp;

public sealed partial class ApplicationNavigationRail : Control
{
    private const float ExpandedWidth = 224f;
    private const float CollapsedWidth = 62f;

    private static readonly NavigationItem[] Items =
    [
        new(ApplicationSection.Overview, "⌂", "VISÃO GERAL", Key.Key1),
        new(ApplicationSection.Simulation, "▶", "SIMULAÇÃO", Key.Key2),
        new(ApplicationSection.Editor, "◇", "EDITOR 3D", Key.Key3),
        new(ApplicationSection.Diagnostics, "≋", "DIAGNÓSTICO", Key.Key4),
        new(ApplicationSection.Connections, "⇄", "CONEXÕES", Key.Key5),
        new(ApplicationSection.Help, "?", "AJUDA", Key.Key6)
    ];

    private readonly Dictionary<ApplicationSection, Button> _buttons = [];
    private readonly Dictionary<ApplicationSection, Label> _badges = [];
    private PanelContainer _panel = null!;
    private Label _brand = null!;
    private Label _caption = null!;
    private Label _footer = null!;
    private Button _collapseButton = null!;
    private bool _isBuilt;

    public event Action<ApplicationSection>? SectionRequested;

    public event Action<bool>? ExpandedChanged;

    public ApplicationSection ActiveSection { get; private set; } = ApplicationSection.Overview;

    public bool IsExpanded { get; private set; } = true;

    public override void _Ready()
    {
        BuildInterface();
        SetActiveSection(ActiveSection);
        SetExpanded(IsExpanded);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey { Pressed: true, Echo: false } key || !key.AltPressed)
        {
            return;
        }

        var item = Items.FirstOrDefault(item => item.Shortcut == key.Keycode);
        if (item is null)
        {
            return;
        }

        RequestSection(item.Section);
        GetViewport().SetInputAsHandled();
    }

    public void SetActiveSection(ApplicationSection section)
    {
        ActiveSection = section;
        if (!_isBuilt)
        {
            return;
        }

        foreach (var (candidate, button) in _buttons)
        {
            var active = candidate == section;
            button.ButtonPressed = active;
            button.AddThemeStyleboxOverride(
                "normal",
                active
                    ? ButtonStyle(new Color(0.08f, 0.34f, 0.48f, 0.96f), new Color(0.15f, 0.78f, 1f, 0.95f), 2)
                    : ButtonStyle(new Color(0.035f, 0.075f, 0.115f, 0.68f), new Color(0.12f, 0.29f, 0.40f, 0.55f)));
            button.AddThemeColorOverride(
                "font_color",
                active ? new Color(0.82f, 0.96f, 1f) : new Color(0.58f, 0.72f, 0.82f));
        }
    }

    public void SetExpanded(bool expanded)
    {
        IsExpanded = expanded;
        if (!_isBuilt)
        {
            return;
        }

        _panel.OffsetRight = _panel.OffsetLeft + (expanded ? ExpandedWidth : CollapsedWidth);
        _brand.Visible = expanded;
        _caption.Visible = expanded;
        _footer.Visible = expanded;
        _collapseButton.Text = expanded ? "‹" : "›";
        _collapseButton.TooltipText = expanded ? "Recolher menu" : "Expandir menu";

        foreach (var item in Items)
        {
            var button = _buttons[item.Section];
            button.Text = expanded ? $"{item.Icon}   {item.Label}" : item.Icon;
            button.Alignment = expanded
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center;
            var badge = _badges[item.Section];
            badge.Visible = expanded && !string.IsNullOrWhiteSpace(badge.Text);
        }

        ExpandedChanged?.Invoke(expanded);
    }

    public void SetBadge(ApplicationSection section, string? text, Color? color = null)
    {
        if (!_isBuilt || !_badges.TryGetValue(section, out var badge))
        {
            return;
        }

        badge.Text = text?.Trim() ?? string.Empty;
        badge.Visible = IsExpanded && !string.IsNullOrWhiteSpace(badge.Text);
        badge.AddThemeColorOverride("font_color", color ?? new Color(0.30f, 0.92f, 0.68f));
    }

    public void SetSectionEnabled(ApplicationSection section, bool enabled)
    {
        if (_isBuilt && _buttons.TryGetValue(section, out var button))
        {
            button.Disabled = !enabled;
        }
    }

    private void BuildInterface()
    {
        if (_isBuilt)
        {
            return;
        }

        Name = "NavegacaoPrincipal";
        MouseFilter = MouseFilterEnum.Pass;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _panel = new PanelContainer
        {
            Name = "BarraLateral",
            AnchorBottom = 1f,
            OffsetLeft = 18f,
            OffsetTop = 112f,
            OffsetRight = 18f + ExpandedWidth,
            OffsetBottom = -84f,
            MouseFilter = MouseFilterEnum.Stop
        };
        _panel.AddThemeStyleboxOverride(
            "panel",
            PanelStyle(
                new Color(0.018f, 0.043f, 0.073f, 0.97f),
                new Color(0.08f, 0.44f, 0.64f, 0.78f)));
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 9);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 9);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        column.AddChild(header);

        var identity = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(identity);
        _brand = new Label { Text = "NAVEGAÇÃO" };
        _brand.AddThemeFontSizeOverride("font_size", 13);
        _brand.AddThemeColorOverride("font_color", new Color(0.72f, 0.90f, 1f));
        identity.AddChild(_brand);
        _caption = new Label { Text = "Alt + 1…6" };
        _caption.AddThemeFontSizeOverride("font_size", 10);
        _caption.AddThemeColorOverride("font_color", new Color(0.35f, 0.58f, 0.72f));
        identity.AddChild(_caption);

        _collapseButton = CreateHeaderButton("‹", "Recolher menu");
        _collapseButton.Pressed += () => SetExpanded(!IsExpanded);
        header.AddChild(_collapseButton);

        column.AddChild(CreateSeparator());

        foreach (var item in Items)
        {
            var row = new Control { CustomMinimumSize = new Vector2(0f, 45f) };
            column.AddChild(row);

            var button = CreateNavigationButton(item);
            button.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            button.Pressed += () => RequestSection(item.Section);
            row.AddChild(button);
            _buttons.Add(item.Section, button);

            var badge = new Label
            {
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                AnchorLeft = 1f,
                AnchorRight = 1f,
                AnchorTop = 0.5f,
                AnchorBottom = 0.5f,
                OffsetLeft = -58f,
                OffsetRight = -10f,
                OffsetTop = -12f,
                OffsetBottom = 12f
            };
            badge.AddThemeFontSizeOverride("font_size", 10);
            row.AddChild(badge);
            _badges.Add(item.Section, badge);
        }

        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(spacer);
        column.AddChild(CreateSeparator());

        _footer = new Label
        {
            Text = "AMBIENTE LOCAL\nCLEAN-ROOM",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _footer.AddThemeFontSizeOverride("font_size", 10);
        _footer.AddThemeColorOverride("font_color", new Color(0.28f, 0.74f, 0.62f));
        column.AddChild(_footer);

        _isBuilt = true;
    }

    private Button CreateNavigationButton(NavigationItem item)
    {
        var button = new Button
        {
            Text = $"{item.Icon}   {item.Label}",
            TooltipText = $"{item.Label}  ·  Alt+{(int)item.Shortcut - (int)Key.Key0}",
            ToggleMode = true,
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(0f, 45f),
            Alignment = HorizontalAlignment.Left
        };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.AddThemeConstantOverride("outline_size", 0);
        button.AddThemeStyleboxOverride(
            "normal",
            ButtonStyle(new Color(0.035f, 0.075f, 0.115f, 0.68f), new Color(0.12f, 0.29f, 0.40f, 0.55f)));
        button.AddThemeStyleboxOverride(
            "hover",
            ButtonStyle(new Color(0.06f, 0.20f, 0.29f, 0.94f), new Color(0.14f, 0.62f, 0.82f, 0.80f)));
        button.AddThemeStyleboxOverride(
            "pressed",
            ButtonStyle(new Color(0.08f, 0.34f, 0.48f, 0.96f), new Color(0.15f, 0.78f, 1f, 0.95f), 2));
        button.AddThemeStyleboxOverride(
            "focus",
            ButtonStyle(new Color(0f, 0f, 0f, 0f), new Color(0.32f, 0.90f, 1f, 0.95f), 2));
        button.AddThemeColorOverride("font_hover_color", new Color(0.86f, 0.97f, 1f));
        button.AddThemeColorOverride("font_pressed_color", new Color(0.86f, 0.97f, 1f));
        return button;
    }

    private static Button CreateHeaderButton(string text, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(34f, 34f),
            FocusMode = FocusModeEnum.All
        };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.AddThemeStyleboxOverride(
            "normal",
            ButtonStyle(new Color(0.05f, 0.13f, 0.19f, 0.90f), new Color(0.12f, 0.38f, 0.52f, 0.75f)));
        button.AddThemeStyleboxOverride(
            "hover",
            ButtonStyle(new Color(0.07f, 0.28f, 0.39f, 0.95f), new Color(0.18f, 0.74f, 0.94f, 0.90f)));
        button.AddThemeStyleboxOverride(
            "focus",
            ButtonStyle(new Color(0f, 0f, 0f, 0f), new Color(0.32f, 0.90f, 1f, 0.95f), 2));
        return button;
    }

    private void RequestSection(ApplicationSection section)
    {
        SetActiveSection(section);
        SectionRequested?.Invoke(section);
    }

    private static HSeparator CreateSeparator()
    {
        var separator = new HSeparator();
        separator.AddThemeConstantOverride("separation", 4);
        separator.AddThemeStyleboxOverride(
            "separator",
            new StyleBoxLine { Color = new Color(0.13f, 0.36f, 0.49f, 0.62f), Thickness = 1 });
        return separator;
    }

    private static StyleBoxFlat PanelStyle(Color background, Color border) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 12,
        CornerRadiusTopRight = 12,
        CornerRadiusBottomRight = 12,
        CornerRadiusBottomLeft = 12,
        ShadowColor = new Color(0f, 0f, 0f, 0.42f),
        ShadowSize = 8,
        ContentMarginLeft = 2f,
        ContentMarginTop = 2f,
        ContentMarginRight = 2f,
        ContentMarginBottom = 2f
    };

    private static StyleBoxFlat ButtonStyle(Color background, Color border, int borderWidth = 1) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = borderWidth,
        BorderWidthTop = borderWidth,
        BorderWidthRight = borderWidth,
        BorderWidthBottom = borderWidth,
        CornerRadiusTopLeft = 7,
        CornerRadiusTopRight = 7,
        CornerRadiusBottomRight = 7,
        CornerRadiusBottomLeft = 7,
        ContentMarginLeft = 13f,
        ContentMarginRight = 10f,
        ContentMarginTop = 7f,
        ContentMarginBottom = 7f
    };

    private sealed record NavigationItem(
        ApplicationSection Section,
        string Icon,
        string Label,
        Key Shortcut);
}

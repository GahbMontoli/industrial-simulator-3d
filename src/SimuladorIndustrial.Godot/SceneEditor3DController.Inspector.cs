using System.Globalization;
using Godot;
using SimuladorIndustrial.Core.Editing;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.GodotApp;

public sealed partial class SceneEditor3DController
{
    private SpinBox _positionX = null!;
    private SpinBox _positionY = null!;
    private SpinBox _positionZ = null!;
    private SpinBox _rotationX = null!;
    private SpinBox _rotationY = null!;
    private SpinBox _rotationZ = null!;
    private OptionButton _tagPicker = null!;
    private LineEdit _tagName = null!;
    private LineEdit _tagCanonicalName = null!;
    private SpinBox _tagAddress = null!;
    private Label _runtimeTagState = null!;
    private LineEdit _forceValue = null!;
    private OptionButton _faultPicker = null!;
    private TagRegistry? _runtimeTags;
    private Func<long>? _tickProvider;
    private bool _refreshingInspectorEditors;
    private bool _tagDefinitionsDirty;
    private double _runtimeRefreshSeconds;

    /// <summary>
    /// Connects diagnostic force/fault controls to the live runtime. The editor remains
    /// fully usable without this binding; only its Runtime tab is disabled.
    /// </summary>
    public void BindRuntimeTags(TagRegistry registry, Func<long> tickProvider)
    {
        _runtimeTags = registry ?? throw new ArgumentNullException(nameof(registry));
        _tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        if (IsNodeReady())
        {
            RefreshRuntimeTagState();
        }
    }

    public override void _Process(double delta)
    {
        if (!_active || _runtimeTags is null)
        {
            return;
        }

        _runtimeRefreshSeconds += delta;
        if (_runtimeRefreshSeconds >= 0.25d)
        {
            _runtimeRefreshSeconds = 0d;
            RefreshRuntimeTagState();
        }
    }

    private TabContainer BuildInspectorTabs()
    {
        var tabs = new TabContainer
        {
            CustomMinimumSize = new Vector2(0f, 190f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        tabs.AddChild(BuildTransformInspector());
        tabs.AddChild(BuildTagDefinitionInspector());
        tabs.AddChild(BuildRuntimeTagInspector());
        return tabs;
    }

    private Control BuildTransformInspector()
    {
        var margin = CreateTabMargin("TRANSFORM");
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 5);
        margin.AddChild(content);

        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 5);
        grid.AddThemeConstantOverride("v_separation", 5);
        content.AddChild(grid);

        _positionX = AddNumberField(grid, "X", TranslationSnapMeters);
        _positionY = AddNumberField(grid, "Y", TranslationSnapMeters);
        _positionZ = AddNumberField(grid, "Z", TranslationSnapMeters);
        _rotationX = AddNumberField(grid, "RX", RotationSnapDegrees, -3600d, 3600d);
        _rotationY = AddNumberField(grid, "RY", RotationSnapDegrees, -3600d, 3600d);
        _rotationZ = AddNumberField(grid, "RZ", RotationSnapDegrees, -3600d, 3600d);

        var apply = CreateButton("APLICAR TRANSFORM", new Color(0.16f, 0.40f, 0.58f));
        apply.Pressed += ApplyTransformInspector;
        content.AddChild(apply);
        return margin;
    }

    private Control BuildTagDefinitionInspector()
    {
        var margin = CreateTabMargin("TAGS");
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 5);
        margin.AddChild(content);

        _tagPicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _tagPicker.ItemSelected += _ => RefreshTagDefinitionFields();
        content.AddChild(_tagPicker);

        var names = new GridContainer { Columns = 2 };
        names.AddThemeConstantOverride("h_separation", 6);
        names.AddThemeConstantOverride("v_separation", 4);
        content.AddChild(names);
        _tagName = AddTextField(names, "Nome");
        _tagCanonicalName = AddTextField(names, "Canônico");
        _tagAddress = AddAddressField(names, "Endereço");

        var apply = CreateButton("VALIDAR E APLICAR TAG", new Color(0.16f, 0.40f, 0.58f));
        apply.Pressed += ApplyTagDefinition;
        content.AddChild(apply);
        return margin;
    }

    private Control BuildRuntimeTagInspector()
    {
        var margin = CreateTabMargin("RUNTIME");
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 6);
        margin.AddChild(content);

        _runtimeTagState = new Label
        {
            Text = "Vincule o TagRegistry para diagnóstico em tempo real.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0f, 36f)
        };
        _runtimeTagState.AddThemeFontSizeOverride("font_size", 10);
        _runtimeTagState.AddThemeColorOverride("font_color", new Color(0.62f, 0.79f, 0.90f));
        content.AddChild(_runtimeTagState);

        var forceRow = new HBoxContainer();
        forceRow.AddThemeConstantOverride("separation", 6);
        content.AddChild(forceRow);
        _forceValue = new LineEdit
        {
            PlaceholderText = "bool / int32 / float32",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        forceRow.AddChild(_forceValue);
        AddActionButton(forceRow, "FORÇAR", ForceSelectedTag, new Color(0.58f, 0.34f, 0.08f));
        AddActionButton(forceRow, "SOLTAR", ReleaseSelectedTag);

        var faultRow = new HBoxContainer();
        faultRow.AddThemeConstantOverride("separation", 6);
        content.AddChild(faultRow);
        _faultPicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var fault in Enum.GetValues<TagFault>())
        {
            _faultPicker.AddItem(fault.ToString());
        }
        faultRow.AddChild(_faultPicker);
        AddActionButton(faultRow, "APLICAR FALHA", ApplySelectedTagFault, new Color(0.62f, 0.18f, 0.18f));
        return margin;
    }

    private void ApplyTransformInspector()
    {
        if (_refreshingInspectorEditors)
        {
            return;
        }

        ExecuteSelected(
            component =>
            {
                _editor!.MoveComponent(
                    component.Id,
                    new Vector3Document(_positionX.Value, _positionY.Value, _positionZ.Value));
                _editor.RotateComponent(
                    component.Id,
                    new Vector3Document(_rotationX.Value, _rotationY.Value, _rotationZ.Value));
            },
            "Transform validado e aplicado.");
    }

    private void ApplyTagDefinition()
    {
        if (_editor is null || _tagPicker.Selected < 0)
        {
            SetStatus("Selecione uma tag.", error: true);
            return;
        }

        try
        {
            var current = _editor.Snapshot();
            var tagIndex = Math.Clamp(_tagPicker.Selected, 0, current.Tags.Count - 1);
            var original = current.Tags[tagIndex];
            var replacement = new TagDefinition(
                original.Id,
                _tagName.Text,
                _tagCanonicalName.Text,
                original.DataType,
                original.Direction,
                checked((int)_tagAddress.Value),
                original.Source);
            var tags = current.Tags.ToArray();
            tags[tagIndex] = replacement;
            var candidate = current with { Tags = tags };
            SceneValidator.ValidateAndThrow(candidate);

            var selectedComponentId = _editor.SelectedComponentId;
            _editor = new SceneEditor(candidate);
            if (selectedComponentId is { } id && candidate.Components.Any(component => component.Id == id))
            {
                _editor.SelectComponent(id);
            }

            _tagDefinitionsDirty = true;
            RefreshAll("Tag validada e atualizada; histórico de componentes reiniciado.");
            _tagPicker.Select(tagIndex);
            SceneChanged?.Invoke(candidate);
        }
        catch (Exception exception)
        {
            SetStatus($"Tag rejeitada: {exception.Message}", error: true);
        }
    }

    private void ForceSelectedTag()
    {
        if (!TryGetRuntimeSelection(out var snapshot, out var tick))
        {
            return;
        }

        try
        {
            var value = ParseTagValue(snapshot.Definition.DataType, _forceValue.Text);
            _runtimeTags!.Force(snapshot.Definition.Id, value, tick);
            RefreshRuntimeTagState();
            SetStatus($"Tag {snapshot.Definition.CanonicalName} forçada.");
        }
        catch (Exception exception)
        {
            SetStatus($"Valor de força inválido: {exception.Message}", error: true);
        }
    }

    private void ReleaseSelectedTag()
    {
        if (!TryGetRuntimeSelection(out var snapshot, out var tick))
        {
            return;
        }

        _runtimeTags!.ReleaseForce(snapshot.Definition.Id, tick);
        RefreshRuntimeTagState();
        SetStatus($"Força liberada de {snapshot.Definition.CanonicalName}.");
    }

    private void ApplySelectedTagFault()
    {
        if (!TryGetRuntimeSelection(out var snapshot, out var tick))
        {
            return;
        }

        var fault = Enum.GetValues<TagFault>()[Math.Clamp(_faultPicker.Selected, 0, Enum.GetValues<TagFault>().Length - 1)];
        _runtimeTags!.SetFault(snapshot.Definition.Id, fault, tick);
        RefreshRuntimeTagState();
        SetStatus($"Falha {fault} aplicada a {snapshot.Definition.CanonicalName}.");
    }

    private bool TryGetRuntimeSelection(out TagSnapshot snapshot, out long tick)
    {
        snapshot = null!;
        tick = 0;
        if (_runtimeTags is null || _tickProvider is null)
        {
            SetStatus("Runtime de tags não vinculado.", error: true);
            return false;
        }

        if (!TryGetSelectedTagDefinition(out var definition))
        {
            SetStatus("Selecione uma tag.", error: true);
            return false;
        }

        try
        {
            snapshot = _runtimeTags.Get(definition.Id);
            tick = _tickProvider();
            return true;
        }
        catch (KeyNotFoundException)
        {
            SetStatus("A tag editada não existe no runtime atual; reinicie a cena para aplicá-la.", error: true);
            return false;
        }
    }

    private void RefreshInspectorEditors()
    {
        if (!IsInstanceValid(_positionX) || _editor is null)
        {
            return;
        }

        _refreshingInspectorEditors = true;
        try
        {
            if (_editor.SelectedComponentId is { } selectedId)
            {
                var component = _editor.GetComponent(selectedId);
                _positionX.Value = component.Transform.Position.X;
                _positionY.Value = component.Transform.Position.Y;
                _positionZ.Value = component.Transform.Position.Z;
                _rotationX.Value = component.Transform.RotationDegrees.X;
                _rotationY.Value = component.Transform.RotationDegrees.Y;
                _rotationZ.Value = component.Transform.RotationDegrees.Z;
            }

            RefreshTagPicker();
            RefreshRuntimeTagState();
        }
        finally
        {
            _refreshingInspectorEditors = false;
        }
    }

    private void RefreshTagPicker()
    {
        var selectedTagId = TryGetSelectedTagDefinition(out var previous) ? previous.Id : (Guid?)null;
        _tagPicker.Clear();
        var tags = _editor!.Snapshot().Tags;
        for (var index = 0; index < tags.Count; index++)
        {
            var tag = tags[index];
            _tagPicker.AddItem($"{tag.CanonicalName}  [{AddressPrefix(tag)}{tag.Address:D3}]");
            if (selectedTagId == tag.Id)
            {
                _tagPicker.Select(index);
            }
        }

        if (tags.Count > 0 && _tagPicker.Selected < 0)
        {
            _tagPicker.Select(0);
        }

        RefreshTagDefinitionFields();
    }

    private void RefreshTagDefinitionFields()
    {
        if (!TryGetSelectedTagDefinition(out var tag))
        {
            _tagName.Text = string.Empty;
            _tagCanonicalName.Text = string.Empty;
            _tagAddress.Value = 0;
            return;
        }

        _tagName.Text = tag.Name;
        _tagCanonicalName.Text = tag.CanonicalName;
        _tagAddress.MaxValue = tag.DataType == TagDataType.Bool ? ushort.MaxValue : ushort.MaxValue - 1;
        _tagAddress.Value = tag.Address;
        RefreshRuntimeTagState();
    }

    private void RefreshRuntimeTagState()
    {
        if (!IsInstanceValid(_runtimeTagState))
        {
            return;
        }

        if (_runtimeTags is null || !TryGetSelectedTagDefinition(out var definition))
        {
            _runtimeTagState.Text = "Vincule o TagRegistry para diagnóstico em tempo real.";
            return;
        }

        try
        {
            var snapshot = _runtimeTags.Get(definition.Id);
            _runtimeTagState.Text =
                $"{snapshot.Definition.DataType}  valor={FormatTagValue(snapshot.EffectiveValue)}  " +
                $"qualidade={snapshot.Quality}  força={(snapshot.IsForced ? "SIM" : "NÃO")}  falha={snapshot.Fault}\n" +
                $"tick={snapshot.LastChangedTick}  UTC={snapshot.LastChangedAtUtc:HH:mm:ss.fff}";
            _faultPicker.Select((int)snapshot.Fault);
        }
        catch (KeyNotFoundException)
        {
            _runtimeTagState.Text = "Tag fora do runtime atual; reinicie a cena para aplicar mudanças estruturais.";
        }
    }

    private bool TryGetSelectedTagDefinition(out TagDefinition definition)
    {
        definition = null!;
        if (_editor is null)
        {
            return false;
        }

        var tags = _editor.Snapshot().Tags;
        if (tags.Count == 0 || _tagPicker.Selected < 0 || _tagPicker.Selected >= tags.Count)
        {
            return false;
        }

        definition = tags[_tagPicker.Selected];
        return true;
    }

    private static MarginContainer CreateTabMargin(string name)
    {
        var margin = new MarginContainer { Name = name };
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        return margin;
    }

    private static SpinBox AddNumberField(
        GridContainer grid,
        string label,
        double step,
        double minimum = -10_000d,
        double maximum = 10_000d)
    {
        var name = new Label { Text = label, VerticalAlignment = VerticalAlignment.Center };
        name.AddThemeFontSizeOverride("font_size", 10);
        name.AddThemeColorOverride("font_color", new Color(0.52f, 0.70f, 0.82f));
        grid.AddChild(name);
        var field = new SpinBox
        {
            MinValue = minimum,
            MaxValue = maximum,
            Step = step,
            CustomMinimumSize = new Vector2(82f, 0f),
            AllowGreater = false,
            AllowLesser = false
        };
        grid.AddChild(field);
        return field;
    }

    private static LineEdit AddTextField(GridContainer grid, string label)
    {
        var name = new Label { Text = label, VerticalAlignment = VerticalAlignment.Center };
        name.AddThemeFontSizeOverride("font_size", 10);
        name.AddThemeColorOverride("font_color", new Color(0.52f, 0.70f, 0.82f));
        grid.AddChild(name);
        var field = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddChild(field);
        return field;
    }

    private static SpinBox AddAddressField(GridContainer grid, string label)
    {
        var name = new Label { Text = label, VerticalAlignment = VerticalAlignment.Center };
        name.AddThemeFontSizeOverride("font_size", 10);
        name.AddThemeColorOverride("font_color", new Color(0.52f, 0.70f, 0.82f));
        grid.AddChild(name);
        var field = new SpinBox
        {
            MinValue = 0,
            MaxValue = ushort.MaxValue,
            Step = 1,
            AllowGreater = false,
            AllowLesser = false,
            UpdateOnTextChanged = true
        };
        grid.AddChild(field);
        return field;
    }

    private static TagValue ParseTagValue(TagDataType type, string input)
    {
        var normalized = input.Trim();
        return type switch
        {
            TagDataType.Bool => normalized.ToLowerInvariant() switch
            {
                "1" or "true" or "on" or "sim" => TagValue.FromBool(true),
                "0" or "false" or "off" or "não" or "nao" => TagValue.FromBool(false),
                _ => throw new FormatException("use true/false, on/off, sim/não ou 1/0")
            },
            TagDataType.Int32 when int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) =>
                TagValue.FromInt32(integer),
            TagDataType.Float32 when TryParseFloat(normalized, out var floatingPoint) =>
                TagValue.FromFloat32(floatingPoint),
            TagDataType.Int32 => throw new FormatException("use um número inteiro de 32 bits"),
            TagDataType.Float32 => throw new FormatException("use um número real finito"),
            _ => throw new InvalidOperationException($"Tipo de tag não suportado: {type}.")
        };
    }

    private static bool TryParseFloat(string input, out float value) =>
        float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        float.TryParse(input, NumberStyles.Float, CultureInfo.GetCultureInfo("pt-BR"), out value);

    private static string FormatTagValue(TagValue value) => value.DataType switch
    {
        TagDataType.Bool => value.AsBool() ? "ON" : "OFF",
        TagDataType.Int32 => value.AsInt32().ToString(CultureInfo.InvariantCulture),
        TagDataType.Float32 => value.AsFloat32().ToString("F3", CultureInfo.GetCultureInfo("pt-BR")),
        _ => "?"
    };

    private static string AddressPrefix(TagDefinition tag) => (tag.Direction, tag.DataType) switch
    {
        (TagDirection.PlcInput, TagDataType.Bool) => "I",
        (TagDirection.PlcOutput, TagDataType.Bool) => "Q",
        (TagDirection.PlcInput, _) => "IR",
        (TagDirection.PlcOutput, _) => "HR",
        _ => string.Empty
    };
}

using Godot;
using SimuladorIndustrial.Core.Editing;
using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.GodotApp;

/// <summary>
/// Lightweight authoring layer for scene components. The editor is independent from the
/// running simulation and writes only validated, versioned scene documents under user://.
/// </summary>
public sealed partial class SceneEditor3DController : Node3D
{
    public const float TranslationSnapMeters = 0.25f;
    public const float RotationSnapDegrees = 15f;

    private static readonly (string Type, string Label)[] Catalog =
    [
        ("source.generator.v1", "Gerador de recipientes"),
        ("source.container.v1", "Fonte da cena de envase"),
        ("sink.remover.v1", "Removedor"),
        ("conveyor.straight.v1", "Esteira reta"),
        ("sensor.diffuse.v1", "Sensor difuso"),
        ("actuator.stop-gate.v1", "Bloqueador pneumático"),
        ("process.filling-head.v1", "Cabeçote de envase"),
        ("process.tank.v1", "Tanque"),
        ("actuator.fill-valve.v1", "Válvula de enchimento"),
        ("actuator.discharge-valve.v1", "Válvula de descarga"),
        ("operator.button.v1", "Botão"),
        ("indicator.light.v1", "Luz piloto"),
        ("indicator.display.v1", "Display")
    ];

    private SceneEditor? _editor;
    private Node3D _proxyRoot = null!;
    private CanvasLayer _canvasLayer = null!;
    private PanelContainer _panel = null!;
    private Button _toggleButton = null!;
    private Tree _sceneTree = null!;
    private OptionButton _catalogPicker = null!;
    private Label _selectionLabel = null!;
    private Label _historyLabel = null!;
    private Label _statusLabel = null!;
    private bool _active;
    private bool _rebuildingTree;

    public event Action<SceneDocument>? SceneChanged;

    public event Action<bool>? ActiveChanged;

    public bool Active => _active;

    public string SavePath { get; private set; } = string.Empty;

    public SceneDocument CurrentScene =>
        _editor?.Snapshot() ?? throw new InvalidOperationException("O editor ainda não foi configurado.");

    public void SetToggleButtonVisible(bool visible)
    {
        if (GodotObject.IsInstanceValid(_toggleButton))
        {
            _toggleButton.Visible = visible;
        }
    }

    public void SetPanelLeftOffset(float offset)
    {
        if (GodotObject.IsInstanceValid(_toggleButton))
        {
            _toggleButton.Position = new Vector2(offset, _toggleButton.Position.Y);
        }

        if (GodotObject.IsInstanceValid(_panel))
        {
            _panel.Position = new Vector2(offset, _panel.Position.Y);
        }
    }

    public void SetInterfaceScale(double scale, Vector2 viewportSize)
    {
        var safeScale = (float)Math.Clamp(scale, 0.75d, 1.50d);
        if (GodotObject.IsInstanceValid(_canvasLayer))
        {
            _canvasLayer.Scale = Vector2.One * safeScale;
        }

        if (GodotObject.IsInstanceValid(_panel))
        {
            var logicalHeight = viewportSize.Y / safeScale;
            var availableHeight = Math.Clamp(logicalHeight - _panel.Position.Y - 20f, 240f, 710f);
            _panel.Size = new Vector2(430f, availableHeight);
        }
    }

    public void Configure(SceneDocument scene, string? savePath = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _editor = new SceneEditor(scene);
        _tagDefinitionsDirty = false;
        SavePath = string.IsNullOrWhiteSpace(savePath)
            ? CreateDefaultSavePath(scene.Name)
            : EnsureUserDataPath(savePath);

        if (IsNodeReady())
        {
            RefreshAll("Cena carregada no editor.");
        }
    }

    public override void _Ready()
    {
        Name = "EditorDeCena3D";
        _proxyRoot = new Node3D { Name = "ProxiesEditaveis", Visible = false };
        AddChild(_proxyRoot);
        BuildInterface();

        if (_editor is not null)
        {
            RefreshAll("F2 abre o editor 3D.");
        }
        else
        {
            SetStatus("Configure uma SceneDocument antes de editar.", error: true);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.Keycode == Key.F2)
        {
            SetActive(!_active);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_active || _editor is null)
        {
            return;
        }

        var handled = key.CtrlPressed
            ? HandleControlShortcut(key.Keycode)
            : HandleEditorKey(key.Keycode);
        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetActive(bool active)
    {
        _active = active;
        _panel.Visible = active;
        _proxyRoot.Visible = active;
        _toggleButton.Text = active ? "F2  FECHAR EDITOR" : "F2  EDITOR 3D";
        if (active && _editor is not null && _editor.SelectedComponentId is null)
        {
            SelectFirstComponent();
        }

        RefreshAll(active ? "Modo de edição ativo; a simulação pode ser pausada pelo painel principal." : "Editor fechado.");
        ActiveChanged?.Invoke(active);
    }

    public bool Save()
    {
        if (_editor is null)
        {
            SetStatus("O editor não foi configurado.", error: true);
            return false;
        }

        try
        {
            var scene = _editor.Snapshot();
            var directory = Path.GetDirectoryName(SavePath)
                ?? throw new InvalidOperationException("Diretório de destino inválido.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(SavePath, SceneSerializer.Serialize(scene));
            _editor.MarkSaved();
            _tagDefinitionsDirty = false;
            RefreshAll($"Cena salva em {SavePath}");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus($"Falha ao salvar: {exception.Message}", error: true);
            GD.PushError($"Falha ao salvar cena editada: {exception}");
            return false;
        }
    }

    public bool OpenSavedScene()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                SetStatus("Ainda não existe uma cena salva neste projeto.", error: true);
                return false;
            }

            var scene = SceneSerializer.Deserialize(File.ReadAllText(SavePath));
            _editor = new SceneEditor(scene);
            _tagDefinitionsDirty = false;
            RefreshAll($"Cena aberta de {SavePath}");
            SceneChanged?.Invoke(scene);
            return true;
        }
        catch (Exception exception)
        {
            SetStatus($"Falha ao abrir: {exception.Message}", error: true);
            GD.PushError($"Falha ao abrir cena editada: {exception}");
            return false;
        }
    }

    private void BuildInterface()
    {
        _canvasLayer = new CanvasLayer { Name = "InterfaceEditor", Layer = 20 };
        AddChild(_canvasLayer);

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _canvasLayer.AddChild(root);

        _toggleButton = CreateButton("F2  EDITOR 3D", new Color(0.38f, 0.24f, 0.62f));
        _toggleButton.Position = new Vector2(20f, 112f);
        _toggleButton.CustomMinimumSize = new Vector2(196f, 42f);
        _toggleButton.Pressed += () => SetActive(!_active);
        root.AddChild(_toggleButton);

        _panel = CreatePanel();
        _panel.Position = new Vector2(20f, 164f);
        _panel.Size = new Vector2(430f, 710f);
        _panel.Visible = false;
        root.AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _panel.AddChild(margin);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        margin.AddChild(scroll);

        var content = new VBoxContainer();
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(content);

        var heading = new Label { Text = "EDITOR DE CENA 3D" };
        heading.AddThemeFontSizeOverride("font_size", 18);
        heading.AddThemeColorOverride("font_color", new Color(0.78f, 0.91f, 1f));
        content.AddChild(heading);

        var hint = new Label
        {
            Text = "TAB seleciona  •  WASD move X/Z  •  R/F move Y  •  Q/E gira",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        hint.AddThemeFontSizeOverride("font_size", 10);
        hint.AddThemeColorOverride("font_color", new Color(0.48f, 0.67f, 0.80f));
        content.AddChild(hint);
        content.AddChild(new HSeparator());

        _sceneTree = new Tree
        {
            HideRoot = true,
            Columns = 1,
            CustomMinimumSize = new Vector2(0f, 100f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        _sceneTree.ItemSelected += OnTreeItemSelected;
        content.AddChild(_sceneTree);

        _selectionLabel = new Label
        {
            Text = "Nenhum componente selecionado.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0f, 44f)
        };
        _selectionLabel.AddThemeFontSizeOverride("font_size", 11);
        _selectionLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.85f, 0.94f));
        content.AddChild(_selectionLabel);
        content.AddChild(BuildInspectorTabs());

        var catalogRow = new HBoxContainer();
        catalogRow.AddThemeConstantOverride("separation", 6);
        content.AddChild(catalogRow);
        _catalogPicker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var (_, label) in Catalog)
        {
            _catalogPicker.AddItem(label);
        }
        catalogRow.AddChild(_catalogPicker);
        var addButton = CreateButton("+ ADICIONAR", new Color(0.08f, 0.52f, 0.37f));
        addButton.Pressed += AddCatalogComponent;
        catalogRow.AddChild(addButton);

        content.AddChild(CreateMoveGrid());

        var editActions = new HBoxContainer();
        editActions.AddThemeConstantOverride("separation", 6);
        content.AddChild(editActions);
        AddActionButton(editActions, "↻ -15°", () => RotateSelected(-RotationSnapDegrees));
        AddActionButton(editActions, "+15° ↺", () => RotateSelected(RotationSnapDegrees));
        AddActionButton(editActions, "DUPLICAR", DuplicateSelected);
        AddActionButton(editActions, "EXCLUIR", RemoveSelected, new Color(0.62f, 0.18f, 0.18f));

        var historyActions = new HBoxContainer();
        historyActions.AddThemeConstantOverride("separation", 6);
        content.AddChild(historyActions);
        AddActionButton(historyActions, "DESFAZER", Undo);
        AddActionButton(historyActions, "REFAZER", Redo);
        AddActionButton(historyActions, "ABRIR", () => OpenSavedScene());
        AddActionButton(historyActions, "SALVAR", () => Save(), new Color(0.10f, 0.42f, 0.58f));

        _historyLabel = new Label { Text = "Histórico: 0 / 0" };
        _historyLabel.AddThemeFontSizeOverride("font_size", 10);
        _historyLabel.AddThemeColorOverride("font_color", new Color(0.48f, 0.67f, 0.80f));
        content.AddChild(_historyLabel);

        _statusLabel = new Label
        {
            Text = string.Empty,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0f, 38f)
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 10);
        content.AddChild(_statusLabel);
    }

    private GridContainer CreateMoveGrid()
    {
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        AddActionButton(grid, "X -", () => MoveSelected(-TranslationSnapMeters, 0f, 0f));
        AddActionButton(grid, "Y +", () => MoveSelected(0f, TranslationSnapMeters, 0f));
        AddActionButton(grid, "X +", () => MoveSelected(TranslationSnapMeters, 0f, 0f));
        AddActionButton(grid, "Z -", () => MoveSelected(0f, 0f, -TranslationSnapMeters));
        AddActionButton(grid, "Y -", () => MoveSelected(0f, -TranslationSnapMeters, 0f));
        AddActionButton(grid, "Z +", () => MoveSelected(0f, 0f, TranslationSnapMeters));
        return grid;
    }

    private void AddCatalogComponent()
    {
        ExecuteMutation(
            () =>
            {
                var index = Math.Clamp(_catalogPicker.Selected, 0, Catalog.Length - 1);
                var componentId = _editor!.AddComponent(
                    Catalog[index].Type,
                    new TransformDocument(
                        new Vector3Document(0, 0, 0),
                        new Vector3Document(0, 0, 0)));
                _editor.SelectComponent(componentId);
            },
            "Componente adicionado.");
    }

    private void MoveSelected(float x, float y, float z)
    {
        ExecuteSelected(
            component =>
            {
                var position = component.Transform.Position;
                _editor!.MoveComponent(
                    component.Id,
                    new Vector3Document(position.X + x, position.Y + y, position.Z + z));
            },
            $"Movimento aplicado com snap de {TranslationSnapMeters:F2} m.");
    }

    private void RotateSelected(float yDegrees)
    {
        ExecuteSelected(
            component =>
            {
                var rotation = component.Transform.RotationDegrees;
                _editor!.RotateComponent(
                    component.Id,
                    new Vector3Document(rotation.X, NormalizeDegrees(rotation.Y + yDegrees), rotation.Z));
            },
            $"Rotação aplicada com snap de {RotationSnapDegrees:F0}°.");
    }

    private void DuplicateSelected()
    {
        ExecuteSelected(
            component =>
            {
                var duplicateId = _editor!.DuplicateComponent(
                    component.Id,
                    positionOffset: new Vector3Document(TranslationSnapMeters, 0, TranslationSnapMeters));
                _editor.SelectComponent(duplicateId);
            },
            "Componente duplicado.");
    }

    private void RemoveSelected()
    {
        ExecuteSelected(
            component => _editor!.RemoveComponent(component.Id),
            "Componente removido.");
    }

    private void Undo()
    {
        if (_editor?.Undo() == true)
        {
            RefreshAll("Última alteração desfeita.");
        }
    }

    private void Redo()
    {
        if (_editor?.Redo() == true)
        {
            RefreshAll("Alteração refeita.");
        }
    }

    private void ExecuteSelected(Action<ComponentDocument> action, string successMessage)
    {
        if (_editor?.SelectedComponentId is not { } selectedId)
        {
            SetStatus("Selecione um componente na árvore.", error: true);
            return;
        }

        ExecuteMutation(() => action(_editor.GetComponent(selectedId)), successMessage);
    }

    private void ExecuteMutation(Action mutation, string successMessage)
    {
        try
        {
            mutation();
            RefreshAll(successMessage);
            SceneChanged?.Invoke(_editor!.Snapshot());
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, error: true);
        }
    }

    private bool HandleControlShortcut(Key key)
    {
        switch (key)
        {
            case Key.D:
                DuplicateSelected();
                return true;
            case Key.Z:
                Undo();
                return true;
            case Key.Y:
                Redo();
                return true;
            case Key.S:
                Save();
                return true;
            default:
                return false;
        }
    }

    private bool HandleEditorKey(Key key)
    {
        switch (key)
        {
            case Key.Tab:
                SelectNextComponent();
                return true;
            case Key.W:
                MoveSelected(0f, 0f, -TranslationSnapMeters);
                return true;
            case Key.S:
                MoveSelected(0f, 0f, TranslationSnapMeters);
                return true;
            case Key.A:
                MoveSelected(-TranslationSnapMeters, 0f, 0f);
                return true;
            case Key.D:
                MoveSelected(TranslationSnapMeters, 0f, 0f);
                return true;
            case Key.R:
                MoveSelected(0f, TranslationSnapMeters, 0f);
                return true;
            case Key.F:
                MoveSelected(0f, -TranslationSnapMeters, 0f);
                return true;
            case Key.Q:
                RotateSelected(-RotationSnapDegrees);
                return true;
            case Key.E:
                RotateSelected(RotationSnapDegrees);
                return true;
            case Key.Delete:
                RemoveSelected();
                return true;
            default:
                return false;
        }
    }

    private void SelectFirstComponent()
    {
        var first = _editor?.Components.FirstOrDefault();
        if (first is not null)
        {
            _editor!.SelectComponent(first.Id);
        }
    }

    private void SelectNextComponent()
    {
        if (_editor is null || _editor.Components.Count == 0)
        {
            return;
        }

        var components = _editor.Components;
        var currentIndex = _editor.SelectedComponentId is { } id
            ? components.ToList().FindIndex(component => component.Id == id)
            : -1;
        var next = components[(currentIndex + 1) % components.Count];
        _editor.SelectComponent(next.Id);
        RefreshAll("Componente selecionado.");
    }

    private void OnTreeItemSelected()
    {
        if (_rebuildingTree || _editor is null)
        {
            return;
        }

        var item = _sceneTree.GetSelected();
        var idText = item?.GetMetadata(0).AsString();
        if (Guid.TryParse(idText, out var componentId))
        {
            _editor.SelectComponent(componentId);
            RefreshAll("Componente selecionado.");
        }
    }

    private void RefreshAll(string? message = null)
    {
        if (!IsNodeReady() || _editor is null)
        {
            return;
        }

        RefreshTree();
        RefreshProxies();
        RefreshInspector();
        if (message is not null)
        {
            SetStatus(message);
        }
    }

    private void RefreshTree()
    {
        _rebuildingTree = true;
        _sceneTree.Clear();
        var root = _sceneTree.CreateItem();
        foreach (var component in _editor!.Components)
        {
            var item = _sceneTree.CreateItem(root);
            item.SetText(0, $"{HumanizeType(component.Type)}  [{component.Id.ToString("N")[..8]}]");
            item.SetMetadata(0, component.Id.ToString("D"));
            if (_editor.SelectedComponentId == component.Id)
            {
                item.Select(0);
            }
        }

        _rebuildingTree = false;
    }

    private void RefreshProxies()
    {
        foreach (var child in _proxyRoot.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var component in _editor!.Components)
        {
            BuildProxy(component, _editor.SelectedComponentId == component.Id);
        }
    }

    private void BuildProxy(ComponentDocument component, bool selected)
    {
        var size = ProxySize(component.Type);
        var root = new Node3D
        {
            Name = $"Componente_{component.Id:N}",
            Position = ToGodot(component.Transform.Position),
            RotationDegrees = ToGodot(component.Transform.RotationDegrees)
        };
        _proxyRoot.AddChild(root);

        var color = selected ? new Color(1f, 0.72f, 0.12f, 0.52f) : ProxyColor(component.Type);
        var material = ProceduralGeometry.CreateMaterial(
            color,
            metallic: 0.12f,
            roughness: 0.48f,
            transparent: true,
            emission: selected ? new Color(0.55f, 0.22f, 0.01f) : null);
        ProceduralGeometry.AddBox(root, "Volume", size, new Vector3(0f, size.Y / 2f, 0f), material);
        ProceduralGeometry.AddWorldLabel(
            root,
            HumanizeType(component.Type),
            new Vector3(0f, size.Y + 0.16f, 0f),
            selected ? new Color(1f, 0.80f, 0.24f) : new Color(0.60f, 0.78f, 0.90f),
            fontSize: 26,
            pixelSize: 0.003f);

        if (selected)
        {
            BuildGizmo(root, size.Y + 0.05f);
        }
    }

    private static void BuildGizmo(Node3D root, float elevation)
    {
        var gizmo = new Node3D { Name = "GizmoXYZ", Position = new Vector3(0f, elevation, 0f) };
        root.AddChild(gizmo);
        var red = ProceduralGeometry.CreateMaterial(new Color(1f, 0.15f, 0.18f), unshaded: true);
        var green = ProceduralGeometry.CreateMaterial(new Color(0.18f, 1f, 0.38f), unshaded: true);
        var blue = ProceduralGeometry.CreateMaterial(new Color(0.16f, 0.52f, 1f), unshaded: true);
        ProceduralGeometry.AddBox(gizmo, "X", new Vector3(0.65f, 0.025f, 0.025f), new Vector3(0.325f, 0f, 0f), red);
        ProceduralGeometry.AddBox(gizmo, "Y", new Vector3(0.025f, 0.65f, 0.025f), new Vector3(0f, 0.325f, 0f), green);
        ProceduralGeometry.AddBox(gizmo, "Z", new Vector3(0.025f, 0.025f, 0.65f), new Vector3(0f, 0f, 0.325f), blue);
    }

    private void RefreshInspector()
    {
        if (_editor!.SelectedComponentId is not { } selectedId)
        {
            _selectionLabel.Text = "Nenhum componente selecionado.";
        }
        else
        {
            var component = _editor.GetComponent(selectedId);
            var position = component.Transform.Position;
            var rotation = component.Transform.RotationDegrees;
            _selectionLabel.Text =
                $"{HumanizeType(component.Type)}\n" +
                $"P  X {position.X:F2}  Y {position.Y:F2}  Z {position.Z:F2} m   •   RY {rotation.Y:F0}°";
        }

        _historyLabel.Text =
            $"Componentes: {_editor.Components.Count}  •  desfazer {_editor.UndoCount}  •  refazer {_editor.RedoCount}" +
            (_editor.IsDirty || _tagDefinitionsDirty ? "  •  NÃO SALVO" : "  •  SALVO");
        RefreshInspectorEditors();
    }

    private void SetStatus(string message, bool error = false)
    {
        if (!IsInstanceValid(_statusLabel))
        {
            return;
        }

        _statusLabel.Text = message;
        _statusLabel.AddThemeColorOverride(
            "font_color",
            error ? new Color(1f, 0.38f, 0.38f) : new Color(0.30f, 0.88f, 0.64f));
    }

    private static string CreateDefaultSavePath(string sceneName)
    {
        var safeName = new string(sceneName
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "cena";
        }

        return ProjectSettings.GlobalizePath($"user://edited-scenes/{safeName}.scene.json");
    }

    private static string EnsureUserDataPath(string path)
    {
        var userRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("user://"));
        var expandedPath = path.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            ? ProjectSettings.GlobalizePath(path)
            : path;
        var candidate = Path.GetFullPath(expandedPath);
        if (!candidate.StartsWith(userRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("O editor salva somente dentro do diretório user://.", nameof(path));
        }

        return candidate;
    }

    private static Vector3 ToGodot(Vector3Document vector) =>
        new((float)vector.X, (float)vector.Y, (float)vector.Z);

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360d;
        return normalized < 0d ? normalized + 360d : normalized;
    }

    private static string HumanizeType(string type) =>
        Catalog.FirstOrDefault(item => string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase)).Label
        ?? type;

    private static Vector3 ProxySize(string type)
    {
        if (type.Contains("conveyor", StringComparison.OrdinalIgnoreCase))
        {
            return new Vector3(1.5f, 0.20f, 0.70f);
        }

        if (type.Contains("tank", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("source", StringComparison.OrdinalIgnoreCase))
        {
            return new Vector3(0.75f, 1.10f, 0.75f);
        }

        if (type.Contains("display", StringComparison.OrdinalIgnoreCase))
        {
            return new Vector3(0.80f, 0.50f, 0.18f);
        }

        return new Vector3(0.42f, 0.52f, 0.42f);
    }

    private static Color ProxyColor(string type)
    {
        if (type.Contains("sensor", StringComparison.OrdinalIgnoreCase))
        {
            return new Color(1f, 0.46f, 0.10f, 0.34f);
        }

        if (type.Contains("valve", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("actuator", StringComparison.OrdinalIgnoreCase))
        {
            return new Color(0.92f, 0.20f, 0.26f, 0.34f);
        }

        if (type.Contains("indicator", StringComparison.OrdinalIgnoreCase))
        {
            return new Color(0.28f, 0.95f, 0.55f, 0.34f);
        }

        return new Color(0.18f, 0.62f, 1f, 0.30f);
    }

    private static PanelContainer CreatePanel()
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride(
            "panel",
            new StyleBoxFlat
            {
                BgColor = new Color(0.02f, 0.045f, 0.075f, 0.97f),
                BorderColor = new Color(0.22f, 0.46f, 0.72f, 0.75f),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 10,
                CornerRadiusTopRight = 10,
                CornerRadiusBottomLeft = 10,
                CornerRadiusBottomRight = 10
            });
        return panel;
    }

    private static Button CreateButton(string text, Color color)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        button.AddThemeFontSizeOverride("font_size", 11);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(color));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(color.Lightened(0.12f)));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(color.Darkened(0.12f)));
        var focus = ButtonStyle(new Color(0.02f, 0.04f, 0.07f, 0f));
        focus.DrawCenter = false;
        focus.BorderColor = new Color(0.75f, 0.94f, 1f);
        focus.BorderWidthLeft = 2;
        focus.BorderWidthTop = 2;
        focus.BorderWidthRight = 2;
        focus.BorderWidthBottom = 2;
        button.AddThemeStyleboxOverride("focus", focus);
        return button;
    }

    private static void AddActionButton(
        Container parent,
        string text,
        Action action,
        Color? color = null)
    {
        var button = CreateButton(text, color ?? new Color(0.18f, 0.31f, 0.46f));
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.Pressed += action;
        parent.AddChild(button);
    }

    private static StyleBoxFlat ButtonStyle(Color color) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 8,
        ContentMarginRight = 8,
        ContentMarginTop = 7,
        ContentMarginBottom = 7
    };
}

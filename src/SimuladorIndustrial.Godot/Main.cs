using System.Globalization;
using System.Text;
using Godot;
using SimuladorIndustrial.Core.Diagnostics;
using SimuladorIndustrial.Core.Drivers.Modbus;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Runtime;
using SimuladorIndustrial.Core.Scenarios;
using SimuladorIndustrial.Core.Tags;
using SimulationEngine = SimuladorIndustrial.Core.Runtime.SimulationEngine;

namespace SimuladorIndustrial.GodotApp;

public sealed partial class Main : Node
{
    private const double ModbusPollIntervalSeconds = 0.10d;
    private const double ModbusReconnectIntervalSeconds = 2.0d;

    private SceneDocument _scene = null!;
    private SimulationEngine _engine = null!;
    private EventJournal _journal = null!;
    private TagRegistry _tags = null!;
    private AnimatedFillingLineSystem _line = null!;
    private DemoPlcControllerSystem _controller = null!;
    private FillingCellView _cellView = null!;
    private OrbitCameraController _cameraController = null!;
    private SceneEditor3DController _sceneEditor = null!;
    private ModbusTcpDriver? _modbusDriver;
    private TagDefinition _detectedTag = null!;
    private TagDefinition _gateTag = null!;
    private TagDefinition _valveTag = null!;
    private TagDefinition _volumeTag = null!;
    private Label _runStateLabel = null!;
    private Label _phaseLabel = null!;
    private Label _cameraModeLabel = null!;
    private Label _tickLabel = null!;
    private Label _volumeLabel = null!;
    private Label _sensorLabel = null!;
    private Label _gateLabel = null!;
    private Label _valveLabel = null!;
    private Label _eventsLabel = null!;
    private Label _logicMetricLabel = null!;
    private Label _physicsMetricLabel = null!;
    private Label _renderMetricLabel = null!;
    private Label _networkMetricLabel = null!;
    private Label _modbusStatusLabel = null!;
    private Label _controllerModeLabel = null!;
    private Label _coordinatesLabel = null!;
    private Label _fpsLabel = null!;
    private Label _tagMonitorLabel = null!;
    private Label _timelineLabel = null!;
    private Button _runButton = null!;
    private Button _pauseButton = null!;
    private Button _resetButton = null!;
    private Button _stepButton = null!;
    private Button _exportButton = null!;
    private Button _modbusButton = null!;
    private Label _exportStatusLabel = null!;
    private ConfirmationDialog _resetConfirmation = null!;
    private ConfirmationDialog _modbusConfirmation = null!;
    private bool _initialized;
    private bool _captureRequested;
    private bool _captureEditorRequested;
    private string _captureFileName = "visual-check.png";
    private int _captureFramesRemaining = 540;
    private double _diagnosticsRefreshSeconds;
    private double _smoothedRenderMilliseconds;
    private bool _modbusOperationInProgress;
    private bool _modbusPollInProgress;
    private bool _externalControlEnabled;
    private double _modbusPollElapsedSeconds;
    private double _modbusReconnectElapsedSeconds;
    private string _evidenceRunId = EvidenceExporter.CreateRunId();

    public override void _Ready()
    {
        try
        {
            _scene = LoadScene();
            ComposeSimulation(_scene);
            LoadUiPreferences();
            BuildWorld();
            BuildHud(_scene);
            _engine.Reset();
            _engine.Run();
            var arguments = OS.GetCmdlineUserArgs();
            var uiScaleArgument = arguments.FirstOrDefault(argument =>
                argument.StartsWith("--ui-scale=", StringComparison.OrdinalIgnoreCase));
            if (uiScaleArgument is not null)
            {
                var scaleText = uiScaleArgument[(uiScaleArgument.IndexOf('=') + 1)..];
                if (!double.TryParse(
                        scaleText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var scale) ||
                    scale is < 0.75d or > 1.50d)
                {
                    throw new ArgumentException($"Escala de interface inválida: {scaleText}.");
                }

                _uiPreferences = _uiPreferences with { UiScale = scale };
                RefreshHudLayout(force: true);
            }
            var captureWelcomeRequested = arguments.Contains(
                "--capture-welcome",
                StringComparer.OrdinalIgnoreCase);
            _captureEditorRequested = arguments.Contains(
                "--capture-editor",
                StringComparer.OrdinalIgnoreCase);
            var captureSectionArgument = arguments.FirstOrDefault(argument =>
                argument.StartsWith("--capture-section=", StringComparison.OrdinalIgnoreCase));
            _captureRequested = captureWelcomeRequested ||
                                _captureEditorRequested ||
                                captureSectionArgument is not null ||
                                arguments.Contains("--capture-frame", StringComparer.OrdinalIgnoreCase);
            if (captureWelcomeRequested)
            {
                _captureFileName = "welcome-check.png";
                ShowWelcome();
            }
            else if (_captureRequested)
            {
                HideWelcome();
            }

            if (captureSectionArgument is not null)
            {
                var sectionText = captureSectionArgument[(captureSectionArgument.IndexOf('=') + 1)..];
                if (!Enum.TryParse<ApplicationSection>(sectionText, ignoreCase: true, out var section))
                {
                    throw new ArgumentException($"Seção de captura desconhecida: {sectionText}.");
                }

                NavigateTo(section);
                _captureFileName = $"{section.ToString().ToLowerInvariant()}-check.png";
            }
            if (_captureEditorRequested)
            {
                _captureFramesRemaining = 180;
                NavigateTo(ApplicationSection.Editor);
                _captureFileName = "editor-check.png";
            }
            if (_welcomeLayer.Visible)
            {
                _engine.Pause();
            }
            _initialized = true;
            UpdateVisuals(0d);
            if (arguments.Contains("--start-modbus", StringComparer.OrdinalIgnoreCase))
            {
                _ = ToggleModbusAsync();
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Falha ao iniciar o Simulador Industrial 3D: {exception}");
            BuildFatalError(exception.Message);
        }
    }

    public override void _Process(double delta)
    {
        if (!_initialized)
        {
            return;
        }

        _engine.Advance(TimeSpan.FromSeconds(Math.Min(delta, 0.1d)));
        RefreshHudLayout();
        DriveModbusClient(delta);
        UpdateVisuals(delta);
        CaptureValidationFrameIfRequested();
    }

    public override void _Input(InputEvent @event)
    {
        if (!GodotObject.IsInstanceValid(_welcomeLayer) ||
            !_welcomeLayer.Visible ||
            @event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        var isNavigationShortcut = key.AltPressed &&
                                   key.Keycode is Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4 or Key.Key5 or Key.Key6;
        if (isNavigationShortcut || key.Keycode == Key.F2)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        if (_modbusDriver is null)
        {
            return;
        }

        try
        {
            _modbusDriver.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Falha ao encerrar Modbus: {exception.Message}");
        }
    }

    private SceneDocument LoadScene()
    {
        var projectRoot = ProjectSettings.GlobalizePath("res://");
        var scenePath = Path.GetFullPath(Path.Combine(
            projectRoot,
            "..",
            "..",
            "samples",
            "celula-envase.scene.json"));

        if (!File.Exists(scenePath))
        {
            throw new FileNotFoundException("Cena de envase não encontrada.", scenePath);
        }

        return SceneSerializer.Deserialize(File.ReadAllText(scenePath));
    }

    private void ComposeSimulation(SceneDocument scene)
    {
        _journal = new EventJournal();
        _tags = new TagRegistry(_journal);
        foreach (var tag in scene.Tags)
        {
            _tags.Register(tag);
        }

        _detectedTag = FindTag(scene, "recipienteDetectado");
        _gateTag = FindTag(scene, "bloqueadorFechado");
        _valveTag = FindTag(scene, "valvulaEnchimento");
        _volumeTag = FindTag(scene, "volumePreenchido");
        var flow = ReadFloatParameter(scene, "process.filling-head.v1", "flowLitersPerSecond");
        var conveyorSpeed = ReadFloatParameter(scene, "conveyor.straight.v1", "speedMetersPerSecond");

        _line = new AnimatedFillingLineSystem(
            _detectedTag.Id,
            _gateTag.Id,
            _valveTag.Id,
            _volumeTag.Id,
            spawnX: -2.2d,
            stationX: 0.6d,
            exitX: 2.35d,
            conveyorSpeedMetersPerSecond: Math.Max(0.45d, conveyorSpeed),
            flowLitersPerSecond: flow,
            capacityLiters: 1f,
            respawnDelaySeconds: 0.8d,
            sensorHalfWidthMeters: 0.09d);
        _controller = new DemoPlcControllerSystem(
            _detectedTag.Id,
            _volumeTag.Id,
            _gateTag.Id,
            _valveTag.Id,
            targetVolumeLiters: 1f);

        _engine = new SimulationEngine(
            _tags,
            _journal,
            [_line, _controller],
            new FixedStepClock(
                TimeSpan.FromMilliseconds(scene.Simulation.LogicStepMilliseconds),
                scene.Simulation.MaxCatchUpSteps));

        var modbusConnection = scene.Connections.FirstOrDefault(connection =>
            string.Equals(connection.Driver, "modbusTcp", StringComparison.OrdinalIgnoreCase));
        if (modbusConnection is not null)
        {
            _modbusDriver = ModbusTcpDriver.FromConnection(modbusConnection, _tags, _journal);
        }
    }

    private void BuildWorld()
    {
        var world = new Node3D { Name = "Mundo3D" };
        AddChild(world);

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.025f, 0.043f, 0.071f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.49f, 0.64f, 0.80f),
            AmbientLightEnergy = 0.62f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.07f, 0.11f, 0.17f),
            FogDensity = 0.012f
        };
        world.AddChild(new WorldEnvironment
        {
            Name = "Ambiente",
            Environment = environment
        });

        var keyLight = new DirectionalLight3D
        {
            Name = "LuzPrincipal",
            RotationDegrees = new Vector3(-54f, -28f, 0f),
            LightColor = new Color(0.83f, 0.91f, 1f),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 24f
        };
        world.AddChild(keyLight);
        world.AddChild(new OmniLight3D
        {
            Name = "LuzDePreenchimento",
            Position = new Vector3(-2.2f, 3.8f, 2.6f),
            LightColor = new Color(0.20f, 0.58f, 1f),
            LightEnergy = 4.2f,
            OmniRange = 9f,
            ShadowEnabled = true
        });
        world.AddChild(new OmniLight3D
        {
            Name = "LuzQuente",
            Position = new Vector3(2.6f, 2.6f, -2.0f),
            LightColor = new Color(1f, 0.58f, 0.25f),
            LightEnergy = 2.1f,
            OmniRange = 7f
        });

        ProceduralGeometry.AddCartesianPlane(world);

        _cellView = new FillingCellView();
        world.AddChild(_cellView);

        _cameraController = new OrbitCameraController { Name = "CameraOrbital" };
        world.AddChild(_cameraController);

        _sceneEditor = new SceneEditor3DController();
        _sceneEditor.Configure(_scene);
        _sceneEditor.BindRuntimeTags(_tags, () => _engine.Clock.Tick);
        _sceneEditor.ActiveChanged += HandleEditorActiveChanged;
        _sceneEditor.SceneChanged += scene => _scene = scene;
        world.AddChild(_sceneEditor);
    }

    private void BuildHud(SceneDocument scene)
    {
        var layer = new CanvasLayer { Name = "Interface" };
        AddChild(layer);
        var root = new Control
        {
            Name = "Hud",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layer.AddChild(root);
        ConfigureHudRoot(root);

        BuildTitle(root, scene);
        BuildStatusPanel(root);
        BuildAxisLegend(root);
        BuildDiagnosticsPanel(root);
        BuildCameraHelp(root);
        BuildConfirmations(root);
        BuildApplicationNavigation(root, scene);
    }

    private void BuildTitle(Control root, SceneDocument scene)
    {
        var panel = CreatePanel(new Color(0.025f, 0.055f, 0.095f, 0.92f), new Color(0.10f, 0.55f, 0.82f, 0.55f));
        panel.Name = "BarraSuperior";
        panel.AnchorRight = 1f;
        panel.OffsetLeft = 20f;
        panel.OffsetTop = 18f;
        panel.OffsetRight = -20f;
        panel.OffsetBottom = 92f;
        root.AddChild(panel);

        var margin = CreateMargin(22, 16, 22, 14);
        panel.AddChild(margin);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        margin.AddChild(row);

        var titleBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(titleBox);
        var title = new Label { Text = "SIMULADOR INDUSTRIAL 3D" };
        title.AddThemeFontSizeOverride("font_size", 25);
        title.AddThemeColorOverride("font_color", new Color(0.82f, 0.94f, 1f));
        titleBox.AddChild(title);
        var subtitle = new Label { Text = $"{scene.Name}  •  plano cartesiano métrico  •  passo lógico {scene.Simulation.LogicStepMilliseconds} ms" };
        subtitle.AddThemeFontSizeOverride("font_size", 13);
        subtitle.AddThemeColorOverride("font_color", new Color(0.48f, 0.67f, 0.80f));
        titleBox.AddChild(subtitle);

        var badge = new Label
        {
            Text = "GODOT 4.7  ·  CLEAN-ROOM",
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.AddThemeFontSizeOverride("font_size", 13);
        badge.AddThemeColorOverride("font_color", new Color(0.25f, 0.93f, 0.68f));
        row.AddChild(badge);
        _fpsLabel = new Label
        {
            Text = "0 FPS",
            CustomMinimumSize = new Vector2(70f, 0f),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _fpsLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.75f, 0.88f));
        row.AddChild(_fpsLabel);
    }

    private void BuildStatusPanel(Control root)
    {
        var panel = CreatePanel(new Color(0.025f, 0.055f, 0.090f, 0.95f), new Color(0.16f, 0.48f, 0.68f, 0.60f));
        _statusPanel = panel;
        panel.Name = "PainelOperacional";
        panel.AnchorLeft = 1f;
        panel.AnchorRight = 1f;
        panel.AnchorBottom = 1f;
        panel.OffsetLeft = -348f;
        panel.OffsetTop = 112f;
        panel.OffsetRight = -20f;
        panel.OffsetBottom = -20f;
        root.AddChild(panel);

        var margin = CreateMargin(20, 20, 12, 20);
        panel.AddChild(margin);
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
        content.AddThemeConstantOverride("separation", 7);
        scroll.AddChild(content);

        var heading = new Label { Text = "CONTROLE DA CÉLULA" };
        heading.AddThemeFontSizeOverride("font_size", 18);
        heading.AddThemeColorOverride("font_color", new Color(0.76f, 0.91f, 1f));
        content.AddChild(heading);
        content.AddChild(CreateSeparator());

        _runStateLabel = AddStatusRow(content, "SIMULAÇÃO", "EXECUTANDO");
        _phaseLabel = AddStatusRow(content, "FASE", "APROXIMAÇÃO");
        _cameraModeLabel = AddStatusRow(content, "CÂMERA", "ÓRBITA");
        _tickLabel = AddStatusRow(content, "TICK", "0");
        _volumeLabel = AddStatusRow(content, "VOLUME", "0,000 L");
        content.AddChild(CreateSeparator());
        _sensorLabel = AddStatusRow(content, "SENSOR", "INATIVO");
        _gateLabel = AddStatusRow(content, "BLOQUEADOR", "ABERTO");
        _valveLabel = AddStatusRow(content, "VÁLVULA", "FECHADA");
        _eventsLabel = AddStatusRow(content, "EVENTOS", "0");
        _modbusStatusLabel = AddStatusRow(content, "MODBUS", "PARADO");
        _logicMetricLabel = AddStatusRow(content, "LÓGICA", "0,000 ms");
        _physicsMetricLabel = AddStatusRow(content, "FÍSICA", "0,000 ms");
        _renderMetricLabel = AddStatusRow(content, "RENDER", "0,000 ms");
        _networkMetricLabel = AddStatusRow(content, "REDE", "0 req / 0 falhas");
        content.AddChild(CreateSeparator());

        _controllerModeLabel = new Label
        {
            Text = "PLC demonstrativo automático",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _controllerModeLabel.AddThemeFontSizeOverride("font_size", 13);
        _controllerModeLabel.AddThemeColorOverride("font_color", new Color(0.30f, 0.88f, 0.64f));
        content.AddChild(_controllerModeLabel);

        _modbusButton = CreateButton("⇄  INICIAR MODBUS", new Color(0.10f, 0.42f, 0.52f));
        _modbusButton.TooltipText = "Ativa ou encerra o controle externo configurado na cena.";
        _modbusButton.Pressed += RequestToggleModbus;
        _modbusButton.Disabled = _modbusDriver is null;
        content.AddChild(_modbusButton);

        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 8);
        content.AddChild(controls);
        _runButton = CreateButton("▶  EXECUTAR", new Color(0.08f, 0.55f, 0.37f));
        _pauseButton = CreateButton("Ⅱ  PAUSAR", new Color(0.82f, 0.48f, 0.08f));
        _resetButton = CreateButton("↺  REINICIAR", new Color(0.25f, 0.39f, 0.56f));
        _runButton.TooltipText = "Inicia ou continua a simulação.";
        _pauseButton.TooltipText = "Pausa a simulação sem perder o estado atual.";
        _resetButton.TooltipText = "Retorna processo, tags, falhas e métricas ao estado inicial.";
        controls.AddChild(_runButton);
        controls.AddChild(_pauseButton);
        content.AddChild(_resetButton);
        _runButton.Pressed += RunSimulation;
        _pauseButton.Pressed += PauseSimulation;
        _resetButton.Pressed += RequestResetSimulation;

        var cameraButton = CreateButton("⌂  VISTA INICIAL", new Color(0.10f, 0.36f, 0.60f));
        cameraButton.TooltipText = "Restaura a posição e o pivô iniciais da câmera (Home).";
        cameraButton.Pressed += () => _cameraController.ResetView();
        content.AddChild(cameraButton);

        _stepButton = CreateButton("⏭  AVANÇAR 1 PASSO", new Color(0.42f, 0.28f, 0.62f));
        _stepButton.TooltipText = "Executa exatamente um passo lógico enquanto a simulação está pausada.";
        _stepButton.Pressed += StepSimulation;
        content.AddChild(_stepButton);

        _exportButton = CreateButton("⇩  EXPORTAR EVIDÊNCIA", new Color(0.14f, 0.46f, 0.48f));
        _exportButton.TooltipText = "Salva valores, qualidade, eventos e métricas em JSON e CSV.";
        _exportButton.Pressed += () => _ = ExportEvidenceAsync();
        content.AddChild(_exportButton);

        _exportStatusLabel = new Label
        {
            Text = string.Empty,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _exportStatusLabel.AddThemeFontSizeOverride("font_size", 10);
        _exportStatusLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.75f, 0.88f));
        content.AddChild(_exportStatusLabel);
    }

    private void BuildConfirmations(Control root)
    {
        _resetConfirmation = new ConfirmationDialog
        {
            Title = "Reiniciar simulação?",
            DialogText = "O processo voltará ao início. Forçamentos, falhas, valores de tags e métricas serão limpos.",
            OkButtonText = "REINICIAR",
            CancelButtonText = "CANCELAR",
            Exclusive = true
        };
        _resetConfirmation.Confirmed += ResetSimulation;
        root.AddChild(_resetConfirmation);

        _modbusConfirmation = new ConfirmationDialog
        {
            Title = "Transferir controle ao PLC externo?",
            DialogText = "O PLC demonstrativo será desativado. Confirme host, porta, Unit ID e lógica externa antes de continuar.",
            OkButtonText = "ATIVAR MODBUS",
            CancelButtonText = "CANCELAR",
            Exclusive = true
        };
        _modbusConfirmation.Confirmed += () => _ = ToggleModbusAsync();
        root.AddChild(_modbusConfirmation);
    }

    private void BuildAxisLegend(Control root)
    {
        var panel = CreatePanel(new Color(0.025f, 0.055f, 0.09f, 0.90f), new Color(0.14f, 0.38f, 0.55f, 0.55f));
        _axisPanel = panel;
        panel.AnchorTop = 1f;
        panel.AnchorBottom = 1f;
        panel.OffsetLeft = 20f;
        panel.OffsetTop = -122f;
        panel.OffsetRight = 390f;
        panel.OffsetBottom = -20f;
        root.AddChild(panel);

        var margin = CreateMargin(16, 12, 16, 12);
        panel.AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 5);
        margin.AddChild(content);
        var axes = new Label { Text = "X  VERMELHO     Y  VERDE     Z  AZUL" };
        axes.AddThemeFontSizeOverride("font_size", 13);
        axes.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 0.93f));
        content.AddChild(axes);
        _coordinatesLabel = new Label { Text = "CURSOR  X: —   Y: 0,00   Z: —" };
        _coordinatesLabel.AddThemeFontSizeOverride("font_size", 15);
        _coordinatesLabel.AddThemeColorOverride("font_color", new Color(0.30f, 0.85f, 1f));
        content.AddChild(_coordinatesLabel);
        var scale = new Label { Text = "1 unidade = 1 metro  •  grade fina 0,25 m  •  grade forte 1 m" };
        scale.AddThemeFontSizeOverride("font_size", 11);
        scale.AddThemeColorOverride("font_color", new Color(0.42f, 0.60f, 0.72f));
        content.AddChild(scale);
    }

    private void BuildDiagnosticsPanel(Control root)
    {
        var panel = CreatePanel(new Color(0.025f, 0.055f, 0.09f, 0.91f), new Color(0.14f, 0.38f, 0.55f, 0.50f));
        _diagnosticsPanel = panel;
        panel.Name = "PainelDiagnosticos";
        panel.AnchorRight = 1f;
        panel.AnchorTop = 1f;
        panel.AnchorBottom = 1f;
        panel.OffsetLeft = 410f;
        panel.OffsetTop = -238f;
        panel.OffsetRight = -368f;
        panel.OffsetBottom = -82f;
        root.AddChild(panel);

        var margin = CreateMargin(14, 10, 14, 10);
        panel.AddChild(margin);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 18);
        margin.AddChild(columns);

        var tags = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        columns.AddChild(tags);
        var tagsHeading = new Label { Text = "MONITOR DE TAGS" };
        tagsHeading.AddThemeFontSizeOverride("font_size", 12);
        tagsHeading.AddThemeColorOverride("font_color", new Color(0.30f, 0.85f, 1f));
        tags.AddChild(tagsHeading);
        _tagMonitorLabel = new Label { Text = "Aguardando dados..." };
        _tagMonitorLabel.AddThemeFontSizeOverride("font_size", 11);
        _tagMonitorLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 0.93f));
        tags.AddChild(_tagMonitorLabel);

        columns.AddChild(CreateVerticalSeparator());

        var timeline = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        columns.AddChild(timeline);
        var timelineHeading = new Label { Text = "LINHA DO TEMPO" };
        timelineHeading.AddThemeFontSizeOverride("font_size", 12);
        timelineHeading.AddThemeColorOverride("font_color", new Color(0.30f, 0.85f, 1f));
        timeline.AddChild(timelineHeading);
        _timelineLabel = new Label { Text = "Sem eventos." };
        _timelineLabel.AddThemeFontSizeOverride("font_size", 11);
        _timelineLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 0.93f));
        timeline.AddChild(_timelineLabel);
    }

    private void BuildCameraHelp(Control root)
    {
        var panel = CreatePanel(new Color(0.025f, 0.055f, 0.09f, 0.86f), new Color(0.14f, 0.38f, 0.55f, 0.45f));
        _cameraHelpPanel = panel;
        panel.AnchorLeft = 0.5f;
        panel.AnchorRight = 0.5f;
        panel.AnchorTop = 1f;
        panel.AnchorBottom = 1f;
        panel.OffsetLeft = -330f;
        panel.OffsetTop = -70f;
        panel.OffsetRight = 330f;
        panel.OffsetBottom = -20f;
        root.AddChild(panel);
        var help = new Label
        {
            Text = "F: órbita/livre  •  ÓRBITA: esq./dir./roda  •  LIVRE: WASD + Q/E, olhar dir.  •  HOME: início",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        help.AddThemeFontSizeOverride("font_size", 12);
        help.AddThemeColorOverride("font_color", new Color(0.56f, 0.72f, 0.84f));
        panel.AddChild(help);
    }

    private void UpdateVisuals(double delta)
    {
        var gateClosed = _tags.Get(_gateTag.Id).EffectiveValue.AsBool();
        var valveOpen = _tags.Get(_valveTag.Id).EffectiveValue.AsBool();
        _cellView.ApplyState(_line, gateClosed, valveOpen, delta);

        var running = _engine.State == SimulationRunState.Running;
        _runStateLabel.Text = running ? "EXECUTANDO" : _engine.State == SimulationRunState.Paused ? "PAUSADA" : "PARADA";
        SetStateColor(_runStateLabel, running, new Color(0.30f, 0.96f, 0.64f), new Color(1f, 0.66f, 0.24f));
        _phaseLabel.Text = PhaseText(_line.Phase);
        _cameraModeLabel.Text = _cameraController.Mode == CameraNavigationMode.Orbit ? "ÓRBITA" : "LIVRE";
        _tickLabel.Text = _engine.Clock.Tick.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
        _volumeLabel.Text = $"{_line.VolumeLiters.ToString("F3", CultureInfo.GetCultureInfo("pt-BR"))} L  ({_line.FillRatio:P0})";
        _sensorLabel.Text = _line.SensorActive ? "ATIVO" : "INATIVO";
        SetStateColor(_sensorLabel, _line.SensorActive);
        _gateLabel.Text = gateClosed ? "FECHADO" : "ABERTO";
        SetStateColor(_gateLabel, gateClosed);
        _valveLabel.Text = valveOpen ? "ABERTA" : "FECHADA";
        SetStateColor(_valveLabel, valveOpen);
        _eventsLabel.Text = _journal.Count.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
        _fpsLabel.Text = $"{Godot.Engine.GetFramesPerSecond()} FPS";
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var metrics = _engine.Metrics;
        _logicMetricLabel.Text = $"{metrics.LastLogicStepMilliseconds.ToString("F3", culture)} ms";
        _physicsMetricLabel.Text = $"{(Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimePhysicsProcess) * 1_000d).ToString("F3", culture)} ms";
        if (delta > 0d)
        {
            var renderMilliseconds = delta * 1_000d;
            _smoothedRenderMilliseconds = _smoothedRenderMilliseconds == 0d
                ? renderMilliseconds
                : _smoothedRenderMilliseconds * 0.90d + renderMilliseconds * 0.10d;
        }
        _renderMetricLabel.Text = $"{_smoothedRenderMilliseconds.ToString("F2", culture)} ms";

        if (_modbusDriver is not null)
        {
            var network = _modbusDriver.Monitor;
            _modbusStatusLabel.Text = $"{network.Role.ToString().ToUpperInvariant()} · {network.State.ToString().ToUpperInvariant()}";
            _networkMetricLabel.Text = $"{network.TotalRequests:N0} req / {network.FailedRequests:N0} falhas";
            _modbusButton.Text = _externalControlEnabled ? "■  PARAR MODBUS" : "⇄  INICIAR MODBUS";
            _modbusButton.Disabled = _modbusOperationInProgress;
        }
        else
        {
            _modbusStatusLabel.Text = "NÃO CONFIGURADO";
            _networkMetricLabel.Text = "—";
        }

        _controllerModeLabel.Text = _externalControlEnabled
            ? "PLC externo via Modbus"
            : "PLC demonstrativo automático";

        _runButton.Disabled = running;
        _pauseButton.Disabled = !running;
        _resetButton.Disabled = false;
        _stepButton.Disabled = running;

        _diagnosticsRefreshSeconds += delta;
        if (delta == 0d || _diagnosticsRefreshSeconds >= 0.25d)
        {
            _diagnosticsRefreshSeconds = 0d;
            RefreshDiagnostics();
        }

        if (_cameraController.TryGetGroundPoint(GetViewport().GetMousePosition(), out var point))
        {
            _coordinatesLabel.Text = string.Create(
                CultureInfo.GetCultureInfo("pt-BR"),
                $"CURSOR  X: {point.X,6:F2} m   Y: {point.Y,4:F2} m   Z: {point.Z,6:F2} m");
        }

        UpdateApplicationNavigation();
    }

    private void RefreshDiagnostics()
    {
        var tagText = new StringBuilder();
        foreach (var tag in _tags.GetAll())
        {
            var direction = tag.Definition.Direction == TagDirection.PlcInput ? "I" : "Q";
            var value = tag.EffectiveValue.DataType switch
            {
                TagDataType.Bool => tag.EffectiveValue.AsBool() ? "ON" : "OFF",
                TagDataType.Int32 => tag.EffectiveValue.AsInt32().ToString(CultureInfo.InvariantCulture),
                TagDataType.Float32 => tag.EffectiveValue.AsFloat32().ToString("F3", CultureInfo.GetCultureInfo("pt-BR")),
                _ => "?"
            };
            tagText.Append(direction)
                .Append(tag.Definition.Address.ToString("D3", CultureInfo.InvariantCulture))
                .Append("  ")
                .Append(tag.Definition.CanonicalName)
                .Append(" = ")
                .Append(value)
                .Append("  [")
                .Append(tag.Quality)
                .AppendLine("]");
        }

        _tagMonitorLabel.Text = tagText.ToString().TrimEnd();

        var events = _journal.Events.TakeLast(5).Reverse();
        var eventText = new StringBuilder();
        foreach (var simulationEvent in events)
        {
            eventText.Append('#')
                .Append(simulationEvent.Sequence.ToString("D4", CultureInfo.InvariantCulture))
                .Append("  t=")
                .Append(simulationEvent.Tick.ToString(CultureInfo.InvariantCulture))
                .Append("  ")
                .AppendLine(simulationEvent.Category);
        }

        _timelineLabel.Text = eventText.Length == 0 ? "Sem eventos." : eventText.ToString().TrimEnd();
    }

    private void RunSimulation()
    {
        if (_engine.State != SimulationRunState.Running)
        {
            _engine.Run();
        }
    }

    private void PauseSimulation()
    {
        if (_engine.State == SimulationRunState.Running)
        {
            _engine.Pause();
        }
    }

    private void ResetSimulation()
    {
        _engine.Reset();
        _evidenceRunId = EvidenceExporter.CreateRunId();
        UpdateVisuals(0d);
    }

    private void RequestResetSimulation()
    {
        if (!_uiPreferences.ConfirmDestructiveActions)
        {
            ResetSimulation();
            return;
        }

        _resetConfirmation.PopupCentered(new Vector2I(520, 210));
    }

    private void RequestToggleModbus()
    {
        if (_externalControlEnabled)
        {
            _ = ToggleModbusAsync();
            return;
        }

        if (!_uiPreferences.ConfirmDestructiveActions)
        {
            _ = ToggleModbusAsync();
            return;
        }

        _modbusConfirmation.PopupCentered(new Vector2I(560, 230));
    }

    private void HandleEditorActiveChanged(bool active)
    {
        if (active && _engine.State == SimulationRunState.Running)
        {
            _engine.Pause();
        }

        _cellView.Visible = !active;
        _cameraController.InputEnabled = !active;
        HandleNavigationEditorState(active);
    }

    private async Task ToggleModbusAsync()
    {
        if (_modbusDriver is null || _modbusOperationInProgress)
        {
            return;
        }

        _modbusOperationInProgress = true;
        try
        {
            if (_externalControlEnabled)
            {
                await _modbusDriver.StopAsync();
                _externalControlEnabled = false;
                _controller.Enabled = true;
                SetClientTagQuality(TagQuality.Disconnected);
                _journal.Record(_engine.Clock.Tick, "control.mode", "Controle retornou ao PLC demonstrativo.");
            }
            else
            {
                await _modbusDriver.StartAsync();
                _externalControlEnabled = true;
                _controller.Enabled = false;
                _modbusPollElapsedSeconds = ModbusPollIntervalSeconds;
                _modbusReconnectElapsedSeconds = 0d;
                _journal.Record(_engine.Clock.Tick, "control.mode", "Controle transferido para o PLC externo via Modbus TCP.");
            }
        }
        catch (Exception exception)
        {
            _externalControlEnabled = false;
            _controller.Enabled = true;
            _journal.Record(_engine.Clock.Tick, "modbus.ui-error", exception.Message);
            GD.PushError($"Falha na operação Modbus: {exception}");
        }
        finally
        {
            _modbusOperationInProgress = false;
        }
    }

    private void DriveModbusClient(double delta)
    {
        if (!_externalControlEnabled ||
            _modbusDriver?.Role != ModbusConnectionRole.Client ||
            _modbusOperationInProgress ||
            _modbusPollInProgress)
        {
            return;
        }

        if (_modbusDriver.Client!.IsConnected)
        {
            _modbusPollElapsedSeconds += delta;
            if (_modbusPollElapsedSeconds >= ModbusPollIntervalSeconds)
            {
                _modbusPollElapsedSeconds = 0d;
                _ = SynchronizeModbusClientAsync();
            }

            return;
        }

        _modbusReconnectElapsedSeconds += delta;
        if (_modbusReconnectElapsedSeconds >= ModbusReconnectIntervalSeconds)
        {
            _modbusReconnectElapsedSeconds = 0d;
            _ = ReconnectModbusClientAsync();
        }
    }

    private async Task SynchronizeModbusClientAsync()
    {
        _modbusPollInProgress = true;
        try
        {
            await _modbusDriver!.SynchronizeOnceAsync(_engine.Clock.Tick);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Ciclo Modbus cliente falhou; a reconexão automática será tentada: {exception.Message}");
        }
        finally
        {
            _modbusPollInProgress = false;
        }
    }

    private async Task ReconnectModbusClientAsync()
    {
        _modbusPollInProgress = true;
        try
        {
            await _modbusDriver!.ReconnectAsync();
            await _modbusDriver.SynchronizeOnceAsync(_engine.Clock.Tick);
        }
        catch (Exception exception)
        {
            SetClientTagQuality(TagQuality.Disconnected);
            GD.PushWarning($"Reconexão Modbus cliente pendente: {exception.Message}");
        }
        finally
        {
            _modbusPollInProgress = false;
        }
    }

    private void SetClientTagQuality(TagQuality quality)
    {
        if (_modbusDriver?.Role != ModbusConnectionRole.Client)
        {
            return;
        }

        foreach (var tag in _tags.GetAll().Where(tag => tag.Quality != quality))
        {
            _tags.SetQuality(tag.Definition.Id, quality, _engine.Clock.Tick);
        }
    }

    private void StepSimulation()
    {
        if (_engine.State == SimulationRunState.Running)
        {
            return;
        }

        _engine.Run();
        _engine.Advance(_engine.Clock.Step);
        _engine.Pause();
        UpdateVisuals(0d);
    }

    private async Task ExportEvidenceAsync()
    {
        _exportButton.Disabled = true;
        _exportStatusLabel.Text = "Exportando...";

        try
        {
            var directory = ProjectSettings.GlobalizePath("user://evidence");
            Directory.CreateDirectory(directory);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var jsonPath = Path.Combine(directory, $"simulacao-{timestamp}.json");
            var csvPath = Path.Combine(directory, $"eventos-{timestamp}.csv");
            var telemetryPath = Path.Combine(directory, $"telemetria-{timestamp}.csv");
            var evidence = EvidenceExporter.Capture(
                _scene,
                _engine,
                _tags,
                _journal,
                runId: _evidenceRunId);

            await using (var json = File.Create(jsonPath))
            {
                await EvidenceExporter.WriteJsonAsync(json, evidence);
            }

            await using (var csv = File.Create(csvPath))
            {
                await EvidenceExporter.WriteEventsCsvAsync(csv, evidence);
            }

            await using (var telemetry = File.Create(telemetryPath))
            {
                await EvidenceExporter.WriteTagTelemetryCsvAsync(telemetry, evidence);
            }

            _exportStatusLabel.Text = $"JSON + eventos + telemetria salvos em\n{directory}";
            _exportStatusLabel.AddThemeColorOverride("font_color", new Color(0.30f, 0.88f, 0.64f));
            GD.Print($"Evidência exportada: {jsonPath}; {csvPath}; {telemetryPath}");
        }
        catch (Exception exception)
        {
            _exportStatusLabel.Text = "Falha ao exportar evidência.";
            _exportStatusLabel.AddThemeColorOverride("font_color", new Color(1f, 0.35f, 0.35f));
            GD.PushError($"Falha ao exportar evidência: {exception}");
        }
        finally
        {
            _exportButton.Disabled = false;
        }
    }

    private void CaptureValidationFrameIfRequested()
    {
        if (!_captureRequested || --_captureFramesRemaining > 0)
        {
            return;
        }

        var directory = Path.Combine(ProjectSettings.GlobalizePath("res://"), "artifacts");
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(directory, _captureFileName);
        var error = GetViewport().GetTexture().GetImage().SavePng(outputPath);
        if (error != Error.Ok)
        {
            GD.PushError($"Falha ao salvar captura visual: {error}.");
            GetTree().Quit(3);
            return;
        }

        GD.Print($"Captura visual salva em: {outputPath}");
        GetTree().Quit();
    }

    private void BuildFatalError(string message)
    {
        var label = new Label
        {
            Text = $"Não foi possível iniciar o simulador.\n{message}",
            Position = new Vector2(30f, 30f)
        };
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.35f, 0.35f));
        AddChild(label);
    }

    private static TagDefinition FindTag(SceneDocument scene, string canonicalName) =>
        scene.Tags.SingleOrDefault(tag => string.Equals(
            tag.CanonicalName,
            canonicalName,
            StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Tag obrigatória ausente: {canonicalName}.");

    private static float ReadFloatParameter(SceneDocument scene, string componentType, string parameterName)
    {
        var component = scene.Components.SingleOrDefault(item => string.Equals(
            item.Type,
            componentType,
            StringComparison.OrdinalIgnoreCase));
        if (component is null ||
            !component.Parameters.TryGetValue(parameterName, out var value) ||
            !value.TryGetSingle(out var result))
        {
            throw new InvalidOperationException(
                $"Componente {componentType} deve definir {parameterName}.");
        }

        return result;
    }

    private static string PhaseText(FillingLinePhase phase) => phase switch
    {
        FillingLinePhase.Approaching => "APROXIMAÇÃO",
        FillingLinePhase.Filling => "ENCHIMENTO",
        FillingLinePhase.Departing => "SAÍDA",
        FillingLinePhase.WaitingForNextContainer => "NOVO RECIPIENTE",
        _ => phase.ToString().ToUpperInvariant()
    };

    private static PanelContainer CreatePanel(Color background, Color border)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ShadowColor = new Color(0f, 0f, 0f, 0.34f),
            ShadowSize = 8
        };
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static MarginContainer CreateMargin(int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        return margin;
    }

    private static HSeparator CreateSeparator()
    {
        var separator = new HSeparator();
        separator.AddThemeConstantOverride("separation", 10);
        return separator;
    }

    private static VSeparator CreateVerticalSeparator()
    {
        var separator = new VSeparator();
        separator.AddThemeConstantOverride("separation", 10);
        return separator;
    }

    private static Label AddStatusRow(VBoxContainer parent, string name, string initialValue)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        var nameLabel = new Label
        {
            Text = name,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 13);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.47f, 0.65f, 0.77f));
        row.AddChild(nameLabel);
        var value = new Label
        {
            Text = initialValue,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(150f, 0f)
        };
        value.AddThemeFontSizeOverride("font_size", 14);
        value.AddThemeColorOverride("font_color", new Color(0.82f, 0.93f, 1f));
        row.AddChild(value);
        return value;
    }

    private static Button CreateButton(string text, Color color)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginTop = 9,
            ContentMarginBottom = 9,
            ContentMarginLeft = 10,
            ContentMarginRight = 10
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = color.Lightened(0.13f);
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = color.Darkened(0.13f);
        var button = new Button
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        var focus = (StyleBoxFlat)normal.Duplicate();
        focus.DrawCenter = false;
        focus.BorderColor = new Color(0.75f, 0.94f, 1f);
        focus.BorderWidthLeft = 3;
        focus.BorderWidthTop = 3;
        focus.BorderWidthRight = 3;
        focus.BorderWidthBottom = 3;
        button.AddThemeStyleboxOverride("focus", focus);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        return button;
    }

    private static void SetStateColor(
        Label label,
        bool active,
        Color? activeColor = null,
        Color? inactiveColor = null)
    {
        label.AddThemeColorOverride(
            "font_color",
            activeColor ?? (active ? new Color(0.28f, 0.96f, 0.62f) : new Color(0.57f, 0.68f, 0.76f)));
        if (!active && inactiveColor is { } explicitInactive)
        {
            label.AddThemeColorOverride("font_color", explicitInactive);
        }
    }
}

using System.Globalization;
using Godot;
using SimuladorIndustrial.Core.Drivers.Modbus;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.GodotApp;

public sealed partial class Main
{
    private static readonly double[] AvailableUiScales = [0.75d, 0.90d, 1.0d, 1.10d, 1.25d, 1.50d];

    private PanelContainer _statusPanel = null!;
    private PanelContainer _axisPanel = null!;
    private PanelContainer _diagnosticsPanel = null!;
    private PanelContainer _cameraHelpPanel = null!;
    private ApplicationNavigationRail _navigationRail = null!;
    private PanelContainer _workspacePage = null!;
    private Control _diagnosticsPage = null!;
    private Control _connectionsPage = null!;
    private Control _helpPage = null!;
    private Label _diagnosticsSummary = null!;
    private Label _diagnosticsTags = null!;
    private Label _diagnosticsTimeline = null!;
    private Label _connectionSummary = null!;
    private Label _connectionFeedback = null!;
    private OptionButton _connectionRole = null!;
    private LineEdit _connectionHost = null!;
    private SpinBox _connectionPort = null!;
    private SpinBox _connectionUnitId = null!;
    private Button _connectionToggleButton = null!;
    private Button _connectionApplyButton = null!;
    private OptionButton _uiScaleOption = null!;
    private OptionButton _startupSectionOption = null!;
    private CheckButton _confirmActionsOption = null!;
    private CheckButton _showWelcomeOption = null!;
    private Label _preferencesFeedback = null!;
    private Control _welcomeLayer = null!;
    private Button _welcomeStartButton = null!;
    private ApplicationSection _activeSection = ApplicationSection.Overview;

    private void BuildApplicationNavigation(Control root, SceneDocument scene)
    {
        BuildWorkspacePages(root);

        _navigationRail = new ApplicationNavigationRail();
        _navigationRail.SectionRequested += section => NavigateTo(section);
        _navigationRail.ExpandedChanged += HandleNavigationExpanded;
        root.AddChild(_navigationRail);

        _sceneEditor.SetToggleButtonVisible(false);
        PopulateConnectionFields(scene.Connections.FirstOrDefault(IsModbusConnection));
        PopulatePreferenceFields();
        NavigateTo(_uiPreferences.StartupSection);

        if (!_uiPreferences.WelcomeDismissed)
        {
            ShowWelcome();
        }
    }

    private void BuildWorkspacePages(Control root)
    {
        _workspacePage = CreatePanel(
            new Color(0.018f, 0.043f, 0.073f, 0.975f),
            new Color(0.08f, 0.48f, 0.70f, 0.82f));
        _workspacePage.Name = "PaginaDeTrabalho";
        _workspacePage.AnchorRight = 1f;
        _workspacePage.AnchorBottom = 1f;
        _workspacePage.OffsetLeft = 258f;
        _workspacePage.OffsetTop = 112f;
        _workspacePage.OffsetRight = -20f;
        _workspacePage.OffsetBottom = -84f;
        _workspacePage.Visible = false;
        root.AddChild(_workspacePage);

        var margin = CreateMargin(24, 20, 24, 20);
        _workspacePage.AddChild(margin);
        var stack = new Control();
        stack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddChild(stack);

        _diagnosticsPage = BuildDiagnosticsWorkspace();
        _connectionsPage = BuildConnectionsWorkspace();
        _helpPage = BuildHelpWorkspace();
        stack.AddChild(_diagnosticsPage);
        stack.AddChild(_connectionsPage);
        stack.AddChild(_helpPage);

        BuildWelcome(root);
    }

    private Control BuildDiagnosticsWorkspace()
    {
        var page = CreatePageRoot("DIAGNÓSTICO", "Saúde da simulação, tags, eventos e desempenho em um só lugar.");
        var content = GetPageContent(page);

        _diagnosticsSummary = CreateBodyLabel(14, new Color(0.76f, 0.91f, 1f));
        content.AddChild(_diagnosticsSummary);
        content.AddChild(CreateSeparator());

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 18);
        content.AddChild(columns);

        _diagnosticsTags = CreateDiagnosticCard(columns, "TAGS E QUALIDADE");
        _diagnosticsTimeline = CreateDiagnosticCard(columns, "EVENTOS RECENTES");

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        content.AddChild(actions);
        var export = CreateButton("⇩  EXPORTAR EVIDÊNCIA", new Color(0.12f, 0.48f, 0.50f));
        export.TooltipText = "Gera arquivos JSON e CSV com o estado atual.";
        export.Pressed += () => _ = ExportEvidenceAsync();
        actions.AddChild(export);
        var resetView = CreateButton("⌂  VISTA INICIAL", new Color(0.12f, 0.36f, 0.58f));
        resetView.Pressed += _cameraController.ResetView;
        actions.AddChild(resetView);
        return page;
    }

    private Control BuildConnectionsWorkspace()
    {
        var page = CreatePageRoot("CONEXÕES", "Configure o Modbus TCP sem editar o JSON manualmente.");
        var content = GetPageContent(page);

        _connectionSummary = CreateBodyLabel(14, new Color(0.76f, 0.91f, 1f));
        content.AddChild(_connectionSummary);
        content.AddChild(CreateSeparator());

        var form = new GridContainer { Columns = 2 };
        form.AddThemeConstantOverride("h_separation", 18);
        form.AddThemeConstantOverride("v_separation", 12);
        content.AddChild(form);

        AddFormLabel(form, "Papel");
        _connectionRole = new OptionButton { FocusMode = Control.FocusModeEnum.All };
        _connectionRole.AddItem("Servidor — PLC conecta ao simulador");
        _connectionRole.AddItem("Cliente — simulador conecta ao endpoint");
        form.AddChild(_connectionRole);

        AddFormLabel(form, "Host / IP");
        _connectionHost = new LineEdit
        {
            PlaceholderText = "127.0.0.1",
            TooltipText = "Use localhost ou um endereço simples, sem http:// e sem caminho."
        };
        form.AddChild(_connectionHost);

        AddFormLabel(form, "Porta TCP");
        _connectionPort = CreateIntegerSpinBox(1, 65_535);
        form.AddChild(_connectionPort);

        AddFormLabel(form, "Unit ID");
        _connectionUnitId = CreateIntegerSpinBox(0, 255);
        form.AddChild(_connectionUnitId);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        content.AddChild(actions);
        _connectionApplyButton = CreateButton("✓  VALIDAR E APLICAR", new Color(0.10f, 0.48f, 0.38f));
        _connectionApplyButton.Pressed += () => _ = ApplyConnectionConfigurationAsync();
        actions.AddChild(_connectionApplyButton);
        _connectionToggleButton = CreateButton("⇄  INICIAR MODBUS", new Color(0.10f, 0.42f, 0.54f));
        _connectionToggleButton.Pressed += RequestToggleModbus;
        actions.AddChild(_connectionToggleButton);

        _connectionFeedback = CreateBodyLabel(12, new Color(0.50f, 0.76f, 0.88f));
        _connectionFeedback.Text = "A configuração é validada antes de substituir a conexão ativa.";
        content.AddChild(_connectionFeedback);
        content.AddChild(CreateSeparator());

        var mapping = CreateBodyLabel(13, new Color(0.68f, 0.84f, 0.93f));
        mapping.Text =
            "MAPEAMENTO PADRÃO\n\n" +
            "PLC INPUT  bool       → Discrete Inputs (FC02)\n" +
            "PLC INPUT  int/float  → Input Registers (FC04, 2 palavras)\n" +
            "PLC OUTPUT bool       → Coils (FC01/05/15)\n" +
            "PLC OUTPUT int/float  → Holding Registers (FC03/06/16)\n\n" +
            "Em laboratório local, prefira 127.0.0.1 e uma porta não privilegiada, como 5020.";
        content.AddChild(mapping);
        return page;
    }

    private Control BuildHelpWorkspace()
    {
        var page = CreatePageRoot("AJUDA E PREFERÊNCIAS", "Primeiros passos, atalhos e conforto visual.");
        var content = GetPageContent(page);

        var quickStart = CreateBodyLabel(13, new Color(0.72f, 0.88f, 0.96f));
        quickStart.Text =
            "PRIMEIRO CICLO\n" +
            "1. Abra Simulação e clique em Executar.\n" +
            "2. Observe sensor → bloqueador → válvula → liberação.\n" +
            "3. Use Diagnóstico para conferir tags e eventos.\n" +
            "4. Abra Editor 3D para mover, duplicar ou configurar componentes.\n\n" +
            "ATALHOS\n" +
            "Alt+1…6: navegar  ·  F2: editor  ·  F: câmera orbital/livre  ·  Home: vista inicial\n" +
            "Editor: Tab seleciona  ·  WASD/R/F move  ·  Q/E gira  ·  Ctrl+S salva";
        content.AddChild(quickStart);
        content.AddChild(CreateSeparator());

        var preferences = new GridContainer { Columns = 2 };
        preferences.AddThemeConstantOverride("h_separation", 18);
        preferences.AddThemeConstantOverride("v_separation", 10);
        content.AddChild(preferences);

        AddFormLabel(preferences, "Escala da interface");
        _uiScaleOption = new OptionButton { FocusMode = Control.FocusModeEnum.All };
        foreach (var scale in AvailableUiScales)
        {
            _uiScaleOption.AddItem($"{scale:P0}");
        }
        preferences.AddChild(_uiScaleOption);

        AddFormLabel(preferences, "Página inicial");
        _startupSectionOption = new OptionButton { FocusMode = Control.FocusModeEnum.All };
        foreach (var section in Enum.GetValues<ApplicationSection>())
        {
            _startupSectionOption.AddItem(SectionTitle(section));
        }
        preferences.AddChild(_startupSectionOption);

        AddFormLabel(preferences, "Confirmações de segurança");
        _confirmActionsOption = new CheckButton { Text = "Confirmar reinício e controle externo" };
        preferences.AddChild(_confirmActionsOption);

        AddFormLabel(preferences, "Boas-vindas");
        _showWelcomeOption = new CheckButton { Text = "Mostrar ao iniciar" };
        preferences.AddChild(_showWelcomeOption);

        var preferenceActions = new HBoxContainer();
        preferenceActions.AddThemeConstantOverride("separation", 10);
        content.AddChild(preferenceActions);
        var save = CreateButton("✓  SALVAR PREFERÊNCIAS", new Color(0.10f, 0.48f, 0.38f));
        save.Pressed += SavePreferenceFields;
        preferenceActions.AddChild(save);
        var welcome = CreateButton("?  MOSTRAR GUIA INICIAL", new Color(0.26f, 0.34f, 0.58f));
        welcome.Pressed += ShowWelcome;
        preferenceActions.AddChild(welcome);

        _preferencesFeedback = CreateBodyLabel(12, new Color(0.50f, 0.76f, 0.88f));
        content.AddChild(_preferencesFeedback);
        return page;
    }

    private void BuildWelcome(Control root)
    {
        _welcomeLayer = new Control
        {
            Name = "BoasVindas",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _welcomeLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_welcomeLayer);

        var shade = new ColorRect
        {
            Color = new Color(0.005f, 0.015f, 0.028f, 0.82f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _welcomeLayer.AddChild(shade);

        var panel = CreatePanel(
            new Color(0.025f, 0.065f, 0.105f, 0.99f),
            new Color(0.12f, 0.68f, 0.88f, 0.92f));
        panel.AnchorLeft = 0.5f;
        panel.AnchorRight = 0.5f;
        panel.AnchorTop = 0.5f;
        panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = -330f;
        panel.OffsetRight = 330f;
        panel.OffsetTop = -235f;
        panel.OffsetBottom = 235f;
        _welcomeLayer.AddChild(panel);

        var margin = CreateMargin(30, 26, 30, 26);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);

        var title = new Label { Text = "BEM-VINDO AO SIMULADOR INDUSTRIAL 3D" };
        title.AddThemeFontSizeOverride("font_size", 23);
        title.AddThemeColorOverride("font_color", new Color(0.82f, 0.95f, 1f));
        column.AddChild(title);
        var body = CreateBodyLabel(14, new Color(0.66f, 0.84f, 0.94f));
        body.Text =
            "Use o menu lateral para separar operação, edição, diagnóstico e conexões.\n\n" +
            "1  VISÃO GERAL — acompanha o processo completo\n" +
            "2  SIMULAÇÃO — maximiza a célula e seus controles\n" +
            "3  EDITOR 3D — cria e posiciona componentes\n" +
            "4  DIAGNÓSTICO — tags, eventos e métricas\n" +
            "5  CONEXÕES — configura Modbus TCP\n" +
            "6  AJUDA — atalhos e preferências";
        column.AddChild(body);
        _welcomeStartButton = CreateButton("▶  INICIAR PRIMEIRO CICLO", new Color(0.08f, 0.55f, 0.37f));
        _welcomeStartButton.Pressed += () =>
        {
            HideWelcome();
            NavigateTo(ApplicationSection.Simulation);
            RunSimulation();
        };
        column.AddChild(_welcomeStartButton);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        column.AddChild(row);
        var help = CreateButton("ABRIR AJUDA", new Color(0.18f, 0.36f, 0.58f));
        help.Pressed += () =>
        {
            HideWelcome();
            NavigateTo(ApplicationSection.Help);
        };
        row.AddChild(help);
        var dismiss = CreateButton("NÃO MOSTRAR NOVAMENTE", new Color(0.28f, 0.30f, 0.38f));
        dismiss.Pressed += () =>
        {
            SaveUiPreferences(_uiPreferences with { WelcomeDismissed = true });
            PopulatePreferenceFields();
            HideWelcome();
        };
        row.AddChild(dismiss);
    }

    private Control CreatePageRoot(string titleText, string subtitleText)
    {
        var root = new Control { Visible = false };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(scroll);
        var content = new VBoxContainer
        {
            Name = "Conteudo",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(content);
        var title = new Label { Text = titleText };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.82f, 0.95f, 1f));
        content.AddChild(title);
        var subtitle = CreateBodyLabel(13, new Color(0.45f, 0.69f, 0.82f));
        subtitle.Text = subtitleText;
        content.AddChild(subtitle);
        content.AddChild(CreateSeparator());
        return root;
    }

    private static Label CreateBodyLabel(int size, Color color)
    {
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static VBoxContainer GetPageContent(Control page) =>
        (VBoxContainer)(page.FindChild("Conteudo", recursive: true, owned: false) ??
                        throw new InvalidOperationException("Conteúdo da página não foi criado."));

    private static Label CreateDiagnosticCard(Container parent, string title)
    {
        var card = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        parent.AddChild(card);
        var heading = new Label { Text = title };
        heading.AddThemeFontSizeOverride("font_size", 13);
        heading.AddThemeColorOverride("font_color", new Color(0.30f, 0.85f, 1f));
        card.AddChild(heading);
        var value = CreateBodyLabel(13, new Color(0.72f, 0.86f, 0.94f));
        value.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        card.AddChild(value);
        return value;
    }

    private static void AddFormLabel(Container parent, string text)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color(0.54f, 0.74f, 0.86f));
        parent.AddChild(label);
    }

    private static SpinBox CreateIntegerSpinBox(double minimum, double maximum) => new()
    {
        MinValue = minimum,
        MaxValue = maximum,
        Step = 1d,
        AllowGreater = false,
        AllowLesser = false,
        UpdateOnTextChanged = true
    };

    private void NavigateTo(ApplicationSection section)
    {
        _activeSection = section;
        _navigationRail.SetActiveSection(section);

        _diagnosticsPage.Visible = section == ApplicationSection.Diagnostics;
        _connectionsPage.Visible = section == ApplicationSection.Connections;
        _helpPage.Visible = section == ApplicationSection.Help;
        _workspacePage.Visible = section is ApplicationSection.Diagnostics or ApplicationSection.Connections or ApplicationSection.Help;

        _statusPanel.Visible = section is ApplicationSection.Overview or ApplicationSection.Simulation or ApplicationSection.Editor;
        _axisPanel.Visible = section is ApplicationSection.Overview or ApplicationSection.Simulation;
        _diagnosticsPanel.Visible = section == ApplicationSection.Overview;
        _cameraHelpPanel.Visible = section is ApplicationSection.Overview or ApplicationSection.Simulation;

        if (section == ApplicationSection.Editor)
        {
            _navigationRail.SetExpanded(false);
            _sceneEditor.SetPanelLeftOffset(90f);
            _sceneEditor.SetActive(true);
        }
        else if (_sceneEditor.Active)
        {
            _sceneEditor.SetActive(false);
        }
    }

    private void HandleNavigationExpanded(bool expanded)
    {
        _axisPanel.OffsetLeft = expanded ? 258f : 90f;
        _axisPanel.OffsetRight = _axisPanel.OffsetLeft + 370f;
        _sceneEditor.SetPanelLeftOffset(expanded ? 252f : 90f);
    }

    private void HandleNavigationEditorState(bool active)
    {
        if (active)
        {
            _activeSection = ApplicationSection.Editor;
            _navigationRail.SetActiveSection(ApplicationSection.Editor);
            _navigationRail.SetExpanded(false);
            _sceneEditor.SetPanelLeftOffset(90f);
            _statusPanel.Visible = true;
            _axisPanel.Visible = false;
            _diagnosticsPanel.Visible = false;
            _cameraHelpPanel.Visible = false;
            _workspacePage.Visible = false;
        }
        else if (_activeSection == ApplicationSection.Editor)
        {
            NavigateTo(ApplicationSection.Simulation);
        }
    }

    private void UpdateApplicationNavigation()
    {
        if (!GodotObject.IsInstanceValid(_navigationRail))
        {
            return;
        }

        var running = _engine.State == SimuladorIndustrial.Core.Runtime.SimulationRunState.Running;
        _navigationRail.SetBadge(
            ApplicationSection.Simulation,
            running ? "ATIVA" : "PAUSA",
            running ? new Color(0.30f, 0.92f, 0.62f) : new Color(1f, 0.68f, 0.28f));
        _navigationRail.SetBadge(ApplicationSection.Diagnostics, _journal.Count.ToString(CultureInfo.InvariantCulture));

        var monitor = _modbusDriver?.Monitor;
        var connectionBadge = monitor is null
            ? "—"
            : monitor.State is ModbusConnectionState.Connected or ModbusConnectionState.Listening
                ? "ON"
                : monitor.State == ModbusConnectionState.Faulted ? "ERRO" : "OFF";
        _navigationRail.SetBadge(
            ApplicationSection.Connections,
            connectionBadge,
            connectionBadge == "ON" ? new Color(0.30f, 0.92f, 0.62f) : new Color(1f, 0.68f, 0.28f));

        if (_activeSection == ApplicationSection.Diagnostics)
        {
            var tags = _tags.GetAll();
            var good = tags.Count(tag => tag.Quality == TagQuality.Good);
            var metrics = _engine.Metrics;
            _diagnosticsSummary.Text = string.Create(
                CultureInfo.GetCultureInfo("pt-BR"),
                $"Estado: {_engine.State}   ·   Tick: {_engine.Clock.Tick:N0}   ·   Tags saudáveis: {good}/{tags.Count}   ·   Eventos: {_journal.Count:N0}\n" +
                $"Lógica: {metrics.LastLogicStepMilliseconds:F3} ms   ·   Média: {metrics.AverageLogicStepMilliseconds:F3} ms   ·   Máxima: {metrics.MaximumLogicStepMilliseconds:F3} ms   ·   Passos perdidos: {metrics.DroppedSteps:N0}");
            _diagnosticsTags.Text = _tagMonitorLabel.Text;
            _diagnosticsTimeline.Text = _timelineLabel.Text;
        }

        if (_activeSection == ApplicationSection.Connections)
        {
            _connectionSummary.Text = monitor is null
                ? "Nenhuma conexão Modbus configurada."
                : $"{monitor.Role} · {monitor.State} · {monitor.Endpoint} · Unit {monitor.UnitId}\n" +
                  $"Conexões ativas: {monitor.ActiveConnections} · Requisições: {monitor.TotalRequests:N0} · Falhas: {monitor.FailedRequests:N0}" +
                  (string.IsNullOrWhiteSpace(monitor.LastError) ? string.Empty : $"\nÚltimo erro: {monitor.LastError}");
            _connectionToggleButton.Text = _externalControlEnabled ? "■  PARAR MODBUS" : "⇄  INICIAR MODBUS";
            _connectionToggleButton.Disabled = _modbusOperationInProgress || _modbusDriver is null;
        }
    }

    private void PopulateConnectionFields(ConnectionDocument? connection)
    {
        if (connection is null)
        {
            _connectionRole.Select(0);
            _connectionHost.Text = "127.0.0.1";
            _connectionPort.Value = 5020;
            _connectionUnitId.Value = 1;
            return;
        }

        _connectionRole.Select(string.Equals(connection.Role, "client", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        _connectionHost.Text = connection.Host;
        _connectionPort.Value = connection.Port;
        _connectionUnitId.Value = connection.UnitId;
    }

    private async Task ApplyConnectionConfigurationAsync()
    {
        if (_modbusOperationInProgress)
        {
            return;
        }

        _connectionApplyButton.Disabled = true;
        _modbusOperationInProgress = true;
        _connectionFeedback.Text = "Validando configuração...";
        ModbusTcpDriver? replacementDriver = null;
        var previousDriver = _modbusDriver;
        var previousScene = _scene;
        var previousExternalControl = _externalControlEnabled;
        try
        {
            var existing = _scene.Connections.FirstOrDefault(IsModbusConnection);
            var replacement = new ConnectionDocument(
                existing?.Id ?? Guid.NewGuid(),
                existing?.Name ?? "Modbus TCP",
                "modbusTcp",
                _connectionRole.Selected == 1 ? "client" : "server",
                _connectionHost.Text.Trim(),
                checked((int)_connectionPort.Value),
                checked((int)_connectionUnitId.Value));
            var connections = _scene.Connections.Where(connection => !IsModbusConnection(connection)).Append(replacement).ToArray();
            var candidate = _scene with { Connections = connections };
            SceneValidator.ValidateAndThrow(candidate);
            replacementDriver = ModbusTcpDriver.FromConnection(replacement, _tags, _journal);

            await WaitForModbusPollAsync();
            _sceneEditor.Configure(candidate, _sceneEditor.SavePath);
            _sceneEditor.BindRuntimeTags(_tags, () => _engine.Clock.Tick);

            if (previousExternalControl && previousDriver is not null)
            {
                await previousDriver.StopAsync();
            }

            _modbusDriver = replacementDriver;
            replacementDriver = null;
            _scene = candidate;
            _externalControlEnabled = false;
            _controller.Enabled = true;

            if (previousDriver is not null)
            {
                try
                {
                    await previousDriver.DisposeAsync();
                }
                catch (Exception exception)
                {
                    GD.PushWarning($"A conexão anterior foi substituída, mas sua limpeza falhou: {exception.Message}");
                }
            }

            _connectionFeedback.Text = "Configuração aplicada e pronta para iniciar. Salve a cena no Editor 3D para reutilizá-la.";
            _connectionFeedback.AddThemeColorOverride("font_color", new Color(0.30f, 0.92f, 0.62f));
            _journal.Record(_engine.Clock.Tick, "modbus.configuration", "Configuração Modbus validada e aplicada pela interface.");
        }
        catch (Exception exception)
        {
            if (replacementDriver is not null)
            {
                await replacementDriver.DisposeAsync();
            }

            if (ReferenceEquals(_modbusDriver, previousDriver))
            {
                try
                {
                    _sceneEditor.Configure(previousScene, _sceneEditor.SavePath);
                    _sceneEditor.BindRuntimeTags(_tags, () => _engine.Clock.Tick);
                    if (previousExternalControl && previousDriver is not null && !previousDriver.Monitor.IsAvailable)
                    {
                        await previousDriver.StartAsync();
                    }
                }
                catch (Exception restoreException)
                {
                    GD.PushError($"Falha ao restaurar a conexão anterior: {restoreException}");
                }
            }

            _connectionFeedback.Text = $"Configuração rejeitada: {exception.Message}";
            _connectionFeedback.AddThemeColorOverride("font_color", new Color(1f, 0.42f, 0.34f));
        }
        finally
        {
            _modbusOperationInProgress = false;
            _connectionApplyButton.Disabled = false;
        }
    }

    private async Task WaitForModbusPollAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_modbusPollInProgress && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        if (_modbusPollInProgress)
        {
            throw new TimeoutException("A sincronização Modbus atual não terminou a tempo. Tente novamente.");
        }
    }

    private void PopulatePreferenceFields()
    {
        var scaleIndex = Array.IndexOf(AvailableUiScales, AvailableUiScales.MinBy(scale => Math.Abs(scale - _uiPreferences.UiScale)));
        _uiScaleOption.Select(Math.Max(0, scaleIndex));
        _startupSectionOption.Select((int)_uiPreferences.StartupSection);
        _confirmActionsOption.ButtonPressed = _uiPreferences.ConfirmDestructiveActions;
        _showWelcomeOption.ButtonPressed = !_uiPreferences.WelcomeDismissed;
    }

    private void SavePreferenceFields()
    {
        var selectedScale = AvailableUiScales[Math.Clamp(_uiScaleOption.Selected, 0, AvailableUiScales.Length - 1)];
        var selectedSection = (ApplicationSection)Math.Clamp(
            _startupSectionOption.Selected,
            0,
            Enum.GetValues<ApplicationSection>().Length - 1);
        SaveUiPreferences(_uiPreferences with
        {
            UiScale = selectedScale,
            StartupSection = selectedSection,
            ConfirmDestructiveActions = _confirmActionsOption.ButtonPressed,
            WelcomeDismissed = !_showWelcomeOption.ButtonPressed
        });
        _preferencesFeedback.Text = "Preferências salvas.";
        _preferencesFeedback.AddThemeColorOverride("font_color", new Color(0.30f, 0.92f, 0.62f));
    }

    private void ShowWelcome()
    {
        _welcomeLayer.Visible = true;
        _welcomeLayer.MoveToFront();
        _welcomeStartButton.GrabFocus();
    }

    private void HideWelcome()
    {
        _welcomeLayer.Visible = false;
    }

    private static bool IsModbusConnection(ConnectionDocument connection) =>
        string.Equals(connection.Driver, "modbusTcp", StringComparison.OrdinalIgnoreCase);

    private static string SectionTitle(ApplicationSection section) => section switch
    {
        ApplicationSection.Overview => "Visão geral",
        ApplicationSection.Simulation => "Simulação",
        ApplicationSection.Editor => "Editor 3D",
        ApplicationSection.Diagnostics => "Diagnóstico",
        ApplicationSection.Connections => "Conexões",
        ApplicationSection.Help => "Ajuda",
        _ => section.ToString()
    };
}

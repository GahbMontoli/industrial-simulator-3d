using Godot;
using SimuladorIndustrial.Core.Scenarios;

namespace SimuladorIndustrial.GodotApp;

public sealed partial class FillingCellView : Node3D
{
    private const float ConveyorStartX = -2.6f;
    private const float ConveyorEndX = 2.6f;
    private const float SlatSpacing = 0.31f;

    private readonly List<MeshInstance3D> _slats = [];
    private Node3D _containerRoot = null!;
    private MeshInstance3D _liquid = null!;
    private MeshInstance3D _sensorLed = null!;
    private MeshInstance3D _sensorBeam = null!;
    private MeshInstance3D _gateBlade = null!;
    private MeshInstance3D _liquidStream = null!;
    private StandardMaterial3D _sensorLedMaterial = null!;
    private StandardMaterial3D _sensorBeamMaterial = null!;
    private StandardMaterial3D _streamMaterial = null!;
    private StandardMaterial3D _operatorLightMaterial = null!;
    private MeshInstance3D _removerPusher = null!;
    private Label3D _operatorDisplay = null!;
    private bool _wasContainerVisible;
    private double _animationTime;

    public override void _Ready()
    {
        Name = "CelulaDeEnvase3D";
        BuildConveyor();
        BuildSource();
        BuildContainer();
        BuildSensor();
        BuildGate();
        BuildTankAndValves();
        BuildFillingHead();
        BuildRemover();
        BuildOperatorStation();
        AddStationLabels();
    }

    public void ApplyState(
        AnimatedFillingLineSystem line,
        bool gateClosed,
        bool valveOpen,
        double delta)
    {
        ArgumentNullException.ThrowIfNull(line);
        _animationTime += delta;
        AnimateBelt(line.BeltTravelMeters);
        AnimateContainer(line, delta);
        AnimateGate(gateClosed, delta);
        AnimateSensor(line.SensorActive);
        AnimateAuxiliaries(line, delta);

        var filling = line.SensorActive && gateClosed && valveOpen && line.FillRatio < 1f;
        _liquidStream.Visible = filling;
        if (filling)
        {
            var pulse = 0.90f + 0.10f * Mathf.Sin((float)_animationTime * 18f);
            _liquidStream.Scale = new Vector3(pulse, 1f, pulse);
            _streamMaterial.EmissionEnergyMultiplier = 2.1f + 0.5f * pulse;
        }
    }

    private void BuildConveyor()
    {
        var root = new Node3D { Name = "Esteira" };
        AddChild(root);

        var steel = ProceduralGeometry.CreateMaterial(
            new Color(0.30f, 0.36f, 0.43f),
            metallic: 0.72f,
            roughness: 0.28f);
        var darkSteel = ProceduralGeometry.CreateMaterial(
            new Color(0.10f, 0.14f, 0.19f),
            metallic: 0.55f,
            roughness: 0.42f);
        var belt = ProceduralGeometry.CreateMaterial(
            new Color(0.055f, 0.075f, 0.095f),
            metallic: 0.08f,
            roughness: 0.86f);
        var accent = ProceduralGeometry.CreateMaterial(
            new Color(0.13f, 0.69f, 0.91f),
            metallic: 0.25f,
            roughness: 0.32f,
            emission: new Color(0.02f, 0.24f, 0.40f));

        ProceduralGeometry.AddBox(
            root,
            "Correia",
            new Vector3(5.3f, 0.15f, 1.08f),
            new Vector3(0f, 0.56f, 0f),
            belt);
        ProceduralGeometry.AddBox(root, "LongarinaE", new Vector3(5.45f, 0.19f, 0.09f), new Vector3(0f, 0.48f, -0.61f), steel);
        ProceduralGeometry.AddBox(root, "LongarinaD", new Vector3(5.45f, 0.19f, 0.09f), new Vector3(0f, 0.48f, 0.61f), steel);
        ProceduralGeometry.AddBox(root, "FaixaLuminosaE", new Vector3(5.2f, 0.025f, 0.025f), new Vector3(0f, 0.59f, -0.665f), accent);
        ProceduralGeometry.AddBox(root, "FaixaLuminosaD", new Vector3(5.2f, 0.025f, 0.025f), new Vector3(0f, 0.59f, 0.665f), accent);

        for (var index = 0; index < 18; index++)
        {
            var slat = ProceduralGeometry.AddBox(
                root,
                $"Travessa{index:00}",
                new Vector3(0.055f, 0.021f, 1.01f),
                new Vector3(ConveyorStartX + index * SlatSpacing, 0.646f, 0f),
                darkSteel);
            _slats.Add(slat);
        }

        foreach (var x in new[] { -2.1f, -1.05f, 0f, 1.05f, 2.1f })
        {
            ProceduralGeometry.AddBox(root, $"PeE{x}", new Vector3(0.12f, 0.48f, 0.12f), new Vector3(x, 0.24f, -0.49f), steel);
            ProceduralGeometry.AddBox(root, $"PeD{x}", new Vector3(0.12f, 0.48f, 0.12f), new Vector3(x, 0.24f, 0.49f), steel);
            ProceduralGeometry.AddBox(root, $"BaseE{x}", new Vector3(0.34f, 0.06f, 0.24f), new Vector3(x, 0.03f, -0.49f), darkSteel);
            ProceduralGeometry.AddBox(root, $"BaseD{x}", new Vector3(0.34f, 0.06f, 0.24f), new Vector3(x, 0.03f, 0.49f), darkSteel);
        }

        for (var x = -2.42f; x <= 2.42f; x += 0.44f)
        {
            ProceduralGeometry.AddCylinder(
                root,
                $"Rolo{x:F2}",
                0.075f,
                1.06f,
                new Vector3(x, 0.59f, 0f),
                steel,
                new Vector3(90f, 0f, 0f),
                radialSegments: 20);
        }
    }

    private void BuildSource()
    {
        var root = new Node3D { Name = "Emissor" };
        AddChild(root);
        var steel = ProceduralGeometry.CreateMaterial(new Color(0.27f, 0.34f, 0.42f), metallic: 0.65f, roughness: 0.30f);
        var panel = ProceduralGeometry.CreateMaterial(new Color(0.06f, 0.13f, 0.20f), metallic: 0.25f, roughness: 0.48f);
        var glow = ProceduralGeometry.CreateMaterial(
            new Color(0.12f, 0.80f, 0.97f),
            unshaded: true,
            emission: new Color(0.08f, 0.62f, 0.92f));

        ProceduralGeometry.AddBox(root, "Coluna", new Vector3(0.72f, 1.15f, 1.05f), new Vector3(-2.48f, 1.15f, 0f), panel);
        ProceduralGeometry.AddBox(root, "Moldura", new Vector3(0.82f, 0.10f, 1.15f), new Vector3(-2.48f, 1.69f, 0f), steel);
        ProceduralGeometry.AddBox(root, "Boca", new Vector3(0.30f, 0.34f, 0.58f), new Vector3(-2.17f, 0.91f, 0f), steel);
        ProceduralGeometry.AddBox(root, "Indicador", new Vector3(0.035f, 0.62f, 0.55f), new Vector3(-2.105f, 1.30f, 0f), glow);
        ProceduralGeometry.AddWorldLabel(root, "FONTE", new Vector3(-2.48f, 1.88f, 0f), new Color(0.72f, 0.88f, 1f));
    }

    private void BuildContainer()
    {
        _containerRoot = new Node3D
        {
            Name = "Recipiente",
            Position = new Vector3(-2.2f, 0.91f, 0f)
        };
        AddChild(_containerRoot);

        var glass = ProceduralGeometry.CreateMaterial(
            new Color(0.55f, 0.83f, 0.94f, 0.24f),
            metallic: 0.12f,
            roughness: 0.12f,
            transparent: true);
        var rim = ProceduralGeometry.CreateMaterial(new Color(0.66f, 0.78f, 0.86f), metallic: 0.80f, roughness: 0.20f);
        var liquid = ProceduralGeometry.CreateMaterial(
            new Color(0.04f, 0.48f, 0.96f, 0.82f),
            roughness: 0.12f,
            transparent: true,
            emission: new Color(0.01f, 0.11f, 0.30f));

        ProceduralGeometry.AddCylinder(_containerRoot, "Corpo", 0.22f, 0.52f, Vector3.Zero, glass, radialSegments: 40);
        ProceduralGeometry.AddCylinder(_containerRoot, "Base", 0.225f, 0.035f, new Vector3(0f, -0.26f, 0f), rim, radialSegments: 40);
        ProceduralGeometry.AddCylinder(_containerRoot, "Aro", 0.225f, 0.035f, new Vector3(0f, 0.26f, 0f), rim, radialSegments: 40);
        _liquid = ProceduralGeometry.AddCylinder(
            _containerRoot,
            "Liquido",
            0.19f,
            0.43f,
            new Vector3(0f, -0.225f, 0f),
            liquid,
            radialSegments: 40);
        _liquid.Scale = new Vector3(1f, 0.01f, 1f);
    }

    private void BuildSensor()
    {
        var root = new Node3D { Name = "Sensor" };
        AddChild(root);
        var housing = ProceduralGeometry.CreateMaterial(new Color(0.12f, 0.16f, 0.21f), metallic: 0.48f, roughness: 0.38f);
        _sensorLedMaterial = ProceduralGeometry.CreateMaterial(
            new Color(0.40f, 0.07f, 0.08f),
            unshaded: true,
            emission: new Color(0.28f, 0.01f, 0.01f));
        _sensorBeamMaterial = ProceduralGeometry.CreateMaterial(
            new Color(0.92f, 0.10f, 0.12f, 0.18f),
            unshaded: true,
            transparent: true,
            emission: new Color(0.50f, 0.01f, 0.01f));

        ProceduralGeometry.AddBox(root, "Suporte", new Vector3(0.10f, 0.66f, 0.10f), new Vector3(0.60f, 0.75f, 0.68f), housing);
        ProceduralGeometry.AddBox(root, "Cabecote", new Vector3(0.18f, 0.18f, 0.26f), new Vector3(0.60f, 1.02f, 0.57f), housing);
        _sensorLed = ProceduralGeometry.AddSphere(root, "Led", 0.052f, new Vector3(0.60f, 1.07f, 0.43f), _sensorLedMaterial);
        _sensorBeam = ProceduralGeometry.AddBox(
            root,
            "Feixe",
            new Vector3(0.035f, 0.035f, 1.02f),
            new Vector3(0.60f, 0.93f, 0.04f),
            _sensorBeamMaterial);
        ProceduralGeometry.AddWorldLabel(root, "SENSOR", new Vector3(0.60f, 1.32f, 0.64f), new Color(0.70f, 0.84f, 0.95f), 34);
    }

    private void BuildGate()
    {
        var root = new Node3D { Name = "Bloqueador" };
        AddChild(root);
        var frame = ProceduralGeometry.CreateMaterial(new Color(0.24f, 0.29f, 0.35f), metallic: 0.70f, roughness: 0.26f);
        var safety = ProceduralGeometry.CreateMaterial(
            new Color(1f, 0.53f, 0.06f),
            metallic: 0.25f,
            roughness: 0.35f,
            emission: new Color(0.25f, 0.06f, 0.00f));

        ProceduralGeometry.AddBox(root, "Atuador", new Vector3(0.24f, 0.38f, 0.28f), new Vector3(0.86f, 0.92f, -0.73f), frame);
        _gateBlade = ProceduralGeometry.AddBox(
            root,
            "Lamina",
            new Vector3(0.075f, 0.62f, 1.12f),
            new Vector3(0.86f, 1.00f, 0f),
            safety);
        ProceduralGeometry.AddWorldLabel(root, "STOP", new Vector3(0.86f, 1.62f, -0.70f), new Color(1f, 0.66f, 0.22f), 32);
    }

    private void BuildFillingHead()
    {
        var root = new Node3D { Name = "CabecoteDeEnvase" };
        AddChild(root);
        var steel = ProceduralGeometry.CreateMaterial(new Color(0.34f, 0.42f, 0.50f), metallic: 0.82f, roughness: 0.20f);
        var dark = ProceduralGeometry.CreateMaterial(new Color(0.08f, 0.12f, 0.17f), metallic: 0.46f, roughness: 0.38f);
        _streamMaterial = ProceduralGeometry.CreateMaterial(
            new Color(0.03f, 0.58f, 1f, 0.76f),
            unshaded: true,
            transparent: true,
            emission: new Color(0.01f, 0.32f, 0.80f));

        ProceduralGeometry.AddBox(root, "Coluna", new Vector3(0.15f, 1.62f, 0.15f), new Vector3(0.60f, 1.30f, -0.78f), steel);
        ProceduralGeometry.AddBox(root, "Braco", new Vector3(0.18f, 0.18f, 0.86f), new Vector3(0.60f, 2.02f, -0.39f), steel);
        ProceduralGeometry.AddCylinder(root, "Bico", 0.09f, 0.42f, new Vector3(0.60f, 1.78f, 0f), dark, radialSegments: 32);
        _liquidStream = ProceduralGeometry.AddCylinder(
            root,
            "Fluxo",
            0.045f,
            0.62f,
            new Vector3(0.60f, 1.27f, 0f),
            _streamMaterial,
            radialSegments: 24);
        _liquidStream.Visible = false;
        ProceduralGeometry.AddWorldLabel(root, "ENVASE", new Vector3(0.60f, 2.30f, -0.28f), new Color(0.70f, 0.88f, 1f), 36);
    }

    private void BuildTankAndValves()
    {
        var tank = new Node3D { Name = "Tanque", Position = new Vector3(0.60f, 0f, -1.58f) };
        AddChild(tank);
        var steel = ProceduralGeometry.CreateMaterial(new Color(0.34f, 0.42f, 0.50f), metallic: 0.84f, roughness: 0.20f);
        var dark = ProceduralGeometry.CreateMaterial(new Color(0.07f, 0.11f, 0.15f), metallic: 0.50f, roughness: 0.36f);
        var fluid = ProceduralGeometry.CreateMaterial(
            new Color(0.04f, 0.45f, 0.88f, 0.48f),
            roughness: 0.14f,
            transparent: true,
            emission: new Color(0.01f, 0.10f, 0.26f));
        var safety = ProceduralGeometry.CreateMaterial(
            new Color(1f, 0.48f, 0.05f),
            metallic: 0.22f,
            roughness: 0.32f,
            emission: new Color(0.20f, 0.04f, 0f));

        ProceduralGeometry.AddCylinder(tank, "Reservatorio", 0.43f, 1.05f, new Vector3(0f, 1.34f, 0f), steel, radialSegments: 40);
        ProceduralGeometry.AddCylinder(tank, "VisorNivel", 0.34f, 0.72f, new Vector3(0f, 1.34f, 0.012f), fluid, radialSegments: 40);
        ProceduralGeometry.AddCylinder(tank, "Tampa", 0.47f, 0.08f, new Vector3(0f, 1.90f, 0f), dark, radialSegments: 40);
        foreach (var x in new[] { -0.28f, 0.28f })
        {
            ProceduralGeometry.AddBox(tank, $"Pe{x}", new Vector3(0.10f, 0.72f, 0.10f), new Vector3(x, 0.48f, 0f), steel);
        }

        ProceduralGeometry.AddCylinder(tank, "LinhaDeEnchimento", 0.055f, 0.82f, new Vector3(0f, 1.78f, 0.43f), steel, new Vector3(90f, 0f, 0f), 24);
        ProceduralGeometry.AddSphere(tank, "ValvulaEnchimento", 0.13f, new Vector3(0f, 1.78f, 0.80f), safety);
        ProceduralGeometry.AddBox(tank, "ManoplaEnchimento", new Vector3(0.34f, 0.045f, 0.07f), new Vector3(0f, 1.94f, 0.80f), dark);

        var discharge = new Node3D { Name = "ValvulaDescarga", Position = new Vector3(0f, 0.88f, -0.43f) };
        tank.AddChild(discharge);
        ProceduralGeometry.AddCylinder(discharge, "Tubulacao", 0.065f, 0.52f, new Vector3(0f, 0f, -0.22f), steel, new Vector3(90f, 0f, 0f), 24);
        ProceduralGeometry.AddSphere(discharge, "CorpoValvula", 0.14f, new Vector3(0f, 0f, -0.48f), safety);
        ProceduralGeometry.AddBox(discharge, "ManoplaDescarga", new Vector3(0.32f, 0.045f, 0.07f), new Vector3(0f, 0.16f, -0.48f), dark);
        ProceduralGeometry.AddWorldLabel(tank, "TANQUE  •  VÁLVULAS V1/V2", new Vector3(0f, 2.16f, 0f), new Color(0.70f, 0.88f, 1f), 27);
    }

    private void BuildRemover()
    {
        var root = new Node3D { Name = "Removedor", Position = new Vector3(2.82f, 0f, 0f) };
        AddChild(root);
        var steel = ProceduralGeometry.CreateMaterial(new Color(0.30f, 0.37f, 0.44f), metallic: 0.75f, roughness: 0.25f);
        var dark = ProceduralGeometry.CreateMaterial(new Color(0.06f, 0.10f, 0.15f), metallic: 0.40f, roughness: 0.44f);
        var safety = ProceduralGeometry.CreateMaterial(
            new Color(0.96f, 0.38f, 0.05f),
            metallic: 0.18f,
            roughness: 0.38f,
            emission: new Color(0.18f, 0.03f, 0f));

        ProceduralGeometry.AddBox(root, "Coluna", new Vector3(0.15f, 1.18f, 0.15f), new Vector3(0f, 1.14f, -0.78f), steel);
        ProceduralGeometry.AddBox(root, "Atuador", new Vector3(0.42f, 0.24f, 0.25f), new Vector3(0f, 1.18f, -0.64f), dark);
        _removerPusher = ProceduralGeometry.AddBox(root, "Empurrador", new Vector3(0.38f, 0.38f, 0.10f), new Vector3(0f, 1.05f, -0.43f), safety);
        ProceduralGeometry.AddBox(root, "CaixaColeta", new Vector3(0.76f, 0.56f, 0.70f), new Vector3(0f, 0.30f, 1.10f), dark);
        ProceduralGeometry.AddWorldLabel(root, "REMOVEDOR", new Vector3(0f, 1.88f, -0.55f), new Color(1f, 0.66f, 0.25f), 30);
    }

    private void BuildOperatorStation()
    {
        var root = new Node3D { Name = "EstacaoOperador", Position = new Vector3(1.72f, 0f, 1.42f) };
        AddChild(root);
        var steel = ProceduralGeometry.CreateMaterial(new Color(0.26f, 0.32f, 0.39f), metallic: 0.72f, roughness: 0.27f);
        var console = ProceduralGeometry.CreateMaterial(new Color(0.055f, 0.10f, 0.15f), metallic: 0.30f, roughness: 0.43f);
        var button = ProceduralGeometry.CreateMaterial(
            new Color(0.95f, 0.16f, 0.12f),
            unshaded: true,
            emission: new Color(0.62f, 0.02f, 0.01f));
        _operatorLightMaterial = ProceduralGeometry.CreateMaterial(
            new Color(0.14f, 0.38f, 0.18f),
            unshaded: true,
            emission: new Color(0.02f, 0.18f, 0.04f));

        ProceduralGeometry.AddBox(root, "Pedestal", new Vector3(0.55f, 0.88f, 0.48f), new Vector3(0f, 0.44f, 0f), steel);
        ProceduralGeometry.AddBox(root, "Painel", new Vector3(0.62f, 0.46f, 0.16f), new Vector3(0f, 1.03f, -0.06f), console, new Vector3(-16f, 0f, 0f));
        ProceduralGeometry.AddCylinder(root, "Botao", 0.075f, 0.055f, new Vector3(-0.15f, 1.10f, 0.04f), button, new Vector3(90f, 0f, 0f), 24);
        ProceduralGeometry.AddSphere(root, "Luz", 0.075f, new Vector3(0.16f, 1.12f, 0.03f), _operatorLightMaterial);
        _operatorDisplay = ProceduralGeometry.AddWorldLabel(root, "0,000 L", new Vector3(0f, 1.32f, -0.02f), new Color(0.30f, 0.90f, 1f), 25, 0.0028f);
        ProceduralGeometry.AddWorldLabel(root, "IHM OPERADOR", new Vector3(0f, 1.55f, 0f), new Color(0.70f, 0.88f, 1f), 25);
    }

    private void AnimateAuxiliaries(AnimatedFillingLineSystem line, double delta)
    {
        var removerActive = line.ContainerVisible && line.ContainerX > 2.15d;
        var targetZ = removerActive ? -0.05f : -0.43f;
        var smoothing = 1f - Mathf.Exp(-9f * (float)delta);
        var pusherPosition = _removerPusher.Position;
        pusherPosition.Z = Mathf.Lerp(pusherPosition.Z, targetZ, smoothing);
        _removerPusher.Position = pusherPosition;

        var light = line.SensorActive
            ? new Color(0.18f, 1f, 0.36f)
            : new Color(0.12f, 0.34f, 0.16f);
        _operatorLightMaterial.AlbedoColor = light;
        _operatorLightMaterial.Emission = light;
        _operatorDisplay.Text = $"{line.VolumeLiters:F3} L";
    }

    private void AddStationLabels()
    {
        ProceduralGeometry.AddWorldLabel(this, "-2 m", new Vector3(-2f, 0.08f, -1.05f), new Color(0.55f, 0.70f, 0.82f), 30);
        ProceduralGeometry.AddWorldLabel(this, "0 m", new Vector3(0f, 0.08f, -1.05f), new Color(0.55f, 0.70f, 0.82f), 30);
        ProceduralGeometry.AddWorldLabel(this, "+2 m", new Vector3(2f, 0.08f, -1.05f), new Color(0.55f, 0.70f, 0.82f), 30);
    }

    private void AnimateBelt(double beltTravelMeters)
    {
        var length = ConveyorEndX - ConveyorStartX;
        var offset = (float)(beltTravelMeters % SlatSpacing);
        for (var index = 0; index < _slats.Count; index++)
        {
            var x = ConveyorStartX + index * SlatSpacing + offset;
            while (x > ConveyorEndX)
            {
                x -= length;
            }

            _slats[index].Position = new Vector3(x, 0.646f, 0f);
        }
    }

    private void AnimateContainer(AnimatedFillingLineSystem line, double delta)
    {
        _containerRoot.Visible = line.ContainerVisible;
        if (line.ContainerVisible)
        {
            var target = new Vector3((float)line.ContainerX, 0.91f, 0f);
            if (!_wasContainerVisible || Mathf.Abs(_containerRoot.Position.X - target.X) > 2f)
            {
                _containerRoot.Position = target;
            }
            else
            {
                var smoothing = 1f - Mathf.Exp(-14f * (float)delta);
                _containerRoot.Position = _containerRoot.Position.Lerp(target, smoothing);
            }
        }

        var fill = Mathf.Clamp(line.FillRatio, 0.001f, 1f);
        _liquid.Scale = new Vector3(1f, fill, 1f);
        _liquid.Position = new Vector3(0f, -0.225f + 0.215f * fill, 0f);
        _wasContainerVisible = line.ContainerVisible;
    }

    private void AnimateGate(bool gateClosed, double delta)
    {
        var targetY = gateClosed ? 1.00f : 1.58f;
        var smoothing = 1f - Mathf.Exp(-10f * (float)delta);
        var position = _gateBlade.Position;
        position.Y = Mathf.Lerp(position.Y, targetY, smoothing);
        _gateBlade.Position = position;
    }

    private void AnimateSensor(bool active)
    {
        var ledColor = active
            ? new Color(0.18f, 1f, 0.36f)
            : new Color(0.62f, 0.08f, 0.09f);
        var beamColor = active
            ? new Color(0.16f, 1f, 0.42f, 0.32f)
            : new Color(0.92f, 0.10f, 0.12f, 0.16f);
        _sensorLedMaterial.AlbedoColor = ledColor;
        _sensorLedMaterial.Emission = ledColor;
        _sensorBeamMaterial.AlbedoColor = beamColor;
        _sensorBeamMaterial.Emission = active
            ? new Color(0.06f, 0.72f, 0.20f)
            : new Color(0.50f, 0.01f, 0.01f);
        _sensorLed.Scale = active ? Vector3.One * 1.18f : Vector3.One;
        _sensorBeam.Visible = true;
    }
}

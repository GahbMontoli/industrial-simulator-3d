using System.Text.Json;
using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.Core.Components;

public static class BuiltInComponentCatalog
{
    public const string PackageId = "org.simuladorindustrial.builtin";
    public const string PackageVersion = "1.0.0";

    public static IReadOnlyList<ComponentDefinition> Components { get; } =
    [
        Create("source.generator.v1", "Gerador de recipientes", "Fluxo", "#2979B8", 0.75, 1.10, 0.75),
        Create("source.container.v1", "Fonte da cena de envase", "Fluxo", "#2979B8", 0.75, 1.10, 0.75),
        Create("sink.remover.v1", "Removedor", "Fluxo", "#315F84", 0.75, 0.80, 0.75),
        Create("conveyor.straight.v1", "Esteira reta", "Transporte", "#2E9EE6", 1.50, 0.20, 0.70,
            ports: [Port("running", "Em movimento", ComponentPortDirection.Input, ComponentPortDataType.Bool)]),
        Create("sensor.diffuse.v1", "Sensor difuso", "Sensores", "#F7761A", 0.42, 0.52, 0.42,
            ports: [Port("detected", "Objeto detectado", ComponentPortDirection.Output, ComponentPortDataType.Bool)]),
        Create("actuator.stop-gate.v1", "Bloqueador pneumático", "Atuadores", "#D93645", 0.42, 0.52, 0.42,
            ports: [Port("closed", "Fechado", ComponentPortDirection.Input, ComponentPortDataType.Bool)]),
        Create("process.filling-head.v1", "Cabeçote de envase", "Processo", "#5B8EAD", 0.42, 0.72, 0.42),
        Create("process.tank.v1", "Tanque", "Processo", "#3589C8", 0.75, 1.10, 0.75,
            ports:
            [
                Port("fill", "Encher", ComponentPortDirection.Input, ComponentPortDataType.Bool),
                Port("drain", "Drenar", ComponentPortDirection.Input, ComponentPortDataType.Bool),
                Port("level", "Nível", ComponentPortDirection.Output, ComponentPortDataType.Float32)
            ]),
        Create("actuator.fill-valve.v1", "Válvula de enchimento", "Atuadores", "#E33A49", 0.42, 0.52, 0.42,
            ports: [Port("open", "Abrir", ComponentPortDirection.Input, ComponentPortDataType.Bool)]),
        Create("actuator.discharge-valve.v1", "Válvula de descarga", "Atuadores", "#BD3040", 0.42, 0.52, 0.42,
            ports: [Port("open", "Abrir", ComponentPortDirection.Input, ComponentPortDataType.Bool)]),
        Create("operator.button.v1", "Botão", "Operação", "#D7B12E", 0.42, 0.52, 0.42,
            ports: [Port("pressed", "Pressionado", ComponentPortDirection.Output, ComponentPortDataType.Bool)]),
        Create("indicator.light.v1", "Luz piloto", "Sinalização", "#48E58A", 0.42, 0.52, 0.42,
            ports: [Port("active", "Ativa", ComponentPortDirection.Input, ComponentPortDataType.Bool)]),
        Create("indicator.display.v1", "Display", "Sinalização", "#36B980", 0.80, 0.50, 0.18,
            ports: [Port("value", "Valor", ComponentPortDirection.Input, ComponentPortDataType.Float32)])
    ];

    private static ComponentDefinition Create(
        string type,
        string displayName,
        string category,
        string color,
        double width,
        double height,
        double depth,
        IReadOnlyList<ComponentPortDefinition>? ports = null,
        IReadOnlyList<ComponentParameterDefinition>? parameters = null)
    {
        return new ComponentDefinition(
            type,
            displayName,
            category,
            $"Componente nativo: {displayName}.",
            new ComponentVisualDefinition(
                ComponentVisualKind.Box,
                new Vector3Document(width, height, depth),
                color),
            ports ?? [],
            parameters ?? []);
    }

    private static ComponentPortDefinition Port(
        string name,
        string displayName,
        ComponentPortDirection direction,
        ComponentPortDataType dataType) =>
        new(name, displayName, direction, dataType);

    public static ComponentParameterDefinition Parameter<T>(
        string name,
        string displayName,
        ComponentParameterType dataType,
        T defaultValue,
        bool required = false,
        double? minimum = null,
        double? maximum = null) =>
        new(
            name,
            displayName,
            dataType,
            required,
            JsonSerializer.SerializeToElement(defaultValue),
            minimum,
            maximum);
}

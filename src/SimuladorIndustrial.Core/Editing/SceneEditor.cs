using System.Text.Json;
using SimuladorIndustrial.Core.Persistence;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Editing;

/// <summary>
/// Edits the component layer of a validated scene while preserving its scene, tag and
/// connection contracts. Every mutation is validated before it is committed.
/// </summary>
public sealed class SceneEditor
{
    public const int DefaultHistoryLimit = 100;
    public const int MaximumHistoryLimit = 1_000;

    private readonly int _schemaVersion;
    private readonly Guid _sceneId;
    private readonly string _name;
    private readonly SimulationSettings _simulation;
    private readonly TagDefinition[] _tags;
    private readonly ConnectionDocument[] _connections;
    private readonly int _historyLimit;
    private readonly List<EditorCommand> _undoHistory = [];
    private readonly List<EditorCommand> _redoHistory = [];

    private ComponentDocument[] _components;
    private Guid? _selectedComponentId;
    private long _currentStateId;
    private long _savedStateId;
    private long _nextStateId = 1;

    public SceneEditor(SceneDocument scene, int historyLimit = DefaultHistoryLimit)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (historyLimit is < 1 or > MaximumHistoryLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(historyLimit),
                $"History limit must be between 1 and {MaximumHistoryLimit}.");
        }

        SceneValidator.ValidateAndThrow(scene);

        _schemaVersion = scene.SchemaVersion;
        _sceneId = scene.SceneId;
        _name = scene.Name;
        _simulation = scene.Simulation;
        _components = CloneComponents(scene.Components);
        _tags = scene.Tags.ToArray();
        _connections = scene.Connections.ToArray();
        _historyLimit = historyLimit;
    }

    public Guid? SelectedComponentId => _selectedComponentId;

    public bool IsDirty => _currentStateId != _savedStateId;

    public bool CanUndo => _undoHistory.Count != 0;

    public bool CanRedo => _redoHistory.Count != 0;

    public int UndoCount => _undoHistory.Count;

    public int RedoCount => _redoHistory.Count;

    public IReadOnlyList<ComponentDocument> Components => CloneComponents(_components);

    public void SelectComponent(Guid componentId)
    {
        GetComponentIndex(componentId, _components);
        _selectedComponentId = componentId;
    }

    public void ClearSelection() => _selectedComponentId = null;

    public ComponentDocument GetComponent(Guid componentId)
    {
        var index = GetComponentIndex(componentId, _components);
        return CloneComponent(_components[index]);
    }

    public Guid AddComponent(ComponentDocument component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var componentCopy = CloneComponent(component);

        Execute(
            "Add component",
            (components, _) =>
            {
                components.Add(componentCopy);
                return componentCopy.Id;
            });

        return componentCopy.Id;
    }

    public Guid AddComponent(
        string type,
        TransformDocument transform,
        IReadOnlyDictionary<string, JsonElement>? parameters = null,
        Guid? componentId = null)
    {
        var id = componentId ?? Guid.NewGuid();
        var component = new ComponentDocument(
            id,
            type,
            transform,
            parameters ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal));

        return AddComponent(component);
    }

    public void MoveComponent(Guid componentId, Vector3Document position)
    {
        ArgumentNullException.ThrowIfNull(position);
        Execute(
            "Move component",
            (components, selection) =>
            {
                var index = GetComponentIndex(componentId, components);
                var component = components[index];
                components[index] = component with
                {
                    Transform = component.Transform with { Position = position }
                };
                return selection;
            });
    }

    public void RotateComponent(Guid componentId, Vector3Document rotationDegrees)
    {
        ArgumentNullException.ThrowIfNull(rotationDegrees);
        Execute(
            "Rotate component",
            (components, selection) =>
            {
                var index = GetComponentIndex(componentId, components);
                var component = components[index];
                components[index] = component with
                {
                    Transform = component.Transform with { RotationDegrees = rotationDegrees }
                };
                return selection;
            });
    }

    public Guid DuplicateComponent(
        Guid componentId,
        Guid? duplicateId = null,
        Vector3Document? positionOffset = null)
    {
        var id = duplicateId ?? Guid.NewGuid();
        var offset = positionOffset ?? new Vector3Document(0, 0, 0);

        Execute(
            "Duplicate component",
            (components, _) =>
            {
                var source = components[GetComponentIndex(componentId, components)];
                var sourcePosition = source.Transform.Position;
                var duplicate = CloneComponent(source) with
                {
                    Id = id,
                    Transform = source.Transform with
                    {
                        Position = new Vector3Document(
                            sourcePosition.X + offset.X,
                            sourcePosition.Y + offset.Y,
                            sourcePosition.Z + offset.Z)
                    }
                };

                components.Add(duplicate);
                return id;
            });

        return id;
    }

    public void RemoveComponent(Guid componentId)
    {
        Execute(
            "Remove component",
            (components, selection) =>
            {
                components.RemoveAt(GetComponentIndex(componentId, components));
                return selection == componentId ? null : selection;
            });
    }

    public void SetComponentParameter(Guid componentId, string name, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Execute(
            "Set component parameter",
            (components, selection) =>
            {
                var index = GetComponentIndex(componentId, components);
                var component = components[index];
                var parameters = component.Parameters.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Clone(),
                    StringComparer.Ordinal);
                parameters[name] = value.Clone();
                components[index] = component with { Parameters = parameters };
                return selection;
            });
    }

    public void SetComponentParameter<T>(Guid componentId, string name, T value)
    {
        SetComponentParameter(componentId, name, JsonSerializer.SerializeToElement(value));
    }

    public bool Undo()
    {
        if (_undoHistory.Count == 0)
        {
            return false;
        }

        var command = _undoHistory[^1];
        _undoHistory.RemoveAt(_undoHistory.Count - 1);
        _components = CloneComponents(command.BeforeComponents);
        _selectedComponentId = command.BeforeSelection;
        _currentStateId = command.BeforeStateId;
        _redoHistory.Add(command);
        return true;
    }

    public bool Redo()
    {
        if (_redoHistory.Count == 0)
        {
            return false;
        }

        var command = _redoHistory[^1];
        _redoHistory.RemoveAt(_redoHistory.Count - 1);
        _components = CloneComponents(command.AfterComponents);
        _selectedComponentId = command.AfterSelection;
        _currentStateId = command.AfterStateId;
        _undoHistory.Add(command);
        TrimUndoHistory();
        return true;
    }

    public void MarkSaved() => _savedStateId = _currentStateId;

    public SceneDocument Snapshot()
    {
        var snapshot = CreateScene(_components);
        SceneValidator.ValidateAndThrow(snapshot);
        return snapshot;
    }

    private void Execute(
        string description,
        Func<List<ComponentDocument>, Guid?, Guid?> mutation)
    {
        var beforeComponents = CloneComponents(_components);
        var candidateComponents = CloneComponents(_components).ToList();
        var candidateSelection = mutation(candidateComponents, _selectedComponentId);
        var candidateArray = CloneComponents(candidateComponents);

        var candidateScene = CreateScene(candidateArray);
        SceneValidator.ValidateAndThrow(candidateScene);

        if (ComponentsEqual(beforeComponents, candidateArray) &&
            candidateSelection == _selectedComponentId)
        {
            return;
        }

        var command = new EditorCommand(
            description,
            beforeComponents,
            candidateArray,
            _selectedComponentId,
            candidateSelection,
            _currentStateId,
            _nextStateId++);

        _components = CloneComponents(candidateArray);
        _selectedComponentId = candidateSelection;
        _currentStateId = command.AfterStateId;
        _undoHistory.Add(command);
        TrimUndoHistory();
        _redoHistory.Clear();
    }

    private SceneDocument CreateScene(IReadOnlyList<ComponentDocument> components)
    {
        return new SceneDocument(
            _schemaVersion,
            _sceneId,
            _name,
            _simulation,
            CloneComponents(components),
            _tags.ToArray(),
            _connections.ToArray());
    }

    private void TrimUndoHistory()
    {
        if (_undoHistory.Count > _historyLimit)
        {
            _undoHistory.RemoveRange(0, _undoHistory.Count - _historyLimit);
        }
    }

    private static int GetComponentIndex(
        Guid componentId,
        IReadOnlyList<ComponentDocument> components)
    {
        for (var index = 0; index < components.Count; index++)
        {
            if (components[index].Id == componentId)
            {
                return index;
            }
        }

        throw new KeyNotFoundException($"Component {componentId:D} was not found.");
    }

    private static ComponentDocument[] CloneComponents(
        IEnumerable<ComponentDocument> components)
    {
        return components.Select(CloneComponent).ToArray();
    }

    private static ComponentDocument CloneComponent(ComponentDocument component)
    {
        return new ComponentDocument(
            component.Id,
            component.Type,
            new TransformDocument(
                new Vector3Document(
                    component.Transform.Position.X,
                    component.Transform.Position.Y,
                    component.Transform.Position.Z),
                new Vector3Document(
                    component.Transform.RotationDegrees.X,
                    component.Transform.RotationDegrees.Y,
                    component.Transform.RotationDegrees.Z)),
            component.Parameters.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.Ordinal));
    }

    private static bool ComponentsEqual(
        IReadOnlyList<ComponentDocument> first,
        IReadOnlyList<ComponentDocument> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            var left = first[index];
            var right = second[index];
            if (left.Id != right.Id ||
                !string.Equals(left.Type, right.Type, StringComparison.Ordinal) ||
                left.Transform != right.Transform ||
                !ParametersEqual(left.Parameters, right.Parameters))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ParametersEqual(
        IReadOnlyDictionary<string, JsonElement> first,
        IReadOnlyDictionary<string, JsonElement> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        foreach (var (name, value) in first)
        {
            if (!second.TryGetValue(name, out var candidate) ||
                value.ValueKind != candidate.ValueKind ||
                !string.Equals(value.GetRawText(), candidate.GetRawText(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record EditorCommand(
        string Description,
        ComponentDocument[] BeforeComponents,
        ComponentDocument[] AfterComponents,
        Guid? BeforeSelection,
        Guid? AfterSelection,
        long BeforeStateId,
        long AfterStateId);
}

using System.Text.Json;
using SimuladorIndustrial.Core.Editing;
using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.Core.Tests;

public sealed class SceneEditorTests
{
    [Fact]
    public void EditsComponentsAndPreservesTheValidatedSceneContract()
    {
        var original = LoadScene();
        var editor = new SceneEditor(original);
        var originalComponent = original.Components[0];
        var authoredId = Guid.Parse("161de221-674b-4724-ac45-56adbd25e447");
        var duplicateId = Guid.Parse("abdd6a65-999b-491c-8cf7-fe3468716e27");

        editor.SelectComponent(originalComponent.Id);
        editor.MoveComponent(originalComponent.Id, new Vector3Document(-1.5, 0.75, 2));
        editor.RotateComponent(originalComponent.Id, new Vector3Document(0, 45, 0));
        editor.SetComponentParameter(originalComponent.Id, "intervalSeconds", 2.5);
        editor.AddComponent(new ComponentDocument(
            authoredId,
            "fixture.workbench.v1",
            new TransformDocument(
                new Vector3Document(3, 0, 1),
                new Vector3Document(0, 90, 0)),
            new Dictionary<string, JsonElement>
            {
                ["widthMeters"] = JsonSerializer.SerializeToElement(1.2)
            }));
        editor.DuplicateComponent(
            authoredId,
            duplicateId,
            new Vector3Document(0.5, 0, -0.25));

        var snapshot = editor.Snapshot();

        Assert.Empty(SceneValidator.Validate(snapshot));
        Assert.Equal(original.SchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(original.SceneId, snapshot.SceneId);
        Assert.Equal(original.Name, snapshot.Name);
        Assert.Equal(original.Simulation, snapshot.Simulation);
        Assert.Equal(original.Tags, snapshot.Tags);
        Assert.Equal(original.Connections, snapshot.Connections);
        Assert.Equal(
            original.Components.Select(component => component.Id),
            snapshot.Components.Take(original.Components.Count).Select(component => component.Id));

        var edited = snapshot.Components.Single(component => component.Id == originalComponent.Id);
        Assert.Equal(new Vector3Document(-1.5, 0.75, 2), edited.Transform.Position);
        Assert.Equal(new Vector3Document(0, 45, 0), edited.Transform.RotationDegrees);
        Assert.Equal(2.5, edited.Parameters["intervalSeconds"].GetDouble());

        var duplicate = snapshot.Components.Single(component => component.Id == duplicateId);
        Assert.Equal("fixture.workbench.v1", duplicate.Type);
        Assert.Equal(new Vector3Document(3.5, 0, 0.75), duplicate.Transform.Position);
        Assert.Equal(1.2, duplicate.Parameters["widthMeters"].GetDouble());
        Assert.Equal(duplicateId, editor.SelectedComponentId);
    }

    [Fact]
    public void UndoRedoTracksDirtyStateAndSavedBaseline()
    {
        var editor = new SceneEditor(LoadScene());
        var component = editor.Components[0];
        var originalPosition = component.Transform.Position;
        var movedPosition = new Vector3Document(7, 1, -2);

        editor.SelectComponent(component.Id);
        Assert.False(editor.IsDirty);

        editor.MoveComponent(component.Id, movedPosition);
        Assert.True(editor.IsDirty);
        Assert.Equal(1, editor.UndoCount);
        editor.MarkSaved();
        Assert.False(editor.IsDirty);

        editor.RotateComponent(component.Id, new Vector3Document(0, 30, 0));
        Assert.True(editor.IsDirty);
        Assert.True(editor.Undo());
        Assert.False(editor.IsDirty);
        Assert.Equal(movedPosition, editor.GetComponent(component.Id).Transform.Position);

        Assert.True(editor.Undo());
        Assert.True(editor.IsDirty);
        Assert.Equal(originalPosition, editor.GetComponent(component.Id).Transform.Position);

        Assert.True(editor.Redo());
        Assert.False(editor.IsDirty);
        Assert.True(editor.Redo());
        Assert.True(editor.IsDirty);
        Assert.Equal(component.Id, editor.SelectedComponentId);
    }

    [Fact]
    public void HistoryLimitKeepsOnlyTheMostRecentCommands()
    {
        var editor = new SceneEditor(LoadScene(), historyLimit: 2);
        var componentId = editor.Components[0].Id;

        editor.MoveComponent(componentId, new Vector3Document(1, 0, 0));
        editor.MoveComponent(componentId, new Vector3Document(2, 0, 0));
        editor.MoveComponent(componentId, new Vector3Document(3, 0, 0));

        Assert.Equal(2, editor.UndoCount);
        Assert.True(editor.Undo());
        Assert.True(editor.Undo());
        Assert.False(editor.Undo());
        Assert.Equal(new Vector3Document(1, 0, 0), editor.GetComponent(componentId).Transform.Position);
        Assert.Equal(2, editor.RedoCount);
    }

    [Fact]
    public void InvalidParameterAndTransformEditsAreAtomicAndNotRecorded()
    {
        var editor = new SceneEditor(LoadScene());
        var componentId = editor.Components[0].Id;
        var before = SceneSerializer.Serialize(editor.Snapshot());

        Assert.Throws<SceneValidationException>(
            () => editor.SetComponentParameter(componentId, "path", "C:/unsafe/model.glb"));
        Assert.Throws<SceneValidationException>(
            () => editor.MoveComponent(componentId, new Vector3Document(double.NaN, 0, 0)));

        Assert.Equal(before, SceneSerializer.Serialize(editor.Snapshot()));
        Assert.Equal(0, editor.UndoCount);
        Assert.Equal(0, editor.RedoCount);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void RemovingAComponentReferencedByATagIsRejectedAtomically()
    {
        var scene = LoadScene();
        var editor = new SceneEditor(scene);
        var source = scene.Tags[0].Source;
        var componentId = Guid.ParseExact(source[..source.IndexOf('.')], "D");
        var before = SceneSerializer.Serialize(editor.Snapshot());

        Assert.Throws<SceneValidationException>(() => editor.RemoveComponent(componentId));

        Assert.Equal(before, SceneSerializer.Serialize(editor.Snapshot()));
        Assert.Equal(0, editor.UndoCount);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void RemovingAnUnreferencedComponentCanBeUndone()
    {
        var scene = LoadScene();
        var editor = new SceneEditor(scene);
        var componentId = scene.Components[0].Id;
        editor.SelectComponent(componentId);

        editor.RemoveComponent(componentId);

        Assert.DoesNotContain(editor.Components, component => component.Id == componentId);
        Assert.Null(editor.SelectedComponentId);
        Assert.True(editor.Undo());
        Assert.Contains(editor.Components, component => component.Id == componentId);
        Assert.Equal(componentId, editor.SelectedComponentId);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void DuplicateUuidAndUnknownSelectionAreAtomicAndNotRecorded()
    {
        var editor = new SceneEditor(LoadScene());
        var componentId = editor.Components[0].Id;
        var before = SceneSerializer.Serialize(editor.Snapshot());

        Assert.Throws<SceneValidationException>(
            () => editor.DuplicateComponent(componentId, componentId));
        Assert.Throws<KeyNotFoundException>(() => editor.SelectComponent(Guid.NewGuid()));

        Assert.Equal(before, SceneSerializer.Serialize(editor.Snapshot()));
        Assert.Null(editor.SelectedComponentId);
        Assert.Equal(0, editor.UndoCount);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void NoOpEditsDoNotCreateHistoryAndReturnedDocumentsAreIsolated()
    {
        var editor = new SceneEditor(LoadScene());
        var component = editor.Components[0];

        editor.MoveComponent(component.Id, component.Transform.Position);
        editor.SetComponentParameter(
            component.Id,
            "intervalSeconds",
            component.Parameters["intervalSeconds"]);

        Assert.Equal(0, editor.UndoCount);
        Assert.False(editor.IsDirty);

        var snapshot = editor.Snapshot();
        var mutableParameters = Assert.IsType<Dictionary<string, JsonElement>>(
            snapshot.Components[0].Parameters);
        mutableParameters["intervalSeconds"] = JsonSerializer.SerializeToElement(99);

        Assert.NotEqual(
            99,
            editor.GetComponent(component.Id).Parameters["intervalSeconds"].GetDouble());
    }

    private static SceneDocument LoadScene()
    {
        return SceneSerializer.Deserialize(File.ReadAllText(SamplePath));
    }

    private static string SamplePath => Path.Combine(
        AppContext.BaseDirectory,
        "samples",
        "celula-envase.scene.json");
}

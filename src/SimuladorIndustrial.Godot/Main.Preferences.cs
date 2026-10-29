using Godot;
using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.GodotApp;

public sealed partial class Main
{
    private UiPreferences _uiPreferences = UiPreferences.Default;
    private Control _hudRoot = null!;
    private Vector2 _lastHudViewportSize;
    private string _uiPreferencesPath = string.Empty;

    private void LoadUiPreferences()
    {
        _uiPreferencesPath = ProjectSettings.GlobalizePath("user://ui-preferences.json");
        try
        {
            _uiPreferences = UiPreferencesStore.Load(_uiPreferencesPath);
        }
        catch (UiPreferencesException exception)
        {
            _uiPreferences = UiPreferences.Default;
            GD.PushWarning($"Preferências de interface inválidas; usando padrões: {exception.Message}");
        }
    }

    private void SaveUiPreferences(UiPreferences preferences)
    {
        try
        {
            UiPreferencesStore.Save(_uiPreferencesPath, preferences);
            _uiPreferences = preferences;
            RefreshHudLayout(force: true);
        }
        catch (UiPreferencesException exception)
        {
            GD.PushWarning($"Não foi possível salvar as preferências: {exception.Message}");
        }
    }

    private void ConfigureHudRoot(Control root)
    {
        _hudRoot = root;
        _hudRoot.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        RefreshHudLayout(force: true);
    }

    private void RefreshHudLayout(bool force = false)
    {
        if (!GodotObject.IsInstanceValid(_hudRoot))
        {
            return;
        }

        var viewportSize = GetViewport().GetVisibleRect().Size;
        if (!force && viewportSize.IsEqualApprox(_lastHudViewportSize))
        {
            return;
        }

        var scale = (float)Math.Clamp(_uiPreferences.UiScale, 0.75d, 1.50d);
        _lastHudViewportSize = viewportSize;
        _hudRoot.Position = Vector2.Zero;
        _hudRoot.Scale = Vector2.One * scale;
        _hudRoot.Size = viewportSize / scale;
        if (GodotObject.IsInstanceValid(_sceneEditor))
        {
            _sceneEditor.SetInterfaceScale(scale, viewportSize);
        }
    }
}

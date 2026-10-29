using Godot;

namespace SimuladorIndustrial.GodotApp;

public sealed partial class OrbitCameraController : Node3D
{
    private const float RotationSensitivity = 0.006f;
    private const float PanSensitivity = 0.0016f;
    private const float MinimumPitch = -0.05f;
    private const float MaximumPitch = 1.38f;
    private const float MinimumDistance = 3f;
    private const float MaximumDistance = 16f;
    private const float FreeLookSensitivity = 0.0035f;
    private const float FreeMoveSpeed = 3.5f;

    private Camera3D _camera = null!;
    private Vector3 _target;
    private float _yaw;
    private float _pitch;
    private float _distance;
    private bool _rotating;
    private bool _panning;
    private bool _freeLooking;
    private float _freeYaw;
    private float _freePitch;

    public Camera3D Camera => _camera;

    public Vector3 Target => _target;

    public CameraNavigationMode Mode { get; private set; } = CameraNavigationMode.Orbit;

    public bool InputEnabled { get; set; } = true;

    public override void _Ready()
    {
        _camera = new Camera3D
        {
            Name = "Camera3D",
            Current = true,
            Fov = 48f,
            Near = 0.05f,
            Far = 120f
        };
        AddChild(_camera);
        ResetView();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton mouseButton:
                HandleMouseButton(mouseButton);
                break;
            case InputEventMouseMotion mouseMotion:
                HandleMouseMotion(mouseMotion);
                break;
            case InputEventKey key when key.Pressed && !key.Echo:
                HandleKey(key);
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!InputEnabled || Mode != CameraNavigationMode.Free || !IsInstanceValid(_camera))
        {
            return;
        }

        var direction = Vector3.Zero;
        var forward = -_camera.GlobalTransform.Basis.Z.Normalized();
        var right = _camera.GlobalTransform.Basis.X.Normalized();
        if (Input.IsKeyPressed(Key.W)) direction += forward;
        if (Input.IsKeyPressed(Key.S)) direction -= forward;
        if (Input.IsKeyPressed(Key.D)) direction += right;
        if (Input.IsKeyPressed(Key.A)) direction -= right;
        if (Input.IsKeyPressed(Key.E)) direction += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q)) direction -= Vector3.Up;
        if (direction.IsZeroApprox())
        {
            return;
        }

        var speed = Input.IsKeyPressed(Key.Shift) ? FreeMoveSpeed * 3f : FreeMoveSpeed;
        _camera.GlobalPosition += direction.Normalized() * speed * (float)delta;
    }

    public bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
    {
        if (!IsInstanceValid(_camera))
        {
            point = Vector3.Zero;
            return false;
        }

        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);
        if (Mathf.Abs(direction.Y) < 0.0001f)
        {
            point = Vector3.Zero;
            return false;
        }

        var distanceToPlane = -origin.Y / direction.Y;
        if (distanceToPlane < 0f)
        {
            point = Vector3.Zero;
            return false;
        }

        point = origin + direction * distanceToPlane;
        return true;
    }

    public void ResetView()
    {
        Mode = CameraNavigationMode.Orbit;
        _freeLooking = false;
        _target = new Vector3(0f, 0.75f, 0f);
        _yaw = Mathf.DegToRad(-42f);
        _pitch = Mathf.DegToRad(27f);
        _distance = 7.4f;
        UpdateCameraTransform();
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        if (Mode == CameraNavigationMode.Free)
        {
            if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                _freeLooking = mouseButton.Pressed;
            }
            else if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                _camera.GlobalPosition += -_camera.GlobalTransform.Basis.Z.Normalized() * 0.65f;
            }
            else if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                _camera.GlobalPosition += _camera.GlobalTransform.Basis.Z.Normalized() * 0.65f;
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        switch (mouseButton.ButtonIndex)
        {
            case MouseButton.Left:
                _rotating = mouseButton.Pressed;
                if (mouseButton.Pressed && mouseButton.DoubleClick &&
                    TryGetGroundPoint(mouseButton.Position, out var pivot))
                {
                    _target = pivot with { Y = 0.65f };
                    UpdateCameraTransform();
                }
                break;
            case MouseButton.Right:
            case MouseButton.Middle:
                _panning = mouseButton.Pressed;
                break;
            case MouseButton.WheelUp when mouseButton.Pressed:
                _distance = Mathf.Max(MinimumDistance, _distance * 0.88f);
                UpdateCameraTransform();
                break;
            case MouseButton.WheelDown when mouseButton.Pressed:
                _distance = Mathf.Min(MaximumDistance, _distance * 1.14f);
                UpdateCameraTransform();
                break;
        }

        GetViewport().SetInputAsHandled();
    }

    private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        if (Mode == CameraNavigationMode.Free)
        {
            if (!_freeLooking)
            {
                return;
            }

            _freeYaw -= mouseMotion.Relative.X * FreeLookSensitivity;
            _freePitch = Mathf.Clamp(
                _freePitch - mouseMotion.Relative.Y * FreeLookSensitivity,
                -1.52f,
                1.52f);
            _camera.Rotation = new Vector3(_freePitch, _freeYaw, 0f);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_rotating)
        {
            _yaw -= mouseMotion.Relative.X * RotationSensitivity;
            _pitch = Mathf.Clamp(
                _pitch - mouseMotion.Relative.Y * RotationSensitivity,
                MinimumPitch,
                MaximumPitch);
            UpdateCameraTransform();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_panning)
        {
            return;
        }

        var right = _camera.GlobalTransform.Basis.X.Normalized();
        var scale = _distance * PanSensitivity;
        _target += -right * mouseMotion.Relative.X * scale;
        _target += Vector3.Up * mouseMotion.Relative.Y * scale;
        _target.Y = Mathf.Clamp(_target.Y, 0f, 3f);
        UpdateCameraTransform();
        GetViewport().SetInputAsHandled();
    }

    private void HandleKey(InputEventKey key)
    {
        if (key.Keycode == Key.Home)
        {
            ResetView();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode != Key.F)
        {
            return;
        }

        if (Mode == CameraNavigationMode.Orbit)
        {
            Mode = CameraNavigationMode.Free;
            _rotating = false;
            _panning = false;
            _freePitch = _camera.Rotation.X;
            _freeYaw = _camera.Rotation.Y;
        }
        else
        {
            Mode = CameraNavigationMode.Orbit;
            _freeLooking = false;
            _target = _camera.GlobalPosition - _camera.GlobalTransform.Basis.Z.Normalized() * _distance;
            UpdateOrbitAnglesFromCamera();
            UpdateCameraTransform();
        }

        GetViewport().SetInputAsHandled();
    }

    private void UpdateOrbitAnglesFromCamera()
    {
        var offset = _camera.GlobalPosition - _target;
        if (offset.IsZeroApprox())
        {
            return;
        }

        _distance = Mathf.Clamp(offset.Length(), MinimumDistance, MaximumDistance);
        var normalized = offset / _distance;
        _pitch = Mathf.Asin(Mathf.Clamp(normalized.Y, -1f, 1f));
        _yaw = Mathf.Atan2(normalized.X, normalized.Z);
    }

    private void UpdateCameraTransform()
    {
        if (!IsInstanceValid(_camera))
        {
            return;
        }

        var cosPitch = Mathf.Cos(_pitch);
        var offset = new Vector3(
            cosPitch * Mathf.Sin(_yaw),
            Mathf.Sin(_pitch),
            cosPitch * Mathf.Cos(_yaw)) * _distance;
        _camera.Position = _target + offset;
        _camera.LookAt(_target, Vector3.Up);
    }
}

public enum CameraNavigationMode
{
    Orbit,
    Free
}

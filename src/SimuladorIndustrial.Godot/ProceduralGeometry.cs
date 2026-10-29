using Godot;

namespace SimuladorIndustrial.GodotApp;

public static class ProceduralGeometry
{
    public static StandardMaterial3D CreateMaterial(
        Color color,
        float metallic = 0f,
        float roughness = 0.72f,
        bool unshaded = false,
        bool transparent = false,
        Color? emission = null)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Metallic = metallic,
            Roughness = roughness,
            ShadingMode = unshaded
                ? BaseMaterial3D.ShadingModeEnum.Unshaded
                : BaseMaterial3D.ShadingModeEnum.PerPixel
        };

        if (transparent || color.A < 0.999f)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }

        if (emission is { } emissionColor)
        {
            material.EmissionEnabled = true;
            material.Emission = emissionColor;
            material.EmissionEnergyMultiplier = 2.2f;
        }

        return material;
    }

    public static MeshInstance3D AddBox(
        Node parent,
        string name,
        Vector3 size,
        Vector3 position,
        Material material,
        Vector3? rotationDegrees = null)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh
            {
                Size = size,
                Material = material
            },
            Position = position,
            RotationDegrees = rotationDegrees ?? Vector3.Zero
        };
        parent.AddChild(instance);
        return instance;
    }

    public static MeshInstance3D AddCylinder(
        Node parent,
        string name,
        float radius,
        float height,
        Vector3 position,
        Material material,
        Vector3? rotationDegrees = null,
        int radialSegments = 32)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = height,
                RadialSegments = radialSegments,
                Material = material
            },
            Position = position,
            RotationDegrees = rotationDegrees ?? Vector3.Zero
        };
        parent.AddChild(instance);
        return instance;
    }

    public static MeshInstance3D AddCone(
        Node parent,
        string name,
        float radius,
        float height,
        Vector3 position,
        Material material,
        Vector3? rotationDegrees = null)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh
            {
                TopRadius = 0f,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 24,
                Material = material
            },
            Position = position,
            RotationDegrees = rotationDegrees ?? Vector3.Zero
        };
        parent.AddChild(instance);
        return instance;
    }

    public static MeshInstance3D AddSphere(
        Node parent,
        string name,
        float radius,
        Vector3 position,
        Material material)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = new SphereMesh
            {
                Radius = radius,
                Height = radius * 2f,
                RadialSegments = 24,
                Rings = 12,
                Material = material
            },
            Position = position
        };
        parent.AddChild(instance);
        return instance;
    }

    public static Node3D AddCartesianPlane(Node parent)
    {
        var root = new Node3D { Name = "PlanoCartesiano" };
        parent.AddChild(root);

        var floorMaterial = CreateMaterial(new Color(0.045f, 0.075f, 0.115f, 0.96f), roughness: 0.9f);
        var floor = new MeshInstance3D
        {
            Name = "Piso",
            Mesh = new PlaneMesh
            {
                Size = new Vector2(8f, 6f),
                Material = floorMaterial
            },
            Position = new Vector3(0f, -0.018f, 0f)
        };
        root.AddChild(floor);

        AddGrid(root, 8f, 6f, 0.25f);
        AddAxes(root);
        return root;
    }

    private static void AddGrid(Node3D parent, float width, float depth, float spacing)
    {
        var minorMaterial = CreateMaterial(
            new Color(0.18f, 0.29f, 0.40f, 0.48f),
            unshaded: true,
            transparent: true);
        var majorMaterial = CreateMaterial(
            new Color(0.31f, 0.48f, 0.64f, 0.72f),
            unshaded: true,
            transparent: true);
        var minor = new ImmediateMesh();
        var major = new ImmediateMesh();
        minor.SurfaceBegin(Mesh.PrimitiveType.Lines, minorMaterial);
        major.SurfaceBegin(Mesh.PrimitiveType.Lines, majorMaterial);

        var halfWidth = width / 2f;
        var halfDepth = depth / 2f;
        for (var x = -halfWidth; x <= halfWidth + 0.001f; x += spacing)
        {
            var target = IsMajor(x) ? major : minor;
            target.SurfaceAddVertex(new Vector3(x, 0f, -halfDepth));
            target.SurfaceAddVertex(new Vector3(x, 0f, halfDepth));
        }

        for (var z = -halfDepth; z <= halfDepth + 0.001f; z += spacing)
        {
            var target = IsMajor(z) ? major : minor;
            target.SurfaceAddVertex(new Vector3(-halfWidth, 0f, z));
            target.SurfaceAddVertex(new Vector3(halfWidth, 0f, z));
        }

        minor.SurfaceEnd();
        major.SurfaceEnd();
        parent.AddChild(new MeshInstance3D { Name = "Grade025m", Mesh = minor });
        parent.AddChild(new MeshInstance3D
        {
            Name = "Grade1m",
            Mesh = major,
            Position = new Vector3(0f, 0.002f, 0f)
        });
    }

    private static void AddAxes(Node3D parent)
    {
        var axes = new Node3D { Name = "EixosXYZ" };
        parent.AddChild(axes);

        var red = CreateMaterial(new Color(0.95f, 0.19f, 0.22f), metallic: 0.15f, unshaded: true);
        var green = CreateMaterial(new Color(0.22f, 0.92f, 0.40f), metallic: 0.15f, unshaded: true);
        var blue = CreateMaterial(new Color(0.20f, 0.52f, 1f), metallic: 0.15f, unshaded: true);

        AddBox(axes, "EixoX", new Vector3(2.6f, 0.025f, 0.025f), new Vector3(1.3f, 0.035f, 0f), red);
        AddCone(axes, "SetaX", 0.075f, 0.20f, new Vector3(2.70f, 0.035f, 0f), red, new Vector3(0f, 0f, -90f));
        AddBox(axes, "EixoY", new Vector3(0.025f, 2.25f, 0.025f), new Vector3(0f, 1.125f, 0f), green);
        AddCone(axes, "SetaY", 0.075f, 0.20f, new Vector3(0f, 2.35f, 0f), green);
        AddBox(axes, "EixoZ", new Vector3(0.025f, 0.025f, 2.4f), new Vector3(0f, 0.035f, 1.2f), blue);
        AddCone(axes, "SetaZ", 0.075f, 0.20f, new Vector3(0f, 0.035f, 2.50f), blue, new Vector3(90f, 0f, 0f));

        AddAxisLabel(axes, "X", new Vector3(2.92f, 0.12f, 0f), new Color(1f, 0.30f, 0.32f));
        AddAxisLabel(axes, "Y", new Vector3(0f, 2.62f, 0f), new Color(0.35f, 1f, 0.50f));
        AddAxisLabel(axes, "Z", new Vector3(0f, 0.12f, 2.78f), new Color(0.35f, 0.65f, 1f));
    }

    public static Label3D AddWorldLabel(
        Node parent,
        string text,
        Vector3 position,
        Color color,
        int fontSize = 42,
        float pixelSize = 0.0035f)
    {
        var label = new Label3D
        {
            Name = $"Rotulo{text}",
            Text = text,
            Position = position,
            Modulate = color,
            FontSize = fontSize,
            PixelSize = pixelSize,
            OutlineSize = 8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true
        };
        parent.AddChild(label);
        return label;
    }

    private static void AddAxisLabel(Node parent, string text, Vector3 position, Color color) =>
        AddWorldLabel(parent, text, position, color, fontSize: 58, pixelSize: 0.004f);

    private static bool IsMajor(float value) =>
        Mathf.Abs(value - Mathf.Round(value)) < 0.01f;
}

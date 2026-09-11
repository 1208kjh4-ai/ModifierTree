namespace ModifierTree.Core;

/// <summary>A Rhino-independent world-coordinate point, direction or spacing.</summary>
public readonly record struct ModifierVector(double X, double Y, double Z)
{
    public static ModifierVector Zero => new(0, 0, 0);
}

public sealed record MirrorSettings(ModifierVector Origin, ModifierVector Normal, bool KeepOriginal = true, bool Union = true)
{
    public static MirrorSettings Default { get; } = new(ModifierVector.Zero, new ModifierVector(1, 0, 0));
}

/// <summary>Degrees of bend about a Control Box's local axis. Local Y-axis length is always preserved.</summary>
public sealed record ControlBoxSettings(double Strength = 45, bool Limited = true)
{
    public static ControlBoxSettings Default { get; } = new();
}

/// <summary>Counts include the original input; signed spacings use document units along independent axis directions.</summary>
public sealed record ArraySettings
{
    public int CountX { get; init; } = 2;
    public int CountY { get; init; } = 1;
    public int CountZ { get; init; } = 1;
    public ModifierVector Spacing { get; init; } = new(10, 10, 10);
    public ModifierVector AxisX { get; init; } = new(1, 0, 0);
    public ModifierVector AxisY { get; init; } = new(0, 1, 0);
    public ModifierVector AxisZ { get; init; } = new(0, 0, 1);
    public static ArraySettings Default { get; } = new();
}

public static class ModifierSettings
{
    public const int MaxArrayInstances = 256;
    public const double MaxCoordinate = 1e12;
    public const double MaxBendStrength = 180;

    public static bool TryValidate(ControlBoxSettings? settings, out string error)
    {
        error = "";
        if (settings is null) { error = "Control Box settings are missing."; return false; }
        if (!double.IsFinite(settings.Strength) || Math.Abs(settings.Strength) > MaxBendStrength)
        { error = $"Bend Strength must be between {-MaxBendStrength} and {MaxBendStrength} degrees."; return false; }
        return true;
    }

    public static bool TryValidate(MirrorSettings? settings, out string error)
    {
        error = "";
        if (settings is null) { error = "Mirror settings are missing."; return false; }
        if (!ValidVector(settings.Origin) || !ValidVector(settings.Normal))
        { error = "Mirror coordinates must be finite and within 1e12 document units."; return false; }
        var normal = settings.Normal;
        if (Math.Max(Math.Abs(normal.X), Math.Max(Math.Abs(normal.Y), Math.Abs(normal.Z))) < 1e-12)
        { error = "The Mirror plane normal cannot be zero."; return false; }
        return true;
    }

    public static bool TryValidate(ArraySettings? settings, out string error)
    {
        error = "";
        if (settings is null) { error = "Array settings are missing."; return false; }
        if (settings.CountX is < 1 or > MaxArrayInstances || settings.CountY is < 1 or > MaxArrayInstances ||
            settings.CountZ is < 1 or > MaxArrayInstances || (long)settings.CountX * settings.CountY * settings.CountZ > MaxArrayInstances)
        { error = $"Array counts must be positive and produce at most {MaxArrayInstances} placements, including the original."; return false; }
        if (!ValidVector(settings.Spacing))
        { error = "Array spacings must be finite and within 1e12 document units."; return false; }
        if (!ValidDirection(settings.AxisX) || !ValidDirection(settings.AxisY) || !ValidDirection(settings.AxisZ))
        { error = "Array axis directions must be finite, nonzero and within 1e12."; return false; }
        return true;
    }

    internal static bool TryValidate(TreeNodeKind kind, bool enabled, MirrorSettings? mirror, ArraySettings? array,
        ControlBoxSettings? controlBox, out string error)
    {
        error = "";
        if ((kind is TreeNodeKind.Geometry or TreeNodeKind.BasePlane or TreeNodeKind.ControlBox) && !enabled)
        { error = "Only Modifiers can be disabled."; return false; }
        if ((kind == TreeNodeKind.Mirror) != (mirror is not null) || (kind == TreeNodeKind.Array) != (array is not null) ||
            (kind == TreeNodeKind.ControlBox) != (controlBox is not null))
        { error = "Modifier settings do not match their node kind."; return false; }
        return mirror is not null ? TryValidate(mirror, out error) : array is not null ? TryValidate(array, out error) :
            controlBox is null || TryValidate(controlBox, out error);
    }

    private static bool ValidVector(ModifierVector value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z) &&
        Math.Abs(value.X) <= MaxCoordinate && Math.Abs(value.Y) <= MaxCoordinate && Math.Abs(value.Z) <= MaxCoordinate;

    private static bool ValidDirection(ModifierVector value) => ValidVector(value) &&
        Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z))) >= 1e-12;
}

using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino.Geometry;

internal static class BooleanUnionEdgeChecks
{
    private const double Tolerance = 0.001;

    public static void Run()
    {
        using var outer = Box(0, 10);
        using var inner = Box(2, 8, 2, 8, 2, 8);
        inner.Flip();
        using var hollow = ResultGeometry.Create([outer, inner]);
        using var half = Box(-1, 5, -1, 11, -1, 11);
        using var inVoid = Box(3, 4, 3, 4, 3, 4);
        using var inMaterial = Box(0.5, 1.5, 0.5, 1.5, 0.5, 1.5);
        using var enclosing = Box(-1, 11, -1, 11, -1, 11);
        using var shifted = hollow.DuplicateBrep();
        shifted.Transform(Transform.Translation(2, 0, 0));
        using var normalInner = Box(2, 8, 2, 8, 2, 8);

        Result(hollow, half, 1256, 1,
            "Union retains the unfilled half of a cavity when another solid crosses it");
        Result(hollow, inVoid, 785, 2,
            "Union keeps a separate solid inside the cavity instead of filling the cavity");
        Result(hollow, inMaterial, 784, 1,
            "Union with a solid already inside the material leaves the cavity unchanged");
        Result(hollow, enclosing, 1728, 1,
            "Union with an enclosing solid fills the cavity and keeps the enclosing boundary");
        Result(hollow, shifted, 1056, 1,
            "Union of overlapping hollow solids preserves only their shared cavity");
        Result(outer, normalInner, 1000, 1,
            "Union removes a redundant solid fully contained in an ordinary solid");
    }

    private static void Result(Brep first, Brep second, double expectedVolume, int expectedCount, string message)
    {
        var firstBefore = Snapshot(first);
        var secondBefore = Snapshot(second);
        var results = BooleanEvaluator.Evaluate(TreeNodeKind.BooleanUnion, [[first], [second]], Tolerance);
        try
        {
            Check(results.Length == expectedCount && results.All(result => result.IsValid && result.IsSolid &&
                      result.SolidOrientation == BrepSolidOrientation.Outward) &&
                  Near(results.Sum(Volume), expectedVolume), message);
            Check(Same(firstBefore, Snapshot(first)) && Same(secondBefore, Snapshot(second)),
                message + ": both source solids retain their shell orientation, volume and position");
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    private static SourceState Snapshot(Brep solid)
    {
        var shells = solid.GetConnectedComponents();
        try
        {
            return new SourceState(solid.IsValid, solid.IsSolid, solid.SolidOrientation, Volume(solid),
                solid.GetBoundingBox(true), shells.Select(shell =>
                    new ShellState(shell.SolidOrientation, Volume(shell))).ToArray());
        }
        finally { foreach (var shell in shells) shell.Dispose(); }
    }

    private static bool Same(SourceState first, SourceState second) =>
        first.Valid == second.Valid && first.Solid == second.Solid && first.Orientation == second.Orientation &&
        Near(first.Volume, second.Volume) && first.Bounds.Equals(second.Bounds) &&
        first.Shells.Length == second.Shells.Length && first.Shells.Zip(second.Shells).All(pair =>
            pair.First.Orientation == pair.Second.Orientation && Near(pair.First.Volume, pair.Second.Volume));

    private static Brep Box(double x0, double x1, double y0 = 0, double y1 = 10, double z0 = 0, double z1 = 10) =>
        new BoundingBox(x0, y0, z0, x1, y1, z1).ToBrep();

    private static double Volume(Brep solid)
    {
        using var properties = VolumeMassProperties.Compute(solid)
            ?? throw new Exception("FAIL: Cannot measure the closed Boolean solid.");
        return properties.Volume;
    }

    private static bool Near(double first, double second) => Math.Abs(first - second) < Tolerance;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed record SourceState(bool Valid, bool Solid, BrepSolidOrientation Orientation, double Volume,
        BoundingBox Bounds, ShellState[] Shells);
    private readonly record struct ShellState(BrepSolidOrientation Orientation, double Volume);
}

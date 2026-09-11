using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino.Geometry;

internal static class BendControlCageCacheChecks
{
    private const double Tolerance = 0.001;
    private const string CornerKey = "ModifierTree.ControlBox.Corners";

    public static void Run()
    {
        ReuseAndSettings();
        GeometryAndMetadata();
        AbsoluteDragAndCancel();
        RemovalAndDisposal();
    }

    private static void ReuseAndSettings()
    {
        using var geometry = CreateBox();
        using var cache = new BendControlCageCache();
        var id = Guid.NewGuid();
        var settings = new ControlBoxSettings(45);
        var first = Required(cache.Get(id, geometry, settings, Tolerance));
        Check(ReferenceEquals(first, cache.Get(id, geometry, new ControlBoxSettings(45), Tolerance)),
            "Repeated cage draws and picks reuse the same curves for unchanged box settings");
        var previousWires = first.Wires.ToArray();
        var stronger = Required(cache.Get(id, geometry, settings with { Strength = 90 }, Tolerance));
        Check(!ReferenceEquals(first, stronger) && previousWires.All(wire => wire.Disposed),
            "Strength changes dispose the old display curves and construct a new cage");
        previousWires = stronger.Wires.ToArray();
        var unlimited = Required(cache.Get(id, geometry, new ControlBoxSettings(90, false), Tolerance));
        Check(!ReferenceEquals(stronger, unlimited) && previousWires.All(wire => wire.Disposed),
            "Bend mode changes invalidate the control cage even when the in-box outline is unchanged");
        var finer = Required(cache.Get(id, geometry, new ControlBoxSettings(90, false), Tolerance / 2));
        Check(!ReferenceEquals(unlimited, finer), "Document tolerance changes rebuild the display cage");
        using var duplicate = geometry.DuplicateBrep();
        Check(ReferenceEquals(finer, cache.Get(id, duplicate, new ControlBoxSettings(90, false), Tolerance / 2)),
            "An equivalent raw box with the same corner identities reuses its current cage");
        Check(cache.Count == 1, "Repeated property changes retain only one cache entry per Control Box");
    }

    private static void GeometryAndMetadata()
    {
        using var geometry = CreateBox();
        using var cache = new BendControlCageCache();
        var id = Guid.NewGuid();
        var settings = new ControlBoxSettings(45);
        var first = Required(cache.Get(id, geometry, settings, Tolerance));
        var originalBounds = first.Bounds;
        var offset = new Vector3d(21, -6, 4);
        geometry.Transform(Transform.Translation(offset));
        var moved = Required(cache.Get(id, geometry, settings, Tolerance));
        Check(!ReferenceEquals(first, moved) &&
            moved.Bounds.Center.DistanceTo(originalBounds.Center + offset) < Tolerance,
            "Native box geometry edits invalidate the cached cage without relying on a new node ID");

        var savedCorners = geometry.GetUserString(CornerKey);
        var oldWires = moved.Wires.ToArray();
        geometry.SetUserString(CornerKey, "damaged");
        Check(cache.Get(id, geometry, settings, Tolerance) is null && oldWires.All(wire => wire.Disposed),
            "Damaged corner identities remove the old visible and pickable cage immediately");
        Check(cache.Get(id, geometry, settings, Tolerance) is null && cache.Count == 1,
            "An invalid box is retained as a null cache entry instead of growing drag history");
        geometry.SetUserString(CornerKey, savedCorners);
        Check(cache.Get(id, geometry, settings, Tolerance) is not null,
            "Restoring corner identities recovers the current control cage");
        geometry.SetUserString(CornerKey, null);
        Check(cache.Get(id, geometry, settings, Tolerance) is null,
            "Removing corner metadata invalidates the cage independently of geometric coordinates");
        geometry.SetUserString(CornerKey, savedCorners);
        var repaired = Required(cache.Get(id, geometry, settings, Tolerance));
        oldWires = repaired.Wires.ToArray();
        using var damagedShape = new LineCurve(Point3d.Origin, new Point3d(1, 2, 3));
        Check(cache.Get(id, damagedShape, settings, Tolerance) is null && oldWires.All(wire => wire.Disposed),
            "Replacing a box with invalid geometry never leaves a stale cage behind");
        Check(cache.Get(id, geometry, settings, Tolerance) is not null,
            "Restoring the raw Brep after a damaged shape rebuilds its cage");
    }

    private static void AbsoluteDragAndCancel()
    {
        using var geometry = CreateBox();
        var crc = geometry.DataCRC(0);
        var corners = geometry.GetUserString(CornerKey);
        using var cache = new BendControlCageCache();
        var id = Guid.NewGuid();
        var settings = new ControlBoxSettings(60);
        var baseline = Required(cache.Get(id, geometry, settings, Tolerance));
        var baselineBounds = baseline.Bounds;
        var transform = Transform.Translation(10, 20, -4);
        var first = Required(cache.Get(id, geometry, settings, Tolerance, transform));
        Check(ReferenceEquals(first, cache.Get(id, geometry, settings, Tolerance, transform)),
            "Draw and hit-test requests for the same dynamic transform share one cage");

        var shifted = Transform.Translation(30, -12, 7);
        var second = Required(cache.Get(id, geometry, settings, Tolerance, shifted));
        Check(second.Bounds.Center.DistanceTo(baselineBounds.Center + new Vector3d(30, -12, 7)) < Tolerance,
            "A later drag matrix applies to the original box instead of accumulating prior transforms");
        shifted.M03 += 1e-9;
        var exact = Required(cache.Get(id, geometry, settings, Tolerance, shifted));
        Check(!ReferenceEquals(second, exact),
            "Cage cache distinguishes exact transform changes smaller than document tolerance");

        var oldWires = exact.Wires.ToArray();
        var cancelled = Required(cache.Get(id, geometry, settings, Tolerance));
        Check(cancelled.Bounds.Min.DistanceTo(baselineBounds.Min) < Tolerance &&
            cancelled.Bounds.Max.DistanceTo(baselineBounds.Max) < Tolerance && oldWires.All(wire => wire.Disposed),
            "Cancelling a drag immediately restores the baseline cage without a live evaluator rebuild");

        Curve[]? previous = null;
        var released = true;
        for (var step = 1; step <= 40; step++)
        {
            var current = Required(cache.Get(id, geometry, settings, Tolerance,
                Transform.Translation(step, step / 2.0, 0)));
            released &= previous is null || previous.All(wire => wire.Disposed);
            previous = current.Wires.ToArray();
        }
        Check(cache.Count == 1 && released,
            "A sustained drag disposes superseded curves and does not retain transform history");
        Check(geometry.DataCRC(0) == crc && geometry.GetUserString(CornerKey) == corners,
            "Cage cache updates and cancellation leave the original box geometry and metadata untouched");
    }

    private static void RemovalAndDisposal()
    {
        using var geometry = CreateBox();
        using var cache = new BendControlCageCache();
        var settings = new ControlBoxSettings(45);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = Required(cache.Get(firstId, geometry, settings, Tolerance));
        var second = Required(cache.Get(secondId, geometry, settings, Tolerance));
        var firstWires = first.Wires.ToArray();
        var secondWires = second.Wires.ToArray();
        Check(!ReferenceEquals(first, second) && cache.Count == 2,
            "Separate Control Boxes own independent cached cage lifetimes");
        cache.Retain([secondId]);
        Check(cache.Count == 1 && firstWires.All(wire => wire.Disposed) &&
            ReferenceEquals(second, cache.Get(secondId, geometry, settings, Tolerance)),
            "Removing a Control Box releases only its cage and retains surviving controls");
        cache.Clear();
        Check(cache.Count == 0 && secondWires.All(wire => wire.Disposed),
            "Clearing the session cage cache releases all remaining display curves");
        var fresh = Required(cache.Get(secondId, geometry, settings, Tolerance));
        var freshWires = fresh.Wires.ToArray();
        cache.Dispose();
        cache.Dispose();
        Check(cache.Count == 0 && freshWires.All(wire => wire.Disposed),
            "Repeated session disposal safely releases its current cage");
    }

    private static Brep CreateBox() => ControlBoxGeometry.Create(Plane.WorldXY, new Vector3d(20, 100, 20));
    private static BendControlCage Required(BendControlCage? cage) => cage ?? throw new Exception("Expected a valid Control Box cage.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}

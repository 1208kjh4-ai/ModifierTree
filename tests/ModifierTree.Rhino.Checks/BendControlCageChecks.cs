using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino.Geometry;
using Rhino.Input.Custom;

internal static class BendControlCageChecks
{
    private const double Tolerance = 0.001;
    private static Box DefaultBox => new(Plane.WorldXY, new Interval(-10, 10),
        new Interval(0, 100), new Interval(-10, 10));

    public static void Run()
    {
        ShapeAndSettings();
        DynamicFrame();
        FoldedDecoration();
        InvalidInputs();
        PickBentWires();
    }

    private static void ShapeAndSettings()
    {
        using var raw = ControlBoxGeometry.Create(DefaultBox);
        var crc = raw.DataCRC(0);
        var strings = raw.GetUserStrings();
        foreach (var strength in new[] { -180.0, -90, -0.0000001, 0, 0.0000001, 90, 180 })
        {
            using var limited = BendControlCage.Create(raw, new ControlBoxSettings(strength), Tolerance);
            using var unlimited = BendControlCage.Create(raw, new ControlBoxSettings(strength, false), Tolerance);
            Check(limited is { Bounds.IsValid: true, Wires.Count: >= 10 and <= 14 } &&
                limited.Wires.All(curve => curve.IsValid) && unlimited is not null &&
                SameWires(limited, unlimited),
                $"Bend cage {strength} degrees has bounded valid wires; modes agree within the box interval");
            var morph = new LengthPreservingBend(Plane.WorldXY, 100, strength * Math.PI / 180, true, Tolerance);
            var matched = true;
            foreach (var x in new[] { -10.0, 10 })
            foreach (var z in new[] { -10.0, 10 })
            foreach (var y in new[] { 0.0, 12.5, 37.5, 62.5, 87.5, 100 })
                matched &= DistanceToWires(limited!, morph.MorphPoint(new Point3d(x, y, z))) < Tolerance;
            Check(matched && Math.Abs(limited!.Wires[4].GetLength() - 100) < Tolerance,
                $"Bend cage {strength} degrees follows actual bend rails and preserves its neutral spine length");
        }
        using var flat = BendControlCage.Create(raw, new ControlBoxSettings(0), Tolerance);
        using var positive = BendControlCage.Create(raw, new ControlBoxSettings(90), Tolerance);
        using var negative = BendControlCage.Create(raw, new ControlBoxSettings(-90), Tolerance);
        Check(flat is not null && flat.Bounds.Min.DistanceTo(DefaultBox.BoundingBox.Min) < Tolerance &&
            flat.Bounds.Max.DistanceTo(DefaultBox.BoundingBox.Max) < Tolerance &&
            positive!.Bounds.Center.X > 20 && negative!.Bounds.Center.X < -20,
            "Zero-strength cage retains raw bounds and positive/negative Strength bend in opposite directions");
        Check(raw.DataCRC(0) == crc && strings.AllKeys.All(key => raw.GetUserString(key) == strings[key]) &&
            ControlBoxGeometry.TryGetBox(raw, Tolerance, out var restored, out _) &&
            restored.Center.DistanceTo(DefaultBox.Center) < Tolerance,
            "Creating display cages preserves native Control Box geometry and corner identity metadata");
    }

    private static void DynamicFrame()
    {
        var frame = new Plane(new Point3d(12, -8, 3), new Vector3d(1, 2, 0), new Vector3d(-2, 1, 0));
        using var raw = ControlBoxGeometry.Create(frame, new Vector3d(20, 100, 30));
        var crc = raw.DataCRC(0);
        var settings = new ControlBoxSettings(90);
        using var original = BendControlCage.Create(raw, settings, Tolerance);
        var transforms = new[]
        {
            Transform.Translation(5, -14, 20),
            Transform.Rotation(0.9, new Vector3d(1, 2, 3), new Point3d(7, -3, 9)),
            Transform.Scale(frame, 2, 0.5, 3),
            Transform.Scale(frame, -1, 2, -0.5)
        };
        for (var i = 0; i < transforms.Length; i++)
        {
            var transform = transforms[i];
            using var actual = BendControlCage.Create(raw, settings, Tolerance, transform);
            using var movedRaw = raw.DuplicateBrep();
            movedRaw.Transform(transform);
            using var expected = BendControlCage.Create(movedRaw, settings, Tolerance);
            Check(actual is not null && expected is not null && SameWires(actual, expected) && raw.DataCRC(0) == crc,
                $"Live Bend cage transform {i} recomputes from the transformed frame without editing its source");
            if (i < 2)
            {
                using var rigidSpine = original!.Wires[4].DuplicateCurve(); rigidSpine.Transform(transform);
                Check(rigidSpine.PointAtEnd.DistanceTo(actual!.Wires[4].PointAtEnd) < Tolerance,
                    $"Rigid Bend cage transform {i} carries the displayed bend with the control");
            }
            if (i == 2)
            {
                using var incorrectlyScaled = original!.Wires[4].DuplicateCurve(); incorrectlyScaled.Transform(transform);
                Check(incorrectlyScaled.PointAtEnd.DistanceTo(actual!.Wires[4].PointAtEnd) > 10 &&
                    Math.Abs(actual.Wires[4].GetLength() - 50) < Tolerance,
                    "Nonuniform live scale recomputes the bend radius and new Y length instead of scaling a bent cage");
            }
        }
    }

    private static void FoldedDecoration()
    {
        var radius = 100 / (Math.PI / 2);
        foreach (var halfWidth in new[] { radius, radius + 20 })
        {
            using var raw = ControlBoxGeometry.Create(new Box(Plane.WorldXY, new Interval(-halfWidth, halfWidth),
                new Interval(0, 100), new Interval(-10, 10)));
            using var cage = BendControlCage.Create(raw, new ControlBoxSettings(90), Tolerance);
            Check(cage is { Bounds.IsValid: true } && cage.Wires.All(wire => wire.IsValid && wire.GetLength() > 0) &&
                DistanceToWires(cage, new Point3d(radius, radius, 0)) < Tolerance,
                $"Decorative Bend cage remains valid when its inner rails {(halfWidth == radius ? "collapse" : "fold")} at the curvature center");
        }
    }

    private static void InvalidInputs()
    {
        using var raw = ControlBoxGeometry.Create(DefaultBox);
        using var unmarked = DefaultBox.ToBrep();
        var shear = Transform.Identity; shear.M01 = 0.5;
        Check(BendControlCage.Create(unmarked, ControlBoxSettings.Default, Tolerance) is null &&
            BendControlCage.Create(raw, new ControlBoxSettings(double.NaN), Tolerance) is null &&
            BendControlCage.Create(raw, new ControlBoxSettings(181), Tolerance) is null &&
            BendControlCage.Create(raw, ControlBoxSettings.Default, 0) is null &&
            BendControlCage.Create(raw, ControlBoxSettings.Default, Tolerance, Transform.Unset) is null &&
            BendControlCage.Create(raw, ControlBoxSettings.Default, Tolerance, shear) is null &&
            BendControlCage.Create(raw, ControlBoxSettings.Default, Tolerance, Transform.Scale(Point3d.Origin, 0)) is null,
            "Bend cage rejects damaged boxes, invalid settings/tolerance and unsupported dynamic transforms");
        using var before = BendControlCage.Create(raw, new ControlBoxSettings(45), Tolerance);
        using var other = BendControlCage.Create(raw, new ControlBoxSettings(-90), Tolerance);
        using var again = BendControlCage.Create(raw, new ControlBoxSettings(45), Tolerance);
        Check(before is not null && again is not null && other is not null && SameWires(before, again) &&
            before.Bounds.Center.DistanceTo(other.Bounds.Center) > 10,
            "Each Control Box cage depends on its own settings without sequentially deforming other controls");
    }

    private static bool SameWires(BendControlCage first, BendControlCage second) =>
        first.Wires.Count == second.Wires.Count && first.Wires.Zip(second.Wires).All(pair =>
            pair.First.PointAtStart.DistanceTo(pair.Second.PointAtStart) < Tolerance &&
            pair.First.PointAtEnd.DistanceTo(pair.Second.PointAtEnd) < Tolerance &&
            Math.Abs(pair.First.GetLength() - pair.Second.GetLength()) < Tolerance &&
            pair.First.GetBoundingBox(true).Min.DistanceTo(pair.Second.GetBoundingBox(true).Min) < Tolerance &&
            pair.First.GetBoundingBox(true).Max.DistanceTo(pair.Second.GetBoundingBox(true).Max) < Tolerance);

    private static void PickBentWires()
    {
        using var raw = ControlBoxGeometry.Create(DefaultBox);
        using var cage = BendControlCage.Create(raw, new ControlBoxSettings(90), Tolerance);
        var morph = new LengthPreservingBend(Plane.WorldXY, 100, Math.PI / 2, true, Tolerance);
        var bentEdge = morph.MorphPoint(new Point3d(10, 50, 10));
        using (var pick = PickAt(bentEdge))
            Check(PickGeometry.TestCurves(pick, cage!.Wires, cage.Bounds, out _, out _) &&
                !PickGeometry.Test(pick, raw, false, out _, out _),
                "Viewport picking hits the curved Control Box wire where no undeformed box edge exists");
        using (var pick = PickAt(new Point3d(10, 50, 10)))
            Check(!PickGeometry.TestCurves(pick, cage!.Wires, cage.Bounds, out _, out _) &&
                PickGeometry.Test(pick, raw, false, out _, out _),
                "Viewport cage picking ignores the obsolete straight box edge");
        using (var pick = PickAt(morph.MorphPoint(new Point3d(5, 12.5, 10))))
            Check(!PickGeometry.TestCurves(pick, cage!.Wires, cage.Bounds, out _, out _),
                "Control Box picking ignores the invisible face between bent rails and cross sections");
    }

    private static PickContext PickAt(Point3d point)
    {
        var pick = new PickContext
        {
            PickStyle = PickStyle.PointPick,
            PickLine = new Line(new Point3d(point.X, point.Y, 100), new Point3d(point.X, point.Y, -100))
        };
        pick.SetPickTransform(Transform.Scale(Plane.WorldXY, 100, 100, 0.01) *
            Transform.Translation(-point.X, -point.Y, 0));
        return pick;
    }

    private static double DistanceToWires(BendControlCage cage, Point3d point) => cage.Wires.Min(curve =>
        curve.ClosestPoint(point, out var parameter) ? curve.PointAt(parameter).DistanceTo(point) : double.PositiveInfinity);

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}

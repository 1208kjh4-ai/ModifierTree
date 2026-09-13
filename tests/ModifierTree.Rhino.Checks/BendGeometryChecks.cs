using System.IO;
using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino.FileIO;
using Rhino.Geometry;

internal static class BendGeometryChecks
{
    private const double Tolerance = 0.001;
    private static Box DefaultBox => new(Plane.WorldXY, new Interval(-10, 10), new Interval(0, 100), new Interval(-10, 10));

    public static void Run()
    {
        ControlGeometry();
        SingleBend();
        SafetyBounds();
        TreeControls();
        LiveControl();
    }

    private static void ControlGeometry()
    {
        var frame = new Plane(new Point3d(17, -8, 3), new Vector3d(1, 2, 0), new Vector3d(-2, 1, 0));
        var size = new Vector3d(20, 100, 30);
        using var original = ControlBoxGeometry.Create(frame, size);
        Check(ControlBoxGeometry.TryGetBox(original, Tolerance, out var initial, out _) &&
            initial.Center.DistanceTo(frame.Origin) < Tolerance && Math.Abs(initial.Y.Length - 100) < Tolerance,
            "Control Box center and local Y height read back from its native Brep");
        var transforms = new[]
        {
            Transform.Identity, Transform.Translation(15, -24, 9),
            Transform.Rotation(0.72, new Vector3d(1, 2, 3), Point3d.Origin),
            Transform.Scale(frame, 2, 0.5, 3), Transform.Scale(frame, -1, 2, -0.5)
        };
        using var file = new File3dm();
        var persisted = new Dictionary<Guid, Box>();
        for (var index = 0; index < transforms.Length; index++)
        {
            var transform = transforms[index];
            using var moved = original.DuplicateBrep();
            moved.Transform(transform);
            var center = frame.Origin; center.Transform(transform);
            var x = frame.XAxis; x.Transform(transform);
            var y = frame.YAxis; y.Transform(transform);
            var z = frame.ZAxis; z.Transform(transform);
            Check(ControlBoxGeometry.TryGetBox(moved, Tolerance, out var read, out _) &&
                read.Center.DistanceTo(center) < Tolerance &&
                Math.Abs(read.X.Length - size.X * x.Length) < Tolerance &&
                Math.Abs(read.Y.Length - size.Y * y.Length) < Tolerance &&
                Math.Abs(read.Z.Length - size.Z * z.Length) < Tolerance &&
                read.Plane.XAxis * x / x.Length > 1 - 1e-9 && read.Plane.YAxis * y / y.Length > 1 - 1e-9,
                $"Control Box retains its signed local axes after native transform {index}");
            persisted.Add(file.Objects.AddBrep(moved), read);
        }
        var path = Path.Combine(Path.GetTempPath(), $"ModifierTree-bend-box-{Guid.NewGuid():N}.3dm");
        try
        {
            Check(file.Write(path, new File3dmWriteOptions { Version = 8, SaveUserData = true }),
                "Control Box native geometry and corner identities save to a real 3dm");
            using var reopened = File3dm.Read(path);
            Check(reopened is not null && reopened.Objects.Count == transforms.Length &&
                reopened.Objects.All(item => ControlBoxGeometry.TryGetBox(item.Geometry, Tolerance, out var read, out _) &&
                    persisted.TryGetValue(item.Id, out var expected) && read.Center.DistanceTo(expected.Center) < Tolerance &&
                    read.Plane.XAxis * expected.Plane.XAxis > 1 - 1e-9 && read.Plane.YAxis * expected.Plane.YAxis > 1 - 1e-9 &&
                    Math.Abs(read.Y.Length - expected.Y.Length) < Tolerance),
                "Transformed Control Box frames restore after a real 3dm round trip");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        using var sheared = original.DuplicateBrep();
        var shear = Transform.Identity; shear.M01 = 0.4;
        sheared.Transform(shear);
        Check(!ControlBoxGeometry.TryGetBox(sheared, Tolerance, out _, out var error) && error.Contains("perpendicular"),
            "Sheared Control Box fails explicitly instead of guessing a world-aligned box");
        using var unmarked = DefaultBox.ToBrep();
        Check(!ControlBoxGeometry.TryGetBox(unmarked, Tolerance, out _, out _) &&
            !ControlBoxGeometry.TryGetBox(original, double.NaN, out _, out _),
            "Control Box rejects missing axis identities and invalid tolerance");
    }

    private static void SingleBend()
    {
        using var source = Source();
        var crc = source.DataCRC(0);
        foreach (var limited in new[] { true, false })
        foreach (var strength in new[] { -90.0, 0, 90, 180 })
        {
            var results = BendEvaluator.Evaluate([[source]], [new BendControl(DefaultBox, new ControlBoxSettings(strength, limited))], Tolerance);
            try
            {
                Check(results is [{ IsValid: true, IsSolid: true }] &&
                    results[0].Edges.All(edge => edge.Valence != EdgeAdjacency.Naked) && source.DataCRC(0) == crc,
                    $"Bend {strength} degrees Limited={limited} retains a closed solid and preserves its source");
            }
            finally { Dispose(results); }
        }
        var limitedMorph = new LengthPreservingBend(Plane.WorldXY, 100, Math.PI / 2, true, Tolerance / 10);
        var unlimitedMorph = new LengthPreservingBend(Plane.WorldXY, 100, Math.PI / 2, false, Tolerance / 10);
        using var axis = new LineCurve(new Point3d(0, -25, 0), new Point3d(0, 125, 0)).ToNurbsCurve();
        Check(limitedMorph.Morph(axis) && Math.Abs(axis.GetLength() - 150) < Tolerance,
            "Production Bend preserves neutral-axis arc length across Limited boundaries");
        var below = new Point3d(4, -25, 0);
        var top1 = limitedMorph.MorphPoint(new Point3d(4, 110, 0));
        var top2 = limitedMorph.MorphPoint(new Point3d(4, 125, 0));
        Check(limitedMorph.MorphPoint(below).DistanceTo(below) < Tolerance &&
            (top2 - top1 - new Vector3d(15, 0, 0)).Length < Tolerance &&
            unlimitedMorph.MorphPoint(below).DistanceTo(below) > 1,
            "Limited exterior follows straight end tangents while Unlimited keeps bending");

        var bent = BendEvaluator.Evaluate([[source]], [new BendControl(DefaultBox, new ControlBoxSettings(90))], Tolerance);
        try
        {
            var center = limitedMorph.MorphPoint(new Point3d(0, 50, 0));
            using var cutter = new Cylinder(new Circle(new Plane(center - Vector3d.ZAxis * 10, Vector3d.ZAxis), 2), 20).ToBrep(true, true);
            var difference = DifferenceEvaluator.Evaluate(bent, [cutter], Tolerance);
            try { Check(difference.Length == 1 && difference[0].IsSolid && difference[0].GetVolume() < bent[0].GetVolume(),
                "Production Bend result remains usable in Boolean Difference"); }
            finally { Dispose(difference); }
            using var sphere = new Sphere(center + Vector3d.ZAxis * 4, 5).ToBrep();
            var union = BooleanEvaluator.Evaluate(TreeNodeKind.BooleanUnion, [bent, new[] { sphere }], Tolerance);
            try { Check(union.Length == 1 && union[0].IsSolid && union[0].GetVolume() > bent[0].GetVolume(),
                "Production Bend result remains usable in Boolean Union"); }
            finally { Dispose(union); }
        }
        finally { Dispose(bent); }
    }

    private static void SafetyBounds()
    {
        var rotatedFrame = new Plane(new Point3d(23, -17, 9),
            new Vector3d(Math.Cos(0.61), Math.Sin(0.61), 0),
            new Vector3d(-Math.Sin(0.61), Math.Cos(0.61), 0));
        foreach (var (strength, frame, wingStart, wingEnd, label) in new[]
        {
            (90.0, Plane.WorldXY, 115.0, 135.0, "positive world upper"),
            (-90.0, rotatedFrame, -35.0, -15.0, "negative rotated lower")
        })
        {
            var box = new Box(frame, new Interval(-10, 10), new Interval(0, 100), new Interval(-10, 10));
            using var outsideWing = WingedSource(frame, Math.Sign(strength), -40, wingStart, wingEnd, 140);
            var crc = outsideWing.DataCRC(0);
            var limited = BendEvaluator.Evaluate([[outsideWing]],
                [new BendControl(box, new ControlBoxSettings(strength))], Tolerance);
            try
            {
                Check(limited is [{ IsValid: true, IsSolid: true }] &&
                    limited[0].Edges.All(edge => edge.Valence != EdgeAdjacency.Naked) &&
                    outsideWing.DataCRC(0) == crc,
                    $"Limited Bend ignores a wide {label} wing wholly beyond its active Y slab and preserves the source");
            }
            finally { Dispose(limited); }
            Check(Fails(() => BendEvaluator.Evaluate([[outsideWing]],
                    [new BendControl(box, new ControlBoxSettings(strength, false))], Tolerance), "curvature center") &&
                outsideWing.DataCRC(0) == crc,
                $"Unlimited Bend still rejects the same wide {label} wing because every Y position is active");

            using var activeWing = WingedSource(frame, Math.Sign(strength), -40, 40, 60, 140);
            var activeCrc = activeWing.DataCRC(0);
            Check(Fails(() => BendEvaluator.Evaluate([[activeWing]],
                    [new BendControl(box, new ControlBoxSettings(strength))], Tolerance), "curvature center") &&
                activeWing.DataCRC(0) == activeCrc,
                $"Limited Bend still rejects a true {label} collapse inside its active Y slab");
        }

        using var outsideDisplayWidth = new BoundingBox(90, 10, -3, 120, 90, 3).ToBrep();
        Check(Fails(() => BendEvaluator.Evaluate([[outsideDisplayWidth]],
            [new BendControl(DefaultBox, new ControlBoxSettings(90))], Tolerance), "curvature center"),
            "Bend collapse checks actual input width, including geometry outside displayed Control Box width");
        using var longInput = new BoundingBox(-2, -60, -3, 2, 160, 3).ToBrep();
        Check(Fails(() => BendEvaluator.Evaluate([[longInput]],
            [new BendControl(DefaultBox, new ControlBoxSettings(180, false))], Tolerance), "full turn"),
            "Unlimited rejects an actual input span that would wrap through a full turn");
        using var above = new BoundingBox(90, 120, -3, 120, 140, 3).ToBrep();
        var rigid = BendEvaluator.Evaluate([[above]], [new BendControl(DefaultBox, new ControlBoxSettings(90))], Tolerance);
        try { Check(rigid is [{ IsValid: true, IsSolid: true }] && Math.Abs(rigid[0].GetVolume() - above.GetVolume()) < Tolerance,
            "Limited input entirely above the box uses rigid extension without a false collapse error"); }
        finally { Dispose(rigid); }
    }

    private static void TreeControls()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Source());
        var bend = fixture.Modifier(source);
        fixture.Evaluate();
        Check(fixture.Result(bend) is { IsCurrent: false, Status: "Needs Control Box" },
            "A Bend with an input waits for an explicit Control Box");
        var first = fixture.Control(bend, DefaultBox, new ControlBoxSettings(45));
        var secondFrame = new Plane(new Point3d(-10, 20, 0), Vector3d.XAxis, Vector3d.YAxis);
        secondFrame.Rotate(0.4, Vector3d.ZAxis);
        var secondBox = new Box(secondFrame, new Interval(-10, 10), new Interval(0, 100), new Interval(-10, 10));
        var second = fixture.Control(bend, secondBox, new ControlBoxSettings(-35));
        fixture.Evaluate();
        Check(fixture.Result(bend) is { IsCurrent: true, Results.Count: 1 } && fixture.Evaluator.InputPreviews.Count == 1,
            "Two Control Boxes affect one input without becoming geometry outputs or input-wire operands");
        var firstBounds = Bounds(fixture.Result(bend));
        var controlsBefore = new[] { first, second }.Select(id => fixture.Source(fixture.ObjectId(id))!.DataCRC(0)).ToArray();
        fixture.Tree.Move(second, bend, 0, out _); fixture.Evaluate();
        Check(fixture.Result(bend).IsCurrent && firstBounds.Center.DistanceTo(Bounds(fixture.Result(bend)).Center) > 0.1,
            "Reordering Control Boxes changes the sequential Bend result");
        Check(controlsBefore.SequenceEqual(new[] { first, second }.Select(id => fixture.Source(fixture.ObjectId(id))!.DataCRC(0))),
            "Sequential Bend never deforms either Control Box itself");
        using var fixedResult = ResultGeometry.Create(fixture.Result(bend));
        Check(fixedResult.IsValid && fixedResult.IsSolid &&
            Math.Abs(fixedResult.GetVolume() - fixture.Result(bend).Results[0].GetVolume()) < Tolerance,
            "Production Bake/Merge result geometry contains only the bent input");
        var remote = fixture.Add(Source(30)); fixture.Tree.Move(remote, bend, fixture.Tree.Find(bend)!.Children.Count, out _);
        fixture.Evaluate();
        Check(fixture.Result(bend) is { IsCurrent: true, Results.Count: 2 } && fixture.Evaluator.InputPreviews.Count == 2,
            "Bend applies its controls to every geometry child and preserves separate result pieces");
        fixture.Replace(first, new LineCurve(Point3d.Origin, new Point3d(1, 0, 0))); fixture.Evaluate();
        Check(fixture.Result(bend) is { IsCurrent: false, HasResult: true, Status: "Invalid box", Results.Count: 2 },
            "A damaged Control Box keeps the last valid Bend result and reports its failure");
        fixture.Tree.SetModifierEnabled(bend, false, out _); fixture.Evaluate();
        Check(fixture.Result(bend) is { IsCurrent: true, Results.Count: 1 } &&
            Bounds(fixture.Result(bend)).Min.DistanceTo(SourceBounds().Min) < Tolerance,
            "Disabled Bend bypasses its first geometric child even when a Control Box is damaged");
        fixture.Replace(first, ControlBoxGeometry.Create(DefaultBox));
        fixture.Tree.SetModifierEnabled(bend, true, out _); fixture.Evaluate();
        Check(fixture.Result(bend).IsCurrent, "Repairing a Control Box recovers the Bend result");
        fixture.Replace(source, new BoundingBox(-4, 0, -5, 400, 100, 5).ToBrep()); fixture.Evaluate();
        Check(fixture.Result(bend) is { IsCurrent: false, HasResult: true } &&
            fixture.Result(bend).Error!.Contains("curvature center"),
            "Unsafe source edits retain the last valid Bend cache instead of committing collapsed solids");
    }

    private static void LiveControl()
    {
        using var fixture = new Fixture();
        var source = fixture.Add(Source());
        var bend = fixture.Modifier(source);
        var control = fixture.Control(bend, DefaultBox, new ControlBoxSettings(60)); fixture.Evaluate();
        using var live = new LiveTreePreview();
        var baseline = Bounds(fixture.Result(bend));
        var move = Transform.Translation(17, 4, -9);
        var whole = fixture.Tree.SourcesInSubtree(bend).ToDictionary(id => id, _ => move);
        live.Update(fixture.Tree, fixture.Source, whole, Tolerance, fixture.Evaluator, bend);
        Check(live.Find(bend) is { IsCurrent: true } &&
            Bounds(live.Find(bend)!).Center.DistanceTo(baseline.Center + new Vector3d(17, 4, -9)) < Tolerance &&
            live.LastReusedModifierCount == 1,
            "Whole Bend translation carries inputs and boxes and reuses the valid result");
        var rotate = Transform.Rotation(0.5, Vector3d.ZAxis, Point3d.Origin);
        whole = fixture.Tree.SourcesInSubtree(bend).ToDictionary(id => id, _ => rotate);
        live.Update(fixture.Tree, fixture.Source, whole, Tolerance, fixture.Evaluator, bend);
        using var expected = fixture.Result(bend).Results[0].DuplicateBrep(); expected.Transform(rotate);
        var expectedBounds = expected.GetBoundingBox(true); var actualBounds = Bounds(live.Find(bend)!);
        Check(live.Find(bend)!.IsCurrent && actualBounds.Min.DistanceTo(expectedBounds.Min) < Tolerance &&
            actualBounds.Max.DistanceTo(expectedBounds.Max) < Tolerance,
            "Whole Bend rotation carries the Control Box frame with its input in live evaluation");
        var one = new Dictionary<Guid, Transform> { [fixture.ObjectId(control)] = Transform.Translation(10, 0, 0) };
        live.Update(fixture.Tree, fixture.Source, one, Tolerance, fixture.Evaluator, control);
        Check(live.Find(bend)!.IsCurrent && Bounds(live.Find(bend)!).Center.DistanceTo(baseline.Center) > 1 &&
            Bounds(fixture.Result(bend)).Center.DistanceTo(baseline.Center) < Tolerance,
            "Moving only Control Box changes the live Bend while preserving the source and committed result");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Dictionary<Guid, GeometryBase> _sources = [];
        public ModifierTreeModel Tree { get; } = new();
        public TreeEvaluator Evaluator { get; } = new();
        public GeometryBase? Source(Guid id) => _sources.GetValueOrDefault(id);
        public Guid ObjectId(Guid id) => Tree.Find(id)!.ObjectId!.Value;
        public NodeEvaluation Result(Guid id) => Evaluator.Find(id)!;
        public void Evaluate() => Evaluator.Rebuild(Tree, Source, Tolerance);
        public Guid Add(GeometryBase geometry)
        {
            var id = Guid.NewGuid(); _sources.Add(id, geometry); Tree.RegisterSource(id); return Tree.FindSource(id)!.Id;
        }
        public Guid Modifier(Guid input)
        {
            var node = Tree.AddModifier(TreeNodeKind.Bend); Tree.Move(input, node.Id, 0, out _); return node.Id;
        }
        public Guid Control(Guid bend, Box box, ControlBoxSettings settings)
        {
            var id = Guid.NewGuid(); _sources.Add(id, ControlBoxGeometry.Create(box));
            if (!Tree.AddControlBox(bend, id, out var error)) throw new Exception(error);
            var node = Tree.FindSource(id)!;
            if (!Tree.SetControlBoxSettings(node.Id, settings, out error)) throw new Exception(error);
            return node.Id;
        }
        public void Replace(Guid node, GeometryBase geometry)
        { var id = ObjectId(node); _sources[id].Dispose(); _sources[id] = geometry; }
        public void Dispose() { Evaluator.Dispose(); foreach (var source in _sources.Values) source.Dispose(); }
    }

    private static BoundingBox SourceBounds(double z = 0) => new(-4, 0, z - 5, 4, 100, z + 5);
    private static Brep Source(double z = 0) => SourceBounds(z).ToBrep();
    private static Brep WingedSource(Plane frame, int side, double stemStart,
        double wingStart, double wingEnd, double stemEnd)
    {
        const double stemHalfWidth = 3;
        const double wingExtent = 100;
        var points = new[]
        {
            new Point3d(-stemHalfWidth, stemStart, -3), new Point3d(stemHalfWidth, stemStart, -3),
            new Point3d(stemHalfWidth, wingStart, -3), new Point3d(wingExtent, wingStart, -3),
            new Point3d(wingExtent, wingEnd, -3), new Point3d(stemHalfWidth, wingEnd, -3),
            new Point3d(stemHalfWidth, stemEnd, -3), new Point3d(-stemHalfWidth, stemEnd, -3),
            new Point3d(-stemHalfWidth, stemStart, -3)
        };
        if (side < 0)
            points = points.Select(point => new Point3d(-point.X, point.Y, point.Z)).Reverse().ToArray();
        using var profile = new PolylineCurve(points);
        using var extrusion = Extrusion.Create(profile, 6, true)
            ?? throw new InvalidOperationException("Could not create the winged Bend safety fixture.");
        var source = extrusion.ToBrep();
        var toFrame = Transform.PlaneToPlane(Plane.WorldXY, frame);
        if (!source.Transform(toFrame) || !source.IsValid || !source.IsSolid ||
            source.Edges.Any(edge => edge.Valence == EdgeAdjacency.Naked))
        {
            source.Dispose();
            throw new InvalidOperationException("Could not create a closed winged Bend safety fixture.");
        }
        return source;
    }
    private static BoundingBox Bounds(NodeEvaluation result)
    { var box = BoundingBox.Empty; foreach (var brep in result.Results) box.Union(brep.GetBoundingBox(true)); return box; }
    private static bool Fails(Func<Brep[]> evaluate, string message)
    {
        try { Dispose(evaluate()); return false; }
        catch (InvalidOperationException exception) { return exception.Message.Contains(message, StringComparison.Ordinal); }
    }
    private static void Dispose(IEnumerable<Brep> breps) { foreach (var brep in breps) brep.Dispose(); }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}

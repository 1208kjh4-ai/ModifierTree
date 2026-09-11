using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using Rhino.Geometry.Morphs;

internal static class BendProbe
{
    private const double Tolerance = 0.001;
    // Surface fitting tolerance is pointwise; it does not directly bound integrated
    // curve length. A tighter fit resolved the measured 270-degree curve length error.
    private const double FitTolerance = Tolerance / 10;
    private static readonly List<object> Measurements = [];
    private static int _failures;

    public static int Run(string output)
    {
        Directory.CreateDirectory(output);
        Console.WriteLine($"RhinoCommon {typeof(Brep).Assembly.GetName().Version}; documentTolerance={Tolerance}; fitTolerance={FitTolerance}; units=mm");
        using var file = new File3dm();
        file.Settings.ModelUnitSystem = Rhino.UnitSystem.Millimeters;
        file.Settings.ModelAbsoluteTolerance = Tolerance;
        CompareNative();
        foreach (var limited in new[] { true, false })
        foreach (var degrees in new[] { 0.0, 45, 90, -90, 180 })
        foreach (var shape in new[] { "box", "cylinder", "hole", "boundary" })
        {
            using var source = CreateSource(shape);
            var morph = new LengthPreservingBend(Plane.WorldXY, 100, degrees * Math.PI / 180, limited, FitTolerance);
            Evaluate($"custom-{(limited ? "limited" : "unlimited")}-{degrees}-{shape}", source, morph, file);
        }
        GeometryChecks();
        File.WriteAllText(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(Measurements,
            new JsonSerializerOptions { WriteIndented = true }));
        Check(file.Write(Path.Combine(output, "bend-samples.3dm"), 8), "write standalone sample Breps to 3dm");
        using var roundtrip = File3dm.Read(Path.Combine(output, "bend-samples.3dm"));
        Check(roundtrip is not null && roundtrip.Objects.Count == 8 &&
            roundtrip.Objects.All(o => o.Geometry is Brep { IsValid: true, IsSolid: true }),
            "read 8 standalone closed Breps back from 3dm");
        Console.WriteLine($"COMPLETE failures={_failures}; output={Path.GetFullPath(output)}");
        return _failures == 0 ? 0 : 2;
    }

    private static void CompareNative()
    {
        foreach (var straight in new[] { true, false })
        foreach (var directionPoint in new[] { new Point3d(100, 100, 0), new Point3d(200 / Math.PI, 200 / Math.PI, 0) })
        {
            using var native = new BendSpaceMorph(Point3d.Origin, new Point3d(0, 100, 0),
                directionPoint, Math.PI / 2, straight, false)
                { Tolerance = FitTolerance, PreserveStructure = false, QuickPreview = false };
            var expected = new LengthPreservingBend(Plane.WorldXY, 100, Math.PI / 2, true, FitTolerance);
            var points = new[] { -25.0, 0, 25, 50, 75, 100, 125, 150 }.Select(y =>
            {
                var p = native.MorphPoint(new Point3d(0, y, 0));
                return new { sourceY = y, p.X, p.Y, p.Z };
            }).ToArray();
            var length = SampleLength(native, 0, 100);
            var delta = Enumerable.Range(0, 101).Max(i => native.MorphPoint(new Point3d(0, i, 0))
                .DistanceTo(expected.MorphPoint(new Point3d(0, i, 0))));
            Console.WriteLine($"NATIVE straight={straight} pointX={directionPoint.X:F6} valid={native.IsValid} neutralLength={length:F9} customDelta={delta:F6} end={native.MorphPoint(new Point3d(0, 100, 0))}");
            Measurements.Add(new { kind = "native-points", straight, pointX = directionPoint.X, length, delta, points });
            using var source = CreateSource("box");
            using var result = source.DuplicateBrep();
            var timer = Stopwatch.StartNew();
            var success = native.Morph(result);
            timer.Stop();
            Console.WriteLine($"NATIVE BREP straight={straight} morph={success} valid={result.IsValid} solid={result.IsSolid} ms={timer.Elapsed.TotalMilliseconds:F3}");
        }
    }

    private static void Evaluate(string name, Brep source, SpaceMorph morph, File3dm samples)
    {
        var sourceCrc = source.DataCRC(0);
        using var result = source.DuplicateBrep();
        var timer = Stopwatch.StartNew();
        var success = morph.Morph(result);
        timer.Stop();
        var naked = result.Edges.Count(e => e.Valence == EdgeAdjacency.Naked);
        using var volume = VolumeMassProperties.Compute(result);
        var valid = success && result.IsValid && result.IsSolid && naked == 0 &&
            result.SolidOrientation == BrepSolidOrientation.Outward && volume is { Volume: > 0 };
        var maxDeviation = SampleDeviation(source, result, morph);
        if (name.EndsWith("-boundary")) maxDeviation = Math.Max(maxDeviation, BoundaryDeviation(result, morph));
        Check(valid && maxDeviation <= Tolerance && source.DataCRC(0) == sourceCrc,
            $"{name}: solid={result.IsSolid}, valid={result.IsValid}, naked={naked}, deviation={maxDeviation:G4}, ms={timer.Elapsed.TotalMilliseconds:F2}");
        Measurements.Add(new { kind = "brep", name, success, valid = result.IsValid, solid = result.IsSolid,
            naked, volume = volume?.Volume, maxDeviation, milliseconds = timer.Elapsed.TotalMilliseconds,
            faces = result.Faces.Count, edgeCount = result.Edges.Count, sourceUnchanged = sourceCrc == source.DataCRC(0) });
        // Display only a representative subset, laid out for manual opening without a plugin.
        if (name.Contains("-90-") && !name.Contains("--90-"))
        {
            using var display = result.DuplicateBrep();
            var index = samples.Objects.Count;
            display.Translate(new Vector3d((index % 4) * 190, (index / 4) * 220, 0));
            using var attributes = new ObjectAttributes { Name = name };
            samples.Objects.AddBrep(display, attributes);
        }
    }

    private static void GeometryChecks()
    {
        foreach (var limited in new[] { true, false })
        foreach (var degrees in new[] { -180.0, -90, 0, 1e-7, 45, 90, 180 })
        {
            var morph = new LengthPreservingBend(Plane.WorldXY, 100, degrees * Math.PI / 180, limited, FitTolerance);
            var length = SampleLength(morph, -25, 125);
            Check(Math.Abs(length - 150) < 0.00001, $"neutral axis {degrees} limited={limited} length={length:F9}/150");
            using var curve = new Line(new Point3d(0, -25, 0), new Point3d(0, 125, 0)).ToNurbsCurve();
            var curveOk = morph.Morph(curve);
            var curveLength = curve.GetLength();
            Check(curveOk && curve.IsValid && Math.Abs(curveLength - 150) < Tolerance,
                $"refitted neutral NURBS curve {degrees} limited={limited} length={curveLength:F9}/150");
            Measurements.Add(new { kind = "neutral-curve", limited, degrees, sampledLength = length, curveLength, curveOk });
        }
        var first = new LengthPreservingBend(Plane.WorldXY, 100, Math.PI / 2, true, FitTolerance);
        var unlimited = new LengthPreservingBend(Plane.WorldXY, 100, Math.PI / 2, false, FitTolerance);
        var below = new Point3d(4, -25, 3);
        Check(first.MorphPoint(below).DistanceTo(below) < 1e-10, "Limited leaves the region below the box unchanged");
        var topA = first.MorphPoint(new Point3d(4, 110, 3));
        var topB = first.MorphPoint(new Point3d(4, 125, 3));
        Check((topB - topA - new Vector3d(15, 0, 0)).Length < 1e-10,
            "Limited upper region stays straight and follows the end tangent");
        Check(unlimited.MorphPoint(below).DistanceTo(below) > 1 &&
            Math.Abs(unlimited.MorphPoint(new Point3d(0, 125, 0)).Y - unlimited.MorphPoint(new Point3d(0, 110, 0)).Y) > 1,
            "Unlimited bends both below and above the box height");
        var radius = 200 / Math.PI;
        Check(first.MorphPoint(new Point3d(0, 100, 0)).DistanceTo(new Point3d(radius, radius, 0)) < 1e-10,
            "90 degree strength gives the expected quarter-circle endpoint");
        using var box = CreateSource("box");
        using var bent = box.DuplicateBrep();
        Check(first.Morph(bent), "prepare bent solid for Boolean checks");
        var center = first.MorphPoint(new Point3d(0, 50, 0));
        using var cutter = new Cylinder(new Circle(new Plane(center - new Vector3d(0, 0, 10), Vector3d.ZAxis), 2), 20).ToBrep(true, true);
        var difference = Brep.CreateBooleanDifference(bent, cutter, Tolerance);
        CheckSolids(difference, "Difference after Bend", Volume(bent), false);
        using var sphere = new Sphere(center + new Vector3d(0, 0, 4), 5).ToBrep();
        var union = Brep.CreateBooleanUnion(new[] { bent, sphere }, Tolerance);
        CheckSolids(union, "Union after Bend", Volume(bent), true);
        var secondFrame = new Plane(new Point3d(0, 0, 0), Vector3d.ZAxis, Vector3d.YAxis);
        var second = new LengthPreservingBend(secondFrame, 100, Math.PI / 4, false, FitTolerance);
        using var ab = box.DuplicateBrep();
        using var ba = box.DuplicateBrep();
        var abOk = first.Morph(ab) && second.Morph(ab);
        var baOk = second.Morph(ba) && first.Morph(ba);
        Check(abOk && baOk && ab.IsValid && ba.IsValid && ab.IsSolid && ba.IsSolid, "two boxes sequentially preserve a valid closed Brep in this example");
        var abDeviation = SampleDeviation(box, ab, new CompositeMorph(first, second));
        var baDeviation = SampleDeviation(box, ba, new CompositeMorph(second, first));
        Check(abDeviation <= Tolerance * 2 && baDeviation <= Tolerance * 2,
            $"sequential Breps follow the composed deformation: AB={abDeviation:G4}, BA={baDeviation:G4}");
        var p = new Point3d(0, 90, 0);
        var orderDistance = second.MorphPoint(first.MorphPoint(p)).DistanceTo(first.MorphPoint(second.MorphPoint(p)));
        Check(orderDistance > 1, $"box order changes geometry: delta={orderDistance:F6}");
        var fortyFive = new LengthPreservingBend(Plane.WorldXY, 100, Math.PI / 4, true, FitTolerance);
        var sequentialLength = SampleLength(new CompositeMorph(fortyFive, fortyFive), 0, 100);
        using var sequentialCurve = new Line(Point3d.Origin, new Point3d(0, 100, 0)).ToNurbsCurve();
        var sequenceCurveOk = fortyFive.Morph(sequentialCurve) && fortyFive.Morph(sequentialCurve);
        Check(sequenceCurveOk && Math.Abs(sequentialCurve.GetLength() - sequentialLength) < Tolerance * 2,
            $"same-frame 45+45 degrees: point-curve={sequentialLength:F6}, refitted curve={sequentialCurve.GetLength():F6}/100");
        Console.WriteLine($"OBSERVATION two same-frame boxes do not preserve the already-curved input length: {sequentialLength:F9}/100");
        Measurements.Add(new { kind = "sequence", orderDistance, sequentialLength, refittedLength = sequentialCurve.GetLength(), abOk, baOk, abDeviation, baDeviation });
        var rigid = Transform.Rotation(0.63, new Vector3d(1, 2, 3), Point3d.Origin);
        rigid = Transform.Translation(23, -31, 17) * rigid;
        var frame = Plane.WorldXY;
        frame.Transform(rigid);
        var movedMorph = new LengthPreservingBend(frame, 100, Math.PI / 2, true, FitTolerance);
        var maxRigidError = 0.0;
        foreach (var y in new[] { -25.0, 0, 30, 100, 125 })
        {
            var original = new Point3d(4, y, 3);
            var expected = first.MorphPoint(original); expected.Transform(rigid);
            original.Transform(rigid);
            maxRigidError = Math.Max(maxRigidError, expected.DistanceTo(movedMorph.MorphPoint(original)));
        }
        Check(maxRigidError < 1e-10, $"moving and rotating the whole frame carries bend direction: error={maxRigidError:G4}");
    }

    private static Brep CreateSource(string shape)
    {
        if (shape == "cylinder")
            return new Cylinder(new Circle(new Plane(Point3d.Origin, Vector3d.YAxis), 4), 100).ToBrep(true, true);
        var box = new Box(Plane.WorldXY, new Interval(-5, 5),
            shape == "boundary" ? new Interval(-25, 125) : new Interval(0, 100), new Interval(-4, 4)).ToBrep();
        if (shape != "hole") return box;
        using (box)
        using (var cutter = new Cylinder(new Circle(new Plane(new Point3d(0, 50, -10), Vector3d.ZAxis), 2), 20).ToBrep(true, true))
        {
            var result = Brep.CreateBooleanDifference(box, cutter, Tolerance);
            if (result is not { Length: 1 }) throw new InvalidOperationException("Cannot create perforated input.");
            return result[0];
        }
    }

    private static double SampleLength(SpaceMorph morph, double from, double to)
    {
        const int divisions = 10000;
        var last = morph.MorphPoint(new Point3d(0, from, 0));
        var length = 0.0;
        for (var i = 1; i <= divisions; i++)
        {
            var next = morph.MorphPoint(new Point3d(0, from + (to - from) * i / divisions, 0));
            length += next.DistanceTo(last);
            last = next;
        }
        return length;
    }

    private static double SampleDeviation(Brep original, Brep result, SpaceMorph morph)
    {
        var max = 0.0;
        foreach (var face in original.Faces)
        for (var i = 0; i <= 11; i++)
        for (var j = 0; j <= 11; j++)
        {
            var u = face.Domain(0).ParameterAt(i / 11.0);
            var v = face.Domain(1).ParameterAt(j / 11.0);
            if (face.IsPointOnFace(u, v) == PointFaceRelation.Exterior) continue;
            var expected = morph.MorphPoint(face.PointAt(u, v));
            var closest = result.ClosestPoint(expected);
            max = Math.Max(max, closest.IsValid ? closest.DistanceTo(expected) : double.PositiveInfinity);
        }
        return max;
    }

    private static double BoundaryDeviation(Brep result, SpaceMorph morph)
    {
        var max = 0.0;
        foreach (var y in new[] { -0.01, 0, 0.01, 99.99, 100, 100.01 })
        foreach (var x in new[] { -5.0, 5 })
        foreach (var z in new[] { -4.0, 0, 4 })
        {
            var expected = morph.MorphPoint(new Point3d(x, y, z));
            max = Math.Max(max, expected.DistanceTo(result.ClosestPoint(expected)));
        }
        return max;
    }

    private static double Volume(Brep brep) { using var mass = VolumeMassProperties.Compute(brep); return mass?.Volume ?? double.NaN; }

    private static void CheckSolids(Brep[]? results, string name, double previousVolume, bool increasing)
    {
        try
        {
            var volume = results?.Sum(Volume) ?? double.NaN;
            Check(results is { Length: 1 } && results.All(b => b.IsValid && b.IsSolid) &&
                (increasing ? volume > previousVolume + 1 : volume < previousVolume - 1 && volume > 0),
                $"{name}: pieces={results?.Length}, volume={volume:F4}, before={previousVolume:F4}");
        }
        finally { if (results != null) foreach (var result in results) result.Dispose(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) _failures++;
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {message}");
    }

    private sealed class CompositeMorph(SpaceMorph a, SpaceMorph b) : SpaceMorph
    {
        public override Point3d MorphPoint(Point3d point) => b.MorphPoint(a.MorphPoint(point));
    }
}

using System.Diagnostics;
using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino.Geometry;

internal static class LiveOptimizationChecks
{
    public static void Run()
    {
        var tree = new ModifierTreeModel();
        var sources = new Dictionary<Guid, GeometryBase>();
        Guid Box(double x0, double x1, double y0 = 0, double y1 = 10, double z0 = 0, double z1 = 10)
        {
            var id = Guid.NewGuid();
            sources.Add(id, new BoundingBox(x0, y0, z0, x1, y1, z1).ToBrep());
            tree.RegisterSource(id);
            return id;
        }
        Guid Difference(params Guid[] inputs)
        {
            var node = tree.AddModifier();
            foreach (var input in inputs) tree.Move(tree.FindSource(input)?.Id ?? input, node.Id, node.Children.Count, out _);
            return node.Id;
        }
        var a = Box(0, 10); var b = Box(5, 20, -1, 11, -1, 11); var c = Box(0, 2);
        var child = Difference(a, b); var root = Difference(child, c);
        var d = Box(30, 40); var e = Box(35, 45); var other = Difference(d, e);
        var f = Box(60, 70); var g = Box(65, 75); _ = Difference(f, g);
        GeometryBase? Source(Guid id) => sources.GetValueOrDefault(id);
        using var committed = new TreeEvaluator();
        using var live = new LiveTreePreview();
        using var reference = new TreeEvaluator();
        committed.Rebuild(tree, Source, 0.001);
        Dictionary<Guid, Transform> Move(IEnumerable<Guid> ids, double x) => ids.ToDictionary(id => id, _ => Transform.Translation(x, 0, 0));
        void Full(IReadOnlyDictionary<Guid, Transform> transforms, double tolerance = 0.001)
        {
            var moved = new Dictionary<Guid, GeometryBase>();
            try
            {
                GeometryBase? Read(Guid id)
                {
                    if (Source(id) is not { } geometry || !transforms.TryGetValue(id, out var transform)) return Source(id);
                    var copy = geometry.Duplicate(); moved.Add(id, copy); copy.Transform(transform); return copy;
                }
                reference.Rebuild(tree, Read, tolerance);
            }
            finally { foreach (var geometry in moved.Values) geometry.Dispose(); }
        }
        bool Equivalent()
        {
            foreach (var id in tree.Roots.Where(id => tree.Find(id)!.IsModifier))
            {
                var actual = live.Find(id)!; var expected = reference.Find(id)!;
                if (actual.HasResult != expected.HasResult || actual.IsCurrent != expected.IsCurrent || actual.Results.Count != expected.Results.Count) return false;
                if (Math.Abs(actual.Results.Sum(brep => brep.GetVolume()) - expected.Results.Sum(brep => brep.GetVolume())) > 0.01) return false;
                if (actual.Results.Count == 0) continue;
                var aBox = BoundingBox.Empty; var bBox = BoundingBox.Empty;
                foreach (var brep in actual.Results) aBox.Union(brep.GetBoundingBox(true));
                foreach (var brep in expected.Results) bBox.Union(brep.GetBoundingBox(true));
                if (aBox.Min.DistanceTo(bBox.Min) > 0.001 || aBox.Max.DistanceTo(bBox.Max) > 0.001) return false;
            }
            return true;
        }
        void Update(Dictionary<Guid, Transform> transforms, double tolerance = 0.001)
        {
            live.Update(tree, Source, transforms, tolerance, committed);
            Full(transforms, tolerance);
        }
        try
        {
            Update(Move(new[] { a, b, c }, 20));
            Check(Equivalent() && live.LastBooleanCount == 0 && live.LastReusedModifierCount == 2,
                "Whole nested translation reuses two committed modifiers with zero Boolean calls on its first frame");
            Update(Move(new[] { a, b, c }, 25));
            Check(Equivalent() && live.Find(root)!.Results[0].GetBoundingBox(true).Min.X == 27,
                "Cached translation is absolute across frames, with unchanged shape");
            Update(Move(new[] { b }, 2));
            Check(Equivalent() && live.LastBooleanCount == 2, "Switching from whole-tree movement to a cutter restores removed transforms and recomputes only ancestors");
            var untouched = live.Find(other)!.Results[0];
            Update(Move(new[] { b }, 3));
            Check(Equivalent() && live.LastEvaluatedNodeCount == 3 && ReferenceEquals(untouched, live.Find(other)!.Results[0]),
                "Cutter frame evaluates one source and two ancestors, retaining unrelated result geometry");
            var rebuilds = live.RebuildCount;
            Check(!live.Update(tree, Source, Move(new[] { b }, 3), 0.001, committed) && live.RebuildCount == rebuilds,
                "Identical optimized frame performs no evaluation");
            Update(Move(new[] { a, b }, 20));
            Check(Equivalent() && live.LastBooleanCount == 1 && live.LastReusedModifierCount == 1,
                "Translating a nested Modifier reuses its result and recalculates its parent");
            Update(Move(new[] { d }, 1));
            Check(Equivalent() && live.LastBooleanCount == 1, "Changing the moving subtree restores the previous subtree from committed results");
            Update(new[] { a, b, c }.ToDictionary(id => id, _ => Transform.Scale(Point3d.Origin, 1.2)));
            Check(Equivalent() && live.LastBooleanCount >= 2, "Scaling uses Boolean evaluation rather than the translation shortcut");
            Update(Move(new[] { b }, -6));
            Check(Equivalent() && live.Find(root)!.Results.Count == 0, "Incremental subtraction preserves a valid empty result");
            Update(Move(new[] { b }, 2));
            Check(Equivalent() && live.Find(root)!.Results.Count > 0, "Moving out of an empty result recovers correctly");

            // Both paths use the same source snapshots and positions; exclude viewport drawing.
            foreach (var scenario in new[] { "whole", "cutter", "nested" })
            {
                live.Clear();
                var oldMs = new List<double>(); var newMs = new List<double>(); var oldCalls = 0; var newCalls = 0;
                for (var i = 0; i <= 20; i++)
                {
                    var ids = scenario == "whole" ? new[] { a, b, c } : scenario == "nested" ? new[] { a, b } : new[] { b };
                    var transforms = Move(ids, 0.1 + i * 0.1);
                    var start = Stopwatch.GetTimestamp(); Full(transforms); var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    live.Update(tree, Source, transforms, 0.001, committed);
                    if (!Equivalent()) throw new Exception("Benchmark geometry differs: " + scenario);
                    if (i == 0) continue; // Warm up each path and create its drag cache.
                    oldMs.Add(elapsed); newMs.Add(live.LastUpdateMilliseconds);
                    oldCalls += reference.LastBooleanCount; newCalls += live.LastBooleanCount;
                }
                oldMs.Sort(); newMs.Sort();
                Console.WriteLine($"BENCH {scenario}: 20 frames, full median {oldMs[10]:F3} ms / optimized {newMs[10]:F3} ms; Boolean calls {oldCalls} -> {newCalls}; geometry matched every frame.");
                Check(oldCalls == 80 && newCalls == (scenario == "whole" ? 0 : scenario == "nested" ? 20 : 40),
                    $"Measured {scenario} drag eliminates expected redundant Boolean calls");
            }

            var sameTransform = Move(new[] { b }, 2);
            Update(sameTransform);
            sources[c].Translate(1, 0, 0);
            committed.Rebuild(tree, Source, 0.001);
            Check(live.Update(tree, Source, sameTransform, 0.001, committed), "New committed geometry invalidates a live cache even at an unchanged cursor position");
            Full(sameTransform);
            Check(Equivalent(), "Cache reseeding picks up an edited stationary input");
            var added = Box(3, 4);
            tree.Move(tree.FindSource(added)!.Id, root, tree.Find(root)!.Children.Count, out _);
            Update(Move(new[] { a, b, c, added }, 20));
            Check(Equivalent(), "Tree revision rejects an obsolete committed expression before applying the whole-move shortcut");
            Update(Move(new[] { b }, 3), 0.01);
            Check(Equivalent(), "Document tolerance change invalidates drag caches");
            Check(live.Clear() && live.Find(root) is null, "Cancelling an optimized drag discards all transient geometry");
        }
        finally { foreach (var geometry in sources.Values) geometry.Dispose(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}

using System.IO;
using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using ModifierTree.Rhino.Persistence;
using Rhino.Collections;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

internal static class TreeArchiveChecks
{
    public static void Run()
    {
        ArchivePolicyChecks();
        var folder = Path.Combine(Path.GetTempPath(), "ModifierTree-archive-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var binaryPath = Path.Combine(folder, "tree.archive");
        var modelPath = Path.Combine(folder, "tree.3dm");
        try
        {
            using var model = new File3dm();
            model.Settings.ModelAbsoluteTolerance = 0.001;
            using var first = new BoundingBox(0, 0, 0, 10, 10, 10).ToBrep();
            using var cutter = new BoundingBox(5, -1, -1, 15, 11, 11).ToBrep();
            using var attributes = new ObjectAttributes { Name = "Main \uD615\uC0C1" };
            var a = model.Objects.AddBrep(first, attributes);
            var b = model.Objects.AddBrep(cutter);
            var tree = new ModifierTreeModel();
            tree.RegisterSource(a);
            tree.RegisterSource(b);
            var modifier = tree.AddModifier();
            tree.RenameModifier(modifier.Id, "Main \uACB0\uACFC", out _);
            tree.Move(tree.FindSource(a)!.Id, modifier.Id, 0, out _);
            tree.Move(tree.FindSource(b)!.Id, modifier.Id, 1, out _);
            var wires = new InputVisibility();
            wires.Set(b, true);
            var state = TreeStateCodec.Capture(tree, wires, false);

            var loaded = BinaryRoundTrip(binaryPath, writer => TreeArchive.Write(writer, state));
            Check(loaded.CanRestore && loaded.Error is null && loaded.PreservedArchive is null &&
                  TreeStateCodec.Encode(loaded.State!) == TreeStateCodec.Encode(state),
                "Native archive preserves ordered tree IDs, source links and preview/wire settings");

            var future = TreeArchive.Create(state);
            future.Version = 99;
            future.Set("FutureSetting", "preserve this value");
            loaded = BinaryRoundTrip(binaryPath, writer => TreeArchive.WritePreserved(writer, future));
            Check(!loaded.CanRestore && loaded.PreservedArchive?.Version == 99 && loaded.Error is not null,
                "A future archive remains available without restoring an unsupported tree");
            loaded = BinaryRoundTrip(binaryPath, writer => TreeArchive.WritePreserved(writer, loaded.PreservedArchive!));
            Check(loaded.PreservedArchive?.GetString("FutureSetting") == "preserve this value",
                "Re-saving a future archive preserves unfamiliar fields");

            var futureSchema = TreeArchive.Create(state);
            futureSchema.Set("Payload", TreeStateCodec.Encode(state).Replace($"\"schemaVersion\":{TreeStateCodec.CurrentSchemaVersion}", "\"schemaVersion\":99", StringComparison.Ordinal));
            loaded = BinaryRoundTrip(binaryPath, writer => TreeArchive.WritePreserved(writer, futureSchema));
            Check(!loaded.CanRestore && loaded.PreservedArchive?.GetString("Payload") == futureSchema.GetString("Payload"),
                "A future tree schema is preserved intact inside a readable archive envelope");

            var corrupt = TreeArchive.Create(state);
            corrupt.Set("Payload", "{ incomplete JSON");
            loaded = BinaryRoundTrip(binaryPath, writer => TreeArchive.WritePreserved(writer, corrupt));
            Check(!loaded.CanRestore && loaded.PreservedArchive?.GetString("Payload") == "{ incomplete JSON" && loaded.Error is not null,
                "Malformed tree payload is reported and retained instead of replaced with an empty tree");

            // File3dm has a public reader for plug-in data but no public writer for that table.
            // Store the identical dictionary in an object fixture to exercise a complete real
            // .3dm geometry/dictionary roundtrip. This does not claim PlugIn hook/autoload coverage.
            const string fixtureKey = "ModifierTreeArchiveFixture";
            model.Objects.First(obj => obj.Id == a).Attributes.UserDictionary.Set(fixtureKey, TreeArchive.Create(state));
            Check(model.Write(modelPath, new File3dmWriteOptions { Version = 8, SaveUserData = true }),
                "A real Rhino 8 .3dm fixture saves source geometry and the archive dictionary");
            using var reopened = File3dm.Read(modelPath) ?? throw new Exception("Cannot reopen the archive fixture.");
            var reopenedA = reopened.Objects.FirstOrDefault(obj => obj.Id == a);
            var reopenedB = reopened.Objects.FirstOrDefault(obj => obj.Id == b);
            Check(reopenedA is not null && reopenedB is not null && reopened.Objects.Count == 2 &&
                  reopenedA.Attributes.Name == "Main \uD615\uC0C1",
                "Reopened .3dm retains source GUIDs and Unicode names without adding preview objects");
            loaded = TreeArchive.Decode(reopenedA!.Attributes.UserDictionary.GetDictionary(fixtureKey));
            Check(loaded.CanRestore, "The archive dictionary survives the complete .3dm roundtrip");
            var restoredTree = new ModifierTreeModel();
            var restoredWires = new InputVisibility();
            TreeStateCodec.Apply(loaded.State!, restoredTree, restoredWires);
            using var evaluator = new TreeEvaluator();
            evaluator.Rebuild(restoredTree, id => reopened.Objects.FirstOrDefault(obj => obj.Id == id)?.Geometry,
                reopened.Settings.ModelAbsoluteTolerance);
            var result = evaluator.Find(modifier.Id);
            var volume = result?.Results.Sum(brep =>
            {
                using var mass = VolumeMassProperties.Compute(brep);
                return mass.Volume;
            });
            Check(result is { HasResult: true, IsCurrent: true } && Math.Abs(volume!.Value - 500) < 0.01 &&
                  restoredWires.IsVisible(b) && !loaded.State!.PreviewEnabled &&
                  restoredTree.Find(modifier.Id)!.Name == "Main \uACB0\uACFC",
                "Restored tree reconnects saved GUIDs, retains the Modifier name and rebuilds the expected Boolean result");
        }
        finally
        {
            if (File.Exists(binaryPath)) File.Delete(binaryPath);
            if (File.Exists(modelPath)) File.Delete(modelPath);
            Directory.Delete(folder);
        }
    }

    private static TreeArchiveReadResult BinaryRoundTrip(string path, Action<BinaryArchiveWriter> write)
    {
        using (var file = new BinaryArchiveFile(path, BinaryArchiveMode.Write3dm))
        {
            if (!file.Open()) throw new Exception("Cannot create the native archive fixture.");
            write(file.Writer);
            if (file.Writer.WriteErrorOccured) throw new Exception("Native archive write failed.");
        }
        using var input = new BinaryArchiveFile(path, BinaryArchiveMode.Read3dm);
        if (!input.Open()) throw new Exception("Cannot reopen the native archive fixture.");
        return TreeArchive.Read(input.Reader);
    }

    private static void ArchivePolicyChecks()
    {
        using var write = new FileWriteOptions { WriteUserData = true };
        Check(TreeArchive.ShouldWrite(write), "Complete document saves include the tree archive");
        write.WriteSelectedObjectsOnly = true;
        Check(!TreeArchive.ShouldWrite(write), "Export Selected skips incomplete tree references");
        write.WriteSelectedObjectsOnly = false;
        write.WriteUserData = false;
        Check(!TreeArchive.ShouldWrite(write), "Save without user data respects the Rhino file option");
        write.WriteUserData = true;
        write.WriteGeometryOnly = true;
        Check(!TreeArchive.ShouldWrite(write), "Geometry-only exports skip document tree data");
        using var read = new FileReadOptions { OpenMode = true };
        Check(TreeArchive.ShouldRestore(read), "Opening a complete document permits tree restoration");
        read.OpenMode = false;
        read.NewMode = true;
        Check(TreeArchive.ShouldRestore(read), "A new document created from a complete template permits tree restoration");
        read.OpenMode = true;
        read.NewMode = false;
        read.ImportMode = true;
        Check(!TreeArchive.ShouldRestore(read), "Import cannot overwrite the destination tree or bind colliding GUIDs");
        read.ImportMode = false;
        read.InsertMode = true;
        Check(!TreeArchive.ShouldRestore(read), "Block insertion does not restore a document-level tree");
        read.InsertMode = false;
        read.ImportReferenceMode = true;
        Check(!TreeArchive.ShouldRestore(read), "Reference models do not replace the active tree");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}

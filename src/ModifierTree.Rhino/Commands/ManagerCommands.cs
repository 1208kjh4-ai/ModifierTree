using ModifierTree.Rhino.UI;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input.Custom;
using Rhino.UI;

namespace ModifierTree.Rhino.Commands;

public sealed class OpenManagerCommand : Command
{
    public override string EnglishName => "MTree";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        _ = ModifierTreePlugIn.Instance.Session(doc);
        Panels.OpenPanel(typeof(ModifierManagerPanel));
        return Result.Success;
    }
}

public sealed class AddObjectsCommand : Command
{
    public override string EnglishName => "MTreeAddObjects";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        using var picker = new GetObject();
        picker.SetCommandPrompt("Select source objects to register");
        picker.GeometryFilter = ObjectType.Brep | ObjectType.Extrusion | ObjectType.Curve;
        picker.SubObjectSelect = false;
        picker.EnablePreSelect(true, true);
        picker.GetMultiple(1, 0);
        if (picker.CommandResult() != Result.Success)
            return picker.CommandResult();

        var session = ModifierTreePlugIn.Instance.Session(doc);
        var added = session.RegisterSources(picker.Objects().Select(reference => reference.ObjectId));
        session.RequestRefresh();
        RhinoApp.WriteLine($"Registered {added} source object(s). Total: {session.Tree.SourceCount}.");
        return Result.Success;
    }
}

public sealed class TraceCommand : Command
{
    public override string EnglishName => "MTreeTrace";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var trace = ModifierTreePlugIn.Instance.Session(doc).Trace;
        trace.Enabled = !trace.Enabled;
        RhinoApp.WriteLine($"Modifier Tree event trace: {(trace.Enabled ? "ON" : "OFF")}. MTreeDumpLog prints the buffer.");
        return Result.Success;
    }
}

public sealed class DumpLogCommand : Command
{
    public override string EnglishName => "MTreeDumpLog";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var lines = ModifierTreePlugIn.Instance.Session(doc).Trace.Snapshot();
        RhinoApp.WriteLine($"Modifier Tree trace: {lines.Length} records (latest 2000 retained).");
        foreach (var line in lines) RhinoApp.WriteLine(line);
        return Result.Success;
    }
}

public sealed class StatusCommand : Command
{
    public override string EnglishName => "MTreeStatus";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var session = ModifierTreePlugIn.Instance.Session(doc);
        RhinoApp.WriteLine($"Modifier Tree {typeof(ModifierTreePlugIn).Assembly.GetName().Version} | Rhino {RhinoApp.Version} | .NET {System.Environment.Version}");
        RhinoApp.WriteLine($"Document {doc.RuntimeSerialNumber} | Sources {session.Tree.SourceCount} | Modifiers {session.Tree.ModifierCount} | Trace {session.Trace.Enabled}");
        RhinoApp.WriteLine($"Rebuilds: {session.Evaluator.RebuildCount}. Add an empty Modifier, then drag inputs into it in tree order.");
        RhinoApp.WriteLine($"Last live update: {session.LivePreview.LastUpdateMilliseconds:F2} ms | Boolean modifiers evaluated {session.LivePreview.LastBooleanCount} | Evaluated nodes {session.LivePreview.LastEvaluatedNodeCount} | Reused modifiers {session.LivePreview.LastReusedModifierCount} | Target interval 33 ms (drawing excluded).");
        foreach (var node in session.Tree.Nodes.Where(node => node.IsModifier))
        {
            var result = session.Evaluator.Find(node.Id);
            RhinoApp.WriteLine($"{ModifierTree.Core.ModifierNames.DisplayName(node)} {node.Id.ToString()[..8]}: {result?.Status ?? "Pending"} | {node.Children.Count(id => session.Tree.Find(id)?.IsControl != true)} input(s) | {result?.Error}");
        }
        RhinoApp.WriteLine(session.ArchiveError ?? "Tree structure and display settings are saved in .3dm. Tree edits use Rhino Undo/Redo.");
        return Result.Success;
    }
}

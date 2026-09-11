using ModifierTree.Core;
using ModifierTree.Rhino.Modifiers;
using Rhino.Geometry;

// Existing regression scenarios now run through the production tree evaluator.
internal sealed class DifferenceFixture : IDisposable
{
    private readonly ModifierTreeModel _tree = new();
    private readonly TreeEvaluator _engine = new();
    private readonly ModifierTreeNode _modifier;

    public DifferenceFixture(Guid firstId, Guid cutterId)
    {
        _tree.RegisterSource(firstId);
        _tree.RegisterSource(cutterId);
        _modifier = _tree.AddModifier();
        _tree.Move(_tree.FindSource(firstId)!.Id, _modifier.Id, 0, out _);
        _tree.Move(_tree.FindSource(cutterId)!.Id, _modifier.Id, 1, out _);
    }

    private NodeEvaluation? Result => _engine.Find(_modifier.Id);
    public IReadOnlyList<Brep> Results => Result?.Results ?? Array.Empty<Brep>();
    public string? Error => Result?.Error;
    public bool HasValidResult => Result?.HasResult == true;

    public void SwapInputs()
    {
        _tree.Move(_modifier.Children[1], _modifier.Id, 0, out _);
        _engine.Reset();
    }

    public void Rebuild(GeometryBase? first, GeometryBase? cutter, double tolerance)
    {
        var firstId = _tree.Find(_modifier.Children[0])!.ObjectId!.Value;
        _engine.Rebuild(_tree, id => id == firstId ? first : cutter, tolerance);
    }

    public void Dispose() => _engine.Dispose();
}

using ModifierTree.Rhino.UI;
using ModifierTree.Rhino.Persistence;
using ModifierTree.Core;
using Rhino;
using Rhino.FileIO;
using Rhino.PlugIns;
using Rhino.UI;

namespace ModifierTree.Rhino;

public sealed class ModifierTreePlugIn : PlugIn
{
    private readonly Dictionary<uint, DocumentSession> _sessions = [];
    public static ModifierTreePlugIn Instance { get; private set; } = null!;

    public ModifierTreePlugIn() => Instance = this;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        Panels.RegisterPanel(this, typeof(ModifierManagerPanel), "Modifier Manager",
            (System.Drawing.Icon?)null, PanelType.PerDoc);
        RhinoDoc.CloseDocument += OnCloseDocument;
        RhinoDoc.BeginOpenDocument += OnBeginOpenDocument;
        RhinoDoc.EndOpenDocument += OnEndOpenDocument;
        RhinoApp.WriteLine("Modifier Tree: MTree opens the manager. Add Modifier creates an empty operator for drag-and-drop inputs.");
        return LoadReturnCode.Success;
    }

    internal DocumentSession Session(RhinoDoc document)
    {
        if (!_sessions.TryGetValue(document.RuntimeSerialNumber, out var session))
        {
            session = new DocumentSession(document);
            _sessions.Add(document.RuntimeSerialNumber, session);
        }
        return session;
    }

    protected override bool ShouldCallWriteDocument(FileWriteOptions options) => TreeArchive.ShouldWrite(options);

    protected override void WriteDocument(RhinoDoc doc, BinaryArchiveWriter archive, FileWriteOptions options)
    {
        try
        {
            if (!TreeArchive.ShouldWrite(options)) return;
            if (_sessions.TryGetValue(doc.RuntimeSerialNumber, out var session))
            {
                session.Working.AssertReadyForSave(session.Tree);
                if (session.PreservedArchive is { } preserved) TreeArchive.WritePreserved(archive, preserved);
                else if (session.ArchiveError is { } error) throw new InvalidOperationException(error);
                else TreeArchive.Write(archive, session.CaptureState());
            }
            else TreeArchive.Write(archive, TreeStateCodec.Capture(new ModifierTreeModel(), new InputVisibility(), true));
        }
        catch (Exception exception)
        {
            archive.WriteErrorOccured = true;
            RhinoApp.WriteLine("Modifier Tree could not be saved: " + exception.Message);
        }
    }

    protected override void ReadDocument(RhinoDoc doc, BinaryArchiveReader archive, FileReadOptions options)
    {
        if (!TreeArchive.ShouldRestore(options)) return;
        var session = Session(doc);
        session.BeginFileRead();
        try
        {
            var result = TreeArchive.Read(archive);
            if (result.State is { } state) session.RestoreFromFile(state);
            else session.PreserveUnreadArchive(result.PreservedArchive, result.Error ?? "The Modifier Tree could not be restored.");
        }
        catch (Exception exception) { session.PreserveUnreadArchive(null, "Modifier Tree archive read failed: " + exception.Message); }
    }

    private void OnBeginOpenDocument(object? sender, DocumentOpenEventArgs e)
    {
        if (!e.Merge && !e.Reference && _sessions.TryGetValue(e.Document.RuntimeSerialNumber, out var session)) session.BeginFileRead(clearPrevious: true);
    }

    private void OnEndOpenDocument(object? sender, DocumentOpenEventArgs e)
    {
        if (!e.Merge && !e.Reference && _sessions.TryGetValue(e.Document.RuntimeSerialNumber, out var session)) session.EndFileRead();
    }

    private void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        if (_sessions.Remove(e.Document.RuntimeSerialNumber, out var session))
            session.Dispose();
    }

    protected override void OnShutdown()
    {
        RhinoDoc.CloseDocument -= OnCloseDocument;
        RhinoDoc.BeginOpenDocument -= OnBeginOpenDocument;
        RhinoDoc.EndOpenDocument -= OnEndOpenDocument;
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
        base.OnShutdown();
    }
}

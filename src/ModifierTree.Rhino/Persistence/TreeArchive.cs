using ModifierTree.Core;
using Rhino.Collections;
using Rhino.FileIO;

namespace ModifierTree.Rhino.Persistence;

/// <summary>
/// The document archive contains an ordered expression and display settings, never preview geometry.
/// The dictionary envelope remains readable even when a future tree schema cannot be restored.
/// </summary>
internal static class TreeArchive
{
    private const int EnvelopeVersion = 1;
    private const string Format = "ModifierTree.DocumentState";

    // Export Selected, clipboard copies and geometry-only files cannot carry the complete tree.
    // Complete templates are ordinary .3dm models and can include source geometry as well.
    public static bool ShouldWrite(FileWriteOptions options) =>
        options.WriteUserData && !options.WriteSelectedObjectsOnly &&
        !options.WriteGeometryOnly;

    // Import/Insert can remap Rhino object GUIDs. Until that mapping is supported, never replace
    // the destination tree or reconnect an imported tree to an unrelated existing object.
    public static bool ShouldRestore(FileReadOptions options) =>
        (options.OpenMode || options.NewMode) && !options.ImportMode &&
        !options.InsertMode && !options.ImportReferenceMode;

    public static ArchivableDictionary Create(ModifierDocumentState state)
    {
        var archive = new ArchivableDictionary(EnvelopeVersion, "ModifierTree");
        archive.Set("Format", Format);
        archive.Set("Payload", TreeStateCodec.Encode(state));
        return archive;
    }

    public static void Write(BinaryArchiveWriter writer, ModifierDocumentState state)
    {
        try { writer.WriteDictionary(Create(state)); }
        catch
        {
            // Rhino's documented failure flag aborts the file save, including validation errors
            // that occur before WriteDictionary has a chance to set the flag itself.
            writer.WriteErrorOccured = true;
            throw;
        }
    }

    public static void WritePreserved(BinaryArchiveWriter writer, ArchivableDictionary archive)
    {
        try { writer.WriteDictionary(archive); }
        catch
        {
            writer.WriteErrorOccured = true;
            throw;
        }
    }

    // Binary I/O failures intentionally propagate: no complete payload is available to preserve,
    // so the caller must protect the document from replacing the unread data with an empty tree.
    public static TreeArchiveReadResult Read(BinaryArchiveReader reader) => Decode(reader.ReadDictionary());

    public static TreeArchiveReadResult Decode(ArchivableDictionary? archive)
    {
        if (archive is null)
            return new(null, null, "The Modifier Tree archive could not be read.");
        if (archive.Version != EnvelopeVersion)
            return new(null, archive, $"Unsupported Modifier Tree archive version {archive.Version}.");
        if (!archive.TryGetString("Format", out var format) || format != Format)
            return new(null, archive, "The Modifier Tree archive format is not recognized.");
        if (!archive.TryGetString("Payload", out var json))
            return new(null, archive, "The Modifier Tree archive has no tree payload.");
        try { return new(TreeStateCodec.Decode(json), null, null); }
        catch (FormatException exception)
        {
            return new(null, archive, "The Modifier Tree could not be restored: " + exception.Message);
        }
    }
}

internal sealed record TreeArchiveReadResult(
    ModifierDocumentState? State,
    ArchivableDictionary? PreservedArchive,
    string? Error)
{
    public bool CanRestore => State is not null;
}

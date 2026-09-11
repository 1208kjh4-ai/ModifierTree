using Rhino;

namespace ModifierTree.Rhino.Modifiers;

internal static class SourceNameEditor
{
    public static bool Rename(RhinoDoc doc, Guid objectId, string name, out string error)
    {
        error = "";
        var obj = doc.Objects.FindId(objectId);
        if (obj is null || obj.IsReference || obj.IsLocked)
        { error = "The source is missing, locked, or referenced; its name cannot be edited."; return false; }
        if (obj.Attributes.Name == name) return true;
        var record = doc.UndoRecordingIsActive ? 0 : doc.BeginUndoRecord("Rename Modifier Tree source");
        try
        {
            using var attributes = obj.Attributes.Duplicate();
            attributes.Name = name;
            if (doc.Objects.ModifyAttributes(objectId, attributes, true)) return true;
            error = "Rhino could not update the source name.";
            return false;
        }
        finally { if (record != 0) doc.EndUndoRecord(record); }
    }
}

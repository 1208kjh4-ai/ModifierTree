using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class PluginMetadataChecks
{
    public static void Verify(string pluginPath)
    {
        // Read the actual .rhp metadata without starting Rhino or loading its native dependencies.
        using var stream = File.OpenRead(pluginPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var ids = new List<Guid>();
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.GetString(type.Namespace) != "System.Runtime.InteropServices" ||
                metadata.GetString(type.Name) != "GuidAttribute") continue;

            var value = metadata.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != 1 || !Guid.TryParse(value.ReadSerializedString(), out var id))
                throw new Exception("FAIL: Plugin assembly GuidAttribute is malformed.");
            ids.Add(id);
        }

        var expected = new Guid("DF5C138D-8BF7-4FAB-ADAA-C25C53BAA6E9");
        if (ids.Count != 1 || ids[0] != expected)
            throw new Exception("FAIL: The .rhp must declare the stable plugin GUID at assembly scope. A class GuidAttribute does not set Rhino PlugIn.Id.");
        Console.WriteLine($"PASS: Built .rhp assembly declares plugin GUID {expected}.");
    }
}

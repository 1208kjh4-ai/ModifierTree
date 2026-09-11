using System.Runtime.InteropServices;

// Rhino reads PlugIn.Id from the assembly, not the PlugIn subclass.
// Keep this identity stable across builds. The Eto panel has its own class GUID.
[assembly: Guid("DF5C138D-8BF7-4FAB-ADAA-C25C53BAA6E9")]

using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        const string system = @"C:\Program Files\Rhino 8\System";
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (var folder in new[] { Path.Combine(system, "netcore"), system })
            {
                var path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        if (!SetDllDirectory(system)) throw new InvalidOperationException("Cannot set native DLL directory.");
        Environment.SetEnvironmentVariable("PATH", system + ";" + Environment.GetEnvironmentVariable("PATH"));
        try { return Run(args); }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        Console.WriteLine("HOST NoWindow; safemode; isolated scheme ModifierTreeBendProbe; no installed plugin load.");
        using var core = new Rhino.Runtime.InProcess.RhinoCore(
            ["/nosplash", "/notemplate", "/safemode", "/scheme=ModifierTreeBendProbe"],
            Rhino.Runtime.InProcess.WindowStyle.NoWindow);
        return BendProbe.Run(args.Length > 0 ? args[0] : "artifacts/bend-probe");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string path);
}

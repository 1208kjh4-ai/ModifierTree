using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var rhinoSystem = args.Length > 0 ? Path.GetFullPath(args[0]) : @"C:\Program Files\Rhino 8\System";
        AssemblyLoadContext.Default.Resolving += (_, name) => Resolve(rhinoSystem, name);
        if (!SetDllDirectory(rhinoSystem)) throw new InvalidOperationException("Cannot set Rhino native DLL search directory.");
        System.Environment.SetEnvironmentVariable("PATH", rhinoSystem + ";" + System.Environment.GetEnvironmentVariable("PATH"));
        try { return args.Contains("--ui-only") ? RunUiChecks() : Run(args.Contains("--bend-only")); }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static Assembly? Resolve(string system, AssemblyName name)
    {
        foreach (var folder in new[] { Path.Combine(system, "netcore"), system, AppContext.BaseDirectory })
        {
            foreach (var extension in new[] { ".dll", ".rhp" })
            {
                var path = Path.Combine(folder, name.Name + extension);
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunUiChecks()
    {
        Console.WriteLine("Checking Eto/WPF controls without starting RhinoCore or opening a window...");
        ManagerLayoutChecks.Run();
        Console.WriteLine("All Eto/WPF layout and property input checks passed.");
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(bool bendOnly)
    {
        Console.WriteLine("Starting an isolated Rhino core without a window...");
        using var core = new Rhino.Runtime.InProcess.RhinoCore(["/nosplash", "/notemplate", "/safemode", "/scheme=ModifierTreeValidation014"], Rhino.Runtime.InProcess.WindowStyle.NoWindow);
        try
        {
            if (bendOnly)
            {
                BendControlCageChecks.Run();
                BendControlCageCacheChecks.Run();
                BendGeometryChecks.Run();
                BendDocumentChecks.Run();
                BendWorkingChecks.Run();
                Console.WriteLine("All Bend geometry, document and working result checks passed. Viewport interaction requires UI verification.");
            }
            else RhinoChecks.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            // Report before native host shutdown, which can take time after a failed check.
            return 1;
        }
        return 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string path);
}

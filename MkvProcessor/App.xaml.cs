using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using MkvProcessor.Services;

namespace MkvProcessor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register resolvers BEFORE any code touches the embedded-OCR types so that the
        // first reference triggers loading from pgstosrt/.
        RegisterBundledAssemblyResolver();
        RegisterBundledNativeLibrarySearchPath();

        AppConfiguration.Initialize();
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    /// <summary>
    /// Redirects managed-assembly loads for libse / TesseractOCR (and their transitive
    /// dependencies) into the bundled pgstosrt/ folder where PgsToSrt's full dependency
    /// set already lives. This lets us reference those DLLs without duplicating them at
    /// the bin root — a single source of truth for the tesseract/libse stack.
    /// </summary>
    private static void RegisterBundledAssemblyResolver()
    {
        var pgsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pgstosrt");
        if (!Directory.Exists(pgsFolder))
            return;

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var requested = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(requested))
                return null;

            var candidate = Path.Combine(pgsFolder, requested + ".dll");
            if (File.Exists(candidate))
            {
                try { return Assembly.LoadFrom(candidate); } catch { }
            }
            return null;
        };
    }

    /// <summary>
    /// Tesseract's managed wrapper loads native DLLs (leptonica, tesseract) via P/Invoke.
    /// In PgsToSrt's normal invocation those natives live beside the .NET entry point, but
    /// in our case they live inside pgstosrt/ which is not on the default DLL search path.
    /// Prepend it with <see cref="SetDllDirectory"/> so the dynamic loader finds them.
    /// </summary>
    private static void RegisterBundledNativeLibrarySearchPath()
    {
        var pgsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pgstosrt");
        if (!Directory.Exists(pgsFolder))
            return;

        // AddDllDirectory is only available on Windows 8+; SetDllDirectory works everywhere
        // and covers our WPF/Windows-only target.
        SetDllDirectory(pgsFolder);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string lpPathName);
}

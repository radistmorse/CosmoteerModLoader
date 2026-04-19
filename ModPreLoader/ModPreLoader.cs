using System.Reflection;
using System.Runtime.Loader;

[assembly: AssemblyVersion("1.4.1.0")]
[assembly: AssemblyFileVersion("1.4.1.0")]
namespace ModPreLoader
{
    public class ModPreLoader
    {
        /// <summary>
        /// Mod pre-loader is needed to decouple game entry point with cosmoteer.dll.
        /// We load cosmoteer assembly from another file, so that ModLoader could be
        /// loaded into the context without the name conflict
        /// </summary>
        [STAThread]
        static public void Main(string[] argv)
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)??string.Empty;
            var origFile = Path.GetFullPath(Path.Combine(dir, "Cosmoteer_o.dll"));
            var modLoaderFile = Path.GetFullPath(Path.Combine(dir, "ModLoader.dll"));

            AssemblyName.GetAssemblyName(origFile);
            AssemblyName.GetAssemblyName(modLoaderFile);
            AssemblyLoadContext.Default.LoadFromAssemblyPath(origFile);
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(modLoaderFile);

            if (assembly.EntryPoint == null)
            {
                throw new DllNotFoundException("ModLoader dll doesn't have an entry point.");
            }

            assembly.EntryPoint?.Invoke(null, [argv]);
        }
    }
}

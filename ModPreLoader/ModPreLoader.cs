using System.Reflection;
using System.Runtime.Loader;

namespace ModPreLoader
{
    public class ModPreLoader
    {
        [STAThread]
        static public void Main(string[] argv)
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var origFile = Path.GetFullPath(Path.Combine(dir, "Cosmoteer_o.dll"));
            var modLoaderFile = Path.GetFullPath(Path.Combine(dir, "ModLoader.dll"));

            Assembly? assembly = null;
            try
            {
                AssemblyName.GetAssemblyName(origFile);
                AssemblyName.GetAssemblyName(modLoaderFile);
                AssemblyLoadContext.Default.LoadFromAssemblyPath(origFile);
                assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(modLoaderFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            assembly?.EntryPoint?.Invoke(null, [argv]);
        }
    }
}

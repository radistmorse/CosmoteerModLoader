#define STEAM

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ModLoader
{

    public partial class ModLoader
    {
        /// <summary>
        /// This is a dummy method to call functions that has UnmanagedCallersOnly attributes
        /// </summary>
        /// <param name="funcPtr">The pointer to the function to be called</param>
        private static readonly Action<IntPtr> CallFromUnmanaged;

        [LibraryImport("winmm.dll", EntryPoint = "CallFromUnmanaged")]
        private static unsafe partial void CallFromUnmanagedWinmm(IntPtr funcPtr);

        [LibraryImport("unmanaged.dll", EntryPoint = "CallFromUnmanaged")]
        private static unsafe partial void CallFromUnmanagedVersion(IntPtr funcPtr);


        static ModLoader() {
            try
            {
                // on windows we use winmm.dll
                Marshal.Prelink(((Action<IntPtr>)CallFromUnmanagedWinmm).Method);
                CallFromUnmanaged = CallFromUnmanagedWinmm;
            }
            catch (Exception)
            {
                // ignore
            }
            try
            {
                // on proton/linux we use version.dll
                Marshal.Prelink(((Action<IntPtr>)CallFromUnmanagedVersion).Method);
                CallFromUnmanaged = CallFromUnmanagedVersion;
            }
            catch (Exception)
            {
                // ignore
            }
            if (CallFromUnmanaged == null)
            {
                throw new DllNotFoundException("Could not find a dll with the correct native method");
            }
        }


        /// <summary>
        /// The main entrypoint. Here we do the minimum game init to get the location of the settings file.
        /// </summary>
        [STAThread]
        static public void Main(string[] argv)
        {
            Task DelayedInitializer = Task.CompletedTask;
            if (!Cosmoteer.GameApp.IsNoModsMode)
            {
                // we need steam to get steamid for the settings file location
                Cosmoteer.Steamworks.Steam.Init();
                // we need to remove the callback that was added by init, or the game will not launch
                // (it will try to add it again)
                foreach (var callback in Cosmoteer.Steamworks.Steam.s_callbacks.Select(pair => pair.Value as IDisposable))
                    callback?.Dispose();
                Cosmoteer.Steamworks.Steam.s_callbacks.Clear();

                // also needed for settings file location
                Halfling.App.Platform = Halfling.Platforms.Platform.Create();
                var settingsFile = new Halfling.ObjectText.OTFile(Cosmoteer.Paths.SettingsFile);

                // I have no idea how to output it more conveniently
                // Cosmoteer logging is very hard to initialize without the game itself
                // for now let's output it to stdout, even though nobody will see it
                Console.WriteLine($"[Mod Preloader] Reading mod settings from {settingsFile}");

                var serializer = new Halfling.Serialization.ObjectText.ObjectTextSerializer(true);
                var reader = serializer.CreateGenericSerialReader(settingsFile.MakeAtPath("GameSettings"));
                var enabledMods = reader.ReadFromPath<HashSet<Halfling.IO.AbsolutePath>>(nameof(Cosmoteer.Settings.EnabledMods));

                var libs = new Dictionary<string, string>();
                string? harmonyLib = null;
                Version? harmonyVer = null;

                foreach (var mod in enabledMods)
                {
                    Console.WriteLine($"[Mod Preloader] Found enabled mod dir {mod}");

                    foreach (var file in Directory.EnumerateFiles(mod, "*.dll", SearchOption.AllDirectories))
                    {
                        Console.WriteLine($"[Mod Preloader] found dll file {file}");

                        try
                        {
                            // we first try to load assembly name
                            // if we try to load the assembly right away, it can corrupt
                            // the context and throw an uncachable exeption. if we can get
                            // the name that at least means that the dll is a manageable
                            // assembly, and we can attempt to load it
                            var name = AssemblyName.GetAssemblyName(file);
                            if (name.Name == "0Harmony")
                            {
                                // pick the latest harmony version, if several are found
                                if (harmonyVer == null || harmonyVer < name.Version) {
                                    harmonyLib = file;
                                    harmonyVer = name.Version;
                                }
                            }
                            else
                            {
                                if (name.Name != null)
                                {
                                    if (!libs.TryGetValue(name.Name, out string? value))
                                    {
                                        libs.Add(name.Name, file);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Library {file} duplicates another library {value}, ignored");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Mod Preloader] failed to load lib from {file}, exception\n{ex}");
                        }
                    }
                }

                if (harmonyLib != null)
                {
                    try
                    {
                        AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", false);
                        AssemblyLoadContext.Default.LoadFromAssemblyPath(harmonyLib);
                        Console.WriteLine($"[Mod Preloader] loaded harmony lib from {harmonyLib}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Mod Preloader] failed to load harmony lib from {harmonyLib}, exception\n{ex}");
                    }
                }

                var delayedInitMethods = new Dictionary<MethodInfo, string>();

                foreach (var lib in libs.Values)
                {
                    try
                    {
                        AssemblyName.GetAssemblyName(lib);
                        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(lib);
                        Console.WriteLine($"[Mod Preloader] loaded mod lib from {lib}");

                        foreach (var type in assembly.GetTypes())
                        {
                            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                            {
                                if (method.Name == "AssemblyLoadInitializer" && method.GetParameters().Length == 0 && method.ReturnType == typeof(void))
                                {
                                    // EML required a special attribute, which prevents calling from manageable code
                                    if (method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() == null)
                                    {
                                        method.Invoke(null, null);
                                        Console.WriteLine($"[Mod Preloader] called init method {method.Name} for mod lib {lib}");
                                    }
                                    else
                                    {
                                        CallFromUnmanaged(method.MethodHandle.GetFunctionPointer());
                                        Console.WriteLine($"[Mod Preloader] called unmanaged init method {method.Name} for mod lib {lib}");
                                    }
                                }

                                if ((method.Name == "GameLoadInitializer" || method.Name == "InitializePatches") && method.GetParameters().Length == 0 && method.ReturnType == typeof(void))
                                {
                                    delayedInitMethods.Add(method, lib);
                                }

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Mod Preloader] failed to load mod lib from {lib}, exception\n{ex}");
                    }
                }
                // start the async task for delayed initialization
                DelayedInitializer = DelayedInit(delayedInitMethods);
            }
            // start the actual game
            Cosmoteer.GameApp.Main(argv);

            // close the task
            DelayedInitializer.Wait();
        }

        /// <summary>
        /// This is the async task that will wait until the game object is created, and then call
        /// all the init methods from mods
        /// </summary>
        /// <param name="methods">The list of methods to call</param>
        static async Task DelayedInit(Dictionary<MethodInfo, string> methods)
        {
            // wait for director to appear, which means the GameApp constructor has finished
            while (Halfling.App.Director == null) await Task.Delay(10);

            foreach(var (method, lib) in methods)
            {
                try
                {
                    // here the logger is already initialized
                    if (method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() == null)
                    {
                        method.Invoke(null, null);
                        Halfling.Logging.Logger.Log($"[Mod Preloader] called delayed init method {method.Name} for mod lib {lib}");
                    }
                    else
                    {
                        CallFromUnmanaged(method.MethodHandle.GetFunctionPointer());
                        Halfling.Logging.Logger.Log($"[Mod Preloader] called delayed unmanaged init method {method.Name} for mod lib {lib}");
                    }
                }
                catch (Exception ex)
                {
                    Halfling.Logging.Logger.Log($"[Mod Preloader] failed to load mod lib from {lib}, exception\n{ex}");
                }
            }

            return;
        }

    }
}

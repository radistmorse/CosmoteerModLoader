#define STEAM

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ModLoader
{

    public class ModLoader
    {

        [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
        public static unsafe extern void CallFromUnmanaged(IntPtr funcPtr);


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
                foreach (var callback in Cosmoteer.Steamworks.Steam.s_callbacks.Select(pair => pair.Value as Steamworks.Callback<Steamworks.GameOverlayActivated_t>))
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

                var libs = new HashSet<string>();
                string? harmonyLib = null;

                foreach (var mod in enabledMods)
                {
                    Console.WriteLine($"[Mod Preloader] Found enabled mod dir {mod}");

                    foreach (var file in Directory.EnumerateFiles(mod, "*.dll", SearchOption.AllDirectories))
                    {
                        Console.WriteLine($"[Mod Preloader] foun dll file {file}");

                        // not the best way to distinguish harmony, but should work
                        if (file.EndsWith("0Harmony.dll"))
                        {
                            harmonyLib = file;
                        }
                        else
                        {
                            libs.Add(file);
                        }
                    }
                }

                if (harmonyLib != null)
                {
                    try
                    {
                        AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", false);

                        // we first try to load assembly name
                        // if we try to load the assembly right away, it can corrupt
                        // the context and throw an uncachable exeption. if we can get
                        // the name that at least means that the dll is a manageable
                        // assembly, and we can attempt to load it
                        AssemblyName.GetAssemblyName(harmonyLib);
                        AssemblyLoadContext.Default.LoadFromAssemblyPath(harmonyLib);
                        Console.WriteLine($"[Mod Preloader] loaded harmony lib from {harmonyLib}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Mod Preloader] failed to load harmony lib from {harmonyLib}, exception\n{ex.ToString()}");
                    }
                }

                var delayedInitMethods = new Dictionary<MethodInfo, string>();

                foreach (var lib in libs)
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
                                        Console.WriteLine($"[Mod Preloader] called init metod for user lib {lib}");
                                    }
                                    else
                                    {
                                        CallFromUnmanaged(method.MethodHandle.GetFunctionPointer());
                                        Console.WriteLine($"[Mod Preloader] called unmanaged init metod for user lib {lib}");
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
                        Console.WriteLine($"[Mod Preloader] failed to load mod lib from {lib}, exception\n{ex.ToString()}");
                    }
                }
                DelayedInitializer = DelayedInit(delayedInitMethods);
            }
            Cosmoteer.GameApp.Main(argv);
            DelayedInitializer.Wait();
        }

        static async Task DelayedInit(Dictionary<MethodInfo, string> methods)
        {
            while (Halfling.App.Director == null) await Task.Delay(10);

            foreach(var (method, lib) in methods)
            {
                try
                {
                    if (method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() == null)
                    {
                        method.Invoke(null, null);
                        Console.WriteLine($"[Mod Preloader] called delayed init metod for user lib {lib}");
                    }
                    else
                    {
                        CallFromUnmanaged(method.MethodHandle.GetFunctionPointer());
                        Console.WriteLine($"[Mod Preloader] called delayed unmanaged init metod for user lib {lib}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Mod Preloader] failed to load mod lib from {lib}, exception\n{ex.ToString()}");
                }
            }

            return;
        }

    }
}

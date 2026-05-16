#define STEAM

using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

[assembly: AssemblyVersion("1.4.1.1")]
[assembly: AssemblyFileVersion("1.4.1.1")]
namespace ModLoader
{

    public partial class ModLoader
    {
        // Guid for harmony 2.4.1.0
        // Must be updated if another version of the library is shipped
        private static readonly Guid HarmonyGuid = new("dc2e7251-4b84-4883-90eb-eb05a041522c");
        private static readonly Guid ModLoaderGuid = Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId;
        private static readonly string ModLoaderName = Assembly.GetExecutingAssembly().GetName().Name ?? string.Empty;

        private static Dictionary<MethodInfo, Guid> DelayedInitMethods = [];

        private static Dictionary<Guid, (string file, bool duplicated, bool consented, bool fromTrusted, string? error)> LibraryFiles = [];
        private static Dictionary<Halfling.IO.AbsolutePath, HashSet<Guid>> ModFolders = [];
        private static HashSet<Guid> KnownModLibraries = [];
        private static HashSet<Guid> LibrariesInContext = [];
        private static HashSet<Halfling.IO.AbsolutePath> TrustedMods = [];
        private static bool showErrorMessageOnce = false;

        /// <summary>
        /// This is a dummy method to call functions that has UnmanagedCallersOnly attributes
        /// </summary>
        /// <param name="funcPtr">The pointer to the function to be called</param>
        private static readonly Action<IntPtr> CallFromUnmanaged;

        // dll-proxy
        [LibraryImport("winmm.dll", EntryPoint = "CallFromUnmanaged")]
        private static unsafe partial void CallFromUnmanagedAvrt(IntPtr funcPtr);

        // non-dll-proxy
        [LibraryImport("unmanaged.dll", EntryPoint = "CallFromUnmanaged")]
        private static unsafe partial void CallFromUnmanagedVersion(IntPtr funcPtr);


        static ModLoader() {
            try
            {
                // case of dll-proxy (winmm.dll)
                Marshal.Prelink(((Action<IntPtr>)CallFromUnmanagedAvrt).Method);
                CallFromUnmanaged = CallFromUnmanagedAvrt;
            }
            catch (Exception)
            {
                // ignore
            }
            try
            {
                // case of non-dll-proxy (unmanaged.dll)
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
            LoadLibs();

            // start the actual game
            Cosmoteer.GameApp.Main(argv);
        }

        /// <summary>
        /// Scans the given folder in search for dll files
        /// </summary>
        /// <param name="mod">The folder to scan</param>
        /// <returns>path for harmony lib, if found</returns>
        static private string? LoadLibsForFolder(Halfling.IO.AbsolutePath mod)
        {
            if (Directory.Exists(mod))
            {
                Halfling.Logging.Logger.Log($"Found enabled mod dir {mod}");
            }
            else
            {
                Halfling.Logging.Logger.Log($"Non-existing mod dir ignored {mod}");
                return null;
            }
            ModFolders[mod] = [];
            bool isModLoader = false;
            string? harmonyFile = null;

            foreach (var file in Directory.EnumerateFiles(mod, "*.dll", SearchOption.AllDirectories))
            {
                Halfling.Logging.Logger.Log($"found dll file {file}");

                try
                {
                    var peReader = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(file));
                    if (!peReader.HasMetadata)
                    {
                        Halfling.Logging.Logger.Log($"File {file} doesn't have an assembly metadata, ignored");
                        continue;
                    }
                    var mdReader = peReader.GetMetadataReader();
                    if (!mdReader.IsAssembly)
                    {
                        Halfling.Logging.Logger.Log($"File {file} is not an assembly, ignored");
                        continue;
                    }
                    var guid = mdReader.GetGuid(mdReader.GetModuleDefinition().Mvid);
                    if (guid == ModLoaderGuid)
                    {
                        Halfling.Logging.Logger.Log($"Library {file} is a mod-loader library, ignored");
                        isModLoader = true;
                        continue;
                    }
                    if (LibrariesInContext.Contains(guid))
                    {
                        // if the assembly is already in the context that means that
                        // the mod was already loaded. We trust that the lib is OK then
                        KnownModLibraries.Add(guid);
                        ModFolders[mod].Add(guid);
                        continue;
                    }

                    if (LibraryFiles.TryGetValue(guid, out var lib))
                    {
                        // if it's the same file just ignore it
                        if (file != lib.file)
                        {
                            Halfling.Logging.Logger.Log($"Library {file} duplicates another mod library {lib.file}");
                            Halfling.Logging.Logger.Log("This may signal a suspicious behaviour, both files are ignored");
                            lib.duplicated = true;
                            lib.error = $"Two or more mods have the same library {Path.GetFileName(file)}";
                            LibraryFiles[guid] = lib;
                        }
                        ModFolders[mod].Add(guid);
                        continue;
                    }
                    // we first try to load assembly name
                    // if we try to load the assembly right away, it can corrupt
                    // the context and throw an uncachable exeption. if we can get
                    // the name that at least means that the dll is a manageable
                    // assembly, and we can attempt to load it
                    var name = AssemblyName.GetAssemblyName(file);

                    if (name.Name == "0Harmony")
                    {
                        if (guid != HarmonyGuid)
                        {
                            Halfling.Logging.Logger.Log($"Found harmony library {file} with incorrect GUID");
                            Halfling.Logging.Logger.Log($"{guid}");
                            Halfling.Logging.Logger.Log($"The file loading is disabled for security reasons");
                            continue;
                        }
                        harmonyFile = file;
                    }
                    else
                    {
                        if (TrustedMods.Contains(mod))
                        {
                            LibraryFiles.Add(guid, (file: file, duplicated: false, consented: true, fromTrusted: true, error: default));
                        }
                        else if (KnownModLibraries.Contains(guid))
                        {
                            LibraryFiles.Add(guid, (file: file, duplicated: false, consented: true, fromTrusted: false, error: default));
                        }
                        else
                        {
                            if (name.Name == ModLoaderName)
                            {
                                LibraryFiles.Add(guid, (file: file, duplicated: false, consented: false, fromTrusted: false, error: $"Unknown library {Path.GetFileName(file)}, it appears to be a newer version of the ModLoader, please repeat the installation procedure"));
                                Halfling.Logging.Logger.Log($"Library {file} is not in the list of known assemblies, ignored");
                            }
                            else
                            {
                                LibraryFiles.Add(guid, (file: file, duplicated: false, consented: false, fromTrusted: false, error: $"Unknown library {Path.GetFileName(file)}, open the mods list and trust the libraries for the relevant mod"));
                                Halfling.Logging.Logger.Log($"Library {file} is not in the list of known assemblies, ignored");
                            }
                        }
                        ModFolders[mod].Add(guid);
                    }
                }
                catch (Exception ex)
                {
                    Halfling.Logging.Logger.Log($"failed to load lib from {file}, exception\n{ex}");
                }
            }
            if (isModLoader)
            {
                // remove all the libs from the list
                foreach (var guid in ModFolders[mod])
                {
                    LibraryFiles.Remove(guid);
                }
                ModFolders[mod].Clear();
            }
            return harmonyFile;
        }

        /// <summary>
        /// Finds and loads mod libraries into the context
        /// </summary>
        static public void LoadLibs()
        {
            if (Cosmoteer.GameApp.IsNoModsMode)
            {
                return;
            }

            // we need steam to get steamid for the settings file location
            Cosmoteer.Steamworks.Steam.Init();
            // we need to remove the callback that was added by init, or the game will not launch
            // (it will try to add it again)
            foreach (var callback in Cosmoteer.Steamworks.Steam.s_callbacks.Select(pair => pair.Value as IDisposable))
                callback?.Dispose();
            Cosmoteer.Steamworks.Steam.s_callbacks.Clear();

            // also needed for settings file location
            Halfling.App.Platform = Halfling.Platforms.Platform.Create();


            Directory.CreateDirectory(Cosmoteer.Paths.LogsFolder);
            var loggerWriter = Halfling.Logging.Logger.SetupLogOutputFile(Cosmoteer.Paths.LogsFolder / $"log{DateTime.Now:yyyy-MM-dd HH_mm_ss}_modloader.txt");

            try
            {
                // if no settings file exists, no mods are enabled, skip the load
                if (!File.Exists(Cosmoteer.Paths.SettingsFile))
                {
                    Halfling.Logging.Logger.Log($"Setting file not found: {Cosmoteer.Paths.SettingsFile}\nMod loading will not continue.");
                    Halfling.Logging.Logger.UnregisterLogOutputWriter(loggerWriter);
                    return;
                }

                var settingsFile = new Halfling.ObjectText.OTFile(Cosmoteer.Paths.SettingsFile);

                Halfling.Logging.Logger.Log($"Reading mod settings from {settingsFile}");

                settingsFile.MakeAtPath("GameSettings");
                var serializer = new Halfling.Serialization.ObjectText.ObjectTextSerializer(true);
                var reader = serializer.GetGenericReaderForPath(settingsFile, "GameSettings");
                var enabledMods = reader.ReadFromPath<HashSet<Halfling.IO.AbsolutePath>>(nameof(Cosmoteer.Settings.EnabledMods));
                if (reader.HasPath(nameof(KnownModLibraries)))
                {
                    KnownModLibraries = [.. reader.ReadFromPath<string[]>(nameof(KnownModLibraries)).Select(guid => new Guid(guid))];
                }
                if (reader.HasPath(nameof(TrustedMods)))
                {
                    TrustedMods = reader.ReadFromPath<HashSet<Halfling.IO.AbsolutePath>>(nameof(TrustedMods));
                }

                string? harmonyLib = null;

                foreach (var mod in enabledMods)
                {
                    var file = LoadLibsForFolder(mod);
                    if (file != null)
                    {
                        if (harmonyLib == null)
                        {
                            harmonyLib = file;
                            Halfling.Logging.Logger.Log($"Found Harmony lib {file}");
                        }
                        else
                        {
                            Halfling.Logging.Logger.Log($"Found duplicated Harmony lib {file}, ignored");
                        }
                    }
                }

                // remove known libraries if they contain something that was removed since last launch
                // if some mod was disabled it will remove that dangling libraries as well
                KnownModLibraries.IntersectWith(LibraryFiles.Where(kvp => kvp.Value.fromTrusted == false).Select(kvp => kvp.Key));

                // remove trusted mods that were disabled or removed
                TrustedMods.IntersectWith(enabledMods.Where(mod => Directory.Exists(mod)));

                // if no harmony found, that means the mod loader is disabled or broken
                // skip the load, some libs might break anyway
                if (harmonyLib == null)
                {
                    Halfling.Logging.Logger.Log($"Harmony lib not found.\nMod loading will not continue.");
                    Halfling.Logging.Logger.UnregisterLogOutputWriter(loggerWriter);
                    return;
                }

                try
                {
                    AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", false);
                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(harmonyLib);
                    Halfling.Logging.Logger.Log($"loaded harmony lib from {harmonyLib}");
                    PatchMethods(assembly);
                }
                catch (Exception ex)
                {
                    Halfling.Logging.Logger.Log($"failed to load harmony lib from {harmonyLib}, exception\n{ex}");
                }

                static bool isMethodPreInit(MethodInfo method) => method.Name == "AssemblyLoadInitializer" && method.GetParameters().Length == 0 && method.ReturnType == typeof(void);
                static bool isMethodPostInit(MethodInfo method) => method.Name == "GameLoadInitializer" && method.GetParameters().Length == 0 && method.ReturnType == typeof(void);
                static bool isMethodEMLInit(MethodInfo method) => method.Name == "InitializePatches" && method.GetParameters().Length == 0; // EML doesn't require void return

                foreach (var (guid, lib) in LibraryFiles.Where(lib => !lib.Value.duplicated && lib.Value.consented).Select(lib => (lib.Key, lib.Value.file)).ToList())
                {
                    try
                    {
                        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(lib);
                        Halfling.Logging.Logger.Log($"loaded mod lib from {lib}");

                        foreach (var type in assembly.GetTypes())
                        {
                            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                            {
                                if (isMethodPreInit(method))
                                {
                                    // EML required a special attribute, which prevents calling from managed code
                                    if (method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() == null)
                                    {
                                        method.Invoke(null, null);
                                        Halfling.Logging.Logger.Log($"called init method {method.Name} for mod lib {lib}");
                                    }
                                    else
                                    {
                                        CallFromUnmanaged(method.MethodHandle.GetFunctionPointer());
                                        Halfling.Logging.Logger.Log($"called unmanaged init method {method.Name} for mod lib {lib}");
                                    }
                                }

                                if (isMethodPostInit(method) || isMethodEMLInit(method))
                                {
                                    DelayedInitMethods.Add(method, guid);
                                }

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Halfling.Logging.Logger.Log($"failed to load mod lib from {lib}, exception\n{ex}");
                        if (LibraryFiles.TryGetValue(guid, out var modlib))
                        {
                            modlib.error = ex.Message;
                            LibraryFiles[guid] = modlib;
                        }
                    }
                }

                LibrariesInContext = [.. AssemblyLoadContext.Default.Assemblies.Select(a => a.ManifestModule.ModuleVersionId)];

                // start the async task for delayed initialization
                Halfling.Logging.Logger.Log("Mod loading complete. Starting the game now.");
                Halfling.Logging.Logger.UnregisterLogOutputWriter(loggerWriter);
                return;
            }
            catch (Exception ex)
            {
                Halfling.Logging.Logger.Log($"Exception during mod loading:\n{ex}");
                Halfling.Logging.Logger.Log($"Game will attempt to load without mods");
                Halfling.Logging.Logger.UnregisterLogOutputWriter(loggerWriter);
                return;
            }
        }

        /// <summary>
        /// This function asks harmony to patch various cosmoteer methods
        /// 
        /// It utilizes advanced reflection magic to keep this assembly independent from 0Harmony.dll
        /// 
        /// Harmony is still needed, but with this we can load it into context manually.
        /// Also it won't crash if for some reason harmony is not found.
        /// </summary>
        /// <param name="harmony">Loaded harmony assembly</param>
        static void PatchMethods(Assembly harmony)
        {
            var classHarmony = harmony.GetType("HarmonyLib.Harmony");
            var classHarmonyMethod = harmony.GetType("HarmonyLib.HarmonyMethod");

            var classHarmonyConstructor = classHarmony?.GetConstructor([typeof(string)]);
            var harmonyMethodConstructor = classHarmonyMethod?.GetConstructor([typeof(MethodInfo)]);

            var titleScreenConstructor = typeof(Cosmoteer.Gui.TitleScreen).GetConstructor([]);
            var titleScreenTranspilerMethodInfo = typeof(ModLoader).GetMethod(nameof(TitleScreenTranspiler), BindingFlags.Static | BindingFlags.NonPublic);
            var titleScreenTranspilerHarmonyMethod = harmonyMethodConstructor?.Invoke([titleScreenTranspilerMethodInfo]);

            var populateModList = typeof(Cosmoteer.Mods.ModInfo).GetMethod(nameof(Cosmoteer.Mods.ModInfo.LoadMods));
            var modListPostfixMethodInfo = typeof(ModLoader).GetMethod(nameof(ModListPostfix), BindingFlags.Static | BindingFlags.NonPublic);
            var modListPostfixHarmonyMethod = harmonyMethodConstructor?.Invoke([modListPostfixMethodInfo]);

            var onModSelected = typeof(Cosmoteer.Gui.ModsDialog).GetMethod(nameof(Cosmoteer.Gui.ModsDialog.OnModSelected), BindingFlags.Instance | BindingFlags.NonPublic);
            var onModSelectedPrefixMethodInfo = typeof(ModLoader).GetMethod(nameof(OnModSelectedPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            var onModSelectedPrefixHarmonyMethod = harmonyMethodConstructor?.Invoke([onModSelectedPrefixMethodInfo]);
            var onModSelectedPostfixMethodInfo = typeof(ModLoader).GetMethod(nameof(OnModSelectedPostfix), BindingFlags.Static | BindingFlags.NonPublic);
            var onModSelectedPostfixHarmonyMethod = harmonyMethodConstructor?.Invoke([onModSelectedPostfixMethodInfo]);

            var refreshToggleButtons = typeof(Cosmoteer.Gui.ModsDialog).GetMethod(nameof(Cosmoteer.Gui.ModsDialog.RefreshToggleButtons), BindingFlags.Instance | BindingFlags.NonPublic);
            var refreshToggleButtonsPostfixMethodInfo = typeof(ModLoader).GetMethod(nameof(RefreshToggleButtonsPostfix), BindingFlags.Static | BindingFlags.NonPublic);
            var refreshToggleButtonsPostfixHarmonyMethod = harmonyMethodConstructor?.Invoke([refreshToggleButtonsPostfixMethodInfo]);

            var settingsWriteTo = typeof(Cosmoteer.Settings).GetMethod(nameof(Cosmoteer.Settings.WriteTo));
            var settingsWritePostfixMethodInfo = typeof(ModLoader).GetMethod(nameof(SettingsWritePostfix), BindingFlags.Static | BindingFlags.NonPublic);
            var settingsWritePostfixHarmonyMethod = harmonyMethodConstructor?.Invoke([settingsWritePostfixMethodInfo]);

            var applicationMain = typeof(Halfling.Application.Bases.GenericApp).GetMethod(nameof(Halfling.Application.Bases.GenericApp.ApplicationMain));
            var applicationMainPrefixMethodInfo = typeof(ModLoader).GetMethod(nameof(ApplicationMainPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            var applicationMainPrefixHarmonyMethod = harmonyMethodConstructor?.Invoke([applicationMainPrefixMethodInfo]);


            var harmonyObj = classHarmonyConstructor?.Invoke(["Cosmoteer.ModLoader"]);
            var harmonyPatchMethod = classHarmony?.GetMethod("Patch");

            harmonyPatchMethod?.Invoke(harmonyObj, [titleScreenConstructor, null, null, titleScreenTranspilerHarmonyMethod, null]);
            harmonyPatchMethod?.Invoke(harmonyObj, [populateModList, null, modListPostfixHarmonyMethod, null, null]);
            harmonyPatchMethod?.Invoke(harmonyObj, [onModSelected, onModSelectedPrefixHarmonyMethod, onModSelectedPostfixHarmonyMethod, null, null]);
            harmonyPatchMethod?.Invoke(harmonyObj, [refreshToggleButtons, null, refreshToggleButtonsPostfixHarmonyMethod, null, null]);
            harmonyPatchMethod?.Invoke(harmonyObj, [settingsWriteTo, null, settingsWritePostfixHarmonyMethod, null, null]);
            harmonyPatchMethod?.Invoke(harmonyObj, [applicationMain, applicationMainPrefixHarmonyMethod, null, null, null]);
        }

        /// <summary>
        /// Patches Cosmoteer.Gui.TitleScreen() constructor by replacing the game version string.
        /// 
        /// Accesses all the Harmony stuff via reflection, to not depend on the assembly directly.
        /// </summary>
        private static IEnumerable<object> TitleScreenTranspiler(IEnumerable<object> instructions)
        {
            // instruction to replace:
            // 889	0C20	ldstr	"{game version}"
            FieldInfo? opCodeField = null;
            FieldInfo? operandField = null;
            // cosmoteer declares game version as const, so we need to extract it through reflection, otherwise compiler will evaluate it at compile time
            var gameVersion = typeof(Cosmoteer.Versions).GetField(nameof(Cosmoteer.Versions.GameVersionBuild))?.GetValue(null) as string ?? string.Empty;
            foreach (var instruction in instructions)
            {
                if (opCodeField == null)
                    opCodeField = instruction.GetType().GetField("opcode");

                var opCode = opCodeField?.GetValue(instruction);

                if (opCode != null && (System.Reflection.Emit.OpCode)opCode == System.Reflection.Emit.OpCodes.Ldstr)
                {
                    if (operandField == null)
                        operandField = instruction.GetType().GetField("operand");
                    var operand = operandField?.GetValue(instruction);
                    if (gameVersion == (operand as string))
                    {
                        operandField?.SetValue(instruction, $"{operand} with YAML ver. {Assembly.GetExecutingAssembly().GetName().Version}");
                    }
                }
            }

            return instructions;
        }

        /// <summary>
        /// Patches Cosmoteer.Mods.ModInfo.LoadMods
        /// 
        /// Adds errors caught from mod libs to the errorList
        /// </summary>
        private static void ModListPostfix(ref List<Cosmoteer.Mods.ModInfo> __result, IList<(string ModID, string Error)>? errorList)
        {
            if (showErrorMessageOnce || errorList == null)
            {
                return;
            }

            foreach (var mod in __result)
            {
                if (ModFolders.TryGetValue(mod.Folder, out var modFolder))
                {
                    foreach (var guid in modFolder)
                    {
                        var error = LibraryFiles[guid].error;
                        if (error != null)
                        {
                            errorList.Add((mod.ID, error));
                        }
                    }
                }
            }

            showErrorMessageOnce = true;
        }

        /// <summary>
        /// Constructs a label from parameters 
        /// </summary>
        private static Halfling.Gui.Label FormatLibList(string caption, string color, string[] libs)
        {
            var res = $"<{color}>{caption}";
            foreach (var lib in libs)
            {
                res += "\n" + lib;
            }
            res += $"</{color}>";
            var label = new Halfling.Gui.Label();
            label.Text = res;
            label.TextRenderer.HAlignment = Halfling.Graphics.Text.HAlignment.Left;
            label.TextRenderer.OversizeMode = Halfling.Graphics.Text.OversizeMode.Wrap;
            label.TextRenderer.XmlFormatting = true;
            label.AutoSize.AutoHeightMode = Halfling.Gui.AutoSizeMode.Enable;
            return label;
        }

        /// <summary>
        /// Callback for the "Trust" button, adds libs to the list of trusted
        /// </summary>
        private static void OnTrustButtonClicked(object? sender, EventArgs e)
        {
            if (sender is Halfling.Gui.Components.Input.WidgetClickController controller && controller.Widget?.UserData is Halfling.IO.AbsolutePath folder)
            {
                foreach(var guid in ModFolders[folder])
                {
                    KnownModLibraries.Add(guid);
                }
                controller.Widget.SelfInputActive = false;
            }    
        }

        /// <summary>
        /// Callback for the "Trust Mod" button, adds mod to the list of trusted
        /// </summary>
        private static void OnTrustModButtonClicked(object? sender, EventArgs e)
        {
            if (sender is Halfling.Gui.Components.Selection.WidgetTriggeredSelectionController controller && controller.Widget?.UserData is Halfling.IO.AbsolutePath folder)
            {
                if (controller.IsSelected)
                    TrustedMods.Add(folder);
                else
                    TrustedMods.Remove(folder);
            }
        }


        /// <summary>
        /// Patches Cosmoteer.Gui.ModsDialog.OnModSelected
        /// 
        /// Tries to find mod libraries if they are not in the list
        /// </summary>
        private static void OnModSelectedPrefix(Cosmoteer.Gui.ModsDialog __instance)
        {
            if (__instance._mods.SelectedWidget == null)
            {
                return;
            }
            if (__instance._mods.SelectedWidget.IsModEnabled && !ModFolders.ContainsKey(__instance._mods.SelectedWidget.ModInfo.Folder))
            {
                LoadLibsForFolder(__instance._mods.SelectedWidget.ModInfo.Folder);
            }
        }

        /// <summary>
        /// Patches Cosmoteer.Gui.ModsDialog.OnModSelected
        /// 
        /// Adds labels showing the status of mod libs
        /// </summary>
        private static void OnModSelectedPostfix(Cosmoteer.Gui.ModsDialog __instance)
        {
            var count = __instance._descBox.Children.Count;
            if (count < 3)
            {
                return;
            }
            var modInfo = __instance._mods.SelectedWidget?.ModInfo;
            if (modInfo == null)
            {
                return;
            }
            if (ModFolders.TryGetValue(modInfo.Folder, out var mod) && mod.Count != 0)
            {
                var libsOk = mod.Select(guid => LibraryFiles[guid]).Where(lib => lib.duplicated == false && lib.consented == true && lib.error == null).Select(lib => Path.GetFileName(lib.file)).ToArray();
                var libsError = mod.Select(guid => LibraryFiles[guid]).Where(lib => lib.duplicated == false && lib.consented == true && lib.error != null).Select(lib => Cosmoteer.Localization.Strings.FormatText("ModLoader/libError", [Path.GetFileName(lib.file), lib.error])).ToArray();
                var libsDup = mod.Select(guid => (guid, LibraryFiles[guid])).Where(lib => lib.Item2.duplicated == true).Select(lib => Cosmoteer.Localization.Strings.FormatText("ModLoader/libDup", [Path.GetFileName(lib.Item2.file), ModFolders.Where(kvp => kvp.Value.Contains(lib.guid) && kvp.Key != modInfo.Folder).Select(kvp => __instance._mods.Children.Select(child => child.ModInfo).FirstOrDefault(modinfo => modinfo.Folder == kvp.Key)?.Name).Aggregate("", (current, next) => current + (current.Length > 0 && next?.Length > 0 ? ", " : "") + next)])).ToArray(); // because I can!
                var libsUnknown = mod.Select(guid => LibraryFiles[guid]).Where(lib => lib.consented == false).Select(lib => Path.GetFileName(lib.file)).ToArray();
                var libsUnknownStill = mod.Where(guid => !KnownModLibraries.Contains(guid)).Select(guid => LibraryFiles[guid]).Where(lib => lib.consented == false).Select(lib => Path.GetFileName(lib.file)).Any();

                if (modInfo.Description != null)
                {
                    count--;
                }
                if (modInfo.Logo != null)
                {
                    count--;
                }
                if (libsOk.Length != 0)
                {
                    __instance._descBox.Children.Insert(count, FormatLibList(Cosmoteer.Localization.Strings.GetText("ModLoader/libsOk"), "good", libsOk));
                }
                if (libsDup.Length != 0)
                {
                    __instance._descBox.Children.Insert(count, FormatLibList(Cosmoteer.Localization.Strings.GetText("ModLoader/libsDup"), "bad", libsDup));
                }
                if (libsError.Length != 0)
                {
                    __instance._descBox.Children.Insert(count, FormatLibList(Cosmoteer.Localization.Strings.GetText("ModLoader/libsError"), "bad", libsError));
                }
                if (libsUnknown.Length != 0)
                {
                    var btn = new Halfling.Gui.Button(Cosmoteer.Gui.WidgetRules.Instance.GoodButton)
                    {
                        PercentileRect = new Halfling.Geometry.Rect(0f, 0f, 50f, 10f),
                        Right = -5f,
                        TextProvider = Cosmoteer.Localization.Strings.KeyString("ModLoader/trust"),
                        SelfActive = true,
                        SelfInputActive = libsUnknownStill && !TrustedMods.Contains(modInfo.Folder),
                        UserData = modInfo.Folder
                    };
                    btn.Clicked += OnTrustButtonClicked;
                    __instance._descBox.Children.Insert(count, btn);
                    __instance._descBox.Children.Insert(count, FormatLibList(Cosmoteer.Localization.Strings.GetText("ModLoader/libsUnknown"), "bad", libsUnknown));
                }
                if (modInfo.Folder.IsSubPathOf(Cosmoteer.Paths.UserModsFolder))
                {
                    var modBtn = new Halfling.Gui.ToggleButton(Cosmoteer.Gui.WidgetRules.Instance.ToggleCheckButton)
                    {
                        PercentileRect = new Halfling.Geometry.Rect(0f, 0f, 50f, 10f),
                        Right = -5f,
                        TextProvider = Cosmoteer.Localization.Strings.KeyString("ModLoader/trustMod"),
                        SelfActive = true,
                        IsSelected = TrustedMods.Contains(modInfo.Folder),
                        SelfInputActive = true,
                        UserData = modInfo.Folder
                    };
                    modBtn.SelectionChanged += OnTrustModButtonClicked;
                    __instance._descBox.Children.Insert(1, modBtn);
                }

            }
        }

        /// <summary>
        /// Patches Cosmoteer.Gui.ModsDialog.RefreshToggleButtons
        /// 
        /// Updates the mod window when the mod is enabled or disabled
        /// </summary>
        private static void RefreshToggleButtonsPostfix(Cosmoteer.Gui.ModsDialog __instance)
        {
            if (__instance._mods.SelectedWidget == null)
            {
                return;
            }
            if (__instance._mods.SelectedWidget.IsModEnabled && !ModFolders.ContainsKey(__instance._mods.SelectedWidget.ModInfo.Folder))
            {
                LoadLibsForFolder(__instance._mods.SelectedWidget.ModInfo.Folder);
                OnModSelectedPostfix(__instance);
            }
            if (!__instance._mods.SelectedWidget.IsModEnabled && ModFolders.TryGetValue(__instance._mods.SelectedWidget.ModInfo.Folder, out var libs))
            {
                foreach(var guid in libs)
                {
                    if (!LibrariesInContext.Contains(guid))
                    {
                        LibraryFiles.Remove(guid);
                    }
                    KnownModLibraries.Remove(guid);
                }
                ModFolders.Remove(__instance._mods.SelectedWidget.ModInfo.Folder);
                __instance.OnModSelected(null, new Halfling.Gui.WidgetEventArgs(__instance._mods.SelectedWidget));
            }
        }

        /// <summary>
        /// Patches Cosmoteer.Settings.WriteTo
        /// 
        /// Adds the list of trusted libs to the saved settings
        /// </summary>
        private static void SettingsWritePostfix(Halfling.Serialization.Generic.GenericSerialWriter writer)
        {
            // remove the libraries that are no longer present
            KnownModLibraries.IntersectWith(LibraryFiles.Where(kvp => kvp.Value.fromTrusted == false).Select(kvp => kvp.Key));

            // remove the mods that are not enabled
            TrustedMods.IntersectWith(Cosmoteer.Settings.EnabledMods);

            if (KnownModLibraries.Count > 0)
            {
                writer.WriteToPath(nameof(KnownModLibraries), KnownModLibraries.Select(guid => guid.ToString()).ToArray());
            }
            if (TrustedMods.Count > 0)
            {
                writer.WriteToPath(nameof(TrustedMods), TrustedMods);
            }

        }

        /// <summary>
        /// Patches Halfling.Application.Bases.ApplicationMain
        /// 
        /// Runs delayed inits before the game loop starts
        /// </summary>
        private static void ApplicationMainPrefix()
        {
            foreach (var (method, guid) in DelayedInitMethods)
            {
                var lib = LibraryFiles[guid].file;
                try
                {
                    if (method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() == null)
                    {
                        method.Invoke(null, null);
                        Halfling.Logging.Logger.Log($"called delayed init method {method.Name} for mod lib {lib}");
                    }
                    else
                    {
                        CallFromUnmanaged(method.MethodHandle.GetFunctionPointer());
                        Halfling.Logging.Logger.Log($"called delayed unmanaged init method {method.Name} for mod lib {lib}");
                    }
                }
                catch (Exception ex)
                {
                    Halfling.Logging.Logger.Log($"failed to load mod lib from {lib}, exception\n{ex}");
                    if (LibraryFiles.TryGetValue(guid, out var modlib))
                    {
                        modlib.error = ex.Message;
                        LibraryFiles[guid] = modlib;
                    }
                }
            }
        }
    }
}

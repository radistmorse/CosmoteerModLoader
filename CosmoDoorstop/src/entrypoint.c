#include "wincrt.h"
#include "logger.h"
#include "hook.h"

#define INIT_FUNCTION "hostfxr_main_startupinfo"

#define ASSEMBLY_NAME TEXT("ModLoader.dll")

int (*hostfxr_main_startupinfo)(const int argc, const char_t *argv[],
                                const char_t *host_path,
                                const char_t *dotnet_root,
                                const char_t *app_path) = NULL;

struct {
    char_t *app_dir;
    char_t *doorstop_path;
    char_t *doorstop_filename;
} paths;

char_t *get_module_path(void *module) {
    DWORD i = 0;
    DWORD s;
    char_t *result = NULL;
    do {
        if (result != NULL)
            heap_free(result);
        i++;
        s = i * MAX_PATH + 1;
        result = heap_malloc(sizeof(char_t) * s);
        GetModuleFileName(module, result, s);
    } while (GetLastError() == ERROR_INSUFFICIENT_BUFFER);

    return result;
}

void paths_init(void *doorstop_module) {
    char_t *app_path = get_module_path(NULL);
    char_t *separator = strrchr(app_path, '\\');
    *separator = '\0';
    char_t *doorstop_path = get_module_path(doorstop_module);
    separator = strrchr(doorstop_path, '\\');
    char_t *doorstop_filename = strdup(separator + 1);

    paths.app_dir = app_path;
    paths.doorstop_path = doorstop_path;
    paths.doorstop_filename = doorstop_filename;
    return;
}

int init_function_intercept(const int argc, const char_t** argv,
    const char_t* host_path,
    const char_t* dotnet_root,
    const char_t* app_path) {

    LOG("Intercepted game startup, redirecting");


    size_t len = strlen(paths.app_dir);
    char_t* assembly = heap_malloc(
        (len + STR_LEN(ASSEMBLY_NAME) + 1) * sizeof(char_t));

    strcpy(assembly, paths.app_dir);
    assembly[len] = '\\';
    strcpy(assembly + len + 1, ASSEMBLY_NAME);

    DWORD ab = GetFileAttributes(assembly);
    if (ab != INVALID_FILE_ATTRIBUTES &&
        (ab & FILE_ATTRIBUTE_DIRECTORY) == 0) {
        LOG("Redirecting to the assembly: %s", assembly);

        return hostfxr_main_startupinfo(argc, argv, host_path, dotnet_root,
            assembly);
    }

    LOG("Assembly not found: %s\nReverting to the original", assembly);
    return hostfxr_main_startupinfo(argc, argv, host_path, dotnet_root,
        app_path);
}


bool_t initialized = FALSE;
void *WINAPI get_proc_address_detour(void *module, char *name) {

    LOG("Captured %S at %p", name, module);

    // If the lpProcName pointer contains an ordinal rather than a string,
    // high-word value of the pointer is zero (see Doorstop PR #66)
    if (HIWORD(name) && lstrcmpA(name, INIT_FUNCTION) == 0) {
        if (!initialized) {
            initialized = TRUE;
            LOG("Got %S at %p", name, module);
			hostfxr_main_startupinfo = (void *)GetProcAddress(module, INIT_FUNCTION);
            LOG("Loaded runtime function\n")
        }
        return (void *)(init_function_intercept);
    }

    return (void *)GetProcAddress(module, name);
}

void inject(void) {
    LOG("Doorstop enabled!");
    HMODULE app_module = GetModuleHandle(NULL);

    LOG("Installing IAT hook");
    bool_t ok = iat_hook(app_module, "kernel32.dll", &GetProcAddress,
                         &get_proc_address_detour);

    if (!ok) {
        LOG("Failed to install IAT hook!");
        free_logger();
    } else {
        LOG("Hooks installed");
    }
}

BOOL WINAPI DllEntry(HINSTANCE hInstDll, DWORD reasonForDllLoad,
                     LPVOID reserved) {
    if (reasonForDllLoad != DLL_PROCESS_ATTACH)
        return TRUE;

    init_crt();

    paths_init(hInstDll);
    init_logger(paths.app_dir);

    LOG("Doorstop started!");
    LOG("Application dir: %s", paths.app_dir);
    LOG("Doorstop library path: %s", paths.doorstop_path);
    LOG("Doorstop library name: %s", paths.doorstop_filename);

    inject();

    return TRUE;
}



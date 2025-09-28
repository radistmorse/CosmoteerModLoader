#ifndef PROXY_H
#define PROXY_H

#include "../wincrt.h"
#include "../logger.h"
#include <windows.h>

extern void load_functions(void *dll);

static inline void load_proxy(char_t *module_name) {
    size_t module_name_len = strlen(module_name);

    UINT sys_len = GetSystemDirectory(NULL, 0);
    char_t *sys_full_path = (char_t *)heap_malloc(
        (sys_len + module_name_len) *
        sizeof(char_t));
    GetSystemDirectory(sys_full_path, sys_len);
    sys_full_path[sys_len - 1] = TEXT('\\');
    strcpy(sys_full_path + sys_len, module_name);

    LOG("Looking for original DLL from %s", sys_full_path);

    void *handle = LoadLibrary(sys_full_path);
    heap_free(sys_full_path);

    ASSERT(handle != NULL, "Unable to load original %s.dll!", module_name);

    load_functions(handle);
}

#endif

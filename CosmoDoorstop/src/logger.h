#ifndef LOGGER_WIN_H
#define LOGGER_WIN_H

#include <windows.h>
#include "wincrt.h"

#define printf wsprintfW

#if VERBOSE

extern HANDLE log_handle;
extern char_t buffer[4096];

static inline void init_logger(char_t *path) {
    printf(buffer, TEXT("\\\\\?\\%s\\cosmodoorstop_%lx.log"), path, GetTickCount());
    log_handle = CreateFile(buffer, GENERIC_WRITE, FILE_SHARE_READ, NULL,
                            CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
}

static inline void free_logger(void) { CloseHandle(log_handle); }

inline char* narrow(const char_t* str) {
    const int req_size =
        WideCharToMultiByte(CP_UTF8, 0, str, -1, NULL, 0, NULL, NULL);
    char* result = heap_malloc(req_size * sizeof(char));
    WideCharToMultiByte(CP_UTF8, 0, str, -1, result, req_size, NULL, NULL);
    return result;
}

#if !defined(_MSVC_TRADITIONAL) || _MSVC_TRADITIONAL
#define LOG(message, ...)                                                      \
    {                                                                          \
        size_t len = printf(buffer, TEXT(message) TEXT("\n"), ##__VA_ARGS__);  \
        char *log_data = narrow(buffer);                                       \
        WriteFile(log_handle, log_data, len, NULL, NULL);                      \
        heap_free(log_data);                                                   \
    }
#else
#define LOG(message, ...)                                                      \
    {                                                                          \
        size_t len = printf(buffer, TEXT(message) TEXT("\n") __VA_OPT__(, ) __VA_ARGS__); \
        char *log_data = narrow(buffer);                                       \
        WriteFile(log_handle, log_data, len, NULL, NULL);                      \
        heap_free(log_data);                                                   \
    }
#endif

#else

#define LOG(message, ...)

static inline void init_logger(char_t *path) {}
static inline void free_logger(void) {}

#endif

#define ASSERT(test, message, ...)                                             \
    if (!(test)) {                                                             \
        char_t *buff = (char_t *)heap_malloc(sizeof(char_t) * 1024);           \
        printf(buff, TEXT(message) TEXT("\n"), __VA_ARGS__);                   \
        MessageBox(NULL, buff, TEXT("Doorstop: Fatal"), MB_OK | MB_ICONERROR); \
        heap_free(buff);                                                       \
        ExitProcess(EXIT_FAILURE);                                             \
    }

#endif

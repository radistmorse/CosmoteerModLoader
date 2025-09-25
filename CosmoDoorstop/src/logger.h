#ifndef LOGGER_WIN_H
#define LOGGER_WIN_H
#if VERBOSE

#include <windows.h>
#include "wincrt.h"

extern HANDLE log_handle;
extern char_t buffer[4096];

#ifdef UNICODE
#define printf wsprintfW
#else
#define printf wsprintfA
#endif

static inline void init_logger(char_t *path) {
    printf(buffer, TEXT("\\\\\?\\C:\\Program Files (x86)\\Steam\\steamapps\\common\\Cosmoteer\\Bin\\doorstop_%lx.log"), GetTickCount());
    log_handle = CreateFile(buffer, GENERIC_WRITE, FILE_SHARE_READ, NULL,
                            CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
}

static inline void free_logger() { CloseHandle(log_handle); }

inline char* narrow(const char_t* str) {
#ifndef UNICODE
    char* result = (char*)malloc(strlen(str) + 1);
    strcpy(result, str);
    return result;
#else
    const int req_size =
        WideCharToMultiByte(CP_UTF8, 0, str, -1, NULL, 0, NULL, NULL);
    char* result = malloc(req_size * sizeof(char));
    WideCharToMultiByte(CP_UTF8, 0, str, -1, result, req_size, NULL, NULL);
    return result;
#endif
}

#if !defined(_MSVC_TRADITIONAL) || _MSVC_TRADITIONAL
#define LOG(message, ...)                                                      \
    {                                                                          \
        size_t len = printf(buffer, TEXT(message) TEXT("\n"), ##__VA_ARGS__);  \
        char *log_data = narrow(buffer);                                       \
        WriteFile(log_handle, log_data, len, NULL, NULL);                      \
        free(log_data);                                                        \
    }
#else
#define LOG(message, ...)                                                      \
    {                                                                          \
        size_t len = printf(buffer, TEXT(message) TEXT("\n") __VA_OPT__(, ) __VA_ARGS__); \
        char *log_data = narrow(buffer);                                       \
        WriteFile(log_handle, log_data, len, NULL, NULL);                      \
        free(log_data);                                                        \
    }
#endif

#else

#define LOG(message, ...)

static inline void init_logger() {}
static inline void free_logger() {}

#endif

#define ASSERT(test, message, ...)                                             \
    if (!(test)) {                                                             \
        char_t *buff = (char_t *)malloc(sizeof(char_t) * 1024);                \
        printf(buff, TEXT(message) TEXT("\n"), __VA_ARGS__);                   \
        MessageBox(NULL, buff, TEXT("Doorstop: Fatal"), MB_OK | MB_ICONERROR); \
        free(buff);                                                            \
        ExitProcess(EXIT_FAILURE);                                             \
    }

#endif

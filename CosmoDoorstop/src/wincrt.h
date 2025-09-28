/*
 * Custom implementation for common C runtime functions
 * This makes the DLL essentially freestanding on Windows without having to rely
 * on msvcrt.dll
 */
#ifndef WIN_CRT_H
#define WIN_CRT_H

#include <windows.h>

typedef TCHAR char_t;
typedef BOOL bool_t;

extern void init_crt(void);

#define STR_LEN(str) (sizeof(str) / sizeof((str)[0]))

extern void *memset(void *dst, int c, size_t n);
#pragma intrinsic(memset)

extern void *memcpy(void *dst, const void *src, size_t n);
#pragma intrinsic(memcpy)

extern size_t strlen_wide(const char_t *str);
#define strlen strlen_wide

extern void *heap_malloc(size_t size);

extern void heap_free(void *mem);

extern char_t *strcpy_wide(char_t *dst, const char_t *src);
#define strcpy strcpy_wide

extern char_t *strrchr_wide(const char_t *str, int chr);
#define strrchr strrchr_wide

extern char_t *strdup_wide(const char_t *str);
#define strdup strdup_wide

#endif

#include "wincrt.h"

static HANDLE h_heap;

void init_crt() { h_heap = GetProcessHeap(); }

#pragma function(memset)
void *memset(void *dst, int c, size_t n) {
    char *d = dst;
    while (n--)
        *d++ = (char)c;
    return dst;
}

#pragma function(memcpy)
void *memcpy(void *dst, const void *src, size_t n) {
    char *d = dst;
    const char *s = src;
    while (n--)
        *d++ = *s++;
    return dst;
}

void *dlsym(void *handle, const char *name) {
    return GetProcAddress((HMODULE)handle, name);
}

void *dlopen(const char_t *filename, int flag) { return LoadLibrary(filename); }

void free(void *mem) { HeapFree(h_heap, 0, mem); }

int setenv(const char_t *name, const char_t *value, int overwrite) {
    return !SetEnvironmentVariable(name, value);
}

size_t strlen_wide(char_t const *str) {
    size_t result = 0;
    while (*str++)
        result++;
    return result;
}

void *malloc(size_t size) {
    return HeapAlloc(h_heap, HEAP_GENERATE_EXCEPTIONS, size);
}

void *calloc(size_t num, size_t size) {
    return HeapAlloc(h_heap, HEAP_ZERO_MEMORY, size * num);
}

char_t *strcat_wide(char_t *dst, const char_t *src) {
    size_t size = strlen(dst);
    size_t size2 = strlen(src);
    return strncpy(dst + size, src, size2 + 1);
}

char_t *strcpy_wide(char_t *dst, const char_t *src) {
    char_t *d = dst;
    const char_t *s = src;
    while (*s)
        *d++ = *s++;
    *d = *s;
    return dst;
}

char_t *strncpy_wide(char_t *dst, const char_t *src, size_t n) {
    char_t *d = dst;
    const char_t *s = src;
    while (n--)
        *d++ = *s++;
    return dst;
}

char_t *strrchr_wide(const char_t *str, int chr)
{
    char_t *save;
    do {
        if (*str == chr)
            save = (char_t*)str;
    } while (*++str);
    return save;
}

char_t *strdup_wide(const char_t* str)
{
    size_t siz = sizeof(char_t) * (strlen(str) + 1);
    char_t *copy = malloc(siz);
    memcpy(copy, str, siz);
    return copy;
}

char_t *getenv_wide(const char_t *name) {
    DWORD size = GetEnvironmentVariable(name, NULL, 0);
    if (size == 0)
        return NULL;
    char_t *buf = calloc(size + 1, sizeof(char_t));
    GetEnvironmentVariable(name, buf, size + 1);
    return buf;
}

void shutenv(char_t *buf) {
    if (buf) {
        free(buf);
    }
}

void *fopen(char_t *filename, const char_t *mode) {
    return CreateFile(filename, GENERIC_READ, FILE_SHARE_READ, NULL,
                      OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
}

size_t fread(void *ptr, size_t size, size_t count, void *stream) {
    size_t read_size = 0;
    ReadFile(stream, ptr, size * count, &read_size, NULL);
    return read_size;
}

int fclose(void *stream) { CloseHandle(stream); }
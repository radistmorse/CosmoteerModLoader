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

size_t strlen_wide(char_t const *str) {
    size_t result = 0;
    while (*str++)
        result++;
    return result;
}

void *heap_malloc(size_t size) {
    return HeapAlloc(h_heap, HEAP_GENERATE_EXCEPTIONS, size);
}

void heap_free(void *mem) { HeapFree(h_heap, 0, mem); }

char_t *strcpy_wide(char_t *dst, const char_t *src) {
    char_t *d = dst;
    const char_t *s = src;
    while (*s)
        *d++ = *s++;
    *d = *s;
    return dst;
}

char_t *strrchr_wide(const char_t *str, int chr)
{
    char_t *save = NULL;
    do {
        if (*str == chr)
            save = (char_t*)str;
    } while (*++str);
    return save;
}

char_t *strdup_wide(const char_t* str)
{
    size_t siz = sizeof(char_t) * (strlen(str) + 1);
    char_t *copy = heap_malloc(siz);
    memcpy(copy, str, siz);
    return copy;
}

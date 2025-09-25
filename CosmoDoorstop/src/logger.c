#include "logger.h"
#include "wincrt.h"
#include <windows.h>

#if VERBOSE
HANDLE log_handle;
char_t buffer[4096];
#endif
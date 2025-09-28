#include <windows.h>

void WINAPI CallFromUnmanaged(void (*func)(void)) {
    func();
}

#ifdef STANDALONE
BOOL WINAPI DllEntry(HINSTANCE hInstDll, DWORD reasonForDllLoad,
    LPVOID reserved) {
    return TRUE;
}
#endif

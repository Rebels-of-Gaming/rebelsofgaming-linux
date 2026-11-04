// Nativer Starter der macOS-App (ersetzt den .NET-apphost in Contents/MacOS/rebelsofgaming).
//
// Warum: Der .NET-apphost linkt kein AppKit – AppKit kommt erst später per dlopen (libAvaloniaNative). In der App
// Sandbox meldet sich AppKit dann bei LaunchServices an, launchd verweigert die Suche nach
// com.apple.coreservices.launchservicesd und die App bricht in [NSApplication sharedApplication] ab (Bauplan M9).
// Ein Programm, das AppKit beim Start mitlädt, ist davon nicht betroffen. Dieser Starter linkt AppKit und startet dann
// die .NET-Laufzeit wie der apphost: libhostfxr.dylib neben dem Programm, rebelsofgaming.dll als App.
#include <dlfcn.h>
#include <libgen.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef int (*hostfxr_main_startupinfo_fn)(int argc, const char **argv, const char *host_path,
                                           const char *dotnet_root, const char *app_path);

// Verweis auf AppKit, damit der Linker die Bibliothek nicht wegoptimiert (-dead_strip_dylibs)
extern void *NSApp;
void *volatile rog_appkit_anchor = &NSApp;

int main(int argc, const char **argv)
{
    char exe[PATH_MAX];
    uint32_t size = sizeof exe;
    char real[PATH_MAX];
    if (_NSGetExecutablePath(exe, &size) != 0 || realpath(exe, real) == NULL) {
        fprintf(stderr, "rebelsofgaming: Programmpfad nicht ermittelbar\n");
        return 1;
    }

    char dir_buffer[PATH_MAX];
    strlcpy(dir_buffer, real, sizeof dir_buffer);
    const char *dir = dirname(dir_buffer);
    char hostfxr[PATH_MAX];
    char app[PATH_MAX];
    snprintf(hostfxr, sizeof hostfxr, "%s/libhostfxr.dylib", dir);
    snprintf(app, sizeof app, "%s/rebelsofgaming.dll", dir);

    void *library = dlopen(hostfxr, RTLD_NOW | RTLD_LOCAL);
    if (library == NULL) {
        fprintf(stderr, "rebelsofgaming: %s\n", dlerror());
        return 1;
    }

    hostfxr_main_startupinfo_fn start = (hostfxr_main_startupinfo_fn)dlsym(library, "hostfxr_main_startupinfo");
    if (start == NULL) {
        fprintf(stderr, "rebelsofgaming: hostfxr_main_startupinfo fehlt\n");
        return 1;
    }

    // host_path = dieses Programm, dotnet_root = App-Ordner (self-contained), app_path = Haupt-Assembly
    return start(argc, argv, real, dir, app);
}

// Mise à jour automatique de Wilds DPS Meter : plugin REFramework (reframework\plugins\wilds_dps_meter_updater.dll).
//
// Le Lua de REFramework n'a pas accès au réseau : ce plugin le fait à sa place. REFramework charge les DLL de
// reframework\plugins tout au début du lancement du jeu, bien avant les scripts Lua (ScriptRunner). Au chargement,
// un thread :
//   1. lit mh-wilds/release/update.txt sur GitHub (version publiée + liste des fichiers avec leur SHA-256) ;
//   2. si cette version est plus récente que reframework\autorun\wilds_dps_meter\version.txt, télécharge chaque
//      fichier, vérifie son SHA-256, l'écrit à côté (.new) ;
//   3. seulement si tout est bon, remplace les fichiers (version.txt en dernier, il est le dernier de la liste).
// Toute erreur (pas de réseau, cache de GitHub pas encore à jour…) laisse les fichiers tels quels : nouvel essai au
// prochain lancement. Le plugin n'utilise pas l'API de REFramework : il n'exporte rien, son travail se fait dans le
// thread lancé par DllMain.
//
// Sécurité : seule source, raw.githubusercontent.com/Ghermin/dream-dps-meter/main/mh-wilds/release/ ; seules
// destinations, des chemins relatifs sous reframework\ dans le dossier du jeu.
// Développement : reframework\data\wilds_dps_meter_repo.txt peut contenir le chemin d'un dossier local (racine du
// dépôt) à lire à la place de GitHub.
// Résultat : reframework\data\wilds_dps_meter_update.txt (lu et affiché par le script).
//
// Compilation : mh-wilds\updater\build.ps1 (zig cc).

#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <winhttp.h>
#include <bcrypt.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#define HOST L"raw.githubusercontent.com"
#define REPO_PATH L"/Ghermin/dream-dps-meter/main/"
#define RELEASE_PREFIX "mh-wilds/release/"
#define MANIFEST RELEASE_PREFIX "update.txt"
#define MAX_FILES 32
#define MAX_DOWNLOAD (8 * 1024 * 1024)

static wchar_t g_game[MAX_PATH];   // dossier du jeu, avec '\' final
static wchar_t g_local[MAX_PATH];  // dépôt local (développement), avec '\' final, ou vide
static char g_log[4096];

static void note(const char* fmt, ...) {
    size_t n = strlen(g_log);
    if (n >= sizeof(g_log) - 2) return;
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(g_log + n, sizeof(g_log) - n - 1, fmt, ap);
    va_end(ap);
    strcat(g_log, "\n");
}

static void game_path(wchar_t* out, const char* rel) {
    wcscpy(out, g_game);
    size_t n = wcslen(out);
    MultiByteToWideChar(CP_UTF8, 0, rel, -1, out + n, (int)(MAX_PATH - n));
    for (wchar_t* p = out; *p; p++) if (*p == L'/') *p = L'\\';
}

static void write_log(void) {
    wchar_t path[MAX_PATH];
    game_path(path, "reframework\\data");
    CreateDirectoryW(path, NULL);
    game_path(path, "reframework\\data\\wilds_dps_meter_update.txt");
    HANDLE h = CreateFileW(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return;
    DWORD w;
    WriteFile(h, g_log, (DWORD)strlen(g_log), &w, NULL);
    CloseHandle(h);
}

// ---------------------------------------------------------------- lecture de fichiers / téléchargement

typedef struct { char* data; size_t size; } Buffer;

static int read_file(const wchar_t* path, Buffer* out) {
    HANDLE h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    if (h == INVALID_HANDLE_VALUE) return 0;
    LARGE_INTEGER size;
    if (!GetFileSizeEx(h, &size) || size.QuadPart > MAX_DOWNLOAD) { CloseHandle(h); return 0; }
    out->data = (char*)malloc((size_t)size.QuadPart + 1);
    DWORD r = 0;
    int ok = out->data && ReadFile(h, out->data, (DWORD)size.QuadPart, &r, NULL) && r == (DWORD)size.QuadPart;
    CloseHandle(h);
    if (!ok) { free(out->data); out->data = NULL; return 0; }
    out->size = r;
    out->data[r] = 0;
    return 1;
}

static HINTERNET g_session, g_connect;

static int http_open(void) {
    g_session = WinHttpOpen(L"WildsDpsMeter-Updater/1", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_NO_PROXY_NAME,
                            WINHTTP_NO_PROXY_BYPASS, 0);
    if (!g_session) // avant Windows 8.1
        g_session = WinHttpOpen(L"WildsDpsMeter-Updater/1", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME,
                                WINHTTP_NO_PROXY_BYPASS, 0);
    if (!g_session) return 0;
    WinHttpSetTimeouts(g_session, 5000, 5000, 5000, 10000);
    g_connect = WinHttpConnect(g_session, HOST, INTERNET_DEFAULT_HTTPS_PORT, 0);
    return g_connect != NULL;
}

static void http_close(void) {
    if (g_connect) WinHttpCloseHandle(g_connect);
    if (g_session) WinHttpCloseHandle(g_session);
    g_connect = g_session = NULL;
}

/// Fichier `rel` du dépôt (chemin relatif à sa racine), depuis GitHub ou le dépôt local.
static int fetch(const char* rel, Buffer* out) {
    out->data = NULL;
    out->size = 0;
    if (g_local[0]) {
        wchar_t path[MAX_PATH];
        wcscpy(path, g_local);
        size_t n = wcslen(path);
        MultiByteToWideChar(CP_UTF8, 0, rel, -1, path + n, (int)(MAX_PATH - n));
        for (wchar_t* p = path; *p; p++) if (*p == L'/') *p = L'\\';
        return read_file(path, out);
    }
    wchar_t url[1024] = REPO_PATH;
    size_t n = wcslen(url);
    MultiByteToWideChar(CP_UTF8, 0, rel, -1, url + n, (int)(1024 - n));
    HINTERNET req = WinHttpOpenRequest(g_connect, L"GET", url, NULL, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES,
                                       WINHTTP_FLAG_SECURE);
    if (!req) return 0;
    int ok = 0;
    DWORD status = 0, len = sizeof(status);
    if (WinHttpSendRequest(req, L"Cache-Control: no-cache\r\n", (DWORD)-1L, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) &&
        WinHttpReceiveResponse(req, NULL) &&
        WinHttpQueryHeaders(req, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX,
                            &status, &len, WINHTTP_NO_HEADER_INDEX) &&
        status == 200) {
        size_t cap = 64 * 1024;
        out->data = (char*)malloc(cap + 1);
        ok = out->data != NULL;
        for (;;) {
            DWORD avail = 0, read = 0;
            if (!ok || !WinHttpQueryDataAvailable(req, &avail)) { ok = 0; break; }
            if (avail == 0) break;
            if (out->size + avail > MAX_DOWNLOAD) { ok = 0; break; }
            if (out->size + avail > cap) {
                while (out->size + avail > cap) cap *= 2;
                char* p = (char*)realloc(out->data, cap + 1);
                if (!p) { ok = 0; break; }
                out->data = p;
            }
            if (!WinHttpReadData(req, out->data + out->size, avail, &read)) { ok = 0; break; }
            out->size += read;
        }
        if (ok) out->data[out->size] = 0;
    } else if (status) {
        note("HTTP %lu : %s", status, rel);
    }
    WinHttpCloseHandle(req);
    if (!ok) { free(out->data); out->data = NULL; out->size = 0; }
    return ok;
}

// ---------------------------------------------------------------- SHA-256 (BCrypt)

static int sha256_hex(const Buffer* b, char out[65]) {
    BCRYPT_ALG_HANDLE alg;
    UCHAR hash[32];
    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, NULL, 0) != 0) return 0;
    int ok = BCryptHash(alg, NULL, 0, (PUCHAR)b->data, (ULONG)b->size, hash, sizeof(hash)) == 0;
    BCryptCloseAlgorithmProvider(alg, 0);
    for (int i = 0; i < 32; i++) sprintf(out + i * 2, "%02x", hash[i]);
    return ok;
}

// ---------------------------------------------------------------- versions

static void parse_version(const char* s, int v[4]) {
    v[0] = v[1] = v[2] = v[3] = 0;
    for (int i = 0; i < 4 && *s; i++) {
        v[i] = atoi(s);
        while (*s >= '0' && *s <= '9') s++;
        if (*s != '.') break;
        s++;
    }
}

static int version_cmp(const char* a, const char* b) {
    int x[4], y[4];
    parse_version(a, x);
    parse_version(b, y);
    for (int i = 0; i < 4; i++) if (x[i] != y[i]) return x[i] < y[i] ? -1 : 1;
    return 0;
}

static int valid_version(const char* s) {
    if (!*s) return 0;
    for (; *s; s++) if (!((*s >= '0' && *s <= '9') || *s == '.')) return 0;
    return 1;
}

// ---------------------------------------------------------------- chemins

static int has_dotdot(const char* s) {
    for (const char* p = s; *p; p++) {
        if (p[0] == '.' && p[1] == '.') return 1;
    }
    return 0;
}

static int valid_src(const char* s) {
    return strncmp(s, RELEASE_PREFIX, strlen(RELEASE_PREFIX)) == 0 && !has_dotdot(s) && !strchr(s, ':') &&
           !strchr(s, '\\');
}

static int valid_dst(const char* s) {
    return _strnicmp(s, "reframework\\", 12) == 0 && !has_dotdot(s) && !strchr(s, ':') && !strchr(s, '/');
}

/// Crée les dossiers parents de `path`.
static void make_parents(const wchar_t* path) {
    wchar_t dir[MAX_PATH];
    wcscpy(dir, path);
    for (wchar_t* p = dir + wcslen(g_game); *p; p++) {
        if (*p == L'\\') {
            *p = 0;
            CreateDirectoryW(dir, NULL);
            *p = L'\\';
        }
    }
}

static int write_all(const wchar_t* path, const Buffer* b) {
    make_parents(path);
    HANDLE h = CreateFileW(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return 0;
    DWORD w = 0;
    int ok = WriteFile(h, b->data, (DWORD)b->size, &w, NULL) && w == b->size;
    ok = FlushFileBuffers(h) && ok;
    CloseHandle(h);
    return ok;
}

/// Remplace `dst` par `src` ; si `dst` est en cours d'utilisation (ce plugin lui-même), il est d'abord renommé en
/// .old (Windows permet de renommer une DLL chargée), supprimé au lancement suivant.
static int replace_file(const wchar_t* src, const wchar_t* dst) {
    if (MoveFileExW(src, dst, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) return 1;
    wchar_t old[MAX_PATH];
    swprintf(old, MAX_PATH, L"%ls.old", dst);
    DeleteFileW(old);
    if (!MoveFileExW(dst, old, MOVEFILE_REPLACE_EXISTING)) return 0;
    return MoveFileExW(src, dst, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}

// ---------------------------------------------------------------- mise à jour

typedef struct {
    char hash[65];
    char src[260];
    char dst[260];
    wchar_t path[MAX_PATH];
    wchar_t tmp[MAX_PATH];
} Entry;

static Entry g_files[MAX_FILES];

static void trim(char* s) {
    size_t n = strlen(s);
    while (n && (s[n - 1] == '\r' || s[n - 1] == '\n' || s[n - 1] == ' ' || s[n - 1] == '\t')) s[--n] = 0;
}

static void update(void) {
    // reste d'une mise à jour du plugin lui-même
    wchar_t self[MAX_PATH];
    game_path(self, "reframework\\plugins\\wilds_dps_meter_updater.dll.old");
    DeleteFileW(self);

    wchar_t path[MAX_PATH];
    Buffer b;
    game_path(path, "reframework\\autorun\\wilds_dps_meter\\version.txt");
    if (!read_file(path, &b)) { note("Wilds DPS Meter absent : rien à mettre à jour"); return; }
    char local[64] = {0};
    strncpy(local, b.data, sizeof(local) - 1);
    free(b.data);
    trim(local);

    game_path(path, "reframework\\data\\wilds_dps_meter_repo.txt");
    if (read_file(path, &b)) {
        trim(b.data);
        if (b.data[0]) {
            MultiByteToWideChar(CP_UTF8, 0, b.data, -1, g_local, MAX_PATH - 2);
            size_t n = wcslen(g_local);
            if (g_local[n - 1] != L'\\' && g_local[n - 1] != L'/') wcscat(g_local, L"\\");
            note("source : dossier local");
        }
        free(b.data);
    }
    if (!g_local[0] && !http_open()) { note("réseau indisponible"); return; }

    if (!fetch(MANIFEST, &b)) { note("version publiée illisible (pas de réseau ?)"); return; }
    char version[64] = {0};
    int count = 0, bad = 0;
    for (char* line = strtok(b.data, "\n"); line; line = strtok(NULL, "\n")) {
        trim(line);
        if (strncmp(line, "version ", 8) == 0) {
            strncpy(version, line + 8, sizeof(version) - 1);
        } else if (strncmp(line, "file ", 5) == 0 && count < MAX_FILES) {
            Entry* e = &g_files[count];
            if (sscanf(line + 5, "%64s %259s %259s", e->hash, e->src, e->dst) == 3 && strlen(e->hash) == 64 &&
                valid_src(e->src) && valid_dst(e->dst)) {
                count++;
            } else {
                bad = 1;
            }
        }
    }
    free(b.data);
    if (bad || !count || !valid_version(version)) { note("liste des fichiers invalide"); return; }
    if (version_cmp(version, local) <= 0) { note("à jour (v%s)", local); return; }

    // téléchargement de tout, vérifié, à côté des fichiers (.new)
    int ok = 1;
    for (int i = 0; i < count && ok; i++) {
        Entry* e = &g_files[i];
        game_path(e->path, e->dst);
        swprintf(e->tmp, MAX_PATH, L"%ls.new", e->path);
        char hex[65];
        if (!fetch(e->src, &b)) { note("téléchargement impossible : %s", e->src); ok = 0; break; }
        if (!sha256_hex(&b, hex) || _stricmp(hex, e->hash) != 0) {
            note("contenu inattendu (cache de GitHub pas encore à jour ?) : %s", e->src);
            ok = 0;
        } else if (!write_all(e->tmp, &b)) {
            note("écriture impossible : %s", e->dst);
            ok = 0;
        }
        free(b.data);
    }
    if (!ok) {
        for (int i = 0; i < count; i++) DeleteFileW(g_files[i].tmp);
        note("mise à jour v%s reportée au prochain lancement", version);
        return;
    }
    for (int i = 0; i < count; i++) {
        if (!replace_file(g_files[i].tmp, g_files[i].path)) {
            note("remplacement impossible : %s", g_files[i].dst);
            ok = 0;
        }
    }
    if (ok) note("mis à jour : v%s -> v%s", local, version);
    else note("mise à jour v%s incomplète, nouvel essai au prochain lancement", version);
}

static DWORD WINAPI update_thread(LPVOID param) {
    (void)param;
    wchar_t exe[MAX_PATH];
    DWORD n = GetModuleFileNameW(NULL, exe, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return 0;
    wchar_t* slash = wcsrchr(exe, L'\\');
    if (!slash) return 0;
    slash[1] = 0;
    wcscpy(g_game, exe);
    update();
    http_close();
    write_log();
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(inst);
        HANDLE t = CreateThread(NULL, 0, update_thread, NULL, 0, NULL);
        if (t) CloseHandle(t);
    }
    return TRUE;
}

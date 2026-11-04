// codex-sandbox: runs one program (Tectonic, for print jobs) locked down with Landlock and resource limits.
//
//   codex-sandbox [--rw DIR]... [--ro PATH]... [--exec PATH]... [--net-port N]...
//                 [--fsize BYTES] [--as BYTES] [--cpu SECONDS] [--nproc N] [--allow-unconfined-net] -- PROGRAM [ARGS...]
//
// After the limits are set the process can read only the --ro and --exec paths (and run only --exec ones), write only
// under --rw directories, and open TCP connections only to the --net-port ports. TCP needs Landlock ABI 4: on an older
// kernel it refuses to run (exit 125) unless --allow-unconfined-net says the caller accepts unrestricted TCP. Abstract unix sockets and signals to processes outside the sandbox are
// refused where the kernel supports scoping (ABI 6). It never gains privileges (no_new_privs). If the kernel has no
// Landlock at all it refuses to run (exit 125): the caller must not fall back to an unsandboxed compile.
// Built static (gcc -O2 -static) by src/Dockerfile.worker and the CI API-test job; the app finds it through CODEX_SANDBOX.

#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <linux/landlock.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <sys/resource.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <unistd.h>

#define FAIL 125
#define MAX_RULES 64

#ifndef LANDLOCK_ACCESS_FS_REFER
#define LANDLOCK_ACCESS_FS_REFER (1ULL << 13)
#endif
#ifndef LANDLOCK_ACCESS_FS_TRUNCATE
#define LANDLOCK_ACCESS_FS_TRUNCATE (1ULL << 14)
#endif
#ifndef LANDLOCK_ACCESS_FS_IOCTL_DEV
#define LANDLOCK_ACCESS_FS_IOCTL_DEV (1ULL << 15)
#endif

struct ruleset_attr {
    uint64_t handled_access_fs;
    uint64_t handled_access_net;
    uint64_t scoped;
};

struct net_port_attr {
    uint64_t allowed_access;
    uint64_t port;
};

#define NET_BIND_TCP (1ULL << 0)
#define NET_CONNECT_TCP (1ULL << 1)
#define RULE_NET_PORT 2
#define SCOPE_ABSTRACT_UNIX_SOCKET (1ULL << 0)
#define SCOPE_SIGNAL (1ULL << 1)

static const uint64_t READ_FILE = LANDLOCK_ACCESS_FS_READ_FILE;
static const uint64_t READ_DIR = LANDLOCK_ACCESS_FS_READ_DIR;
static const uint64_t EXECUTE = LANDLOCK_ACCESS_FS_EXECUTE;

static uint64_t fs_rights(int abi) {
    uint64_t all = LANDLOCK_ACCESS_FS_EXECUTE | LANDLOCK_ACCESS_FS_WRITE_FILE | LANDLOCK_ACCESS_FS_READ_FILE |
                   LANDLOCK_ACCESS_FS_READ_DIR | LANDLOCK_ACCESS_FS_REMOVE_DIR | LANDLOCK_ACCESS_FS_REMOVE_FILE |
                   LANDLOCK_ACCESS_FS_MAKE_CHAR | LANDLOCK_ACCESS_FS_MAKE_DIR | LANDLOCK_ACCESS_FS_MAKE_REG |
                   LANDLOCK_ACCESS_FS_MAKE_SOCK | LANDLOCK_ACCESS_FS_MAKE_FIFO | LANDLOCK_ACCESS_FS_MAKE_BLOCK |
                   LANDLOCK_ACCESS_FS_MAKE_SYM;
    if (abi >= 2) all |= LANDLOCK_ACCESS_FS_REFER;
    if (abi >= 3) all |= LANDLOCK_ACCESS_FS_TRUNCATE;
    if (abi >= 5) all |= LANDLOCK_ACCESS_FS_IOCTL_DEV;
    return all;
}

static int fail(const char *what) {
    fprintf(stderr, "codex-sandbox: %s: %s\n", what, strerror(errno));
    return FAIL;
}

static int set_limit(int resource, const char *value, const char *name) {
    if (value == NULL) return 0;
    char *end = NULL;
    unsigned long long n = strtoull(value, &end, 10);
    if (end == value || *end != '\0') {
        fprintf(stderr, "codex-sandbox: %s needs a number\n", name);
        return FAIL;
    }
    struct rlimit limit = {.rlim_cur = n, .rlim_max = n};
    return setrlimit(resource, &limit) == 0 ? 0 : fail(name);
}

// A path that does not exist is skipped (an optional certificate or font folder); any other error is fatal.
static int add_path(int ruleset, const char *path, uint64_t access, uint64_t handled) {
    int fd = open(path, O_PATH | O_CLOEXEC);
    if (fd < 0) return errno == ENOENT ? 0 : fail(path);
    struct stat st;
    if (fstat(fd, &st) != 0) {
        close(fd);
        return fail(path);
    }
    if (!S_ISDIR(st.st_mode)) {
        // Directory-only rights cannot be granted on a file.
        access &= LANDLOCK_ACCESS_FS_EXECUTE | LANDLOCK_ACCESS_FS_WRITE_FILE | LANDLOCK_ACCESS_FS_READ_FILE |
                  LANDLOCK_ACCESS_FS_TRUNCATE | LANDLOCK_ACCESS_FS_IOCTL_DEV;
    }
    struct landlock_path_beneath_attr rule = {.allowed_access = access & handled, .parent_fd = fd};
    long rc = syscall(SYS_landlock_add_rule, ruleset, LANDLOCK_RULE_PATH_BENEATH, &rule, 0);
    close(fd);
    return rc == 0 ? 0 : fail(path);
}

int main(int argc, char **argv) {
    const char *rw[MAX_RULES], *ro[MAX_RULES], *ex[MAX_RULES], *fsize = NULL, *as = NULL, *cpu = NULL, *nproc = NULL;
    unsigned long ports[MAX_RULES];
    int nrw = 0, nro = 0, nex = 0, nports = 0, unconfined_net = 0, i = 1;
    for (; i < argc; i++) {
        const char *flag = argv[i];
        if (strcmp(flag, "--") == 0) {
            i++;
            break;
        }
        if (strcmp(flag, "--allow-unconfined-net") == 0) {
            unconfined_net = 1;
            continue;
        }
        if (i + 1 >= argc) {
            fprintf(stderr, "codex-sandbox: %s needs a value\n", flag);
            return FAIL;
        }
        const char *value = argv[++i];
        if (strcmp(flag, "--rw") == 0 && nrw < MAX_RULES) rw[nrw++] = value;
        else if (strcmp(flag, "--ro") == 0 && nro < MAX_RULES) ro[nro++] = value;
        else if (strcmp(flag, "--exec") == 0 && nex < MAX_RULES) ex[nex++] = value;
        else if (strcmp(flag, "--net-port") == 0 && nports < MAX_RULES) ports[nports++] = strtoul(value, NULL, 10);
        else if (strcmp(flag, "--fsize") == 0) fsize = value;
        else if (strcmp(flag, "--as") == 0) as = value;
        else if (strcmp(flag, "--cpu") == 0) cpu = value;
        else if (strcmp(flag, "--nproc") == 0) nproc = value;
        else {
            fprintf(stderr, "codex-sandbox: unknown or repeated option %s\n", flag);
            return FAIL;
        }
    }
    if (i >= argc) {
        fprintf(stderr, "usage: codex-sandbox [options] -- PROGRAM [ARGS...]\n");
        return FAIL;
    }

    struct rlimit no_core = {0, 0};
    if (setrlimit(RLIMIT_CORE, &no_core) != 0) return fail("RLIMIT_CORE");
    if (set_limit(RLIMIT_FSIZE, fsize, "--fsize") || set_limit(RLIMIT_AS, as, "--as") ||
        set_limit(RLIMIT_CPU, cpu, "--cpu") || set_limit(RLIMIT_NPROC, nproc, "--nproc"))
        return FAIL;

    long abi = syscall(SYS_landlock_create_ruleset, NULL, 0, LANDLOCK_CREATE_RULESET_VERSION);
    if (abi < 1) {
        fprintf(stderr, "codex-sandbox: this kernel has no Landlock (%s); refusing to run unsandboxed\n", strerror(errno));
        return FAIL;
    }

    struct ruleset_attr attr = {.handled_access_fs = fs_rights((int)abi)};
    if (abi >= 4) attr.handled_access_net = NET_BIND_TCP | NET_CONNECT_TCP;
    if (abi >= 6) attr.scoped = SCOPE_ABSTRACT_UNIX_SOCKET | SCOPE_SIGNAL;
    size_t size = abi >= 6 ? sizeof attr : abi >= 4 ? 2 * sizeof(uint64_t) : sizeof(uint64_t);
    int ruleset = (int)syscall(SYS_landlock_create_ruleset, &attr, size, 0);
    if (ruleset < 0) return fail("landlock_create_ruleset");

    for (int k = 0; k < nrw; k++)
        if (add_path(ruleset, rw[k], attr.handled_access_fs & ~EXECUTE, attr.handled_access_fs)) return FAIL;
    for (int k = 0; k < nro; k++)
        if (add_path(ruleset, ro[k], READ_FILE | READ_DIR, attr.handled_access_fs)) return FAIL;
    for (int k = 0; k < nex; k++)
        if (add_path(ruleset, ex[k], READ_FILE | READ_DIR | EXECUTE, attr.handled_access_fs)) return FAIL;
    if (abi >= 4) {
        for (int k = 0; k < nports; k++) {
            struct net_port_attr rule = {.allowed_access = NET_CONNECT_TCP, .port = ports[k]};
            if (syscall(SYS_landlock_add_rule, ruleset, RULE_NET_PORT, &rule, 0) != 0) return fail("--net-port");
        }
    } else if (unconfined_net) {
        fprintf(stderr, "codex-sandbox: note: Landlock ABI %ld cannot restrict TCP; only files are confined\n", abi);
    } else {
        fprintf(stderr, "codex-sandbox: Landlock ABI %ld cannot restrict TCP (needs 4); refusing to run\n", abi);
        return FAIL;
    }

    if (prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0) return fail("no_new_privs");
    if (syscall(SYS_landlock_restrict_self, ruleset, 0) != 0) return fail("landlock_restrict_self");
    close(ruleset);

    execv(argv[i], &argv[i]);
    return fail(argv[i]);
}

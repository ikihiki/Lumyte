#ifndef LUMYTE_CEF_H
#define LUMYTE_CEF_H
#include <stddef.h>
#include <stdint.h>
#ifdef __cplusplus
extern "C"
{
#endif
    /* Selection probe ABI, not the product session ABI. No CEF pointers escape.
     * ABI 1, exact size/version required. All calls on initialization thread.
     * Status: 0 success, 1 invalid/unsupported, 2 busy/reentrant/live browsers,
     * 3 caught Rust panic, 4 no runtime on this thread. Fatal CEF failures can
     * terminate the process and are not recoverable status results.
     * Callbacks/context must survive until successful shutdown. They must not
     * throw/unwind; input bytes are callback-only borrowed storage.
     * Event 1: signed little-endian int64 delay milliseconds, can be any thread.
     * Event 2: created. Event 3: asynchronous before-close complete.
     * Event 4: complete top-down packed premultiplied BGRA8 sRGB 64x64 frame.
     * Event 5: synthetic contract frame [20 40 80 80], 1x1 (not CEF output).
     * No frame pointer survives the callback. Managed recipient must copy.
     * Reentrant runtime calls are rejected with status 2.
     */
    typedef void (*lumyte_cef_event)(uintptr_t, uint32_t, const uint8_t*, size_t, int32_t, int32_t);
    typedef struct
    {
        uint32_t size;
        uint32_t version;
        uintptr_t context;
        lumyte_cef_event event;
    } lumyte_cef_callbacks;
    uint32_t lumyte_cef_abi_version(void);
    int32_t lumyte_cef_contract(const lumyte_cef_callbacks*);
    int32_t lumyte_cef_initialize(const lumyte_cef_callbacks*);
    /* Subprocess returns CEF process exit code or -1 for browser process. */
    int32_t lumyte_cef_subprocess(void);
    /* One 64x64 browser for an operator-supplied trusted local HTTP page.
     * UTF8 + explicit length. Prefix check only; redirects/subresources are not
     * restricted to loopback. This is not a network security boundary. */
    int32_t lumyte_cef_create(const uint8_t* url, size_t length);
    int32_t lumyte_cef_pump(void);
    int32_t lumyte_cef_close(void);
    /* Refuses while before-close has not arrived. No callbacks after success. */
    int32_t lumyte_cef_shutdown(void);
#ifdef __cplusplus
}
#endif
#endif

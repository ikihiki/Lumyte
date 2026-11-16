# Backend-local Linux supervisor. No native code is loaded by the UI process.
# Own descendants through subreaper adoption, not global PID enumeration or killpg.
import ctypes
import os
import selectors
import shutil
import signal
import subprocess
import sys
import time


def children():
    with open('/proc/self/task/%d/children' % os.getpid()) as source:
        return [int(pid) for pid in source.read().split()]


def reap():
    while True:
        try:
            pid, _ = os.waitpid(-1, os.WNOHANG)
            if not pid:
                return
        except ChildProcessError:
            return


def signal_owned(sig):
    for pid in children():
        try:
            fd = os.pidfd_open(pid)
            try:
                # Recheck ownership after acquiring a stable identity. This child
                # cannot be reparented away while we live; pidfd prevents reuse.
                if pid in children():
                    signal.pidfd_send_signal(fd, sig)
            finally:
                os.close(fd)
        except ProcessLookupError:
            pass


def cleanup():
    started = time.monotonic()
    while True:
        reap()
        if not children():
            return
        signal_owned(signal.SIGTERM if time.monotonic() - started < .3 else signal.SIGKILL)
        # Killing an inner supervisor adopts its descendants, including nested
        # setsid children. Repeat until all owned descendants have been reaped.
        time.sleep(.01)


def main():
    owner = int(sys.argv[2])
    if os.getppid() != owner:
        return 125
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(36, 1, 0, 0, 0) != 0:
        raise OSError(ctypes.get_errno(), 'subreaper unavailable')
    # Require safe signaling before launching anything.
    if not hasattr(os, 'pidfd_open') or not hasattr(signal, 'pidfd_send_signal'):
        raise RuntimeError('pidfd unavailable')
    fd = os.pidfd_open(os.getpid())
    os.close(fd)
    stopping = False

    def stop(signum, frame):
        nonlocal stopping
        stopping = True

    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    child = None
    try:
        child = subprocess.Popen(sys.argv[3:], stdin=subprocess.PIPE,
                                 stdout=subprocess.PIPE, start_new_session=True, bufsize=0)
        selector = selectors.DefaultSelector()
        # Bounded relay buffers maintain protocol byte transparency/backpressure.
        incoming = bytearray()
        outgoing = bytearray()
        streams = (0, 1, child.stdin.fileno(), child.stdout.fileno())
        for stream in streams:
            os.set_blocking(stream, False)
        while not stopping and not os.path.exists(sys.argv[1]) and os.getppid() == owner and child.poll() is None:
            interests = {}
            if len(incoming) < 65536:
                interests[0] = selectors.EVENT_READ
            if incoming:
                interests[child.stdin.fileno()] = selectors.EVENT_WRITE
            if len(outgoing) < 65536:
                interests[child.stdout.fileno()] = selectors.EVENT_READ
            if outgoing:
                interests[1] = selectors.EVENT_WRITE
            for key in list(selector.get_map().values()):
                selector.unregister(key.fd)
            for fd, event in interests.items():
                selector.register(fd, event)
            for key, event in selector.select(.02):
                fd = key.fd
                if event == selectors.EVENT_READ:
                    data = os.read(fd, 65536 - len(incoming if fd == 0 else outgoing))
                    if not data:
                        stopping = True
                        break
                    (incoming if fd == 0 else outgoing).extend(data)
                else:
                    buffer = outgoing if fd == 1 else incoming
                    count = os.write(fd, buffer)
                    del buffer[:count]
        code = child.poll()
        # On helper exit flush at most one bounded tail, without blocking cleanup.
        if code is not None:
            try:
                outgoing.extend(os.read(child.stdout.fileno(), 65536))
            except BlockingIOError:
                pass
            try:
                if outgoing:
                    os.write(1, outgoing)
            except (BlockingIOError, BrokenPipeError):
                pass
        return code if code is not None and code >= 0 else 1
    finally:
        cleanup()
        shutil.rmtree(os.path.dirname(sys.argv[1]), ignore_errors=True)


try:
    sys.exit(main())
except (BrokenPipeError, OSError, RuntimeError):
    sys.exit(125)

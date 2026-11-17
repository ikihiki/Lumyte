#!/bin/bash
# Opt-in Linux desktop launcher; stdout is exclusively the helper protocol.
set -euo pipefail
umask 077
fail() { printf 'cef-isolated: %s\n' "$1" >&2; exit 78; }
self=$(readlink -f -- "${BASH_SOURCE[0]}")
here=${self%/*}
if [[ ${1:-} == --private-session ]]; then
    shift
    [[ ${DBUS_SESSION_BUS_ADDRESS:-} == unix:* && ${LUMYTE_PRIVATE_BUS:-} == 1 ]] || fail 'private bus unavailable'
    unset LUMYTE_PRIVATE_BUS
    # No activation fallback: acquire the backend/store names before frontend.
    SECONDS=0
    wait_owner() {
        local i result
        for ((i=0; i<50 && SECONDS<5; i++)); do
            result=$(timeout 0.2s gdbus call --address "$DBUS_SESSION_BUS_ADDRESS" --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus --method org.freedesktop.DBus.NameHasOwner "$1" 2>/dev/null) || result=
            [[ $result == '(true,)' ]] && return 0
            sleep 0.05
        done
        return 1
    }
    not_ready() { printf 'cef-isolated: private portals not ready\n' >&8; exit 78; }
    env -u LD_LIBRARY_PATH /usr/libexec/xdg-permission-store > "$TMPDIR/permission-store.log" 2>&1 0<&- 8>&- 9>&- &
    env -u LD_LIBRARY_PATH GIO_USE_VFS=local NO_AT_BRIDGE=1 /usr/libexec/xdg-desktop-portal-gtk > "$TMPDIR/portal-gtk.log" 2>&1 0<&- 8>&- 9>&- &
    wait_owner org.freedesktop.impl.portal.PermissionStore || not_ready
    wait_owner org.freedesktop.impl.portal.desktop.gtk || not_ready
    env -u LD_LIBRARY_PATH GIO_USE_VFS=local NO_AT_BRIDGE=1 /usr/libexec/xdg-desktop-portal > "$TMPDIR/portal.log" 2>&1 0<&- 8>&- 9>&- &
    wait_owner org.freedesktop.portal.Desktop || not_ready
    wait_owner org.freedesktop.impl.portal.desktop.gtk || not_ready
    printf 'cef-isolated: private portals ready\n' >&8
    # current_exe() stays the real ELF; --type children do not re-enter us.
    "$here/lumyte-cef-helper" "$@" <&0 >&9 2>&8 9>&- 8>&- &
    helper=$!
    exec 0<&- 8>&- 9>&-
    set +e
    wait "$helper"
    exit $?
fi
[[ ($# == 1 && $1 == --lumyte-helper) || ($# == 2 && $1 == --lumyte-helper && $2 == --lumyte-allow-unsandboxed) ]] || fail 'expected --lumyte-helper [--lumyte-allow-unsandboxed]'
[[ -n ${DISPLAY:-} || -n ${WAYLAND_DISPLAY:-} ]] || fail 'desktop display unavailable'
for dep in dbus-daemon gdbus timeout stat readlink mkdir; do
    command -v "$dep" >/dev/null || fail "missing dependency: $dep"
done
[[ -x /usr/libexec/xdg-desktop-portal && -x /usr/libexec/xdg-desktop-portal-gtk && -x /usr/libexec/xdg-permission-store ]] || fail 'missing distro portal frontend/GTK/permission store'
[[ -x $here/lumyte-cef-helper ]] || fail 'sibling native helper unavailable'
root=$(readlink -f -- "${TMPDIR:-/nonexistent}")
[[ -d $root && ${root##*/} == lumyte-browser-* && $(stat -c %u -- "$root") == "$EUID" && $(stat -c %a -- "$root") == 700 ]] || fail 'private root required'
for value in "${HOME:-}" "${XDG_CONFIG_HOME:-}" "${XDG_CACHE_HOME:-}" "${XDG_DATA_HOME:-}"; do
    [[ $value == "$root" ]] || fail 'private HOME/XDG root required'
done
if [[ -n ${WAYLAND_DISPLAY:-} && $WAYLAND_DISPLAY != /* ]]; then
    [[ -n ${XDG_RUNTIME_DIR:-} ]] || fail 'Wayland runtime unavailable'
    export WAYLAND_DISPLAY="$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY"
fi
if [[ -n ${WAYLAND_DISPLAY:-} ]]; then
    [[ -S $WAYLAND_DISPLAY ]] || fail 'Wayland display socket unavailable'
fi
mkdir -m 700 -- "$root/portal-runtime" "$root/xdg-desktop-portal" "$root/portal-state"
export XDG_RUNTIME_DIR="$root/portal-runtime" XDG_STATE_HOME="$root/portal-state" XDG_CURRENT_DESKTOP=GNOME
export XDG_DATA_DIRS=/usr/local/share:/usr/share XDG_CONFIG_DIRS=/etc/xdg
printf '[preferred]\ndefault=gtk\n' > "$root/xdg-desktop-portal/gnome-portals.conf"
# This fallback exposes no file/document portals to the app. No auto-activated
# desktop daemons or FUSE mounts can outlive an untrappable process-tree kill.
# Only explicitly launched frontend, GTK and permission store use this bus.
unset DBUS_SESSION_BUS_ADDRESS DBUS_SESSION_BUS_PID DBUS_SESSION_BUS_WINDOWID DBUS_STARTER_ADDRESS DBUS_STARTER_BUS_TYPE
export LUMYTE_PRIVATE_BUS=1
# Linux subreaper retains descendant ownership across helper exit and nested
# sessions. Only rechecked owned pidfds are signalled, never saved numeric groups.
[[ -x $here/lumyte-cef-supervisor ]] || fail 'sibling native supervisor unavailable'
"$here/lumyte-cef-supervisor" --portal-session --owner "$$" --root "$root" --stop "$root/isolated-stop" -- "$self" "$@" <&0 8>&2 9>&1 &
supervisor=$!
exec 0<&- 1>&-
cleanup_launcher() {
    trap - EXIT TERM INT HUP
    if [[ -n ${supervisor:-} ]]; then
        : > "$root/isolated-stop"
        wait "$supervisor" 2>/dev/null || true
    fi
}
trap cleanup_launcher EXIT
trap 'exit 143' TERM INT HUP
set +e
wait "$supervisor"
result=$?
supervisor=
exit "$result"

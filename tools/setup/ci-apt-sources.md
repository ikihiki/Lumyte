# CI Ubuntu archive acquisition

The privileged setup-os.sh path on GitHub Actions Ubuntu x64 can select private
APT source copies. Only exact HTTP/HTTPS regional
region-name.ec2.archive.ubuntu.com/ubuntu[/] URI tokens change to the official
`https://archive.ubuntu.com/ubuntu[/]`. This avoids the observed slow regional
archive route, not a measured throughput guarantee or retry loop.

Native apt-config shell /f and /d resolve effective source paths under the same
privileged/proxy environment as apt-get. Both update and install receive the same
two Dir::Etc source-selection options. No global APT source is written. Temporary
directories are private and removed on exit; active .list and deb822 .sources
files preserve names/order and bytes except those URI tokens. Signed-By/inline
keys, suites, components, architectures/options, other repositories, pins, hooks,
TLS and proxies remain native APT policy. The exact original package list (including
fonts-noto-cjk), install flags and APT::Update::Error-Mode=any stay unchanged.

Local/Debian/arm64/rootless paths are unchanged. Security, ports, Azure, canonical,
credentialed/query/port-qualified and unknown URIs are not rewritten. Disabled
deb822 stanzas stay unchanged. Without a recognized change, APT receives its
original arguments without source overrides. Missing main sources.list is normal
on deb822 Ubuntu; no active files, read errors, malformed supported deb822 fields,
or binary-specific/chroot source roots fail clearly instead of dropping repos.
Native preference paths are queried too. Hostname-based Pin: origin rules must
match both old and canonical hosts equivalently; otherwise acquisition is refused
before update/install rather than rewriting priorities or changing selected versions.
Release-based pins remain unchanged. Folded Pin fields and unsupported origin-pin
expressions fail closed before APT; no general preference parser is substituted.
Helper diagnostics do not print source text, effective config or proxy values;
APT keeps its ordinary native output.

This is narrow token substitution, not a general APT parser, trust/keyring/version
replacement, font removal, rootless Mesa/sysroot ABI change, new dependency cache,
CI timeout/check change or favorable workflow rerun. Mirror replication can differ;
resolver/version policy is preserved, not guaranteed identical package availability.
Live hosted network performance is unmeasured until genuine changed-source CI.

References: native apt-config(8) shell /f and /d; apt.conf(5) Dir::Etc and normal
configuration loading; sources.list(5) legacy/deb822 URI fields, active filenames,
disabled stanzas and Signed-By.

Offline verification (no system apt/sudo/root/network):

    python3 tools/setup/test-ci-apt-sources.py
    bash -n tools/setup/setup-os.sh

Actual fixture copies of setup use fake uname/OS-release/apt-config/apt-get/sudo.
LUMYTE_TEST_BASELINE=1 selects historical969a setup for two fail-first regional
source contracts. Tests read/write private fixtures, never live host config.

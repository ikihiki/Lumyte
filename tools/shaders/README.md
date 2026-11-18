# Slang compiler incremental inputs

SlangCompilerPath remains the exact command/path passed to the existing managed
shader tool. The default is still slangc; invocation, working directory, arguments,
backend selection, shader math and artifact packing are unchanged.

MSBuild filesystem inputs are different from executable lookup. A bare slangc
input is not a PATH search, so a missing relative file made two otherwise identical
builds execute the compiler twice. The Linux default-PATH fixture reproduces this
against the original target with a fake compiler, then verifies one invocation.

The preparation target resolves filesystem dependencies only. For the actually
qualified .NET10.0.14 child host, unrooted ProcessStartInfo lookup checks the host
executable directory first, then the child project working directory, then
nonempty PATH entries. The task must not equate its DLL base or process with an
arbitrary child host. An executable in CWD can shadow PATH even when the parent
shell started outside the project. A regression reproduces b09 tracking PATH A
while the child executed CWD B; modifying only B incorrectly skipped compilation.

Cache qualification requires a shell-resolved child dotnet host equal to the
actual task process executable, the inspected10.0.14 runtime with a single10.x
framework installation, and the exact SDK-generated tool runtime configuration.
Wrappers, copied/different hosts, changed runtime/startup policies, unavailable
metadata and other runtime versions/formats conservatively rebuild. This is a
qualification restriction, not a runtime version requirement or changed launch
policy. The one-invocation incremental fixtures require this qualified host
prerequisite; additional installed10.x runtimes legitimately cause conservative
rebuilds and do not qualify as a warm-skip fixture environment. Public BCL metadata APIs are accessed compatibly with the inline task's
netstandard references; private runtime/MSBuild lookup methods are not invoked.

Host-directory/CWD candidates retain File.Exists precedence: an inaccessible
shadow cannot be skipped in favor of PATH. PATH non-executable placeholders are
skipped; partial permission/ownership/ACL shapes are unqualified rather than
approximated. Normal all-execute PATH files are checked for effective access.
Absolute compiler paths remain direct; relative paths, spaces and Unicode are
covered. The resolved executable and write-only-when-different path/mtime/length
identity stamp remain dependencies, including older replacements. This uses
ordinary timestamp semantics, not a full content-hash cache. No raw PATH is stored
or logged.

Unknown identities use an intentionally absent input, so they rebuild and retain
original execution/failure, never guessed-cache success. Isolated tests copy a
muxer and link its read-only framework directories to prove executable-directory
precedence without changing any installed host. The different host is deliberately
unqualified and rebuilds even on identical builds; an opaque wrapper is covered
too. Windows bare commands retain original CreateProcess/PATHEXT execution and
conservative rebuilds; no Windows runtime/optimization claim. NoBuild/pack still
does not resolve, probe, write an identity stamp or execute the compiler.

The original shader, shared include, imported-project/tool runtime dependencies,
resource/backend/RID output layout and command arguments are retained. Tests cover
source/include/project argument changes, compiler changes and PATH identity changes,
unchanged absolute compiler controls, missing compilers, alternate layouts, runtime
DLL changes and NoBuild packaging. All compiler execution in the new qualification
is fake CPU fixture work; no real WGSL/SPIR-V/DXIL output or GPU comparison is implied.

Hosted build-time improvement and whether actual CI repeated the same evaluated
output remain unmeasured. Other RID/configuration/SDK publish passes can be valid.
Keep all real build/verifier passes and CI deadlines; real compiler cold/warm
artifact comparison requires separate authorization.

## Shell identity boundary

The unchanged Exec uses quoted shell arguments. On Linux, any backslash-bearing
compiler or host string is therefore unqualified for caching: task/file identity
is not assumed to equal the post-MSBuild/post-shell identity. Original/raw property
Contains checks travel as boolean metadata, because normalized task parameters
alone may already have lost those characters. This does not reject execution or
escape/rewrite user commands; it forces the existing conservative rebuild path.
Windows path separators are excluded from this Linux-only decision.

The related boundary audit retains existing rejection of dollar expansion,
backticks, embedded double quotes and CR/LF; compiler semicolons remain unqualified
as MSBuild list boundaries. Glob characters, apostrophes and ordinary whitespace
inside the existing double quotes are not newly interpreted or rewritten.

Fake fixtures include literal two-backslash and one-backslash filenames plus an
executable normalized directory alias, and observe the actual callee with separate
ledgers. This SDK normalizes command paths, so the review's literal compiler-only
stale case was not demonstrated: that control rebuilt after the change. The host
argument control did fail before repair (one compile instead of two after only the
selected host changed). It now conservatively rebuilds, with all ordinary
incremental controls retained. Neither result is a real shader timing claim.

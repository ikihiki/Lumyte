# Slang compiler incremental inputs

SlangCompilerPath remains the exact command/path passed to the existing managed
shader tool. The default is still slangc; invocation, working directory, arguments,
backend selection, shader math and artifact packing are unchanged.

MSBuild filesystem inputs are different from executable lookup. A bare slangc
input is not a PATH search, so a missing relative file made two otherwise identical
builds execute the compiler twice. The Linux default-PATH fixture reproduces this
against the original target with a fake compiler, then verifies one invocation.

The preparation target resolves a filesystem dependency without executing or
probing the compiler. Linux lookup follows PATH order and effective execute access,
including a non-executable placeholder before a valid tool. Absolute/relative
paths, spaces and Unicode are covered. The resolved input remains a dependency;
a write-only-when-different identity stamp also tracks selected path, timestamp and
length, so selecting an older compiler or replacing one invalidates output.
This retains ordinary MSBuild timestamp semantics, not a full content-hash cache.
No entire PATH value is stored or logged.

Unresolved/missing/inaccessible commands use an intentionally absent input and
therefore rebuild instead of accepting stale output. The original invocation then
succeeds or fails normally. Windows bare-command CreateProcess/PATHEXT selection
and other unsupported platform lookups are not emulated: they retain original
execution with conservative rebuilds. Windows explicit file paths are tracked,
but Windows execution was not qualified by the Linux-only fake tests. No Windows
incrementality or mock-as-runtime claim is made. NoBuild/pack does not resolve,
probe or execute the compiler and keeps its existing embedded-assembly contract.

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

# Native Cargo compilation cache

The required workflow restores standard Cargo compilation state after pinned
tool bootstrap and checkpoints it after successful compilation. The existing
Compile command and subsequent verification/test/package gates remain unchanged.
The reusable action can be composed as follows:

~~~yaml
      # Existing Mise setup has completed; Windows build runner is built too.
      - name: Restore native Cargo compilation
        uses: ./.github/actions/cache-native

      # KEEP the existing Build solution (Linux/Windows) step here, unchanged.
      # Compile must run unconditionally, including every native build,
      # provenance/header/license verification and staging operation.

      - name: Save verified native Cargo compilation
        uses: ./.github/actions/cache-native
        with:
          mode: save
      # Existing integration/GPU/windowing/distribution gates follow unchanged.
~~~

For GPU/format jobs whose setup and Compile currently share a step, split only at
the setup/Compile boundary to insert restore. Do not add cache-hit conditions.
CPU jobs that compile as part of Test may restore before Test and save after Test.

## Contract

- The action is enabled on Linux and Windows, x64 and ARM64. It provisions the
  pinned Rust toolchain using the existing Setup target before fingerprinting.
- Only standard Cargo target directories are restored. Staged native libraries,
  generated provenance, package assets and license/notices are never restored.
- Exact keys include OS/architecture, runner image revision, native source,
  locks, pin metadata, headers, patches, build/setup scripts and Cargo config;
  actual installed rustc/cargo executable hashes and verbose versions; C/linker
  identities and compile flags; Linux installed package revisions or Windows VS
  compiler hash and SDK versions/include/library environment.
- Keys intentionally exclude tokens and volatile CI job/run metadata. This is
  **standard Cargo compilation caching**, not a claim of complete environment or
  whole-SDK hashing. Normal Cargo builds remain responsible for compiler, source,
  dependency and build-script fingerprints (including rerun-if-env-changed).
  Source unpack/header probes and all normal validation still execute. Newly
  unpacked source mtimes may conservatively trigger recompilation; no timestamps
  are forged to force a hit. There is no timestamp-only native skip.
- The action activates the same Linux/Windows build environment as Compile.
  Build-specific Rust/libclang overrides continue to be applied by the existing
  build targets and scripts, and their definitions are source-keyed. Custom
  untracked SDK mutations on self-hosted runners are outside this hosted-runner
  contract; bump the key or clear caches after such mutations.
- Save uses the exact key captured before Compile, not a recomputed post-build
  environment. It runs only after a successful preceding Compile. No automatic
  post-job save can publish results from a failed compile. Exact hits are
  immutable; there are no broad fallback keys and no cross-OS cache reuse.

Local checks:

~~~sh
python3 -m unittest discover -s native -p test_cargo_cache.py -v
CARGO_HOME="$PWD/artifacts/build/tools/cargo" \
RUSTUP_HOME="$PWD/artifacts/build/tools/rustup" python3 native/cargo_cache.py
~~~

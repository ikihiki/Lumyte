# Repository instructions

## Tests

- Use xUnit for automated tests.
- Keep each test project next to its production project as `Lumyte.<Area>.Tests`.
- Name test classes `<Subject>Tests` and test methods as concise English behavior sentences in PascalCase, such as `FactoryAppliesNamedParameters`. Do not use underscore-separated `Subject_Condition_Result` names.
- Test one observable behavior at a time. Prefer public behavior over private implementation details.
- Avoid change-detector tests that merely duplicate generated output:
  - do not compare an entire HTML page, generated source file, serialized document, or other large textual artifact when only a few behaviors matter;
  - for web UI, assert semantic structure and user-visible behavior through parsed DOM or browser locators such as role, accessible name, relevant text, state, navigation, and interaction;
  - do not couple tests to irrelevant whitespace, attribute order, CSS class order, generated identifiers, or unrelated page content;
  - use a full snapshot or golden output only when the complete artifact is an intentional stable contract, and keep that approval surface as small and reviewable as possible.
- Structure non-trivial tests as Arrange, Act, and Assert sections separated by blank lines. Add `// Arrange`, `// Act`, and `// Assert` comments only when the phases are not otherwise clear.
- Multiple assertions are allowed when they describe one behavior. Split independent outcomes into separate tests.
- Avoid assertion roulette:
  - compare a meaningful expected value object when several fields form one result;
  - use `Assert.Collection` for ordered collections instead of repeating assertions in a loop;
  - use a `[Theory]` for repeated input/output cases;
  - extract complex repeated checks into narrowly named assertion helpers;
  - make every failure identify the property, item, or invariant that broke.
- Use the most specific assertion available. For exceptions, verify the relevant parameter name, message fragment, or resulting state when it is part of the contract.
- Keep tests deterministic and parallel-safe. Do not depend on test order, shared mutable state, wall-clock timing, sleeps, the network, or machine-specific state.
- Use `ManualClock`, fixed random seeds, and in-memory fakes where appropriate. Prefer small fakes over mocking frameworks.
- Add a regression test for every bug fix when the failure can be reproduced automatically.
- Use source-generator consumer tests that compile and execute the generated API. Do not make generated source text snapshots the primary behavioral test.
- Keep integration and conformance tests separate from fast unit tests. Mark or isolate tests that require external hardware, processes, or platform facilities.
- Do not skip or weaken a failing test without documenting the concrete reason.
- Before completing a code change, run `dotnet test Lumyte.slnx` and report any tests that could not run.

#pragma warning disable SA1402 // Closely related execution adapters share this linked source.
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Lumyte.Testing;

// xUnit v3 4.0.3 now provides parallelMode=all, but upgrading removes the v2
// CollectionBehavior.DisableTestParallelization API used by existing GPU assemblies.
// This small v2 adapter avoids a repository-wide framework/API migration.

/// <summary>Opt-in, bounded method scheduling while retaining xUnit's test lifecycle.</summary>
/// <param name="messageSink">The diagnostic sink.</param>
public sealed class CpuMethodTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    /// <inheritdoc/>
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        => new CpuMethodExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
}

internal sealed class CpuMethodExecutor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
    : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
{
    protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
    {
        IXunitTestCase[] cases = testCases.ToArray();
        string? configured = Environment.GetEnvironmentVariable("LUMYTE_CPU_METHOD_PARALLELISM");
        bool enabled = int.TryParse(configured, out int concurrency) && concurrency > 0;
        bool assemblySerial = TestAssembly.Assembly.GetCustomAttributes(typeof(CollectionBehaviorAttribute))
            .Any(attribute => attribute.GetNamedArgument<bool>(nameof(CollectionBehaviorAttribute.DisableTestParallelization)));
        bool filteredCpuSafe = TestAssembly.Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute))
            .OfType<IReflectionAttributeInfo>().Any(attribute => attribute.Attribute is AssemblyMetadataAttribute { Key: "Lumyte.CpuMethodParallelism", Value: "true" });
        bool excluded = Environment.GetEnvironmentVariable("LUMYTE_TEST_SERIAL") == "1"
            || cases.Any(test => test.SkipReason == null && (test.Traits.Any(trait => trait.Value.Any(value => value.Equals("GPU", StringComparison.OrdinalIgnoreCase) || value.Equals("Integration", StringComparison.OrdinalIgnoreCase)))
                || test.TestMethod.Method.GetCustomAttributes(typeof(FactAttribute)).Any(attribute => attribute is IReflectionAttributeInfo reflection && reflection.Attribute.GetType().Name.Contains("Gpu", StringComparison.OrdinalIgnoreCase))));
        if (excluded)
        {
            executionOptions.SetValue("xunit.execution.DisableParallelization", true);
        }

        if (!enabled || (assemblySerial && !filteredCpuSafe) || excluded || executionOptions.DisableParallelizationOrDefault())
        {
            base.RunTestCases(cases, executionMessageSink, executionOptions);
            return;
        }

        if (assemblySerial && filteredCpuSafe)
        {
            // Only explicitly reviewed assemblies can narrow their native-only blanket
            // serialization for a filtered CPU run; explicit collections stay serial.
            executionOptions.SetValue("xunit.execution.DisableParallelization", false);
        }

        // Also bound xUnit's collection scheduler; the separate method gate is shared
        // by every parallel collection, including methods suspended at an await.
        executionOptions.SetValue("xunit.execution.MaxParallelThreads", concurrency);
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        using var runner = new CpuMethodAssemblyRunner(TestAssembly, cases, DiagnosticMessageSink, executionMessageSink, executionOptions, gate);
        await runner.RunAsync();
    }
}

internal sealed class CpuMethodAssemblyRunner(ITestAssembly assembly, IEnumerable<IXunitTestCase> cases, IMessageSink diagnosticSink, IMessageSink executionSink, ITestFrameworkExecutionOptions options, SemaphoreSlim gate)
    : XunitTestAssemblyRunner(assembly, cases, diagnosticSink, executionSink, options)
{
    protected override Task<RunSummary> RunTestCollectionAsync(IMessageBus messageBus, ITestCollection collection, IEnumerable<IXunitTestCase> testCases, CancellationTokenSource cancellationTokenSource)
        => new CpuMethodCollectionRunner(collection, testCases, DiagnosticMessageSink, messageBus, TestCaseOrderer, new ExceptionAggregator(Aggregator), cancellationTokenSource, gate).RunAsync();
}

internal sealed class CpuMethodCollectionRunner(ITestCollection collection, IEnumerable<IXunitTestCase> cases, IMessageSink diagnosticSink, IMessageBus messageBus, ITestCaseOrderer orderer, ExceptionAggregator aggregator, CancellationTokenSource cancellation, SemaphoreSlim gate)
    : XunitTestCollectionRunner(collection, cases, diagnosticSink, messageBus, orderer, aggregator, cancellation)
{
    protected override Task<RunSummary> RunTestClassAsync(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> testCases)
    {
        // Explicit collections are a user-declared serialization boundary (and may
        // share mutable fixtures). Keep all of their original v2 semantics.
        bool collectionSerial = TestCollection.CollectionDefinition?.GetCustomAttributes(typeof(CollectionDefinitionAttribute))
            .Any(attribute => attribute.GetNamedArgument<bool>(nameof(CollectionDefinitionAttribute.DisableParallelization))) == true;
        bool parallel = !collectionSerial && !@class.GetCustomAttributes(typeof(CollectionAttribute)).Any();

        return new CpuMethodClassRunner(testClass, @class, testCases, DiagnosticMessageSink, MessageBus, TestCaseOrderer, new ExceptionAggregator(Aggregator), CancellationTokenSource, CollectionFixtureMappings, gate, parallel).RunAsync();
    }
}

internal sealed class CpuMethodClassRunner(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> cases, IMessageSink diagnosticSink, IMessageBus messageBus, ITestCaseOrderer orderer, ExceptionAggregator aggregator, CancellationTokenSource cancellation, IDictionary<Type, object> collectionFixtures, SemaphoreSlim gate, bool parallel)
    : XunitTestClassRunner(testClass, @class, cases, diagnosticSink, messageBus, orderer, aggregator, cancellation, collectionFixtures)
{
    protected override async Task<RunSummary> RunTestMethodsAsync()
    {
        // Fixture initialization has already completed in the base class. Each method
        // still uses the stock method/case runner, including sequential theory rows,
        // async test instances, output helpers, failure aggregation and disposal.
        if (!parallel)
        {
            return await base.RunTestMethodsAsync();
        }

        IXunitTestCase[] orderedCases;
        try
        {
            orderedCases = TestCaseOrderer.OrderTestCases(TestCases).ToArray();
        }
        catch (Exception exception)
        {
            MessageBus.QueueMessage(new ErrorMessage(TestCases, exception));
            return new RunSummary();
        }

        object[] arguments = CreateTestClassConstructorArguments();
        Task<RunSummary>[] tasks = orderedCases
            .GroupBy(test => test.TestMethod, TestMethodComparer.Instance)
            .Select(method => Task.Run(() => RunTestMethodAsync(method.Key, (IReflectionMethodInfo)method.Key.Method, method, arguments)))
            .ToArray();
        RunSummary[] results = await Task.WhenAll(tasks);
        var summary = new RunSummary();
        foreach (RunSummary result in results)
        {
            summary.Aggregate(result);
        }

        return summary;
    }

    protected override async Task<RunSummary> RunTestMethodAsync(ITestMethod testMethod, IReflectionMethodInfo method, IEnumerable<IXunitTestCase> testCases, object[] constructorArguments)
    {
        // xUnit mutates the constructor array to bind/unbind each output helper.
        // Clone it per method so concurrent tests never borrow another test's output.
        // Waiting without a cancellation token lets every queued task drain cleanly
        // when xUnit requests cancellation, rather than faulting the async executor.
        await gate.WaitAsync();
        try
        {
            return CancellationTokenSource.IsCancellationRequested
                ? new RunSummary()
                : await base.RunTestMethodAsync(testMethod, method, testCases, (object[])constructorArguments.Clone());
        }
        finally
        {
            gate.Release();
        }
    }
}

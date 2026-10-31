using Xunit;

namespace Lumyte.Composition.Tests;

/// <summary>Checks callback factories generated in real compilations, including inheritance and closed generic delegates.</summary>
public sealed class CallbackTests
{
    /// <summary>Checks callback omission and explicit null clear declaration defaults while ordinary Optional settings retain theirs.</summary>
    [Fact]
    public void CallbackDefaultsAreNullWithoutChangingOrdinaryOptionalParameters()
    {
        TestKit.Definitions.CallbackItem<int> omitted = TestKit.CallbackItem<int>();
        Assert.Null(omitted.OnInherited);
        Assert.Null(omitted.OnValue);
        Assert.Null(omitted.OnResult);
        Assert.Null(omitted.OnCustom);
        Assert.Null(omitted.Handler);
        Assert.Equal("kept", omitted.Label);
        Assert.Equal(5, omitted.Count);
        TestKit.Definitions.CallbackItem<int> cleared = TestKit.CallbackItem<int>(onValue: null, onResult: null, onInherited: null, handler: null, label: null);
        Assert.Null(cleared.OnInherited);
        Assert.Null(cleared.OnValue);
        Assert.Null(cleared.OnResult);
        Assert.Null(cleared.Handler);
        Assert.Null(cleared.Label);
    }

    /// <summary>Checks lambdas, method groups, variables, init-only inheritance and with ordering without invoking callbacks at construction.</summary>
    [Fact]
    public void DirectCallbackArgumentsAreAssignedBeforeWithWithoutRunningThem()
    {
        int calls = 0;
        Action inherited = () => calls++;
        TestKit.Definitions.CallbackResult<int> custom = value => value + 3;
        TestKit.Definitions.CallbackItem<int> item = TestKit.CallbackItem<int>(
            onValue: value => calls += value,
            onResult: Result,
            onInherited: inherited,
            onCustom: custom,
            count: 7,
            with:
            [
                target =>
                {
                    Assert.Equal(0, calls);
                    Assert.Same(inherited, target.OnInherited);
                    Assert.Same(custom, target.OnCustom);
                    Assert.NotNull(target.OnValue);
                    Assert.NotNull(target.OnResult);
                    Assert.Equal(7, target.Count);
                },
            ]);
        Assert.Equal(0, calls);
        item.OnValue!(2);
        item.OnInherited!();
        Assert.Equal(3, calls);
        Assert.Equal(8, item.OnResult!(4));
        Assert.Equal(7, item.OnCustom!(4));
        Assert.Same(TestKit.CallbackItemFactory<int>(), TestKit.CallbackItemFactory<int>());
    }

    /// <summary>Checks non-nullable delegates and required nullable delegates become mandatory direct arguments.</summary>
    [Fact]
    public void RequiredDelegatesArePlacedBeforeOptionalArguments()
    {
        int calls = 0;
        TestKit.Definitions.RequiredCallbacks item = TestKit.RequiredCallbacks(onRequired: null, zRequired: () => calls++, aOptional: () => calls += 2);
        Assert.Null(item.OnRequired);
        Assert.Equal(0, calls);
        item.ZRequired();
        item.AOptional!();
        Assert.Equal(3, calls);
        System.Reflection.ParameterInfo[] parameters = typeof(TestKit.Definitions.RequiredCallbacksFactory).GetMethod("Invoke")!.GetParameters();
        Assert.False(parameters.Single(parameter => parameter.Name == "zRequired").IsOptional);
        Assert.False(parameters.Single(parameter => parameter.Name == "onRequired").IsOptional);
        Assert.True(parameters.Single(parameter => parameter.Name == "aOptional").IsOptional);
    }

    private static int Result(int value) => value * 2;
}

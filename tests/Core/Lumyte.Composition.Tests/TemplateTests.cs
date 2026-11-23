using Xunit;

namespace Lumyte.Composition.Tests;

/// <summary>Checks typed template evaluation and deferred fixed-composition assignments.</summary>
public sealed class TemplateTests
{
    /// <summary>Checks 1-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity1RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, object> template = (arg1) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, object>)first.Value!)(1));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, object>)second.Value!)(-1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 2-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity2RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, object> template = (arg1, arg2) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, object>)first.Value!)(1, 2));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, object>)second.Value!)(-1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 3-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity3RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, object> template = (arg1, arg2, arg3) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, object>)first.Value!)(1, 2, 3));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, object>)second.Value!)(-1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 4-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity4RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, object> template = (arg1, arg2, arg3, arg4) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, object>)first.Value!)(1, 2, 3, 4));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, object>)second.Value!)(-1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 5-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity5RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 6-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity6RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 7-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity7RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 8-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity8RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            Assert.Equal(8, arg8);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7, 8));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 9-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity9RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            Assert.Equal(8, arg8);
            Assert.Equal(9, arg9);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7, 8, 9));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 10-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity10RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            Assert.Equal(8, arg8);
            Assert.Equal(9, arg9);
            Assert.Equal(10, arg10);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7, 8, 9, 10));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 11-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity11RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            Assert.Equal(8, arg8);
            Assert.Equal(9, arg9);
            Assert.Equal(10, arg10);
            Assert.Equal(11, arg11);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 12-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity12RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            Assert.Equal(8, arg8);
            Assert.Equal(9, arg9);
            Assert.Equal(10, arg10);
            Assert.Equal(11, arg11);
            Assert.Equal(12, arg12);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 13-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity13RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            Assert.Equal(8, arg8);
            Assert.Equal(9, arg9);
            Assert.Equal(10, arg10);
            Assert.Equal(11, arg11);
            Assert.Equal(12, arg12);
            Assert.Equal(13, arg13);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 14-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity14RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            Assert.Equal(8, arg8);
            Assert.Equal(9, arg9);
            Assert.Equal(10, arg10);
            Assert.Equal(11, arg11);
            Assert.Equal(12, arg12);
            Assert.Equal(13, arg13);
            Assert.Equal(14, arg14);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 15-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity15RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            Assert.Equal(8, arg8);
            Assert.Equal(9, arg9);
            Assert.Equal(10, arg10);
            Assert.Equal(11, arg11);
            Assert.Equal(12, arg12);
            Assert.Equal(13, arg13);
            Assert.Equal(14, arg14);
            Assert.Equal(15, arg15);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks 16-argument templates preserve typing, deferral, identity, and fixed values.</summary>
    [Fact]
    public void Arity16RetainsTypedDelegateAndConstant()
    {
        int calls = 0;
        object result = new object();
        CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object> template = (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16) =>
        {
            calls++;
            Assert.Equal(1, arg1);
            Assert.Equal(2, arg2);
            Assert.Equal(3, arg3);
            Assert.Equal(4, arg4);
            Assert.Equal(5, arg5);
            Assert.Equal(6, arg6);
            Assert.Equal(7, arg7);
            Assert.Equal(8, arg8);
            Assert.Equal(9, arg9);
            Assert.Equal(10, arg10);
            Assert.Equal(11, arg11);
            Assert.Equal(12, arg12);
            Assert.Equal(13, arg13);
            Assert.Equal(14, arg14);
            Assert.Equal(15, arg15);
            Assert.Equal(16, arg16);
            return result;
        };
        var slot = new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[template];
        var first = new Holder();
        var second = new Holder();
        assignment.Apply(first);
        assignment.Apply(second);
        Assert.Equal(0, calls);
        Assert.Same(template, first.Value);
        Assert.Same(template, second.Value);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>)first.Value!)(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16));
        Assert.Equal(1, calls);
        slot[result].Apply(second);
        Assert.Same(result, ((CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>)second.Value!)(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentNullException>(() => new CompositionTemplateSlot<Holder, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>(null!));
        Assert.Throws<ArgumentNullException>(() => slot[(CompositionTemplate<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, object>)null!]);
        Assert.Throws<ArgumentNullException>(() => slot[(object)null!]);
    }

    /// <summary>Checks delegate evaluation can return null and receive null without added validation.</summary>
    [Fact]
    public void NullableEvaluationValuesArePassedThrough()
    {
        CompositionTemplate<string?, string?> template = value => value;
        var slot = new CompositionTemplateSlot<Holder, string?, string?>((target, value) => target.Value = value);
        var holder = new Holder();
        slot[template].Apply(holder);
        Assert.Null(((CompositionTemplate<string?, string?>)holder.Value!)(null));
        Assert.Throws<ArgumentNullException>(() => slot[(string?)null]);
    }

    /// <summary>Checks default value-type compositions are fixed values, not omitted assignments.</summary>
    [Fact]
    public void DefaultValueTypeCompositionIsAccepted()
    {
        var holder = new Holder();
        var slot = new CompositionTemplateSlot<Holder, int, int>((target, value) => target.Value = value);
        slot[0].Apply(holder);
        Assert.Equal(0, ((CompositionTemplate<int, int>)holder.Value!)(17));
    }

    /// <summary>Checks fixed construction occurs once while lambda construction happens on each evaluation.</summary>
    [Fact]
    public void FixedCompositionDoesNotRebuildAndLambdaDoes()
    {
        int calls = 0;
        object Build()
        {
            calls++;
            return new object();
        }

        var slot = new CompositionTemplateSlot<Holder, int, object>((target, value) => target.Value = value);
        CompositionSlotAssignment<Holder> assignment = slot[Build()];
        Assert.Equal(1, calls);
        var holder = new Holder();
        assignment.Apply(holder);
        var fixedTemplate = (CompositionTemplate<int, object>)holder.Value!;
        Assert.Same(fixedTemplate(1), fixedTemplate(2));
        Assert.Equal(1, calls);
        slot[_ => Build()].Apply(holder);
        var dynamicTemplate = (CompositionTemplate<int, object>)holder.Value!;
        Assert.NotSame(dynamicTemplate(1), dynamicTemplate(2));
        Assert.Equal(3, calls);
    }

    /// <summary>Checks multicast order and the final return value use ordinary delegate semantics.</summary>
    [Fact]
    public void MulticastAndExceptionsKeepDelegateSemantics()
    {
        var order = new List<int>();
        CompositionTemplate<int, int> template = value =>
        {
            order.Add(1);
            return value;
        };
        template += value =>
        {
            order.Add(2);
            return value + 1;
        };
        var holder = new Holder();
        var slot = new CompositionTemplateSlot<Holder, int, int>((target, value) => target.Value = value);
        slot[template].Apply(holder);
        Assert.Empty(order);
        Assert.Equal(4, ((CompositionTemplate<int, int>)holder.Value!)(3));
        Assert.Equal(new[] { 1, 2 }, order);
        var failure = new InvalidOperationException("template");
        CompositionTemplate<int, int> failing = _ => throw failure;
        failing += _ =>
        {
            order.Add(3);
            return 0;
        };
        slot[failing].Apply(holder);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => ((CompositionTemplate<int, int>)holder.Value!)(0)));
        Assert.DoesNotContain(3, order);
    }

    /// <summary>Checks application failures propagate without invoking the template or undoing prior effects.</summary>
    [Fact]
    public void ApplicationFailurePreservesExistingAssignmentContract()
    {
        int calls = 0;
        var failure = new InvalidOperationException("apply");
        var holder = new Holder();
        CompositionTemplate<int, object> template = _ =>
        {
            calls++;
            return new object();
        };
        var slot = new CompositionTemplateSlot<Holder, int, object>((target, value) =>
        {
            target.Value = value;
            throw failure;
        });
        CompositionSlotAssignment<Holder> assignment = slot[template];
        Assert.Throws<ArgumentNullException>(() => assignment.Apply(null!));
        Assert.Throws<InvalidOperationException>(() => default(CompositionSlotAssignment<Holder>).Apply(holder));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => assignment.Apply(holder)));
        Assert.Same(template, holder.Value);
        Assert.Equal(0, calls);
    }

    private sealed class Holder
    {
        public Delegate? Value { get; set; }
    }
}

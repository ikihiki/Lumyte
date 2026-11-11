using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises retained transfers without a GPU or backend registration table.</summary>
public sealed partial class PersistentStorageTests
{
    /// <summary>Only initial and complete updates capture; only reference changes encode.</summary>
    [Fact]
    public void DirectUpdatesAndRepeatedUploadsDoNotRecaptureOrReencode()
    {
        var fixture = new Fixture();
        Value.Captures = 0;
        Value[] initial = [fixture.Make(1), fixture.Make(2)];
        ShaderDataStorage<Value> storage = fixture.Layout.CreateStorage<Value>(fixture.Owner, initial, fixture.Encode);
        Assert.Equal(2, Value.Captures);
        Assert.Equal(4, fixture.Encodes);
        var destination = new FakeBuffer(256);
        ShaderValueSnapshot[] first = fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 2, 0);
        fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 2, 0);
        storage.SetValue(0, new ShaderDataValueMember<Value, float>("Number"), 12f);
        storage.SetValue(0, new ShaderDataValueMember<Value, Vector4>("Nested.Color"), new Vector4(2, 3, 4, 5));
        Assert.Equal(2, Value.Captures);
        Assert.Equal(4, fixture.Encodes);
        Ref replacement = fixture.Reference(8);
        storage.SetReference(0, new ShaderDataReferenceMember<Value, float>("Buffer"), replacement);
        Assert.Equal(2, Value.Captures);
        Assert.Equal(5, fixture.Encodes);
        ShaderValueSnapshot[] updated = fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, 0);
        Assert.Equal(12f, Number(updated[0]));
        Assert.Equal(12f, MemoryMarshal.Read<float>(destination.Bytes));
        Assert.Equal(new Vector4(2, 3, 4, 5), MemoryMarshal.Read<Vector4>(destination.Bytes.AsSpan(16)));
        Assert.Equal(8, MemoryMarshal.Read<int>(destination.Bytes.AsSpan(96)));
        Assert.Same(replacement, Leaf(updated[0], "Buffer").Reference);
        Value full = fixture.Make(33);
        storage.Set(0, full);
        fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 2, 0);
        Assert.Equal(3, Value.Captures);
        Assert.Equal(7, fixture.Encodes);
        Assert.Equal(1f, Number(first[0]));
    }

    /// <summary>Complete snapshots own at most one stride and preserve typed identities.</summary>
    [Fact]
    public void FullAndPartialSnapshotsSurviveEverySubsequentMutation()
    {
        var fixture = new Fixture();
        Value[] initial = Enumerable.Range(0, 128).Select(i => fixture.Make(i)).ToArray();
        ShaderDataStorage<Value> storage = fixture.Layout.CreateStorage<Value>(fixture.Owner, initial, fixture.Encode);
        var destination = new FakeBuffer(128 * fixture.Layout.Size);
        ShaderValueSnapshot[] full = fixture.Layout.Upload(storage, fixture.Owner, destination, 0, initial.Length, 0);
        ShaderValueSnapshot partial = Assert.Single(fixture.Layout.Upload(storage, fixture.Owner, destination, 73, 1, 0));
        Ref original = initial[73].Buffer;
        Ref originalOther = initial[73].Other;
        initial[73] = fixture.Make(-1);
        storage.SetValue(73, new ShaderDataValueMember<Value, float>("Number"), -2f);
        ShaderValueSnapshot numeric = Assert.Single(fixture.Layout.Upload(storage, fixture.Owner, destination, 73, 1, 0));
        Ref replacement = fixture.Reference(0);
        storage.SetReference(73, new ShaderDataReferenceMember<Value, float>("Buffer"), replacement);
        ShaderValueSnapshot reference = Assert.Single(fixture.Layout.Upload(storage, fixture.Owner, destination, 73, 1, 0));
        storage.Set(73, fixture.Make(-3));
        storage.Set(0, fixture.Make(-4));
        fixture.Layout.Upload(storage, fixture.Owner, destination, 0, storage.Count, 0);
        Array.Fill(destination.Bytes, (byte)255);
        foreach (ShaderValueSnapshot snapshot in new[] { full[73], partial })
        {
            AssertComplete(snapshot, 73, original, originalOther, fixture.Layout.Size);
        }

        AssertComplete(numeric, -2, original, originalOther, fixture.Layout.Size);
        AssertComplete(reference, -2, replacement, originalOther, fixture.Layout.Size);
        Assert.Equal(new Vector4(73, 2, 3, 4), MemoryMarshal.Read<Vector4>(Leaf(partial, "Nested.Color").Data.Span));
        Assert.Equal(Matrix4x4.Identity, MemoryMarshal.Read<Matrix4x4>(Leaf(partial, "Transform").Data.Span));
    }

    /// <summary>Tokens, null references and indices are checked before retained state changes.</summary>
    [Fact]
    public void InvalidUpdatesLeaveBytesAndIdentitiesUnchanged()
    {
        var fixture = new Fixture();
        Value initial = fixture.Make(7);
        ShaderDataStorage<Value> storage = fixture.Layout.CreateStorage<Value>(fixture.Owner, [initial], fixture.Encode);
        var destination = new FakeBuffer(fixture.Layout.Size);
        fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, 0);
        byte[] before = destination.Bytes.ToArray();
        Action[] invalid =
        [
            () => storage.SetValue(0, new ShaderDataValueMember<Value, int>("Number"), 1),
            () => storage.SetValue(0, new ShaderDataValueMember<Value, float>("Missing"), 1f),
            () => storage.SetValue(0, default(ShaderDataValueMember<Value, float>), 1f),
            () => storage.SetValue(0, new ShaderDataValueMember<Value, Vector4>("Buffer"), Vector4.One),
            () => storage.SetReference(0, default(ShaderDataReferenceMember<Value, float>), fixture.Reference(1)),
            () => storage.SetReference(0, new ShaderDataReferenceMember<Value, float>("Missing"), fixture.Reference(1)),
            () => storage.SetReference(0, new ShaderDataReferenceMember<Value, float>("Number"), fixture.Reference(1)),
            () => storage.SetReference(0, new ShaderDataReferenceMember<Value, int>("Buffer"), new ForeignRef<int>()),
            () => storage.SetReference(0, new ShaderDataReferenceMember<Value, float>("Buffer"), null!),
            () => storage.SetValue(-1, new ShaderDataValueMember<Value, float>("Number"), 1f),
            () => storage.SetReference(1, new ShaderDataReferenceMember<Value, float>("Buffer"), fixture.Reference(1)),
            () => storage.Set(int.MaxValue, initial),
        ];
        foreach (Action action in invalid)
        {
            Assert.ThrowsAny<ArgumentException>(action);
            ShaderValueSnapshot snapshot = Assert.Single(fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, 0));
            Assert.Equal(before, destination.Bytes);
            AssertComplete(snapshot, 7, initial.Buffer, initial.Other, fixture.Layout.Size);
        }
    }

    /// <summary>Encoder validation and exceptions cannot partially replace an element.</summary>
    [Fact]
    public void FailedReferenceAndFullUpdatesAreAtomic()
    {
        var fixture = new Fixture();
        Value initial = fixture.Make(7);
        ShaderDataStorage<Value> storage = fixture.Layout.CreateStorage<Value>(fixture.Owner, [initial], fixture.Encode);
        var destination = new FakeBuffer(fixture.Layout.Size);
        fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, 0);
        byte[] before = destination.Bytes.ToArray();
        foreach (string failure in new[] { "throw", "short", "foreign", "null" })
        {
            fixture.Failure = failure;
            Assert.ThrowsAny<Exception>(() => storage.SetReference(0, new ShaderDataReferenceMember<Value, float>("Buffer"), fixture.Reference(99)));

            // The second reference fails after the numeric members and first reference were encoded.
            fixture.FailPath = "Other";
            Assert.ThrowsAny<Exception>(() => storage.Set(0, fixture.Make(99)));
            fixture.Failure = null;
            fixture.FailPath = "Buffer";
            ShaderValueSnapshot snapshot = Assert.Single(fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, 0));
            Assert.Equal(before, destination.Bytes);
            AssertComplete(snapshot, 7, initial.Buffer, initial.Other, fixture.Layout.Size);
        }

        Assert.Throws<ArgumentException>(() => storage.SetReference(0, new ShaderDataReferenceMember<Value, float>("Buffer"), new ForeignRef<float>()));
        var otherOwner = new Ref(new object(), 0);
        Assert.Throws<ArgumentException>(() => storage.SetReference(0, new ShaderDataReferenceMember<Value, float>("Buffer"), otherOwner));
        Value nullValue = initial with { Buffer = null! };
        Assert.ThrowsAny<ArgumentException>(() => storage.Set(0, nullValue));
        ShaderValueSnapshot unchanged = Assert.Single(fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, 0));
        AssertComplete(unchanged, 7, initial.Buffer, initial.Other, fixture.Layout.Size);
        Assert.Equal(before, destination.Bytes);
    }

    /// <summary>All upload prevalidation, including empty ranges, precedes writes.</summary>
    [Fact]
    public void UploadRejectsInvalidOwnerSchemaAndRangesBeforeWriting()
    {
        var fixture = new Fixture();
        ShaderDataStorage<Value> storage = fixture.Layout.CreateStorage<Value>(fixture.Owner, [fixture.Make(1), fixture.Make(2)], fixture.Encode);
        var destination = new FakeBuffer(512);
        Array.Fill(destination.Bytes, (byte)0xCC);
        byte[] before = destination.Bytes.ToArray();
        ShaderDataLayout incompatible = Layout(node => node["sizes"]![0]!["value"] = 144);
        Action[] invalid =
        [
            () => fixture.Layout.Upload(storage, new object(), destination, 0, 0, 0),
            () => incompatible.Upload(storage, fixture.Owner, destination, 0, 0, 0),
            () => fixture.Layout.Upload(new ForeignStorage(), fixture.Owner, destination, 0, 0, 0),
            () => fixture.Layout.Upload<Value>(null!, fixture.Owner, destination, 0, 0, 0),
            () => fixture.Layout.Upload(storage, fixture.Owner, null!, 0, 0, 0),
            () => fixture.Layout.Upload(storage, fixture.Owner, destination, -1, 1, 0),
            () => fixture.Layout.Upload(storage, fixture.Owner, destination, 0, -1, 0),
            () => fixture.Layout.Upload(storage, fixture.Owner, destination, 3, 0, 0),
            () => fixture.Layout.Upload(storage, fixture.Owner, destination, 1, 2, 0),
            () => fixture.Layout.Upload(storage, fixture.Owner, destination, int.MaxValue, int.MaxValue, 0),
            () => fixture.Layout.Upload(storage, fixture.Owner, destination, 0, int.MaxValue, 0),
            () => fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, ulong.MaxValue),
            () => fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, 500),
            () => fixture.Layout.Upload(storage, fixture.Owner, destination, 2, 0, 513),
        ];
        foreach (Action action in invalid)
        {
            Assert.ThrowsAny<ArgumentException>(action);
            Assert.Equal(before, destination.Bytes);
            Assert.Equal(0, destination.Writes);
        }

        Assert.Empty(fixture.Layout.Upload(storage, fixture.Owner, destination, 2, 0, destination.Count));
        Assert.Equal(0, destination.Writes);
    }

    /// <summary>A failed mapped copy cannot publish its already prepared snapshots.</summary>
    [Fact]
    public void DestinationFailureDoesNotReturnSnapshots()
    {
        var fixture = new Fixture();
        ShaderDataStorage<Value> storage = fixture.Layout.CreateStorage<Value>(fixture.Owner, [fixture.Make(5)], fixture.Encode);
        var destination = new FakeBuffer(128) { FailWrites = true };
        ShaderValueSnapshot[]? published = null;
        Assert.Throws<InvalidOperationException>(() => published = fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, 0));
        Assert.Null(published);
        Assert.All(destination.Bytes, value => Assert.Equal((byte)0, value));
        destination.FailWrites = false;
        Assert.Equal(5f, Number(Assert.Single(fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 1, 0))));
    }

    /// <summary>Empty initial arrays still certify every shape and helper ABI component.</summary>
    /// <param name="defect">The deliberately malformed schema component.</param>
    [Theory]
    [InlineData("offset")]
    [InlineData("alignment")]
    [InlineData("stride")]
    [InlineData("scalar-size")]
    [InlineData("matrix-element")]
    [InlineData("matrix-shape")]
    [InlineData("matrix-layout")]
    [InlineData("helper-kind")]
    [InlineData("helper-size")]
    [InlineData("helper-order")]
    [InlineData("helper-field-kind")]
    [InlineData("helper-field-type")]
    [InlineData("helper-offset")]
    [InlineData("target")]
    public void EmptyInitialValuesCannotMaskMalformedReflection(string defect)
    {
        var fixture = new Fixture();
        Assert.ThrowsAny<Exception>(() =>
        {
            ShaderDataLayout layout = Layout(node => Corrupt(node, defect));
            layout.CreateStorage<Value>(fixture.Owner, [], fixture.Encode);
        });
        Assert.Equal(0, fixture.Encodes);
    }

    /// <summary>Equal all-zero helper bytes and slot zero cannot substitute for certification.</summary>
    [Fact]
    public void ZeroAndEqualReferenceBytesDoNotMaskMalformedHelpers()
    {
        var fixture = new Fixture();
        Value initial = fixture.Make(0) with { Buffer = fixture.Reference(0), Other = fixture.Reference(0) };
        foreach (string defect in new[] { "helper-kind", "helper-size", "helper-order", "helper-field-kind" })
        {
            Assert.ThrowsAny<Exception>(() => Layout(node => Corrupt(node, defect)).CreateStorage<Value>(fixture.Owner, [initial], fixture.Encode));
        }

        Assert.Equal(0, fixture.Encodes);
    }

    /// <summary>Generated metadata must match the complete compiled member set.</summary>
    /// <param name="defect">The deliberately malformed schema component.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    public void EmptyStorageRejectsIncompleteSchema(string defect)
    {
        var fixture = new Fixture();
        Assert.ThrowsAny<ArgumentException>(() => Layout(node =>
        {
            JsonArray fields = node["fields"]!.AsArray();
            if (defect == "missing")
            {
                fields.RemoveAt(4);
            }
            else if (defect == "duplicate")
            {
                fields[4]!["name"] = "Buffer";
            }
            else
            {
                fields.Add(Field("Extra", Scalar("float32"), 116, 4));
            }
        }).CreateStorage<Value>(fixture.Owner, [], fixture.Encode));
        Assert.Equal(0, fixture.Encodes);
    }

    /// <summary>Nested alignment must constrain its offset and the containing structure alignment.</summary>
    /// <param name="parent">Whether to corrupt the parent instead of the nested offset.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedAlignmentIsCertifiedBeforeReadingInitialValues(bool parent)
    {
        var fixture = new Fixture();
        Value.Captures = 0;
        ShaderDataLayout layout = Layout(node =>
        {
            if (parent)
            {
                node["sizes"]![0]!["alignment"] = 4;
            }
            else
            {
                node["fields"]![1]!["binding"]!["offset"] = 4;
            }
        });
        Assert.Throws<NotSupportedException>(() => layout.CreateStorage<Value>(fixture.Owner, [], fixture.Encode));
        Assert.Equal(0, Value.Captures);
        Assert.Equal(0, fixture.Encodes);
    }

    /// <summary>Buffer targets are compared as logical schemas, not just 16-byte helpers.</summary>
    [Fact]
    public void EmptyStorageRejectsDifferentBufferReferenceTarget()
    {
        var fixture = new Fixture();
        ShaderDataLayout different = Layout(referenceTarget: "uint");
        Assert.Throws<ArgumentException>(() => different.CreateStorage<Value>(fixture.Owner, [], fixture.Encode));
        Assert.Equal(0, fixture.Encodes);
    }

    /// <summary>Valid empty storage certifies metadata without capturing or encoding values.</summary>
    [Fact]
    public void EmptyStorageCanUploadItsEmptyEndRange()
    {
        var fixture = new Fixture();
        Value.Captures = 0;
        ShaderDataStorage<Value> storage = fixture.Layout.CreateStorage<Value>(fixture.Owner, [], fixture.Encode);
        var destination = new FakeBuffer(1);
        Assert.Equal(0, storage.Count);
        Assert.Empty(fixture.Layout.Upload(storage, fixture.Owner, destination, 0, 0, 1));
        Assert.Equal(0, Value.Captures);
        Assert.Equal(0, fixture.Encodes);
        Assert.Equal(0, destination.Writes);
    }

    /// <summary>Legacy handwritten codecs keep capture support but have no retained metadata.</summary>
    [Fact]
    public void HandwrittenCodecWithoutMetadataIsExplicitlyUnsupported()
    {
        var fixture = new Fixture();
        Assert.Single(IShaderArguments.Capture(new Legacy(4)).Values);
        Assert.Throws<NotSupportedException>(() => fixture.Layout.CreateStorage<Legacy>(fixture.Owner, [], fixture.Encode));
        Assert.Throws<NotSupportedException>(() => fixture.Layout.CreateStorage<Legacy>(fixture.Owner, [new Legacy(4)], fixture.Encode));
    }

    /// <summary>Artifact padding stays zero after complete and field-local updates.</summary>
    [Fact]
    public void PaddingIsZeroAndPartialCopyTouchesOnlyItsDestinationRange()
    {
        var fixture = new Fixture();
        ShaderDataStorage<Value> storage = fixture.Layout.CreateStorage<Value>(fixture.Owner, [fixture.Make(1), fixture.Make(2)], fixture.Encode);
        storage.SetValue(1, new ShaderDataValueMember<Value, float>("Number"), 8f);
        storage.SetReference(1, new ShaderDataReferenceMember<Value, float>("Buffer"), fixture.Reference(9));
        var destination = new FakeBuffer(256);
        Array.Fill(destination.Bytes, (byte)0xCC);
        ShaderValueSnapshot snapshot = Assert.Single(fixture.Layout.Upload(storage, fixture.Owner, destination, 1, 1, 32));
        Assert.Equal(8f, Number(snapshot));
        Assert.All(destination.Bytes[..32], value => Assert.Equal((byte)0xCC, value));
        Assert.All(destination.Bytes[160..], value => Assert.Equal((byte)0xCC, value));
        Assert.All(destination.Bytes[36..48], value => Assert.Equal((byte)0, value));
        storage.Set(1, fixture.Make(10));
        fixture.Layout.Upload(storage, fixture.Owner, destination, 1, 1, 32);
        Assert.All(destination.Bytes[36..48], value => Assert.Equal((byte)0, value));
    }
}

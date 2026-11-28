using Errandy.Application.Common.Utils;
using Errandy.Application.Features.Errands.UploadProof;
using Errandy.Tests.Support;
using Xunit;

namespace Errandy.Tests.Utils;

public class ImageFileInspectorTests
{
    [Fact]
    public void Recognises_Jpeg() => Assert.Equal("jpg", ImageFileInspector.Inspect(TestFiles.Jpeg())?.Extension);

    [Fact]
    public void Recognises_Png() => Assert.Equal("png", ImageFileInspector.Inspect(TestFiles.Png())?.Extension);

    [Fact]
    public void Recognises_WebP() => Assert.Equal("webp", ImageFileInspector.Inspect(TestFiles.WebP())?.Extension);

    [Fact]
    public void Rejects_ATextFileNoMatterWhatItIsCalled() =>
        Assert.Null(ImageFileInspector.Inspect(TestFiles.NotAnImage()));

    [Fact]
    public void Rejects_AnEmptyFile() =>
        Assert.Null(ImageFileInspector.Inspect(new MemoryStream()));

    [Fact]
    public void Rejects_AFileThatIsTooShortToTell() =>
        Assert.Null(ImageFileInspector.Inspect(new MemoryStream(new byte[] { 0xFF, 0xD8 })));
    [Fact]
    public void Receipt_AcceptsAPdf() => Assert.Equal("pdf", ImageFileInspector.InspectReceipt(TestFiles.Pdf())?.Extension);

    [Fact]
    public void Receipt_StillAcceptsImages() => Assert.Equal("jpg", ImageFileInspector.InspectReceipt(TestFiles.Jpeg())?.Extension);

    [Fact]
    public void Receipt_RejectsATextFile() => Assert.Null(ImageFileInspector.InspectReceipt(TestFiles.NotAnImage()));

    [Fact]
    public void ThePhoto_DoesNotAcceptAPdf() => Assert.Null(ImageFileInspector.Inspect(TestFiles.Pdf()));

    [Fact]
    public void Inspecting_DoesNotMoveTheStream()
    {
        var stream = TestFiles.Jpeg();

        ImageFileInspector.Inspect(stream);

        Assert.Equal(0, stream.Position);
    }


}

public class UploadProofValidatorTests
{
    private static UploadProofCommand Valid() => new()
    {
        ErrandId = Guid.NewGuid(),
        RunnerId = Guid.NewGuid(),
        ImageContent = TestFiles.Jpeg(),
        FinalCost = 4800m
    };

    private static readonly UploadProofCommandValidator Validator = new();

    [Fact]
    public void APhotoAndACost_IsValid_WithoutAReceipt() =>
        Assert.True(Validator.Validate(Valid()).IsValid);

    [Fact]
    public void APhotoAndAReceipt_IsValid() =>
        Assert.True(Validator.Validate(Valid() with { ReceiptContent = TestFiles.Png() }).IsValid);

    [Fact]
    public void ANonImagePhoto_IsInvalid() =>
        Assert.False(Validator.Validate(Valid() with { ImageContent = TestFiles.NotAnImage() }).IsValid);

    [Fact]
    public void ANonImageReceipt_IsInvalid() =>
        Assert.False(Validator.Validate(Valid() with { ReceiptContent = TestFiles.NotAnImage() }).IsValid);

    [Fact]
    public void AnEmptyPhoto_IsInvalid() =>
        Assert.False(Validator.Validate(Valid() with { ImageContent = new MemoryStream() }).IsValid);

    [Fact]
    public void APdfReceipt_IsValid() =>
    Assert.True(Validator.Validate(Valid() with { ReceiptContent = TestFiles.Pdf() }).IsValid);

    [Fact]
    public void APdfAsThePhoto_IsInvalid() =>
        Assert.False(Validator.Validate(Valid() with { ImageContent = TestFiles.Pdf() }).IsValid);

    [Fact]
    public void APhotoOverTheSizeLimit_IsInvalid()
    {
        var bytes = new byte[ProofFileRules.MaxBytes + 1];
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF;

        Assert.False(Validator.Validate(Valid() with { ImageContent = new MemoryStream(bytes) }).IsValid);
    }

    [Fact]
    public void AZeroCost_IsInvalid() =>
        Assert.False(Validator.Validate(Valid() with { FinalCost = 0m }).IsValid);
}
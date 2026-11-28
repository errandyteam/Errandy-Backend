using Errandy.Application.Common.Utils;
using Xunit;

namespace Errandy.Tests.Utils;

public class PhoneNumberMaskerTests
{
    [Theory]
    [InlineData("Call me on 08012345678", "Call me on [hidden]")]
    [InlineData("My number is +234 801 234 5678.", "My number is [hidden].")]
    [InlineData("0801-234-5678 or 0902 111 2233", "[hidden] or [hidden]")]
    [InlineData("0 8 0 1 2 3 4 5 6 7 8", "[hidden]")]
    [InlineData("Reach me: 2348012345678", "Reach me: [hidden]")]
    [InlineData("+2340801 234 5678", "[hidden]")]
    [InlineData("0801.234.5678", "[hidden]")]
    [InlineData("08012345678 08012345679", "[hidden] [hidden]")]
    [InlineData("Call 08012345678, thanks", "Call [hidden], thanks")]
    [InlineData("Send to opay 8012345678", "Send to opay [hidden]")]
    [InlineData("Account 0123456789 GTBank", "Account [hidden] GTBank")]
    [InlineData("Account 0123 456 789 0 GTBank", "Account [hidden] GTBank")]
    [InlineData("My BVN is 22123456789", "My BVN is [hidden]")]
    [InlineData("Call 08012345678 or pay 8012345678", "Call [hidden] or pay [hidden]")]
    [InlineData("Card 5399 8301 2345 6789 please", "Card [hidden] please")]
    [InlineData("call08012345678now", "call[hidden]now")]
    public void Mask_HidesNigerianPhoneNumbers(string input, string expected)
    {
        var result = PhoneNumberMasker.Mask(input, out var wasMasked);

        Assert.Equal(expected, result);
        Assert.True(wasMasked);
    }

    [Theory]
    [InlineData("I spent 4800 naira on 12 items")]
    [InlineData("Total is 480000 naira")]
    [InlineData("Item code 12345")]
    [InlineData("Meet at 0800")]
    [InlineData("")]
    [InlineData("Order 123456789")]
    [InlineData("Price is 1,000,000,000 naira")]
    [InlineData("Delivered on 20-11-2026")]
    public void Mask_LeavesOrdinaryMessagesAlone(string input)
    {
        var result = PhoneNumberMasker.Mask(input, out var wasMasked);

        Assert.Equal(input, result);
        Assert.False(wasMasked);
    }

    [Fact]
    public void Replacement_IsShorterThanTheShortestNumber()
    {
        // The shortest number the masker catches is 11 characters. If the replacement were
        // longer, masking could push a message over the 2000-character limit.
        Assert.True(PhoneNumberMasker.Replacement.Length < 10);
    }

    [Fact]
    public void Mask_NeverMakesAMessageLonger()
    {
        var message = string.Join(" ", Enumerable.Repeat("08012345678", 150));

        var result = PhoneNumberMasker.Mask(message, out _);

        Assert.True(result.Length <= message.Length);
    }
}

public class LocationTests
{
    [Fact]
    public void Fuzz_RoundsCoordinatesSoTheExactBuildingIsNotRevealed()
    {
        var (lat, lng) = LocationPrivacyHelper.Fuzz(6.6018, 3.3515);

        Assert.Equal(6.6, lat, 2);
        Assert.Equal(3.35, lng, 2);
    }

    [Fact]
    public void Distance_BetweenTheSamePoint_IsZero()
    {
        Assert.Equal(0, GeoDistanceCalculator.DistanceKm(6.6018, 3.3515, 6.6018, 3.3515), 3);
    }

    [Fact]
    public void Distance_ZeroPointZeroFiveDegreesEast_IsAboutFiveAndAHalfKm()
    {
        // This is outside the default 5km radius, which is why such an errand
        // would not show up in a runner's "nearby" list.
        var km = GeoDistanceCalculator.DistanceKm(6.6018, 3.3515, 6.6018, 3.4015);

        Assert.InRange(km, 5.4, 5.6);
    }
}
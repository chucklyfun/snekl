using Snekl.Domain;

namespace Snekl.Domain.Tests;

public class NsfwAgeTests
{
    [Fact]
    public void CalculateAge_BeforeBirthday_IsPreviousYear()
    {
        var age = NsfwAge.CalculateAge(new DateOnly(2000, 6, 15), new DateOnly(2018, 6, 14));
        Assert.Equal(17, age);
    }

    [Fact]
    public void CalculateAge_OnBirthday_Counts()
    {
        var age = NsfwAge.CalculateAge(new DateOnly(2000, 6, 15), new DateOnly(2018, 6, 15));
        Assert.Equal(18, age);
    }

    [Fact]
    public void MeetsMinimum_RejectsFutureBirthDate()
    {
        Assert.False(NsfwAge.MeetsMinimum(new DateOnly(2030, 1, 1), 18, new DateOnly(2026, 9, 16)));
    }

    [Fact]
    public void ClampMinimumAge_DoesNotGoBelow18()
    {
        Assert.Equal(18, NsfwAge.ClampMinimumAge(16));
    }
}

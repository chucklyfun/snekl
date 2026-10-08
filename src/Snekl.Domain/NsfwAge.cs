namespace Snekl.Domain;

public static class NsfwAge
{
    public const int DefaultMinimumAge = 18;
    public const int MaxReasonableAge = 130;
    public const int MaxExplanationLength = 2000;

    public static string NormalizeExplanation(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length <= MaxExplanationLength) return trimmed;
        return trimmed[..MaxExplanationLength];
    }

    public static int ClampMinimumAge(int age)
    {
        if (age < DefaultMinimumAge) return DefaultMinimumAge;
        if (age > MaxReasonableAge) return MaxReasonableAge;
        return age;
    }

    public static int CalculateAge(DateOnly birthDate, DateOnly today)
    {
        if (birthDate > today) return -1;
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;
        return age;
    }

    public static bool MeetsMinimum(DateOnly birthDate, int minimumAge, DateOnly today)
    {
        var age = CalculateAge(birthDate, today);
        if (age < 0 || age > MaxReasonableAge) return false;
        return age >= ClampMinimumAge(minimumAge);
    }
}

namespace GameSaveManager.Application.Updates;

/// <summary>用于客户端更新比较的 SemVer 2.0.0 版本；构建元数据不参与顺序比较。</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, IReadOnlyList<string> prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public IReadOnlyList<string> Prerelease { get; }
    public bool IsPrerelease => Prerelease.Count > 0;

    public static SemanticVersion Parse(string value)
    {
        if (TryParse(value, out SemanticVersion? version) && version is not null) return version;
        throw new FormatException($"无效的语义化版本：{value}");
    }

    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string normalized = value.Trim();
        if (normalized.StartsWith('v')) normalized = normalized[1..];
        int buildSeparator = normalized.IndexOf('+');
        if (buildSeparator >= 0) normalized = normalized[..buildSeparator];

        string[] versionAndPrerelease = normalized.Split('-', 2);
        string[] numbers = versionAndPrerelease[0].Split('.');
        if (numbers.Length != 3
            || !TryParseNumber(numbers[0], out int major)
            || !TryParseNumber(numbers[1], out int minor)
            || !TryParseNumber(numbers[2], out int patch)) return false;

        string[] prerelease = versionAndPrerelease.Length == 1
            ? []
            : versionAndPrerelease[1].Split('.');
        if (prerelease.Any(identifier => !IsValidIdentifier(identifier))) return false;
        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        int numeric = Major.CompareTo(other.Major);
        if (numeric == 0) numeric = Minor.CompareTo(other.Minor);
        if (numeric == 0) numeric = Patch.CompareTo(other.Patch);
        if (numeric != 0) return numeric;
        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;
        if (!other.IsPrerelease) return -1;

        int shared = Math.Min(Prerelease.Count, other.Prerelease.Count);
        for (int index = 0; index < shared; index++)
        {
            int identifier = CompareIdentifier(Prerelease[index], other.Prerelease[index]);
            if (identifier != 0) return identifier;
        }
        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public override string ToString() =>
        Prerelease.Count == 0
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{string.Join('.', Prerelease)}";

    private static bool TryParseNumber(string value, out int number)
    {
        number = 0;
        return value.Length > 0
            && (value.Length == 1 || value[0] != '0')
            && int.TryParse(value, out number)
            && number >= 0;
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')) return false;
        return !value.All(char.IsAsciiDigit) || value.Length == 1 || value[0] != '0';
    }

    private static int CompareIdentifier(string left, string right)
    {
        bool leftNumeric = left.All(char.IsAsciiDigit);
        bool rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric && rightNumeric)
        {
            int length = left.Length.CompareTo(right.Length);
            return length != 0 ? length : string.CompareOrdinal(left, right);
        }
        if (leftNumeric) return -1;
        if (rightNumeric) return 1;
        return string.CompareOrdinal(left, right);
    }
}

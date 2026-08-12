using System.Globalization;

namespace NetPulseMonitor;

internal sealed record RegionalCountryOption(
    string Code,
    string CultureName,
    string EnglishName)
{
    public override string ToString() => $"{EnglishName} ({Code})";
}

internal sealed record RegionalTimeZoneOption(
    string Id,
    string DisplayName)
{
    public override string ToString() => DisplayName;
}

internal static class RegionalSettingsCatalog
{
    private static readonly Lazy<IReadOnlyList<RegionalCountryOption>> Countries =
        new(CreateCountries);
    private static readonly Lazy<IReadOnlyList<RegionalTimeZoneOption>> TimeZones =
        new(() => TimeZoneInfo.GetSystemTimeZones()
            .Select(zone => new RegionalTimeZoneOption(
                zone.Id,
                $"{zone.Id} ({FormatUtcOffset(zone.BaseUtcOffset)})"))
            .OrderBy(zone => zone.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray());

    private static readonly IReadOnlyDictionary<string, string> SuggestedWindowsZones =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GR"] = "GTB Standard Time",
            ["CY"] = "GTB Standard Time",
            ["GB"] = "GMT Standard Time",
            ["IE"] = "GMT Standard Time",
            ["PT"] = "GMT Standard Time",
            ["DE"] = "W. Europe Standard Time",
            ["AT"] = "W. Europe Standard Time",
            ["CH"] = "W. Europe Standard Time",
            ["IT"] = "W. Europe Standard Time",
            ["NL"] = "W. Europe Standard Time",
            ["FR"] = "Romance Standard Time",
            ["ES"] = "Romance Standard Time",
            ["PL"] = "Central European Standard Time",
            ["RO"] = "GTB Standard Time",
            ["BG"] = "FLE Standard Time",
            ["FI"] = "FLE Standard Time",
            ["EE"] = "FLE Standard Time",
            ["LV"] = "FLE Standard Time",
            ["LT"] = "FLE Standard Time",
            ["TR"] = "Turkey Standard Time",
            ["JP"] = "Tokyo Standard Time",
            ["CN"] = "China Standard Time",
            ["IN"] = "India Standard Time",
            ["BR"] = "E. South America Standard Time",
            ["ZA"] = "South Africa Standard Time",
            ["NZ"] = "New Zealand Standard Time"
        };

    public static IReadOnlyList<RegionalCountryOption> GetCountries() =>
        Countries.Value;

    public static IReadOnlyList<RegionalTimeZoneOption> GetTimeZones() =>
        TimeZones.Value;

    public static string GetInitialCountryCode()
    {
        try
        {
            string code = RegionInfo.CurrentRegion.TwoLetterISORegionName;
            if (Countries.Value.Any(item => item.Code.Equals(
                    code, StringComparison.OrdinalIgnoreCase)))
                return code.ToUpperInvariant();
        }
        catch (CultureNotFoundException)
        {
        }
        return "US";
    }

    public static RegionalCountryOption ResolveCountry(string? countryCode)
    {
        string code = string.IsNullOrWhiteSpace(countryCode)
            ? GetInitialCountryCode()
            : countryCode.Trim().ToUpperInvariant();
        return Countries.Value.FirstOrDefault(item => item.Code == code) ??
               Countries.Value.First(item => item.Code == "US");
    }

    public static string ResolveCultureName(
        string? countryCode,
        string? cultureName = null)
    {
        RegionalCountryOption country = ResolveCountry(countryCode);
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(cultureName.Trim());
                var region = new RegionInfo(culture.Name);
                if (region.TwoLetterISORegionName.Equals(
                        country.Code, StringComparison.OrdinalIgnoreCase))
                    return culture.Name;
            }
            catch (CultureNotFoundException)
            {
            }
        }
        return country.CultureName;
    }

    public static string SuggestTimeZoneId(string? countryCode)
    {
        string code = ResolveCountry(countryCode).Code;
        if (SuggestedWindowsZones.TryGetValue(code, out string? suggested) &&
            TimeZones.Value.Any(zone => zone.Id == suggested))
            return suggested;

        try
        {
            if (RegionInfo.CurrentRegion.TwoLetterISORegionName.Equals(
                    code, StringComparison.OrdinalIgnoreCase))
                return TimeZoneInfo.Local.Id;
        }
        catch (CultureNotFoundException)
        {
        }
        return TimeZoneInfo.Local.Id;
    }

    public static TimeZoneInfo ResolveTimeZone(string? id, string? countryCode = null)
    {
        string candidate = string.IsNullOrWhiteSpace(id)
            ? SuggestTimeZoneId(countryCode)
            : id.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(candidate);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                SuggestTimeZoneId(countryCode));
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static IReadOnlyList<RegionalCountryOption> CreateCountries()
    {
        var candidates = new List<(RegionInfo Region, CultureInfo Culture)>();
        foreach (CultureInfo culture in CultureInfo.GetCultures(
                     CultureTypes.SpecificCultures))
        {
            try
            {
                candidates.Add((new RegionInfo(culture.Name), culture));
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return candidates
            .GroupBy(item => item.Region.TwoLetterISORegionName,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                (RegionInfo Region, CultureInfo Culture) selected = group
                    .OrderByDescending(item => item.Culture.Name.Equals(
                        CultureInfo.CurrentCulture.Name,
                        StringComparison.OrdinalIgnoreCase))
                    .ThenBy(item => item.Culture.Name, StringComparer.OrdinalIgnoreCase)
                    .First();
                return new RegionalCountryOption(
                    selected.Region.TwoLetterISORegionName.ToUpperInvariant(),
                    selected.Culture.Name,
                    selected.Region.EnglishName);
            })
            .OrderBy(item => item.EnglishName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FormatUtcOffset(TimeSpan offset)
    {
        int totalMinutes = (int)Math.Abs(offset.TotalMinutes);
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"UTC{sign}{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }
}

internal sealed class OfficialClock
{
    public string CountryCode { get; }
    public string CountryName { get; }
    public CultureInfo Culture { get; }
    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(
        DateTimeOffset.UtcNow,
        TimeZone);

    public OfficialClock(AppSettings settings)
    {
        RegionalCountryOption country = RegionalSettingsCatalog.ResolveCountry(
            settings.CountryCode);
        CountryCode = country.Code;
        CountryName = country.EnglishName;
        Culture = CultureInfo.GetCultureInfo(
            RegionalSettingsCatalog.ResolveCultureName(
                country.Code,
                settings.CountryCultureName));
        TimeZone = RegionalSettingsCatalog.ResolveTimeZone(
            settings.OfficialTimeZoneId,
            country.Code);
    }

    public DateTimeOffset Convert(DateTime value)
    {
        DateTime utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), TimeZone);
    }

    public string FormatDisplay(DateTime value) =>
        Convert(value).ToString(
            Culture.DateTimeFormat.ShortDatePattern + " HH:mm:ss",
            Culture);

    public string FormatWallClock(DateTime value) =>
        value.ToString(
            Culture.DateTimeFormat.ShortDatePattern + " HH:mm:ss",
            Culture);

    public string FormatTime(DateTime value) =>
        Convert(value).ToString("HH:mm:ss", Culture);

    public string FormatCsv(DateTime value) =>
        Convert(value).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    public string FormatReport(DateTime value)
    {
        DateTimeOffset official = Convert(value);
        return official.ToString(
                   Culture.DateTimeFormat.ShortDatePattern + " HH:mm:ss zzz",
                   Culture) +
               $" ({TimeZone.Id}, {CountryCode})";
    }
}

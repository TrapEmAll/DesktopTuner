using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DesktopTuner;

public sealed record TaskbarWeatherLocation(string Name, double Latitude, double Longitude);
public sealed record TaskbarCurrentWeather(double Temperature, string Unit, int WeatherCode, bool IsDay);
public enum TaskbarWeatherAnimation { None, Sun, Rain, Snow, Storm }

public static class TaskbarWeatherPolicy
{
    private static readonly HashSet<string> FahrenheitRegions = ["BS", "BZ", "KY", "PW", "US"];

    public static TaskbarWeatherSettings Normalize(TaskbarWeatherSettings? settings)
    {
        settings ??= new TaskbarWeatherSettings();
        var latitude = settings.Latitude is >= -90 and <= 90 ? settings.Latitude : null;
        var longitude = settings.Longitude is >= -180 and <= 180 ? settings.Longitude : null;
        var hasCoordinates = latitude is not null && longitude is not null;
        return settings with
        {
            Enabled = settings.Enabled && hasCoordinates,
            LocationQuery = Limit(settings.LocationQuery, 120),
            LocationName = hasCoordinates ? Limit(settings.LocationName, 120) : string.Empty,
            Latitude = hasCoordinates ? latitude : null,
            Longitude = hasCoordinates ? longitude : null
        };
    }

    public static bool UsesFahrenheit(string? regionCode) =>
        !string.IsNullOrWhiteSpace(regionCode) && FahrenheitRegions.Contains(regionCode.Trim().ToUpperInvariant());

    public static string GetTemperatureUnit(string? regionCode) => UsesFahrenheit(regionCode) ? "°F" : "°C";

    public static string GetCondition(int code, bool isDay) => code switch
    {
        0 => isDay ? "Clear sky" : "Clear night",
        1 => isDay ? "Mainly clear" : "Mainly clear night",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Weather conditions unavailable"
    };

    public static string GetGlyph(int code, bool isDay) => code switch
    {
        0 => isDay ? "☀" : "☾",
        1 or 2 => "⛅",
        3 => "☁",
        45 or 48 => "≋",
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "🌧",
        71 or 73 or 75 or 77 or 85 or 86 => "❄",
        95 or 96 or 99 => "⛈",
        _ => "?"
    };

    public static TaskbarWeatherAnimation GetAnimation(int code) => code switch
    {
        0 => TaskbarWeatherAnimation.Sun,
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => TaskbarWeatherAnimation.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => TaskbarWeatherAnimation.Snow,
        95 or 96 or 99 => TaskbarWeatherAnimation.Storm,
        _ => TaskbarWeatherAnimation.None
    };

    public static TaskbarCurrentWeather ParseCurrentResponse(string json, string unit)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("current", out var current) ||
            !current.TryGetProperty("temperature_2m", out var temperature) ||
            !current.TryGetProperty("weather_code", out var weatherCode) ||
            !current.TryGetProperty("is_day", out var isDay))
            throw new InvalidDataException("The weather service response did not include current conditions.");
        return new TaskbarCurrentWeather(
            temperature.GetDouble(),
            unit == "fahrenheit" ? "°F" : "°C",
            weatherCode.GetInt32(),
            isDay.GetInt32() != 0);
    }

    private static string Limit(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

public static class TaskbarWeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(20);
    private static string? _cachedKey;
    private static TaskbarCurrentWeather? _cachedWeather;
    private static DateTime _cacheExpiresUtc;

    static TaskbarWeatherService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopTuner/1.0");

    public static async Task<IReadOnlyList<TaskbarWeatherLocation>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var uri = new Uri("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(query.Trim()) + "&count=5&language=en&format=json");
        using var response = await Http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        return results.EnumerateArray()
            .Select(TryReadLocation)
            .Where(location => location is not null)
            .Cast<TaskbarWeatherLocation>()
            .ToArray();
    }

    public static async Task<TaskbarCurrentWeather> GetCurrentAsync(double latitude, double longitude, string? regionCode, CancellationToken cancellationToken = default)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Weather coordinates are outside the valid range.");
        var unit = TaskbarWeatherPolicy.UsesFahrenheit(regionCode) ? "fahrenheit" : "celsius";
        var key = string.Create(CultureInfo.InvariantCulture, $"{latitude:F4},{longitude:F4}:{unit}");
        await CacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (key == _cachedKey && _cachedWeather is not null && DateTime.UtcNow < _cacheExpiresUtc)
                return _cachedWeather;

            var query = string.Create(CultureInfo.InvariantCulture,
                $"latitude={latitude:R}&longitude={longitude:R}&current=temperature_2m,weather_code,is_day&temperature_unit={unit}&timezone=auto");
            using var response = await Http.GetAsync(new Uri("https://api.open-meteo.com/v1/forecast?" + query), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var weather = TaskbarWeatherPolicy.ParseCurrentResponse(json, unit);
            _cachedKey = key;
            _cachedWeather = weather;
            _cacheExpiresUtc = DateTime.UtcNow.Add(CacheDuration);
            return weather;
        }
        finally { CacheLock.Release(); }
    }

    private static TaskbarWeatherLocation? TryReadLocation(JsonElement result)
    {
        if (!result.TryGetProperty("name", out var name) || !result.TryGetProperty("latitude", out var latitude) || !result.TryGetProperty("longitude", out var longitude))
            return null;
        var cityName = name.GetString();
        if (string.IsNullOrWhiteSpace(cityName) || !latitude.TryGetDouble(out var lat) || !longitude.TryGetDouble(out var lon))
            return null;
        var details = new[]
        {
            result.TryGetProperty("admin1", out var admin1) ? admin1.GetString() : null,
            result.TryGetProperty("country", out var country) ? country.GetString() : null
        }.Where(value => !string.IsNullOrWhiteSpace(value) && !string.Equals(value, cityName, StringComparison.OrdinalIgnoreCase));
        var displayName = string.Join(", ", new[] { cityName }.Concat(details).Distinct(StringComparer.OrdinalIgnoreCase));
        return new TaskbarWeatherLocation(displayName, lat, lon);
    }
}

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Globalization;
using Microsoft.Maps.MapControl.WPF;
using System.Collections.Generic;

public class ElevationResult
{
    [JsonProperty("latitude")]
    public double Latitude { get; set; }

    [JsonProperty("longitude")]
    public double Longitude { get; set; }

    [JsonProperty("elevation")]
    public double Elevation { get; set; }
}

public class ElevationResponse
{
    [JsonProperty("results")]
    public List<ElevationResult> Results { get; set; }
}

public class PathfindingMath
{
    private const double EarthRadius = 6371e3; // Радиус Земли в метрах
    private readonly double ElevationWeightFactor;
    private static readonly HttpClient HttpClient = new HttpClient();

    public PathfindingMath(double elevationWeightFactor)
    {
        ElevationWeightFactor = elevationWeightFactor;
    }

    public double GetDistance(Location from, Location to)
    {
        double lat1 = DegreeToRadian(from.Latitude);
        double lon1 = DegreeToRadian(from.Longitude);
        double lat2 = DegreeToRadian(to.Latitude);
        double lon2 = DegreeToRadian(to.Longitude);

        double deltaLat = lat2 - lat1;
        double deltaLon = lon2 - lon1;

        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                   Math.Cos(lat1) * Math.Cos(lat2) *
                   Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadius * c;
    }

    public double CalculateCost(Location from, Location to, double fromElevation, double toElevation)
    {
        double horizontalDistance = GetDistance(from, to);
        double elevationDifference = toElevation - fromElevation;
        double elevationPenalty = Math.Abs(elevationDifference) * ElevationWeightFactor;

        return horizontalDistance + elevationPenalty;
    }

    private double DegreeToRadian(double degree)
    {
        return degree * (Math.PI / 180.0);
    }

    public async Task<double> GetElevationAsync(double latitude, double longitude)
    {
        try
        {
            string apiUrl = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-elevation.com/api/v1/lookup?locations={0},{1}", latitude, longitude);

            HttpResponseMessage response = await HttpClient.GetAsync(apiUrl);

            if (response.IsSuccessStatusCode)
            {
                string responseData = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<ElevationResponse>(responseData);
                return responseObject?.Results?[0]?.Elevation ?? 0;
            }
            else
            {
                Console.WriteLine($"Ошибка запроса: {response.StatusCode}");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при получении высоты: {ex.Message}");
            return 0;
        }
    }
}

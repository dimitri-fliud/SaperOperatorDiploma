using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maps.MapControl.WPF;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using System.Globalization;

namespace SaperOperator
{
    public class SapperPathfinder
    {
        private List<MapPolygon> selectedZones; // Список вибраних зон
        private List<Location> sapperLocations; // Список координат саперів
        private List<Location> zoneLocations; // Список координат зон
        private Dictionary<Location, double> elevations; // Словник з висотами для кожної точки

        private static readonly HttpClient HttpClient = new HttpClient(); // Статичний HttpClient для запитів

        public SapperPathfinder(List<Location> zoneLocations, List<Location> sapperLocations)
        {
            this.zoneLocations = zoneLocations;
            this.sapperLocations = sapperLocations;
            this.elevations = new Dictionary<Location, double>(); // Ініціалізація словника з висотами
        }

        // Метод для отримання висоти для конкретної точки
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
                    Console.WriteLine($"Помилка запиту: {response.StatusCode}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка при отриманні висоти: {ex.Message}");
                return 0;
            }
        }

        // Метод для розрахунку вартості шляху з урахуванням висоти
        private async Task<double> GetPathCost(Location start, Location end)
        {
            double distance = GetDistance(start, end); // Отримуємо географічну відстань
            double startElevation = await GetElevationAsync(start.Latitude, start.Longitude); // Отримуємо висоту початкової точки
            double endElevation = await GetElevationAsync(end.Latitude, end.Longitude); // Отримуємо висоту кінцевої точки
            double elevationDifference = Math.Abs(startElevation - endElevation); // Різниця у висотах

            // Можна додати вагу за різницю у висотах, наприклад, помножити на певний коефіцієнт
            double elevationWeight = elevationDifference * 0.1; // 0.1 - це коефіцієнт, можна налаштувати

            return distance + elevationWeight; // Вартість шляху - це відстань + вага різниці у висотах
        }

        public async Task<double> CalculateTotalPathCost(List<Location> path)
        {
            double totalCost = 0;

            for (int i = 0; i < path.Count - 1; i++)
            {
                double segmentCost = await GetPathCost(path[i], path[i + 1]);
                totalCost += segmentCost;
                Console.WriteLine($"Крок {i + 1}: Перехід від ({path[i].Latitude}, {path[i].Longitude}) до ({path[i + 1].Latitude}, {path[i + 1].Longitude}) - Вартість: {segmentCost}, Загальна вартість: {totalCost}");
            }

            return totalCost;
        }

        public async Task<List<Location>> FindOptimalPath(Location start, List<Location> assignedMines)
        {
            List<Location> path = new List<Location> { start };

            foreach (var mineZone in assignedMines)
            {
                path.Add(mineZone);
            }

            return path;
        }

        // Метод для розрахунку відстані між двома точками (наприклад, з використанням формули Гаверсина)
        private double GetDistance(Location start, Location end)
        {
            double R = 6371; // Радіус Землі у кілометрах
            double lat1 = start.Latitude * Math.PI / 180;
            double lon1 = start.Longitude * Math.PI / 180;
            double lat2 = end.Latitude * Math.PI / 180;
            double lon2 = end.Longitude * Math.PI / 180;

            double dlat = lat2 - lat1;
            double dlon = lon2 - lon1;
            double a = Math.Sin(dlat / 2) * Math.Sin(dlat / 2) +
                       Math.Cos(lat1) * Math.Cos(lat2) *
                       Math.Sin(dlon / 2) * Math.Sin(dlon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            double distance = R * c; // Відстань у кілометрах
            return distance;
        }

        public async Task<Dictionary<Location, List<Location>>> AssignMineZones()
        {
            var assignments = new Dictionary<Location, List<Location>>();
            var unassignedZones = new HashSet<Location>(zoneLocations); // Нерозподілені зони

            // Ініціалізуємо порожні списки для кожного сапера
            foreach (var sapper in sapperLocations)
            {
                assignments[sapper] = new List<Location>();
            }

            // Розділимо зони на групи для кожного сапера
            var sapperZoneGroups = new Dictionary<Location, List<Location>>();
            for (int i = 0; i < sapperLocations.Count; i++)
            {
                sapperZoneGroups[sapperLocations[i]] = new List<Location>();
            }

            // Розподілимо зони по групах
            foreach (var zone in zoneLocations)
            {
                // Знайдемо найближчого сапера для поточної зони
                var nearestSapper = sapperLocations.OrderBy(s => GetDistance(s, zone)).First();
                sapperZoneGroups[nearestSapper].Add(zone);
            }

            // Призначимо зони саперам
            foreach (var sapper in sapperLocations)
            {
                var zonesForSapper = sapperZoneGroups[sapper];
                foreach (var zone in zonesForSapper)
                {
                    assignments[sapper].Add(zone);
                    unassignedZones.Remove(zone);
                }
            }

            return assignments;
        }

        private Location GetZoneCenter(MapPolygon zone)
        {
            double sumLat = 0, sumLon = 0;
            int count = zone.Locations.Count;

            foreach (var location in zone.Locations)
            {
                sumLat += location.Latitude;
                sumLon += location.Longitude;
            }

            return new Location(sumLat / count, sumLon / count);
        }

        // Відповідь API для отримання висоти
        public class ElevationResponse
        {
            public List<ElevationResult> Results { get; set; }
        }

        public class ElevationResult
        {
            public double Elevation { get; set; }
        }
    }
}

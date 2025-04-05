using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;
using System.Xml.Linq;
using Microsoft.Maps.MapControl.WPF;
using System.Windows.Input;
using System.Windows.Controls;

namespace SaperOperator
{
    public class Node
    {
        public Location Location { get; set; }
        public double GCost { get; set; }
        public double HCost { get; set; }
        public Node Parent { get; set; }

        // Перевизначення Equals та GetHashCode для коректного порівняння
        public override bool Equals(object obj)
        {
            if (obj is Node otherNode)
            {
                return (this.Location.Latitude == otherNode.Location.Latitude) && (this.Location.Longitude == otherNode.Location.Longitude);  // Порівнюємо лише координати
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Location.GetHashCode();  // Використовуємо координати для хешування
        }

        public double FCost => GCost + HCost;
    }

    public partial class MainWindow : Window
    {
        private List<MapPolygon> selectedZones = new List<MapPolygon>();
        private List<Pushpin> sappers = new List<Pushpin>();
        private bool isSelectingArea = false;
        private Location startLocation;
        private MapPolygon currentPolygon;

        public MainWindow()
        {
            InitializeComponent();
            InitializeMap();
        }

        private void InitializeMap()
        {
            BingMap.Center = new Location(50.4501, 30.5234); // Центр карти (Київ)
            BingMap.ZoomLevel = 10;
        }

        private void SelectArea_Click(object sender, RoutedEventArgs e)
        {
            isSelectingArea = !isSelectingArea; // Включаємо або виключаємо режим виділення зон
            MessageBox.Show(isSelectingArea
                ? "Режим виділення зон активовано. Клацніть правою кнопкою миші для початку."
                : "Режим виділення зон вимкнено.");
        }

        private void PlaceSappers_Click(object sender, RoutedEventArgs e)
        {
            isSelectingArea = false;
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

        public async void CalculatePaths_Click(object sender, RoutedEventArgs e)
        {
            // Перевірка на наявність саперів та зон
            if (sappers.Count == 0 || selectedZones.Count == 0)
            {
                MessageBox.Show("Немає саперів або зон для призначення!");
                return;
            }

            var zoneLocations = selectedZones.Select(zone => GetZoneCenter(zone)).ToList();
            var sapperLocations = sappers.Select(sapper => sapper.Location).ToList();

            // Створюємо шлях, передаючи список саперів
            SapperPathfinder pathfinder = new SapperPathfinder(zoneLocations, sapperLocations);
            var assignments = await pathfinder.AssignMineZones();

            // Очищаємо старі TextBlock
            ClearCostTextBlocks();

            // Обробка одиночного випадку (1 сапер, 1 зона)
            if (assignments.Count == 1)
            {
                var sapper = sappers.First();
                var sapperLocation = sapper.Location;  // Отримуємо координати першого сапера
                var assignedMines = assignments.First().Value
                    .Select(zone => zone)  // Отримуємо центри зон
                    .ToList();

                if (assignedMines.Count > 0)
                {
                    var path = await pathfinder.FindOptimalPath(sapper.Location, assignedMines);
                    DisplayPath(path);

                    // Розраховуємо загальну вартість шляху
                    double totalCost = await pathfinder.CalculateTotalPathCost(path);
                    DisplayCostAboveSapper(sapper, totalCost); // Відображаємо вартість над сапером
                }
            }
            else if (assignments.Count > 1)
            {
                // Обробка випадку з кількома саперами
                foreach (var sapper in sappers)
                {
                    var sapperLocation = sapper.Location;
                    if (assignments.TryGetValue(sapperLocation, out var assignedMines) && assignedMines.Count > 0)
                    {
                        var path = await pathfinder.FindOptimalPath(sapper.Location, assignedMines);
                        DisplayPath(path);

                        // Розраховуємо загальну вартість шляху
                        double totalCost = await pathfinder.CalculateTotalPathCost(path);
                        DisplayCostAboveSapper(sapper, totalCost); // Відображаємо вартість над сапером
                    }
                }
            }
            else
            {
                MessageBox.Show("Немає призначених мінних зон.");
            }
        }

        private void ClearCostTextBlocks()
        {
            var textBlocks = BingMap.Children.OfType<TextBlock>().ToList();
            foreach (var textBlock in textBlocks)
            {
                BingMap.Children.Remove(textBlock);
            }
        }

        private void DisplayCostAboveSapper(Pushpin sapper, double totalCost)
        {
            var textBlock = new TextBlock
            {
                Text = $"Вартість шляху: {totalCost:F2}",
                Foreground = Brushes.Black,
                Background = Brushes.White,
                FontSize = 10,
                Padding = new Thickness(2)
            };

            // Позиціонуємо TextBlock над сапером
            MapLayer.SetPosition(textBlock, sapper.Location);
            MapLayer.SetPositionOrigin(textBlock, PositionOrigin.BottomCenter);

            BingMap.Children.Add(textBlock);
        }

        public static Location GetKeyFromLocation(Location location)
        {
            var roundedLatitude = Math.Round(location.Latitude, 4);  // Округлюємо до 4 знаків
            var roundedLongitude = Math.Round(location.Longitude, 4);
            return new Location(roundedLatitude, roundedLongitude);
        }

        private void DisplayPath(List<Location> path)
        {
            var polyline = new MapPolyline
            {
                Locations = new LocationCollection(),
                Stroke = Brushes.Blue,
                StrokeThickness = 2
            };

            foreach (var location in path)
            {
                polyline.Locations.Add(location);
            }

            BingMap.Children.Add(polyline);
        }

        private void BingMap_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var mousePosition = e.GetPosition(BingMap);
            var clickedLocation = BingMap.ViewportPointToLocation(mousePosition);

            if (isSelectingArea)
            {
                // Початок виділення зони
                startLocation = clickedLocation;

                currentPolygon = new MapPolygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(32, 255, 0, 0)), // Напівпрозорий червоний колір
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    Locations = new LocationCollection { startLocation, startLocation } // Перші дві точки
                };

                BingMap.Children.Add(currentPolygon); // Додаємо тимчасовий полігон на карту
            }
            else
            {
                // Додавання сапера
                var sapper = new Pushpin
                {
                    Location = clickedLocation,
                    Background = Brushes.Green,
                    ToolTip = "Сапер"
                };

                BingMap.Children.Add(sapper); // Додаємо сапера на карту
                sappers.Add(sapper); // Зберігаємо в список саперів
            }
        }

        private void BingMap_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isSelectingArea && currentPolygon != null)
            {
                // Завершуємо виділення зони
                var mousePosition = e.GetPosition(BingMap);
                var endLocation = BingMap.ViewportPointToLocation(mousePosition);

                // Оновлюємо полігон та зберігаємо
                currentPolygon.Locations = new LocationCollection
                {
                    startLocation,
                    new Location(startLocation.Latitude, endLocation.Longitude),
                    endLocation,
                    new Location(endLocation.Latitude, startLocation.Longitude),
                    startLocation // Замикаємо контур
                };

                selectedZones.Add(currentPolygon); // Зберігаємо в список зон
                currentPolygon = null; // Очищаємо тимчасовий полігон, готуємось до наступного виділення
            }
        }

        private void ClearMap_Click(object sender, RoutedEventArgs e)
        {
            // Видаляємо зони
            foreach (var zone in selectedZones)
            {
                BingMap.Children.Remove(zone);
            }
            selectedZones.Clear();

            // Видаляємо саперів
            foreach (var sapper in sappers)
            {
                BingMap.Children.Remove(sapper);
            }
            sappers.Clear();

            // Видаляємо шляхи
            var polylines = BingMap.Children.OfType<MapPolyline>().ToList();
            foreach (var polyline in polylines)
            {
                BingMap.Children.Remove(polyline);
            }
            ClearCostTextBlocks();
        }

        private void BingMap_MouseMove(object sender, MouseEventArgs e)
        {
            if (isSelectingArea && currentPolygon != null)
            {
                // Оновлюємо тимчасовий полігон за поточним положенням миші
                var mousePosition = e.GetPosition(BingMap);
                var currentLocation = BingMap.ViewportPointToLocation(mousePosition);

                // Очищаємо поточні точки полігону
                currentPolygon.Locations.Clear();

                // Додаємо нові точки полігону
                currentPolygon.Locations.Add(startLocation);
                currentPolygon.Locations.Add(new Location(startLocation.Latitude, currentLocation.Longitude));
                currentPolygon.Locations.Add(currentLocation);
                currentPolygon.Locations.Add(new Location(currentLocation.Latitude, startLocation.Longitude));
                currentPolygon.Locations.Add(startLocation); // Замикаємо контур

                // Примусово оновлюємо відображення полігону
                currentPolygon.InvalidateVisual();
            }

            // Оновлюємо відображення координат
            var position = e.GetPosition(BingMap);
            var location = BingMap.ViewportPointToLocation(position);
            HeightInfo.Text = $"Координати: {location.Latitude:F6}, {location.Longitude:F6}";
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using CoordinateSharp;
using Newtonsoft.Json;
using static Program;

public class Program
{
    public class Waypoint
    {
        public string name { get; set; }
        public string description { get; set; }
        public double lat { get; set; }
        public double lon { get; set; }
        public int altSmoothed { get; set; }
    }

    public class Turnpoint
    {
        public int radius { get; set; }
        public Waypoint waypoint { get; set; }
        public string type { get; set; }
    }

    public class SSS
    {
        public string type { get; set; }
        public string direction { get; set; }
        public List<string> timeGates { get; set; }
    }

    public class Goal
    {
        public string type { get; set; }
        public string deadline { get; set; }
    }

    public class Airspace
    {
        public string Name { get; set; }
        public string Class { get; set; }
        public List<(double Lat, double Lon)> Coordinates { get; set; }
        public string Floor { get; set; }
        public string Ceiling { get; set; }

        public Airspace()
        {
            Coordinates = new List<(double Lat, double Lon)>();
        }
    }

    public class Task
    {
        public int version { get; set; }
        public string taskType { get; set; }
        public string earthModel { get; set; }
        public SSS sss { get; set; }
        public Goal goal { get; set; }
        public List<Turnpoint> turnpoints { get; set; }
    }

    //private static double DegToRad(double degrees)
    //{
    //    return degrees * (Math.PI / 180);
    //}

    //private static double RadToDeg(double radians)
    //{
    //    return radians * (180 / Math.PI);
    //}

    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var c1 = new Coordinate(lat1, lon1);
        var c2 = new Coordinate(lat2, lon2);
        var distance = new Distance(c1, c2);

        return distance.Meters;

        //const double R = 6371; // Earth's radius in km
        //var dLat = DegToRad(lat2 - lat1);
        //var dLon = DegToRad(lon2 - lon1);
        //var a =
        //    Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
        //    Math.Cos(DegToRad(lat1)) * Math.Cos(DegToRad(lat2)) *
        //    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        //var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        //var dist = R * c;
        //return dist;
    }

    private static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        var c1 = new Coordinate(lat1, lon1);
        var c2 = new Coordinate(lat2, lon2);
        var distance = new Distance(c1, c2);

        return distance.Bearing;

        //var y = Math.Sin(DegToRad(lon2 - lon1)) * Math.Cos(DegToRad(lat2));
        //var x = Math.Cos(DegToRad(lat1)) * Math.Sin(DegToRad(lat2)) -
        //        Math.Sin(DegToRad(lat1)) * Math.Cos(DegToRad(lat2)) * Math.Cos(DegToRad(lon2 - lon1));
        //var bearing = RadToDeg(Math.Atan2(y, x));
        //double bearingInDeg = (bearing + 360) % 360;
        //return bearingInDeg;
    }

    private static (double lat, double lon) CalculateDestination(double lat, double lon, double bearing, double distanceInMeters)
    {
        var c1 = new Coordinate(lat, lon);
        c1.Move(distanceInMeters, bearing, Shape.Ellipsoid);
        return (c1.Latitude.DecimalDegree, c1.Longitude.DecimalDegree);

        //const double R = 6371; // Earth's radius in km
        //var angularDistance = distanceInKm / R;
        //var bearingRad = DegToRad(bearing);

        //var lat1 = DegToRad(lat);
        //var lon1 = DegToRad(lon);

        //var lat2 = Math.Asin(
        //    Math.Sin(lat1) * Math.Cos(angularDistance) +
        //    Math.Cos(lat1) * Math.Sin(angularDistance) * Math.Cos(bearingRad)
        //);

        //var lon2 = lon1 + Math.Atan2(
        //    Math.Sin(bearingRad) * Math.Sin(angularDistance) * Math.Cos(lat1),
        //    Math.Cos(angularDistance) - Math.Sin(lat1) * Math.Sin(lat2)
        //);

        //var lat2Deg = RadToDeg(lat2);
        //var lon2Deg = RadToDeg(lon2);
        //return (lat2Deg, lon2Deg);
    }

    private static Task TransformTask(Task task, double newStartLat, double newStartLon, double newHeading)
    {
        var oldStart = task.turnpoints[0].waypoint;
        var newTurnpoints = new List<Turnpoint>();

        // Transform the start point
        newTurnpoints.Add(new Turnpoint
        {
            radius = task.turnpoints[0].radius,
            waypoint = new Waypoint
            {
                name = oldStart.name,
                description = oldStart.description,
                lat = newStartLat,
                lon = newStartLon,
                altSmoothed = oldStart.altSmoothed
            },
            type = task.turnpoints[0].type
        });

        // Calculate the rotation angle
        var oldFirstLegBearing = CalculateBearing(task.turnpoints[0].waypoint.lat, task.turnpoints[0].waypoint.lon,
                                                  task.turnpoints[1].waypoint.lat, task.turnpoints[1].waypoint.lon);
        var rotationAngle = newHeading - oldFirstLegBearing;

        for (int i = 1; i < task.turnpoints.Count; i++)
        {
            var oldPrev = task.turnpoints[i - 1].waypoint;
            var oldCurrent = task.turnpoints[i].waypoint;
            var newPrev = newTurnpoints[i - 1].waypoint;

            // Calculate distance and bearing from previous to current point
            var distance = CalculateDistance(oldPrev.lat, oldPrev.lon, oldCurrent.lat, oldCurrent.lon);
            var oldBearing = CalculateBearing(oldPrev.lat, oldPrev.lon, oldCurrent.lat, oldCurrent.lon);

            // Apply rotation to the bearing
            var newBearing = (oldBearing + rotationAngle + 360) % 360;

            // Calculate new position
            var (newLat, newLon) = CalculateDestination(newPrev.lat, newPrev.lon, newBearing, distance);

            var type = task.turnpoints[i].type;

            newTurnpoints.Add(new Turnpoint
            {
                radius = task.turnpoints[i].radius,
                waypoint = new Waypoint
                {
                    name = oldCurrent.name,
                    description = oldCurrent.description,
                    lat = newLat,
                    lon = newLon,
                    altSmoothed = oldCurrent.altSmoothed
                },
                type = task.turnpoints[i].type
            });
        }

        return new Task
        {
            version = task.version,
            taskType = task.taskType,
            earthModel = task.earthModel,
            sss = task.sss,
            goal = task.goal,
            turnpoints = newTurnpoints
        };
    }

    private static string ConvertToCup(Task task)
    {
        var cupLines = new List<string>
        {
            "name,code,country,lat,lon,elev,style,rwdir,rwlen,freq,desc"
        };

        foreach (var turnpoint in task.turnpoints)
        {
            var wp = turnpoint.waypoint;
            var lat = $"{Math.Abs(wp.lat):00}{Math.Abs(wp.lat % 1 * 60):00.000}{(wp.lat >= 0 ? "N" : "S")}";
            var lon = $"{Math.Abs(wp.lon):000}{Math.Abs(wp.lon % 1 * 60):00.000}{(wp.lon >= 0 ? "E" : "W")}";
            cupLines.Add($"\"{wp.name}\",{wp.name},,{lat},{lon},{wp.altSmoothed}m,1,,,,");
        }

        cupLines.Add("-----Related Tasks-----");
        var taskLine = $"\"{task.taskType}\",\"\",{string.Join(",", task.turnpoints.Select(tp => $"\"{tp.waypoint.name}\""))},\"\"";
        cupLines.Add(taskLine);

        cupLines.Add("Options,GoalIsLine=True,Competition=True");

        for (int i = 0; i < task.turnpoints.Count; i++)
        {
            var tp = task.turnpoints[i];
            var obsZoneLine = $"ObsZone={i},R1={tp.radius}m";
            if (tp.type == "SSS") obsZoneLine += ",sss=True";
            if (tp.type == "ESS") obsZoneLine += ",ess=True,Line=True";
            cupLines.Add(obsZoneLine);
        }

        return string.Join("\n", cupLines);
    }

    static bool TryParseWaypoint(string waypoint, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;

        Coordinate c;
        if (Coordinate.TryParse(waypoint, out c))
        {
            lat = c.Latitude.DecimalDegree;
            lon = c.Longitude.DecimalDegree;
            return true;
        }

        return false;
        
        //// Split the input string by comma
        //string[] parts = waypoint.Split(',');

        //// Check if we have exactly 2 parts
        //if (parts.Length != 2)
        //{
        //    return false;
        //}

        //// Trim whitespace and try to parse both parts as doubles
        //if (double.TryParse(parts[0].Trim(), out lat) && double.TryParse(parts[1].Trim(), out lon))
        //{
        //    return true;
        //}

        //return false;
    }

    public static void Main(string[] args)
    {
        string outputFormat;
        if (args.Length < 1)
        {
            Console.WriteLine("Please specify the output format for the task: 'xctsk' or 'cup' (Default: 'cup'");
            outputFormat = "cup";
        }
        else
        {
            outputFormat = args[0].ToLower();
        }

        if (outputFormat != "xctsk" && outputFormat != "cup")
        {
            Console.WriteLine("Unknown format requested. Please use either 'xctsk' or 'cup'. DEFAULTING TO CUP");
            outputFormat = "cup";
        }

        // Read the input task file
        Console.Write("Please enter task filename (.xctsk): ");
        string taskFilename = Console.ReadLine();
        if (!File.Exists(taskFilename))
        {
            Console.WriteLine("Task file not found.");
            return;
        }

        var inputJson = File.ReadAllText(taskFilename);
        var inputTask = JsonConvert.DeserializeObject<Task>(inputJson);

        // Read the input airspace file
        Console.Write("Please enter airspace filename (.txt): ");
        string airspaceFilename = Console.ReadLine();
        if (!File.Exists(airspaceFilename))
        {
            Console.WriteLine("Airspace file not found.");
            return;
        }

        var airspaces = ParseOpenAir(airspaceFilename);

        Console.Write("Please enter lat, long for the new starting location from Google Maps (nn.nnnnn, m.mmmmm):");
        if (TryParseWaypoint(Console.ReadLine(), out double lat, out double lon))
        {
            Console.WriteLine($"Valid waypoint: lat = {lat}, long = {lon}");
        }
        else
        {
            Console.WriteLine("Invalid waypoint format.");
            return;
        }

        Console.Write("Please enter new departure direction in degrees: ");
        if (!double.TryParse(Console.ReadLine(), out double departureDegrees))
        {
            Console.WriteLine("Invalid departure degrees. Please enter a valid number.");
            return;
        }

        // Transform the task
        var transformedTask = TransformTask(inputTask, lat, lon, departureDegrees);

        // Transform the airspaces
        var oldStartLat = inputTask.turnpoints[0].waypoint.lat;
        var oldStartLon = inputTask.turnpoints[0].waypoint.lon;
        var transformedAirspaces = TransformAirspaces(airspaces, oldStartLat, oldStartLon, lat, lon, departureDegrees);

        // Write the output files
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmm");

        if (outputFormat == "xctsk")
        {
            string outputFilename = $"TransformedTask_{timestamp}.xctsk";
            File.WriteAllText(outputFilename, JsonConvert.SerializeObject(transformedTask, Formatting.Indented));
            Console.WriteLine($"Tasks transformed and saved to {outputFilename}");
        }
        else if (outputFormat == "cup")
        {
            string outputFilename = $"TransformedTask_{timestamp}.cup";
            File.WriteAllText(outputFilename, ConvertToCup(transformedTask));
            Console.WriteLine($"Tasks transformed and saved to {outputFilename}");
        }

        // Write the transformed airspaces
        string airspaceOutputFilename = $"TransformedAirspaces_{timestamp}.txt";
        WriteOpenAir(transformedAirspaces, airspaceOutputFilename);
        Console.WriteLine($"Airspaces transformed and saved to {airspaceOutputFilename}");
    }

    /// <summary>
    /// FROM HERE THE CODE RELATES TO AIRSPACE HANDLING
    /// </summary>
    /// 

    private static List<Airspace> ParseOpenAir(string filename)
    {
        var airspaces = new List<Airspace>();
        Airspace currentAirspace = null;

        foreach (var line in File.ReadLines(filename))
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

            if (trimmedLine.StartsWith("AC"))
            {
                if (currentAirspace != null) 
                    airspaces.Add(currentAirspace);
                currentAirspace = new Airspace { Class = trimmedLine.Substring(3) };
            }
            else if (trimmedLine.StartsWith("AN")) currentAirspace.Name = trimmedLine.Substring(3);
            else if (trimmedLine.StartsWith("AL")) currentAirspace.Floor = trimmedLine.Substring(3);
            else if (trimmedLine.StartsWith("AH")) currentAirspace.Ceiling = trimmedLine.Substring(3);
            else if (trimmedLine.StartsWith("DP"))
            {
                var separator = trimmedLine.Contains("N") ? 'N' : 'S';
                var parts = trimmedLine.Substring(3).Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
                parts[0] = parts[0] + separator;
                
                if (parts.Length == 2)
                {
                    if (ParseDmsCoordinate(parts[0], out double lat) &&
                        ParseDmsCoordinate(parts[1], out double lon))
                    {
                        currentAirspace.Coordinates.Add((lat, lon));
                    }
                }
            }
        }

        if (currentAirspace != null) airspaces.Add(currentAirspace);
        return airspaces;
    }

    private static bool ParseDmsCoordinate(string dmsWithHemisphere, out double result)
    {
        result = 0;
        string dms = dmsWithHemisphere.Substring(0, dmsWithHemisphere.Length - 1).Trim();
        char hemisphere = dmsWithHemisphere[dmsWithHemisphere.Length - 1];

        var parts = dms.Split(':');
        if (parts.Length != 3) return false;

        if (int.TryParse(parts[0], out int degrees) &&
            int.TryParse(parts[1], out int minutes) &&
            double.TryParse(parts[2], out double seconds))
        {
            result = degrees + minutes / 60.0 + seconds / 3600.0;

            if (hemisphere == 'S' || hemisphere == 'W')
            {
                result = -result;
            }

            return true;
        }

        return false;
    }

    private static List<Airspace> TransformAirspaces(List<Airspace> airspaces,
    double oldStartLat, double oldStartLon,
    double newStartLat, double newStartLon, double newHeading)
    {
        var transformedAirspaces = new List<Airspace>();

        // Calculate the rotation angle
        var firstAirspaceCoord = airspaces[0].Coordinates[0];
        var oldFirstLegBearing = CalculateBearing(oldStartLat, oldStartLon, firstAirspaceCoord.Lat, firstAirspaceCoord.Lon);
        var rotationAngle = newHeading - oldFirstLegBearing;

        foreach (var airspace in airspaces)
        {
            var transformedAirspace = new Airspace
            {
                Name = airspace.Name,
                Class = airspace.Class,
                Floor = airspace.Floor,
                Ceiling = airspace.Ceiling
            };

            var prevLat = oldStartLat;
            var prevLon = oldStartLon;
            var newPrevLat = newStartLat;
            var newPrevLon = newStartLon;

            foreach (var (lat, lon) in airspace.Coordinates)
            {
                // Calculate distance and bearing from previous to current point
                var distance = CalculateDistance(prevLat, prevLon, lat, lon);
                var oldBearing = CalculateBearing(prevLat, prevLon, lat, lon);

                // Apply rotation to the bearing
                var newBearing = (oldBearing + rotationAngle + 360) % 360;

                // Calculate new position
                var (newLat, newLon) = CalculateDestination(newPrevLat, newPrevLon, newBearing, distance);

                transformedAirspace.Coordinates.Add((newLat, newLon));

                // Update previous coordinates for the next iteration
                prevLat = lat;
                prevLon = lon;
                newPrevLat = newLat;
                newPrevLon = newLon;
            }

            transformedAirspaces.Add(transformedAirspace);
        }

        return transformedAirspaces;
    }

    private static void WriteOpenAir(List<Airspace> airspaces, string filename)
    {
        using (var writer = new StreamWriter(filename))
        {
            foreach (var airspace in airspaces)
            {
                writer.WriteLine($"AC {airspace.Class}");
                writer.WriteLine($"AN {airspace.Name}");
                writer.WriteLine($"AL {airspace.Floor}");
                writer.WriteLine($"AH {airspace.Ceiling}");
                foreach (var (lat, lon) in airspace.Coordinates)
                {
                    writer.WriteLine($"DP {lat:F6} {lon:F6}");
                }
                writer.WriteLine();
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
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

    public class Task
    {
        public int version { get; set; }
        public string taskType { get; set; }
        public string earthModel { get; set; }
        public SSS sss { get; set; }
        public Goal goal { get; set; }
        public List<Turnpoint> turnpoints { get; set; }
    }

    private static double DegToRad(double degrees)
    {
        return degrees * (Math.PI / 180);
    }

    private static double RadToDeg(double radians)
    {
        return radians * (180 / Math.PI);
    }

    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371; // Earth's radius in km
        var dLat = DegToRad(lat2 - lat1);
        var dLon = DegToRad(lon2 - lon1);
        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(DegToRad(lat1)) * Math.Cos(DegToRad(lat2)) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        var y = Math.Sin(DegToRad(lon2 - lon1)) * Math.Cos(DegToRad(lat2));
        var x = Math.Cos(DegToRad(lat1)) * Math.Sin(DegToRad(lat2)) -
                Math.Sin(DegToRad(lat1)) * Math.Cos(DegToRad(lat2)) * Math.Cos(DegToRad(lon2 - lon1));
        var bearing = RadToDeg(Math.Atan2(y, x));
        return (bearing + 360) % 360;
    }

    private static (double lat, double lon) CalculateDestination(double lat, double lon, double bearing, double distance)
    {
        const double R = 6371; // Earth's radius in km
        var angularDistance = distance / R;
        var bearingRad = DegToRad(bearing);

        var lat1 = DegToRad(lat);
        var lon1 = DegToRad(lon);

        var lat2 = Math.Asin(
            Math.Sin(lat1) * Math.Cos(angularDistance) +
            Math.Cos(lat1) * Math.Sin(angularDistance) * Math.Cos(bearingRad)
        );

        var lon2 = lon1 + Math.Atan2(
            Math.Sin(bearingRad) * Math.Sin(angularDistance) * Math.Cos(lat1),
            Math.Cos(angularDistance) - Math.Sin(lat1) * Math.Sin(lat2)
        );

        return (RadToDeg(lat2), RadToDeg(lon2));
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

        // Split the input string by comma
        string[] parts = waypoint.Split(',');

        // Check if we have exactly 2 parts
        if (parts.Length != 2)
        {
            return false;
        }

        // Trim whitespace and try to parse both parts as doubles
        if (double.TryParse(parts[0].Trim(), out lat) && double.TryParse(parts[1].Trim(), out lon))
        {
            return true;
        }

        return false;
    }

    public static void Main(string[] args)
    {
        string outputFormat;
        if (args.Length < 1)
        {
            Console.WriteLine("Please specify the output format: xctsk or cup. Defaulting to .cup format.");
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

        // Read the input file
        Console.Write("Please enter filename: ");
        string filename = Console.ReadLine();
        if (!File.Exists(filename))
        {
            Console.WriteLine("File not found.");
            return;
        }

        var inputJson = File.ReadAllText(filename);
        var inputTask = JsonConvert.DeserializeObject<Task>(inputJson);

        Console.Write("Please enter latitude, longitude from Google Maps (nn.nnnnn, m.mmmmm): ");
        if (!TryParseWaypoint(Console.ReadLine(), out double lat, out double lon))
        {
            Console.WriteLine($"Valid waypoint: lat = {lat}, long = {lon}");
        }
        else
        {
            Console.WriteLine("Invalid waypoint format.");
            return;
        }

        //Console.Write("Please enter latitude from Google Maps (nn.nnnnn): ");
        //if (!double.TryParse(Console.ReadLine(), out double lat))
        //{
        //    Console.WriteLine("Invalid latitude. Please enter a valid number.");
        //    return;
        //}

        //Console.Write("Please enter longitude from Google Maps (n.nnnnn): ");
        //if (!double.TryParse(Console.ReadLine(), out double lon))
        //{
        //    Console.WriteLine("Invalid longitude. Please enter a valid number.");
        //    return;
        //}

        Console.Write("Please enter departure degrees: ");
        if (!double.TryParse(Console.ReadLine(), out double departureDegrees))
        {
            Console.WriteLine("Invalid departure degrees. Please enter a valid number.");
            return;
        }

        // Transform the task
        //var transformedTask = TransformTask(inputTask, 47.19648, 9.09960, 45);
        var transformedTask = TransformTask(inputTask, lat, lon, departureDegrees);

        // Write the output files
        if (outputFormat == "xctsk")
        {
            File.WriteAllText("transformed_task.xctsk", JsonConvert.SerializeObject(transformedTask, Formatting.Indented));
            Console.WriteLine("Tasks transformed and saved to transformed_task.xctsk");
        }
        else if(outputFormat == "cup") // cup format
        {
            File.WriteAllText("transformed_task.cup", ConvertToCup(transformedTask));
            Console.WriteLine("Tasks transformed and saved to transformed_task.cup");
        }
    }
}
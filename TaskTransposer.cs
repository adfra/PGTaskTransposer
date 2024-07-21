using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

public class Program
{
    public class Waypoint
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public int AltSmoothed { get; set; }
    }

    public class Turnpoint
    {
        public int Radius { get; set; }
        public Waypoint Waypoint { get; set; }
        public string Type { get; set; }
    }

    public class SSS
    {
        public string Type { get; set; }
        public string Direction { get; set; }
        public List<string> TimeGates { get; set; }
    }

    public class Goal
    {
        public string Type { get; set; }
        public string Deadline { get; set; }
    }

    public class Task
    {
        public int Version { get; set; }
        public string TaskType { get; set; }
        public string EarthModel { get; set; }
        public SSS Sss { get; set; }
        public Goal Goal { get; set; }
        public List<Turnpoint> Turnpoints { get; set; }
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

    private static Task TransformTaskOLD(Task task, double newStartLat, double newStartLon, double newHeading)
    {
        var oldStart = task.Turnpoints[0].Waypoint;

        var transformedTurnpoints = new List<Turnpoint>();
        for (int i = 0; i < task.Turnpoints.Count; i++)
        {
            var tp = task.Turnpoints[i];
            if (i == 0)
            {
                // The start point
                transformedTurnpoints.Add(new Turnpoint
                {
                    Radius = tp.Radius,
                    Waypoint = new Waypoint
                    {
                        Name = tp.Waypoint.Name,
                        Description = tp.Waypoint.Description,
                        Lat = newStartLat,
                        Lon = newStartLon,
                        AltSmoothed = tp.Waypoint.AltSmoothed
                    },
                    Type = tp.Type
                });
            }
            else if (i == 1)
            {
                // The second point (end of first leg)
                var distance = CalculateDistance(oldStart.Lat, oldStart.Lon, tp.Waypoint.Lat, tp.Waypoint.Lon);
                var (newLat, newLon) = CalculateDestination(newStartLat, newStartLon, newHeading, distance);
                transformedTurnpoints.Add(new Turnpoint
                {
                    Radius = tp.Radius,
                    Waypoint = new Waypoint
                    {
                        Name = tp.Waypoint.Name,
                        Description = tp.Waypoint.Description,
                        Lat = newLat,
                        Lon = newLon,
                        AltSmoothed = tp.Waypoint.AltSmoothed
                    },
                    Type = tp.Type
                });
            }
            else
            {
                // All subsequent points
                var distance = CalculateDistance(oldStart.Lat, oldStart.Lon, tp.Waypoint.Lat, tp.Waypoint.Lon);
                var oldBearing = CalculateBearing(oldStart.Lat, oldStart.Lon, tp.Waypoint.Lat, tp.Waypoint.Lon);
                var oldFirstLegBearing = CalculateBearing(task.Turnpoints[0].Waypoint.Lat, task.Turnpoints[0].Waypoint.Lon,
                                                          task.Turnpoints[1].Waypoint.Lat, task.Turnpoints[1].Waypoint.Lon);

                // Calculate the angle relative to the first leg
                var relativeAngle = (oldBearing - oldFirstLegBearing + 360) % 360;

                // Apply this relative angle to the new heading
                var newBearing = (newHeading + relativeAngle + 360) % 360;

                var (newLat, newLon) = CalculateDestination(newStartLat, newStartLon, newBearing, distance);

                transformedTurnpoints.Add(new Turnpoint
                {
                    Radius = tp.Radius,
                    Waypoint = new Waypoint
                    {
                        Name = tp.Waypoint.Name,
                        Description = tp.Waypoint.Description,
                        Lat = newLat,
                        Lon = newLon,
                        AltSmoothed = tp.Waypoint.AltSmoothed
                    },
                    Type = tp.Type
                });
            }
        }

        return new Task
        {
            Version = task.Version,
            TaskType = task.TaskType,
            EarthModel = task.EarthModel,
            Sss = task.Sss,
            Goal = task.Goal,
            Turnpoints = transformedTurnpoints
        };
    }

    private static Task TransformTask(Task task, double newStartLat, double newStartLon, double newHeading)
    {
        var oldStart = task.Turnpoints[0].Waypoint;
        var newTurnpoints = new List<Turnpoint>();

        // Transform the start point
        newTurnpoints.Add(new Turnpoint
        {
            Radius = task.Turnpoints[0].Radius,
            Waypoint = new Waypoint
            {
                Name = oldStart.Name,
                Description = oldStart.Description,
                Lat = newStartLat,
                Lon = newStartLon,
                AltSmoothed = oldStart.AltSmoothed
            },
            Type = task.Turnpoints[0].Type
        });

        // Calculate the rotation angle
        var oldFirstLegBearing = CalculateBearing(task.Turnpoints[0].Waypoint.Lat, task.Turnpoints[0].Waypoint.Lon,
                                                  task.Turnpoints[1].Waypoint.Lat, task.Turnpoints[1].Waypoint.Lon);
        var rotationAngle = newHeading - oldFirstLegBearing;

        for (int i = 1; i < task.Turnpoints.Count; i++)
        {
            var oldPrev = task.Turnpoints[i - 1].Waypoint;
            var oldCurrent = task.Turnpoints[i].Waypoint;
            var newPrev = newTurnpoints[i - 1].Waypoint;

            // Calculate distance and bearing from previous to current point
            var distance = CalculateDistance(oldPrev.Lat, oldPrev.Lon, oldCurrent.Lat, oldCurrent.Lon);
            var oldBearing = CalculateBearing(oldPrev.Lat, oldPrev.Lon, oldCurrent.Lat, oldCurrent.Lon);

            // Apply rotation to the bearing
            var newBearing = (oldBearing + rotationAngle + 360) % 360;

            // Calculate new position
            var (newLat, newLon) = CalculateDestination(newPrev.Lat, newPrev.Lon, newBearing, distance);

            newTurnpoints.Add(new Turnpoint
            {
                Radius = task.Turnpoints[i].Radius,
                Waypoint = new Waypoint
                {
                    Name = oldCurrent.Name,
                    Description = oldCurrent.Description,
                    Lat = newLat,
                    Lon = newLon,
                    AltSmoothed = oldCurrent.AltSmoothed
                },
                Type = task.Turnpoints[i].Type
            });
        }

        return new Task
        {
            Version = task.Version,
            TaskType = task.TaskType,
            EarthModel = task.EarthModel,
            Sss = task.Sss,
            Goal = task.Goal,
            Turnpoints = newTurnpoints
        };
    }

    public static void Main()
    {
        // Read the input file
        var inputJson = File.ReadAllText("SCR_Hüsliberg_v2.xctsk");
        var inputTask = JsonConvert.DeserializeObject<Task>(inputJson);

        // Transform the task
        var transformedTask180 = TransformTask(inputTask, 47.19648, 9.09960, 180);
        var transformedTask45 = TransformTask(inputTask, 47.19648, 9.09960, 45);

        // Write the output files
        File.WriteAllText("transformed_task_180.xctsk", JsonConvert.SerializeObject(transformedTask180, Formatting.Indented));
        File.WriteAllText("transformed_task_45.xctsk", JsonConvert.SerializeObject(transformedTask45, Formatting.Indented));

        Console.WriteLine("Tasks transformed and saved to transformed_task_180.xctsk and transformed_task_45.xctsk");
    }
}
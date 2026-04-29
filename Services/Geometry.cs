using KonturVideo.AI.Inference;
using KonturVideo.AI.Models;

namespace KonturVideo.AI.Services;

public static class Geometry
{
    public static bool PointInPolygon(float x, float y, IReadOnlyList<GeoPointDto> polygon)
    {
        if (polygon.Count < 3) return true;

        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var xi = polygon[i].X; var yi = polygon[i].Y;
            var xj = polygon[j].X; var yj = polygon[j].Y;

            var intersect = ((yi > y) != (yj > y))
                && (x < (xj - xi) * (y - yi) / ((yj - yi) == 0 ? double.Epsilon : (yj - yi)) + xi);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    public static int SideOfLine(float x, float y, IReadOnlyList<GeoPointDto> line)
    {
        if (line.Count != 2) return 0;
        var a = line[0];
        var b = line[1];
        var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
        return cross switch
        {
            > 0 => 1,
            < 0 => -1,
            _ => 0
        };
    }

    public static bool IsInsideRegion(Detection detection, AiRuleDto rule)
        => rule.Region?.Polygon is null || rule.Region.Polygon.Count < 3
            || PointInPolygon(detection.CenterX, detection.CenterY, rule.Region.Polygon);
}

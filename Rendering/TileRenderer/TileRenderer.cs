using Mapster.Common.MemoryMappedTypes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mapster.Rendering;

public static class TileRenderer
{
    public static BaseShape Tessellate(this MapFeatureData feature, ref BoundingBox boundingBox, ref PriorityQueue<BaseShape, int> shapes)
    {
        BaseShape? baseShape = null;

        var featureType = feature.FeatureType;

        if ((featureType & FeatureType.WATERWAY) != 0)
        {
            var coordinates = feature.Coordinates;
            var waterway = new Waterway(coordinates, feature.Type == GeometryType.Polygon);
            baseShape = waterway;
            shapes.Enqueue(waterway, waterway.ZIndex);
        }
        // else if ((featureType & FeatureType.PLACE_NAME) != 0)
        // {
        //     var coordinates = feature.Coordinates;
        //     var popPlace = new PopulatedPlace(coordinates, feature);
        //     baseShape = popPlace;
        //     shapes.Enqueue(popPlace, popPlace.ZIndex);
        // }
        else if ((featureType & FeatureType.HIGHWAY) != 0)
        {
            var coordinates = feature.Coordinates;
            var road = new Road(coordinates);
            baseShape = road;
            shapes.Enqueue(road, road.ZIndex);
        }
        else if ((featureType & FeatureType.BORDER) != 0)
        {
            var coordinates = feature.Coordinates;
            var border = new Border(coordinates);
            baseShape = border;
            shapes.Enqueue(border, border.ZIndex);
        }
        else if ((featureType & FeatureType.RAILWAY) != 0)
        {
            var coordinates = feature.Coordinates;
            var railway = new Railway(coordinates);
            baseShape = railway;
            shapes.Enqueue(railway, railway.ZIndex);
        }
        else if ((featureType & FeatureType.L_FOREST) != 0)
        {
            var coordinates = feature.Coordinates;
            var geoFeature = new GeoFeature(coordinates, GeoFeature.GeoFeatureType.Forest);
            baseShape = geoFeature;
            shapes.Enqueue(geoFeature, geoFeature.ZIndex);
        }
        else if ((featureType & FeatureType.L_PLAIN) != 0)
        {
            var coordinates = feature.Coordinates;
            var geoFeature = new GeoFeature(coordinates, GeoFeature.GeoFeatureType.Plain);
            baseShape = geoFeature;
            shapes.Enqueue(geoFeature, geoFeature.ZIndex);
        }
        else if ((featureType & FeatureType.L_HILLS) != 0)
        {
            var coordinates = feature.Coordinates;
            var geoFeature = new GeoFeature(coordinates, GeoFeature.GeoFeatureType.Hills);
            baseShape = geoFeature;
            shapes.Enqueue(geoFeature, geoFeature.ZIndex); ;
        }
        else if ((featureType & FeatureType.L_MOUNTAINS) != 0)
        {
            var coordinates = feature.Coordinates;
            var geoFeature = new GeoFeature(coordinates, GeoFeature.GeoFeatureType.Mountains);
            baseShape = geoFeature;
            shapes.Enqueue(geoFeature, geoFeature.ZIndex);
        }
        else if ((featureType & FeatureType.L_DESERT) != 0)
        {
            var coordinates = feature.Coordinates;
            var geoFeature = new GeoFeature(coordinates, GeoFeature.GeoFeatureType.Desert);
            baseShape = geoFeature;
            shapes.Enqueue(geoFeature, geoFeature.ZIndex);
        }
        else if ((featureType & FeatureType.L_WATER) != 0)
        {
            var coordinates = feature.Coordinates;
            var geoFeature = new GeoFeature(coordinates, GeoFeature.GeoFeatureType.Water);
            baseShape = geoFeature;
            shapes.Enqueue(geoFeature, geoFeature.ZIndex);
        }
        else if ((featureType & FeatureType.L_RESIDENTIAL) != 0)
        {
            var coordinates = feature.Coordinates;
            var geoFeature = new GeoFeature(coordinates, GeoFeature.GeoFeatureType.Residential);
            baseShape = geoFeature;
            shapes.Enqueue(geoFeature, geoFeature.ZIndex);
        }

        else if ((featureType & FeatureType.BUILDING) != 0)
        {
            var coordinates = feature.Coordinates;
            var geoFeature = new GeoFeature(coordinates, GeoFeature.GeoFeatureType.Residential);
            baseShape = geoFeature;
            shapes.Enqueue(geoFeature, geoFeature.ZIndex);
        }
        else if (((featureType & FeatureType.LANDUSE) != 0))
        {
            var coordinates = feature.Coordinates;
            var geoFeature = new GeoFeature(coordinates, feature);
            baseShape = geoFeature;
            shapes.Enqueue(geoFeature, geoFeature.ZIndex);
        }

        if (baseShape != null)
        {
            for (var j = 0; j < baseShape.ScreenCoordinates.Length; ++j)
            {
                boundingBox.MinX = Math.Min(boundingBox.MinX, baseShape.ScreenCoordinates[j].X);
                boundingBox.MaxX = Math.Max(boundingBox.MaxX, baseShape.ScreenCoordinates[j].X);
                boundingBox.MinY = Math.Min(boundingBox.MinY, baseShape.ScreenCoordinates[j].Y);
                boundingBox.MaxY = Math.Max(boundingBox.MaxY, baseShape.ScreenCoordinates[j].Y);
            }
        }
        return baseShape;
    }

    public static Image<Rgba32> Render(this PriorityQueue<BaseShape, int> shapes, BoundingBox boundingBox, int width, int height)
    {
        var canvas = new Image<Rgba32>(width, height);

        // Calculate the scale for each pixel, essentially applying a normalization
        var scaleX = canvas.Width / (boundingBox.MaxX - boundingBox.MinX);
        var scaleY = canvas.Height / (boundingBox.MaxY - boundingBox.MinY);
        var scale = Math.Min(scaleX, scaleY);

        // Background Fill
        canvas.Mutate(x => x.Fill(Color.White));
        while (shapes.Count > 0)
        {
            var entry = shapes.Dequeue();
            // FIXME: Hack
            if (entry.ScreenCoordinates.Length < 2)
            {
                continue;
            }
            entry.TranslateAndScale(boundingBox.MinX, boundingBox.MinY, scale, canvas.Height);
            canvas.Mutate(x => entry.Render(x));
        }

        return canvas;
    }

    public struct BoundingBox
    {
        public float MinX;
        public float MaxX;
        public float MinY;
        public float MaxY;
    }
}

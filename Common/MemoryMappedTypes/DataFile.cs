using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mapster.Common.MemoryMappedTypes;

/// <summary>
///     Action to be called when iterating over <see cref="MapFeature" /> in a given bounding box via a call to
///     <see cref="DataFile.ForeachFeature" />
/// </summary>
/// <param name="feature">The current <see cref="MapFeature" />.</param>
/// <param name="label">The label of the feature, <see cref="string.Empty" /> if not available.</param>
/// <param name="coordinates">The coordinates of the <see cref="MapFeature" />.</param>
/// <returns></returns>
public delegate bool MapFeatureDelegate(MapFeatureData featureData);

/// <summary>
///     Aggregation of all the data needed to render a map feature
/// </summary>
public readonly ref struct MapFeatureData
{
    public long Id { get; init; }

    public GeometryType Type { get; init; }
    public ReadOnlySpan<char> Label { get; init; }
    public ReadOnlySpan<Coordinate> Coordinates { get; init; }
    public Dictionary<string, string> Properties { get; init; }
    public FeatureType FeatureType { get; init; }
}

/// <summary>
///     Represents a file with map data organized in the following format:<br />
///     <see cref="FileHeader" /><br />
///     Array of <see cref="TileHeaderEntry" /> with <see cref="FileHeader.TileCount" /> records<br />
///     Array of tiles, each tile organized:<br />
///     <see cref="TileBlockHeader" /><br />
///     Array of <see cref="MapFeature" /> with <see cref="TileBlockHeader.FeaturesCount" /> at offset
///     <see cref="TileHeaderEntry.OffsetInBytes" /> + size of <see cref="TileBlockHeader" /> in bytes.<br />
///     Array of <see cref="Coordinate" /> with <see cref="TileBlockHeader.CoordinatesCount" /> at offset
///     <see cref="TileBlockHeader.CharactersOffsetInBytes" />.<br />
///     Array of <see cref="StringEntry" /> with <see cref="TileBlockHeader.StringCount" /> at offset
///     <see cref="TileBlockHeader.StringsOffsetInBytes" />.<br />
///     Array of <see cref="char" /> with <see cref="TileBlockHeader.CharactersCount" /> at offset
///     <see cref="TileBlockHeader.CharactersOffsetInBytes" />.<br />
/// </summary>
public unsafe class DataFile : IDisposable
{
    private readonly FileHeader* _fileHeader;
    private readonly MemoryMappedViewAccessor _mma;
    private readonly MemoryMappedFile _mmf;

    private readonly byte* _ptr;
    private readonly int CoordinateSizeInBytes = Marshal.SizeOf<Coordinate>();
    private readonly int FileHeaderSizeInBytes = Marshal.SizeOf<FileHeader>();
    private readonly int MapFeatureSizeInBytes = Marshal.SizeOf<MapFeature>();
    private readonly int StringEntrySizeInBytes = Marshal.SizeOf<StringEntry>();
    private readonly int TileBlockHeaderSizeInBytes = Marshal.SizeOf<TileBlockHeader>();
    private readonly int TileHeaderEntrySizeInBytes = Marshal.SizeOf<TileHeaderEntry>();

    private bool _disposedValue;

    public DataFile(string path)
    {
        _mmf = MemoryMappedFile.CreateFromFile(path);
        _mma = _mmf.CreateViewAccessor();
        _mma.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);
        _fileHeader = (FileHeader*)_ptr;
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _mma?.SafeMemoryMappedViewHandle.ReleasePointer();
                _mma?.Dispose();
                _mmf?.Dispose();
            }

            _disposedValue = true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private TileHeaderEntry* GetNthTileHeader(int i)
    {
        return (TileHeaderEntry*)(_ptr + i * TileHeaderEntrySizeInBytes + FileHeaderSizeInBytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private (TileBlockHeader? Tile, ulong TileOffset) GetTile(int tileId)
    {
        ulong tileOffset = 0;
        for (var i = 0; i < _fileHeader->TileCount; ++i)
        {
            var tileHeaderEntry = GetNthTileHeader(i);
            if (tileHeaderEntry->ID == tileId)
            {
                tileOffset = tileHeaderEntry->OffsetInBytes;
                return (*(TileBlockHeader*)(_ptr + tileOffset), tileOffset);
            }
        }

        return (null, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private MapFeature* GetFeature(int i, ulong offset)
    {
        return (MapFeature*)(_ptr + offset + TileBlockHeaderSizeInBytes + i * MapFeatureSizeInBytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private ReadOnlySpan<Coordinate> GetCoordinates(ulong coordinateOffset, int ithCoordinate, int coordinateCount)
    {
        return new ReadOnlySpan<Coordinate>(_ptr + coordinateOffset + ithCoordinate * CoordinateSizeInBytes, coordinateCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private void GetString(ulong stringsOffset, ulong charsOffset, int i, out ReadOnlySpan<char> value)
    {
        var stringEntry = (StringEntry*)(_ptr + stringsOffset + i * StringEntrySizeInBytes);
        value = new ReadOnlySpan<char>(_ptr + charsOffset + stringEntry->Offset * 2, stringEntry->Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private void GetProperty(ulong stringsOffset, ulong charsOffset, int i, out ReadOnlySpan<char> key, out ReadOnlySpan<char> value)
    {
        if (i % 2 != 0)
        {
            throw new ArgumentException("Properties are key-value pairs and start at even indices in the string list (i.e. i % 2 == 0)");
        }

        GetString(stringsOffset, charsOffset, i, out key);
        GetString(stringsOffset, charsOffset, i + 1, out value);
    }

    public static FeatureType classifyFeature(IDictionary<string, string> properties, GeometryType geometryType)
    {
        if (properties.Any(p => p.Key == "highway"))
        {
            switch (properties["highway"])
            {
                case "motorway":
                    return FeatureType.H_MOTORWAY;
                case "trunk":
                    return FeatureType.H_TRUNK;
                case "primary":
                    return FeatureType.H_PRIMARY;
                case "secondary":
                    return FeatureType.H_SECONDARY;
                case "tertiary":
                    return FeatureType.H_TERTIARY;
                case "residential":
                    return FeatureType.H_RESIDENTIAL;
                case "living_street":
                    return FeatureType.H_RESIDENTIAL;
                default:
                    return FeatureType.UNKNOWN;
            }
        }
        else if (properties.Any(p => p.Key.StartsWith("water")) && geometryType != GeometryType.Point)
        {
            return FeatureType.WATERWAY;
        }
        else if (properties.Any(p => p.Key == "railway"))
        {
            return FeatureType.RAILWAY;
        }
        else if (properties.Any(p => p.Key.StartsWith("boundary") && p.Value.StartsWith("administrative") && properties.Any(p => p.Key.StartsWith("admin_level") && p.Value == "2")))
        {
            return FeatureType.BORDER;
        }
        // else if (geometryType != GeometryType.Point && properties.Any(p => p.Key.StartsWith("place") && new List<string> { "city", "town", "locality", "hamlet" }.Contains(p.Value)))
        // {
        //     return FeatureType.PLACE_NAME;
        // }
        else if (properties.Any(p => p.Key.StartsWith("boundary") && p.Value.StartsWith("forest")))
        {
            return FeatureType.L_FOREST;
        }
        else if (properties.Any(p => p.Key.StartsWith("landuse") && (p.Value.StartsWith("forest") || p.Value.StartsWith("orchard"))))
        {
            return FeatureType.L_FOREST;
        }
        else if (geometryType == GeometryType.Polygon && properties.Any(p => p.Key.StartsWith("landuse")) &&
            new List<string> { "residential", "cemetery", "industrial", "commercial", "square", "construction", "military", "quarry", "brownfield" }.Contains(properties["landuse"]))
        {
            return FeatureType.L_RESIDENTIAL;
        }
        else if (geometryType == GeometryType.Polygon && properties.Any(p => p.Key.StartsWith("landuse")) &&
            new List<string> { "farm", "meadow", "grass", "greenfield", "recreation_ground", "winter_sports", "allotments" }.Contains(properties["landuse"]))
        {
            return FeatureType.L_PLAIN;
        }
        else if (geometryType == GeometryType.Polygon && properties.Any(p => p.Key.StartsWith("landuse")) &&
            new List<string> { "reservoir", "basin" }.Contains(properties["landuse"]))
        {
            return FeatureType.L_WATER;
        }
        else if (geometryType == GeometryType.Polygon && properties.Any(p => p.Key.StartsWith("building")))
        {
            return FeatureType.L_RESIDENTIAL;
        }
        else if (geometryType == GeometryType.Polygon && properties.Any(p => p.Key.StartsWith("leisure")))
        {
            return FeatureType.L_RESIDENTIAL;
        }
        else if (geometryType == GeometryType.Polygon && properties.Any(p => p.Key.StartsWith("amenity")))
        {
            return FeatureType.L_RESIDENTIAL;
        }
        else if (geometryType == GeometryType.Polygon && properties.Any(p => p.Key.StartsWith("natural")))
        {
            if (new List<string> { "fell", "grassland", "heath", "moor", "scrub", "wetland" }.Contains(properties["natural"]))
            {
                return FeatureType.L_PLAIN;
            }
            else if (new List<string> { "wood", "tree_row" }.Contains(properties["natural"]))
            {
                return FeatureType.L_FOREST;
            }
            else if (new List<string> { "bare_rock", "rock", "scree" }.Contains(properties["natural"]))
            {
                return FeatureType.L_MOUNTAINS;
            }
            else if (new List<string> { "sand", "beach" }.Contains(properties["natural"]))
            {
                return FeatureType.L_DESERT;
            }
            else if (properties["natural"] == "water")
            {
                return FeatureType.L_WATER;
            }
            else return FeatureType.L_NATURAL;
        }

        return FeatureType.UNKNOWN;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void ForeachFeature(BoundingBox b, MapFeatureDelegate? action)
    {
        if (action == null)
        {
            return;
        }

        var tiles = TiligSystem.GetTilesForBoundingBox(b.MinLat, b.MinLon, b.MaxLat, b.MaxLon);
        for (var i = 0; i < tiles.Length; ++i)
        {
            var header = GetTile(tiles[i]);
            if (header.Tile == null)
            {
                continue;
            }
            for (var j = 0; j < header.Tile.Value.FeaturesCount; ++j)
            {
                var feature = GetFeature(j, header.TileOffset);
                var coordinates = GetCoordinates(header.Tile.Value.CoordinatesOffsetInBytes, feature->CoordinateOffset, feature->CoordinateCount);
                var isFeatureInBBox = false;

                for (var k = 0; k < coordinates.Length; ++k)
                {
                    if (b.Contains(coordinates[k]))
                    {
                        isFeatureInBBox = true;
                        break;
                    }
                }

                var label = ReadOnlySpan<char>.Empty;
                if (feature->LabelOffset >= 0)
                {
                    GetString(header.Tile.Value.StringsOffsetInBytes, header.Tile.Value.CharactersOffsetInBytes, feature->LabelOffset, out label);
                }

                if (isFeatureInBBox)
                {
                    var properties = new Dictionary<string, string>(feature->PropertyCount);
                    for (var p = 0; p < feature->PropertyCount; ++p)
                    {
                        GetProperty(header.Tile.Value.StringsOffsetInBytes, header.Tile.Value.CharactersOffsetInBytes, p * 2 + feature->PropertiesOffset, out var key, out var value);
                        properties.Add(key.ToString(), value.ToString());
                    }

                    if (!action(new MapFeatureData
                    {
                        Id = feature->Id,
                        Label = label,
                        Coordinates = coordinates,
                        Type = feature->GeometryType,
                        FeatureType = classifyFeature(properties, feature->GeometryType)
                    }))
                    {
                        break;
                    }
                }
            }
        }
    }
}

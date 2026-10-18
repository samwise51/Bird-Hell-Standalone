using System.Text;
using BirdHell.Systems;
using BirdHell.World.Geometry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NumericalVector2 = System.Numerics.Vector2;

namespace BirdHell.Presentation;

public enum CaveBoundaryCategory
{
    Floor,
    Ceiling,
    WallLeft,
    WallRight,
    Slope,
}

public readonly record struct CaveBoundarySegment(
    int OutlineIndex,
    int SegmentIndex,
    NumericalVector2 Start,
    NumericalVector2 End,
    NumericalVector2 Tangent,
    NumericalVector2 OpenNormal,
    float Length,
    CaveBoundaryCategory Category);

public readonly record struct CaveDecorationInstance(
    NumericalVector2 Position,
    NumericalVector2 Tangent,
    NumericalVector2 OpenNormal,
    int AssetVariant);

/// <summary>Presentation-only world-space material, edge, and decoration mapping for authoritative cave geometry.</summary>
public sealed class CaveMaterialPresentation : IDisposable
{
    public const string TexturePath = "Sprites/Terrain/cave_base_material_v1.png";
    public const string SurfaceCapTexturePath = "Sprites/Terrain/cave_surface_cap_v1.png";
    public const string CeilingCapTexturePath = "Sprites/Terrain/cave_ceiling_cap_v1.png";
    public const string WallEdgeTexturePath = "Sprites/Terrain/cave_wall_edge_v1.png";
    public const string BoulderTexturePath = "Sprites/Terrain/cave_boulder_small_v1.png";
    public const float MaterialWorldWidth = 6f;
    public const float MaterialWorldHeight = 6f;
    public const float SurfaceCapWorldRepeatLength = 2.75f;
    public const float SurfaceCapOpenOverhang = 0.18f;
    public const float SurfaceCapSolidDepth = 0.62f;
    public const float CeilingCapWorldRepeatLength = 3.25f;
    public const float CeilingCapOpenOverhang = 0.55f;
    public const float CeilingCapSolidDepth = 0.30f;
    public const float WallEdgeWorldRepeatLength = 3.50f;
    public const float WallEdgeOpenOverhang = 0.14f;
    public const float WallEdgeSolidDepth = 0.55f;
    public const float MinimumSurfaceOpenNormalY = 0.35f;
    public const float MaximumCeilingOpenNormalY = -0.45f;
    public const float MinimumWallOpenNormalX = 0.80f;
    public const float MaximumWallOpenNormalY = 0.35f;
    public const float BoundaryClassificationOffset = 0.05f;
    public const float BoulderMinimumOpenNormalY = 0.80f;
    public const float BoulderMinimumSegmentLength = 1.25f;
    public const float BoulderCornerClearance = 0.45f;
    public const float BoulderMinimumSpacing = 7.5f;
    public const float BoulderSpawnClearance = 4f;
    public const float BoulderWorldWidth = 0.85f;
    public const float BoulderWorldHeight = 0.55f;
    public const float BoulderSolidInset = 0.20f;
    public const int BoulderMaximumCount = 6;
    public const int BoulderProbabilityPermille = 450;
    public const TextureFilter Filter = TextureFilter.Linear;
    public const TextureAddressMode AddressU = TextureAddressMode.Wrap;
    public const TextureAddressMode AddressV = TextureAddressMode.Wrap;
    public const TextureAddressMode SurfaceCapAddressV = TextureAddressMode.Clamp;

    public CaveMaterialPresentation(GraphicsDevice graphicsDevice, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        Texture = LoadTexture(graphicsDevice, contentRoot, TexturePath);
        SurfaceCapTexture = LoadTexture(graphicsDevice, contentRoot, SurfaceCapTexturePath);
        CeilingCapTexture = LoadTexture(graphicsDevice, contentRoot, CeilingCapTexturePath);
        WallEdgeTexture = LoadTexture(graphicsDevice, contentRoot, WallEdgeTexturePath);
        BoulderTexture = LoadTexture(graphicsDevice, contentRoot, BoulderTexturePath);
        SurfaceCapSampler = new SamplerState { Filter = Filter, AddressU = AddressU, AddressV = SurfaceCapAddressV };
        PropSampler = new SamplerState { Filter = Filter, AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp };
    }

    public Texture2D Texture { get; }
    public Texture2D SurfaceCapTexture { get; }
    public Texture2D CeilingCapTexture { get; }
    public Texture2D WallEdgeTexture { get; }
    public Texture2D BoulderTexture { get; }
    public SamplerState SurfaceCapSampler { get; }
    public SamplerState PropSampler { get; }

    public static NumericalVector2 WorldToUv(float worldX, float worldY) =>
        new(worldX / MaterialWorldWidth, worldY / MaterialWorldHeight);

    public static VertexPositionTexture[] BuildVertices(CaveGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return geometry.Vertices.Select(vertex =>
        {
            var uv = WorldToUv(vertex.X, vertex.Y);
            return new VertexPositionTexture(new Vector3(vertex.X, vertex.Y, 0f), new Microsoft.Xna.Framework.Vector2(uv.X, uv.Y));
        }).ToArray();
    }

    public static CaveBoundaryCategory ClassifyBoundary(NumericalVector2 openNormal)
    {
        if (openNormal.Y >= MinimumSurfaceOpenNormalY) return CaveBoundaryCategory.Floor;
        if (openNormal.Y <= MaximumCeilingOpenNormalY) return CaveBoundaryCategory.Ceiling;
        if (MathF.Abs(openNormal.X) >= MinimumWallOpenNormalX && MathF.Abs(openNormal.Y) < MaximumWallOpenNormalY)
            return openNormal.X < 0f ? CaveBoundaryCategory.WallLeft : CaveBoundaryCategory.WallRight;
        return CaveBoundaryCategory.Slope;
    }

    public static CaveBoundarySegment[] AnalyzeBoundaries(CaveGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var occupancy = new TerrainOccupancy(geometry);
        var segments = new List<CaveBoundarySegment>();
        for (var outlineIndex = 0; outlineIndex < geometry.Outlines.Count; outlineIndex++)
        {
            var outline = geometry.Outlines[outlineIndex];
            for (var segmentIndex = 0; segmentIndex < outline.Points.Count - 1; segmentIndex++)
            {
                var start = ToNumerical(outline.Points[segmentIndex]);
                var end = ToNumerical(outline.Points[segmentIndex + 1]);
                var delta = end - start;
                var length = delta.Length();
                if (length <= 0f) continue;
                var tangent = delta / length;
                if (tangent.X < 0f || (tangent.X == 0f && tangent.Y < 0f))
                {
                    (start, end) = (end, start);
                    tangent = -tangent;
                }
                var leftNormal = new NumericalVector2(-tangent.Y, tangent.X);
                var midpoint = (start + end) * 0.5f;
                var left = occupancy.Classify(midpoint + leftNormal * BoundaryClassificationOffset);
                var right = occupancy.Classify(midpoint - leftNormal * BoundaryClassificationOffset);
                NumericalVector2 openNormal;
                if (left == TerrainPointClassification.Traversable && right == TerrainPointClassification.Solid)
                    openNormal = leftNormal;
                else if (right == TerrainPointClassification.Traversable && left == TerrainPointClassification.Solid)
                    openNormal = -leftNormal;
                else continue;
                segments.Add(new CaveBoundarySegment(outlineIndex, segmentIndex, start, end, tangent, openNormal, length,
                    ClassifyBoundary(openNormal)));
            }
        }
        return segments.ToArray();
    }

    public static VertexPositionTexture[] BuildSurfaceCapVertices(CaveGeometry geometry) =>
        BuildSurfaceCapVertices(AnalyzeBoundaries(geometry));

    public static VertexPositionTexture[] BuildSurfaceCapVertices(IReadOnlyList<CaveBoundarySegment> segments) =>
        BuildRibbonVertices(segments, CaveBoundaryCategory.Floor,
            SurfaceCapWorldRepeatLength, SurfaceCapOpenOverhang, SurfaceCapSolidDepth);

    public static VertexPositionTexture[] BuildCeilingCapVertices(CaveGeometry geometry) =>
        BuildCeilingCapVertices(AnalyzeBoundaries(geometry));

    public static VertexPositionTexture[] BuildCeilingCapVertices(IReadOnlyList<CaveBoundarySegment> segments) =>
        BuildRibbonVertices(segments, CaveBoundaryCategory.Ceiling,
            CeilingCapWorldRepeatLength, CeilingCapOpenOverhang, CeilingCapSolidDepth);

    public static VertexPositionTexture[] BuildWallEdgeVertices(CaveGeometry geometry) =>
        BuildWallEdgeVertices(AnalyzeBoundaries(geometry));

    public static VertexPositionTexture[] BuildWallEdgeVertices(IReadOnlyList<CaveBoundarySegment> segments) =>
        BuildRibbonVertices(segments,
            static category => category is CaveBoundaryCategory.WallLeft or CaveBoundaryCategory.WallRight,
            WallEdgeWorldRepeatLength, WallEdgeOpenOverhang, WallEdgeSolidDepth);

    public static CaveDecorationInstance[] BuildBoulderPlacements(
        CaveGeometry geometry,
        string seed,
        NumericalVector2? excludedPosition = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return BuildBoulderPlacements(AnalyzeBoundaries(geometry), seed, excludedPosition);
    }

    public static CaveDecorationInstance[] BuildBoulderPlacements(
        IReadOnlyList<CaveBoundarySegment> segments,
        string seed,
        NumericalVector2? excludedPosition = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        var placements = new List<CaveDecorationInstance>(BoulderMaximumCount);
        foreach (var segment in segments)
        {
            if (placements.Count >= BoulderMaximumCount) break;
            if (segment.Category != CaveBoundaryCategory.Floor || segment.OpenNormal.Y < BoulderMinimumOpenNormalY ||
                segment.Length < BoulderMinimumSegmentLength + BoulderCornerClearance * 2f) continue;
            var midpoint = (segment.Start + segment.End) * 0.5f;
            var hash = StableDecorationHash(seed, segment.OutlineIndex, segment.SegmentIndex, midpoint, 0xB01D3u);
            if (hash % 1000u >= BoulderProbabilityPermille) continue;
            var available = segment.Length * 0.5f - BoulderCornerClearance;
            var signedUnit = ((hash >> 10) & 0xffffu) / 65535f * 2f - 1f;
            var position = midpoint + segment.Tangent * (signedUnit * available * 0.55f);
            if (excludedPosition is { } excluded && NumericalVector2.Distance(position, excluded) < BoulderSpawnClearance)
                continue;
            if (placements.Any(existing => NumericalVector2.Distance(existing.Position, position) < BoulderMinimumSpacing))
                continue;
            placements.Add(new CaveDecorationInstance(position, segment.Tangent, segment.OpenNormal, 0));
        }
        return placements.ToArray();
    }

    public static VertexPositionTexture[] BuildBoulderVertices(IReadOnlyList<CaveDecorationInstance> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        var vertices = new List<VertexPositionTexture>(placements.Count * 6);
        foreach (var placement in placements)
        {
            var bottomCenter = placement.Position - placement.OpenNormal * BoulderSolidInset;
            var bottomLeft = bottomCenter - placement.Tangent * (BoulderWorldWidth * 0.5f);
            var bottomRight = bottomCenter + placement.Tangent * (BoulderWorldWidth * 0.5f);
            var topLeft = bottomLeft + placement.OpenNormal * BoulderWorldHeight;
            var topRight = bottomRight + placement.OpenNormal * BoulderWorldHeight;
            AddTexturedQuad(vertices, topLeft, topRight, bottomLeft, bottomRight, 0f, 1f);
        }
        return vertices.ToArray();
    }

    private static VertexPositionTexture[] BuildRibbonVertices(
        IReadOnlyList<CaveBoundarySegment> segments,
        CaveBoundaryCategory category,
        float repeatLength,
        float openOverhang,
        float solidDepth) =>
        BuildRibbonVertices(segments, candidate => candidate == category, repeatLength, openOverhang, solidDepth);

    private static VertexPositionTexture[] BuildRibbonVertices(
        IReadOnlyList<CaveBoundarySegment> segments,
        Func<CaveBoundaryCategory, bool> eligible,
        float repeatLength,
        float openOverhang,
        float solidDepth)
    {
        var vertices = new List<VertexPositionTexture>();
        foreach (var segment in segments)
        {
            if (!eligible(segment.Category)) continue;
            var solidNormal = -segment.OpenNormal;
            var outerStart = segment.Start + segment.OpenNormal * openOverhang;
            var outerEnd = segment.End + segment.OpenNormal * openOverhang;
            var innerStart = segment.Start + solidNormal * solidDepth;
            var innerEnd = segment.End + solidNormal * solidDepth;
            var uStart = NumericalVector2.Dot(segment.Start, segment.Tangent) / repeatLength;
            var uEnd = uStart + segment.Length / repeatLength;
            AddTexturedQuad(vertices, outerStart, outerEnd, innerStart, innerEnd, uStart, uEnd);
        }
        return vertices.ToArray();
    }

    private static void AddTexturedQuad(ICollection<VertexPositionTexture> vertices,
        NumericalVector2 outerStart, NumericalVector2 outerEnd, NumericalVector2 innerStart, NumericalVector2 innerEnd,
        float uStart, float uEnd)
    {
        var first = CreateVertex(outerStart, uStart, 0f); var second = CreateVertex(outerEnd, uEnd, 0f);
        var third = CreateVertex(innerEnd, uEnd, 1f); var fourth = CreateVertex(innerStart, uStart, 1f);
        vertices.Add(first); vertices.Add(second); vertices.Add(third);
        vertices.Add(first); vertices.Add(third); vertices.Add(fourth);
    }

    private static uint StableDecorationHash(string seed, int outlineIndex, int segmentIndex, NumericalVector2 position, uint salt)
    {
        var hash = 2166136261u;
        foreach (var value in Encoding.UTF8.GetBytes(seed)) hash = (hash ^ value) * 16777619u;
        hash = Mix(hash, unchecked((uint)outlineIndex)); hash = Mix(hash, unchecked((uint)segmentIndex));
        hash = Mix(hash, unchecked((uint)MathF.Round(position.X * 16f)));
        hash = Mix(hash, unchecked((uint)MathF.Round(position.Y * 16f)));
        return Mix(hash, salt);
    }

    private static uint Mix(uint hash, uint value)
    {
        for (var shift = 0; shift < 32; shift += 8) hash = (hash ^ (byte)(value >> shift)) * 16777619u;
        return hash;
    }

    private static VertexPositionTexture CreateVertex(NumericalVector2 position, float u, float v) =>
        new(new Vector3(position.X, position.Y, 0f), new Microsoft.Xna.Framework.Vector2(u, v));
    private static NumericalVector2 ToNumerical(CaveVertex vertex) => new(vertex.X, vertex.Y);

    private static Texture2D LoadTexture(GraphicsDevice graphicsDevice, string contentRoot, string relativePath)
    {
        var path = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        try { using var stream = File.OpenRead(path); return Texture2D.FromStream(graphicsDevice, stream); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new InvalidOperationException($"Could not load required cave texture '{relativePath}' at '{path}'.", exception); }
    }

    public void Dispose()
    {
        PropSampler.Dispose(); SurfaceCapSampler.Dispose(); BoulderTexture.Dispose(); WallEdgeTexture.Dispose();
        CeilingCapTexture.Dispose(); SurfaceCapTexture.Dispose(); Texture.Dispose();
    }
}

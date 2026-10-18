using BirdHell.Systems;
using BirdHell.World.Geometry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NumericalVector2 = System.Numerics.Vector2;

namespace BirdHell.Presentation;

/// <summary>Presentation-only world-space material mapping for authoritative cave geometry.</summary>
public sealed class CaveMaterialPresentation : IDisposable
{
    public const string TexturePath = "Sprites/Terrain/cave_base_material_v1.png";
    public const string SurfaceCapTexturePath = "Sprites/Terrain/cave_surface_cap_v1.png";
    public const float MaterialWorldWidth = 6f;
    public const float MaterialWorldHeight = 6f;
    public const float SurfaceCapWorldRepeatLength = 2.75f;
    public const float SurfaceCapOpenOverhang = 0.18f;
    public const float SurfaceCapSolidDepth = 0.62f;
    public const float MinimumSurfaceOpenNormalY = 0.35f;
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
        SurfaceCapSampler = new SamplerState
        {
            Filter = Filter,
            AddressU = AddressU,
            AddressV = SurfaceCapAddressV,
        };
    }

    public Texture2D Texture { get; }
    public Texture2D SurfaceCapTexture { get; }
    public SamplerState SurfaceCapSampler { get; }

    public static System.Numerics.Vector2 WorldToUv(float worldX, float worldY) =>
        new(worldX / MaterialWorldWidth, worldY / MaterialWorldHeight);

    public static VertexPositionTexture[] BuildVertices(CaveGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return geometry.Vertices.Select(vertex =>
        {
            var uv = WorldToUv(vertex.X, vertex.Y);
            return new VertexPositionTexture(
                new Vector3(vertex.X, vertex.Y, 0f),
                new Microsoft.Xna.Framework.Vector2(uv.X, uv.Y));
        }).ToArray();
    }

    public static VertexPositionTexture[] BuildSurfaceCapVertices(CaveGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var occupancy = new TerrainOccupancy(geometry);
        var vertices = new List<VertexPositionTexture>();
        foreach (var outline in geometry.Outlines)
        for (var index = 0; index < outline.Points.Count - 1; index++)
        {
            var start = ToNumerical(outline.Points[index]);
            var end = ToNumerical(outline.Points[index + 1]);
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
            const float classificationOffset = 0.05f;
            var left = occupancy.Classify(midpoint + leftNormal * classificationOffset);
            var right = occupancy.Classify(midpoint - leftNormal * classificationOffset);
            NumericalVector2 openNormal;
            if (left == TerrainPointClassification.Traversable && right == TerrainPointClassification.Solid)
                openNormal = leftNormal;
            else if (right == TerrainPointClassification.Traversable && left == TerrainPointClassification.Solid)
                openNormal = -leftNormal;
            else
                continue;
            if (openNormal.Y < MinimumSurfaceOpenNormalY) continue;

            var solidNormal = -openNormal;
            var outerStart = start + openNormal * SurfaceCapOpenOverhang;
            var outerEnd = end + openNormal * SurfaceCapOpenOverhang;
            var innerStart = start + solidNormal * SurfaceCapSolidDepth;
            var innerEnd = end + solidNormal * SurfaceCapSolidDepth;
            var uStart = NumericalVector2.Dot(start, tangent) / SurfaceCapWorldRepeatLength;
            var uEnd = uStart + length / SurfaceCapWorldRepeatLength;
            AddQuad(vertices, outerStart, outerEnd, innerStart, innerEnd, uStart, uEnd);
        }
        return vertices.ToArray();
    }

    private static void AddQuad(
        ICollection<VertexPositionTexture> vertices,
        NumericalVector2 outerStart,
        NumericalVector2 outerEnd,
        NumericalVector2 innerStart,
        NumericalVector2 innerEnd,
        float uStart,
        float uEnd)
    {
        var first = CreateVertex(outerStart, uStart, 0f);
        var second = CreateVertex(outerEnd, uEnd, 0f);
        var third = CreateVertex(innerEnd, uEnd, 1f);
        var fourth = CreateVertex(innerStart, uStart, 1f);
        vertices.Add(first);
        vertices.Add(second);
        vertices.Add(third);
        vertices.Add(first);
        vertices.Add(third);
        vertices.Add(fourth);
    }

    private static VertexPositionTexture CreateVertex(NumericalVector2 position, float u, float v) =>
        new(new Vector3(position.X, position.Y, 0f), new Microsoft.Xna.Framework.Vector2(u, v));

    private static NumericalVector2 ToNumerical(CaveVertex vertex) => new(vertex.X, vertex.Y);

    private static Texture2D LoadTexture(GraphicsDevice graphicsDevice, string contentRoot, string relativePath)
    {
        var path = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            using var stream = File.OpenRead(path);
            return Texture2D.FromStream(graphicsDevice, stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"Could not load required cave texture '{relativePath}' at '{path}'.", exception);
        }
    }

    public void Dispose()
    {
        SurfaceCapSampler.Dispose();
        SurfaceCapTexture.Dispose();
        Texture.Dispose();
    }
}

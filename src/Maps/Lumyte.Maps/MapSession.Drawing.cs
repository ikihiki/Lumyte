using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Maps;

/// <summary>Records immutable adopted map data through the borrowed host canvas.</summary>
public sealed partial class MapSession
{
    /// <summary>Records background and globally ordered style passes; no loading or publication occurs.</summary>
    /// <param name="canvas">Already begun canvas. Its transform and clip are restored even on failure.</param>
    public void Record(DrawingCanvas2D canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        Enter(false);
        try
        {
            if (_viewport.X <= 0 || _viewport.Y <= 0)
            {
                return;
            }

            List<DrawItem> items = GetDrawItems();
            ValidateRecordingBudget(items);
            canvas.Save();
            try
            {
                canvas.ClipRect(new(0, 0, _viewport.X, _viewport.Y));
                canvas.FillRect(new(0, 0, _viewport.X, _viewport.Y), _style.Background);
                foreach (MapStyleLayer paint in _style.Layers)
                {
                    foreach (DrawItem item in items)
                    {
                        Entry entry = item.Entry;
                        if (!TryPlacement(item, out Placement placement, out int copies))
                        {
                            continue;
                        }

                        for (int copy = 0; copy < copies; copy++)
                        {
                            (double cameraX, double cameraY) = Camera.Project();
                            double count = 1 << item.Owner.Zoom;
                            double tileX = item.Owner.X + ((placement.First + copy) * count);
                            double left = (((tileX / count) - cameraX) * placement.World) + (_viewport.X / 2.0);
                            double right = ((((tileX + 1) / count) - cameraX) * placement.World) + (_viewport.X / 2.0);
                            double bottom = ((((item.Owner.Y + 1) / count) - cameraY) * placement.World) + (_viewport.Y / 2.0);
                            canvas.Save();
                            try
                            {
                                canvas.ClipOwnershipRect(new Vector2((float)left, (float)placement.Top), new Vector2((float)right, (float)bottom));
                                foreach (MapTileLayer layer in entry.Lease!.Value.Layers)
                                {
                                    if (layer.Name != paint.SourceLayer)
                                    {
                                        continue;
                                    }

                                    foreach (MapTileFeature feature in layer.Features)
                                    {
                                        if (!Eligible(feature, paint))
                                        {
                                            continue;
                                        }

                                        double sourceCount = 1 << entry.Id.Zoom;
                                        double sourceX = entry.Id.X + ((placement.First + copy) * sourceCount);
                                        double sourceLeft = (((sourceX / sourceCount) - cameraX) * placement.World) + (_viewport.X / 2.0);
                                        double sourceTop = (((entry.Id.Y / sourceCount) - cameraY) * placement.World) + (_viewport.Y / 2.0);
                                        Placement sourcePlacement = new(sourceLeft, sourceTop, placement.World / sourceCount, placement.World, 0);
                                        Path2D geometry = BuildPath(feature, layer.Extent, sourceLeft, sourcePlacement);
                                        if (paint.Stroke is { } stroke)
                                        {
                                            canvas.StrokePath(geometry, paint.Color, stroke);
                                        }
                                        else
                                        {
                                            canvas.FillPath(geometry, paint.Color, FillRule2D.NonZero);
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                canvas.Restore();
                            }
                        }
                    }
                }
            }
            finally
            {
                canvas.Restore();
            }
        }
        finally
        {
            _entered = false;
        }
    }

    private static bool Eligible(MapTileFeature feature, MapStyleLayer paint)
        => (paint.Stroke is null) == (feature.GeometryType == MapGeometryType.Polygon);

    private static Path2D BuildPath(MapTileFeature feature, uint extent, double left, Placement placement)
    {
        var builder = new PathBuilder2D();
        foreach (MapTilePath path in feature.Paths)
        {
            for (int index = 0; index < path.Points.Length; index++)
            {
                MapPoint point = path.Points[index];
                var position = new Vector2((float)(left + (point.X * placement.Size / extent)), (float)(placement.Top + (point.Y * placement.Size / extent)));
                if (index == 0)
                {
                    builder.MoveTo(position);
                }
                else
                {
                    builder.LineTo(position);
                }
            }

            if (feature.GeometryType == MapGeometryType.Polygon)
            {
                builder.Close();
            }
        }

        return builder.Build();
    }

    private List<DrawItem> GetDrawItems()
    {
        if (!_spatial)
        {
            return _entries.Select(entry => new DrawItem(entry, entry.Id)).ToList();
        }

        var result = new List<DrawItem>();
        foreach (MapTileId owner in SelectVisible(out _))
        {
            MapTileId source = owner;
            for (int level = 0; level <= _options.MaxFallbackDepth && source.Zoom >= _tiles.MinZoom; level++)
            {
                Entry? entry = _entries.FirstOrDefault(entry => entry.Id == source && !entry.Retiring
                    && entry.Lease is not null && IsCurrent(entry));
                if (entry is not null)
                {
                    result.Add(new(entry, owner));
                    break;
                }

                source = new(source.Zoom - 1, source.X / 2, source.Y / 2);
            }
        }

        return result;
    }

    private bool TryPlacement(DrawItem item, out Placement placement, out int copies)
    {
        placement = default;
        copies = 0;
        Entry entry = item.Entry;
        if (entry.Lease is null || (_spatial ? !IsCurrent(entry) : _options.SelectVisibleTiles && entry.Id.Zoom != SourceZoom))
        {
            return false;
        }

        (double cameraX, double cameraY) = Camera.Project();
        double world = 512 * Math.Pow(2, Camera.Zoom);
        double count = 1 << item.Owner.Zoom;
        double size = world / count;
        double top = (((item.Owner.Y / count) - cameraY) * world) + (_viewport.Y / 2.0);
        if (top >= _viewport.Y || top + size <= 0)
        {
            return false;
        }

        double left = (((item.Owner.X / count) - cameraX) * world) + (_viewport.X / 2.0);
        double first = Math.Floor((-size - left) / world) + 1;
        double last = Math.Ceiling((_viewport.X - left) / world) - 1;
        double total = last - first + 1;
        if (total <= 0)
        {
            return false;
        }

        if (!double.IsFinite(total) || total > int.MaxValue || total > _options.MaxRecordingCpuBytes / 256)
        {
            throw new InvalidOperationException("Map world repetition exceeds the recording reservation before allocation.");
        }

        copies = (int)total;
        placement = new(left + (first * world), top, size, world, first);
        return true;
    }

    private void ValidateRecordingBudget(List<DrawItem> items)
    {
        long remaining = _options.MaxRecordingCpuBytes;
        foreach (MapStyleLayer paint in _style.Layers)
        {
            foreach (DrawItem item in items)
            {
                Entry entry = item.Entry;
                if (!TryPlacement(item, out _, out int copies))
                {
                    continue;
                }

                long perCopy = 256;
                foreach (MapTileLayer layer in entry.Lease!.Value.Layers)
                {
                    if (layer.Name != paint.SourceLayer)
                    {
                        continue;
                    }

                    foreach (MapTileFeature feature in layer.Features)
                    {
                        if (!Eligible(feature, paint))
                        {
                            continue;
                        }

                        long charge = 1024;
                        foreach (MapTilePath path in feature.Paths)
                        {
                            charge = checked(charge + 256 + (192L * (path.Points.Length + 1)));
                        }

                        perCopy = checked(perCopy + charge);
                    }
                }

                long total = checked(perCopy * copies);
                if (total > remaining)
                {
                    throw new InvalidOperationException("Map recording CPU reservation exceeded before allocating paths.");
                }

                remaining -= total;
            }
        }
    }

    private readonly record struct DrawItem(Entry Entry, MapTileId Owner);

    private readonly record struct Placement(double Left, double Top, double Size, double World, double First);
}

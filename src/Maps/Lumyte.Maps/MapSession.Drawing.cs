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

            ValidateRecordingBudget();
            canvas.Save();
            try
            {
                canvas.ClipRect(new(0, 0, _viewport.X, _viewport.Y));
                canvas.FillRect(new(0, 0, _viewport.X, _viewport.Y), _style.Background);
                (double cameraX, double cameraY) = Camera.Project();
                double world = 512 * Math.Pow(2, Camera.Zoom);
                foreach (MapStyleLayer paint in _style.Layers)
                {
                    foreach (Entry entry in _entries)
                    {
                        if (entry.Lease is null)
                        {
                            continue;
                        }

                        MapTileContent content = entry.Lease.Value;
                        double count = 1 << entry.Id.Zoom;
                        double size = world / count;
                        double left = (((entry.Id.X / count) - cameraX) * world) + (_viewport.X / 2.0);
                        double top = (((entry.Id.Y / count) - cameraY) * world) + (_viewport.Y / 2.0);
                        if (left >= _viewport.X || top >= _viewport.Y || left + size <= 0 || top + size <= 0)
                        {
                            continue;
                        }

                        canvas.Save();
                        try
                        {
                            canvas.ClipRect(new((float)left, (float)top, (float)size, (float)size));
                            foreach (MapTileLayer layer in content.Layers)
                            {
                                if (layer.Name != paint.SourceLayer)
                                {
                                    continue;
                                }

                                foreach (MapTileFeature feature in layer.Features)
                                {
                                    if ((paint.Stroke is null) != (feature.GeometryType == MapGeometryType.Polygon))
                                    {
                                        continue;
                                    }

                                    var builder = new PathBuilder2D();
                                    foreach (MapTilePath path in feature.Paths)
                                    {
                                        for (int index = 0; index < path.Points.Length; index++)
                                        {
                                            MapPoint point = path.Points[index];
                                            var position = new Vector2((float)(left + (point.X * size / layer.Extent)), (float)(top + (point.Y * size / layer.Extent)));
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

                                    Path2D geometry = builder.Build();
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

    private void ValidateRecordingBudget()
    {
        long remaining = _options.MaxRecordingCpuBytes;
        foreach (MapStyleLayer paint in _style.Layers)
        {
            foreach (Entry entry in _entries)
            {
                if (entry.Lease is null)
                {
                    continue;
                }

                foreach (MapTileLayer layer in entry.Lease.Value.Layers)
                {
                    if (layer.Name != paint.SourceLayer)
                    {
                        continue;
                    }

                    foreach (MapTileFeature feature in layer.Features)
                    {
                        long charge = 1024;
                        foreach (MapTilePath path in feature.Paths)
                        {
                            charge = checked(charge + 256 + (192L * (path.Points.Length + 1)));
                        }

                        if (charge > remaining)
                        {
                            throw new InvalidOperationException("Map recording CPU reservation exceeded before allocating paths.");
                        }

                        remaining -= charge;
                    }
                }
            }
        }
    }
}

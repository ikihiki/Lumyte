using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using SkiaSharp;
internal sealed record Op(Rect2D Rect, Path2D? Path, int Paint, Matrix3x2 Transform, bool Clip);
internal sealed class Trace
{
 internal const int W=1920,H=1080;
 internal readonly PaintGraph2D[] Paints=[PaintGraph2D.Solid(new(1,0,0,1)),PaintGraph2D.Solid(new(0,1,0,1)),PaintGraph2D.Solid(new(0,0,1,1)),PaintGraph2D.LinearGradient(new(0,0),new(28,24),[new(0,new(1,0,0,1)),new(1,new(0,0,1,1))]),PaintGraph2D.Solid(new(1,0,0,0.5f)),PaintGraph2D.Solid(new(0,0,1,0.5f))];
 internal readonly Op[][] Frames;
 internal readonly string Name;
 internal readonly bool Static;
 internal readonly Path2D Clip1=Rectangle(new(3,3,25,23)),Clip2=Rectangle(new(5,1,24,24));
 internal Trace(string name){Name=name;if(name is not ("rect-static" or "rect-dynamic" or "path-static" or "path-dynamic" or "glyph-static" or "glyph-dynamic"))throw new ArgumentException("Unknown fixture",nameof(name));Static=name.EndsWith("static");Frames=new Op[32][];Path2D? glyph=name.StartsWith("glyph")?Glyph():null;for(int f=0;f<Frames.Length;f++){int n=name.StartsWith("rect")?1000:200;Frames[f]=Enumerable.Range(0,n).Select(i=>{int x=12+(i%50)*37+(Static?0:f%4),y=12+(i/50)*48;Path2D? p=glyph;if(name.StartsWith("path")){var b=new PathBuilder2D();b.MoveTo(new(0,0));b.CubicTo(new(30,-4+(Static?0:f%4)),new(34,29),new(2,27));b.LineTo(new(0,0));b.Close();p=b.Build();}return new Op(new(0,0,30,28),p,name.StartsWith("path")?3:i%3,Matrix3x2.CreateRotation(name.StartsWith("rect")?0:0.05f*(i%3))*Matrix3x2.CreateTranslation(x,y),!name.StartsWith("rect"));}).ToArray();}}
 internal DrawList2D Record(int frame,Canvas2D c){c.Begin();foreach(var o in Frames[frame%Frames.Length]){c.Save();c.ConcatTransform(o.Transform);if(o.Clip){c.ClipPath(Clip1);c.ClipPath(Clip2);}if(o.Path is null)c.FillRect(o.Rect,Paints[o.Paint]);else c.FillPath(o.Path,Paints[o.Paint]);c.Restore();}return c.End();}
 internal static Path2D Rectangle(Rect2D r){var b=new PathBuilder2D();b.MoveTo(new(r.X,r.Y));b.LineTo(new(r.X+r.Width,r.Y));b.LineTo(new(r.X+r.Width,r.Y+r.Height));b.LineTo(new(r.X,r.Y+r.Height));b.Close();return b.Build();}
 static Path2D Glyph(){using var type=SKTypeface.FromFile("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");using var font=new SKFont(type,30);ushort id=font.GetGlyph('B');using var path=font.GetGlyphPath(id)??throw new Exception("Missing glyph outline");using var iter=path.CreateRawIterator();SKPoint[] p=new SKPoint[4];var b=new PathBuilder2D();while(true){var v=iter.Next(p);if(v==SKPathVerb.Done)break;Vector2 V(int i)=>new(p[i].X,p[i].Y+25);switch(v){case SKPathVerb.Move:b.MoveTo(V(0));break;case SKPathVerb.Line:b.LineTo(V(1));break;case SKPathVerb.Quad:b.QuadraticTo(V(1),V(2));break;case SKPathVerb.Cubic:b.CubicTo(V(1),V(2),V(3));break;case SKPathVerb.Close:b.Close();break;default:throw new Exception("Unsupported glyph verb "+v);}}return b.Build();}
}

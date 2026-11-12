using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;

using SkiaSharp;
interface IAdapter:IDisposable {string Identity{get;} void Record(int frame); Task RenderAsync(); byte[] Pixels();}
sealed class SkiaAdapter:IAdapter
{
 readonly Egl egl=new();readonly GRGlInterface gi;readonly GRContext gr;readonly SKSurface surface;readonly Trace trace;readonly bool aa;SKPicture? picture;
 readonly Dictionary<Path2D,SKPath> paths=new(ReferenceEqualityComparer.Instance);readonly SKPaint[] paints;
 public string Identity=>egl.Identity;
 internal SkiaAdapter(Trace t,bool aa){trace=t;this.aa=aa;gi=GRGlInterface.CreateOpenGl(Egl.eglGetProcAddress);gr=GRContext.CreateGl(gi)??throw new Exception("GPU context missing");surface=SKSurface.Create(gr,false,new SKImageInfo(Trace.W,Trace.H,SKColorType.Rgba8888,SKAlphaType.Premul,SKColorSpace.CreateSrgb()))??throw new Exception("GPU surface missing");paints=t.Paints.Select(p=>{var q=new SKPaint{IsAntialias=aa};if(p.Root is SolidPaintNode2D s)q.ColorF=new(s.Color.R,s.Color.G,s.Color.B,s.Color.A);else if(p.Root is LinearPaintNode2D l)q.Shader=SKShader.CreateLinearGradient(new(l.P0.X,l.P0.Y),new(l.P1.X,l.P1.Y),l.Stops.Select(s=>new SKColorF(s.Color.R,s.Color.G,s.Color.B,s.Color.A)).ToArray(),SKColorSpace.CreateSrgbLinear(),l.Stops.Select(s=>s.Offset).ToArray(),SKShaderTileMode.Clamp);else throw new Exception("paint");return q;}).ToArray();foreach(var f in t.Frames)foreach(var o in f)if(o.Path!=null)GetPath(o.Path);GetPath(t.Clip1);GetPath(t.Clip2);egl.ReleaseCurrent();}
 SKPath GetPath(Path2D p){if(paths.TryGetValue(p,out var r))return r;using var builder=new SKPathBuilder();foreach(var s in p.Segments)switch(s.Verb){case PathVerb2D.Move:builder.MoveTo(s.P0.X,s.P0.Y);break;case PathVerb2D.Line:builder.LineTo(s.P0.X,s.P0.Y);break;case PathVerb2D.Quadratic:builder.QuadTo(s.P0.X,s.P0.Y,s.P1.X,s.P1.Y);break;case PathVerb2D.Cubic:builder.CubicTo(s.P0.X,s.P0.Y,s.P1.X,s.P1.Y,s.P2.X,s.P2.Y);break;case PathVerb2D.Close:builder.Close();break;default:throw new Exception("path");}r=builder.Detach();paths.Add(p,r);return r;}
 static SKMatrix M(Matrix3x2 m)=>new(){ScaleX=m.M11,SkewX=m.M21,TransX=m.M31,SkewY=m.M12,ScaleY=m.M22,TransY=m.M32,Persp2=1};
 public void Record(int f){if(trace.Static&&picture!=null)return;picture?.Dispose();using var rec=new SKPictureRecorder();var c=rec.BeginRecording(new SKRect(0,0,Trace.W,Trace.H));foreach(var o in trace.Frames[f%trace.Frames.Length]){c.Save();c.SetMatrix(M(o.Transform));if(o.Clip){c.ClipPath(GetPath(trace.Clip1),SKClipOperation.Intersect,aa);c.ClipPath(GetPath(trace.Clip2),SKClipOperation.Intersect,aa);}if(o.Path==null)c.DrawRect(o.Rect.X,o.Rect.Y,o.Rect.Width,o.Rect.Height,paints[o.Paint]);else c.DrawPath(GetPath(o.Path),paints[o.Paint]);c.Restore();}picture=rec.EndRecording();}
 public Task RenderAsync(){egl.MakeCurrent();try{surface.Canvas.Clear(SKColors.Transparent);surface.Canvas.DrawPicture(picture!);surface.Flush();gr.Submit();Egl.glFinish();}finally{if(!trace.Static){picture?.Dispose();picture=null;}egl.ReleaseCurrent();}return Task.CompletedTask;}
 public byte[] Pixels(){egl.MakeCurrent();try{using var b=new SKBitmap(new SKImageInfo(Trace.W,Trace.H,SKColorType.Rgba8888,SKAlphaType.Premul,SKColorSpace.CreateSrgb()));if(!surface.ReadPixels(b.Info,b.GetPixels(),b.RowBytes,0,0))throw new Exception("read");return b.Bytes;}finally{egl.ReleaseCurrent();}}
 public void Dispose(){egl.MakeCurrent();picture?.Dispose();foreach(var p in paths.Values)p.Dispose();foreach(var p in paints)p.Dispose();surface.Dispose();gr.Dispose();gi.Dispose();egl.Dispose();}
}

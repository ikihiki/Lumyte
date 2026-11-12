using System.Runtime.InteropServices;
using SkiaSharp;
internal sealed class Egl : IDisposable
{
 const string Lib="libEGL.so.1";
 [DllImport(Lib)] static extern nint eglGetPlatformDisplay(uint platform,nint native,nint attrs);
 [DllImport(Lib)] static extern int eglInitialize(nint d,out int a,out int b);
 [DllImport(Lib)] static extern int eglBindAPI(uint api);
 [DllImport(Lib)] static extern int eglChooseConfig(nint d,int[] attrs,nint[] configs,int count,out int n);
 [DllImport(Lib)] static extern nint eglCreatePbufferSurface(nint d,nint config,int[] attrs);
 [DllImport(Lib)] static extern nint eglCreateContext(nint d,nint config,nint share,int[] attrs);
 [DllImport(Lib)] static extern int eglMakeCurrent(nint d,nint draw,nint read,nint ctx);
 [DllImport(Lib)] internal static extern nint eglGetProcAddress(string name);
 [DllImport(Lib)] static extern int eglDestroyContext(nint d,nint ctx);
 [DllImport(Lib)] static extern int eglDestroySurface(nint d,nint s);
 [DllImport(Lib)] static extern int eglTerminate(nint d);
 [DllImport("libGL.so.1")] static extern nint glGetString(uint name);
 [DllImport("libGL.so.1")] internal static extern void glFinish();
 nint d,s,c;
 internal Egl(){d=eglGetPlatformDisplay(0x31DD,0,0);if(eglInitialize(d,out _,out _)==0)throw new Exception("eglInitialize");eglBindAPI(0x30A2);nint[] configs=new nint[1];if(eglChooseConfig(d,[0x3033,1,0x3040,8,0x3024,8,0x3023,8,0x3022,8,0x3021,8,0x3038],configs,1,out var n)==0||n==0)throw new Exception("config");s=eglCreatePbufferSurface(d,configs[0],[0x3057,1,0x3056,1,0x3038]);c=eglCreateContext(d,configs[0],0,[0x3038]);if(eglMakeCurrent(d,s,s,c)==0)throw new Exception("current");Identity=string.Join("; ",new uint[]{0x1F00,0x1F01,0x1F02}.Select(x=>Marshal.PtrToStringAnsi(glGetString(x))));if(!Identity.Contains("Radeon 8060S")||Identity.Contains("llvmpipe"))throw new Exception("Unexpected GL: "+Identity);}
 internal void MakeCurrent(){if(eglMakeCurrent(d,s,s,c)==0)throw new Exception("eglMakeCurrent");}
 internal void ReleaseCurrent(){if(eglMakeCurrent(d,0,0,0)==0)throw new Exception("eglReleaseCurrent");}
 internal string Identity{get;}
 public void Dispose(){eglMakeCurrent(d,0,0,0);eglDestroyContext(d,c);eglDestroySurface(d,s);eglTerminate(d);}
}

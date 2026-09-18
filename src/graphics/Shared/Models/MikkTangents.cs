// Triangle-only managed adaptation of Morten S. Mikkelsen's MikkTSpace (2011).
// The original copyright and license are in MikkTSpace.LICENSE.txt.
// This is an altered implementation: managed welding/adjacency, no quad API,
// and the default 180 degree angular threshold only.
using System.Numerics;

namespace Lumyte.Graphics.ModelPreparation;

internal static class MikkTangents
{
    internal static Vector4[] Generate(Vector3[] positions, Vector3[] normals, Vector2[] coordinates)
    {
        int count=positions.Length;
        if(count%3!=0 || normals.Length!=count || coordinates.Length!=count)
        { throw new ArgumentException("Tangent generation requires matching triangle corner attributes."); }
        var result=Enumerable.Repeat(new Vector4(1,0,0,-1),count).ToArray();
        var welded=new int[count];var identities=new Dictionary<(Vector3,Vector3,Vector2),int>();
        for(int i=0;i<count;i++)
        {
            var key=(positions[i],normals[i],coordinates[i]);
            if(!identities.TryGetValue(key,out int index)) { index=i;identities.Add(key,index); }
            welded[i]=index;
        }
        List<Face> faces=[];List<int> degenerates=[];
        for(int first=0;first<count;first+=3)
        {
            var p0=positions[first];var p1=positions[first+1];var p2=positions[first+2];
            if(p0==p1 || p0==p2 || p1==p2) { degenerates.Add(first);continue; }
            var d1=p1-p0;var d2=p2-p0;
            var uv1=coordinates[first+1]-coordinates[first];var uv2=coordinates[first+2]-coordinates[first];
            float area=uv1.X*uv2.Y-uv1.Y*uv2.X;
            var s=uv2.Y*d1-uv1.Y*d2;var t=uv1.X*d2-uv2.X*d1;
            float sl=Length(s),tl=Length(t);bool good=Nonzero(area)&&Nonzero(sl)&&Nonzero(tl);
            float sign=area>0 ? 1 : -1;
            faces.Add(new(first,area>0,!good,good ? s*(sign/sl) : Vector3.Zero,good ? t*(sign/tl) : Vector3.Zero));
        }
        var edges=new Dictionary<(int,int),List<(int Face,int Edge)>>();
        for(int f=0;f<faces.Count;f++)
        {
            for(int e=0;e<3;e++)
            {
                int a=welded[faces[f].First+e],b=welded[faces[f].First+(e+1)%3];
                var key=(Math.Min(a,b),Math.Max(a,b));
                if(!edges.TryGetValue(key,out var list)) { list=[];edges.Add(key,list); }
                list.Add((f,e));
            }
        }
        foreach(var list in edges.Values)
        {
            for(int i=0;i<list.Count;i++)
            {
                var (f,e)=list[i];if(faces[f].Neighbors[e]>=0) { continue; }
                int a=welded[faces[f].First+e],b=welded[faces[f].First+(e+1)%3];
                for(int j=i+1;j<list.Count;j++)
                {
                    var (g,k)=list[j];
                    if(faces[g].Neighbors[k]<0 && a==welded[faces[g].First+(k+1)%3] && b==welded[faces[g].First+k])
                    { faces[f].Neighbors[e]=g;faces[g].Neighbors[k]=f;break; }
                }
            }
        }
        int group=0;
        for(int f=0;f<faces.Count;f++)
        {
            for(int corner=0;corner<3;corner++)
            {
                var seed=faces[f];
                if(seed.Any || seed.Groups[corner]>=0) { continue; }
                int representative=welded[seed.First+corner];bool orientation=seed.Orientation;
                List<int> members=[];Stack<int> pending=new();pending.Push(f);
                while(pending.TryPop(out int index))
                {
                    var face=faces[index];int c=Corner(face,representative,welded);
                    if(face.Groups[c]>=0) { continue; }
                    if(face.Any && face.Groups.All(g => g<0)) { face.Orientation=orientation; }
                    if(face.Orientation!=orientation) { continue; }
                    face.Groups[c]=group;members.Add(index);
                    int right=face.Neighbors[(c+2)%3],left=face.Neighbors[c];
                    if(right>=0) { pending.Push(right); }
                    if(left>=0) { pending.Push(left); }
                }
                members.Sort();
                var n=normals[representative];
                foreach(int index in members)
                {
                    var face=faces[index];var s=Project(face.S,n);var t=Project(face.T,n);Vector3 sum=default;
                    foreach(int other in members)
                    {
                        var contribution=faces[other];
                        if(contribution.Any) { continue; }
                        var os=Project(contribution.S,n);var ot=Project(contribution.T,n);
                        if(!face.Any && other!=index && !(Dot(s,os)>-1 && Dot(t,ot)>-1)) { continue; }
                        int c=Corner(contribution,representative,welded);
                        var p=positions[contribution.First+c];
                        var a=Project(positions[contribution.First+(c+2)%3]-p,n);
                        var b=Project(positions[contribution.First+(c+1)%3]-p,n);
                        float angle=MathF.Acos(Math.Clamp(Dot(a,b),-1,1));
                        sum+=angle*os;
                    }
                    result[face.First+Corner(face,representative,welded)]=new(Normalize(sum),orientation ? 1 : -1);
                }
                group++;
            }
        }
        // Geometric degenerates inherit the first matching non-degenerate corner.
        var goodCorners=new Dictionary<int,int>();
        foreach(var face in faces)
        {
            for(int c=0;c<3;c++) { goodCorners.TryAdd(welded[face.First+c],face.First+c); }
        }
        foreach(int first in degenerates)
        {
            for(int c=0;c<3;c++)
            { if(goodCorners.TryGetValue(welded[first+c],out int source)) { result[first+c]=result[source]; } }
        }
        return result;
    }
    private static int Corner(Face face,int representative,int[] welded)
    { for(int c=0;c<3;c++) { if(welded[face.First+c]==representative) { return c; } }throw new InvalidOperationException("Disconnected tangent group."); }
    private static float Dot(Vector3 a,Vector3 b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static float Length(Vector3 v) => MathF.Sqrt(Dot(v,v));
    private static bool Nonzero(float value) => MathF.Abs(value)>1.17549435e-38f;
    private static Vector3 Normalize(Vector3 v) => Nonzero(v.X)||Nonzero(v.Y)||Nonzero(v.Z) ? v*(1/Length(v)) : v;
    private static Vector3 Project(Vector3 v,Vector3 n) => Normalize(v-Dot(n,v)*n);
    private sealed class Face(int first,bool orientation,bool any,Vector3 s,Vector3 t)
    {
        internal int First { get; }=first;
        internal bool Orientation { get; set; }=orientation;
        internal bool Any { get; }=any;
        internal Vector3 S { get; }=s;
        internal Vector3 T { get; }=t;
        internal int[] Neighbors { get; }=[-1,-1,-1];
        internal int[] Groups { get; }=[-1,-1,-1];
    }
}

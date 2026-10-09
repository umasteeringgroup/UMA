#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintVeinTests
    {
        private static Func<Vector3,float,float,float,float,int,float,float> Sampler()
        {
            var type=Type.GetType("UMA.TexturePaint.Examples.SubdermalVeinField, UMA.TexturePaint.Examples",true);
            return (Func<Vector3,float,float,float,float,int,float,float>)type.GetMethod("Sample",BindingFlags.Static|BindingFlags.NonPublic)
                .CreateDelegate(typeof(Func<Vector3,float,float,float,float,int,float,float>));
        }

        private static float[] Render(int size,float branches=1,float width=1,int seed=1731,float direction=18)
        {
            var sample=Sampler();var pixels=new float[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                pixels[y*size+x]=sample(new Vector3((x+.5f)/size*4,(y+.5f)/size*4,0),direction,7,width,branches,seed,4f/size);
            return pixels;
        }

        [Test]
        public void VeinsAreDeterministicBranchingAndWidthDoesNotChangeTheirPlacement()
        {
            var original=Render(256);var repeat=Render(256);var other=Render(256,seed:1732);
            Assert.That(original,Is.EqualTo(repeat));
            Assert.That(original.Zip(other,(a,b)=>Mathf.Abs(a-b)).Sum(),Is.GreaterThan(20));
            var trunks=Render(256,branches:0);var thick=Render(256,width:2);
            Assert.That(original.Sum(),Is.GreaterThan(trunks.Sum()*1.08f));
            Assert.That(thick.Sum(),Is.GreaterThan(original.Sum()*1.5f));
            for(int i=0;i<original.Length;i++)
            {
                Assert.That(original[i],Is.InRange(0f,1f));
                Assert.That(original[i]+.00001f,Is.GreaterThanOrEqualTo(trunks[i]),"Branching retains its parent vessels.");
                Assert.That(thick[i]+.00001f,Is.GreaterThanOrEqualTo(original[i]),"Width enlarges the same vessels.");
            }
        }

        [TestCase(1731)] [TestCase(7)] [TestCase(42)]
        public void BranchingVeinsDoNotEncloseGridCells(int seed)
        {
            const int size=512;
            var values=Render(size,seed:seed);var visited=new bool[values.Length];
            var queue=new Queue<int>();int holes=0;
            for(int index=0;index<values.Length;index++)
            {
                if(visited[index]||values[index]>.08f)continue;
                int area=0;bool touchesBoundary=false;queue.Enqueue(index);visited[index]=true;
                while(queue.Count>0)
                {
                    int current=queue.Dequeue(),x=current%size,y=current/size;area++;
                    if(x==0||y==0||x==size-1||y==size-1)touchesBoundary=true;
                    foreach(int next in new[]{x>0?current-1:-1,x<size-1?current+1:-1,y>0?current-size:-1,y<size-1?current+size:-1})
                        if(next>=0&&!visited[next]&&values[next]<=.08f){visited[next]=true;queue.Enqueue(next);}
                }
                if(!touchesBoundary&&area>=4)holes++;
            }
            Assert.That(holes,Is.Zero,"Crossing vessel grids enclose empty cells; these vessel trees must remain open.");
            if(seed==1731)
            {
                var image=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
                try
                {
                    image.SetPixels(values.Select(v=>Color.Lerp(new Color(.72f,.53f,.43f,1),new Color(.13f,.23f,.30f,1),v*.55f)).ToArray());image.Apply();
                    Directory.CreateDirectory("Library/VeinGeneratorQA");
                    File.WriteAllBytes("Library/VeinGeneratorQA/branching-veins.png",image.EncodeToPNG());
                }
                finally{UnityEngine.Object.DestroyImmediate(image);}
            }
        }

        [Test]
        public void VeinDirectionRotatesTheWholeNetwork()
        {
            var sample=Sampler();float angle=57*Mathf.Deg2Rad;
            for(int i=0;i<300;i++)
            {
                var p=new Vector3(i*.027f-2,Mathf.Sin(i*.17f)*3,.3f);
                var rotated=new Vector3(p.x*Mathf.Cos(angle)-p.y*Mathf.Sin(angle),p.x*Mathf.Sin(angle)+p.y*Mathf.Cos(angle),p.z);
                Assert.That(sample(p,0,7,1,1,19,0),Is.EqualTo(sample(rotated,57,7,1,1,19,0)).Within(.0001f));
            }
        }
    }
}
#endif

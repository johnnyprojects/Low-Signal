using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static partial class UnderwaterCityBuilder
{
    // Shared angle samples include the exact joins of circular curbs and straight approaches.
    static List<float> RoundaboutAngles()
    {
        var angles=new SortedSet<float>();
        for(int i=0;i<=128;i++) angles.Add(i*Mathf.PI*2/128);
        float[] joins={Mathf.Atan(9.5f/40),Mathf.Asin(9.5f/36),Mathf.Atan(13.5f/40),Mathf.Asin(13.5f/40)};
        for(int q=0;q<4;q++) foreach(float angle in joins)
        {
            angles.Add(q*Mathf.PI/2+angle);
            angles.Add((q+1)*Mathf.PI/2-angle);
        }
        return new List<float>(angles);
    }

    static float ApproachRadius(float angle,float circleRadius,float halfWidth)
    {
        float x=Mathf.Abs(Mathf.Cos(angle)), z=Mathf.Abs(Mathf.Sin(angle));
        float major=Mathf.Max(x,z), minor=Mathf.Min(x,z);
        // Union of a circle and four straight approach strips ending at +/-40 m.
        return Mathf.Max(circleRadius,Mathf.Min(40/major,halfWidth/Mathf.Max(minor,.00001f)));
    }

    static Vector3 Polar(float radius,float y,float angle) => V(Mathf.Cos(angle)*radius,y,Mathf.Sin(angle)*radius);

    static GameObject Ring(string name,Transform parent,Func<float,float> inner,Func<float,float> outer,
        float bottom,float height,Material material,bool solid=true)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        Action<Vector3,Vector3,Vector3,Vector3> quad=(a,b,c,d)=>
        {
            int i=vertices.Count;vertices.AddRange(new[]{a,b,c,d});
            triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
        };
        var angles=RoundaboutAngles();
        for(int i=0;i<angles.Count-1;i++)
        {
            float a=angles[i],b=angles[i+1];
            float ia=inner(a),ib=inner(b),oa=outer(a),ob=outer(b);
            if(oa-ia<.0001f && ob-ib<.0001f) continue;
            Vector3 ai=Polar(ia,bottom+height,a),bi=Polar(ib,bottom+height,b);
            Vector3 ao=Polar(oa,bottom+height,a),bo=Polar(ob,bottom+height,b);
            Vector3 down=Vector3.down*height;
            quad(ai,bi,bo,ao);
            quad(ao+down,bo+down,bi+down,ai+down);
            quad(ao,bo,bo+down,ao+down);
            if(ia>0 || ib>0) quad(bi,ai,ai+down,bi+down);
        }
        var mesh=new Mesh {name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        string folder=Root+"/Meshes";Directory.CreateDirectory(folder);
        string path=folder+"/"+name+".asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved==null) { AssetDatabase.CreateAsset(mesh,path);saved=mesh; }
        else { EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh); }
        var go=Group(name,parent).gameObject;
        go.AddComponent<MeshFilter>().sharedMesh=saved;
        go.AddComponent<MeshRenderer>().sharedMaterial=material;
        if(solid) go.AddComponent<MeshCollider>().sharedMesh=saved;
        go.isStatic=true;return go;
    }

    static GameObject Ring(string name,Transform parent,float inner,float outer,float bottom,float height,Material material,bool solid=true)
        => Ring(name,parent,_=>inner,_=>outer,bottom,height,material,solid);

    static void Plaza()
    {
        var root=Group("03 - Fountain roundabout (static)");
        var road=Group("Circular roadway and four approach joins",root);
        Ring("Roundabout asphalt",road,_=>22,a=>ApproachRadius(a,36,9.5f),0,.20f,asphalt);
        Ring("Outer curved sidewalk",road,a=>ApproachRadius(a,36,9.5f),a=>ApproachRadius(a,40,13.5f),0,.60f,concrete);
        // Curb ends at the road mouths instead of crossing an approach.
        Ring("Outer curved curb",road,a=>ApproachRadius(a,36,9.5f),
            a=>Mathf.Min(ApproachRadius(a,36,9.5f)+.30f,ApproachRadius(a,40,13.5f)),.60f,.16f,pale);
        var island=Group("Raised circular island",root);
        Ring("Island paving",island,0,22,0,.65f,pale);
        Ring("Island curb",island,21.55f,22,.65f,.15f,concrete);
        Ring("Inset circular paving band",island,18.8f,19.3f,.651f,.015f,concrete,false);
        for(int i=0;i<12;i++)
        {
            float a=i*Mathf.PI/6;
            var inlay=Box("Radial paving joint",island,Polar(16,.671f,a),V(7,.02f,.09f),concrete,false);
            inlay.transform.localRotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);
        }
        var fountain=Group("Fountain - closed static structure",root);
        Ring("Fountain lower step",fountain,0,12,.65f,.25f,concrete);
        Ring("Fountain upper step",fountain,0,11,.90f,.25f,pale);
        Ring("Fountain basin foundation",fountain,0,10,1.15f,.35f,concrete);
        Ring("Fountain basin floor",fountain,0,9.2f,1.50f,.12f,algae);
        Ring("Fountain basin wall",fountain,8.9f,10,1.50f,1.15f,pale);
        Ring("Fountain basin coping",fountain,8.7f,10.2f,2.65f,.25f,pale);
        Ring("Fountain centre pedestal",fountain,0,2.3f,1.62f,.70f,concrete);
        Ring("Fountain central column",fountain,0,.95f,2.32f,5.9f,pale);
        Ring("Lower bowl support",fountain,0,1.8f,3.95f,.5f,concrete);
        Ring("Lower bowl floor",fountain,0,4.8f,4.45f,.25f,pale);
        Ring("Lower bowl rim",fountain,4.15f,4.8f,4.70f,.60f,pale);
        Ring("Upper bowl support",fountain,0,1.45f,6.10f,.45f,concrete);
        Ring("Upper bowl floor",fountain,0,2.8f,6.55f,.25f,pale);
        Ring("Upper bowl rim",fountain,2.2f,2.8f,6.80f,.50f,pale);
        Ring("Crown base",fountain,0,1.3f,8.2f,.35f,concrete);
        Ring("Crown finial",fountain,0,.65f,8.55f,.90f,pale);
        for(int i=0;i<8;i++)
        {
            float a=(i+.5f)*Mathf.PI/4;
            var spout=Box("Corroded basin spout",fountain,Polar(2.35f,2.5f,a),V(.8f,.22f,.28f),rust,false);
            spout.transform.localRotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);
            var bench=Group("Island bench",island);bench.localPosition=Polar(17,0,a);bench.localRotation=Quaternion.Euler(0,90-a*Mathf.Rad2Deg,0);
            Box("Seat",bench,V(0,1.3f,0),V(3.2f,.25f,.9f),concrete);
            foreach(float x in new[]{-1.1f,1.1f}) Box("Bench support",bench,V(x,.95f,0),V(.3f,.6f,.65f),rust);
        }
    }

    static void ValidateRoundabout()
    {
        var root=GameObject.Find("03 - Fountain roundabout (static)");
        Check(root!=null,"Missing roundabout");
        var road=root.transform.Find("Circular roadway and four approach joins/Roundabout asphalt").GetComponent<MeshCollider>();
        // Check every direction around the ring and the full width of all four approach seams.
        for(int i=0;i<360;i++)
        {
            float a=i*Mathf.Deg2Rad;
            Check(road.Raycast(new Ray(Polar(29,3,a),Vector3.down),out _,4),"Gap in roundabout at "+i);
            Check(!Physics.CheckSphere(Polar(29,4,a),.6f),"Roundabout swim clearance at "+i);
        }
        for(int side=0;side<4;side++) for(float lateral=-9;lateral<=9;lateral+=1)
        {
            Vector3 point=Quaternion.Euler(0,side*90,0)*V(lateral,3,39.95f);
            Check(road.Raycast(new Ray(point,Vector3.down),out _,4),"Approach seam is not paved");
            Check(!Physics.CheckSphere(point,.6f),"Approach blocked by curb");
        }
        Check(Physics.Raycast(V(0,15,0),Vector3.down,out RaycastHit hit,20) && hit.collider.name=="Crown finial","Fountain missing or not solid");
        Check(Physics.Raycast(V(0,3,12),Vector3.down,4),"Old shaft remains open");
        Check(GameObject.Find("Shaft west wall")==null && GameObject.Find("Facility sealed bulkhead - future cutscene")==null,"Legacy facility entrance remains");
        Check(root.GetComponentsInChildren<MonoBehaviour>().Length==0,"Roundabout must remain static");
        Debug.Log("CITY_ROUNDABOUT_PASS: 360 degree road continuity and swim clearance, four full-width approach joins, solid fountain, old shaft closed, no interaction scripts.");
    }
}

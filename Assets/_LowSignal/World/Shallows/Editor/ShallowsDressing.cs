using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Baked, editable scene objects; never modifies terrain data or lighting.
public static class ShallowsDressing
{
    const string ScenePath = "Assets/_LowSignal/Scenes/Shallows.unity";
    const string AssetRoot = "Assets/_LowSignal/World/Shallows/Dressing";
    const string GroupName = "Shallows - Rock gardens and enemy cave";
    static System.Random random;
    static Terrain terrain;
    static Mesh rock, grass;
    static Material stone, pale, green, amber;
    static float R(float a, float b) => a + (float)random.NextDouble() * (b-a);
    static Vector3 V(float x, float y, float z) => new Vector3(x,y,z);
    static float Ground(float x, float z) => terrain.SampleHeight(V(x,0,z)) + terrain.transform.position.y;

    [MenuItem("Low Signal/Shallows/Rebuild rock gardens and cave")]
    public static void Rebuild()
    {
        if (SceneManager.GetActiveScene().path != ScenePath) throw new Exception("Open Shallows first.");
        Build();
    }

    public static void BuildBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            Build();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Capture();
            Debug.Log("SHALLOWS_PASS: dressing saved; cave clearance verified; previews captured.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void RefineCaveBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
            random = new System.Random(201027);
            rock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_LowSignal/World/SeaFloor/Models/Boulder.obj").GetComponentInChildren<MeshFilter>().sharedMesh;
            stone = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Algae limestone.mat");
            pale = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Shell limestone.mat");
            green = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Seagrass jade.mat");
            amber = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Seagrass olive.mat");
            grass = AssetDatabase.LoadAssetAtPath<Mesh>(AssetRoot + "/Ribbon seagrass.asset");
            var root = GameObject.Find(GroupName).transform;
            var old = root.Find("NE cave - entrance faces southwest");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            BuildCave(root);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Capture();
            Debug.Log("CAVE_REFINE_PASS: cave rebuilt and clearance verified.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static Material Mat(string name, Color color, bool foliage = false)
    {
        string path = AssetRoot + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", .15f);
        if (foliage) m.SetFloat("_Cull", 0);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject Piece(string name, Transform parent, Mesh mesh, Material material, Vector3 p, Vector3 scale, bool solid)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = p;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (solid) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true;
        return go;
    }

    static Mesh GrassMesh()
    {
        string path = AssetRoot + "/Ribbon seagrass.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        var verts = new List<Vector3>(); var tris = new List<int>();
        for (int blade = 0; blade < 18; blade++)
        {
            float angle = R(0, Mathf.PI*2), height = R(.65f,1.7f), bend = R(.25f,.65f);
            Vector3 radial = V(Mathf.Cos(angle),0,Mathf.Sin(angle));
            Vector3 side = V(-radial.z,0,radial.x);
            Vector3 root = radial * R(0,.5f);
            int start = verts.Count;
            for (int j=0; j<=5; j++)
            {
                float t=j/5f, width=.085f*(1-t)+.004f;
                Vector3 center=root + Vector3.up*(height*t) + radial*(bend*t*t);
                verts.Add(center-side*width); verts.Add(center+side*width);
                if(j==5) continue;
                int n=start+j*2;
                tris.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});
            }
        }
        var mesh = new Mesh { name="Ribbon seagrass", vertices=verts.ToArray(), triangles=tris.ToArray() };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,path);
        return mesh;
    }

    static void Build()
    {
        terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        if (terrain == null) throw new Exception("Shallows terrain missing.");
        random = new System.Random(201026);
        Directory.CreateDirectory(AssetRoot); AssetDatabase.Refresh();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_LowSignal/World/SeaFloor/Models/Boulder.obj");
        rock = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        stone=Mat("Algae limestone",new Color(.30f,.38f,.31f));
        pale=Mat("Shell limestone",new Color(.56f,.53f,.39f));
        green=Mat("Seagrass jade",new Color(.16f,.34f,.22f),true);
        amber=Mat("Seagrass olive",new Color(.35f,.39f,.16f),true);
        grass=GrassMesh();
        var old=GameObject.Find(GroupName);
        if(old!=null) Undo.DestroyObjectImmediate(old);
        var root=Group(GroupName,null); Undo.RegisterCreatedObjectUndo(root.gameObject,"Dress Shallows");
        var gardens=Group("Rock gardens - open sand routes between clusters",root);
        Vector2[] centers={new Vector2(-48,-43),new Vector2(-22,-31),new Vector2(23,-38),new Vector2(49,-24),new Vector2(-53,-9),new Vector2(-28,15),new Vector2(25,9),new Vector2(-48,45),new Vector2(-12,49),new Vector2(16,36),new Vector2(56,12),new Vector2(7,-57),new Vector2(-62,20),new Vector2(38,-55)};
        for(int c=0;c<centers.Length;c++)
        {
            var g=Group("Garden "+(c+1).ToString("00"),gardens);
            float x=centers[c].x,z=centers[c].y;
            // Thin embedded stone lenses give real relief without sculpting the sand.
            for(int j=0;j<3;j++)
            {
                float px=x+R(-4,4),pz=z+R(-4,4);
                var shelf=Piece("Raised limestone shelf",g,rock,pale,V(px,Ground(px,pz)-.15f,pz),V(R(2.8f,5.5f),R(.35f,.7f),R(2,4)),true);
                shelf.transform.localRotation=Quaternion.Euler(0,R(0,360),0);
            }
            for(int j=0;j<7;j++)
            {
                float px=x+R(-6,6),pz=z+R(-6,6),size=j<2?R(1.4f,2.8f):R(.35f,1.2f);
                var b=Piece("Weathered boulder",g,rock,j%3==0?pale:stone,V(px,Ground(px,pz)+size*.42f,pz),V(size*R(.9f,1.5f),size,size*R(.8f,1.3f)),true);
                b.transform.localRotation=Quaternion.Euler(R(-18,18),R(0,360),R(-12,12));
            }
            for(int j=0;j<32;j++)
            {
                float a=R(0,Mathf.PI*2),radius=R(4.5f,10),px=x+Mathf.Cos(a)*radius,pz=z+Mathf.Sin(a)*radius;
                var plant=Piece("Seagrass tuft",g,grass,j%4==0?amber:green,V(px,Ground(px,pz)-.03f,pz),Vector3.one*R(.65f,1.4f),false);
                plant.transform.localRotation=Quaternion.Euler(0,R(0,360),0);
            }
        }
        BuildCave(root);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
    }

    static void BuildCave(Transform parent)
    {
        var cave=Group("NE cave - entrance faces southwest",parent);
        cave.position=V(51,Ground(51,51),51);
        cave.rotation=Quaternion.Euler(0,45,0);
        // Interior: x +/-5, z -7..7. Roof underside stays above 6m.
        for(int side=-1;side<=1;side+=2)
            for(int row=0;row<5;row++)
            {
                float z=-6+row*3.3f+R(-.65f,.65f);
                var wall=Piece("Cave wall",cave,rock,stone,V(side*R(8.4f,9.4f),R(2.1f,3.1f),z),V(R(3.5f,4.3f),R(4,5.2f),R(3.3f,4.1f)),true);
                wall.transform.localRotation=Quaternion.Euler(R(-12,12),R(-28,28),R(-13,13));
                var shoulder=Piece("Upper shoulder",cave,rock,row%3==0?pale:stone,V(side*R(7.3f,8.8f),R(5.8f,7.5f),z+R(-1,1)),V(R(3.2f,4.2f),R(2.6f,3.6f),R(3.3f,4.2f)),true);
                shoulder.transform.localRotation=Quaternion.Euler(R(-20,20),R(-35,35),side*R(8,25));
            }
        for(int row=0;row<5;row++)
        {
            var roof=Piece("Rock roof",cave,rock,stone,V(R(-.7f,.7f),9.3f+Mathf.Sin(row*.85f)*1.1f,-6+row*3.3f),V(R(8.4f,9.2f),R(2.2f,2.7f),R(3.5f,4.2f)),true);
            roof.transform.localRotation=Quaternion.Euler(R(-9,9),R(-8,8),R(-6,6));
        }
        for(int i=-1;i<=1;i++)
            Piece("Closed rear wall",cave,rock,stone,V(i*4.8f,3.2f,10),V(3.8f,5.3f,2.8f),true);
        // Irregular skirts and crowns blend the enclosure into a rocky outcrop.
        for(int side=-1;side<=1;side+=2)
            for(int i=0;i<14;i++)
            {
                float x=side*R(10,13.8f),z=R(-8,12),s=R(1.8f,3.8f);
                Vector3 world=cave.TransformPoint(V(x,0,z));
                var b=Piece("Outer scree boulder",cave,rock,i%3==0?pale:stone,
                    V(x,Ground(world.x,world.z)-cave.position.y+s*.35f,z),V(s*1.3f,s,s),true);
                b.transform.localRotation=Quaternion.Euler(R(-12,12),R(0,360),R(-10,10));
            }
        // Offset, sloping buttresses break the continuous vertical wall silhouette.
        for(int side=-1;side<=1;side+=2)
            for(int i=0;i<5;i++)
            {
                var b=Piece("Slanted flank outcrop",cave,rock,i%4==0?pale:stone,
                    V(side*R(10.3f,12),R(3.2f,5.2f),-5+i*3.7f+R(-1,1)),
                    V(R(2.5f,3.6f),R(3.5f,5.4f),R(2.4f,3.5f)),true);
                b.transform.localRotation=Quaternion.Euler(R(-25,25),R(-35,35),side*R(18,36));
            }
        for(int i=0;i<16;i++)
        {
            var b=Piece("Broken crown rock",cave,rock,i%3==0?pale:stone,
                V(R(-6,5),R(10.1f,11.6f),R(-5,9)),V(R(2.4f,4.2f),R(1.7f,3.1f),R(2.4f,4)),true);
            b.transform.localRotation=Quaternion.Euler(R(-25,25),R(0,360),R(-28,28));
        }
        for(int side=-1;side<=1;side+=2)
            for(int i=0;i<30;i++)
            {
                float x=side*R(10,15),z=R(-11,12);
                Vector3 world=cave.TransformPoint(V(x,0,z));
                var plant=Piece("Cave fringe seagrass",cave,grass,i%4==0?amber:green,
                    V(x,Ground(world.x,world.z)-cave.position.y-.03f,z),Vector3.one*R(.8f,1.7f),false);
                plant.transform.localRotation=Quaternion.Euler(0,R(0,360),0);
            }
        var spawn=Group("Enemy placement - clear 6m wide x 4m high x 6m deep",cave);
        spawn.localPosition=V(0,2.5f,1);
        // Check the actual terrain and colliders along a broad entrance-to-chamber route.
        Physics.SyncTransforms();
        Vector3 chamber=cave.TransformPoint(V(0,2.2f,1));
        foreach(var hit in Physics.OverlapBox(chamber,V(3,2,3),cave.rotation))
            if(hit.transform.IsChildOf(cave)) throw new Exception("Enemy chamber obstructed: "+hit.name);
        for(int z=-10;z<=4;z+=2)
        {
            Vector3 p=cave.TransformPoint(V(0,0,z));
            float ground=Ground(p.x,p.z);
            Vector3 center=V(p.x,ground+2.2f,p.z);
            foreach(var hit in Physics.OverlapBox(center,V(2.5f,1.8f,.8f),cave.rotation))
                if(hit.transform.IsChildOf(cave)) throw new Exception("Cave clearance obstructed at "+z+": "+hit.name);
        }
        Vector3 point=spawn.position; point.y=Ground(point.x,point.z)+2; spawn.position=point;
        Debug.Log("Cave enemy marker: "+spawn.position+"; approach cleared for 5m wide / 3.6m high volume.");
    }

    static void Capture()
    {
        Directory.CreateDirectory("Previews");
        var camera=Camera.main;
        if(camera==null) return;
        Shot(camera,V(27,8,22),V(51,4,51),"Cave");
        Shot(camera,V(77,14,29),V(51,6,51),"CaveSide");
        Shot(camera,V(-18,5,-49),V(-26,1,-26),"Gardens");
        Shot(camera,V(0,100,-85),V(0,0,0),"Overview");
    }
    static void Shot(Camera camera,Vector3 position,Vector3 target,string name)
    {
        camera.transform.position=position; camera.transform.LookAt(target);
        var rt=new RenderTexture(1280,800,24); camera.targetTexture=rt;
        camera.Render(); RenderTexture.active=rt;
        var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,800),0,0); image.Apply();
        File.WriteAllBytes("Previews/"+name+".png",image.EncodeToPNG());
        camera.targetTexture=null; RenderTexture.active=null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
    }
}

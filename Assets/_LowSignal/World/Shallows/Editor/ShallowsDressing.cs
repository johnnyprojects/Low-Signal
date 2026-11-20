using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Baked, editable scene objects. Only the dunes and reef edge tool reshapes the terrain, always from a saved copy of
// the original heights, so it can be rerun; it never touches lighting.
public static class ShallowsDressing
{
    const string ScenePath = "Assets/_LowSignal/Scenes/Shallows.unity";
    const string AssetRoot = "Assets/_LowSignal/World/Shallows/Dressing";
    const string BaseTerrainPath = "Assets/_LowSignal/World/Shallows/Shallows terrain - original heights.asset";
    const string GroupName = "Shallows - Rock gardens and enemy cave";
    const string EdgeGroupName = "Shallows - Reef edge";
    const string SandRocksGroupName = "Shallows - Open sand rocks";
    const string FormationsGroupName = "Shallows - Rock formations";
    const int FormationCount = 8;
    // The reef bank starts this far (rounded-square distance) from the map centre, give or take BankWobble.
    const float BankStart = 70, BankWobble = 4, BankRise = 12;
    // Rocks and sand stay this far under the water surface, so nothing pokes through it.
    const float SurfaceClearance = 1.8f;
    static readonly Vector2[] GardenCenters={new Vector2(-48,-43),new Vector2(-22,-31),new Vector2(23,-38),new Vector2(49,-24),new Vector2(-53,-9),new Vector2(-28,15),new Vector2(25,9),new Vector2(-48,45),new Vector2(-12,49),new Vector2(16,36),new Vector2(56,12),new Vector2(7,-57),new Vector2(-62,20),new Vector2(38,-55)};
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
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode first.");
        if (SceneManager.GetActiveScene().path != ScenePath) throw new Exception("Open Shallows first.");
        Build();
    }

    [MenuItem("Low Signal/Shallows/Rebuild sand, rocks and reef edge")]
    public static void RebuildEdge()
    {
        // Terrain edits persist out of Play mode but scene edits are discarded, which would leave the two mismatched.
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode first.");
        if (SceneManager.GetActiveScene().path != ScenePath) throw new Exception("Open Shallows first.");
        LoadAssets();
        Sculpt();
        Build();
        BuildEdge();
        BuildFormations();
        BuildSandRocks();
        SettleGrass();
    }

    public static void EdgeBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            LoadAssets();
            Sculpt();
            Build();
            BuildEdge();
            BuildFormations();
            BuildSandRocks();
            SettleGrass();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Capture();
            Debug.Log("EDGE_PASS: dunes and reef edge saved; previews captured.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Screenshots the saved scene as it is, without rebuilding anything.
    public static void PreviewBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
            Capture();
            Debug.Log("PREVIEW_PASS: previews captured.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Builds the formations and scattered rocks on the saved scene and screenshots them, without saving anything.
    public static void FormationsPreviewBatch() => FormationsBatch(false);
    // The same, saving the scene; the terrain, edge, gardens and cave are left as they are.
    public static void FormationsSaveBatch() => FormationsBatch(true);

    static void FormationsBatch(bool save)
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            LoadAssets();
            BuildFormations();
            BuildSandRocks();
            SettleGrass();
            if (save) EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Capture();
            Debug.Log("FORMATIONS_PASS: previews captured; scene " + (save ? "saved." : "not saved."));
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
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
            random = new System.Random(201027);
            LoadAssets();
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

    // Existing assets are reused, so this only creates them on the first build.
    static void LoadAssets()
    {
        terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        if (terrain == null) throw new Exception("Shallows terrain missing.");
        Directory.CreateDirectory(AssetRoot); AssetDatabase.Refresh();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_LowSignal/World/SeaFloor/Models/Boulder.obj");
        rock = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        stone=Mat("Algae limestone",new Color(.30f,.38f,.31f));
        pale=Mat("Shell limestone",new Color(.56f,.53f,.39f));
        green=Mat("Seagrass jade",new Color(.16f,.34f,.22f),true);
        amber=Mat("Seagrass olive",new Color(.35f,.39f,.16f),true);
        grass=GrassMesh();
    }

    static void Build()
    {
        random = new System.Random(201026);
        LoadAssets();
        var old=GameObject.Find(GroupName);
        if(old!=null) Undo.DestroyObjectImmediate(old);
        var root=Group(GroupName,null); Undo.RegisterCreatedObjectUndo(root.gameObject,"Dress Shallows");
        var gardens=Group("Rock gardens - open sand routes between clusters",root);
        var centers=GardenCenters;
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

    static Vector3 Centre => terrain.transform.position+terrain.terrainData.size*.5f;
    static float Surface()
    {
        var water=GameObject.Find("Water Surface");
        return water!=null?water.transform.position.y:terrain.transform.position.y+terrain.terrainData.size.y;
    }
    static Vector2 Marker(string name,Vector2 fallback)
    {
        var go=GameObject.Find(name);
        return go!=null?new Vector2(go.transform.position.x,go.transform.position.z):fallback;
    }
    static float Smooth(float from,float to,float x) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(from,to,x));

    // Rounded-square distance from the map centre, so the bank follows the terrain without square corners.
    static float Rho(float x,float z) => Mathf.Pow(Mathf.Pow(Mathf.Abs(x),4)+Mathf.Pow(Mathf.Abs(z),4),.25f);
    // Point at rounded-square distance r along angle a, relative to the map centre.
    static Vector2 OnRing(float a,float r)
    {
        var d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
        return d*r/Rho(d.x,d.y);
    }
    // Where the bank starts in the direction of (x,z); two noise octaves around the ring keep the shoreline irregular.
    static float BankEdge(float x,float z)
    {
        float a=Mathf.Atan2(z,x),cx=Mathf.Cos(a),cz=Mathf.Sin(a);
        float wobble=(Mathf.PerlinNoise(cx*1.3f+11,cz*1.3f+7)-.5f)*1.4f+(Mathf.PerlinNoise(cx*4+3,cz*4+5)-.5f)*.6f;
        return BankStart+wobble*2*BankWobble;
    }

    // Broad rolling rises and hollows across the whole floor, about 0-5m.
    static float Hills(float x,float z) =>
        Mathf.PerlinNoise(x*.016f+120,z*.016f+80)*3.6f+Mathf.PerlinNoise(x*.04f+5,z*.04f+55)*1.4f;

    // Lopsided dunes on top of the hills: a warped long swell, cross ripples, and patches of tall dunes with a long
    // gentle back and a steep slip face on the lee side, up to about 5m in all.
    static float Dune(float x,float z)
    {
        float warp=Mathf.PerlinNoise(x*.03f+40,z*.03f+40)*6;
        float a=(x*.87f+z*.5f)/17*Mathf.PI*2+warp;
        float swell=Mathf.Pow(.5f+.5f*Mathf.Sin(a+.6f*Mathf.Sin(a)),1.5f);
        float b=(x*.34f-z*.94f)/6.5f*Mathf.PI*2+warp*1.7f;
        float patch=Smooth(.42f,.6f,Mathf.PerlinNoise(x*.017f+90,z*.017f+60));
        float along=Mathf.Repeat((x*.5f-z*.87f)/28+Mathf.PerlinNoise(x*.025f+15,z*.025f+33)*1.5f,1);
        float crest=along<.8f?Mathf.SmoothStep(0,1,along/.8f):1-Mathf.SmoothStep(0,1,(along-.8f)/.2f);
        return swell*1.2f+(.5f+.5f*Mathf.Sin(b))*.25f+crest*patch*3.5f;
    }

    // Rebuilds the heights from the saved original: hills and dunes over the floor, softened around the rock gardens,
    // and a rising reef bank around the rim. The rock gardens and cave are rebuilt on the result afterwards.
    static void Sculpt()
    {
        var data=terrain.terrainData;
        if(AssetDatabase.LoadAssetAtPath<TerrainData>(BaseTerrainPath)==null&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(data),BaseTerrainPath))
            throw new Exception("Could not save the original terrain heights.");
        var source=AssetDatabase.LoadAssetAtPath<TerrainData>(BaseTerrainPath);
        int res=data.heightmapResolution;
        if(source.heightmapResolution!=res) throw new Exception("Heightmap resolution changed; delete "+BaseTerrainPath+" to save the current heights as the original.");
        float[,] original=source.GetHeights(0,0,res,res),heights=new float[res,res];
        Vector3 min=terrain.transform.position,size=data.size,c=Centre;
        float bankTop=Surface()-SurfaceClearance-1.5f-min.y,highest=0;
        Vector2 cave=Marker("NE cave - entrance faces southwest",new Vector2(51,51));
        Vector2 offering=Marker("Offering area - place three treasures in ring",new Vector2(41,31));
        for(int i=0;i<res;i++)
            for(int j=0;j<res;j++)
            {
                float x=min.x+size.x*j/(res-1),z=min.z+size.z*i/(res-1),lx=x-c.x,lz=z-c.z;
                var p=new Vector2(x,z);
                float h=original[i,j]*source.size.y;
                float caveKeep=Smooth(16,28,Vector2.Distance(p,cave));
                // The guardian's cave, route and offering ring sit in a wide, gently rimmed hollow at the original height.
                float keep=Mathf.Min(Smooth(18,38,Vector2.Distance(p,cave)),Smooth(8,24,Vector2.Distance(p,offering)));
                float garden=1;
                foreach(var g in GardenCenters) garden=Mathf.Min(garden,Mathf.Lerp(.5f,1,Smooth(8,14,Vector2.Distance(p,g))));
                float sand=h+(Hills(x,z)+Dune(x,z)*garden)*keep;
                // Lumpy noise keeps the bank from reading as a smooth ramp; its peaks still stay at bankTop.
                float bank=bankTop-1.2f+(Mathf.PerlinNoise(x*.09f+70,z*.09f+20)-.5f)*2.4f;
                float rise=Smooth(0,BankRise,Rho(lx,lz)-BankEdge(lx,lz))*caveKeep;
                heights[i,j]=Mathf.Max(0,Mathf.Lerp(sand,Mathf.Max(sand,bank),rise));
                highest=Mathf.Max(highest,heights[i,j]);
            }
        Undo.RegisterCompleteObjectUndo(data,"Sculpt Shallows dunes");
        float height=Mathf.Max(source.size.y,highest+2);
        data.size=V(size.x,height,size.z);
        for(int i=0;i<res;i++) for(int j=0;j<res;j++) heights[i,j]/=height;
        data.SetHeights(0,0,heights);
        EditorUtility.SetDirty(data);
        SettleProps();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // Sets the treasures and offering ring down on the new sand and lifts the player start clear of it. Placed from
    // the ground up rather than shifted, so it comes out right whatever state the scene was in.
    static void SettleProps()
    {
        var guardian=GameObject.Find("Shallows Treasure Guardian");
        if(guardian!=null)
            foreach(Transform t in guardian.transform)
                if(t.name.StartsWith("Guardian treasure")||t.name.StartsWith("Offering area"))
                {
                    Undo.RecordObject(t,"Settle Shallows props");
                    var renderers=t.GetComponentsInChildren<Renderer>();
                    float bottom=t.position.y;
                    if(renderers.Length>0)
                    {
                        bottom=renderers[0].bounds.min.y;
                        foreach(var r in renderers) bottom=Mathf.Min(bottom,r.bounds.min.y);
                    }
                    t.position+=Vector3.up*(Ground(t.position.x,t.position.z)-.05f-bottom);
                }
        var player=GameObject.Find("Player");
        if(player!=null)
        {
            Undo.RecordObject(player.transform,"Settle Shallows props");
            var p=player.transform.position;
            player.transform.position=V(p.x,Ground(p.x,p.z)+2,p.z);
        }
    }

    // Reef rocks scattered up the bank, kept under the surface; an invisible limit just up the bank foot stops the
    // player and camera before the terrain runs out.
    static void BuildEdge()
    {
        random=new System.Random(201028);
        var old=GameObject.Find(EdgeGroupName);
        if(old!=null) Undo.DestroyObjectImmediate(old);
        var root=Group(EdgeGroupName,null); Undo.RegisterCreatedObjectUndo(root.gameObject,"Build Shallows edge");
        Vector3 c=Centre,min=terrain.transform.position;
        float surface=Surface();
        Vector2 At(float a,float r) => OnRing(a,r)+new Vector2(c.x,c.z);
        // The limit sits a little up the bank, and never closer than the back of the cave.
        float Limit(float a) => Mathf.Max(BankEdge(Mathf.Cos(a),Mathf.Sin(a))+3,71);

        var outcrops=Group("Reef outcrops",root);
        var foot=Group("Bank talus and seagrass",root);
        for(float a=0;a<Mathf.PI*2;)
        {
            float edge=BankEdge(Mathf.Cos(a),Mathf.Sin(a)),limit=Limit(a);
            for(int k=random.Next(1,4);k>0;k--)
            {
                float r=edge+R(.5f,BankRise),s=R(2.5f,7);
                Boulder("Reef outcrop",outcrops,At(a+R(-.04f,.04f),r),s,R(-.25f,.05f),r-s<limit+2);
            }
            for(int k=0;k<4;k++)
            {
                float r=edge+R(-5,4);
                Boulder("Fallen talus",foot,At(a+R(-.05f,.05f),r),R(.5f,2.2f),R(.05f,.3f),r<limit+3);
            }
            for(int k=0;k<5;k++) Tuft("Bank seagrass",foot,At(a+R(-.05f,.05f),edge+R(-9,3)));
            a+=R(5,9)/edge;
        }

        var limits=Group("Swim limit (invisible)",root);
        const int segments=120;
        for(int i=0;i<segments;i++)
        {
            float a0=i*Mathf.PI*2/segments,a1=(i+1)*Mathf.PI*2/segments;
            Vector2 p0=At(a0,Limit(a0)),p1=At(a1,Limit(a1)),d=p1-p0,mid=(p0+p1)*.5f;
            var segment=new GameObject("Limit "+i);
            segment.transform.SetParent(limits,false);
            segment.transform.SetPositionAndRotation(V(mid.x,(min.y-10+surface+12)*.5f,mid.y),Quaternion.LookRotation(V(d.x,0,d.y)));
            segment.AddComponent<BoxCollider>().size=V(2,surface+22-min.y,d.magnitude+.6f);
            segment.isStatic=true;
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // Places that scattered rocks and formations stay out of: the cave, the guardian's route to the offering ring,
    // the rock gardens, the treasures and the player start.
    static List<(Vector2 at,float radius)> KeepClear()
    {
        Vector2 cave=Marker("NE cave - entrance faces southwest",new Vector2(51,51));
        Vector2 offering=Marker("Offering area - place three treasures in ring",new Vector2(41,31));
        var clear=new List<(Vector2 at,float radius)>{(cave,26),(offering,12)};
        for(float t=0;t<=1;t+=.1f) clear.Add((Vector2.Lerp(cave,offering,t),8));
        foreach(var g in GardenCenters) clear.Add((g,13));
        foreach(var name in new[]{"Guardian treasure 1","Guardian treasure 2","Guardian treasure 3","Player"})
        {
            var go=GameObject.Find(name);
            if(go!=null) clear.Add((new Vector2(go.transform.position.x,go.transform.position.z),6));
        }
        return clear;
    }

    static Vector2 RandomFloorPoint() => new Vector2(Centre.x+R(-75,75),Centre.z+R(-75,75));

    // True when p is at least margin past every clear zone and bankGap short of the reef bank.
    static bool Open(Vector2 p,List<(Vector2 at,float radius)> clear,float margin,float bankGap)
    {
        float lx=p.x-Centre.x,lz=p.y-Centre.z;
        if(Rho(lx,lz)>BankEdge(lx,lz)-bankGap) return false;
        foreach(var (at,radius) in clear) if(Vector2.Distance(p,at)<radius+margin) return false;
        return true;
    }

    // Large swim-around landmarks in the cave's style: arches, swim-through grottos, rock spires and outcrops.
    // Each is sunk or dropped if it would break the water surface, and dropped if its opening comes out blocked.
    static void BuildFormations()
    {
        random=new System.Random(201030);
        var old=GameObject.Find(FormationsGroupName);
        if(old!=null) Undo.DestroyObjectImmediate(old);
        // The scattered rocks are rebuilt around the formations, so the old ones must not block their sites.
        var oldRocks=GameObject.Find(SandRocksGroupName);
        if(oldRocks!=null) Undo.DestroyObjectImmediate(oldRocks);
        var root=Group(FormationsGroupName,null); Undo.RegisterCreatedObjectUndo(root.gameObject,"Build Shallows rock formations");
        var clear=KeepClear();
        float ceiling=Surface()-SurfaceClearance;
        var built=new List<Vector2>();
        int attempts=0;
        string[] kinds={"Arch","Swim-through grotto","Rock spires","Rock outcrop"};
        var failed=new int[kinds.Length];
        var blocked=new int[kinds.Length];
        for(int tries=0;tries<1500&&built.Count<FormationCount;tries++)
        {
            var p=RandomFloorPoint();
            if(!Open(p,clear,6,10)) continue;
            bool crowded=false;
            foreach(var other in built) crowded|=Vector2.Distance(p,other)<22;
            if(crowded||Occupied(V(p.x,Ground(p.x,p.y)+3,p.y),7)) continue;
            // Cycle through the kinds per attempt, so one that keeps failing cannot crowd out the rest.
            int kind=attempts++%kinds.Length;
            var f=Group(kinds[kind]+" "+(built.Count+1).ToString("00"),root);
            f.position=V(p.x,Ground(p.x,p.y),p.y);
            f.rotation=Quaternion.Euler(0,R(0,360),0);
            bool clearInside=kind==0?Arch(f):kind==1?Grotto(f):kind==2?Spires(f):Outcrop(f);
            float top=float.MinValue;
            foreach(var r in f.GetComponentsInChildren<Renderer>()) top=Mathf.Max(top,r.bounds.max.y);
            if(!clearInside||top-ceiling>3) { failed[kind]++; if(!clearInside) blocked[kind]++; UnityEngine.Object.DestroyImmediate(f.gameObject); continue; }
            if(top>ceiling) f.position+=Vector3.down*(top-ceiling);
            for(int k=0;k<14;k++)
            {
                float a=R(0,Mathf.PI*2),r=R(6,13);
                Tuft("Formation seagrass",f,p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r);
            }
            built.Add(p);
        }
        Debug.Log("Rock formations built: "+built.Count+"; dropped (blocked or too tall): "+string.Join(", ",kinds.Select((k,i)=>k+" "+failed[i]+" ("+blocked[i]+" blocked)")));
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // A boulder in a formation at local (x,z), its centre y above the sand under it (or above the formation's base).
    static GameObject Block(string name,Transform f,float x,float y,float z,Vector3 scale,Vector3 tilt,bool onSand=true)
    {
        Vector3 w=f.TransformPoint(V(x,0,z));
        float baseY=onSand?Ground(w.x,w.z)-f.position.y:0;
        var b=Piece(name,f,rock,R(0,1)<.3f?pale:stone,V(x,baseY+y,z),scale,true);
        b.transform.localRotation=Quaternion.Euler(tilt);
        return b;
    }

    static void Scree(Transform f,int count,float from,float to)
    {
        for(int i=0;i<count;i++)
        {
            float a=R(0,Mathf.PI*2),r=R(from,to),s=R(.8f,1.8f);
            Block("Scree",f,Mathf.Cos(a)*r,s*.3f,Mathf.Sin(a)*r,V(s*R(1,1.4f),s,s),V(R(-15,15),R(0,360),R(-15,15)));
        }
    }

    // True when nothing in the formation intrudes into the given local box.
    static bool Clear(Transform f,Vector3 centre,Vector3 half)
    {
        Physics.SyncTransforms();
        foreach(var hit in Physics.OverlapBox(f.TransformPoint(centre),half,f.rotation))
            if(hit.transform.IsChildOf(f)) return false;
        return true;
    }

    // The boulder mesh is about 2m across at scale 1, so a block's half-size roughly equals its scale.
    // A craggy mass of overlapping boulders filling a tapering elliptical column, built bottom-up like the cave walls:
    // bigger, flatter pieces low down, each layer sunk deep into the one below so the whole reads as one rock, not a
    // stack. Tall pieces make spires. Returns the height of its top above the formation's base.
    static float Mass(string name,Transform f,float cx,float cz,float rx,float rz,float height,float taper,bool tall=false)
    {
        float top=0;
        for(float y=-.8f;y<height;)
        {
            float t=Mathf.Clamp01(y/height),shrink=1-taper*t,ex=rx*shrink,ez=rz*shrink,layer=0;
            int count=Mathf.Max(tall?2:1,Mathf.RoundToInt(ex*ez/1.8f));
            for(int i=0;i<count;i++)
            {
                float a=R(0,Mathf.PI*2),d=Mathf.Sqrt(R(0,1))*.8f;
                float s=R(1.8f,2.6f)*Mathf.Lerp(1.15f,.75f,t),h=s*(tall?R(1.2f,1.6f):R(.6f,.85f));
                var b=Block(name,f,cx+Mathf.Cos(a)*ex*d,y+h*.5f,cz+Mathf.Sin(a)*ez*d,V(s*R(1,1.35f),h,s*R(.9f,1.25f)),V(R(-18,18),R(0,360),R(-18,18)));
                layer=Mathf.Max(layer,h);
                top=Mathf.Max(top,b.transform.localPosition.y+h*.9f);
            }
            y+=layer*R(.7f,.85f);
        }
        return top;
    }

    // Loose blocks resting on a formation's top, at a height above its base.
    static void Crown(Transform f,int count,float xHalf,float zHalf,float y)
    {
        for(int i=0;i<count;i++)
            Block("Crown rock",f,R(-xHalf,xHalf),y+R(-.2f,.4f),R(-zHalf,zHalf),V(R(1.4f,2.1f),R(.8f,1.2f),R(1.3f,1.9f)),V(R(-22,22),R(0,360),R(-22,22)),false);
    }

    // Slanted slabs leaning against a formation's flanks break up its outline, like the cave's flank outcrops.
    static void Buttresses(Transform f,int count,float rx,float rz)
    {
        for(int i=0;i<count;i++)
        {
            float a=R(0,Mathf.PI*2);
            Vector3 dir=V(Mathf.Cos(a),0,Mathf.Sin(a));
            float lean=R(18,34);
            var b=Block("Leaning buttress",f,dir.x*rx,R(1.6f,2.6f),dir.z*rz,V(R(1.3f,1.8f),R(2.2f,3),R(1.2f,1.7f)),Vector3.zero);
            // Tilt the top in toward the formation.
            b.transform.localRotation=Quaternion.LookRotation(-dir)*Quaternion.Euler(lean,R(-25,25),R(-10,10));
        }
    }

    static bool Arch(Transform f)
    {
        float span=R(7.5f,8.5f),legHeight=R(5.8f,6.8f);
        float legTop=Mathf.Max(Mass("Arch leg",f,-span,0,2.4f,2,legHeight,.3f),Mass("Arch leg",f,span,0,2.4f,2,legHeight,.3f));
        // Three overlapping slabs make a slightly sagging, irregular span.
        for(int k=-1;k<=1;k++)
            Block("Arch span",f,k*span*.55f,legTop-.2f-(k==0?.35f:0),R(-.4f,.4f),V(span*.5f+1.4f,R(1.3f,1.6f),R(2,2.4f)),V(R(-6,6),R(-10,10),R(-8,8)),false);
        Crown(f,6,span*.8f,.9f,legTop+1.2f);
        Buttresses(f,4,span+2.5f,2.5f);
        Scree(f,7,span-1,span+5);
        return Clear(f,V(0,(legTop-1.8f)*.5f,0),V(span-5,(legTop-1.8f)*.5f-1,.8f));
    }

    static bool Grotto(Transform f)
    {
        float length=R(9,12),width=R(7,7.6f);
        float wallTop=Mathf.Max(Mass("Grotto wall",f,-width,0,2,length*.5f,5.2f,.25f),Mass("Grotto wall",f,width,0,2,length*.5f,5.2f,.25f));
        for(float z=-length*.5f;z<=length*.5f;z+=R(2.4f,3))
            Block("Grotto roof",f,R(-.5f,.5f),wallTop-.5f,z,V(width+1.5f,R(1.2f,1.5f),R(1.8f,2.2f)),V(R(-6,6),R(-8,8),R(-6,6)),false);
        Crown(f,6,width*.6f,length*.4f,wallTop+.9f);
        Buttresses(f,5,width+2.5f,length*.5f+1);
        Scree(f,9,width+1,width+6);
        return Clear(f,V(0,2.4f,0),V(2.4f,1.6f,length*.5f-1));
    }

    static bool Spires(Transform f)
    {
        Mass("Spire base",f,0,0,3.5f,3,2.6f,.25f);
        int count=random.Next(2,4);
        for(int i=0;i<count;i++)
        {
            float a=i*Mathf.PI*2/count+R(-.5f,.5f),r=R(1.2f,2.6f);
            Mass("Spire",f,Mathf.Cos(a)*r,Mathf.Sin(a)*r,1.1f,1,R(6,9),.55f,true);
        }
        Buttresses(f,4,4.5f,4);
        Scree(f,8,3,8);
        return true;
    }

    // A broad, craggy rock hill with a taller spur off-centre.
    static bool Outcrop(Transform f)
    {
        float rx=R(4,5),rz=R(3,4);
        Mass("Outcrop",f,0,0,rx,rz,R(4.5f,5.5f),.45f);
        Mass("Outcrop spur",f,R(-1.5f,1.5f),R(.5f,1.5f),1.8f,1.6f,R(6.5f,8),.4f);
        Buttresses(f,5,rx+2.5f,rz+2.5f);
        Scree(f,9,rx+1,rx+6);
        return true;
    }

    // Lone boulders, small clusters and half-buried slabs across the open sand, kept clear of the gardens, the cave,
    // the guardian's route to the offering ring, the treasures and the player start.
    static void BuildSandRocks()
    {
        random=new System.Random(201029);
        var old=GameObject.Find(SandRocksGroupName);
        if(old!=null) Undo.DestroyObjectImmediate(old);
        var root=Group(SandRocksGroupName,null); Undo.RegisterCreatedObjectUndo(root.gameObject,"Scatter Shallows rocks");
        var clear=KeepClear();
        int placed=0;
        for(int tries=0;tries<600&&placed<55;tries++)
        {
            var p=RandomFloorPoint();
            if(!Open(p,clear,0,4)) continue;
            placed++;
            float kind=R(0,1);
            if(kind<.4f) Boulder("Lone boulder",root,p,R(1.4f,3.6f),R(-.1f,.25f),true);
            else if(kind<.75f)
                for(int k=random.Next(2,5);k>0;k--) Boulder("Clustered boulder",root,p+new Vector2(R(-3,3),R(-3,3)),R(.6f,2.4f),R(0,.3f),true);
            else Boulder("Buried slab",root,p,R(2.5f,4.5f),R(-.3f,-.1f),true,R(0,.5f));
            for(int k=random.Next(0,6);k>0;k--)
            {
                float a=R(0,Mathf.PI*2),r=R(2,4.5f);
                Tuft("Sand seagrass",root,p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r);
            }
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    [MenuItem("Low Signal/Shallows/Settle seagrass on the sand")]
    public static void SettleGrassMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode first.");
        if (SceneManager.GetActiveScene().path != ScenePath) throw new Exception("Open Shallows first.");
        SettleGrass();
    }

    public static void SettleGrassBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            SettleGrass();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Capture();
            Debug.Log("GRASS_PASS: seagrass settled; previews captured.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Roots every seagrass tuft at the lowest sand under its blades, so none hangs over a slope, a dip, or sand that
    // was reshaped after it was placed. Reports what it moved, grouped by the scene group it belongs to.
    static void SettleGrass()
    {
        terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetRoot + "/Ribbon seagrass.asset");
        var moved = new SortedDictionary<string, (int count, float worst)>();
        int total = 0;
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if (filter.sharedMesh != mesh) continue;
            total++;
            var t = filter.transform;
            // The blades root within half a metre of the tuft's centre at scale 1.
            float reach = 0.55f * t.lossyScale.x, low = Ground(t.position.x, t.position.z);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                low = Mathf.Min(low, Ground(t.position.x + Mathf.Cos(a) * reach, t.position.z + Mathf.Sin(a) * reach));
            }
            float target = low - 0.05f, off = t.position.y - target;
            if (Mathf.Abs(off) < 0.02f) continue;
            Undo.RecordObject(t, "Settle seagrass");
            t.position = V(t.position.x, target, t.position.z);
            string group = t.root.name;
            moved.TryGetValue(group, out var entry);
            moved[group] = (entry.count + 1, Mathf.Max(entry.worst, off));
        }
        foreach (var pair in moved)
            Debug.Log($"Seagrass settled in {pair.Key}: {pair.Value.count} tufts, worst {pair.Value.worst:0.00}m above the sand.");
        Debug.Log("Seagrass checked: " + total);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static bool InsideTerrain(Vector2 p)
    {
        Vector3 min=terrain.transform.position,size=terrain.terrainData.size;
        return p.x>min.x+1&&p.x<min.x+size.x-1&&p.y>min.z+1&&p.y<min.z+size.z-1;
    }

    // A rock resting on the sand at p, sunk if it would break the water surface. Shape picks a wide slab (<.5),
    // a rounded lump (<.85) or a pinnacle; left out, it is random.
    static void Boulder(string name,Transform parent,Vector2 p,float s,float bury,bool solid,float shape=-1)
    {
        if(!InsideTerrain(p)) return;
        var world=V(p.x,Ground(p.x,p.y)+s*bury,p.y);
        if(Occupied(world,s*.4f)) return;
        if(shape<0) shape=R(0,1);
        Vector3 scale=shape<.5f?V(s*R(1.2f,1.8f),s*R(.45f,.7f),s*R(1,1.5f))
            :shape<.85f?V(s,s*R(.8f,1.1f),s*R(.9f,1.2f)):V(s*.7f,s*R(1.3f,1.8f),s*.75f);
        var b=Piece(name,parent,rock,R(0,1)<.3f?pale:stone,world,scale,solid);
        b.transform.localRotation=Quaternion.Euler(R(-20,20),R(0,360),R(-20,20));
        float over=b.GetComponent<Renderer>().bounds.max.y-(Surface()-SurfaceClearance);
        if(over>0) b.transform.position+=Vector3.down*over;
    }

    static void Tuft(string name,Transform parent,Vector2 p)
    {
        var world=V(p.x,Ground(p.x,p.y)-.03f,p.y);
        if(!InsideTerrain(p)||Occupied(world,.4f)) return;
        var plant=Piece(name,parent,grass,R(0,1)<.25f?amber:green,world,Vector3.one*R(.7f,1.6f),false);
        // Placed in world space: formations parent their seagrass to a moved and turned group.
        plant.transform.SetPositionAndRotation(world,Quaternion.Euler(0,R(0,360),0));
    }

    static bool Occupied(Vector3 p,float radius)
    {
        Physics.SyncTransforms();
        foreach(var hit in Physics.OverlapSphere(p,radius))
            if(!(hit is TerrainCollider)) return true;
        return false;
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
        Shot(camera,V(-30,6,-45),V(-60,8,-78),"Edge");
        Shot(camera,V(0,150,-1),V(0,0,0),"TopDown");
        for(int i=0;i<4;i++)
        {
            float a=i*Mathf.PI*.5f+.4f;
            var from=V(Mathf.Cos(a)*20,0,Mathf.Sin(a)*20);
            if(terrain!=null) from.y=Ground(from.x,from.z)+3;
            Shot(camera,from,V(Mathf.Cos(a)*60,from.y,Mathf.Sin(a)*60),"Floor"+i);
        }
        var formations=GameObject.Find(FormationsGroupName);
        if(formations!=null)
            foreach(Transform f in formations.transform)
            {
                Shot(camera,f.TransformPoint(V(0,4,-20)),f.TransformPoint(V(0,3.5f,0)),"Formation - "+f.name+" front");
                Shot(camera,f.TransformPoint(V(16,6,-12)),f.TransformPoint(V(0,3.5f,0)),"Formation - "+f.name+" side");
            }
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

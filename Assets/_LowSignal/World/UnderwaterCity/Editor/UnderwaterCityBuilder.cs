using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Editor-only authoring: the saved level contains ordinary editable objects, no runtime generator.
public static partial class UnderwaterCityBuilder
{
    const string Root = "Assets/_LowSignal/World/UnderwaterCity";
    const string ScenePath = "Assets/_LowSignal/Scenes/UnderwaterCity.unity";
    static Material concrete, brick, pale, glass, asphalt, sand, rust, algae, line, beacon;
    static System.Random random;
    static Transform buildings, streets, dressing;
    static float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    [MenuItem("Low Signal/City/Create new tutorial city copy")]
    public static void CreateCopy()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build(AssetDatabase.GenerateUniqueAssetPath(ScenePath));
    }

    public static void BuildBatch()
    {
        try
        {
            Build(ScenePath);
            Validate();
            Capture();
            Debug.Log("CITY_BUILD_PASS: saved scene, verified collisions and routes, captured previews.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static Material Mat(string name, Color color, bool emissive = false)
    {
        string path = Root + "/Materials/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new Exception("URP Lit shader missing");
        material = new Material(shader) { name = name, enableInstancing = true };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.08f);
        if (emissive) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2f); }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Transform Group(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material mat, bool solid = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic = true;
        return go;
    }

    static void Build(string path)
    {
        random = new System.Random(221008);
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory("Assets/_LowSignal/Scenes");
        AssetDatabase.Refresh();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        concrete = Mat("Weathered concrete", new Color(.29f,.35f,.34f));
        brick = Mat("Silted red brick", new Color(.36f,.23f,.19f));
        pale = Mat("Limestone trim", new Color(.47f,.49f,.42f));
        glass = Mat("Opaque dead windows", new Color(.055f,.10f,.11f));
        asphalt = Mat("Broken asphalt", new Color(.105f,.145f,.14f));
        sand = Mat("Grey green silt", new Color(.33f,.38f,.29f));
        rust = Mat("Oxidized steel", new Color(.28f,.17f,.10f));
        algae = Mat("Dark algae", new Color(.12f,.23f,.17f));
        line = Mat("Faded road paint", new Color(.51f,.49f,.32f));
        beacon = Mat("Facility marker cyan", new Color(.18f,.72f,.62f), true);
        streets = Group("01 - Streets and seabed");
        buildings = Group("02 - City blocks (sealed exteriors)");
        dressing = Group("04 - Cars, rubble and marine growth");
        // The former access shaft is closed; fountain access is future gameplay.
        // Keep a single recessed floor beneath the modular slabs. The visible slabs
        // still define the district, while this underlay prevents camera/fog views
        // from exposing the small construction seams between them.
        Box("Continuous seabed underlay", streets, V(0,-4.08f,0), V(440,8.16f,440), sand);
        Box("Seabed west", streets, V(-112,-4,0), V(216,8,440), sand);
        Box("Seabed east", streets, V(112,-4,0), V(216,8,440), sand);
        Box("Seabed south", streets, V(0,-4,-108), V(8,8,224), sand);
        Box("Seabed north", streets, V(0,-4,120), V(8,8,200), sand);
        Box("Closed ground beneath fountain", streets, V(0,-4,12), V(8,8,16), sand);
        foreach (float x in new[] {-108f,0f,108f}) Road(true, x);
        foreach (float z in new[] {-108f,0f,108f}) Road(false, z);
        Intersections();
        CityBlocks();
        Building("Plaza landmark - fractured civic tower", V(48,0,26),20,24,108,false,true);
        Building("Plaza west - municipal offices", V(-48,0,26),20,24,32,true,true);
        Building("Plaza north - former records office", V(-26,0,49),24,18,23,true,false);
        Building("Transit rear - sealed offices", V(32,0,54),30,16,26,false,true);
        Building("Collapsed west tower - swim above or around", CollapsedTowerPosition,16,17,60,false,true);
        buildings.Find("Collapsed west tower - swim above or around").rotation=Quaternion.Euler(0,0,68);
        AddCollapsedTowerSupport(buildings.Find("Collapsed west tower - swim above or around"));
        OpenBuilding("Explore A - corner shop", V(-48,0,-24), 0);
        OpenBuilding("Explore B - former transit waiting room", V(32,0,78), 180);
        Plaza();
        for (int i=0; i<28; i++)
        {
            bool alongZ = i % 2 == 0;
            float lane = new[] {-108f,0f,108f}[i % 3];
            float travel = R(-172,172);
            if (Mathf.Abs(lane)<1 && Mathf.Abs(travel)<42) travel += travel<0 ? -50:50;
            Car(V(alongZ ? lane+R(-6,6) : travel,.5f,alongZ ? travel : lane+R(-6,6)), R(-15,15)+(alongZ?0:90), i);
        }
        for (int i=0; i<180; i++)
        {
            Vector3 p = V(R(-182,182),.25f,R(-182,182));
            if (Mathf.Abs(p.x)<17 || (Mathf.Abs(p.x)<35 && Mathf.Abs(p.z)<40)) continue;
            var rubble = Box("Settled masonry", dressing,p,V(R(.5f,3.5f),R(.3f,2),R(.7f,3)), i%3==0?brick:concrete);
            rubble.transform.localRotation=Quaternion.Euler(R(-15,15),R(0,180),R(-12,12));
        }
        for (int i=0; i<90; i++)
        {
            Vector3 p=V(R(-185,185),.05f,R(-185,185));
            if (Mathf.Abs(p.x)<20 && Mathf.Abs(p.z)<44) continue;
            Box("Silt drift",dressing,p,V(R(3,10),R(.15f,.65f),R(2,7)),sand,false).transform.localRotation=Quaternion.Euler(0,R(0,180),0);
            if (i%3!=0) continue;
            for(int j=0;j<3;j++)
                Box("Marine growth",dressing,p+V(j*.5f,1.2f,0),V(.15f,R(1,4),.38f),algae,false).transform.localRotation=Quaternion.Euler(0,R(0,180),R(-20,20));
        }
        Boundary();
        Atmosphere();
        Player();
        var hooks = Group("06 - Future cutscene destinations (markers only)");
        Group("West district transition",hooks).position=V(-174,5,0);
        Group("East district transition",hooks).position=V(174,5,0);
        Group("Facility relic destination - future content",hooks).position=V(0,-12,40);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),path);
        CitySurfaceCleanup.Repair();
        ConvertBuildingsToPrefabs(true);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),path);
    }

    static void Road(bool alongZ, float offset)
    {
        var root=Group((alongZ?"North-south ":"East-west ")+offset,streets);
        root.position=alongZ?V(offset,0,0):V(0,0,offset);
        if(!alongZ) root.rotation=Quaternion.Euler(0,90,0);
        // A 19 m carriageway meets 4 m sidewalks. Junctions are separate clear tiles.
        float plazaHalf=40;
        float roadStart=-180, walkStart=-180;
        foreach(float crossing in new[]{-108f,0f,108f})
        {
            bool plaza=offset==0 && crossing==0;
            float roadHalf=plaza?plazaHalf:9.5f;
            float walkHalf=plaza?plazaHalf:13.5f;
            RoadSection(root,roadStart,crossing-roadHalf);
            WalkSection(root,walkStart,crossing-walkHalf);
            roadStart=crossing+roadHalf;
            walkStart=crossing+walkHalf;
        }
        RoadSection(root,roadStart,180);
        WalkSection(root,walkStart,180);
        for(float z=-170;z<=170;z+=10)
        {
            bool junction=false;
            foreach(float crossing in new[]{-108f,0f,108f})
                if(Mathf.Abs(z-crossing)<(offset==0 && crossing==0?plazaHalf+4:16)) junction=true;
            if(!junction) Box("Faded centre marking",root,V(0,.215f,z),V(.14f,.02f,4),line,false);
        }
    }

    static void RoadSection(Transform root,float start,float end)
    {
        const float seamOverlap=.08f;
        Box("Connected carriageway",root,V(0,.10f,(start+end)/2),V(19,.20f,end-start+seamOverlap*2),asphalt);
    }

    static void WalkSection(Transform root,float start,float end)
    {
        foreach(float side in new[]{-1f,1f})
        {
            const float seamOverlap=.08f;
            Box("Sidewalk",root,V(side*11.5f,.3f,(start+end)/2),V(4,.6f,end-start+seamOverlap*2),concrete);
            Box("Curb",root,V(side*9.65f,.38f,(start+end)/2),V(.3f,.76f,end-start+seamOverlap*2),pale);
            for(float z=start+12;z<end-8;z+=32)
            {
                var lamp=Group("Dead street light",root);
                lamp.localPosition=V(side*11.5f,.6f,z);
                lamp.localRotation=Quaternion.Euler(0,side<0?180:0,0);
                Box("Pole",lamp,V(0,4,0),V(.18f,8,.18f),rust);
                Box("Arm",lamp,V(-1,8,0),V(2.3f,.2f,.2f),rust);
                Box("Empty lamp housing",lamp,V(-2,7.8f,0),V(1,.25f,.55f),glass);
            }
        }
    }

    static void Intersections()
    {
        foreach(float x in new[]{-108f,0f,108f})
        foreach(float z in new[]{-108f,0f,108f})
        {
            if(x==0 && z==0) continue;
            var root=Group("Clear junction "+x+", "+z,streets);root.position=V(x,0,z);
            Box("Intersection asphalt",root,V(0,.10f,0),V(19,.20f,19),asphalt);
            foreach(float sx in new[]{-1f,1f}) foreach(float sz in new[]{-1f,1f})
                Box("Sidewalk corner",root,V(sx*11.5f,.3f,sz*11.5f),V(4,.6f,4),concrete);
        }
    }

    static void CityBlocks()
    {
        float[] lo={-183,-94,14,122};
        float[] hi={-122,-14,94,183};
        for(int bx=0;bx<4;bx++) for(int bz=0;bz<4;bz++)
        for(int lx=0;lx<2;lx++) for(int lz=0;lz<2;lz++)
        {
            float width=(hi[bx]-lo[bx]-8)/2;
            float depth=(hi[bz]-lo[bz]-8)/2;
            float x=lo[bx]+width/2+lx*(width+8);
            float z=lo[bz]+depth/2+lz*(depth+8);
            if(Mathf.Abs(x)<40 && Mathf.Abs(z)<40) continue;
            if(x==32 && z==76) continue; // Reserved transit interior.
            if(x<-120 && z==32) continue; // Collapsed tower footprint.
            bool masonry=(lx+lz+bx+bz)%3!=0;
            float height=lx==0 && lz==0?R(48,86):R(16,39);
            Building("Block "+(bx*4+bz+1).ToString("00")+" / Lot "+(lx*2+lz+1)+
                (masonry?" - brick apartments":" - concrete offices"),
                V(x,0,z),width-2,depth-2,height,masonry,(bx+bz+lx+lz)%4==0);
        }
    }

    static void Building(string name, Vector3 position,float w,float d,float h,bool masonry,bool ruined)
    {
        var root=Group(name,buildings); root.position=position;
        var body=masonry?brick:concrete;
        // A continuous solid body seals every window, including the damaged upper silhouette.
        Box("Sealed structural mass",root,V(0,h/2,0),V(w,h,d),body);
        // Keep the stain clearly in front of the structural wall.  A 2.5 cm
        // offset was close enough to z-fight at shallow viewing angles.
        Box("Algae tide stain",root,V(0,1.4f,0),V(w+.20f,2.8f,d+.20f),algae,false);
        int floors=Mathf.FloorToInt(h/4.5f);
        for(int floor=1;floor<floors;floor++)
        {
            float y=floor*4.5f;
            if(floor%2==0) Box("Weathered floor band",root,V(0,y-1.7f,0),V(w+.35f,.25f,d+.35f),pale,false);
            foreach(float side in new[]{-1f,1f})
            {
                for(float x=-w/2+3;x<w/2-1;x+=4.8f)
                    Box("Blocked window",root,V(x,y,side*(d/2+.025f)),V(2.2f,2.5f,.09f),random.NextDouble()<.15?rust:glass,false);
                for(float z=-d/2+3;z<d/2-1;z+=4.8f)
                    Box("Blocked window",root,V(side*(w/2+.025f),y,z),V(.09f,2.5f,2.2f),random.NextDouble()<.12?body:glass,false);
            }
        }
        if(ruined)
        {
            // Consume the old roof's random draws so later buildings keep their layout.
            for(int i=0;i<7;i++) { R(-w/2,w/2); R(1,3); R(-d/2,d/2); R(3,6); }
            for(int i=0;i<4;i++)
                FacadeDamage(root,w,h,d,R(2,h*.6f),R(-d*.4f,d*.4f),i);
        }
        Roof(root,w,d,h,ruined);
    }

    static void OpenBuilding(string name,Vector3 position,float yaw)
    {
        var root=Group(name,buildings);root.position=position;root.rotation=Quaternion.Euler(0,yaw,0);
        Box("Floor",root,V(0,.4f,0),V(20,.8f,18),concrete);
        Box("Roof",root,V(0,8,0),V(20,.8f,18),concrete);
        Box("Rear wall",root,V(0,4,8.5f),V(20,8,1),brick);
        foreach(float side in new[]{-1f,1f})
        {
            Box("Side wall",root,V(side*9.5f,4,0),V(1,8,18),brick);
            Box("Front wall beside open entrance",root,V(side*6.5f,4,-8.5f),V(7,8,1),brick);
            Box("Open door jamb",root,V(side*3.1f,3,-9.1f),V(.3f,6,.3f),pale);
        }
        Box("Entrance lintel",root,V(0,7,-8.5f),V(6,2,1),pale);
        Box("Interior counter",root,V(-5,1.6f,3),V(5,1.6f,2),rust);
        Box("Abandoned bench",root,V(6,1.1f,3),V(2,.6f,5),concrete);
        Box("Entrance marker",root,V(0,6,-9.1f),V(3,.18f,.15f),beacon,false);
        Roof(root,20,18,8.4f,false);
    }

    static void Car(Vector3 p,float yaw,int index)
    {
        // All wrecks belong on the asphalt.  Banking a whole car raises one side
        // above the roadway and made several of them appear to float.
        if (index%9!=0) R(-5,5); // Preserve the original conditional random draw.
        var root=Group("Wreck "+index.ToString("00"),dressing); root.position=V(p.x,CarRoadHeight,p.z);
        root.rotation=Quaternion.Euler(0,yaw,0);
        Box("Corroded sedan body",root,V(0,.65f,0),V(2,.8f,4.5f),index%3==0?concrete:rust);
        Box("Opaque cabin",root,V(0,1.3f,-.2f),V(1.75f,.65f,2.2f),glass);
        Box("Collapsed roof",root,V(0,1.66f,-.2f),V(1.8f,.12f,2.3f),rust);
        foreach(float x in new[]{-1f,1f}) foreach(float z in new[]{-1.4f,1.4f})
            Box("Buried wheel",root,V(x,.35f,z),V(.2f,.65f,.7f),glass);
    }

    static void Boundary()
    {
        var root=Group("05 - District edge and distant silhouettes");
        for(int side=0;side<4;side++)
        {
            var edge=Group("Silt embankment "+side,root);edge.rotation=Quaternion.Euler(0,side*90,0);
            for(int i=0;i<12;i++)
            {
                var rock=Box("Collapsed perimeter masonry",edge,V(-198+i*36,R(2,7),198),V(40,R(13,25),26),concrete);
                rock.transform.localRotation=Quaternion.Euler(R(-12,12),R(-12,12),R(-8,8));
            }
            Box("District limit",edge,V(0,70,220),V(460,170,2),concrete).GetComponent<Renderer>().enabled=false;
        }
        Box("Upper swim limit",root,V(0,151,0),V(440,2,440),concrete).GetComponent<Renderer>().enabled=false;
        Box("Lower safety bed",root,V(0,-20,0),V(440,2,440),concrete);
    }

    static void LightAt(Transform parent,Vector3 position,Color color,float intensity,float range)
    {
        var obj=Group("Access glow",parent);obj.localPosition=position;
        var light=obj.gameObject.AddComponent<Light>();light.type=LightType.Point;light.color=color;light.intensity=intensity;light.range=range;
    }

    static void Atmosphere()
    {
        RenderSettings.skybox=null;
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;
        RenderSettings.fogColor=new Color(.055f,.16f,.17f);RenderSettings.fogDensity=.008f;
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.28f,.46f,.44f);
        RenderSettings.ambientEquatorColor=new Color(.16f,.26f,.24f);
        RenderSettings.ambientGroundColor=new Color(.08f,.12f,.10f);
        var sun=Group("Filtered light from surface");sun.rotation=Quaternion.Euler(58,-35,0);
        var light=sun.gameObject.AddComponent<Light>();light.type=LightType.Directional;light.color=new Color(.65f,.83f,.76f);light.intensity=1.25f;light.shadows=LightShadows.Soft;
        RenderSettings.sun=light;
    }

    static void Player()
    {
        var player=Group("Player");player.position=V(0,7,-168);
        var capsule=player.gameObject.AddComponent<CapsuleCollider>();capsule.height=1.8f;capsule.radius=.45f;
        var body=player.gameObject.AddComponent<Rigidbody>();body.useGravity=false;body.constraints=RigidbodyConstraints.FreezeRotation;
        body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        var camera=Group("Main Camera",player);camera.gameObject.tag="MainCamera";
        var cam=camera.gameObject.AddComponent<Camera>();cam.nearClipPlane=.1f;cam.farClipPlane=420;cam.fieldOfView=72;
        cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=RenderSettings.fogColor;
        camera.gameObject.AddComponent<AudioListener>();
        var swim=player.gameObject.AddComponent<UnderwaterSwimController>();
        var so=new SerializedObject(swim);so.FindProperty("viewTransform").objectReferenceValue=camera;so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static void Validate()
    {
        var playerCollider=GameObject.Find("Player").GetComponent<CapsuleCollider>();
        playerCollider.enabled=false;
        Physics.SyncTransforms();
        Check(UnityEngine.Object.FindObjectsByType<UnderwaterSwimController>(FindObjectsSortMode.None).Length==1,"Expected one swimmer");
        Check(!Physics.CheckCapsule(V(0,6.55f,-168),V(0,7.45f,-168),.46f),"Spawn obstructed");
        Check(!Physics.SphereCast(V(0,7,-168),.46f,Vector3.forward,out _,145),"Main approach blocked");
        ValidateRoundabout();
        foreach(Transform building in buildings)
        {
            var mass=building.Find("Sealed structural mass");
            if(mass!=null)
            {
                Check(mass.GetComponent<BoxCollider>()!=null,"Unsealed building "+building.name);
                Vector3 start=mass.position+Vector3.back*(mass.localScale.z/2+2);
                Check(Physics.Raycast(start,Vector3.forward,3),"Window collision missing "+building.name);
            }
            else
            {
                Vector3 start=building.TransformPoint(V(0,3,-13));
                Check(!Physics.SphereCast(start,.46f,building.forward,out _,17),"Explore entrance blocked "+building.name);
            }
        }
        foreach(var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            Check(renderer.sharedMaterial!=null && renderer.sharedMaterial.shader!=null,"Missing material");
        ValidateStreets();
        Check(buildings.childCount>=60,"City density unexpectedly reduced");
        playerCollider.enabled=true;
        Debug.Log("CITY_VALIDATE_PASS: spawn, avenue, both interiors, closed fountain, sealed buildings, materials.");
    }

    static void ValidateStreets()
    {
        var streetColliders=streets.GetComponentsInChildren<BoxCollider>();
        foreach(float x in new[]{-108f,0f,108f}) foreach(float z in new[]{-108f,0f,108f})
        {
            if(x==0 && z==0) continue;
            var junction=new Bounds(V(x,1,z),V(18.98f,2,18.98f));
            foreach(var collider in streetColliders)
                if(collider.name=="Sidewalk" || collider.name=="Sidewalk corner" || collider.name=="Curb" || collider.name=="Pole")
                    Check(!junction.Intersects(collider.bounds),"Street furniture intrudes on junction: "+collider.transform.parent.name);
        }
        foreach(Transform obj in streets.GetComponentsInChildren<Transform>())
        {
            if(obj.name!="Dead street light") continue;
            foreach(float axis in new[]{-108f,0f,108f})
                Check(Mathf.Abs(obj.position.x-axis)>9.5f && Mathf.Abs(obj.position.z-axis)>9.5f,"Lamp in carriageway");
        }
        // Sample continuous asphalt along all six streets, including tile/junction seams.
        foreach(bool alongZ in new[]{true,false}) foreach(float offset in new[]{-108f,0f,108f})
        for(float t=-179;t<=179;t+=1)
        {
            if(offset==0 && Mathf.Abs(t)<=40) continue;
            Vector3 p=alongZ?V(offset,2,t):V(t,2,offset);
            bool found=false;
            foreach(var collider in streetColliders)
                if((collider.name=="Connected carriageway" || collider.name=="Intersection asphalt") &&
                    collider.Raycast(new Ray(p,Vector3.down),out _,3)) { found=true;break; }
            Check(found,"Gap in road surface at "+p);
        }
        // Every straight sidewalk's inner edge coincides with the asphalt edge.
        foreach(var collider in streetColliders)
            if(collider.name=="Sidewalk")
                Check(Mathf.Abs(Mathf.Abs(collider.transform.localPosition.x)-collider.transform.localScale.x/2-9.5f)<.001f,"Road/sidewalk gap");
        foreach(Transform building in buildings)
        {
            if(building.name.StartsWith("Collapsed")) continue;
            var mass=building.Find("Sealed structural mass");
            if(mass==null) continue;
            Bounds bounds=mass.GetComponent<BoxCollider>().bounds;
            foreach(var collider in streetColliders)
                if(collider.name=="Sidewalk" || collider.name=="Connected carriageway")
                    Check(!bounds.Intersects(collider.bounds),"Building overlaps street: "+building.name);
        }
        Debug.Log("CITY_STREETS_PASS: eight clear junctions, curb-side lamps, six continuous streets, flush sidewalks; buildings="+buildings.childCount);
    }

    static void Capture()
    {
        Directory.CreateDirectory("Previews");
        var camera=Camera.main;
        Shot(camera,V(0,12,-96),V(0,16,5),"Approach");
        Shot(camera,V(29,40,-38),V(0,1,0),"Plaza");
        Shot(camera,V(15,10,-22),V(0,3,0),"Fountain");
        Shot(camera,V(0,116,0),Vector3.zero,"Roundabout",true);
        Shot(camera,V(185,230,-210),V(0,0,0),"Overview",true);
        Shot(camera,V(120,55,-144),V(108,0,-108),"Intersection",true);
        camera.transform.localPosition=Vector3.zero;camera.transform.localRotation=Quaternion.identity;
    }

    static void Shot(Camera camera,Vector3 position,Vector3 target,string name,bool overview=false)
    {
        bool fog=RenderSettings.fog;
        if(overview) RenderSettings.fog=false;
        camera.transform.position=position;camera.transform.LookAt(target);
        var rt=new RenderTexture(1280,800,24);camera.targetTexture=rt;
        camera.Render();RenderTexture.active=rt;
        var image=new Texture2D(1280,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();
        File.WriteAllBytes("Previews/"+name+".png",image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);
        RenderSettings.fog=fog;
    }

    const float CarRoadHeight = .175f;

    [MenuItem("Low Signal/City/Repair and ground vehicle wrecks")]
    public static void RepairVehicleWrecks()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        RepairVehicleWrecksInScene();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
    }

    public static void RepairVehicleWrecksBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            RepairVehicleWrecksInScene();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void RepairVehicleWrecksInScene()
    {
        var dressingRoot = GameObject.Find("04 - Cars, rubble and marine growth");
        if (dressingRoot == null) throw new Exception("Missing city dressing root.");
        int repaired = 0;
        foreach (Transform wreck in dressingRoot.transform)
        {
            if (!wreck.name.StartsWith("Wreck ", StringComparison.Ordinal)) continue;
            Undo.RecordObject(wreck, "Ground vehicle wreck");
            float yaw = wreck.eulerAngles.y;
            wreck.position = new Vector3(wreck.position.x, CarRoadHeight, wreck.position.z);
            wreck.rotation = Quaternion.Euler(0, yaw, 0);
            var wheel = wreck.Find("Buried wheel");
            if (wheel == null) throw new Exception("Vehicle wreck has no wheels: " + wreck.name);
            float wheelBottom = wheel.GetComponent<Collider>().bounds.min.y;
            if (Mathf.Abs(wheelBottom - .2f) > .002f)
                throw new Exception("Vehicle wreck is not grounded: " + wreck.name + ", wheel bottom=" + wheelBottom);
            repaired++;
        }
        if (repaired != 28) throw new Exception("Expected 28 vehicle wrecks, found " + repaired);
        Debug.Log("CITY_VEHICLES_PASS: grounded and leveled " + repaired + " vehicle wrecks on the road surface.");
    }
}


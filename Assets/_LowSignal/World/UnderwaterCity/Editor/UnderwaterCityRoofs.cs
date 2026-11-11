using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class UnderwaterCityBuilder
{
    static void Roof(Transform building,float w,float d,float top,bool ruined)
    {
        // Stable local seed: roof detail never changes the rest of the city's random sequence.
        int seed=17;
        foreach(char c in building.name) seed=unchecked(seed*31+c);
        var rng=new System.Random(seed & int.MaxValue);
        int style=rng.Next(5);
        var roof=Group("Roof detail - variant "+style,building);
        roof.localPosition=V(0,top,0);
        float edgeHeight=style==0?.25f:style==1?.65f:style==2?1.15f:style==3?.85f:.4f;
        Material deck=style==0 || style==3?concrete:asphalt;
        Box("Sealed roof membrane",roof,V(0,.04f,0),V(w,.08f,d),deck);
        for(int side=0;side<4;side++)
        {
            float length=side%2==0?w:d;
            int sections=ruined?7:1;
            for(int section=0;section<sections;section++)
            {
                if(ruined && rng.NextDouble()<.28) continue;
                float height=ruined?edgeHeight*(.35f+(float)rng.NextDouble()):edgeHeight;
                float along=-length/2+(section+.5f)*length/sections;
                Vector3 p=side%2==0?V(along,.08f+height/2,(side==0?-1:1)*(d/2-.20f)):
                    V((side==1?-1:1)*(w/2-.20f),.08f+height/2,along);
                Vector3 size=side%2==0?V(length/sections-.04f,height,.40f):V(.40f,height,length/sections-.04f);
                Box("Parapet",roof,p,size,style==2?concrete:pale);
                Box("Parapet coping",roof,p+V(0,height/2+.055f,0),size+V(.08f,.11f-height,.08f),pale);
            }
        }
        if(style==2)
            Box("Thin projecting cornice",roof,V(0,-.12f,0),V(w+.5f,.22f,d+.5f),pale);
        if(style==1 || style==3)
        {
            for(float z=-d/2+4;z<d/2-2;z+=5)
                Box("Membrane seam",roof,V(0,.09f,z),V(w-1,.025f,.07f),concrete,false);
            int vents=style==1?2:3;
            for(int i=0;i<vents;i++)
            {
                var vent=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                vent.name="Small perimeter exhaust vent";vent.transform.SetParent(roof,false);
                vent.transform.localPosition=V(-w/2+2+i*2.2f,.55f,d/2-2);
                vent.transform.localScale=V(.5f,.45f,.5f);
                vent.GetComponent<Renderer>().sharedMaterial=rust;vent.isStatic=true;
                var cap=Box("Vent cap",roof,vent.transform.localPosition+V(0,.5f,0),V(.7f,.12f,.7f),rust);
            }
        }
        if(style==4)
        {
            // A small offset, opaque skylight; the structural roof remains sealed beneath it.
            var skylight=Box("Low sealed skylight",roof,V(w*.23f,.30f,-d*.24f),V(2.4f,.25f,4),glass);
            skylight.transform.localRotation=Quaternion.Euler(6,0,0);
            foreach(float x in new[]{-1.3f,1.3f})
                Box("Skylight flashing",roof,V(w*.23f+x,.20f,-d*.24f),V(.18f,.23f,4.2f),rust);
        }
        int deposits=ruined?7:2;
        for(int i=0;i<deposits;i++)
        {
            float x=((float)rng.NextDouble()-.5f)*(w-4),z=((float)rng.NextDouble()-.5f)*(d-4);
            var fragment=Box(ruined?"Roof masonry fragment":"Settled rooftop silt",roof,V(x,.16f,z),
                V(.8f+(float)rng.NextDouble()*2,.12f+(ruined?.2f:0),.6f+(float)rng.NextDouble()),ruined?concrete:sand,false);
            fragment.transform.localRotation=Quaternion.Euler(0,(float)rng.NextDouble()*180,0);
        }
    }

    public static void UpdateRoofsBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            RequirePrimitiveBuildings();
            buildings=GameObject.Find("02 - City blocks (sealed exteriors)").transform;
            streets=GameObject.Find("01 - Streets and seabed").transform;
            concrete=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Weathered concrete.mat");
            pale=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Limestone trim.mat");
            asphalt=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Broken asphalt.mat");
            rust=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Oxidized steel.mat");
            sand=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Grey green silt.mat");
            glass=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Opaque dead windows.mat");
            int count=0;var variants=new HashSet<string>();
            foreach(Transform building in buildings)
            {
                var mass=building.Find("Sealed structural mass");
                bool ruined=building.Find("Broken roof west section")!=null;
                var remove=new List<GameObject>();
                foreach(Transform child in building)
                    if(child.name=="Rooftop service plant" || child.name=="Intact roof coping" ||
                        child.name=="Broken roof west section" || child.name=="Broken roof rear section" ||
                        child.name=="Exposed rebar" || child.name.StartsWith("Roof detail -")) remove.Add(child.gameObject);
                foreach(var go in remove) UnityEngine.Object.DestroyImmediate(go);
                if(mass!=null) Roof(building,mass.localScale.x,mass.localScale.z,mass.localPosition.y+mass.localScale.y/2,ruined);
                else Roof(building,20,18,8.4f,false);
                foreach(Transform child in building) if(child.name.StartsWith("Roof detail -")) variants.Add(child.name);
                count++;
            }
            Check(GameObject.Find("Rooftop service plant")==null,"Old rooftop blocks remain");
            Check(GameObject.Find("Broken roof west section")==null,"Old raised roof masses remain");
            Check(variants.Count==5,"Roof styles did not vary");
            Physics.SyncTransforms();
            foreach(Transform building in buildings)
            {
                var mass=building.Find("Sealed structural mass");
                if(mass==null || building.name.StartsWith("Collapsed")) continue;
                float top=mass.localPosition.y+mass.localScale.y/2;
                Check(Physics.Raycast(building.TransformPoint(V(0,top+5,0)),-building.up,7),"Roof collision missing: "+building.name);
            }
            Validate();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);
            Capture();
            Camera.main.farClipPlane=800;
            Shot(Camera.main,V(150,195,-155),V(15,20,-15),"Roofs",true);
            Debug.Log("CITY_ROOFS_PASS: "+count+" buildings updated in place; five roof styles; old blocks removed; roofs remain sealed.");
            EditorApplication.Exit(0);
        }
        catch(Exception error) { Debug.LogException(error);EditorApplication.Exit(1); }
    }
}

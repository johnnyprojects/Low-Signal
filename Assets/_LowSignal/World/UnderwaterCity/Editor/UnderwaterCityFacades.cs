using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class UnderwaterCityBuilder
{
    static void FacadeDamage(Transform building,float width,float height,float depth,float y,float z,int index)
    {
        var patch=Group("Spalled facade - attached wall damage",building);
        patch.localPosition=V(width/2,Mathf.Clamp(y,3,height-3),Mathf.Clamp(z,-depth/2+2,depth/2-2));
        // Thin pieces overlap the structural wall; rotation is only within its Y/Z plane.
        patch.localRotation=Quaternion.Euler(index%2==0?7:-9,0,0);
        Box("Exposed weathered backing",patch,V(.02f,0,0),V(.12f,3.6f,1.9f),rust,false);
        Box("Chipped concrete edge",patch,V(.025f,1.25f,.85f),V(.12f,1.2f,.5f),concrete,false);
        Box("Chipped concrete edge",patch,V(.025f,-1.1f,-.8f),V(.12f,.9f,.55f),concrete,false);
        Box("Remaining cladding fragment",patch,V(.04f,.6f,-.85f),V(.12f,.8f,.45f),pale,false);
        Box("Remaining cladding fragment",patch,V(.04f,-1.65f,.3f),V(.12f,.45f,1.1f),pale,false);
    }

    public static void UpdateFacadesBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            RequirePrimitiveBuildings();
            buildings=GameObject.Find("02 - City blocks (sealed exteriors)").transform;
            streets=GameObject.Find("01 - Streets and seabed").transform;
            concrete=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Weathered concrete.mat");
            pale=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Limestone trim.mat");
            rust=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Oxidized steel.mat");
            int count=0;
            foreach(Transform building in buildings)
            {
                var mass=building.Find("Sealed structural mass");
                if(mass==null) continue;
                var panels=new List<Transform>();
                foreach(Transform child in building) if(child.name=="Slipped facade panel") panels.Add(child);
                for(int i=0;i<panels.Count;i++)
                {
                    Vector3 p=panels[i].localPosition;
                    FacadeDamage(building,mass.localScale.x,mass.localScale.y,mass.localScale.z,p.y,p.z,i);
                    UnityEngine.Object.DestroyImmediate(panels[i].gameObject);
                    count++;
                }
                foreach(Transform patch in building)
                {
                    if(patch.name!="Spalled facade - attached wall damage") continue;
                    foreach(Transform piece in patch)
                    {
                        float centre=patch.localPosition.x+piece.localPosition.x;
                        float half=piece.localScale.x/2;
                        Check(centre-half<mass.localScale.x/2,"Facade piece detached from wall");
                        Check(centre+half<=mass.localScale.x/2+.11f,"Facade piece protrudes too far");
                    }
                }
            }
            Check(GameObject.Find("Slipped facade panel")==null,"Floating panels remain");
            Check(count>0,"No old facade panels found");
            Validate();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);
            Capture();
            Shot(Camera.main,V(80,28,37),V(58,23,26),"Facades");
            Debug.Log("CITY_FACADES_PASS: "+count+" floating panels replaced in place; every fragment overlaps its wall and projects at most 10 cm.");
            EditorApplication.Exit(0);
        }
        catch(Exception error) { Debug.LogException(error);EditorApplication.Exit(1); }
    }
}

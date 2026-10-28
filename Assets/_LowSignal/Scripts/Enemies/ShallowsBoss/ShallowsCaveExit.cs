using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider))]
public class ShallowsCaveExit : MonoBehaviour
{
    public ShallowsFishBoss boss;
    public string destinationScene = "UnderwaterCity";
    bool loading;
    bool reportedError;

    void OnTriggerEnter(Collider other) => TryEnter(other);
    void OnTriggerStay(Collider other) => TryEnter(other);

    void TryEnter(Collider other)
    {
        if (loading || boss == null || !boss.PassageOpen || other.GetComponentInParent<UnderwaterSwimController>() == null) return;
        if (!Application.CanStreamedLevelBeLoaded(destinationScene))
        {
            if (!reportedError) Debug.LogError("Cave destination is missing from Build Settings: " + destinationScene, this);
            reportedError = true;
            return;
        }
        loading = true;
        if (!SaveGameService.Instance.TravelTo(destinationScene, out string error))
        {
            loading = false;
            Debug.LogError(error, this);
        }
    }
}

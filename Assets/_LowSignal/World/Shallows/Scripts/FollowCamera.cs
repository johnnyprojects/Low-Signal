using UnityEngine;

/// <summary>
/// Keeps this object centred on the main camera, so effects such as drifting plankton always fill the space around the
/// viewer without spawning particles across the whole level. Particles left behind keep drifting in place, because
/// their system simulates in world space.
/// </summary>
public class FollowCamera : MonoBehaviour
{
    private Camera viewer;

    private void LateUpdate()
    {
        if (viewer == null)
            viewer = Camera.main;
        if (viewer != null)
            transform.position = viewer.transform.position;
    }
}

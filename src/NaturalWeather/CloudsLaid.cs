using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// On the renderer that lays the clouds on in their turn among the see-through things (Clouds.Laying): just before each
    /// camera draws it, the material of that camera's clouds is put on it, or one that draws nothing for any other camera
    /// (the scaled space's, a kerbal's portrait, a reflection).
    /// </summary>
    sealed class CloudsLaid : MonoBehaviour
    {
        internal Clouds Owner;
        Renderer drawn;

        void OnWillRenderObject()
        {
            if (Owner == null) return;
            if (drawn == null) drawn = GetComponent<Renderer>();
            Material m = Owner.LaidFor(Camera.current);
            if (drawn != null && m != null && drawn.sharedMaterial != m) drawn.sharedMaterial = m;
        }
    }
}

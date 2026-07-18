using UnityEngine;

namespace UniVRMXT.Vfx
{
    /// <summary>
    /// Destroys the owned particle material when the emitter GameObject is destroyed.
    /// Must live in its own file — Unity MonoBehaviour script discovery requires matching filename.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VrmxtVfxOwnedParticleMaterial : MonoBehaviour
    {
        private void OnDestroy()
        {
            var renderer = GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
            {
                return;
            }

            var material = renderer.sharedMaterial;
            if (!VrmxtVfxParticleSystemMapper.IsOwnedParticleMaterial(material))
            {
                return;
            }

            renderer.sharedMaterial = null;
            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }
    }
}

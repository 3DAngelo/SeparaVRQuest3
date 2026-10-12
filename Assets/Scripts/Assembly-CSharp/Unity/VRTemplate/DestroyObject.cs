using UnityEngine;

namespace Unity.VRTemplate
{
    public class DestroyObject : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Time before destroying in seconds.")]
        float m_Lifetime = 5f;

        void Start()
        {
            Destroy(gameObject, m_Lifetime);
        }
    }
}

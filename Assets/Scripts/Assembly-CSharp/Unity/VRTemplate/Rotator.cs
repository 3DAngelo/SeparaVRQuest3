using UnityEngine;

namespace Unity.VRTemplate
{
    public class Rotator : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Angular velocity in degrees per second")]
        Vector3 m_Velocity;

        void Update()
        {
            transform.Rotate(m_Velocity * Time.deltaTime);
        }
    }
}

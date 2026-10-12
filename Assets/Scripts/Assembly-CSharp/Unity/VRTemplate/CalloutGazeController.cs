using UnityEngine;
using UnityEngine.Events;

namespace Unity.VRTemplate
{
    public class CalloutGazeController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The transform which the forward direction will be used to evaluate as the gaze direction.")]
        Transform m_GazeTransform;

        [SerializeField]
        [Tooltip("Threshold for the dot product when determining if the Gaze Transform is facing this object. The lower the threshold, the wider the field of view.")]
        [Range(0f, 1f)]
        float m_FacingThreshold = 0.85f;

        [SerializeField]
        [Tooltip("Events fired when the Gaze Transform begins facing this game object")]
        UnityEvent m_FacingEntered;

        [SerializeField]
        [Tooltip("Events fired when the Gaze Transform stops facing this game object")]
        UnityEvent m_FacingExited;

        [SerializeField]
        [Tooltip("Distance threshold for movement in a single frame that determines a large movement that will trigger Facing Exited events.")]
        float m_LargeMovementDistanceThreshold = 0.05f;

        [SerializeField]
        [Tooltip("Cool down time after a large movement for Facing Entered events to fire again.")]
        float m_LargeMovementCoolDownTime = 0.25f;

        bool m_IsFacing;
        float m_LargeMovementCoolDown;
        Vector3 m_LastPosition;

        void Update()
        {
            if (m_GazeTransform)
            {
                CheckLargeMovement();

                if (m_LargeMovementCoolDown < m_LargeMovementCoolDownTime)
                    return;

                var directionToGaze = (transform.position - m_GazeTransform.position).normalized;
                var dot = Vector3.Dot(m_GazeTransform.forward, directionToGaze);

                if (dot > m_FacingThreshold && !m_IsFacing)
                    FacingEntered();
                else if (dot < m_FacingThreshold && m_IsFacing)
                    FacingExited();
            }
        }

        void CheckLargeMovement()
        {
            // RECONSTRUCCION-DUDA: final stores (cooldown accumulation and m_LastPosition update) partly inferred from the disassembly.
            var position = transform.position;
            if (Mathf.Abs((position - m_LastPosition).magnitude) > m_LargeMovementDistanceThreshold)
            {
                m_LargeMovementCoolDown = 0f;
                FacingExited();
            }

            m_LargeMovementCoolDown += Time.deltaTime;
            m_LastPosition = position;
        }

        void FacingEntered()
        {
            m_IsFacing = true;
            m_FacingEntered.Invoke();
        }

        void FacingExited()
        {
            m_IsFacing = false;
            m_FacingExited.Invoke();
        }
    }
}

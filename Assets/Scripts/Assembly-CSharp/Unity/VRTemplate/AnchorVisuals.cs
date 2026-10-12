using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Utilities.Tweenables.Primitives;

namespace Unity.VRTemplate
{
    public class AnchorVisuals : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The animation for the vertical glow element on the platform.")]
        Animation m_FadeAnimation;

        [SerializeField]
        [Tooltip("The arrow transform, at the center of the platform.")]
        Transform m_Arrow;

        [SerializeField]
        [Tooltip("Height of the arrow transform when teleport ray hovers the teleport pad.")]
        float m_TargetArrowHeight = 1f;

        [SerializeField]
        [Tooltip("Animation duration of the arrow transform to and from the target arrow height.")]
        float m_ArrowAnimationDuration = 0.2f;

        [SerializeField]
        [Tooltip("Animation curve of hte arrow transform to and from the target arrow height.")]
        AnimationCurve m_AnimationCurve;

        Coroutine m_ArrowCoroutine;
        Vector3TweenableVariable m_ArrowHeight;
        Vector3 m_InitialArrowScale;

        void Start()
        {
            if (m_FadeAnimation != null)
                m_FadeAnimation[m_FadeAnimation.clip.name].normalizedTime = 1f;

            // RECONSTRUCCION-DUDA: m_ArrowHeight.Value is never initialised, so Update before the first Enter sets the arrow to the zero vector (as in the binary).
            m_ArrowHeight = new Vector3TweenableVariable();
            m_ArrowHeight.animationCurve = m_AnimationCurve;
            m_InitialArrowScale = m_Arrow.localScale;
        }

        void Update()
        {
            m_Arrow.localPosition = m_ArrowHeight.Value;
        }

        public void OnAnchorEnter()
        {
            m_Arrow.localScale = m_InitialArrowScale;

            if (m_FadeAnimation != null)
            {
                var clipName = m_FadeAnimation.clip.name;
                m_FadeAnimation[clipName].normalizedTime = 0f;
                m_FadeAnimation[clipName].speed = 1f;
                m_FadeAnimation.Play();
            }

            if (m_ArrowCoroutine != null)
                StopCoroutine(m_ArrowCoroutine);

            // RECONSTRUCCION-DUDA: exact PlaySequence argument shape (start, target, duration) is inferred.
            var position = m_Arrow.localPosition;
            m_ArrowCoroutine = StartCoroutine(m_ArrowHeight.PlaySequence(position, new Vector3(position.x, m_TargetArrowHeight, position.z), m_ArrowAnimationDuration));
        }

        public void OnAnchorExit()
        {
            if (m_FadeAnimation != null)
            {
                var clipName = m_FadeAnimation.clip.name;
                m_FadeAnimation[clipName].normalizedTime = 1f;
                m_FadeAnimation[clipName].speed = -1.5f;
                m_FadeAnimation.Play();
            }

            if (m_ArrowCoroutine != null)
                StopCoroutine(m_ArrowCoroutine);

            var position = m_Arrow.localPosition;
            m_ArrowCoroutine = StartCoroutine(m_ArrowHeight.PlaySequence(position, new Vector3(position.x, 0f, position.z), m_ArrowAnimationDuration));
        }

        public void HideArrowOnTeleport()
        {
            m_Arrow.localScale = Vector3.zero;
        }
    }
}

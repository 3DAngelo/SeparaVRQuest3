using Unity.Mathematics;
using Unity.XR.CoreUtils.Bindings;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Utilities.Tweenables.Primitives;

namespace Unity.VRTemplate
{
    [AddComponentMenu("XR/XR Poke Follow Affordance Fill", 22)]
    public class XRPokeFollowAffordanceFill : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Transform that will move in the poke direction when this or a parent GameObject is poked.\nNote: Should be a direct child GameObject.")]
        Transform m_PokeFollowTransform;

        [SerializeField]
        [Tooltip("Transform that will scale the mask when this interactable is poked.")]
        RectTransform m_PokeFill;

        [SerializeField]
        [Tooltip("The max width size for the poke fill image when pressed")]
        float m_PokeFillMaxSizeX;

        [SerializeField]
        [Tooltip("The max height size for the poke fill image when pressed")]
        float m_PokeFillMaxSizeY;

        [SerializeField]
        [Range(0f, 20f)]
        [Tooltip("Multiplies transform position interpolation as a factor of Time.deltaTime. If 0, no smoothing will be applied.")]
        float m_SmoothingSpeed = 8f;

        [SerializeField]
        [Tooltip("When this component is no longer the target of the poke, the Poke Follow Transform returns to the original position.")]
        bool m_ReturnToInitialPosition = true;

        [SerializeField]
        [Tooltip("Whether to apply the follow animation if the target of the poke is a child of this transform. This is useful for UI objects that may have child graphics.")]
        bool m_ApplyIfChildIsTarget = true;

        [Header("Distance Clamping")]
        [SerializeField]
        [Tooltip("Whether to keep the Poke Follow Transform from moving past a minimum distance from the poke target.")]
        bool m_ClampToMinDistance;

        [SerializeField]
        [Tooltip("The minimum distance from this transform that the Poke Follow Transform can move.")]
        float m_MinDistance;

        [Space]
        [SerializeField]
        [Tooltip("Whether to keep the Poke Follow Transform from moving past a maximum distance from the poke target.")]
        bool m_ClampToMaxDistance;

        [SerializeField]
        [Tooltip("The maximum distance from this transform that the Poke Follow Transform can move. Will shrink to the distance of initial position if that is smaller, or if this is 0.")]
        float m_MaxDistance;

        IPokeStateDataProvider m_PokeDataProvider;
        readonly Vector3TweenableVariable m_TransformTweenableVariable = new Vector3TweenableVariable();
        readonly FloatTweenableVariable m_PokeStrengthTweenableVariable = new FloatTweenableVariable();
        readonly BindingsGroup m_BindingsGroup = new BindingsGroup();

        Vector3 m_InitialPosition;
        // RECONSTRUCCION-DUDA: nothing in the binary sets m_IsFirstFrame to true, so the first-frame branch in LateUpdate is dead; reproduced as compiled.
        bool m_IsFirstFrame;

        public Transform pokeFollowTransform
        {
            get => m_PokeFollowTransform;
            set => m_PokeFollowTransform = value;
        }

        public float smoothingSpeed
        {
            get => m_SmoothingSpeed;
            set => m_SmoothingSpeed = value;
        }

        public bool returnToInitialPosition
        {
            get => m_ReturnToInitialPosition;
            set => m_ReturnToInitialPosition = value;
        }

        public bool applyIfChildIsTarget
        {
            get => m_ApplyIfChildIsTarget;
            set => m_ApplyIfChildIsTarget = value;
        }

        public bool clampToMinDistance
        {
            get => m_ClampToMinDistance;
            set => m_ClampToMinDistance = value;
        }

        public float minDistance
        {
            get => m_MinDistance;
            set => m_MinDistance = value;
        }

        public bool clampToMaxDistance
        {
            get => m_ClampToMaxDistance;
            set => m_ClampToMaxDistance = value;
        }

        public float maxDistance
        {
            get => m_MaxDistance;
            set => m_MaxDistance = value;
        }

        protected void Awake()
        {
            m_PokeDataProvider = GetComponentInParent<IPokeStateDataProvider>();
        }

        protected void Start()
        {
            if (m_PokeFollowTransform != null)
            {
                m_InitialPosition = m_PokeFollowTransform.localPosition;
                var magnitude = m_InitialPosition.magnitude;
                m_MaxDistance = m_MaxDistance > 0f ? Mathf.Min(m_MaxDistance, magnitude) : magnitude;

                m_BindingsGroup.AddBinding(m_TransformTweenableVariable.Subscribe(OnTransformTweenableVariableUpdated));
                m_BindingsGroup.AddBinding(m_PokeStrengthTweenableVariable.Subscribe(OnPokeStrengthChanged));
                // RECONSTRUCCION-DUDA: binary calls interface slot +1 on the poke data observable; SubscribeAndUpdate assumed (matches the original package sample).
                m_BindingsGroup.AddBinding(m_PokeDataProvider.pokeStateData.SubscribeAndUpdate(OnPokeStateDataUpdated));
            }
            else
            {
                enabled = false;
                #if UNITY_EDITOR
                Debug.LogWarning($"Missing Poke Follow Transform assignment on {this}. Disabling component.", this);
                #endif
            }
        }

        protected void OnDestroy()
        {
            m_BindingsGroup.Clear();
            m_TransformTweenableVariable?.Dispose();
        }

        protected void LateUpdate()
        {
            if (m_IsFirstFrame)
            {
                m_TransformTweenableVariable.HandleTween(1f);
                m_PokeStrengthTweenableVariable.target = 0f;
                m_PokeStrengthTweenableVariable.HandleTween(1f);
                m_IsFirstFrame = false;
                return;
            }

            var tweenTarget = m_SmoothingSpeed > 0f ? Time.deltaTime * m_SmoothingSpeed : 1f;
            m_TransformTweenableVariable.HandleTween(tweenTarget);
            m_PokeStrengthTweenableVariable.HandleTween(tweenTarget);
        }

        void OnTransformTweenableVariableUpdated(float3 position)
        {
            m_PokeFollowTransform.localPosition = position;
        }

        void OnPokeStrengthChanged(float newStrength)
        {
            m_PokeFill.sizeDelta = new Vector2(m_PokeFillMaxSizeX * newStrength, m_PokeFillMaxSizeY * newStrength);
        }

        void OnPokeStateDataUpdated(PokeStateData data)
        {
            var pokeTarget = data.target;
            var applyFollow = pokeTarget != null &&
                ((m_ApplyIfChildIsTarget && pokeTarget.IsChildOf(transform)) || pokeTarget == transform);

            if (applyFollow)
            {
                // RECONSTRUCCION-DUDA: InverseTransformPoint is called on data.target (not on the follow transform) per the disassembly.
                var targetLocalPosition = pokeTarget.InverseTransformPoint(data.axisAlignedPokeInteractionPoint);

                // RECONSTRUCCION-DUDA: the min-distance clamp compiles to an effective no-op in the binary; written in the natural form.
                if (m_ClampToMinDistance && targetLocalPosition.sqrMagnitude < m_MinDistance * m_MinDistance)
                    targetLocalPosition = Vector3.ClampMagnitude(targetLocalPosition, m_MinDistance);

                if (m_ClampToMaxDistance && targetLocalPosition.sqrMagnitude > m_MaxDistance * m_MaxDistance)
                    targetLocalPosition = Vector3.ClampMagnitude(targetLocalPosition, m_MaxDistance);

                m_TransformTweenableVariable.target = targetLocalPosition;
                m_PokeStrengthTweenableVariable.target = Mathf.Clamp01(data.interactionStrength);
            }
            else if (m_ReturnToInitialPosition)
            {
                m_TransformTweenableVariable.target = m_InitialPosition;
                m_PokeStrengthTweenableVariable.target = 0f;
            }
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Unity.VRTemplate
{
    [RequireComponent(typeof(Toggle))]
    public class BooleanToggleVisualsController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        const float k_TargetPositionX = 17f;

        [SerializeField]
        [Tooltip("The boolean toggle knob.")]
        RectTransform m_Knob;

        [SerializeField]
        [Tooltip("How much to translate the button imagery on the z on hover.")]
        float m_ZTranslation = 5f;

        Toggle m_Toggle;
        float m_InitialBackground;
        // RECONSTRUCCION-DUDA: m_ColorFade and m_LocalMove are declared in the stub but never used in the binary's methods.
        Coroutine m_ColorFade;
        Coroutine m_LocalMove;

        void Awake()
        {
            m_Toggle = gameObject.GetComponent<Toggle>();
            m_Toggle.onValueChanged.AddListener(ToggleValueChanged);

            if (m_Knob != null)
                m_InitialBackground = m_Knob.localPosition.z;
        }

        void OnEnable()
        {
            ToggleValueChanged(m_Toggle.isOn);
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            PerformEntranceActions();
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            PerformExitActions();
        }

        void ToggleValueChanged(bool value)
        {
            var position = m_Knob.localPosition;
            m_Knob.localPosition = new Vector3(value ? k_TargetPositionX : -k_TargetPositionX, position.y, position.z);
        }

        void PerformEntranceActions()
        {
            // RECONSTRUCCION-DUDA: disassembly shows no scale change on entrance; only the z translation.
            if (m_Knob != null)
            {
                var position = m_Knob.localPosition;
                m_Knob.localPosition = new Vector3(position.x, position.y, m_InitialBackground - m_ZTranslation);
            }
        }

        void PerformExitActions()
        {
            if (m_Knob != null)
            {
                var position = m_Knob.localPosition;
                m_Knob.localPosition = new Vector3(position.x, position.y, m_InitialBackground);
                m_Knob.localScale = Vector3.one;
            }
        }
    }
}

using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Unity.VRTemplate
{
    public class RayAttachModifier : MonoBehaviour
    {
        IXRSelectInteractable m_SelectInteractable;

        protected void OnEnable()
        {
            m_SelectInteractable = GetComponent<IXRSelectInteractable>();
            if ((m_SelectInteractable as Object) == null)
            {
                Debug.LogError("Ray Attach Modifier missing required Select Interactable on " + name, this);
                return;
            }

            m_SelectInteractable.selectEntered.AddListener(OnSelectEntered);
        }

        protected void OnDisable()
        {
            if ((m_SelectInteractable as Object) != null)
                m_SelectInteractable.selectEntered.RemoveListener(OnSelectEntered);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRRayInteractor)
            {
                // RECONSTRUCCION-DUDA: interface slot semantics of GetLocalAttachPoseOnSelect (IXRSelectInteractor) inferred from vtable offset.
                var attachTransform = args.interactorObject.GetAttachTransform(m_SelectInteractable);
                var attachPose = args.interactorObject.GetLocalAttachPoseOnSelect(m_SelectInteractable);
                attachTransform.SetLocalPose(attachPose);
            }
        }
    }
}

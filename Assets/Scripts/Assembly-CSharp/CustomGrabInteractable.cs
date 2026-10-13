using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class CustomGrabInteractable : XRGrabInteractable
{
	private XRRayInteractor rayInteractor;

	private NearFarInteractor farInteractor;

	private Container currentTargetContainer;

	private bool aimSelecting;

	// Hand tracking: pose of the object in the hand (see HandHoldPose). It must run AFTER XRI's default transformer,
	// which keeps computing the pose for controllers; this one only overrides it for a tracked hand.
	private HandHoldPose handHoldPose;

	// XRI calls this (once, on the first Dynamic update) when no grab transformer is registered yet.
	protected override void AddDefaultSingleGrabTransformer()
	{
		base.AddDefaultSingleGrabTransformer();
		AddHandHoldPose();
	}

	private void AddHandHoldPose()
	{
		if (handHoldPose == null)
		{
			handHoldPose = new HandHoldPose();
			AddSingleGrabTransformer(handHoldPose);
		}
	}

	protected override void OnSelectEntering(SelectEnterEventArgs args)
	{
		// Only if some other transformer got registered first (so XRI never called AddDefaultSingleGrabTransformer).
		if (handHoldPose == null && singleGrabTransformersCount > 0)
		{
			AddHandHoldPose();
		}
		base.OnSelectEntering(args);
	}

	protected override void OnSelectEntered(SelectEnterEventArgs args)
	{
		base.OnSelectEntered(args);
		if (args.interactorObject is XRRayInteractor ray)
		{
			rayInteractor = ray;
			Debug.Log("Ray interactor detected");
		}
		if (args.interactorObject is NearFarInteractor nearFar)
		{
			farInteractor = nearFar;
			currentTargetContainer = null;
			if (!aimSelecting)
			{
				aimSelecting = true;
				Container.BeginAimSelection();
			}
		}
		if (GameManager.Instance != null)
		{
			GameManager.Instance.OnObjectGrabbed(gameObject);
		}
	}

	protected override void OnSelectExited(SelectExitEventArgs args)
	{
		base.OnSelectExited(args);
		if (currentTargetContainer != null)
		{
			Debug.Log("Exit on Container: " + currentTargetContainer.descripcion);
			if (currentTargetContainer.IDcontainer == GetComponent<Residuo>().IDcontenedor)
			{
				Debug.Log("Correct Container");
				Residuo residuo = GetComponent<Residuo>();
				if (GameManager.Instance != null)
				{
					GameManager.Instance.IniciarArrojarResiduos(currentTargetContainer, residuo);
				}
			}
			else
			{
				GetComponent<Rigidbody>().isKinematic = false;
				Debug.Log("Incorrect Container");
				GameManager.Instance.GetComponent<AudioSource>().Play();
			}
		}
		currentTargetContainer = null;
		if (!isSelected)
		{
			farInteractor = null;
			if (aimSelecting)
			{
				aimSelecting = false;
				Container.EndAimSelection();
			}
		}
	}

	private void Update()
	{
		if (farInteractor == null || !isSelected)
		{
			return;
		}
		// While the object is held, the container is chosen by the HORIZONTAL direction of the aim only (pitch is
		// ignored): aiming to the right side but too high or too low still counts. The chosen container is the one
		// highlighted, and the one used on release. Aiming clearly away from every container chooses none.
		// Hand: aim = the hand's aim pose. Controller: aim = the controller pose (far caster origin), as before.
		Vector3 origin;
		Vector3 direction;
		HandCloseGrab.GetAim(farInteractor, transform, out origin, out direction);
		Debug.DrawRay(origin, direction * 10f, Color.red);
		Container container = Container.PickByHorizontalAim(origin, direction, currentTargetContainer);
		if (container != currentTargetContainer)
		{
			currentTargetContainer = container;
			Container.SetAimTarget(container);
			Debug.Log(container != null ? "New Current Container: " + container.descripcion : "No container selected");
		}
	}
}

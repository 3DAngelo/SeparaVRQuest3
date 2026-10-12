using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class CustomGrabInteractable : XRGrabInteractable
{
	private XRRayInteractor rayInteractor;

	private NearFarInteractor farInteractor;

	private Container currentTargetContainer;

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
	}

	private void Update()
	{
		if (farInteractor != null)
		{
			// Hand tracking: the interactor's own transform is not tracked on the hand rig, the pose that aims is the
			// far caster's cast origin (hand: "Aim Pose"; controller: the interactor transform itself, as before).
			Transform aim = farInteractor.transform;
			if (farInteractor.farInteractionCaster != null && farInteractor.farInteractionCaster.castOrigin != null)
			{
				aim = farInteractor.farInteractionCaster.castOrigin;
			}
			Ray ray = new Ray(aim.position, aim.forward);
			Debug.DrawRay(ray.origin, ray.direction * 10f, Color.red, 5f);
			RaycastHit hit;
			if (RaycastIgnoringSelf(ray, 10f, out hit))
			{
				Container container = hit.collider.GetComponent<Container>();
				if (container != null && currentTargetContainer != container)
				{
					currentTargetContainer = container;
					Debug.Log("New Current Container: " + container.descripcion);
				}
			}
			else
			{
				currentTargetContainer = null;
				Debug.Log("No container selected");
			}
		}
	}

	private static readonly RaycastHit[] aimHits = new RaycastHit[32];

	// Same as Physics.Raycast (nearest hit) but skipping this object's own colliders: with a hand the object is held
	// at the pinch point, in front of the aim ray origin, and would otherwise hide the container behind it.
	private bool RaycastIgnoringSelf(Ray ray, float maxDistance, out RaycastHit nearest)
	{
		nearest = default(RaycastHit);
		bool found = false;
		int count = Physics.RaycastNonAlloc(ray, aimHits, maxDistance);
		for (int i = 0; i < count; i++)
		{
			if (aimHits[i].collider.transform.IsChildOf(transform))
			{
				continue;
			}
			if (!found || aimHits[i].distance < nearest.distance)
			{
				nearest = aimHits[i];
				found = true;
			}
		}
		return found;
	}
}

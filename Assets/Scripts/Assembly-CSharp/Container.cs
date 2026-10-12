using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class Container : XRBaseInteractable
{
	public enum Type
	{
		GARBAGE = 0,
		RECYCLE = 1,
		BIG_GREENS = 2,
		ELECTRONICS = 3
	}

	public Type IDcontainer;

	public KeyCode shorcut = KeyCode.Alpha1;

	public string descripcion;

	public Transform arrojarAqui;

	private MeshRenderer _renderer;

	private Component[] childrenRenderers;

	public Material defaultMaterial;

	public Material hilightMaterial;

	// ---- Choice of container by horizontal aim while an object is held (see CustomGrabInteractable.Update) ----

	// A container is chosen when the horizontal direction of the aim is within this angle of the horizontal direction
	// to the container (pitch is ignored). Seen from the player the two containers of NuevoJuego are about 72 degrees
	// apart and each is about 12 degrees wide to each side, so 28 leaves a neutral zone of about 16 degrees between them.
	public const float AimToleranceDeg = 28f;

	// Once chosen, a container stays chosen up to this angle (hysteresis, so it does not flicker at the edge).
	public const float AimKeepDeg = 33f;

	private static readonly List<Container> all = new List<Container>();

	private static int aimUsers;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		all.Clear();
		aimUsers = 0;
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		if (!all.Contains(this))
		{
			all.Add(this);
		}
	}

	protected override void OnDisable()
	{
		all.Remove(this);
		base.OnDisable();
	}

	/// <summary>An object has been grabbed: from now on the highlight shows the container chosen by aim, not the hovered one.</summary>
	public static void BeginAimSelection()
	{
		aimUsers++;
		SetAimTarget(null);
	}

	/// <summary>The object was released: highlighting goes back to plain hover.</summary>
	public static void EndAimSelection()
	{
		aimUsers = Mathf.Max(0, aimUsers - 1);
		if (aimUsers == 0)
		{
			foreach (Container container in all)
			{
				container.SetMaterial(container.isHovered ? container.hilightMaterial : container.defaultMaterial);
			}
		}
	}

	/// <summary>Highlights the given container (the one a release would use) and un-highlights the others.</summary>
	public static void SetAimTarget(Container target)
	{
		foreach (Container container in all)
		{
			container.SetMaterial(container == target ? container.hilightMaterial : container.defaultMaterial);
		}
		if (target != null && GameManager.Instance != null)
		{
			GameManager.Instance.OnContainerHovered(target);
		}
	}

	/// <summary>
	/// The container whose horizontal direction is closest to the horizontal direction of the aim, within tolerance;
	/// null when aiming clearly away from all of them (or almost straight up / down).
	/// </summary>
	public static Container PickByHorizontalAim(Vector3 origin, Vector3 direction, Container current)
	{
		Vector3 flat = new Vector3(direction.x, 0f, direction.z);
		if (flat.sqrMagnitude < 0.04f * direction.sqrMagnitude)
		{
			return null;
		}
		Container best = null;
		float bestAngle = float.MaxValue;
		foreach (Container container in all)
		{
			if (!container.isActiveAndEnabled)
			{
				continue;
			}
			Vector3 to = container.AimPoint - origin;
			to.y = 0f;
			if (to.sqrMagnitude < 0.0001f)
			{
				continue;
			}
			float angle = Vector3.Angle(flat, to);
			float limit = container == current ? AimKeepDeg : AimToleranceDeg;
			if (angle <= limit && angle < bestAngle)
			{
				best = container;
				bestAngle = angle;
			}
		}
		return best;
	}

	private Vector3 AimPoint
	{
		get
		{
			if (colliders.Count > 0 && colliders[0] != null)
			{
				return colliders[0].bounds.center;
			}
			return transform.position;
		}
	}

	private void Start()
	{
		childrenRenderers = GetComponentsInChildren<MeshRenderer>();
		_renderer = GetComponent<MeshRenderer>();
		SetMaterial(defaultMaterial);
	}

	private void SetMaterial(Material material)
	{
		if (childrenRenderers == null)
		{
			return;
		}
		if (_renderer)
		{
			_renderer.material = material;
		}
		foreach (MeshRenderer childRenderer in childrenRenderers)
		{
			childRenderer.material = material;
		}
	}

	protected override void OnHoverEntered(HoverEnterEventArgs args)
	{
		base.OnHoverEntered(args);
		if (aimUsers > 0)
		{
			return;
		}
		SetMaterial(hilightMaterial);
		GameManager.Instance.OnContainerHovered(this);
	}

	protected override void OnHoverExited(HoverExitEventArgs args)
	{
		base.OnHoverExited(args);
		if (aimUsers > 0)
		{
			return;
		}
		SetMaterial(defaultMaterial);
	}
}

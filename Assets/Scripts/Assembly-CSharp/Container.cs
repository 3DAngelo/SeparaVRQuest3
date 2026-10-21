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

	private MeshRenderer[] childrenRenderers;

	public Material defaultMaterial;

	// ---- Choice of container by horizontal aim while an object is held (see CustomGrabInteractable.Update) ----

	// A container is chosen when the horizontal direction of the aim is within this angle of the horizontal direction
	// to the container (pitch is ignored). Seen from the player the two containers of NuevoJuego are about 72 degrees
	// apart and each is about 12 degrees wide to each side, so 28 leaves a neutral zone of about 16 degrees between them.
	public const float AimToleranceDeg = 28f;

	// Once chosen, a container stays chosen up to this angle (hysteresis, so it does not flicker at the edge).
	public const float AimKeepDeg = 33f;

	// ---- Highlight look (the container a release would use, only while an object is held) ----
	// The container keeps its own look (a private copy of defaultMaterial) with a soft pulse: its base colour is only
	// gently lerped toward the outline colour and a little emission of that colour is added. Plus a pulsing outline.

	// Soft pulse, cycles per second.
	public const float PulseHz = HighlightPulse.PulseHz;
	// Fraction of the way the base colour moves toward the outline colour at the bottom / top of the pulse (0 = no tint).
	public const float PulseMinTint = 0.05f;
	public const float PulseMaxTint = 0.12f;
	// Emission added on top, as a fraction of the outline colour, at the bottom / top of the pulse.
	public const float PulseMinEmission = 0.03f;
	public const float PulseMaxEmission = 0.12f;
	// Outline thickness in world units (metres), constant at any distance.
	public const float OutlineWidth = 0.01f;
	// The outline breathes a little too: width and brightness factors at the bottom of the pulse (top = 1).
	public const float OutlineMinWidthFactor = HighlightPulse.OutlineMinWidthFactor;
	public const float OutlineMinBrightness = HighlightPulse.OutlineMinBrightness;
	public static readonly Color OutlineOrange = new Color(1f, 0.45f, 0f, 1f);
	public static readonly Color OutlineGreen = new Color(0.1f, 1f, 0.2f, 1f);
	// Colour of the outline line itself (the same for every container); orange / green above only tint the body pulse.
	public static readonly Color OutlineLineColor = Color.white;
	// Assets/Resources/ContainerOutline.shader
	public const string OutlineShaderResource = HighlightPulse.OutlineShaderResource;

	private bool highlighted;
	private Color outlineColor;
	private HighlightPulse pulse;
	private HighlightPulse.PulsingMaterial pulseMaterial;

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

	/// <summary>An object has been grabbed: from now on the container chosen by aim is highlighted.</summary>
	public static void BeginAimSelection()
	{
		aimUsers++;
		SetAimTarget(null);
	}

	/// <summary>The object was released: with nothing held, no container is highlighted.</summary>
	public static void EndAimSelection()
	{
		aimUsers = Mathf.Max(0, aimUsers - 1);
		if (aimUsers == 0)
		{
			foreach (Container container in all)
			{
				container.SetHighlighted(false);
			}
		}
	}

	/// <summary>Highlights the given container (the one a release would use) and un-highlights the others.</summary>
	public static void SetAimTarget(Container target)
	{
		foreach (Container container in all)
		{
			container.SetHighlighted(container == target);
		}
		if (target != null && GameManager.HasInstance)
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

	public Vector3 AimPoint
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
		CreateHighlightResources();
		highlighted = false;
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

	// ---------------------------------------------------------------- highlight: softly pulsing own material + coloured outline (HighlightPulse)

	private void CreateHighlightResources()
	{
		string id = (name + " " + descripcion).ToLowerInvariant();
		if (id.Contains("naranja"))
		{
			outlineColor = OutlineOrange;
		}
		else if (id.Contains("verde"))
		{
			outlineColor = OutlineGreen;
		}
		else
		{
			outlineColor = IDcontainer == Type.RECYCLE ? OutlineOrange : OutlineGreen;
		}

		// One private copy per container, so that pulsing never touches the material asset. Based on the container's own
		// look (without a default material only the outline is shown).
		if (defaultMaterial != null)
		{
			pulseMaterial = new HighlightPulse.PulsingMaterial(defaultMaterial);
		}

		pulse = new HighlightPulse("Container " + name, OutlineLineColor);
		pulse.Width = OutlineWidth;
	}

	private void SetHighlighted(bool value)
	{
		if (childrenRenderers == null || value == highlighted)
		{
			return;   // (null: Start has not run yet)
		}
		highlighted = value;
		if (!value)
		{
			pulse.End();
			SetMaterial(defaultMaterial);
			return;
		}
		pulse.Begin();
		if (pulseMaterial != null)
		{
			// sharedMaterial: every renderer shows the very same instance, the one UpdateHighlight animates.
			if (_renderer)
			{
				_renderer.sharedMaterial = pulseMaterial.Copy;
			}
			foreach (MeshRenderer childRenderer in childrenRenderers)
			{
				childRenderer.sharedMaterial = pulseMaterial.Copy;
			}
		}
		UpdateHighlight();
	}

	private void LateUpdate()
	{
		if (highlighted)
		{
			UpdateHighlight();
		}
	}

	private void UpdateHighlight()
	{
		if (pulseMaterial != null)
		{
			float p = pulse.Value;
			pulseMaterial.Apply(outlineColor, Mathf.Lerp(PulseMinTint, PulseMaxTint, p), 1f, outlineColor * Mathf.Lerp(PulseMinEmission, PulseMaxEmission, p));
		}
		pulse.DrawOutline(childrenRenderers, gameObject.layer);
	}

	protected override void OnDestroy()
	{
		if (pulseMaterial != null)
		{
			pulseMaterial.Destroy();
			pulseMaterial = null;
		}
		if (pulse != null)
		{
			pulse.Dispose();
			pulse = null;
		}
		base.OnDestroy();
	}

	// Hovering a container with an empty hand does NOT highlight it: the highlight only exists while an object is
	// held (BeginAimSelection .. EndAimSelection), and then it follows the aim, not the hover.
}

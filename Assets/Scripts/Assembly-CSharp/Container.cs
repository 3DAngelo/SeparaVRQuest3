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

	// ---- Highlight look (the container a release would use, or the hovered one) ----

	// Soft pulse, cycles per second.
	public const float PulseHz = 0.9f;
	// Brightness of the highlight material's base colour: multiplier at the bottom / top of the pulse.
	public const float PulseMinBrightness = 1.0f;
	public const float PulseMaxBrightness = 1.5f;
	// Emission added on top, as a fraction of the outline colour, at the bottom / top of the pulse.
	public const float PulseMinEmission = 0.10f;
	public const float PulseMaxEmission = 0.45f;
	// Outline thickness as a fraction of the view height (0.012 is roughly 10 pixels per eye on a Quest 3).
	public const float OutlineWidth = 0.012f;
	// The outline breathes a little too: width and brightness factors at the bottom of the pulse (top = 1).
	public const float OutlineMinWidthFactor = 0.75f;
	public const float OutlineMinBrightness = 0.8f;
	public static readonly Color OutlineOrange = new Color(1f, 0.45f, 0f, 1f);
	public static readonly Color OutlineGreen = new Color(0.1f, 1f, 0.2f, 1f);
	// Assets/Resources/ContainerOutline.shader
	public const string OutlineShaderResource = "ContainerOutline";

	private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
	private static readonly int ColorId = Shader.PropertyToID("_Color");
	private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
	private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
	private static readonly int OutlineCenterId = Shader.PropertyToID("_OutlineCenter");
	private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

	private bool highlighted;
	private float highlightStartTime;
	private Color outlineColor;
	private Material pulseMaterial;
	private int pulseColorId = -1;
	private Color pulseBaseColor = Color.white;
	private bool pulseHasEmission;
	private Material outlineMaterial;

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
				container.SetHighlighted(container.isHovered);
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

	// ---------------------------------------------------------------- highlight: brighter pulsing material + coloured outline

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

		if (hilightMaterial != null)
		{
			// One private copy per container, so that pulsing never touches the material asset.
			pulseMaterial = new Material(hilightMaterial);
			pulseMaterial.name = hilightMaterial.name + " (pulse)";
			pulseColorId = pulseMaterial.HasProperty(BaseColorId) ? BaseColorId : (pulseMaterial.HasProperty(ColorId) ? ColorId : -1);
			if (pulseColorId != -1)
			{
				pulseBaseColor = pulseMaterial.GetColor(pulseColorId);
			}
			pulseHasEmission = pulseMaterial.HasProperty(EmissionColorId);
			if (pulseHasEmission)
			{
				// Only visible if the emission variant of the shader is in the build. It is for URP Lit here
				// ("container Naranja highlight" uses it); if it were not, the base colour pulse alone remains.
				pulseMaterial.EnableKeyword("_EMISSION");
				pulseMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
			}
		}

		// The shader lives in Assets/Resources so that it is always included in the build (Shader.Find alone would not do that).
		Shader outlineShader = Resources.Load<Shader>(OutlineShaderResource);
		if (outlineShader != null && outlineShader.isSupported)
		{
			outlineMaterial = new Material(outlineShader);
			outlineMaterial.name = "Container outline (" + name + ")";
			outlineMaterial.enableInstancing = true;
		}
		else
		{
			Debug.LogWarning("Container: outline shader 'Resources/" + OutlineShaderResource + "' not found or not supported; the highlight will have no outline.");
		}
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
			SetMaterial(defaultMaterial);
			return;
		}
		highlightStartTime = Time.time;
		if (pulseMaterial == null)
		{
			SetMaterial(hilightMaterial);
		}
		else
		{
			// sharedMaterial: every renderer shows the very same instance, the one UpdateHighlight animates.
			if (_renderer)
			{
				_renderer.sharedMaterial = pulseMaterial;
			}
			foreach (MeshRenderer childRenderer in childrenRenderers)
			{
				childRenderer.sharedMaterial = pulseMaterial;
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
		// 0..1, soft (sine), starting at mid level and rising.
		float pulse = 0.5f + 0.5f * Mathf.Sin((Time.time - highlightStartTime) * PulseHz * 2f * Mathf.PI);

		if (pulseMaterial != null)
		{
			if (pulseColorId != -1)
			{
				Color c = pulseBaseColor * Mathf.Lerp(PulseMinBrightness, PulseMaxBrightness, pulse);
				c.a = pulseBaseColor.a;
				pulseMaterial.SetColor(pulseColorId, c);
			}
			if (pulseHasEmission)
			{
				Color e = outlineColor * Mathf.Lerp(PulseMinEmission, PulseMaxEmission, pulse);
				e.a = 1f;
				pulseMaterial.SetColor(EmissionColorId, e);
			}
		}

		if (outlineMaterial == null)
		{
			return;
		}
		// Centre of the container: the outline is its silhouette pushed away from this point on screen.
		bool any = false;
		Bounds bounds = new Bounds(transform.position, Vector3.zero);
		foreach (MeshRenderer childRenderer in childrenRenderers)
		{
			if (childRenderer == null || !childRenderer.enabled || !childRenderer.gameObject.activeInHierarchy)
			{
				continue;
			}
			if (!any)
			{
				bounds = childRenderer.bounds;
				any = true;
			}
			else
			{
				bounds.Encapsulate(childRenderer.bounds);
			}
		}
		if (!any)
		{
			return;
		}
		Color oc = outlineColor * Mathf.Lerp(OutlineMinBrightness, 1f, pulse);
		oc.a = 1f;
		outlineMaterial.SetColor(OutlineColorId, oc);
		outlineMaterial.SetVector(OutlineCenterId, bounds.center);
		outlineMaterial.SetFloat(OutlineWidthId, OutlineWidth * Mathf.Lerp(OutlineMinWidthFactor, 1f, pulse));

		RenderParams rp = new RenderParams(outlineMaterial);
		rp.layer = gameObject.layer;
		rp.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		rp.receiveShadows = false;
		rp.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
		rp.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
		foreach (MeshRenderer childRenderer in childrenRenderers)
		{
			if (childRenderer == null || !childRenderer.enabled || !childRenderer.gameObject.activeInHierarchy)
			{
				continue;
			}
			MeshFilter filter = childRenderer.GetComponent<MeshFilter>();
			Mesh mesh = filter != null ? filter.sharedMesh : null;
			if (mesh == null)
			{
				continue;
			}
			// A static-batched renderer draws a range of sub-meshes of the combined mesh, whose vertices are already
			// in world space.
			bool batched = childRenderer.isPartOfStaticBatch;
			int first = batched ? childRenderer.subMeshStartIndex : 0;
			int count = batched ? Mathf.Max(1, childRenderer.sharedMaterials.Length) : mesh.subMeshCount;
			// (Scene static batching has no batch root, here and in the original build: the vertices are in world space.)
			Matrix4x4 matrix = batched ? Matrix4x4.identity : childRenderer.transform.localToWorldMatrix;
			for (int i = 0; i < count; i++)
			{
				int subMesh = first + i;
				if (subMesh >= 0 && subMesh < mesh.subMeshCount)
				{
					Graphics.RenderMesh(rp, mesh, subMesh, matrix);
				}
			}
		}
	}

	protected override void OnDestroy()
	{
		if (pulseMaterial != null)
		{
			Destroy(pulseMaterial);
		}
		if (outlineMaterial != null)
		{
			Destroy(outlineMaterial);
		}
		base.OnDestroy();
	}

	protected override void OnHoverEntered(HoverEnterEventArgs args)
	{
		base.OnHoverEntered(args);
		if (aimUsers > 0)
		{
			return;
		}
		SetHighlighted(true);
		GameManager.Instance.OnContainerHovered(this);
	}

	protected override void OnHoverExited(HoverExitEventArgs args)
	{
		base.OnHoverExited(args);
		if (aimUsers > 0)
		{
			return;
		}
		SetHighlighted(false);
	}
}

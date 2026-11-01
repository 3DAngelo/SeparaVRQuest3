using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shared look of the highlights: a soft pulse (0..1), private pulsing copies of materials (<see cref="PulsingMaterial"/>)
/// and an outline of constant width in world units, drawn every frame with Graphics.RenderMesh using
/// Assets/Resources/ContainerOutline.shader.
///
/// Used by Container (the container a release would use, coloured outline) and CustomGrabInteractable (the object being
/// pointed at, white outline). Plain class: the owner calls Begin / End, DrawOutline every frame while active (from
/// LateUpdate) and Dispose from OnDestroy.
/// </summary>
public class HighlightPulse
{
	// Soft pulse, cycles per second.
	public const float PulseHz = 0.9f;
	// Outline thickness in world units (metres), constant at any distance.
	public const float OutlineWidth = 0.05f;
	// ...but never thinner than this many pixels of the render target (per eye in XR), so that small or distant objects
	// still get a visible border. Quest 3 eye buffer: roughly 15-20 px per degree at the centre.
	public const float OutlineMinPixels = 4f;
	// The outline breathes a little too: width and brightness factors at the bottom of the pulse (top = 1).
	public const float OutlineMinWidthFactor = 0.75f;
	public const float OutlineMinBrightness = 0.8f;
	// Assets/Resources/ContainerOutline.shader
	public const string OutlineShaderResource = "ContainerOutline";

	public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
	public static readonly int ColorId = Shader.PropertyToID("_Color");
	public static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
	private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
	private static readonly int OutlineCenterId = Shader.PropertyToID("_OutlineCenter");
	private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
	private static readonly int OutlineMinPixelsId = Shader.PropertyToID("_OutlineMinPixels");
	// The two passes of the shader, by LightMode, each one drawn with its own material and render queue: all the masks
	// of the object (Geometry+9) are in the stencil before any of its outlines (Geometry+10) is drawn.
	private const string MaskPassName = "SRPDefaultUnlit";
	private const string OutlinePassName = "UniversalForward";
	private const int MaskQueue = (int)RenderQueue.Geometry + 9;
	private const int OutlineQueue = (int)RenderQueue.Geometry + 10;

	/// <summary>Colour of the outline at the top of the pulse.</summary>
	public Color OutlineColor;
	/// <summary>Grosor del outline en unidades de mundo (metros) para este dueño.</summary>
	public float Width = OutlineWidth;
	/// <summary>Grosor mínimo del outline en píxeles (por ojo) para este dueño.</summary>
	public float MinPixels = OutlineMinPixels;

	private Material maskMaterial;
	private Material outlineMaterial;
	private float startTime;
	private bool active;

	public HighlightPulse(string ownerName, Color outlineColor)
	{
		OutlineColor = outlineColor;
		// The shader lives in Assets/Resources so that it is always included in the build (Shader.Find alone would not do that).
		Shader outlineShader = Resources.Load<Shader>(OutlineShaderResource);
		if (outlineShader != null && outlineShader.isSupported)
		{
			maskMaterial = new Material(outlineShader);
			maskMaterial.name = "Highlight outline mask (" + ownerName + ")";
			maskMaterial.enableInstancing = true;
			maskMaterial.renderQueue = MaskQueue;
			maskMaterial.SetShaderPassEnabled(OutlinePassName, false);
			outlineMaterial = new Material(outlineShader);
			outlineMaterial.name = "Highlight outline (" + ownerName + ")";
			outlineMaterial.enableInstancing = true;
			outlineMaterial.renderQueue = OutlineQueue;
			outlineMaterial.SetShaderPassEnabled(MaskPassName, false);
		}
		else
		{
			#if UNITY_EDITOR
			Debug.LogWarning("HighlightPulse: outline shader 'Resources/" + OutlineShaderResource + "' not found or not supported; the highlight of '" + ownerName + "' will have no outline.");
			#endif
		}
	}

	public bool IsActive
	{
		get { return active; }
	}

	/// <summary>Starts (or restarts) the pulse from its mid level, rising.</summary>
	public void Begin()
	{
		active = true;
		startTime = Time.time;
	}

	public void End()
	{
		active = false;
	}

	/// <summary>0..1, soft (sine), starting at mid level and rising when <see cref="Begin"/> was called.</summary>
	public float Value
	{
		get { return 0.5f + 0.5f * Mathf.Sin((Time.time - startTime) * PulseHz * 2f * Mathf.PI); }
	}

	/// <summary>
	/// Draws the outline of the given renderers for this frame (call it every frame while highlighted). The outline is
	/// the joint silhouette pushed away, on screen, from the centre of their joint bounds.
	/// </summary>
	public void DrawOutline(IList<MeshRenderer> renderers, int layer)
	{
		DrawOutline(renderers, layer, Matrix4x4.identity);
	}

	/// <summary>
	/// The same, with the (not static-batched) meshes displaced by <paramref name="worldOffset"/>: an object shown slightly
	/// off its real pose (CustomGrabInteractable.HoverShake) keeps its outline around what is seen.
	/// </summary>
	public void DrawOutline(IList<MeshRenderer> renderers, int layer, Matrix4x4 worldOffset)
	{
		if (outlineMaterial == null || maskMaterial == null || renderers == null)
		{
			return;
		}
		float pulse = Value;

		// Centre of the object: the outline is its silhouette pushed away from this point on screen.
		bool any = false;
		Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
		for (int r = 0; r < renderers.Count; r++)
		{
			MeshRenderer meshRenderer = renderers[r];
			if (!IsDrawable(meshRenderer))
			{
				continue;
			}
			if (!any)
			{
				bounds = meshRenderer.bounds;
				any = true;
			}
			else
			{
				bounds.Encapsulate(meshRenderer.bounds);
			}
		}
		if (!any)
		{
			return;
		}
		Color oc = OutlineColor * Mathf.Lerp(OutlineMinBrightness, 1f, pulse);
		oc.a = 1f;
		outlineMaterial.SetColor(OutlineColorId, oc);
		outlineMaterial.SetVector(OutlineCenterId, worldOffset.MultiplyPoint3x4(bounds.center));
		float widthFactor = Mathf.Lerp(OutlineMinWidthFactor, 1f, pulse);
		outlineMaterial.SetFloat(OutlineWidthId, Width * widthFactor);
		outlineMaterial.SetFloat(OutlineMinPixelsId, MinPixels * widthFactor);

		RenderParams maskParams = MakeRenderParams(maskMaterial, layer);
		RenderParams outlineParams = MakeRenderParams(outlineMaterial, layer);
		for (int r = 0; r < renderers.Count; r++)
		{
			MeshRenderer meshRenderer = renderers[r];
			if (!IsDrawable(meshRenderer))
			{
				continue;
			}
			MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
			Mesh mesh = filter != null ? filter.sharedMesh : null;
			if (mesh == null)
			{
				continue;
			}
			// A static-batched renderer draws a range of sub-meshes of the combined mesh, whose vertices are already
			// in world space.
			bool batched = meshRenderer.isPartOfStaticBatch;
			int first = batched ? meshRenderer.subMeshStartIndex : 0;
			int count = batched ? Mathf.Max(1, meshRenderer.sharedMaterials.Length) : mesh.subMeshCount;
			// (Scene static batching has no batch root, here and in the original build: the vertices are in world space.)
			Matrix4x4 matrix = batched ? Matrix4x4.identity : worldOffset * meshRenderer.transform.localToWorldMatrix;
			for (int i = 0; i < count; i++)
			{
				int subMesh = first + i;
				if (subMesh >= 0 && subMesh < mesh.subMeshCount)
				{
					Graphics.RenderMesh(maskParams, mesh, subMesh, matrix);
					Graphics.RenderMesh(outlineParams, mesh, subMesh, matrix);
				}
			}
		}
	}

	private static RenderParams MakeRenderParams(Material material, int layer)
	{
		RenderParams rp = new RenderParams(material);
		rp.layer = layer;
		rp.shadowCastingMode = ShadowCastingMode.Off;
		rp.receiveShadows = false;
		rp.lightProbeUsage = LightProbeUsage.Off;
		rp.reflectionProbeUsage = ReflectionProbeUsage.Off;
		return rp;
	}

	private static bool IsDrawable(MeshRenderer meshRenderer)
	{
		return meshRenderer != null && meshRenderer.enabled && meshRenderer.gameObject.activeInHierarchy;
	}

	public void Dispose()
	{
		active = false;
		if (outlineMaterial != null)
		{
			Object.Destroy(outlineMaterial);
			outlineMaterial = null;
		}
		if (maskMaterial != null)
		{
			Object.Destroy(maskMaterial);
			maskMaterial = null;
		}
	}

	/// <summary>
	/// A private copy of a material (so pulsing never touches the asset) whose base colour (_BaseColor, or _Color) and
	/// emission (_EmissionColor) are animated by <see cref="Apply"/>. Transparency, textures and every other setting are
	/// those of the source.
	/// </summary>
	public sealed class PulsingMaterial
	{
		public readonly Material Source;
		public readonly Material Copy;

		private readonly int colorId = -1;
		private readonly Color baseColor = Color.white;
		private readonly bool hasEmission;

		public PulsingMaterial(Material source)
		{
			Source = source;
			Copy = new Material(source);
			Copy.name = source.name + " (pulse)";
			colorId = Copy.HasProperty(BaseColorId) ? BaseColorId : (Copy.HasProperty(ColorId) ? ColorId : -1);
			if (colorId != -1)
			{
				baseColor = Copy.GetColor(colorId);
			}
			hasEmission = Copy.HasProperty(EmissionColorId);
			if (hasEmission)
			{
				// Only visible if the emission variant of the shader is in the build. It is for URP Lit here (several
				// scene materials use it); if it were not, the base colour pulse alone remains.
				Copy.EnableKeyword("_EMISSION");
				Copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
			}
		}

		/// <summary>
		/// Base colour = source base colour lerped toward <paramref name="tint"/> by <paramref name="tintAmount"/>
		/// (0 = no tint), times <paramref name="brightness"/>; alpha always that of the source. Emission = <paramref name="emission"/>.
		/// </summary>
		public void Apply(Color tint, float tintAmount, float brightness, Color emission)
		{
			if (colorId != -1)
			{
				Color c = tintAmount > 0f ? Color.Lerp(baseColor, tint, tintAmount) : baseColor;
				c *= brightness;
				c.a = baseColor.a;
				Copy.SetColor(colorId, c);
			}
			if (hasEmission)
			{
				emission.a = 1f;
				Copy.SetColor(EmissionColorId, emission);
			}
		}

		public void Destroy()
		{
			if (Copy != null)
			{
				Object.Destroy(Copy);
			}
		}
	}
}

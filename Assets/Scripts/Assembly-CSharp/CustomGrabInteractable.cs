using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Receiver.Rendering;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Rendering;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.State;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

public class CustomGrabInteractable : XRGrabInteractable
{
	private XRRayInteractor rayInteractor;

	private NearFarInteractor farInteractor;

	private Container currentTargetContainer;

	private bool aimSelecting;

	// Hand tracking: pose of the object in the hand (see HandHoldPose). It must run AFTER XRI's default transformer,
	// which keeps computing the pose for controllers; this one only overrides it for a tracked hand.
	private HandHoldPose handHoldPose;

	// ---- Highlight of the pointed object: hovered by any interactor (ray / far or near, hand or controller) and not held ----
	// Same pulse and outline as the chosen container (HighlightPulse), but with a white outline and no colour tint on
	// the object: only a neutral brightness pulse (hue unchanged) and a tiny white emission.

	// Outline colour (neutral).
	public static readonly Color HoverOutlineColor = Color.white;
	// Multiplier of the object's own base colour at the bottom / top of the pulse (1 = unchanged).
	public const float HoverMinBrightness = 1.0f;
	public const float HoverMaxBrightness = 1.15f;
	// White emission added on top at the bottom / top of the pulse.
	public const float HoverMinEmission = 0.0f;
	public const float HoverMaxEmission = 0.04f;

	private HighlightPulse hoverPulse;
	private bool hoverHighlighted;
	// Released over the right container: it is being thrown into it (and then destroyed), so it is never highlighted again.
	private bool dumped;
	// The object's MeshRenderers (own and children), taken in Start.
	private MeshRenderer[] hoverRenderers;
	// Per renderer, its sharedMaterials as they were when the highlight began; restored exactly when it ends.
	private Material[][] savedMaterials;
	// Pulsing private copies, one per source material, made the first time that material is highlighted and then reused.
	private readonly Dictionary<Material, HighlightPulse.PulsingMaterial> pulseCopies = new Dictionary<Material, HighlightPulse.PulsingMaterial>();

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
			// Last, so it blends whatever pose the others computed (hand or controller).
			flyInPose = new FlyInPose();
			AddSingleGrabTransformer(flyInPose);
		}
	}

	private FlyInPose flyInPose;

	/// <summary>
	/// On grab, the object travels from where it was to the hand in <see cref="FlyInTime"/> seconds (eased), instead of
	/// snapping there. Objects already within <see cref="FlyInMinDistance"/> of their hold pose are not slowed down.
	/// </summary>
	private sealed class FlyInPose : IXRGrabTransformer
	{
		public const float FlyInTime = 0.5f;
		public const float FlyInMinDistance = 0.1f;

		private Pose startPose;
		private float startTime;
		private bool flying;
		private bool started;

		public bool canProcess
		{
			get { return true; }
		}

		public void OnLink(XRGrabInteractable grabInteractable)
		{
		}

		public void OnUnlink(XRGrabInteractable grabInteractable)
		{
		}

		public void OnGrab(XRGrabInteractable grabInteractable)
		{
			Begin(grabInteractable.transform);
		}

		// Also called from OnSelectEntered: on the FIRST grab this transformer is still pending registration, so XRI does
		// not send it OnGrab (the object has not been moved yet at that point).
		public void Begin(Transform t)
		{
			startPose = new Pose(t.position, t.rotation);
			startTime = Time.time;
			flying = true;
			started = false;
		}

		public void OnGrabCountChanged(XRGrabInteractable grabInteractable, Pose targetPose, Vector3 localScale)
		{
		}

		public void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
		{
			if (!flying || (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic && updatePhase != XRInteractionUpdateOrder.UpdatePhase.OnBeforeRender))
			{
				return;
			}
			if (!started)
			{
				started = true;
				// Distance to the hand / controller itself (with a controller the target pose starts at the far hit point).
				Vector3 holder = grabInteractable.interactorsSelecting.Count > 0 ? grabInteractable.interactorsSelecting[0].transform.position : targetPose.position;
				if (Vector3.Distance(startPose.position, holder) < FlyInMinDistance)
				{
					flying = false;   // grabbed in the hand: no travel
					return;
				}
			}
			float t = (Time.time - startTime) / FlyInTime;
			if (t >= 1f)
			{
				flying = false;
				return;
			}
			float k = Mathf.SmoothStep(0f, 1f, t);
			targetPose = new Pose(Vector3.Lerp(startPose.position, targetPose.position, k), Quaternion.Slerp(startPose.rotation, targetPose.rotation, k));
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
		if (flyInPose != null && interactorsSelecting.Count == 1)
		{
			flyInPose.Begin(transform);
		}
		UpdateHoverHighlight();
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
				dumped = true;
				if (GameManager.Instance != null)
				{
					GameManager.Instance.IniciarArrojarResiduos(currentTargetContainer, residuo);
				}
			}
			else
			{
				Rigidbody body = GetComponent<Rigidbody>();
				body.isKinematic = false;
				Debug.Log("Incorrect Container");
				GameManager.Instance.GetComponent<AudioSource>().Play();
				StartCoroutine(TossToward(currentTargetContainer.AimPoint));
			}
		}
		currentTargetContainer = null;
		UpdateHoverHighlight();
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

	// ---- Released over the WRONG container: small toss toward it, so it does not fall at the player's feet ----
	// Initial velocity: horizontal toward the container + upward. Dropped from hand height (~1 m) it lands about
	// 0.8 m ahead (flight ~0.6 s).
	public const float WrongTossHorizontalSpeed = 1.3f;
	public const float WrongTossUpSpeed = 1.2f;

	private IEnumerator TossToward(Vector3 target)
	{
		// XRI applies its own throw velocity on the first physics step after release; set ours after it.
		yield return new WaitForFixedUpdate();
		Rigidbody body = GetComponent<Rigidbody>();
		if (body == null || body.isKinematic || isSelected)
		{
			yield break;   // grabbed again (or made kinematic) in the meantime
		}
		Vector3 flat = target - body.position;
		flat.y = 0f;
		if (flat.sqrMagnitude < 1e-4f)
		{
			yield break;
		}
		body.linearVelocity = flat.normalized * WrongTossHorizontalSpeed + Vector3.up * WrongTossUpSpeed;
		body.angularVelocity = Vector3.zero;
	}

	private void Start()
	{
		hoverRenderers = GetComponentsInChildren<MeshRenderer>();
		hoverPulse = new HighlightPulse("Grabbable " + name, HoverOutlineColor);
		DisableAffordanceTint();
		UpdateHoverHighlight();
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

	// ---------------------------------------------------------------- highlight of the pointed object (HighlightPulse)

	protected override void OnHoverEntered(HoverEnterEventArgs args)
	{
		base.OnHoverEntered(args);
		UpdateHoverHighlight();
	}

	protected override void OnHoverExited(HoverExitEventArgs args)
	{
		base.OnHoverExited(args);
		UpdateHoverHighlight();   // (stays on while any other interactor still hovers)
	}

	protected override void OnDisable()
	{
		SetHoverHighlighted(false);
		base.OnDisable();
	}

	private void UpdateHoverHighlight()
	{
		SetHoverHighlighted(isHovered && !isSelected && !dumped && isActiveAndEnabled);
	}

	/// <summary>
	/// The scene objects carry the XRI starter "Affordance" child (XRInteractableAffordanceStateProvider +
	/// ColorMaterialPropertyAffordanceReceiver with the HighlightInteractionColor theme), which tints _BaseColor through
	/// a MaterialPropertyBlock: peach on hover, light blue while held. That is the colour tint the pointed objects must
	/// not have, and the property block would also override the pulse; so it is switched off here, replaced by the
	/// HighlightPulse highlight.
	/// </summary>
	private void DisableAffordanceTint()
	{
#pragma warning disable 0618 // the XRI affordance system is deprecated (but still present in XRI 3.0)
		foreach (XRInteractableAffordanceStateProvider provider in GetComponentsInChildren<XRInteractableAffordanceStateProvider>(true))
		{
			provider.enabled = false;
		}
		foreach (ColorMaterialPropertyAffordanceReceiver receiver in GetComponentsInChildren<ColorMaterialPropertyAffordanceReceiver>(true))
		{
			receiver.enabled = false;
		}
		foreach (MaterialPropertyBlockHelper helper in GetComponentsInChildren<MaterialPropertyBlockHelper>(true))
		{
			helper.enabled = false;
		}
#pragma warning restore 0618
		// Remove whatever colour the receiver may already have written into the renderers' property blocks.
		MaterialPropertyBlock block = null;
		foreach (MeshRenderer meshRenderer in hoverRenderers)
		{
			if (meshRenderer == null || !meshRenderer.HasPropertyBlock())
			{
				continue;
			}
			if (block == null)
			{
				block = new MaterialPropertyBlock();
			}
			int count = Mathf.Max(1, meshRenderer.sharedMaterials.Length);
			for (int i = 0; i < count; i++)
			{
				meshRenderer.GetPropertyBlock(block, i);
				if (!block.isEmpty)
				{
					block.Clear();
					meshRenderer.SetPropertyBlock(block, i);
				}
			}
			meshRenderer.GetPropertyBlock(block);
			if (!block.isEmpty)
			{
				block.Clear();
				meshRenderer.SetPropertyBlock(block);
			}
		}
	}

	private void SetHoverHighlighted(bool value)
	{
		if (hoverRenderers == null || hoverPulse == null || value == hoverHighlighted)
		{
			return;   // (null: Start has not run yet)
		}
		hoverHighlighted = value;
		if (!value)
		{
			hoverPulse.End();
			RestoreMaterials();
			return;
		}
		hoverPulse.Begin();
		// The materials are taken now, not in Start: Residuo.Start replaces them (with an instance of its defaultMaterial).
		savedMaterials = new Material[hoverRenderers.Length][];
		for (int r = 0; r < hoverRenderers.Length; r++)
		{
			MeshRenderer meshRenderer = hoverRenderers[r];
			if (meshRenderer == null)
			{
				continue;
			}
			Material[] original = meshRenderer.sharedMaterials;
			savedMaterials[r] = original;
			Material[] pulsing = new Material[original.Length];
			for (int i = 0; i < original.Length; i++)
			{
				pulsing[i] = original[i] != null ? GetPulseCopy(original[i]).Copy : null;
			}
			meshRenderer.sharedMaterials = pulsing;
		}
		UpdateHoverPulse();
	}

	private HighlightPulse.PulsingMaterial GetPulseCopy(Material source)
	{
		HighlightPulse.PulsingMaterial copy;
		if (!pulseCopies.TryGetValue(source, out copy))
		{
			copy = new HighlightPulse.PulsingMaterial(source);
			pulseCopies.Add(source, copy);
		}
		return copy;
	}

	private void RestoreMaterials()
	{
		if (savedMaterials == null)
		{
			return;
		}
		for (int r = 0; r < hoverRenderers.Length && r < savedMaterials.Length; r++)
		{
			if (hoverRenderers[r] != null && savedMaterials[r] != null)
			{
				hoverRenderers[r].sharedMaterials = savedMaterials[r];
			}
		}
		savedMaterials = null;
	}

	private void LateUpdate()
	{
		if (hoverHighlighted)
		{
			UpdateHoverPulse();
		}
	}

	private void UpdateHoverPulse()
	{
		float p = hoverPulse.Value;
		float brightness = Mathf.Lerp(HoverMinBrightness, HoverMaxBrightness, p);
		Color emission = HoverOutlineColor * Mathf.Lerp(HoverMinEmission, HoverMaxEmission, p);
		foreach (HighlightPulse.PulsingMaterial copy in pulseCopies.Values)
		{
			copy.Apply(Color.white, 0f, brightness, emission);
		}
		hoverPulse.DrawOutline(hoverRenderers, gameObject.layer);
	}

	protected override void OnDestroy()
	{
		SetHoverHighlighted(false);
		foreach (HighlightPulse.PulsingMaterial copy in pulseCopies.Values)
		{
			copy.Destroy();
		}
		pulseCopies.Clear();
		if (hoverPulse != null)
		{
			hoverPulse.Dispose();
			hoverPulse = null;
		}
		base.OnDestroy();
	}
}

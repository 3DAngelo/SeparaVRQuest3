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
	// Grosor del outline del objeto en unidades de mundo (metros).
	public const float HoverOutlineWidth = 0.005f;
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

	/// <summary>True while this object, just grabbed, is flying from where it was to the hand (FlyInPose).</summary>
	public bool IsFlyingToHand
	{
		get { return isSelected && flyInPose != null && flyInPose.Flying; }
	}

	/// <summary>True once released into the right container (it then flies in and is destroyed).</summary>
	public bool IsDumped
	{
		get { return dumped; }
	}

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

		/// <summary>True while the grabbed object is travelling to the hand (read by HandBeamFX).</summary>
		public bool Flying
		{
			get { return flying; }
		}

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
		// The grab starts: the shake stops first, so everything from here on sees the object exactly as it is.
		if (hoverShake != null)
		{
			hoverShake.End();
		}
		// Only if some other transformer got registered first (so XRI never called AddDefaultSingleGrabTransformer).
		if (handHoldPose == null && singleGrabTransformersCount > 0)
		{
			AddHandHoldPose();
		}
		base.OnSelectEntering(args);
	}

	// Once released into the right container it is flying in (and then destroyed): it can no longer be hovered or grabbed.
	public override bool IsSelectableBy(IXRSelectInteractor interactor)
	{
		return !dumped && base.IsSelectableBy(interactor);
	}

	public override bool IsHoverableBy(IXRHoverInteractor interactor)
	{
		return !dumped && base.IsHoverableBy(interactor);
	}

	protected override void OnSelectEntered(SelectEnterEventArgs args)
	{
		base.OnSelectEntered(args);
		if (flyInPose != null && interactorsSelecting.Count == 1)
		{
			flyInPose.Begin(transform);
		}
		UpdateHoverHighlight();
		if (interactorsSelecting.Count == 1)
		{
			GrabAudio.PlayGrab();
		}
		if (args.interactorObject is XRRayInteractor ray)
		{
			rayInteractor = ray;
			#if UNITY_EDITOR
			Debug.Log("Ray interactor detected");
			#endif
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
		if (GameManager.HasInstance)
		{
			GameManager.Instance.OnObjectGrabbed(gameObject);
		}
	}

	// Se dispara cada vez que se suelta el objeto: (objeto, contenedor elegido o null si se soltó sin apuntar a
	// ninguno, true si el contenedor era el correcto). Lo usa el tutorial de la Intro; el juego no lo necesita.
	public static event System.Action<CustomGrabInteractable, Container, bool> Soltado;

	protected override void OnSelectExited(SelectExitEventArgs args)
	{
		base.OnSelectExited(args);
		Container soltadoEn = currentTargetContainer;
		bool soltadoCorrecto = false;
		if (currentTargetContainer != null)
		{
			#if UNITY_EDITOR
			Debug.Log("Exit on Container: " + currentTargetContainer.descripcion);
			#endif
			if (currentTargetContainer.IDcontainer == GetComponent<Residuo>().IDcontenedor)
			{
				#if UNITY_EDITOR
				Debug.Log("Correct Container");
				#endif
				Residuo residuo = GetComponent<Residuo>();
				dumped = true;
				soltadoCorrecto = true;
				// Sin GameManager (tutorial de la Intro) la animación de arrojar la hace quien escucha Soltado.
				if (GameManager.HasInstance)
				{
					GameManager.Instance.IniciarArrojarResiduos(currentTargetContainer, residuo);
				}
			}
			else
			{
				Rigidbody body = GetComponent<Rigidbody>();
				body.isKinematic = false;
				#if UNITY_EDITOR
				Debug.Log("Incorrect Container");
				#endif
				if (GameManager.HasInstance)
				{
					AudioSource errorAudio = GameManager.Instance.GetComponent<AudioSource>();
					if (errorAudio != null)
					{
						errorAudio.Play();
					}
				}
				StartCoroutine(TossToward(currentTargetContainer.AimPoint));
				// No celebration: just a small grey puff where it was let go.
				FeedbackFX.PlayWrongPuff(transform.position);
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
		if (Soltado != null)
		{
			Soltado(this, soltadoEn, soltadoCorrecto);
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
		hoverPulse.Width = HoverOutlineWidth;
		DisableAffordanceTint();
		UpdateHoverHighlight();
	}

	private void Update()
	{
		RefreshPointed();
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
		#if UNITY_EDITOR
		Debug.DrawRay(origin, direction * 10f, Color.red);
		#endif
		Container container = Container.PickByHorizontalAim(origin, direction, currentTargetContainer);
		if (container != currentTargetContainer)
		{
			currentTargetContainer = container;
			Container.SetAimTarget(container);
			#if UNITY_EDITOR
			Debug.Log(container != null ? "New Current Container: " + container.descripcion : "No container selected");
			#endif
		}
	}

	// ---------------------------------------------------------------- active residuos, and the target kept after the ray left it

	/// <summary>Residuos alive and enabled right now (read by HandCloseGrab: while there are any, pointing keeps the far ray on).</summary>
	public static int ActiveCount { get; private set; }

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetActiveCount()
	{
		ActiveCount = 0;
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		ActiveCount++;
	}

	// Per hand (0 left, 1 right): until when this object still counts as pointed at (see HandCloseGrab.TargetHold).
	private readonly float[] pointedUntil = new float[2];
	private bool pointedByHold;

	private bool HoldRunning
	{
		get
		{
			float now = Time.unscaledTime;
			return now < pointedUntil[0] || now < pointedUntil[1];
		}
	}

	/// <summary>The hand keeps this object as its target until that time (Time.unscaledTime).</summary>
	public void KeepPointed(bool left, float until)
	{
		pointedUntil[left ? 0 : 1] = until;
		if (!pointedByHold)
		{
			pointedByHold = true;
			UpdateHoverHighlight();
		}
	}

	/// <summary>The hand no longer keeps this object: it took another one, or the time is over.</summary>
	public void ReleasePointed(bool left)
	{
		pointedUntil[left ? 0 : 1] = 0f;
		RefreshPointed();
	}

	private void RefreshPointed()
	{
		if (pointedByHold && !HoldRunning)
		{
			pointedByHold = false;
			UpdateHoverHighlight();
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
		ActiveCount--;
		pointedUntil[0] = pointedUntil[1] = 0f;
		pointedByHold = false;
		SetHoverHighlighted(false);
		if (hoverShake != null)
		{
			hoverShake.End();
		}
		UpdateSparkles();   // (no longer active and enabled: stops)
		base.OnDisable();
	}

	private void UpdateHoverHighlight()
	{
		SetHoverHighlighted((isHovered || HoldRunning) && !isSelected && !dumped && isActiveAndEnabled);
		UpdateHoverShake();
		UpdateSparkles();
	}

	// ---------------------------------------------------------------- "telekinesis" shake of the pointed object
	// While hovered, the object looks as if it were trying to move, but stays where it is. Only what is SEEN moves: the
	// object's own transform, Rigidbody and colliders are never touched, so hover rays cannot flicker, physics and the
	// grab do not notice, and when it stops nothing has to be put back but the renderer's switch.

	// Displacement (m): this fraction of the object's largest size, kept within min .. max. On Quest 3 one pixel at 3 m is
	// about 2 mm, so the minimum moves a small object about one pixel each way.
	public const float HoverShakeSizeFactor = 0.015f;
	public const float HoverShakeMinAmplitude = 0.0025f;
	public const float HoverShakeMaxAmplitude = 0.0045f;
	// Tilt around its centre (degrees).
	public const float HoverShakeTilt = 1.5f;
	// Frequencies per axis (Hz): slow and subtle, and unrelated, so it does not look mechanical.
	public static readonly Vector3 HoverShakeHz = new Vector3(6.5f, 8.5f, 5.5f);
	// Slow swell of the strength (Hz), and how deep (0 = none, 1 = down to nothing): it strains, then strains harder.
	public const float HoverShakeSwellHz = 1.15f;
	public const float HoverShakeSwellDepth = 0.35f;
	// Seconds to reach full strength. It stops at once.
	public const float HoverShakeRamp = 0.1f;

	private HoverShake hoverShake;

	private void UpdateHoverShake()
	{
		if (hoverRenderers == null)
		{
			return;   // (Start has not run yet)
		}
		if (!hoverHighlighted)
		{
			if (hoverShake != null)
			{
				hoverShake.End();
			}
			return;
		}
		if (hoverShake == null)
		{
			hoverShake = new HoverShake(hoverRenderers);
		}
		hoverShake.Begin();
	}

	/// <summary>
	/// Shows some MeshRenderers slightly displaced, without moving them. Each one gets (the first time) an inactive child
	/// with a copy of its mesh and no collider; while active, the original is told not to draw and the copy is drawn at the
	/// shaken pose. Static-batched renderers are left alone (they cannot be redrawn from their own mesh). A plain class,
	/// also used directly by the screenshot harness. Apply does not allocate.
	/// </summary>
	public sealed class HoverShake
	{
		private readonly IList<MeshRenderer> renderers;
		private readonly MeshRenderer[] copies;
		private readonly bool[] shown;
		private float strength;
		private float size;

		/// <summary>True between Begin and End.</summary>
		public bool Active { get; private set; }
		/// <summary>World-space displacement currently shown (identity when not active).</summary>
		public Matrix4x4 WorldOffset { get; private set; }
		/// <summary>How far the object's centre is shown from its real one right now (m).</summary>
		public float CurrentDistance { get; private set; }

		public HoverShake(IList<MeshRenderer> renderers)
		{
			this.renderers = renderers;
			copies = new MeshRenderer[renderers.Count];
			shown = new bool[renderers.Count];
			WorldOffset = Matrix4x4.identity;
		}

		public void Begin()
		{
			if (Active)
			{
				return;
			}
			Active = true;
			strength = 0f;
			size = 0f;
			for (int i = 0; i < renderers.Count; i++)
			{
				MeshRenderer original = renderers[i];
				shown[i] = false;
				if (original == null || !original.enabled || !original.gameObject.activeInHierarchy || original.isPartOfStaticBatch)
				{
					continue;
				}
				MeshFilter filter = original.GetComponent<MeshFilter>();
				if (filter == null || filter.sharedMesh == null)
				{
					continue;
				}
				Vector3 extent = original.bounds.size;
				size = Mathf.Max(size, Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)));
				MeshRenderer copy = copies[i];
				if (copy == null)
				{
					GameObject go = new GameObject("Hover Shake Visual");
					go.SetActive(false);
					go.transform.SetParent(original.transform, false);
					go.AddComponent<MeshFilter>();
					copy = go.AddComponent<MeshRenderer>();
					copies[i] = copy;
				}
				copy.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
				copy.sharedMaterials = original.sharedMaterials;   // (the pulsing ones, when highlighted)
				copy.shadowCastingMode = original.shadowCastingMode;
				copy.receiveShadows = original.receiveShadows;
				copy.lightProbeUsage = original.lightProbeUsage;
				copy.reflectionProbeUsage = original.reflectionProbeUsage;
				copy.renderingLayerMask = original.renderingLayerMask;
				copy.gameObject.layer = original.gameObject.layer;
				copy.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
				copy.gameObject.SetActive(true);
				original.forceRenderingOff = true;
				shown[i] = true;
			}
		}

		/// <summary>The shaken pose for this frame.</summary>
		public void Apply(float time, float dt)
		{
			if (!Active)
			{
				return;
			}
			strength = Mathf.MoveTowards(strength, 1f, dt / HoverShakeRamp);
			Vector3 offset;
			Quaternion tilt;
			Delta(time, size, strength, out offset, out tilt);
			CurrentDistance = offset.magnitude;
			bool any = false;
			Bounds bounds = new Bounds();
			for (int i = 0; i < renderers.Count; i++)
			{
				if (!shown[i] || renderers[i] == null)
				{
					continue;
				}
				if (!any)
				{
					bounds = renderers[i].bounds;
					any = true;
				}
				else
				{
					bounds.Encapsulate(renderers[i].bounds);
				}
			}
			if (!any)
			{
				WorldOffset = Matrix4x4.identity;
				return;
			}
			// Tilted around the object's centre, not its pivot (which may be anywhere), then displaced.
			Vector3 center = bounds.center;
			Matrix4x4 world = Matrix4x4.TRS(center + offset, tilt, Vector3.one) * Matrix4x4.Translate(-center);
			WorldOffset = world;
			for (int i = 0; i < renderers.Count; i++)
			{
				if (shown[i] && renderers[i] != null && copies[i] != null)
				{
					Transform source = renderers[i].transform;
					copies[i].transform.SetPositionAndRotation(world.MultiplyPoint3x4(source.position), tilt * source.rotation);
				}
			}
		}

		/// <summary>Back to the plain renderer, at once and exactly: nothing but its draw switch was ever changed.</summary>
		public void End()
		{
			if (!Active)
			{
				return;
			}
			Active = false;
			WorldOffset = Matrix4x4.identity;
			CurrentDistance = 0f;
			for (int i = 0; i < renderers.Count; i++)
			{
				if (!shown[i])
				{
					continue;
				}
				shown[i] = false;
				if (renderers[i] != null)
				{
					renderers[i].forceRenderingOff = false;
				}
				if (copies[i] != null)
				{
					copies[i].gameObject.SetActive(false);
				}
			}
		}

		/// <summary>The displacement and tilt at a given time, for an object of a given size and strength (0 .. 1).</summary>
		public static void Delta(float time, float size, float strength, out Vector3 offset, out Quaternion tilt)
		{
			const float tau = 6.2831853f;
			float swell = 1f - HoverShakeSwellDepth * (0.5f + 0.5f * Mathf.Sin(tau * HoverShakeSwellHz * time));
			float x = Mathf.Sin(tau * HoverShakeHz.x * time);
			float y = Mathf.Sin(tau * HoverShakeHz.y * time + 1.3f);
			float z = Mathf.Sin(tau * HoverShakeHz.z * time + 2.1f);
			float amplitude = Mathf.Clamp(size * HoverShakeSizeFactor, HoverShakeMinAmplitude, HoverShakeMaxAmplitude) * strength * swell;
			offset = new Vector3(x, y, z) * amplitude;
			float degrees = HoverShakeTilt * strength * swell;
			tilt = Quaternion.Euler(y * degrees, z * degrees, x * degrees);
		}
	}

	// ---------------------------------------------------------------- sparkles rising from the pointed / held object (FeedbackFX)

	private ParticleSystem sparkles;
	private bool sparkling;

	private void UpdateSparkles()
	{
		bool value = (isHovered || HoldRunning || isSelected) && !dumped && isActiveAndEnabled && hoverRenderers != null;
		if (value && sparkles == null)
		{
			sparkles = FeedbackFX.CreateSparkles(name);
		}
		if (sparkles == null)
		{
			return;
		}
		if (value)
		{
			// Fewer while held: it is then right in front of the face.
			FeedbackFX.SetSparkleRate(sparkles, isSelected ? FeedbackFX.SparkleRateHeld : FeedbackFX.SparkleRateHovered);
			FeedbackFX.FollowSparkles(sparkles, hoverRenderers);
		}
		if (value == sparkling)
		{
			return;
		}
		sparkling = value;
		if (value)
		{
			sparkles.Play();
		}
		else
		{
			sparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);   // the ones already out finish rising
		}
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
		if (hoverShake != null && hoverShake.Active)
		{
			hoverShake.Apply(Time.time, Time.deltaTime);
		}
		if (hoverHighlighted)
		{
			UpdateHoverPulse();
		}
		if (sparkling)
		{
			FeedbackFX.FollowSparkles(sparkles, hoverRenderers);
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
		if (hoverShake != null && hoverShake.Active)
		{
			hoverPulse.DrawOutline(hoverRenderers, gameObject.layer, hoverShake.WorldOffset);
		}
		else
		{
			hoverPulse.DrawOutline(hoverRenderers, gameObject.layer);
		}
	}

	protected override void OnDestroy()
	{
		SetHoverHighlighted(false);
		foreach (HighlightPulse.PulsingMaterial copy in pulseCopies.Values)
		{
			copy.Destroy();
		}
		pulseCopies.Clear();
		if (sparkles != null)
		{
			Destroy(sparkles.gameObject);
			sparkles = null;
		}
		if (hoverPulse != null)
		{
			hoverPulse.Dispose();
			hoverPulse = null;
		}
		base.OnDestroy();
	}
}

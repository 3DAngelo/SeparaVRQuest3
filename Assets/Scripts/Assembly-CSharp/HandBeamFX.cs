using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>
/// Hand "tractor beam": a soft, slightly curved stream of light from the hand to the residuo being chosen, with little
/// lights flowing from the tip toward the hand. Installed by itself on every Near-Far Interactor of every loaded scene
/// (no scene / prefab changes), like HandCloseGrab. The visuals are made and placed by FeedbackFX (CreateBeam /
/// PlaceBeam); this decides when and where.
///
/// When it shows (one state per frame):
///  - Choosing: nothing held. Shown only while <see cref="AimsAtSidewalk"/> says the hand points down at the sidewalk
///    (or straight at a residuo); its end is the pointed residuo or the ground point, smoothed so it lags a little
///    behind the aim (the line then bends lazily, like the stock XRI line).
///  - Pulling: a residuo was just grabbed and flies to the hand (CustomGrabInteractable.IsFlyingToHand). The tip stays
///    exactly on the flying residuo, so the beam shortens with it; when it arrives, a quick fade.
///  - Off: anything else: holding a residuo and choosing its container, and after letting it go while it flies to a
///    container / is tossed / falls (until it is gone or at rest). Then it is back to Choosing, for the next residuo.
/// Shown / hidden with a short fade (FeedbackFX.BeamFadeSpeed), never a pop.
/// With FeedbackFX.BeamReplacesStockLine the stock XRI line (CurveVisualController) is switched off while this exists,
/// and switched back on if the beam cannot be made or this component goes away.
/// </summary>
[DisallowMultipleComponent]
public class HandBeamFX : MonoBehaviour
{
	// "Pointing at the sidewalk" (AimsAtSidewalk): the aim must point at least this far below the horizon (degrees)...
	public const float MinDownPitch = 8f;
	// ...and meet the ground within FeedbackFX.BeamMaxLength. If the XRI ray hits something before the ground that is
	// higher than this above the ground (container, wall, bench, building), it is not the sidewalk: no beam (m).
	public const float GroundBand = 0.35f;
	// The ground height (groundY) is the XR Origin's floor, but the sidewalk surface can sit higher (in the game scene it is
	// about 0.28 m above it). SurfaceY casts one physics ray along the aim and, if it meets a floor-like surface (normal
	// at least this upright) within GroundBand of the XR Origin floor, uses that height instead, so the beam ends ON the
	// sidewalk, not under it.
	public const float FloorNormalY = 0.7f;
	// How quickly the beam's end follows where the hand points (1/s): lower = lazier, more bend while the hand moves.
	public const float EndSharpness = 9f;
	// After letting a residuo go, the beam stays off at least this long (s), then while the residuo is still moving faster
	// than ReleaseRestSpeed (m/s) or flying into its container, but never more than ReleaseMaxQuiet (s).
	public const float ReleaseQuiet = 0.5f;
	public const float ReleaseRestSpeed = 0.3f;
	public const float ReleaseMaxQuiet = 4f;

	private enum Stage
	{
		Off,
		Choosing,
		Pulling
	}

	private NearFarInteractor nearFar;
	private ICurveInteractionDataProvider curve;
	private XROrigin xrOrigin;
	private ParticleSystem beam;
	private LineRenderer core;
	private CurveVisualController[] stockLines;
	private bool emitting;
	private float visibility;
	private bool hasEnd;
	private Vector3 end;
	private Stage stage;
	private bool quickFade;   // the residuo arrived at the hand: fade out quicker
	// The residuo being pulled / held, its centre in its own space (taken at the grab), and the one last let go.
	private CustomGrabInteractable held;
	private Vector3 heldLocalCenter;
	private CustomGrabInteractable released;
	private float releaseTime;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Bootstrap()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
		Install();
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		Install();
	}

	private static void Install()
	{
		NearFarInteractor[] all = Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		foreach (NearFarInteractor interactor in all)
		{
			if (interactor.GetComponent<HandBeamFX>() == null)
			{
				interactor.gameObject.AddComponent<HandBeamFX>();
			}
		}
	}

	/// <summary>
	/// The "pointing at the sidewalk" rule. True (with the beam's end point) when the hand points straight at a residuo,
	/// or when the aim points at least MinDownPitch below the horizon and meets the ground (height groundY) within
	/// FeedbackFX.BeamMaxLength, and the XRI ray does not hit something tall (more than GroundBand above the ground)
	/// before reaching it. Pointing at the horizon or above (buildings, sky, containers) is always false.
	/// </summary>
	public static bool AimsAtSidewalk(Vector3 origin, Vector3 direction, float groundY, bool castHit, Vector3 hitPoint, bool pointsAtResiduo, out Vector3 endPoint)
	{
		endPoint = origin;
		if (pointsAtResiduo && castHit && Vector3.Distance(origin, hitPoint) <= FeedbackFX.BeamMaxLength)
		{
			endPoint = hitPoint;
			return true;
		}
		direction.Normalize();
		if (direction.y > -Mathf.Sin(MinDownPitch * Mathf.Deg2Rad))
		{
			return false;   // level or up
		}
		float toGround = (groundY - origin.y) / direction.y;
		if (toGround <= 0f || toGround > FeedbackFX.BeamMaxLength)
		{
			return false;   // the hand is below the ground (tracking glitch) or the ground point is too far away
		}
		endPoint = origin + direction * toGround;
		if (castHit)
		{
			float hitDistance = Vector3.Distance(origin, hitPoint);
			if (hitDistance < toGround - 0.05f)
			{
				if (hitPoint.y > groundY + GroundBand)
				{
					return false;   // something tall in the way (container, wall, bench...)
				}
				endPoint = hitPoint;   // something low on the sidewalk (a residuo, a curb): end there
			}
		}
		return true;
	}

	/// <summary>
	/// The height of the walkable surface where the aim meets it: the point of one physics ray along the aim (triggers
	/// ignored, up to FeedbackFX.BeamMaxLength) if it hits a floor-like surface (normal.y >= FloorNormalY) within GroundBand
	/// of <paramref name="groundY"/> that is not a residuo; otherwise <paramref name="groundY"/>. No allocations.
	/// </summary>
	public static float SurfaceY(Vector3 origin, Vector3 direction, float groundY)
	{
		RaycastHit hit;
		if (Physics.Raycast(origin, direction, out hit, FeedbackFX.BeamMaxLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
			&& hit.normal.y >= FloorNormalY
			&& Mathf.Abs(hit.point.y - groundY) <= GroundBand
			&& hit.collider.GetComponentInParent<CustomGrabInteractable>() == null)
		{
			return hit.point.y;
		}
		return groundY;
	}

	/// <summary>Centre of a residuo in its own local space (its colliders, else its renderers, else its pivot).</summary>
	public static Vector3 LocalCenter(Component residuo)
	{
		Bounds bounds = new Bounds();
		bool any = false;
		foreach (Collider c in residuo.GetComponentsInChildren<Collider>())
		{
			if (c.enabled && !c.isTrigger)
			{
				if (any)
				{
					bounds.Encapsulate(c.bounds);
				}
				else
				{
					bounds = c.bounds;
					any = true;
				}
			}
		}
		if (!any)
		{
			foreach (Renderer r in residuo.GetComponentsInChildren<Renderer>())
			{
				if (any)
				{
					bounds.Encapsulate(r.bounds);
				}
				else
				{
					bounds = r.bounds;
					any = true;
				}
			}
		}
		return any ? residuo.transform.InverseTransformPoint(bounds.center) : Vector3.zero;
	}

	private void Awake()
	{
		nearFar = GetComponent<NearFarInteractor>();
		curve = nearFar;
		xrOrigin = GetComponentInParent<XROrigin>(true);
		// Made up front (not on first use), so the stock line can be replaced from the start; hidden until needed.
		beam = FeedbackFX.CreateBeam(transform);
		if (beam == null)
		{
			enabled = false;   // no material on this platform: no beam at all, the stock line stays as it is
			return;
		}
		core = FeedbackFX.BeamCore(beam);
		if (FeedbackFX.BeamReplacesStockLine)
		{
			stockLines = GetComponentsInChildren<CurveVisualController>(true);
			SetStockLines(false);
		}
	}

	private void OnEnable()
	{
		if (beam != null)
		{
			SetStockLines(false);
		}
	}

	private void OnDisable()
	{
		SetEmitting(false);
		visibility = 0f;
		hasEnd = false;
		if (beam != null)
		{
			beam.Clear(true);
			if (core != null)
			{
				core.enabled = false;
			}
		}
		SetStockLines(true);
	}

	private void OnDestroy()
	{
		SetStockLines(true);
		if (beam != null)
		{
			Destroy(beam.gameObject);
		}
	}

	/// <summary>Switches the stock XRI ray line(s) of this interactor off (replaced by the beam) or back on.</summary>
	private void SetStockLines(bool on)
	{
		if (stockLines == null)
		{
			return;
		}
		foreach (CurveVisualController stock in stockLines)
		{
			if (stock == null)
			{
				continue;
			}
			stock.enabled = on;   // when on again, it enables / disables its line itself every LateUpdate
			if (!on && stock.lineRenderer != null)
			{
				stock.lineRenderer.enabled = false;
			}
		}
	}

	private void LateUpdate()
	{
		if (nearFar == null || beam == null)
		{
			return;
		}
		Transform castOrigin = curve.curveOrigin != null ? curve.curveOrigin : transform;
		Vector3 origin = castOrigin.position;
		Vector3 aim = castOrigin.forward;
		float dt = Time.deltaTime;

		TrackSelection();
		Vector3 target;
		bool show;
		float fadeSpeed = FeedbackFX.BeamFadeSpeed;
		bool snapEnd = false;
		if (held != null && held.IsFlyingToHand)
		{
			// Pulling: the tip exactly on the flying residuo.
			stage = Stage.Pulling;
			target = held.transform.TransformPoint(heldLocalCenter);
			show = true;
			snapEnd = true;
		}
		else if (held != null || nearFar.hasSelection || ReleasedStillBusy())
		{
			// Holding (choosing a container), or the residuo just let go is still on its way: off.
			if (stage == Stage.Pulling)
			{
				quickFade = true;   // arrived at the hand
			}
			stage = Stage.Off;
			target = end;
			show = false;
		}
		else
		{
			stage = Stage.Choosing;
			EndPointType endType = curve.TryGetCurveEndPoint(out Vector3 hitPoint);
			bool castHit = endType == EndPointType.ValidCastHit || endType == EndPointType.EmptyCastHit || endType == EndPointType.UI;
			float groundY = xrOrigin != null && xrOrigin.Origin != null ? xrOrigin.Origin.transform.position.y : 0f;
			groundY = SurfaceY(origin, aim, groundY);   // the real sidewalk surface, if the aim meets it
			show = AimsAtSidewalk(origin, aim, groundY, castHit, hitPoint, endType == EndPointType.ValidCastHit && HoversResiduo(), out target);
			if (!show)
			{
				target = end;   // fade out where it was
			}
		}

		if (!hasEnd || visibility <= 0f || snapEnd)
		{
			end = target;
			hasEnd = true;
		}
		else
		{
			end = Vector3.Lerp(end, target, 1f - Mathf.Exp(-EndSharpness * dt));   // lazy end: lags behind the aim
		}
		if (show)
		{
			quickFade = false;
		}
		else if (quickFade)
		{
			fadeSpeed = FeedbackFX.BeamArriveFadeSpeed;
		}
		visibility = Mathf.MoveTowards(visibility, show ? 1f : 0f, fadeSpeed * dt);
		SetEmitting(show);
		if (visibility <= 0f)
		{
			if (beam.particleCount > 0)
			{
				beam.Clear(true);   // fully faded: drop what is left, so it does not pop back in next time
			}
			if (core != null && core.enabled)
			{
				core.enabled = false;
			}
			return;
		}
		FeedbackFX.PlaceBeam(beam, core, origin, aim, end, visibility);
	}

	/// <summary>Follows grabs and releases of residuos by this interactor.</summary>
	private void TrackSelection()
	{
		CustomGrabInteractable selected = nearFar.hasSelection ? nearFar.firstInteractableSelected as CustomGrabInteractable : null;
		if (selected == held)
		{
			return;
		}
		if (held != null)
		{
			released = held;
			releaseTime = Time.time;
		}
		held = selected;
		if (held != null)
		{
			heldLocalCenter = LocalCenter(held);   // once per grab
		}
	}

	/// <summary>True while the residuo last let go is still busy: flying into its container, tossed or falling.</summary>
	private bool ReleasedStillBusy()
	{
		if (released == null)
		{
			return false;   // none, or already destroyed (it reached its container)
		}
		float since = Time.time - releaseTime;
		bool busy = since < ReleaseQuiet;
		if (!busy && since < ReleaseMaxQuiet)
		{
			Rigidbody body = released.GetComponent<Rigidbody>();
			busy = released.IsDumped || (body != null && !body.isKinematic && body.linearVelocity.sqrMagnitude > ReleaseRestSpeed * ReleaseRestSpeed);
		}
		if (!busy)
		{
			released = null;
		}
		return busy;
	}

	/// <summary>True when this interactor is hovering a residuo that can still be grabbed.</summary>
	private bool HoversResiduo()
	{
		var hovered = nearFar.interactablesHovered;
		for (int i = 0; i < hovered.Count; i++)
		{
			if (hovered[i] is CustomGrabInteractable residuo && !residuo.IsDumped)
			{
				return true;
			}
		}
		return false;
	}

	private void SetEmitting(bool value)
	{
		if (value == emitting)
		{
			return;
		}
		emitting = value;
		if (value)
		{
			beam.Play();
		}
		else
		{
			// The lights already on their way keep flowing to the hand while the beam fades.
			beam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
		}
	}
}

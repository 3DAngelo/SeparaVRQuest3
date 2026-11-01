using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>
/// Hand "tractor beam": a curved stream of little lights flowing from the tip toward the hand. Installed by itself on
/// every Near-Far Interactor of every loaded scene (no scene / prefab changes), like HandCloseGrab. The visuals are made
/// and placed by FeedbackFX (CreateBeam / PlaceBeam / BeamMotion); this decides when and where.
///
/// Older players have to be able to trust it, so it answers at once and its tip is the truth: there is no fade in and
/// no smoothing of the end point (only the belly of the curve lags a little, see FeedbackFX.BeamBellySharpness).
///
/// When it shows (one state per frame):
///  - Choosing: nothing held. Shown only while <see cref="AimsAtSidewalk"/> says the hand points down at the sidewalk
///    (or at a residuo). Over the sidewalk: thin and crisp, its tip on the ground point, whole on its first frame.
///    On a hovered residuo: its tip on the residuo's centre at once, growing into the vortex in
///    FeedbackFX.BeamTargetBlendIn (back in FeedbackFX.BeamTargetBlendOut when the target is lost).
///  - Pulling: a residuo was just grabbed and flies to the hand (CustomGrabInteractable.IsFlyingToHand). The tip stays
///    exactly on the flying residuo, so the beam shortens with it; when it arrives, a quick fade.
///  - Off: anything else: holding a residuo and choosing its container, and after letting it go while it flies to a
///    container / is tossed / falls (until it is gone or at rest). Then it is back to Choosing, for the next residuo.
/// Hidden with a short fade out (FeedbackFX.BeamFadeSpeed).
/// In the win scene there is no beam and no stock line; each hand emits sparkles instead, for the players to play and celebrate.
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
	// After letting a residuo go, the beam stays off at least this long (s), then while the residuo is still moving faster
	// than ReleaseRestSpeed (m/s) or flying into its container, but never more than ReleaseMaxQuiet (s).
	public const float ReleaseQuiet = 0.5f;
	public const float ReleaseRestSpeed = 0.3f;
	public const float ReleaseMaxQuiet = 4f;
	// Name of the win scene: there, no beam and no stock line; the hands emit sparkles instead.
	public const string WinSceneName = "Win";

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
	private CurveVisualController[] stockLines;
	private bool playing;
	private bool winScene;
	private ParticleSystem winSparkles;
	private readonly FeedbackFX.BeamMotion motion = new FeedbackFX.BeamMotion();
	private Vector3 end;
	private Stage stage;
	private bool quickFade;   // the residuo arrived at the hand: fade out quicker
	// The residuo being pulled / held, its centre in its own space (taken at the grab), and the one last let go.
	private CustomGrabInteractable held;
	private Vector3 heldLocalCenter;
	private CustomGrabInteractable released;
	private float releaseTime;
	// The residuo hovered last, and its centre in its own space (taken when the hover begins).
	private CustomGrabInteractable hovered;
	private Vector3 hoveredLocalCenter;

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
		if (gameObject.scene.name == WinSceneName)
		{
			winScene = true;
			winSparkles = FeedbackFX.CreateHandSparkles(name);
			stockLines = GetComponentsInChildren<CurveVisualController>(true);
			SetStockLines(false);
			return;
		}
		// Made up front (not on first use), so the stock line can be replaced from the start; hidden until needed.
		beam = FeedbackFX.CreateBeam(transform);
		if (beam == null)
		{
			enabled = false;   // no material on this platform: no beam at all, the stock line stays as it is
			return;
		}
		if (FeedbackFX.BeamReplacesStockLine)
		{
			stockLines = GetComponentsInChildren<CurveVisualController>(true);
			SetStockLines(false);
		}
	}

	private void OnEnable()
	{
		if (beam != null || winScene)
		{
			SetStockLines(false);
		}
	}

	private void OnDisable()
	{
		if (winSparkles != null)
		{
			winSparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
		}
		SetPlaying(false);
		motion.Reset();
		hovered = null;
		SetStockLines(true);
	}

	private void OnDestroy()
	{
		SetStockLines(true);
		if (beam != null)
		{
			Destroy(beam.gameObject);
		}
		if (winSparkles != null)
		{
			Destroy(winSparkles.gameObject);
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
		if (winScene)
		{
			UpdateWinSparkles();
			return;
		}
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
		bool pulling = false;
		bool hasTarget = false;
		if (held != null && held.IsFlyingToHand)
		{
			// Pulling: the tip exactly on the flying residuo.
			stage = Stage.Pulling;
			target = held.transform.TransformPoint(heldLocalCenter);
			show = true;
			pulling = true;
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
			// A hovered residuo (by the far ray or by the near caster, which reports no ray hit): the tip goes on its centre,
			// steady whatever part of it the ray touches, and the same point the pull starts from.
			CustomGrabInteractable residuo = HoveredResiduo();
			if (residuo != hovered)
			{
				hovered = residuo;
				hoveredLocalCenter = residuo != null ? LocalCenter(residuo) : Vector3.zero;   // once per hover
			}
			if (residuo != null)
			{
				hitPoint = residuo.transform.TransformPoint(hoveredLocalCenter);
				castHit = true;
			}
			show = AimsAtSidewalk(origin, aim, groundY, castHit, hitPoint, residuo != null, out target);
			hasTarget = show && residuo != null && (target - hitPoint).sqrMagnitude < 1e-6f;
			if (!show)
			{
				target = end;   // fade out where it was
			}
		}

		end = target;   // no lag: the tip says what is really being pointed at
		if (show)
		{
			quickFade = false;
		}
		else if (quickFade)
		{
			fadeSpeed = FeedbackFX.BeamArriveFadeSpeed;
		}
		motion.Step(dt, show, hasTarget || pulling, pulling, fadeSpeed);
		if (motion.Visibility <= 0f)
		{
			SetPlaying(false);
			return;
		}
		SetPlaying(true);
		FeedbackFX.PlaceBeam(beam, motion, origin, aim, end, dt);
	}

	/// <summary>Win scene: the sparkles follow the palm while the hand is tracked (a controller: its own position).</summary>
	private void UpdateWinSparkles()
	{
		if (winSparkles == null)
		{
			return;
		}
		Vector3 point = transform.position;
		bool tracked = true;
		HandCloseGrab hand = GetComponent<HandCloseGrab>();
		if (hand != null)
		{
			Vector3 wrist, index, middle, little;
			tracked = hand.TryGetPalmJoints(out wrist, out index, out middle, out little);
			point = (wrist + index + middle + little) * 0.25f;
		}
		if (!tracked)
		{
			if (winSparkles.isEmitting)
			{
				winSparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);   // the ones in the air finish
			}
			return;
		}
		winSparkles.transform.position = point;
		if (!winSparkles.isEmitting)
		{
			winSparkles.Play();
		}
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

	/// <summary>The residuo this interactor is hovering that can still be grabbed, or the one its hand still keeps as the target (HandCloseGrab.TargetHold), or null.</summary>
	private CustomGrabInteractable HoveredResiduo()
	{
		var hoveredNow = nearFar.interactablesHovered;
		for (int i = 0; i < hoveredNow.Count; i++)
		{
			if (hoveredNow[i] is CustomGrabInteractable residuo && !residuo.IsDumped)
			{
				return residuo;
			}
		}
		HandCloseGrab hand = nearFar.GetComponent<HandCloseGrab>();
		return hand != null ? hand.PointedResiduo : null;
	}

	/// <summary>Playing while the beam is visible; stopped and empty when hidden.</summary>
	private void SetPlaying(bool value)
	{
		if (beam == null || value == playing)
		{
			return;
		}
		playing = value;
		if (value)
		{
			beam.Play();
		}
		else
		{
			beam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}
	}
}

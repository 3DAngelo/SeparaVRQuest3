using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>
/// Hand beam: a soft stream of light (a thin glowing core with little lights flowing along it) from the hand (or
/// controller) along the interactor's own ray. Installed by itself on every Near-Far Interactor of every loaded scene (no
/// scene / prefab changes), like HandCloseGrab.
///
/// Shown exactly when the XRI ray line is shown (ICurveInteractionDataProvider.isActive: far casting, or a far
/// selection), and also while an object is held, then along the aim used to choose the container (HandCloseGrab.GetAim).
/// It starts at the ray origin XRI casts from (curveOrigin, the Aim Pose for hands) and goes to the ray end point (stops
/// at what is pointed at), or BeamMaxLength when pointing at nothing.
/// With FeedbackFX.BeamReplacesStockLine the stock XRI line (CurveVisualController) is switched off while this exists,
/// so the beam is the ray line; it is switched back on if the beam cannot be made or this component goes away.
/// The visuals themselves are made by FeedbackFX.CreateBeam; this only places them and shows / hides them.
/// </summary>
[DisallowMultipleComponent]
public class HandBeamFX : MonoBehaviour
{
	// How quickly the beam length follows the ray end point (1/s; higher = snappier).
	public const float LengthSharpness = 14f;

	private NearFarInteractor nearFar;
	private ICurveInteractionDataProvider curve;
	private ParticleSystem beam;
	private LineRenderer core;
	private CurveVisualController[] stockLines;
	private bool shown;
	private float length = FeedbackFX.BeamMaxLength;
	private float coreVisibility;

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

	private void Awake()
	{
		nearFar = GetComponent<NearFarInteractor>();
		curve = nearFar;
		// Made up front (not on first use), so the stock line can be replaced from the start; inactive until shown.
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
		SetShown(false);
		coreVisibility = 0f;
		FeedbackFX.SetBeamCore(core, length, 0f);
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
		bool holding = nearFar.hasSelection;
		bool show = holding || curve.isActive;

		Vector3 origin;
		Vector3 direction;
		float target;
		if (holding)
		{
			Transform held = nearFar.firstInteractableSelected != null ? nearFar.firstInteractableSelected.transform : null;
			HandCloseGrab.GetAim(nearFar, held, out origin, out direction);
			target = FeedbackFX.BeamHeldLength;
		}
		else
		{
			// The very origin and end point the XRI line uses (CurveVisualController reads the same provider).
			Transform castOrigin = curve.curveOrigin != null ? curve.curveOrigin : transform;
			origin = castOrigin.position;
			direction = castOrigin.forward;
			target = FeedbackFX.BeamMaxLength;
			EndPointType endType = curve.TryGetCurveEndPoint(out Vector3 end);
			if (endType == EndPointType.ValidCastHit || endType == EndPointType.UI || endType == EndPointType.EmptyCastHit)
			{
				Vector3 toEnd = end - origin;
				float distance = toEnd.magnitude;
				if (distance > 0.001f)
				{
					// Aim the stream at the end point (the line may bend a little), and stop it there.
					direction = toEnd / distance;
					target = Mathf.Min(distance, FeedbackFX.BeamMaxLength);
				}
			}
		}
		target = Mathf.Max(FeedbackFX.BeamMinLength, target);
		length = shown ? Mathf.Lerp(length, target, 1f - Mathf.Exp(-LengthSharpness * Time.deltaTime)) : target;
		coreVisibility = Mathf.MoveTowards(coreVisibility, show ? 1f : 0f, FeedbackFX.BeamCoreFadeSpeed * Time.deltaTime);

		FeedbackFX.PlaceBeam(beam, core, origin, direction, length, coreVisibility);
		SetShown(show);
	}

	private void SetShown(bool value)
	{
		if (value == shown || beam == null)
		{
			shown = value && beam != null;
			return;
		}
		shown = value;
		if (value)
		{
			beam.Play();
		}
		else
		{
			// The particles already in flight finish their way (a soft fade instead of a pop).
			beam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
		}
	}
}

using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

public class GameStart : MonoBehaviour
{
	// Build index of the game scene (NuevoJuego).
	public const int GameSceneBuildIndex = 1;
	// Time the start button takes to travel the rest of the way down once pressed, in unscaled seconds.
	public const float ButtonPressSeconds = 0.08f;

	// No longer used: the delay before the load is now ScreenFader's fade out. Kept so the serialized scene value stays valid.
	public float waitingTime = 2f;

	public GameObject mainCamera;

	private VRScreenFade screenFade;

	private bool started;

	private void Start()
	{
		screenFade = mainCamera.GetComponent<VRScreenFade>();
		screenFade.BeginFadeIn();
	}

	public void StartGame()
	{
		// Called by the button's SelectEntered: only the first press counts.
		if (started)
		{
			return;
		}
		started = true;
		HoldButtonDown();
		ScreenFader.Instance.FadeOutAndLoad(GameSceneBuildIndex, ScreenFader.DefaultFadeOutSeconds, ScreenFader.DefaultFadeInSeconds);
	}

	public void LoadScene()
	{
		#if UNITY_EDITOR
		Debug.Log("Loading Scene 1");
		#endif
		started = true;
		// ScreenFader ignores the call if a load is already in progress.
		ScreenFader.Instance.FadeOutAndLoad(GameSceneBuildIndex, ScreenFader.DefaultFadeOutSeconds, ScreenFader.DefaultFadeInSeconds);
	}

	// Leaves the pressed button down until the scene changes and makes it unpressable.
	// XRPokeFollowAffordance would bring it back up as soon as the finger leaves (returnToInitialPosition), so it is
	// disabled: its LateUpdate is the only thing that moves the follow transform (its poke callbacks only change the tween
	// target), and then the transform is moved to the fully pressed position by hand.
	private void HoldButtonDown()
	{
		XRPokeFollowAffordance affordance = FindPressedButton();
		if (affordance == null || affordance.pokeFollowTransform == null)
		{
			#if UNITY_EDITOR
			Debug.LogWarning("GameStart: start button (XRPokeFollowAffordance) not found; it will not stay pressed.");
			#endif
			return;
		}

		XRPokeFilter filter = affordance.GetComponentInParent<XRPokeFilter>();
		XRBaseInteractable interactable = (filter != null && filter.pokeInteractable != null)
			? filter.pokeInteractable
			: affordance.GetComponentInParent<XRBaseInteractable>();

		Vector3 pressedPosition = PressedLocalPosition(affordance, filter);
		affordance.enabled = false;
		StartCoroutine(PressAndHold(affordance.pokeFollowTransform, pressedPosition, interactable));
	}

	// The button whose interactable calls this StartGame from SelectEntered; failing that, the selected one; failing that,
	// the one nearest to the camera. Only buttons in this scene (the XR rig could bring others, e.g. hand menus).
	private XRPokeFollowAffordance FindPressedButton()
	{
		XRPokeFollowAffordance[] all = Object.FindObjectsByType<XRPokeFollowAffordance>(FindObjectsSortMode.None);
		XRPokeFollowAffordance selected = null;
		XRPokeFollowAffordance nearest = null;
		float nearestSqr = float.MaxValue;
		Vector3 viewer = (Camera.main != null) ? Camera.main.transform.position : transform.position;
		for (int i = 0; i < all.Length; i++)
		{
			XRPokeFollowAffordance candidate = all[i];
			if (candidate.gameObject.scene != gameObject.scene || candidate.pokeFollowTransform == null)
			{
				continue;
			}
			XRPokeFilter filter = candidate.GetComponentInParent<XRPokeFilter>();
			XRBaseInteractable interactable = (filter != null) ? filter.pokeInteractable : null;
			if (interactable != null)
			{
				if (CallsStartGame(interactable))
				{
					return candidate;
				}
				if (selected == null && interactable.isSelected)
				{
					selected = candidate;
				}
			}
			float sqr = (candidate.transform.position - viewer).sqrMagnitude;
			if (sqr < nearestSqr)
			{
				nearestSqr = sqr;
				nearest = candidate;
			}
		}
		return (selected != null) ? selected : nearest;
	}

	private bool CallsStartGame(XRBaseInteractable interactable)
	{
		SelectEnterEvent selectEntered = interactable.selectEntered;
		int count = selectEntered.GetPersistentEventCount();
		for (int i = 0; i < count; i++)
		{
			if (selectEntered.GetPersistentTarget(i) == this && selectEntered.GetPersistentMethodName(i) == nameof(StartGame))
			{
				return true;
			}
		}
		return false;
	}

	// Local position of the follow transform when the button is fully down.
	// XRPokeFollowAffordance moves it to pokeTarget.InverseTransformPoint(axisAlignedPokeInteractionPoint), and XRPokeLogic
	// puts that point at attachPosition + depth * axisNormal, depth going to 0 when fully pressed. So the bottom is the
	// rest position minus its component along the axis normal (clamped to maxDistance like the affordance does).
	// In the intro: rest (0, 0.045, 0), NegativeY -> normal +Y, maxDistance 0.045 -> pressed (0, 0, 0).
	private static Vector3 PressedLocalPosition(XRPokeFollowAffordance affordance, XRPokeFilter filter)
	{
		Vector3 rest = affordance.initialPosition;
		Vector3 axisNormal = Vector3.up;
		if (filter != null && filter.pokeConfiguration != null && filter.pokeConfiguration.Value != null)
		{
			axisNormal = LocalAxisNormal(filter.pokeConfiguration.Value.pokeDirection);
		}
		if (axisNormal == Vector3.zero)
		{
			return rest;
		}

		float travel = Vector3.Dot(rest, axisNormal);
		if (travel <= 0f)
		{
			// Unexpected setup (rest position not above the poke target): fall back to the configured travel.
			travel = affordance.maxDistance;
		}
		if (affordance.clampToMaxDistance && affordance.maxDistance > 0f)
		{
			travel = Mathf.Min(travel, affordance.maxDistance);
		}
		return rest - axisNormal * travel;
	}

	// Same mapping as XRPokeLogic.ComputeRotatedDepthEvaluationAxis(transform, isWorldSpace: false): the normal points out
	// of the button, opposite to the poke; positive axes are negated, negative ones are not.
	private static Vector3 LocalAxisNormal(PokeAxis pokeDirection)
	{
		switch (pokeDirection)
		{
			case PokeAxis.X:
				return Vector3.left;
			case PokeAxis.Y:
				return Vector3.down;
			case PokeAxis.Z:
				return Vector3.back;
			case PokeAxis.NegativeX:
				return Vector3.right;
			case PokeAxis.NegativeY:
				return Vector3.up;
			case PokeAxis.NegativeZ:
				return Vector3.forward;
			default:
				return Vector3.zero;
		}
	}

	private IEnumerator PressAndHold(Transform follow, Vector3 pressedPosition, XRBaseInteractable interactable)
	{
		Vector3 from = follow.localPosition;
		float t = 0f;
		bool interactableDisabled = false;
		while (t < 1f)
		{
			yield return null;
			// One frame later: StartGame runs inside this interactable's SelectEntered, and disabling it there would
			// unregister it from the interaction manager in the middle of that event.
			if (!interactableDisabled)
			{
				interactableDisabled = true;
				if (interactable != null)
				{
					interactable.enabled = false;
				}
			}
			t += Time.unscaledDeltaTime / ButtonPressSeconds;
			follow.localPosition = Vector3.Lerp(from, pressedPosition, Mathf.Clamp01(t));
		}
		follow.localPosition = pressedPosition;
	}
}

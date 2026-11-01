using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

/// <summary>
/// Hand tracking: lets the hand Near-Far Interactors grab with ANY hand-closing gesture (curling one or more of
/// index / middle / ring / little), in addition to the pinch they already read from the input actions.
///
/// No scene set-up: a bootstrap adds this component, at run time, to every hand Near-Far Interactor of every scene
/// that loads. Controllers are never touched.
///
/// How it plugs in: it installs itself as the <c>bypass</c> of the interactor's select input. The interactor then
/// asks this component "is select held?", and the answer is "pinch (the original input) OR a closing gesture".
///
/// Once grabbed, the object is held until the hand is really open (thumb and index tips apart, and the grabbing
/// fingers open) for a short moment. Turning the palm to the face (the Meta system gesture then suppresses the pinch
/// input) or a hand tracking dropout therefore never drops it.
/// </summary>
[DisallowMultipleComponent]
public class HandCloseGrab : MonoBehaviour, IXRInputButtonReader
{
	// Finger "openness" = distance(wrist, fingertip) / length of the finger chain measured from the wrist.
	// About 1.0 with the finger straight, about 0.4 in a fist. Two thresholds = hysteresis against jitter.
	public const float CloseRatio = 0.70f;      // below this a finger becomes "closed"
	public const float OpenRatio = 0.82f;       // above this a closed finger becomes "open" again
	public const float ReleaseDelay = 0.15f;    // seconds the hand must look open (no pinch, grabbing fingers open) before the object is released
	public const float PinchCloseDist = 0.030f; // thumb tip - index tip distance (m) below which the joints count as "pinched"
	public const float PinchOpenDist = 0.050f;  // thumb tip - index tip distance (m) above which a joint pinch counts as "open" again
	public const float GrabWindow = 0.80f;      // seconds after a closing gesture during which a target may still be taken
	public const float LatchTime = 0.60f;       // seconds the last pointed object stays grabbable after the ray left it: the visible TargetHold plus the time a finger takes to get from "decided to close" to CloseRatio
	public const float GestureMemory = 0.80f;   // fingers closed within this time before a grab count as "the grab gesture" (must not be below GrabWindow: it would cut the window short)
	public const float TargetHold = 0.40f;      // seconds the hovered object stays the target (beam tip, shake, sparkles, outline) after the ray left it; a new object in the ray replaces it at once
	public const float FarConeAngle = 10f;      // far ray cone of the hands (degrees; the prefab has 6, controllers keep it). 12 steals more from neighbours at 2-6 deg of aim error and only wins past 8 deg: CONEPROBE in GrabProbes.cs
	public const float MissCueDelay = 0.25f;    // seconds after a closing gesture with nothing to take before the "nothing there" sound (a target arriving before this stays silent)
	public const float MissCueMinGap = 1.50f;   // seconds between two "nothing there" sounds at least
	public const float TrackingGrace = 0.50f;   // seconds of lost hand tracking before the finger state is forgotten (a held object stays held)
	public const float AimFrozenTime = 0.25f;   // aim pose not moving for this long (hand tracked) = aim pose not being updated

	// Re-arm and release relative to what this hand really does, so a hand with limited mobility can grab again and let go.
	// A normal hand keeps CloseRatio to close and OpenRatio to release.
	public const float RearmDelta = 0.12f;      // a closed finger (not holding) re-arms after opening this much from the most closed value it reached. Above the fist tracking noise, reachable by a stiff hand
	public const float RecloseDelta = 0.08f;    // an open finger closes after dropping this much from the most open value since it opened (never above CloseRatio): after an early re-arm, the next close needs a real movement
	public const float ReleaseMargin = 0.05f;   // holding: the finger counts as open at this much below the most open value this hand has shown (never above OpenRatio)
	public const float ReleaseFloor = 0.06f;    // holding: ... and always at least this much above the most closed value of the grip
	public const float PeakFloor = 0.75f;       // the remembered most open value slowly forgets down to here at most...
	public const float PeakDecay = 0.01f;       // ...at this rate per second with nothing held (a tracking spike can never leave a stiff hand unable to let go for long)
	public const float PeakDecayHolding = 0.003f; // ...and at this rate while holding: a relaxed grip looks like a stiff hand letting go, so it must not drop an object during a slow choice of container
	public const float PeakRise = 0.5f;         // the remembered value rises at most this much per second: a real open hand reaches it, a tracking spike barely moves it

	/// <summary>
	/// Open/closed logic of one finger, from its openness ratio. Pure (no Unity state), so the harness can run it on curves.
	/// </summary>
	public struct FingerGate
	{
		public bool Known;       // a first sample has been taken since the last Forget
		public bool Closed;
		public float Low;        // most closed value since the finger closed
		public float PhaseHigh;  // most open value since the finger opened
		public float Peak;       // most open value this hand has shown lately (kept through tracking dropouts)

		/// <summary>Tracking was lost: the next sample is taken as is (it is not a closing gesture).</summary>
		public void Forget()
		{
			Known = false;
			Closed = false;
		}

		/// <summary>Takes one sample. True on the frame the finger closes.</summary>
		public bool Step(float ratio, bool holding, float dt)
		{
			float decayed = Peak > PeakFloor ? Mathf.Max(PeakFloor, Peak - (holding ? PeakDecayHolding : PeakDecay) * dt) : Peak;
			// dt capped: the first sample after a tracking dropout has a long dt and is the likeliest to be a spike.
			Peak = Peak <= 0f ? ratio : Mathf.Max(decayed, Mathf.Min(ratio, decayed + PeakRise * Mathf.Min(dt, 0.1f)));
			if (!Known)
			{
				Known = true;
				Closed = ratio < CloseRatio;
				Low = ratio;
				PhaseHigh = ratio;
				return false;
			}
			if (Closed)
			{
				Low = Mathf.Min(Low, ratio);
				float reopen = holding
					? Mathf.Min(OpenRatio, Mathf.Max(Peak - ReleaseMargin, Low + ReleaseFloor))
					: Mathf.Min(OpenRatio, Low + RearmDelta);
				if (ratio > reopen)
				{
					Closed = false;
					PhaseHigh = ratio;
				}
				return false;
			}
			PhaseHigh = Mathf.Max(PhaseHigh, ratio);
			if (ratio < Mathf.Min(CloseRatio, PhaseHigh - RecloseDelta))
			{
				Closed = true;
				Low = ratio;
				return true;
			}
			return false;
		}
	}

	/// <summary>
	/// The target kept for a moment after the ray left it. Pure, so the harness can run it. A new target replaces it at once.
	/// </summary>
	public struct PointHold<T> where T : class
	{
		public T Held;
		public float Until;

		public T Step(T current, float now, float holdTime)
		{
			if (current != null)
			{
				Held = current;
				Until = now + holdTime;
			}
			else if (Held != null && now >= Until)
			{
				Held = null;
			}
			return Held;
		}

		public void Clear()
		{
			Held = null;
			Until = 0f;
		}
	}

	private static readonly XRHandJointID[][] Fingers =
	{
		new[] { XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip },
		new[] { XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip },
		new[] { XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip },
		new[] { XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip },
	};

	private static readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
	private static XRHandSubsystem handSubsystem;

	private NearFarInteractor interactor;
	private bool isLeft;

	private readonly bool[] closed = new bool[4];
	private readonly bool[] grabbing = new bool[4];   // fingers that take part in the current grab
	private readonly float[] closeTime = new float[4];
	private readonly FingerGate[] gates = new FingerGate[4];
	private float lastGateTime = -1f;
	private bool jointPinch;                         // thumb-index pinch measured from the joints (not suppressed by system gestures)
	private bool trackedThisFrame;                   // the hand joints were read this frame
	private float lastTrackedTime = -999f;

	private bool hadSelection;
	private float releaseSince = -1f;                // time the held hand started to look open; -1 = it does not
	private float pendingUntil = -999f;
	private XRGrabInteractable lastTarget;
	private float lastTargetTime = -999f;
	private PointHold<CustomGrabInteractable> pointHold;
	private CustomGrabInteractable shownHold;        // the object told it is being pointed at by this hand

	private bool pinchNow;                           // pinch input or joint pinch, last read
	private bool missCueArmed;
	private float missCueAt;
	private float lastMissCue = -999f;

	private int tickFrame = -1;
	private bool inTick;
	private bool selectLatched;                      // a selection is held until a confirmed release
	private bool held;
	private bool performedThisFrame;
	private bool completedThisFrame;
	private float value;

	private Transform aimTransform;
	private Vector3 lastAimPos;
	private Quaternion lastAimRot;
	private float lastAimMoveTime;

	private readonly List<IXRInteractable> targets = new List<IXRInteractable>();

	// ---------------------------------------------------------------- bootstrap (no scene edit needed)

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		handSubsystem = null;
		subsystems.Clear();
		trackingSpace = null;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Bootstrap()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
		InstallOnHandInteractors();
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		InstallOnHandInteractors();
	}

	private static void InstallOnHandInteractors()
	{
		NearFarInteractor[] all = Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		foreach (NearFarInteractor nearFar in all)
		{
			if (IsHandInteractor(nearFar) && nearFar.GetComponent<HandCloseGrab>() == null)
			{
				nearFar.gameObject.AddComponent<HandCloseGrab>();
			}
		}
	}

	// The hand interactors of the XRI hands rig live under "Left Hand" / "Right Hand"; the controller ones under "... Controller".
	private static bool IsHandInteractor(NearFarInteractor nearFar)
	{
		for (Transform t = nearFar.transform.parent; t != null; t = t.parent)
		{
			if (t.name.EndsWith("Controller"))
			{
				return false;
			}
			if (t.name.EndsWith("Hand"))
			{
				return true;
			}
		}
		return false;
	}

	// ---------------------------------------------------------------- life cycle

	private void OnEnable()
	{
		interactor = GetComponent<NearFarInteractor>();
		if (interactor == null)
		{
			enabled = false;
			return;
		}
		if (interactor.handedness == InteractorHandedness.Left)
		{
			isLeft = true;
		}
		else if (interactor.handedness == InteractorHandedness.Right)
		{
			isLeft = false;
		}
		else
		{
			isLeft = HasAncestorNamed("Left");
		}
		ResetState();
		interactor.selectInput.bypass = this;
		HookSystemGesture();
		HookPokeGesture();
		CurveInteractionCaster farCaster = interactor.farInteractionCaster as CurveInteractionCaster;
		if (farCaster != null)
		{
			farCaster.coneCastAngle = FarConeAngle;
		}
		GrabAudio.Prepare();
	}

	// The XRI hands rig's MetaSystemGestureDetector (palm turned to the face) calls SetActive(false) on this Near-Far
	// Interactor, which cancels its selection and drops the held object. Those persistent calls are switched off and
	// replaced by ones that hide the interactor only when nothing is held. Found by type name: the detector lives in
	// the XRI sample assembly.
	private bool gestureHooked;

	private void HookSystemGesture()
	{
		if (gestureHooked)
		{
			return;
		}
		gestureHooked = true;
		GameObject self = gameObject;
		for (Transform t = transform.parent; t != null; t = t.parent)
		{
			foreach (MonoBehaviour behaviour in t.GetComponents<MonoBehaviour>())
			{
				if (behaviour == null || behaviour.GetType().Name != "MetaSystemGestureDetector")
				{
					continue;
				}
				UnityEngine.Events.UnityEvent started = behaviour.GetType().GetProperty("systemGestureStarted")?.GetValue(behaviour) as UnityEngine.Events.UnityEvent;
				UnityEngine.Events.UnityEvent ended = behaviour.GetType().GetProperty("systemGestureEnded")?.GetValue(behaviour) as UnityEngine.Events.UnityEvent;
				if (started == null || ended == null)
				{
					continue;
				}
				bool targetsSelf = false;
				foreach (UnityEngine.Events.UnityEvent ev in new[] { started, ended })
				{
					for (int i = 0; i < ev.GetPersistentEventCount(); i++)
					{
						if (ev.GetPersistentTarget(i) == self)
						{
							ev.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
							targetsSelf = true;
						}
					}
				}
				if (targetsSelf)
				{
					started.AddListener(() => { if (interactor == null || !interactor.hasSelection) self.SetActive(false); });
					ended.AddListener(() => self.SetActive(true));
				}
			}
		}
	}

	// The rig's PokeGestureDetector (index out, other fingers curled) switches the far casting of this interactor off. People
	// point at what they want, so that switch-off moved to UpdateFarCasting: it is applied only when no residuo can be chosen.
	// The Poke Interactor is a separate object and keeps working. Found by name and field: the detector lives in the sample
	// assembly and does not expose its events.
	private bool pokeHookTried;
	private bool pokeHooked;
	private bool pokePose;
	private XRPokeInteractor pokeInteractor;

	/// <summary>The detector's two events, or false if this behaviour is not a PokeGestureDetector.</summary>
	public static bool TryGetPokeEvents(MonoBehaviour behaviour, out UnityEngine.Events.UnityEvent started, out UnityEngine.Events.UnityEvent ended)
	{
		started = ended = null;
		if (behaviour == null || behaviour.GetType().Name != "PokeGestureDetector")
		{
			return false;
		}
		const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
		started = behaviour.GetType().GetField("m_PokeGestureStarted", Flags)?.GetValue(behaviour) as UnityEngine.Events.UnityEvent;
		ended = behaviour.GetType().GetField("m_PokeGestureEnded", Flags)?.GetValue(behaviour) as UnityEngine.Events.UnityEvent;
		return started != null && ended != null;
	}

	private void HookPokeGesture()
	{
		if (pokeHookTried)
		{
			return;
		}
		pokeHookTried = true;
		foreach (MonoBehaviour behaviour in transform.root.GetComponentsInChildren<MonoBehaviour>(true))
		{
			UnityEngine.Events.UnityEvent started, ended;
			if (!TryGetPokeEvents(behaviour, out started, out ended))
			{
				continue;
			}
			bool targetsSelf = false;
			foreach (UnityEngine.Events.UnityEvent ev in new[] { started, ended })
			{
				for (int i = 0; i < ev.GetPersistentEventCount(); i++)
				{
					if (ev.GetPersistentTarget(i) == interactor)
					{
						ev.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
						targetsSelf = true;
					}
				}
			}
			if (targetsSelf)
			{
				started.AddListener(() => pokePose = true);
				ended.AddListener(() => pokePose = false);
				pokeHooked = true;
			}
		}
		// This hand's Poke Interactor: the nearest one going up from the interactor (they share the "... Hand" parent).
		for (Transform t = transform.parent; t != null && pokeInteractor == null; t = t.parent)
		{
			pokeInteractor = t.GetComponentInChildren<XRPokeInteractor>(true);
		}
	}

	// True while a residuo can be chosen by this hand: any residuo around, nothing held, and not poking something right now
	// (a finger on a poke button, like the start button of the Intro, behaves as it always did).
	private bool CanChooseResiduo()
	{
		bool poking = pokeInteractor != null && pokeInteractor.isActiveAndEnabled && (pokeInteractor.hasHover || pokeInteractor.hasSelection);
		return CustomGrabInteractable.ActiveCount > 0 && !interactor.hasSelection && !poking;
	}

	private void UpdateFarCasting()
	{
		bool choosing = CanChooseResiduo();
		// If the detector could not be hooked (its fields renamed, say), its own calls still run: only overrule them while choosing.
		bool wanted = pokeHooked ? (!pokePose || choosing) : (choosing || interactor.enableFarCasting);
		if (interactor.enableFarCasting != wanted)
		{
			interactor.enableFarCasting = wanted;
		}
	}

	private void OnDisable()
	{
		if (interactor != null && ReferenceEquals(interactor.selectInput.bypass, this))
		{
			interactor.selectInput.bypass = null;
		}
		ResetState();
	}

	private bool HasAncestorNamed(string part)
	{
		for (Transform t = transform; t != null; t = t.parent)
		{
			if (t.name.Contains(part))
			{
				return true;
			}
		}
		return false;
	}

	private void ResetState()
	{
		for (int i = 0; i < 4; i++)
		{
			closed[i] = false;
			grabbing[i] = false;
			closeTime[i] = -999f;
			gates[i] = default(FingerGate);
		}
		lastGateTime = -1f;
		jointPinch = false;
		pinchNow = false;
		missCueArmed = false;
		ClearPointHold();
		trackedThisFrame = false;
		hadSelection = false;
		releaseSince = -1f;
		pendingUntil = -999f;
		lastTarget = null;
		lastTargetTime = -999f;
		selectLatched = false;
		held = false;
		performedThisFrame = false;
		completedThisFrame = false;
		value = 0f;
		tickFrame = -1;
		lastAimMoveTime = Time.unscaledTime;
	}

	// ---------------------------------------------------------------- select input (called by the interactor, once per frame)

	public bool ReadIsPerformed()
	{
		Tick();
		return held;
	}

	public bool ReadWasPerformedThisFrame()
	{
		Tick();
		return performedThisFrame;
	}

	public bool ReadWasCompletedThisFrame()
	{
		Tick();
		return completedThisFrame;
	}

	public float ReadValue()
	{
		Tick();
		return value;
	}

	public bool TryReadValue(out float result)
	{
		Tick();
		result = value;
		return true;
	}

	private void Tick()
	{
		if (inTick || tickFrame == Time.frameCount || interactor == null)
		{
			return;
		}
		inTick = true;
		tickFrame = Time.frameCount;
		float now = Time.unscaledTime;

		// The original input (pinch). While the interactor is calling us as its bypass, reading it returns the raw input.
		XRInputButtonReader original = interactor.selectInput;
		bool pinch = original.ReadIsPerformed();
		float pinchValue = original.ReadValue();

		int risen = UpdateFingers(now);
		bool hasSelection = interactor.hasSelection;
		bool anyPinch = pinch || jointPinch;
		bool pinchRose = anyPinch && !pinchNow;
		pinchNow = anyPinch;

		if (hasSelection)
		{
			if (!hadSelection)
			{
				// A grab just started (by pinch or by gesture): the fingers that closed moments ago are the ones holding it.
				for (int i = 0; i < 4; i++)
				{
					grabbing[i] = closed[i] && now - closeTime[i] <= GestureMemory;
				}
				selectLatched = true;
				releaseSince = -1f;
			}
			for (int i = 0; i < 4; i++)
			{
				if ((risen & (1 << i)) != 0)
				{
					grabbing[i] = true;   // closing more fingers while holding also holds
				}
			}
		}
		else
		{
			for (int i = 0; i < 4; i++)
			{
				grabbing[i] = false;
			}
			selectLatched = false;
			releaseSince = -1f;
			if (risen != 0 || pinchRose)
			{
				pendingUntil = now + GrabWindow;   // a closing gesture: Update() takes the pointed object, if there is one
				if (!missCueArmed)
				{
					missCueArmed = true;
					missCueAt = now + MissCueDelay;
				}
			}
		}
		hadSelection = hasSelection;

		if (hasSelection && selectLatched)
		{
			// The hand "looks open" only when everything says so on a tracked frame: no pinch input, no joint pinch,
			// no grabbing finger closed. Anything else (an untracked frame included) restarts the release timer.
			bool anyGrabbing = false;
			for (int i = 0; i < 4; i++)
			{
				if (grabbing[i] && closed[i])
				{
					anyGrabbing = true;
				}
			}
			bool looksOpen = trackedThisFrame && !pinch && !jointPinch && !anyGrabbing;
			if (!looksOpen)
			{
				releaseSince = -1f;
			}
			else if (releaseSince < 0f)
			{
				releaseSince = now;
			}
			else if (now - releaseSince >= ReleaseDelay)
			{
				selectLatched = false;   // confirmed release
				releaseSince = -1f;
			}
		}
		bool latchHeld = hasSelection && selectLatched;

		bool wasHeld = held;
		held = pinch || latchHeld;
		performedThisFrame = held && !wasHeld;
		// Second term: a selection with nothing holding it any more must still end (the interactor waits for this edge).
		completedThisFrame = (!held && wasHeld) || (!held && hasSelection);
		value = Mathf.Max(pinchValue, latchHeld ? 1f : 0f);
		inTick = false;
	}

	/// <summary>Updates the open/closed state of the four fingers. Returns a bit mask of the fingers that closed this frame.</summary>
	private int UpdateFingers(float now)
	{
		trackedThisFrame = false;
		XRHand hand;
		if (!TryGetHand(out hand) || !hand.isTracked)
		{
			if (now - lastTrackedTime > TrackingGrace)
			{
				// While holding, the grabbing fingers are kept: only a confirmed open hand (see Tick) releases the object.
				bool holding = interactor.hasSelection;
				for (int i = 0; i < 4; i++)
				{
					closed[i] = false;
					if (!holding)
					{
						grabbing[i] = false;
					}
				}
				if (!holding)
				{
					jointPinch = false;
				}
				for (int i = 0; i < 4; i++)
				{
					gates[i].Forget();   // when tracking comes back, the pose found is taken as is (it is not a "closing gesture")
				}
			}
			return 0;
		}
		lastTrackedTime = now;

		// Joint pinch (thumb tip - index tip, both in tracking space). Keeps its last value if a pose is missing.
		Pose thumbTip, indexTip;
		if (hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out thumbTip) &&
			hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out indexTip))
		{
			float pinchDist = Vector3.Distance(thumbTip.position, indexTip.position);
			jointPinch = jointPinch ? pinchDist < PinchOpenDist : pinchDist < PinchCloseDist;
		}

		Pose wrist;
		if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out wrist))
		{
			return 0;
		}
		trackedThisFrame = true;
		int risen = 0;
		float gateDt = lastGateTime < 0f ? 0f : now - lastGateTime;
		lastGateTime = now;
		bool holdingNow = interactor.hasSelection;
		for (int i = 0; i < 4; i++)
		{
			float ratio;
			if (!TryGetOpenness(hand, wrist.position, Fingers[i], out ratio))
			{
				continue;
			}
			if (gates[i].Step(ratio, holdingNow, gateDt))
			{
				closeTime[i] = now;
				risen |= 1 << i;
			}
			if (!gates[i].Closed)
			{
				grabbing[i] = false;
			}
			closed[i] = gates[i].Closed;
		}
		return risen;
	}

	private static bool TryGetOpenness(XRHand hand, Vector3 wrist, XRHandJointID[] chain, out float ratio)
	{
		ratio = 1f;
		Vector3 previous = wrist;
		Vector3 tip = wrist;
		float length = 0f;
		for (int i = 0; i < chain.Length; i++)
		{
			Pose pose;
			if (!hand.GetJoint(chain[i]).TryGetPose(out pose))
			{
				return false;
			}
			length += Vector3.Distance(previous, pose.position);
			previous = pose.position;
			tip = pose.position;
		}
		if (length < 0.01f)
		{
			return false;
		}
		ratio = Vector3.Distance(wrist, tip) / length;
		return true;
	}

	private bool TryGetHand(out XRHand hand)
	{
		hand = default(XRHand);
		if (handSubsystem == null || !handSubsystem.running)
		{
			handSubsystem = null;
			SubsystemManager.GetSubsystems(subsystems);
			for (int i = 0; i < subsystems.Count; i++)
			{
				if (subsystems[i].running)
				{
					handSubsystem = subsystems[i];
					break;
				}
			}
			if (handSubsystem == null)
			{
				return false;
			}
		}
		hand = isLeft ? handSubsystem.leftHand : handSubsystem.rightHand;
		return true;
	}

	// ---------------------------------------------------------------- palm joints in world space (used by HandHoldPose)

	private static Transform trackingSpace;

	public bool IsLeft
	{
		get { return isLeft; }
	}

	/// <summary>
	/// World positions of the wrist and of the index / middle / little knuckles (proximal joints). False when the hand
	/// is not tracked. XR Hands reports joints in tracking space (the XR Origin's camera offset), hence the conversion.
	/// </summary>
	public bool TryGetPalmJoints(out Vector3 wrist, out Vector3 index, out Vector3 middle, out Vector3 little)
	{
		wrist = index = middle = little = Vector3.zero;
		XRHand hand;
		if (!TryGetHand(out hand) || !hand.isTracked)
		{
			return false;
		}
		Pose w, i, m, l;
		if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out w) ||
			!hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out i) ||
			!hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out m) ||
			!hand.GetJoint(XRHandJointID.LittleProximal).TryGetPose(out l))
		{
			return false;
		}
		Transform space = GetTrackingSpace();
		if (space == null)
		{
			return false;
		}
		wrist = space.TransformPoint(w.position);
		index = space.TransformPoint(i.position);
		middle = space.TransformPoint(m.position);
		little = space.TransformPoint(l.position);
		return true;
	}

	private static Transform GetTrackingSpace()
	{
		if (trackingSpace != null)
		{
			return trackingSpace;
		}
		Unity.XR.CoreUtils.XROrigin origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
		if (origin != null)
		{
			trackingSpace = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : origin.transform;
		}
		else if (Camera.main != null)
		{
			trackingSpace = Camera.main.transform.parent;
		}
		return trackingSpace;
	}

	// ---------------------------------------------------------------- taking the pointed object on a closing gesture

	private void Update()
	{
		if (interactor == null)
		{
			return;
		}
		float now = Time.unscaledTime;

		// Is the aim pose being updated? (see GetAim)
		aimTransform = interactor.farInteractionCaster != null ? interactor.farInteractionCaster.castOrigin : null;
		if (aimTransform != null && (aimTransform.localPosition != lastAimPos || aimTransform.localRotation != lastAimRot))
		{
			lastAimPos = aimTransform.localPosition;
			lastAimRot = aimTransform.localRotation;
			lastAimMoveTime = now;
		}

		UpdateFarCasting();

		if (interactor.hasSelection)
		{
			ClearPointHold();
			pendingUntil = -999f;
			missCueArmed = false;
			return;
		}

		// Remember what the hand is pointing at: closing the fist moves the aim ray a little, and the ray itself shakes at
		// arm's length; neither should lose the target.
		XRGrabInteractable current = null;
		targets.Clear();
		interactor.GetValidTargets(targets);
		for (int i = 0; i < targets.Count; i++)
		{
			XRGrabInteractable grab = targets[i] as XRGrabInteractable;
			if (grab != null && grab.isActiveAndEnabled)
			{
				current = grab;
				break;
			}
		}
		if (current != null)
		{
			lastTarget = current;
			lastTargetTime = now;
		}
		UpdatePointHold(current as CustomGrabInteractable, now);

		if (now > pendingUntil)
		{
			missCueArmed = false;
			return;
		}
		bool stillClosing = pinchNow;
		for (int i = 0; i < 4; i++)
		{
			if (closed[i] && now - closeTime[i] <= GestureMemory)
			{
				stillClosing = true;
			}
		}
		if (!stillClosing)
		{
			pendingUntil = -999f;
			missCueArmed = false;
			return;
		}
		XRGrabInteractable target = current;
		if (target == null && lastTarget != null && lastTarget.isActiveAndEnabled && now - lastTargetTime <= LatchTime)
		{
			target = lastTarget;
		}
		if (target == null)
		{
			// Forming the pointing pose curls fingers. With the ray on nothing that is not a grab: whatever the ray crosses next must not be taken.
			if (pokePose && !closed[0] && !pinchNow)
			{
				pendingUntil = -999f;
				missCueArmed = false;
				return;
			}
			PlayMissCueIfDue(now);
			return;
		}
		XRInteractionManager manager = interactor.interactionManager;
		if (manager == null || !manager.IsSelectPossible(interactor, target))
		{
			return;
		}
		manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)target);
		pendingUntil = -999f;
		missCueArmed = false;
		if (interactor.hasSelection)
		{
			for (int i = 0; i < 4; i++)
			{
				grabbing[i] = closed[i] && now - closeTime[i] <= GestureMemory;
			}
			selectLatched = true;   // held from now on until a confirmed release
			releaseSince = -1f;
			hadSelection = true;
		}
	}

	// ---------------------------------------------------------------- target hold (the object stays the target for a moment)

	/// <summary>
	/// The residuo this hand still counts as pointing at after the ray left it, or null. Read by HandBeamFX, so the beam tip
	/// follows the same object that keeps its outline, shake and sparkles (CustomGrabInteractable.KeepPointed).
	/// </summary>
	public CustomGrabInteractable PointedResiduo
	{
		get { return shownHold != null && !shownHold.IsDumped && !shownHold.isSelected && shownHold.isActiveAndEnabled ? shownHold : null; }
	}

	private void UpdatePointHold(CustomGrabInteractable current, float now)
	{
		if (current != null && (current.IsDumped || current.isSelected))
		{
			current = null;
		}
		CustomGrabInteractable kept = pointHold.Step(current, now, TargetHold);
		if (kept != null && (kept.IsDumped || kept.isSelected || !kept.isActiveAndEnabled))
		{
			pointHold.Clear();
			kept = null;
		}
		if (kept != shownHold)
		{
			if (shownHold != null)
			{
				shownHold.ReleasePointed(isLeft);   // replaced or expired: off in this same frame
			}
			shownHold = kept;
		}
		if (current != null)
		{
			current.KeepPointed(isLeft, pointHold.Until);   // refreshed while the ray is on it, so its hover exit finds the hold already running
		}
	}

	private void ClearPointHold()
	{
		pointHold.Clear();
		if (shownHold != null)
		{
			shownHold.ReleasePointed(isLeft);
		}
		shownHold = null;
	}

	// A close that found nothing: a very soft sound, once. Not for the pointing pose (its curling fingers are not a grab),
	// and not when no residuo can be chosen.
	private void PlayMissCueIfDue(float now)
	{
		if (!missCueArmed || now < missCueAt)
		{
			return;
		}
		missCueArmed = false;
		if (pokePose || !CanChooseResiduo() || now - lastMissCue < MissCueMinGap)
		{
			return;
		}
		lastMissCue = now;
		GrabAudio.PlayMiss();
	}

	// ---------------------------------------------------------------- aim used to choose the container while holding

	/// <summary>
	/// Where the interactor is aiming from and to. Controllers: the far caster origin (the controller pose), as always.
	/// Hands: the hand's aim pose; if that pose is not being updated (it can freeze with the fist closed on some
	/// runtimes) the direction shoulder -> held object is used instead, so the choice of container never gets stuck.
	/// </summary>
	public static void GetAim(NearFarInteractor nearFar, Transform heldObject, out Vector3 origin, out Vector3 direction)
	{
		Transform aim = nearFar.transform;
		if (nearFar.farInteractionCaster != null && nearFar.farInteractionCaster.castOrigin != null)
		{
			aim = nearFar.farInteractionCaster.castOrigin;
		}
		origin = aim.position;
		direction = aim.forward;

		HandCloseGrab hand = nearFar.GetComponent<HandCloseGrab>();
		if (hand == null || !hand.isActiveAndEnabled || heldObject == null)
		{
			return;
		}
		float now = Time.unscaledTime;
		bool handTracked = now - hand.lastTrackedTime < TrackingGrace;
		bool aimFrozen = now - hand.lastAimMoveTime > AimFrozenTime;
		Camera head = Camera.main;
		if (!handTracked || !aimFrozen || head == null)
		{
			return;
		}
		Vector3 right = head.transform.right;
		right.y = 0f;
		right.Normalize();
		Vector3 shoulder = head.transform.position + Vector3.down * 0.22f + right * (hand.isLeft ? -0.17f : 0.17f);
		Vector3 toHand = heldObject.position - shoulder;
		if (toHand.sqrMagnitude > 0.01f)
		{
			origin = shoulder;
			direction = toHand.normalized;
		}
	}
}

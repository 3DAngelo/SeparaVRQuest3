// Numbers for the forgiving-grab work, measured without a headset. Called by ParticleSnapshot.RunAll at the end of its run.
//
// CONEPROBE uses XRI's real CurveInteractionCaster with the values of the scene's far caster, on the scene's residuos, moved
// to the scene's "spot" points the way GameManager does at start (restored afterwards; the scene is never saved).
// REARMPROBE and HOLDPROBE run HandCloseGrab's pure logic on made-up curves at 72 Hz. None of this is Play mode, and none of
// the curves comes from a real hand.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

public static class GrabProbes
{
	private const float Frame = 1f / 72f;
	private static readonly float[] ConeAngles = { 6f, 10f, 12f };
	private static readonly float[] OffsetDegrees = { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 8f };
	private const int OffsetDirections = 8;
	private const int RandomLayouts = 12;
	private const int LayoutSeed = 12345;

	public static void Run(Vector3 aimOrigin)
	{
		Guarded("POKEPROBE", PokeProbe);
		Guarded("AUDIOPROBE", AudioProbe);
		Guarded("REARMPROBE", RearmProbe);
		Guarded("HOLDPROBE", HoldProbe);
		Guarded("CONEPROBE", () => ConeProbe(aimOrigin));
	}

	private static void Guarded(string name, Action probe)
	{
		try
		{
			probe();
		}
		catch (Exception e)
		{
			Log(name + " FAILED: " + e);
		}
	}

	private static void Log(string message)
	{
		Debug.Log("[GrabProbes] " + message);
	}

	// ---------------------------------------------------------------- poke wiring, as found in the scene

	private static void PokeProbe()
	{
		foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			UnityEngine.Events.UnityEvent started, ended;
			if (behaviour == null || behaviour.GetType().Name != "PokeGestureDetector")
			{
				continue;
			}
			bool found = HandCloseGrab.TryGetPokeEvents(behaviour, out started, out ended);
			string line = "POKEPROBE detector '" + behaviour.name + "' events found by reflection " + found;
			if (found)
			{
				foreach (UnityEngine.Events.UnityEvent ev in new[] { started, ended })
				{
					for (int i = 0; i < ev.GetPersistentEventCount(); i++)
					{
						UnityEngine.Object target = ev.GetPersistentTarget(i);
						line += " | " + ev.GetPersistentMethodName(i) + " on " + (target != null ? target.GetType().Name + " '" + target.name + "'" : "null");
					}
				}
			}
			Log(line);
		}
		foreach (NearFarInteractor nearFar in UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			XRPokeInteractor poke = null;
			for (Transform t = nearFar.transform.parent; t != null && poke == null; t = t.parent)
			{
				poke = t.GetComponentInChildren<XRPokeInteractor>(true);
			}
			Log("POKEPROBE interactor '" + nearFar.transform.parent.name + "/" + nearFar.name + "' nearest poke interactor '" + (poke != null ? poke.transform.parent.name + "/" + poke.name : "none") + "'");
		}
	}

	// ---------------------------------------------------------------- the clips (loudness only; nobody listened to them here)

	private static void AudioProbe()
	{
		Log("AUDIOPROBE volumes grab " + GrabAudio.GrabVolume + " miss " + GrabAudio.MissVolume);
		LogClip("Resources/" + GrabAudio.GrabClipName, Resources.Load<AudioClip>(GrabAudio.GrabClipName));
		LogClip("Resources/" + GrabAudio.MissClipName, Resources.Load<AudioClip>(GrabAudio.MissClipName));
		string[] others =
		{
			"Assets/Samples/XR Interaction Toolkit/3.0.6/Starter Assets/DemoSceneAssets/Audio/Button Pop.wav",
			"Assets/Samples/XR Interaction Toolkit/3.0.6/Hands Interaction Demo/HandsDemoSceneAssets/Audio/ButtonClick.wav",
			"Assets/AudioClip/up.ogg",
			"Assets/AudioClip/ContainerAbre.ogg",
		};
		foreach (string path in others)
		{
			LogClip(path, AssetDatabase.LoadAssetAtPath<AudioClip>(path));
		}
	}

	private static void LogClip(string name, AudioClip clip)
	{
		if (clip == null)
		{
			Log("AUDIOPROBE " + name + ": NOT FOUND");
			return;
		}
		clip.LoadAudioData();
		float[] data = new float[clip.samples * clip.channels];
		bool read = clip.GetData(data, 0);
		float peak = 0f;
		double sum = 0.0;
		foreach (float v in data)
		{
			peak = Mathf.Max(peak, Mathf.Abs(v));
			sum += v * v;
		}
		double rms = data.Length > 0 ? Math.Sqrt(sum / data.Length) : 0.0;
		Log("AUDIOPROBE " + name + ": " + clip.length.ToString("F3") + " s, " + clip.frequency + " Hz, " + clip.channels + " ch, load " + clip.loadType + ", read " + read + ", peak " + peak.ToString("F3") + ", rms " + rms.ToString("F3"));
	}

	// ---------------------------------------------------------------- re-arm logic on finger curves

	/// <summary>Piecewise linear curve (time, value, time, value...) sampled at 72 Hz.</summary>
	private static float[] Curve(params float[] keys)
	{
		float end = keys[keys.Length - 2];
		int n = Mathf.CeilToInt(end / Frame) + 1;
		float[] samples = new float[n];
		int k = 0;
		for (int i = 0; i < n; i++)
		{
			float t = i * Frame;
			while (k + 2 < keys.Length - 2 && t > keys[k + 2])
			{
				k += 2;
			}
			float a = Mathf.InverseLerp(keys[k], keys[k + 2], t);
			samples[i] = Mathf.Lerp(keys[k + 1], keys[k + 3], a);
		}
		return samples;
	}

	private static float[] Jitter(float center, float amount, float seconds, int seed)
	{
		System.Random random = new System.Random(seed);
		float[] samples = new float[Mathf.CeilToInt(seconds / Frame)];
		for (int i = 0; i < samples.Length; i++)
		{
			samples[i] = center + amount * (float)(random.NextDouble() * 2.0 - 1.0);
		}
		samples[0] = 1f;   // starts open, closes in the second sample: one edge is the expected count
		return samples;
	}

	/// <summary>
	/// Runs the curve through the old thresholds and through FingerGate. grabAtEdge: the object is taken on that closing edge
	/// (1 = first, 0 = never) and held until the finger counts as open. dropoutAt: tracking lost at that time (seconds), for
	/// one frame as far as the logic is concerned.
	/// </summary>
	private static void RunCurve(string name, float[] curve, int grabAtEdge = 0, float dropoutAt = -1f)
	{
		string newLog = RunGate(curve, grabAtEdge, dropoutAt);
		string oldLog = RunOld(curve, grabAtEdge);
		Log("REARMPROBE " + name + "\n    new: " + newLog + "\n    old: " + oldLog);
	}

	private static string RunGate(float[] curve, int grabAtEdge, float dropoutAt)
	{
		HandCloseGrab.FingerGate gate = default(HandCloseGrab.FingerGate);
		int edges = 0;
		bool holding = false;
		string events = "";
		for (int i = 0; i < curve.Length; i++)
		{
			float t = i * Frame;
			if (dropoutAt >= 0f && Mathf.Abs(t - dropoutAt) < Frame * 0.5f)
			{
				gate.Forget();
				events += " dropout@" + t.ToString("F2");
			}
			bool wasClosed = gate.Closed;
			bool rose = gate.Step(curve[i], holding, Frame);
			if (rose)
			{
				edges++;
				events += " close@" + t.ToString("F2") + "(" + curve[i].ToString("F2") + ")";
				if (edges == grabAtEdge)
				{
					holding = true;
					events += "=GRAB";
				}
			}
			else if (wasClosed && !gate.Closed)
			{
				events += (holding ? " RELEASE@" : " rearm@") + t.ToString("F2") + "(" + curve[i].ToString("F2") + ")";
				holding = false;
			}
		}
		return edges + " closing edges;" + events + (holding ? " STILL HOLDING at end" : "");
	}

	private static string RunOld(float[] curve, int grabAtEdge)
	{
		bool closed = false;
		bool known = false;
		bool holding = false;
		int edges = 0;
		string events = "";
		for (int i = 0; i < curve.Length; i++)
		{
			float t = i * Frame;
			bool now = closed ? curve[i] < 0.82f : curve[i] < 0.70f;
			if (now && !closed && known)
			{
				edges++;
				events += " close@" + t.ToString("F2");
				if (edges == grabAtEdge)
				{
					holding = true;
					events += "=GRAB";
				}
			}
			else if (!now && closed)
			{
				events += (holding ? " RELEASE@" : " rearm@") + t.ToString("F2");
				holding = false;
			}
			closed = now;
			known = true;
		}
		return edges + " closing edges;" + events + (holding ? " STILL HOLDING at end" : "");
	}

	private static void RearmProbe()
	{
		Log("REARMPROBE constants CloseRatio " + HandCloseGrab.CloseRatio + " OpenRatio " + HandCloseGrab.OpenRatio + " RearmDelta " + HandCloseGrab.RearmDelta + " RecloseDelta " + HandCloseGrab.RecloseDelta
			+ " ReleaseMargin " + HandCloseGrab.ReleaseMargin + " ReleaseFloor " + HandCloseGrab.ReleaseFloor + " PeakFloor " + HandCloseGrab.PeakFloor + " PeakDecay " + HandCloseGrab.PeakDecay);
		RunCurve("normal hand, 3 closes (1.0 <-> 0.45)", Curve(0f, 1f, 0.5f, 1f, 1f, 0.45f, 1.5f, 1f, 2f, 0.45f, 2.5f, 1f, 3f, 0.45f, 3.5f, 1f));
		RunCurve("LOW MOBILITY opens to 0.75, 3 closes (0.75 <-> 0.45)", Curve(0f, 0.75f, 0.5f, 0.75f, 1f, 0.45f, 1.5f, 0.75f, 2f, 0.45f, 2.5f, 0.75f, 3f, 0.45f, 3.5f, 0.75f));
		RunCurve("LOW MOBILITY small range, 3 closes (0.75 <-> 0.60)", Curve(0f, 0.75f, 0.5f, 0.75f, 1f, 0.60f, 1.5f, 0.75f, 2f, 0.60f, 2.5f, 0.75f, 3f, 0.60f, 3.5f, 0.75f));
		RunCurve("LOW MOBILITY opens to 0.65 only: starts closed, 2 re-closes (0.65 <-> 0.45)", Curve(0f, 0.65f, 0.5f, 0.65f, 1f, 0.45f, 1.5f, 0.65f, 2f, 0.45f, 2.5f, 0.65f));
		RunCurve("LOW MOBILITY grabs, opens to 0.75 to let go", Curve(0f, 0.75f, 0.5f, 0.75f, 1f, 0.45f, 3f, 0.45f, 3.5f, 0.75f, 4f, 0.75f), 1);
		RunCurve("LOW MOBILITY grabs after a tracking spike to 0.85, opens to 0.75 for 10 s", Curve(0f, 0.75f, 0.2f, 0.75f, 0.25f, 0.85f, 0.3f, 0.75f, 1f, 0.45f, 2f, 0.45f, 2.5f, 0.75f, 12.5f, 0.75f), 1);
		RunCurve("normal hand grabs, relaxes the grip to 0.75 and back (must keep holding)", Curve(0f, 1f, 0.5f, 1f, 1f, 0.45f, 2f, 0.45f, 2.5f, 0.75f, 3.5f, 0.75f, 4f, 0.45f, 5f, 0.45f), 1);
		RunCurve("normal hand grabs, then opens fully (releases at OpenRatio as before)", Curve(0f, 1f, 0.5f, 1f, 1f, 0.45f, 2f, 0.45f, 2.5f, 1f, 3f, 1f), 1);
		RunCurve("normal hand re-grabs from half open, relaxes to 0.68 (must keep holding)", Curve(0f, 1f, 0.5f, 1f, 1f, 0.45f, 1.5f, 0.45f, 1.8f, 0.60f, 2.1f, 0.45f, 3f, 0.45f, 3.3f, 0.68f, 5f, 0.68f), 2);
		RunCurve("normal hand grabs, tracking drops, comes back at 0.45, relaxes to 0.75 (must keep holding)", Curve(0f, 1f, 0.5f, 1f, 1f, 0.45f, 3f, 0.45f, 3.5f, 0.75f, 5f, 0.75f), 1, 2f);
		RunCurve("normal hand, long hold with a relaxed grip of 0.72 for 120 s (when does it drop?)", Curve(0f, 1f, 0.5f, 1f, 1f, 0.45f, 2f, 0.45f, 2.5f, 0.72f, 122.5f, 0.72f), 1);
		RunCurve("LOW MOBILITY grabs after a tracking glitch of 0.85 for 0.5 s, opens to 0.75 for 40 s", Curve(0f, 0.75f, 0.2f, 0.75f, 0.25f, 0.85f, 0.75f, 0.85f, 0.8f, 0.75f, 1.5f, 0.45f, 2.5f, 0.45f, 3f, 0.75f, 43f, 0.75f), 1);
		RunCurve("half relax from the fist (0.45 -> 0.60 -> 0.45): the new re-grab movement", Curve(0f, 1f, 0.5f, 1f, 1f, 0.45f, 2f, 0.45f, 2.3f, 0.60f, 2.6f, 0.45f, 3f, 0.45f));
		foreach (float amount in new[] { 0.03f, 0.05f, 0.06f, 0.08f, 0.10f, 0.15f })
		{
			RunCurveCounts("fist noise +-" + amount.ToString("F2") + " around 0.45 for 10 s at 72 Hz, white (worst case; expected 1 edge)", Jitter(0.45f, amount, 10f, 7));
		}
		RunCurveCounts("threshold noise +-0.03 around 0.70 for 10 s", Jitter(0.70f, 0.03f, 10f, 11));
	}

	private static void RunCurveCounts(string name, float[] curve)
	{
		string newLog = RunGate(curve, 0, -1f);
		string oldLog = RunOld(curve, 0);
		Log("REARMPROBE " + name + " -> new " + newLog.Substring(0, newLog.IndexOf(';')) + ", old " + oldLog.Substring(0, oldLog.IndexOf(';')));
	}

	// ---------------------------------------------------------------- target hold

	/// <summary>Steps PointHold over a list of (seconds, target or null) and logs each change of what is shown.</summary>
	private static void RunHold(string name, params object[] script)
	{
		HandCloseGrab.PointHold<string> hold = default(HandCloseGrab.PointHold<string>);
		string shown = null;
		string events = "";
		float t = 1f;
		int changes = 0;
		float lastRayOn = -1f;
		for (int s = 0; s < script.Length; s += 2)
		{
			float segmentEnd = t + Convert.ToSingle(script[s]);
			string current = script[s + 1] as string;
			while (t < segmentEnd)
			{
				string now = hold.Step(current, t, HandCloseGrab.TargetHold);
				if (current != null)
				{
					lastRayOn = t;
				}
				if (now != shown)
				{
					changes++;
					events += " " + (t - 1f).ToString("F3") + ":" + (shown ?? "-") + "->" + (now ?? "-") + (now == null ? " (" + (t - lastRayOn).ToString("F3") + " s after the ray left)" : "");
					shown = now;
				}
				t += Frame;
			}
		}
		Log("HOLDPROBE " + name + ": " + changes + " changes:" + events);
	}

	private static void HoldProbe()
	{
		Log("HOLDPROBE constants TargetHold " + HandCloseGrab.TargetHold + " LatchTime " + HandCloseGrab.LatchTime + " GrabWindow " + HandCloseGrab.GrabWindow + " GestureMemory " + HandCloseGrab.GestureMemory
			+ " | GestureMemory>=GrabWindow " + (HandCloseGrab.GestureMemory >= HandCloseGrab.GrabWindow) + " LatchTime>=TargetHold " + (HandCloseGrab.LatchTime >= HandCloseGrab.TargetHold));
		RunHold("ray on A 0.5 s, then nothing", 0.5f, "A", 1f, null);
		RunHold("ray on A, leaves 0.1 s, B enters (B replaces at once)", 0.5f, "A", 0.1f, null, 0.3f, "B", 1f, null);
		RunHold("ray on A, straight to B", 0.5f, "A", 0.3f, "B", 1f, null);
		object[] edge = new object[40];
		for (int i = 0; i < edge.Length; i += 2)
		{
			edge[i] = 3f * Frame;
			edge[i + 1] = i % 4 == 0 ? "A" : null;
		}
		RunHold("ray flickering on and off A every 3 frames for 20 cycles (no flicker expected)", edge);
		RunHold("ray on A for a single frame", Frame * 0.5f, "A", 1f, null);
	}

	// ---------------------------------------------------------------- the far cone on the real layout

	private enum Outcome { Hit, Stolen, Blocked, None }

	private sealed class Tally
	{
		public readonly int[] Counts = new int[4];
		public int Total;
		public void Add(Outcome o) { Counts[(int)o]++; Total++; }
		public override string ToString()
		{
			return "hit " + Pct(Counts[0], Total) + " stolen " + Pct(Counts[1], Total) + " blocked " + Pct(Counts[2], Total) + " none " + Pct(Counts[3], Total) + " (n=" + Total + ")";
		}
		private static string Pct(int a, int b) { return (b == 0 ? 0f : 100f * a / b).ToString("F0") + "%"; }
	}

	private static void Count(IDictionary<string, int> counts, string key)
	{
		int count;
		counts.TryGetValue(key, out count);
		counts[key] = count + 1;
	}

	private static void ConeProbe(Vector3 origin)
	{
		CurveInteractionCaster source = null;
		foreach (NearFarInteractor nearFar in UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			source = nearFar.GetComponentInChildren<CurveInteractionCaster>(true);
			if (source != null)
			{
				break;
			}
		}
		if (source == null)
		{
			Log("CONEPROBE no far caster in the scene");
			return;
		}
		Log("CONEPROBE far caster in scene: type " + source.hitDetectionType + " angle " + source.coneCastAngle + " distance " + source.castDistance + " mask 0x" + source.raycastMask.value.ToString("X8")
			+ " segments " + source.targetNumCurveSegments + " triggers " + source.raycastTriggerInteraction + " snap " + source.raycastSnapVolumeInteraction + " origin " + origin.ToString("F2"));

		GameObject[] residuoObjects = GameObject.FindGameObjectsWithTag("residuo");
		GameObject[] spots = GameObject.FindGameObjectsWithTag("spot");
		List<CustomGrabInteractable> residuos = new List<CustomGrabInteractable>();
		foreach (GameObject go in residuoObjects)
		{
			CustomGrabInteractable grab = go.GetComponentInParent<CustomGrabInteractable>();
			if (grab != null && !residuos.Contains(grab))
			{
				residuos.Add(grab);
			}
		}
		Log("CONEPROBE " + residuos.Count + " residuos, " + spots.Length + " spots (GameManager moves each residuo to a random spot at start; authored layout + " + RandomLayouts + " random layouts measured, seed " + LayoutSeed + ")");

		Vector3[] authored = new Vector3[residuos.Count];
		for (int i = 0; i < residuos.Count; i++)
		{
			authored[i] = residuos[i].transform.position;
		}

		GameObject temp = new GameObject("__ConeProbe");
		CurveInteractionCaster caster = null;
		try
		{
			XRInteractionManager manager = temp.AddComponent<XRInteractionManager>();
			foreach (CustomGrabInteractable residuo in residuos)
			{
				if (residuo.colliders.Count == 0)
				{
					foreach (Collider c in residuo.GetComponentsInChildren<Collider>(true))
					{
						if (!c.isTrigger)
						{
							residuo.colliders.Add(c);
						}
					}
				}
				manager.RegisterInteractable((IXRInteractable)residuo);
			}
			caster = temp.AddComponent<CurveInteractionCaster>();
			caster.hitDetectionType = source.hitDetectionType;
			caster.castDistance = source.castDistance;
			caster.raycastMask = source.raycastMask;
			caster.targetNumCurveSegments = source.targetNumCurveSegments;
			caster.raycastTriggerInteraction = source.raycastTriggerInteraction;
			caster.raycastSnapVolumeInteraction = source.raycastSnapVolumeInteraction;
			caster.enableStabilization = false;
			caster.castOrigin = temp.transform;
			typeof(CurveInteractionCaster).GetMethod("InitializeCaster", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(caster, null);

			Tally[,] all = new Tally[ConeAngles.Length, OffsetDegrees.Length];
			Tally[,] authoredOnly = new Tally[ConeAngles.Length, OffsetDegrees.Length];
			for (int a = 0; a < ConeAngles.Length; a++)
			{
				for (int o = 0; o < OffsetDegrees.Length; o++)
				{
					all[a, o] = new Tally();
					authoredOnly[a, o] = new Tally();
				}
			}
			Tally[,,] perResiduo = new Tally[residuos.Count, ConeAngles.Length, OffsetDegrees.Length];
			for (int i = 0; i < residuos.Count; i++)
			{
				for (int a = 0; a < ConeAngles.Length; a++)
				{
					for (int o = 0; o < OffsetDegrees.Length; o++)
					{
						perResiduo[i, a, o] = new Tally();
					}
				}
			}
			SortedDictionary<string, int> stealers = new SortedDictionary<string, int>();
			SortedDictionary<string, int> blockers = new SortedDictionary<string, int>();
			float smallestSeparation = 999f;
			List<float> nearestSeparations = new List<float>();
			System.Random random = new System.Random(LayoutSeed);
			List<Collider> colliders = new List<Collider>();
			List<RaycastHit> hits = new List<RaycastHit>();

			for (int layout = 0; layout <= RandomLayouts; layout++)
			{
				if (layout > 0)
				{
					List<GameObject> free = new List<GameObject>(spots);
					for (int i = 0; i < residuos.Count && free.Count > 0; i++)
					{
						int pick = random.Next(free.Count);
						residuos[i].transform.position = free[pick].transform.position;
						free.RemoveAt(pick);
					}
				}
				Physics.SyncTransforms();
				Vector3[] centers = new Vector3[residuos.Count];
				for (int i = 0; i < residuos.Count; i++)
				{
					centers[i] = residuos[i].transform.TransformPoint(HandBeamFX.LocalCenter(residuos[i]));
				}
				for (int i = 0; i < residuos.Count; i++)
				{
					float nearest = 999f;
					for (int j = 0; j < residuos.Count; j++)
					{
						if (i != j)
						{
							nearest = Mathf.Min(nearest, Vector3.Angle(centers[i] - origin, centers[j] - origin));
						}
					}
					nearestSeparations.Add(nearest);
					smallestSeparation = Mathf.Min(smallestSeparation, nearest);

					Vector3 toCenter = (centers[i] - origin).normalized;
					Vector3 side = Vector3.Cross(toCenter, Vector3.up).normalized;
					for (int a = 0; a < ConeAngles.Length; a++)
					{
						caster.coneCastAngle = ConeAngles[a];
						for (int o = 0; o < OffsetDegrees.Length; o++)
						{
							int directions = OffsetDegrees[o] == 0f ? 1 : OffsetDirections;
							for (int d = 0; d < directions; d++)
							{
								Vector3 axis = Quaternion.AngleAxis(d * 360f / OffsetDirections, toCenter) * side;
								Vector3 aim = Quaternion.AngleAxis(OffsetDegrees[o], axis) * toCenter;
								temp.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(aim));
								caster.TryGetColliderTargets(manager, colliders, hits);
								Outcome outcome = Outcome.None;
								if (hits.Count > 0)
								{
									IXRInteractable first;
									if (!manager.TryGetInteractableForCollider(hits[0].collider, out first))
									{
										outcome = Outcome.Blocked;
									}
									else if (ReferenceEquals(first, residuos[i]))
									{
										outcome = Outcome.Hit;
									}
									else
									{
										outcome = Outcome.Stolen;
										Count(stealers, ConeAngles[a] + " deg: " + residuos[i].name + " loses to " + ((Component)first).name);
									}
									if (outcome == Outcome.Blocked)
									{
										Count(blockers, ConeAngles[a] + " deg: " + hits[0].collider.name);
									}
								}
								perResiduo[i, a, o].Add(outcome);
								all[a, o].Add(outcome);
								if (layout == 0)
								{
									authoredOnly[a, o].Add(outcome);
								}
							}
						}
					}
					if (layout == 0)
					{
						Log("CONEPROBE " + residuos[i].name + " at " + Vector3.Distance(origin, centers[i]).ToString("F2") + " m, nearest neighbour " + nearest.ToString("F1") + " deg away");
					}
				}
			}
			for (int a = 0; a < ConeAngles.Length; a++)
			{
				for (int o = 0; o < OffsetDegrees.Length; o++)
				{
					Log("CONEPROBE cone " + ConeAngles[a] + " deg, aim " + OffsetDegrees[o] + " deg off centre: all layouts " + all[a, o] + " | authored " + authoredOnly[a, o]);
				}
			}
			nearestSeparations.Sort();
			Log("CONEPROBE nearest-neighbour separation over all layouts: min " + smallestSeparation.ToString("F1") + " median " + nearestSeparations[nearestSeparations.Count / 2].ToString("F1") + " max " + nearestSeparations[nearestSeparations.Count - 1].ToString("F1") + " deg");
			for (int i = 0; i < residuos.Count; i++)
			{
				for (int a = 0; a < ConeAngles.Length; a++)
				{
					string row = "CONEPROBE table " + residuos[i].name + " cone " + ConeAngles[a] + ", hit/stolen/blocked % at aim offset";
					for (int o = 0; o < OffsetDegrees.Length; o++)
					{
						Tally tally = perResiduo[i, a, o];
						row += "  " + OffsetDegrees[o] + ":" + (100 * tally.Counts[0] / tally.Total) + "/" + (100 * tally.Counts[1] / tally.Total) + "/" + (100 * tally.Counts[2] / tally.Total);
					}
					Log(row);
				}
			}
			foreach (KeyValuePair<string, int> pair in stealers)
			{
				Log("CONEPROBE stolen " + pair.Key + " x" + pair.Value);
			}
			foreach (KeyValuePair<string, int> pair in blockers)
			{
				Log("CONEPROBE blocked by " + pair.Key + " x" + pair.Value);
			}
		}
		finally
		{
			for (int i = 0; i < residuos.Count; i++)
			{
				residuos[i].transform.position = authored[i];
			}
			Physics.SyncTransforms();
			if (caster != null && caster.samplePoints.IsCreated)
			{
				caster.samplePoints.Dispose();
			}
			UnityEngine.Object.DestroyImmediate(temp);
		}
	}
}

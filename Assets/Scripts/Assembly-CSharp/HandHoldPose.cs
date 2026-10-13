using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

/// <summary>
/// Hand tracking: places a held object in the hand in a sensible pose, instead of wherever the default attach puts it.
///
///  - Elongated or cylindrical objects (bottles, cans, cartons, cup): long / symmetry axis along the grip axis of the
///    fist (the knuckle line, top towards the thumb), resting against the palm.
///  - Flat objects (sandwich, pizza box): lying on the palm; big ones are held by an edge, extending past the fingers.
///
/// The shape is deduced from the object's mesh bounds; <see cref="Overrides"/> can force it per object name.
/// The palm frame is built from XR Hands joints, mirrored for the left hand.
///
/// It is a grab transformer appended AFTER XRI's default one (see CustomGrabInteractable): it only overwrites the
/// target pose when the object is held by a tracked HAND interactor. Controller grabs keep the default pose, and
/// XRI's ease-in, movement type, throw on release and everything that happens after release are untouched.
/// </summary>
public class HandHoldPose : IXRGrabTransformer
{
	// ---- Tunables (metres unless noted) ----

	// Palm centre = wrist + this fraction of the way to the middle of the knuckle line.
	public const float PalmCentreAlongHand = 0.75f;
	// Distance from the joint plane (bone centres) to the palm skin: the object surface rests this far from the joints.
	public const float PalmSurfaceOffset = 0.018f;
	// Shift of the object centre along the grip axis (towards the thumb when positive).
	public const float GripAxisShift = 0f;
	// Two half-extents closer than this fraction are "equal" (the remaining axis is then the symmetry axis).
	public const float EqualAxesTolerance = 0.12f;
	// An axis shorter than this fraction of the others makes the object "flat".
	public const float FlatRatio = 0.5f;
	// Flat objects: how much of the object may lie behind the palm centre (towards the wrist). Bigger objects are
	// pushed forward so that the hand holds them near their edge.
	public const float FlatReachBehindPalm = 0.09f;
	// Seconds the last pose relative to nothing (frozen in place) is kept when hand tracking drops out.
	public const float HoldWhenUntrackedTime = 0.5f;

	public enum Shape
	{
		Auto,
		Elongated,   // gripAxis = long / symmetry axis
		Flat         // gripAxis = thin axis (the face that looks away from the palm)
	}

	public struct Override
	{
		public string nameContains;   // case-insensitive part of the GameObject name
		public Shape shape;
		public Vector3 axis;          // local, signed unit axis: Elongated = the end that points to the thumb; Flat = the face away from the palm
		public Vector3 extraOffset;   // extra offset in the palm frame: x = towards thumb, y = out of the palm, z = towards fingertips
		public float extraRollDeg;    // extra rotation around the grip axis (Elongated) or around the palm normal (Flat)

		public Override(string nameContains, Shape shape, Vector3 axis, Vector3 extraOffset, float extraRollDeg)
		{
			this.nameContains = nameContains;
			this.shape = shape;
			this.axis = axis;
			this.extraOffset = extraOffset;
			this.extraRollDeg = extraRollDeg;
		}
	}

	// Per-object corrections. Empty: the automatic classification already gives, for the 9 objects of NuevoJuego:
	//   Botella Marron, Agua, CajitaTomates: grip axis +Y | CajaJugo, LataRoja, VasitoPlasticoGrande: grip axis +Z
	//   panial: grip axis Y (thin side Z against the palm) | Sanguche, CajaPizza: flat, face +Z away from the palm.
	// Example: new Override("LataRoja", Shape.Elongated, Vector3.back, Vector3.zero, 0f)   // can upside down
	public static readonly Override[] Overrides =
	{
	};

	private bool analysed;
	private Shape shape;
	private Vector3 gripAxis;        // local, signed
	private Vector3 secondAxis;      // local: Elongated = axis that faces the palm normal; Flat = axis that goes along the fingers
	private Vector3 localCentre;     // bounds centre, local
	private Vector3 halfSize;        // bounds half-extents in world units, per local axis
	private Vector3 extraOffset;
	private float extraRollDeg;

	private bool hasLast;
	private Pose lastPose;
	private float lastTime;

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
		hasLast = false;
	}

	public void OnGrabCountChanged(XRGrabInteractable grabInteractable, Pose targetPose, Vector3 localScale)
	{
		hasLast = false;
	}

	public void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
	{
		if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic && updatePhase != XRInteractionUpdateOrder.UpdatePhase.OnBeforeRender)
		{
			return;
		}
		if (grabInteractable.interactorsSelecting.Count != 1)
		{
			return;
		}
		Component interactor = grabInteractable.interactorsSelecting[0] as Component;
		HandCloseGrab hand = interactor != null ? interactor.GetComponent<HandCloseGrab>() : null;
		if (hand == null || !hand.isActiveAndEnabled)
		{
			return;   // controller (or any non-hand interactor): default pose
		}

		Vector3 wrist, index, middle, little;
		if (!hand.TryGetPalmJoints(out wrist, out index, out middle, out little))
		{
			// Tracking lost for a moment: stay where we were instead of jumping to the default attach pose.
			if (hasLast && Time.unscaledTime - lastTime <= HoldWhenUntrackedTime)
			{
				targetPose = lastPose;
			}
			return;
		}

		// Palm frame. fingers = wrist -> knuckles; thumbSide = little knuckle -> index knuckle (the grip axis of the
		// fist); normal = out of the palm. The cross product order is what mirrors it for the left hand.
		Vector3 fingers = middle - wrist;
		Vector3 thumbSide = index - little;
		if (fingers.sqrMagnitude < 1e-6f || thumbSide.sqrMagnitude < 1e-6f)
		{
			return;
		}
		fingers.Normalize();
		thumbSide = thumbSide - fingers * Vector3.Dot(thumbSide, fingers);
		if (thumbSide.sqrMagnitude < 1e-8f)
		{
			return;
		}
		thumbSide.Normalize();
		Vector3 normal = hand.IsLeft ? Vector3.Cross(thumbSide, fingers) : Vector3.Cross(fingers, thumbSide);
		Vector3 palmCentre = wrist + ((index + little) * 0.5f - wrist) * PalmCentreAlongHand;

		if (!analysed)
		{
			Analyse(grabInteractable.transform);
		}

		Quaternion rotation;
		Vector3 centre;
		if (shape == Shape.Flat)
		{
			// Face away from the palm -> palm normal; in-plane axis -> fingers.
			rotation = Quaternion.AngleAxis(extraRollDeg, normal) *
				Quaternion.LookRotation(normal, fingers) * Quaternion.Inverse(Quaternion.LookRotation(gripAxis, secondAxis));
			float thin = Along(halfSize, gripAxis);
			float alongFingers = Along(halfSize, secondAxis);
			centre = palmCentre + normal * (thin + PalmSurfaceOffset) + fingers * Mathf.Max(0f, alongFingers - FlatReachBehindPalm);
		}
		else
		{
			// Grip axis -> knuckle line (top to the thumb); the thinner side faces the palm.
			rotation = Quaternion.AngleAxis(extraRollDeg, thumbSide) *
				Quaternion.LookRotation(normal, thumbSide) * Quaternion.Inverse(Quaternion.LookRotation(secondAxis, gripAxis));
			float radius = Along(halfSize, secondAxis);
			centre = palmCentre + normal * (radius + PalmSurfaceOffset) + thumbSide * GripAxisShift;
		}
		centre += thumbSide * extraOffset.x + normal * extraOffset.y + fingers * extraOffset.z;

		Transform t = grabInteractable.transform;
		Vector3 centreFromPivot = rotation * Vector3.Scale(localCentre, t.lossyScale);
		targetPose = new Pose(centre - centreFromPivot, rotation);
		lastPose = targetPose;
		lastTime = Time.unscaledTime;
		hasLast = true;
	}

	private static float Along(Vector3 size, Vector3 axis)
	{
		return Mathf.Abs(axis.x) * size.x + Mathf.Abs(axis.y) * size.y + Mathf.Abs(axis.z) * size.z;
	}

	// ---------------------------------------------------------------- shape of the object, from its mesh bounds

	private void Analyse(Transform root)
	{
		analysed = true;
		Bounds local;
		if (!TryGetLocalBounds(root, out local))
		{
			local = new Bounds(Vector3.zero, Vector3.one * 0.1f);
		}
		Vector3 scale = root.lossyScale;
		localCentre = local.center;
		halfSize = new Vector3(Mathf.Abs(local.extents.x * scale.x), Mathf.Abs(local.extents.y * scale.y), Mathf.Abs(local.extents.z * scale.z));
		float[] h = { halfSize.x, halfSize.y, halfSize.z };

		// The most similar pair of axes; if similar enough, the remaining axis is the symmetry axis.
		int sym = -1;
		float best = float.MaxValue;
		for (int a = 0; a < 3; a++)
		{
			int b = (a + 1) % 3;
			int c = (a + 2) % 3;
			float diff = Mathf.Abs(h[b] - h[c]) / Mathf.Max(h[b], h[c], 1e-5f);
			if (diff < best)
			{
				best = diff;
				sym = a;
			}
		}
		int longest = 0, shortest = 0;
		for (int a = 1; a < 3; a++)
		{
			if (h[a] > h[longest]) longest = a;
			if (h[a] < h[shortest]) shortest = a;
		}
		int mid = 3 - longest - shortest;
		if (longest == shortest)
		{
			mid = 1;
		}

		int grip;
		if (best <= EqualAxesTolerance)
		{
			float others = Mathf.Max(h[(sym + 1) % 3], h[(sym + 2) % 3]);
			shape = h[sym] < FlatRatio * others ? Shape.Flat : Shape.Elongated;
			grip = sym;
		}
		else if (h[shortest] < FlatRatio * h[mid])
		{
			shape = Shape.Flat;
			grip = shortest;
		}
		else
		{
			shape = Shape.Elongated;
			grip = longest;
		}

		// Sign: the pivot of these models is at their base / back, so the bounds centre lies towards the "top".
		float sign = localCentre[grip] < -1e-4f * Mathf.Max(1f, Mathf.Abs(local.extents[grip])) ? -1f : 1f;
		gripAxis = AxisVector(grip) * sign;
		extraOffset = Vector3.zero;
		extraRollDeg = 0f;

		string name = root.name;
		for (int i = 0; i < Overrides.Length; i++)
		{
			Override o = Overrides[i];
			if (string.IsNullOrEmpty(o.nameContains) || name.IndexOf(o.nameContains, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			if (o.shape != Shape.Auto)
			{
				shape = o.shape;
			}
			if (o.axis.sqrMagnitude > 0.5f)
			{
				grip = DominantAxis(o.axis);
				gripAxis = AxisVector(grip) * Mathf.Sign(o.axis[grip]);
			}
			extraOffset = o.extraOffset;
			extraRollDeg = o.extraRollDeg;
			break;
		}

		// Second axis: Elongated = the thinner of the two remaining (it faces the palm); Flat = the longer one (along the fingers).
		int p = (grip + 1) % 3;
		int q = (grip + 2) % 3;
		bool pick = shape == Shape.Flat ? h[p] >= h[q] : h[p] <= h[q];
		secondAxis = AxisVector(pick ? p : q);
	}

	private static Vector3 AxisVector(int axis)
	{
		return axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
	}

	private static int DominantAxis(Vector3 v)
	{
		float x = Mathf.Abs(v.x), y = Mathf.Abs(v.y), z = Mathf.Abs(v.z);
		return x >= y && x >= z ? 0 : (y >= z ? 1 : 2);
	}

	private static bool TryGetLocalBounds(Transform root, out Bounds bounds)
	{
		bounds = new Bounds();
		bool any = false;
		Matrix4x4 toRoot = root.worldToLocalMatrix;
		MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
		foreach (MeshFilter filter in filters)
		{
			if (filter.sharedMesh == null)
			{
				continue;
			}
			Encapsulate(ref bounds, ref any, toRoot * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds);
		}
		if (!any)
		{
			foreach (Collider collider in root.GetComponentsInChildren<Collider>())
			{
				if (collider.isTrigger)
				{
					continue;
				}
				Encapsulate(ref bounds, ref any, toRoot, collider.bounds);
			}
		}
		return any;
	}

	private static void Encapsulate(ref Bounds bounds, ref bool any, Matrix4x4 matrix, Bounds source)
	{
		Vector3 c = source.center;
		Vector3 e = source.extents;
		for (int i = 0; i < 8; i++)
		{
			Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
			Vector3 point = matrix.MultiplyPoint3x4(corner);
			if (!any)
			{
				bounds = new Bounds(point, Vector3.zero);
				any = true;
			}
			else
			{
				bounds.Encapsulate(point);
			}
		}
	}
}

// Screenshot harness for the particle feedback (FeedbackFX: hand beam, sparkles, container fireworks, wrong puff).
//
// Opens Assets/Scenes/NuevoJuego.unity (never saves it), builds the effects with the real FeedbackFX code, advances them
// with ParticleSystem.Simulate and renders them with a temporary URP camera at the player's eye, "eyebuf" density
// (1832x1920, vertical FOV 100 deg, like the Quest 3 default eye buffer), plus enlarged crops and a contact sheet.
//
// Batch: Unity.exe -batchmode -projectPath <project> -executeMethod ParticleSnapshot.RunAll [-fxOut <dir>] [-fxTag <tag>]
//        -logFile <log> -quit
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class ParticleSnapshot
{
	private const string ScenePath = "Assets/Scenes/NuevoJuego.unity";
	private const string DefaultOutDir = @"C:\Users\Admin\AppData\Local\Temp\claude\G--SeparaVRecover\15853079-1b61-4d7e-9168-99726887a113\scratchpad\fx";
	private static readonly string[] PreferredGrabbables = { "CajaJugo", "Agua" };
	private const float EyeHeight = 1.6f;
	private static readonly Vector3 FallbackEyeFloor = new Vector3(-8.09f, 0f, 8.56f);
	private const int Width = 1832;
	private const int Height = 1920;
	private const float VerticalFov = 100f;
	// Right hand relative to the eye: right, down, forward (metres).
	private static readonly Vector3 HandOffset = new Vector3(0.2f, -0.38f, 0.3f);
	// Fireworks frames (seconds after the deposit): early, peak, fading.
	private static readonly float[] FireworksTimes = { 0.15f, 0.5f, 1.1f };

	[MenuItem("Tools/SeparaVR/Particle snapshot (PNG)")]
	private static void RunFromMenu()
	{
		if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
		{
			return;
		}
		RunAll();
	}

	public static void RunAll()
	{
		string outDir = Arg("-fxOut", DefaultOutDir);
		string tag = Arg("-fxTag", "fx");
		Directory.CreateDirectory(outDir);
		if (SceneManager.GetActiveScene().path != ScenePath)
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		}
		Camera sceneCamera = FindSceneCamera();
		Vector3 eye = FindEye(sceneCamera);
		Log("eye " + eye.ToString("F3") + ", out " + outDir + ", material " + (FeedbackFX.Material != null));

		List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
		RigPose rig = null;
		List<Texture2D> tiles = new List<Texture2D>();
		List<string> names = new List<string>();
		GameObject cameraObject = new GameObject("ParticleSnapshotCamera");
		cameraObject.hideFlags = HideFlags.DontSave;
		temporary.Add(cameraObject);
		try
		{
			Camera camera = MakeCamera(cameraObject, sceneCamera);

			// ---- 1. player view: beam from the right hand to a residuo, with its sparkles ----
			CustomGrabInteractable grab = PickGrabbable(eye);
			if (grab == null)
			{
				Log("no grabbable found");
			}
			else
			{
				Bounds bounds = JointBounds(grab.gameObject);
				Log("grabbable " + grab.name + " at " + bounds.center.ToString("F2") + ", distance " + Vector3.Distance(eye, bounds.center).ToString("F2") + " m, size " + bounds.size.ToString("F3"));
				camera.transform.position = eye;
				camera.transform.LookAt(Vector3.Lerp(eye, bounds.center, 0.75f));
				Vector3 aimPoint = eye + camera.transform.right * HandOffset.x + Vector3.up * HandOffset.y + Flat(camera.transform.forward) * HandOffset.z;

				// The rig's own right hand, posed like a tracked hand pointing at the object: its Aim Pose (the cast origin
				// XRI uses) at aimPoint, pointing at the object; the hand mesh around it (index knuckle at the aim
				// origin, fingers along the ray). The rest of the rig (other hand, controllers, pinch / poke visuals) is
				// hidden: in edit mode it all sits at the rig's rest pose, i.e. inside the camera.
				rig = PoseRightHand(aimPoint, bounds.center);
				Vector3 origin = rig.AimOrigin;
				Vector3 dir = rig.AimDirection;
				float distance = Vector3.Distance(origin, bounds.center);
				Log("aim origin " + origin.ToString("F3") + " dir " + dir.ToString("F2") + " from '" + rig.Source + "', distance " + distance.ToString("F2"));

				ParticleSystem beam = FeedbackFX.CreateBeam(null);
				temporary.Add(beam.gameObject);
				LineRenderer core = FeedbackFX.BeamCore(beam);
				FeedbackFX.PlaceBeam(beam, core, origin, dir, Mathf.Min(distance, FeedbackFX.BeamMaxLength), 1f);

				ParticleSystem sparkles = FeedbackFX.CreateSparkles(grab.name);
				temporary.Add(sparkles.gameObject);
				FeedbackFX.FollowSparkles(sparkles, grab.GetComponentsInChildren<MeshRenderer>());

				beam.Simulate(2f, true, true);
				sparkles.Simulate(2f, true, true);
				Log("beam particles " + beam.particleCount + ", sparkles " + sparkles.particleCount + ", core " + (core != null && core.enabled));

				Texture2D full = Render(camera);
				Save(full, Path.Combine(outDir, tag + "_player_beam_full.png"));
				// Crop: from the hand to a bit above the object.
				Bounds view = bounds;
				view.Encapsulate(origin);
				view.Encapsulate(rig.HandBounds);
				view.Encapsulate(bounds.center + Vector3.up * 0.3f);
				Texture2D crop = Crop(full, CropRect(camera, view), 1200);
				Save(crop, Path.Combine(outDir, tag + "_player_beam_crop.png"));
				Add(tiles, names, crop, "player view: beam + hover sparkles");
				// The beam leaving the hand, same frame: the hand and the first ~0.8 m of the ray.
				Bounds near = rig.HandBounds;
				near.Encapsulate(origin + dir * 0.8f);
				Texture2D handCrop = Crop(full, CropRect(camera, near), 1200);
				Save(handCrop, Path.Combine(outDir, tag + "_beam_hand_crop.png"));
				Add(tiles, names, handCrop, "beam leaving the hand (player view)");
				UnityEngine.Object.DestroyImmediate(full);

				// Side close-up of the beam leaving the hand (a second camera position, ~0.45 m to the right of the ray).
				Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
				camera.transform.position = origin + side * 0.42f + Vector3.up * 0.08f + dir * 0.1f;
				camera.transform.LookAt(origin + dir * 0.3f);
				Texture2D sideShot = Render(camera);
				Bounds sideView = rig.HandBounds;
				sideView.Encapsulate(origin + dir * 0.75f);
				Texture2D sideCrop = Crop(sideShot, CropRect(camera, sideView), 1200);
				Save(sideCrop, Path.Combine(outDir, tag + "_beam_hand_side.png"));
				Add(tiles, names, sideCrop, "beam leaving the hand (side)");
				UnityEngine.Object.DestroyImmediate(sideShot);
				rig.HideHand();   // the later shots are about the object / containers
				// Same player frame without the hand mesh (tells the hand's own pixels from the scene's).
				camera.transform.position = eye;
				camera.transform.LookAt(Vector3.Lerp(eye, bounds.center, 0.75f));
				Texture2D bare = Render(camera);
				Texture2D bareCrop = Crop(bare, CropRect(camera, view), 1200);
				Save(bareCrop, Path.Combine(outDir, tag + "_player_beam_nohand.png"));
				UnityEngine.Object.DestroyImmediate(bare);

				// ---- 2. close-up of the object with its sparkles (about 0.8 m away, a bit from above) ----
				beam.gameObject.SetActive(false);
				Vector3 toEye = Flat(eye - bounds.center).normalized;
				camera.transform.position = bounds.center + toEye * 0.75f + Vector3.up * 0.12f;
				camera.transform.LookAt(bounds.center + Vector3.up * 0.12f);
				sparkles.Simulate(2f, true, true);
				Texture2D close = Render(camera);
				Bounds rise = bounds;
				rise.Encapsulate(bounds.center + Vector3.up * (bounds.extents.y + 0.3f));
				rise.Expand(0.08f);
				Texture2D closeCrop = Crop(close, CropRect(camera, rise), 1200);
				Save(closeCrop, Path.Combine(outDir, tag + "_sparkles_closeup.png"));
				Add(tiles, names, closeCrop, "sparkles close-up");
				UnityEngine.Object.DestroyImmediate(close);

				// Held: fewer sparkles.
				FeedbackFX.SetSparkleRate(sparkles, FeedbackFX.SparkleRateHeld);
				sparkles.Simulate(2f, true, true);
				Texture2D held = Render(camera);
				Texture2D heldCrop = Crop(held, CropRect(camera, rise), 1200);
				Save(heldCrop, Path.Combine(outDir, tag + "_sparkles_held.png"));
				Add(tiles, names, heldCrop, "sparkles while held");
				UnityEngine.Object.DestroyImmediate(held);
				sparkles.gameObject.SetActive(false);

				// ---- wrong puff ----
				ParticleSystem puff = FeedbackFX.CreatePuff();
				temporary.Add(puff.gameObject);
				puff.transform.position = bounds.center + Vector3.up * 0.1f;
				puff.Simulate(0f, true, true);
				puff.Emit(FeedbackFX.PuffCount);
				puff.Simulate(0.25f, true, false);
				Texture2D wrong = Render(camera);
				Texture2D wrongCrop = Crop(wrong, CropRect(camera, rise), 1200);
				Save(wrongCrop, Path.Combine(outDir, tag + "_wrong_puff.png"));
				Add(tiles, names, wrongCrop, "wrong container puff (0.25 s)");
				UnityEngine.Object.DestroyImmediate(wrong);
				puff.gameObject.SetActive(false);
			}

			// ---- 3. fireworks over each container, from the player's eye ----
			List<Container> containers = new List<Container>(UnityEngine.Object.FindObjectsByType<Container>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
			containers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			foreach (Container container in containers)
			{
				Vector3 point = FeedbackFX.FireworksPoint(container);
				Color color = container.HighlightColor;
				Log("container " + container.name + " (" + container.descripcion + ") colour " + color + ", fireworks at " + point.ToString("F2") + ", distance " + Vector3.Distance(eye, point).ToString("F2") + " m");
				ParticleSystem root = FeedbackFX.CreateFireworks(color);
				temporary.Add(root.gameObject);
				Vector3 toViewer = Flat(eye - point);
				root.transform.SetPositionAndRotation(point, Quaternion.LookRotation(-toViewer.normalized, Vector3.up));
				camera.transform.position = eye;
				camera.transform.LookAt(point + Vector3.up * 0.6f);
				Bounds area = new Bounds(point + Vector3.up * 0.8f, new Vector3(3f, 2.6f, 3f));
				string colorName = color == Container.OutlineOrange ? "orange" : (color == Container.OutlineGreen ? "green" : "other");
				foreach (float t in FireworksTimes)
				{
					root.Simulate(t, true, true);
					int count = 0;
					foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>())
					{
						count += ps.particleCount;
					}
					Texture2D frame = Render(camera);
					string label = colorName + "_" + Mathf.RoundToInt(t * 1000f) + "ms";
					if (Mathf.Approximately(t, FireworksTimes[1]))
					{
						Save(frame, Path.Combine(outDir, tag + "_fireworks_" + label + "_full.png"));
					}
					Texture2D frameCrop = Crop(frame, CropRect(camera, area), 1200);
					Save(frameCrop, Path.Combine(outDir, tag + "_fireworks_" + label + ".png"));
					Add(tiles, names, frameCrop, "fireworks " + colorName + " " + t.ToString("F2") + " s (" + count + " particles)");
					Log("fireworks " + label + ": " + count + " particles");
					UnityEngine.Object.DestroyImmediate(frame);
				}
				root.gameObject.SetActive(false);
			}
		}
		catch (Exception e)
		{
			Debug.LogError("[ParticleSnapshot] EXCEPTION: " + e);
		}
		finally
		{
			if (rig != null)
			{
				rig.Restore();
			}
			foreach (UnityEngine.Object o in temporary)
			{
				if (o != null)
				{
					UnityEngine.Object.DestroyImmediate(o);
				}
			}
		}

		if (tiles.Count > 0)
		{
			Save(ContactSheet(tiles, names), Path.Combine(outDir, tag + "_contactsheet.png"));
		}
		Log("RunAll done");
	}

	/// <summary>Logs every line-like renderer and every Near-Far Interactor of the scene (what draws a ray, with what).</summary>
	public static void Diagnose()
	{
		if (SceneManager.GetActiveScene().path != ScenePath)
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		}
		foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			if (r is MeshRenderer || r is SkinnedMeshRenderer || r is ParticleSystemRenderer)
			{
				continue;
			}
			string info = "RENDERER " + r.GetType().Name + " '" + PathOf(r.transform) + "' enabled=" + r.enabled + " active=" + r.gameObject.activeInHierarchy
				+ " bounds=" + r.bounds.center.ToString("F2") + "/" + r.bounds.size.ToString("F2");
			foreach (Material m in r.sharedMaterials)
			{
				info += " mat=" + (m == null ? "NULL" : m.name + " shader=" + (m.shader != null ? m.shader.name : "null") + " supported=" + (m.shader != null && m.shader.isSupported) + " tex=" + (m.mainTexture != null ? m.mainTexture.name : "none"));
			}
			LineRenderer line = r as LineRenderer;
			if (line != null)
			{
				info += " points=" + line.positionCount + " world=" + line.useWorldSpace + " width=" + line.startWidth.ToString("F3") + ".." + line.endWidth.ToString("F3") + " mult=" + line.widthMultiplier.ToString("F3") + " texMode=" + line.textureMode;
				if (line.positionCount > 0)
				{
					info += " p0=" + line.GetPosition(0).ToString("F2") + " pN=" + line.GetPosition(line.positionCount - 1).ToString("F2");
				}
			}
			foreach (MonoBehaviour mb in r.GetComponents<MonoBehaviour>())
			{
				info += " +" + (mb != null ? mb.GetType().Name : "missing-script");
			}
			Log(info);
		}
		foreach (SkinnedMeshRenderer s in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			string info = "SKINNED '" + PathOf(s.transform) + "' enabled=" + s.enabled + " active=" + s.gameObject.activeInHierarchy + " bounds=" + s.bounds.center.ToString("F2") + "/" + s.bounds.size.ToString("F2")
				+ " mesh=" + (s.sharedMesh != null ? s.sharedMesh.name : "null") + " rootBone=" + (s.rootBone != null ? s.rootBone.name + " " + s.rootBone.position.ToString("F2") : "null");
			foreach (Material m in s.sharedMaterials)
			{
				info += " mat=" + (m == null ? "NULL" : m.name + " shader=" + (m.shader != null ? m.shader.name : "null"));
			}
			Transform[] bones = s.bones;
			Vector3 reference = s.rootBone != null ? s.rootBone.position : s.transform.position;
			float far = 0f;
			string farName = "-";
			int nullBones = 0;
			foreach (Transform b in bones)
			{
				if (b == null)
				{
					nullBones++;
					continue;
				}
				float d = Vector3.Distance(b.position, reference);
				if (d > far)
				{
					far = d;
					farName = PathOf(b) + " " + b.position.ToString("F2");
				}
			}
			info += " bones=" + bones.Length + " nullBones=" + nullBones + " farthestBone=" + far.ToString("F2") + "m " + farName;
			Log(info);
		}
		foreach (UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor nf in UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			Transform origin = nf.farInteractionCaster != null ? nf.farInteractionCaster.castOrigin : null;
			Log("NEARFAR '" + PathOf(nf.transform) + "' handedness=" + nf.handedness + " pos=" + nf.transform.position.ToString("F3") + " castOrigin=" + (origin != null ? PathOf(origin) + " " + origin.position.ToString("F3") + " fwd " + origin.forward.ToString("F2") : "null"));
		}
		Log("Diagnose done");
	}

	/// <summary>The XR rig's right hand moved for a shot (and the rest of the rig hidden); Restore puts everything back.</summary>
	private sealed class RigPose
	{
		public Vector3 AimOrigin;
		public Vector3 AimDirection;
		public Bounds HandBounds;
		public string Source = "harness (no rig hand found)";
		public Transform Hand;
		public Vector3 HandPosition;
		public Quaternion HandRotation;
		public readonly List<Renderer> Shown = new List<Renderer>();
		public readonly List<KeyValuePair<Renderer, bool>> Saved = new List<KeyValuePair<Renderer, bool>>();

		public void HideHand()
		{
			foreach (Renderer r in Shown)
			{
				if (r != null)
				{
					r.enabled = false;
				}
			}
		}

		public void Restore()
		{
			foreach (KeyValuePair<Renderer, bool> pair in Saved)
			{
				if (pair.Key != null)
				{
					pair.Key.enabled = pair.Value;
				}
			}
			if (Hand != null)
			{
				Hand.SetPositionAndRotation(HandPosition, HandRotation);
			}
		}
	}

	/// <summary>
	/// Poses the rig's right hand (the "Right Hand" child of Camera Offset, holding the hand Near-Far Interactor and its
	/// Aim Pose) so that the interactor's cast origin is at aimPoint looking at target: the whole hand transform is moved
	/// by the same rigid delta, so the hand mesh keeps its relation to the ray origin. Every other renderer of the rig
	/// (other hand, controllers, pinch / poke visuals, lines) is hidden.
	/// </summary>
	private static RigPose PoseRightHand(Vector3 aimPoint, Vector3 target)
	{
		RigPose pose = new RigPose();
		Vector3 toTarget = target - aimPoint;
		Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
		pose.AimOrigin = aimPoint;
		pose.AimDirection = toTarget.normalized;
		pose.HandBounds = new Bounds(aimPoint, Vector3.one * 0.15f);

		UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor chosen = null;
		foreach (UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor nf in UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			string path = PathOf(nf.transform);
			if (path.Contains("Right Hand") && !path.Contains("Controller"))
			{
				chosen = nf;
				break;
			}
		}
		if (chosen == null)
		{
			return pose;
		}
		Transform aim = chosen.farInteractionCaster != null && chosen.farInteractionCaster.castOrigin != null ? chosen.farInteractionCaster.castOrigin : chosen.transform;
		Transform hand = chosen.transform;
		while (hand.parent != null && hand.parent.name != "Camera Offset")
		{
			hand = hand.parent;
		}
		Transform rigRoot = hand.root;
		if (hand.parent == null || !aim.IsChildOf(hand))
		{
			Log("rig hand: unexpected hierarchy at " + PathOf(chosen.transform));
			return pose;
		}

		foreach (Renderer r in rigRoot.GetComponentsInChildren<Renderer>(true))
		{
			pose.Saved.Add(new KeyValuePair<Renderer, bool>(r, r.enabled));
			bool handMesh = r is SkinnedMeshRenderer && r.transform.IsChildOf(hand) && !PathOf(r.transform).Contains("Pinch");
			r.enabled = handMesh;
			if (handMesh)
			{
				pose.Shown.Add(r);
			}
		}

		pose.Hand = hand;
		pose.HandPosition = hand.position;
		pose.HandRotation = hand.rotation;
		Quaternion delta = desired * Quaternion.Inverse(aim.rotation);
		Vector3 newPosition = aimPoint + delta * (hand.position - aim.position);
		hand.SetPositionAndRotation(newPosition, delta * hand.rotation);

		pose.AimOrigin = aim.position;
		pose.AimDirection = aim.forward;
		pose.Source = PathOf(aim);
		bool any = false;
		foreach (Renderer r in pose.Shown)
		{
			if (!r.gameObject.activeInHierarchy)
			{
				continue;
			}
			if (!any)
			{
				pose.HandBounds = r.bounds;
				any = true;
			}
			else
			{
				pose.HandBounds.Encapsulate(r.bounds);
			}
			SkinnedMeshRenderer skinned = (SkinnedMeshRenderer)r;
			int outside = 0;
			string outsideNames = "";
			foreach (Transform b in skinned.bones)
			{
				if (b != null && !b.IsChildOf(hand))
				{
					outside++;
					if (outside <= 4)
					{
						outsideNames += " " + PathOf(b) + "@" + b.position.ToString("F2");
					}
				}
			}
			string mats = "";
			foreach (Material m in skinned.sharedMaterials)
			{
				mats += " " + (m != null ? m.name + "(" + (m.shader != null ? m.shader.name : "null") + " q" + m.renderQueue + ")" : "NULL");
			}
			Log("rig hand mesh '" + PathOf(r.transform) + "' bounds " + r.bounds.center.ToString("F3") + "/" + r.bounds.size.ToString("F3") + " bones " + skinned.bones.Length + " outside hand " + outside + outsideNames + " root " + (skinned.rootBone != null ? PathOf(skinned.rootBone) : "null") + " mats" + mats);
		}
		if (!any)
		{
			pose.HandBounds = new Bounds(pose.AimOrigin, Vector3.one * 0.15f);
		}
		Log("rig hand moved: '" + PathOf(hand) + "', aim offset from hand root " + (hand.position - pose.AimOrigin).ToString("F3") + ", hidden renderers " + (pose.Saved.Count - pose.Shown.Count));
		return pose;
	}

	/// <summary>Logs the mesh renderers on the floor around the player (what could draw floor-textured bands).</summary>
	public static void DiagnoseFloor()
	{
		if (SceneManager.GetActiveScene().path != ScenePath)
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		}
		Vector3 eye = FindEye(FindSceneCamera());
		Bounds area = new Bounds(new Vector3(eye.x + 1.5f, 0.3f, eye.z + 1f), new Vector3(5f, 1.2f, 5f));
		foreach (MeshRenderer r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
		{
			if (!r.enabled || !r.bounds.Intersects(area))
			{
				continue;
			}
			MeshFilter f = r.GetComponent<MeshFilter>();
			string info = "FLOOR '" + PathOf(r.transform) + "' bounds=" + r.bounds.center.ToString("F2") + "/" + r.bounds.size.ToString("F2")
				+ " mesh=" + (f != null && f.sharedMesh != null ? f.sharedMesh.name + " v" + f.sharedMesh.vertexCount : "null") + " static=" + r.gameObject.isStatic + " layer=" + LayerMask.LayerToName(r.gameObject.layer);
			foreach (Material m in r.sharedMaterials)
			{
				info += " mat=" + (m == null ? "NULL" : m.name + " shader=" + (m.shader != null ? m.shader.name : "null") + " queue=" + m.renderQueue + " tex=" + (m.mainTexture != null ? m.mainTexture.name : "none"));
			}
			Log(info);
		}
		foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
		{
			if (r is MeshRenderer || r is ParticleSystemRenderer || !r.enabled)
			{
				continue;
			}
			Log("OTHER " + r.GetType().Name + " '" + PathOf(r.transform) + "' bounds=" + r.bounds.center.ToString("F2") + "/" + r.bounds.size.ToString("F2") + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name : "null"));
		}
		Log("DiagnoseFloor done");
	}

	/// <summary>Finds which renderers change the player frame (one render per visible renderer, switched off in turn).</summary>
	public static void DiagnoseBand()
	{
		if (SceneManager.GetActiveScene().path != ScenePath)
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		}
		Camera sceneCamera = FindSceneCamera();
		Vector3 eye = FindEye(sceneCamera);
		GameObject cameraObject = new GameObject("ParticleSnapshotCamera");
		cameraObject.hideFlags = HideFlags.DontSave;
		RigPose rig = null;
		try
		{
			Camera camera = MakeCamera(cameraObject, sceneCamera);
			CustomGrabInteractable grab = PickGrabbable(eye);
			Bounds bounds = JointBounds(grab.gameObject);
			camera.transform.position = eye;
			camera.transform.LookAt(Vector3.Lerp(eye, bounds.center, 0.75f));
			rig = PoseRightHand(eye, bounds.center);
			rig.HideHand();
			Texture2D baseline = Render(camera);
			Color32[] basePixels = baseline.GetPixels32();
			Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
			List<KeyValuePair<int, string>> results = new List<KeyValuePair<int, string>>();
			foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
			{
				if (!r.enabled || r is ParticleSystemRenderer || !GeometryUtility.TestPlanesAABB(planes, r.bounds))
				{
					continue;
				}
				r.enabled = false;
				Texture2D test = Render(camera);
				r.enabled = true;
				Color32[] pixels = test.GetPixels32();
				int changed = 0;
				int lower = basePixels.Length * 6 / 10;   // rows from the bottom: the lower 60% of the frame
				for (int i = 0; i < lower; i++)
				{
					int d = Mathf.Abs(pixels[i].r - basePixels[i].r) + Mathf.Abs(pixels[i].g - basePixels[i].g) + Mathf.Abs(pixels[i].b - basePixels[i].b);
					if (d > 30)
					{
						changed++;
					}
				}
				UnityEngine.Object.DestroyImmediate(test);
				string mat = r.sharedMaterial != null ? r.sharedMaterial.name + "(" + (r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "null") + ")" : "null";
				MeshFilter f = r.GetComponent<MeshFilter>();
				results.Add(new KeyValuePair<int, string>(changed, r.GetType().Name + " '" + PathOf(r.transform) + "' bounds=" + r.bounds.center.ToString("F2") + "/" + r.bounds.size.ToString("F2") + " mesh=" + (f != null && f.sharedMesh != null ? f.sharedMesh.name : "-") + " mat=" + mat));
			}
			results.Sort((a, b) => b.Key.CompareTo(a.Key));
			for (int i = 0; i < results.Count && i < 25; i++)
			{
				Log("BAND " + results[i].Key + " px: " + results[i].Value);
			}
			Log("tested " + results.Count + " renderers");
		}
		catch (Exception e)
		{
			Debug.LogError("[ParticleSnapshot] EXCEPTION: " + e);
		}
		finally
		{
			if (rig != null)
			{
				rig.Restore();
			}
			UnityEngine.Object.DestroyImmediate(cameraObject);
		}
		Log("DiagnoseBand done");
	}

	/// <summary>Renders the player frame with the floor's lightmap / the shadows / the floor shader switched off in turn.</summary>
	public static void DiagnoseBandVariants()
	{
		string outDir = Arg("-fxOut", DefaultOutDir);
		if (SceneManager.GetActiveScene().path != ScenePath)
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		}
		Camera sceneCamera = FindSceneCamera();
		Vector3 eye = FindEye(sceneCamera);
		GameObject cameraObject = new GameObject("ParticleSnapshotCamera");
		cameraObject.hideFlags = HideFlags.DontSave;
		RigPose rig = null;
		try
		{
			Camera camera = MakeCamera(cameraObject, sceneCamera);
			CustomGrabInteractable grab = PickGrabbable(eye);
			Bounds bounds = JointBounds(grab.gameObject);
			camera.transform.position = eye;
			camera.transform.LookAt(Vector3.Lerp(eye, bounds.center, 0.75f));
			rig = PoseRightHand(eye, bounds.center);
			rig.HideHand();
			List<MeshRenderer> floor = new List<MeshRenderer>();
			foreach (MeshRenderer r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
			{
				if (r.name.StartsWith("veredaOK") && Vector3.Distance(r.bounds.center, eye) < 12f)
				{
					floor.Add(r);
					Log("floor " + r.name + " lightmapIndex " + r.lightmapIndex + " scaleOffset " + r.lightmapScaleOffset + " realtime " + r.realtimeLightmapIndex);
				}
			}
			Log("lightmaps " + LightmapSettings.lightmaps.Length + " mode " + LightmapSettings.lightmapsMode);
			System.Action<string> shot = (string label) =>
			{
				Texture2D image = Render(camera);
				Save(image, Path.Combine(outDir, "band_" + label + ".png"));
				UnityEngine.Object.DestroyImmediate(image);
			};
			shot("a_baseline");
			List<int> indices = new List<int>();
			foreach (MeshRenderer r in floor) { indices.Add(r.lightmapIndex); r.lightmapIndex = 65535; }
			shot("b_floor_no_lightmap");
			for (int i = 0; i < floor.Count; i++) { floor[i].lightmapIndex = indices[i]; }
			List<KeyValuePair<Light, LightShadows>> lights = new List<KeyValuePair<Light, LightShadows>>();
			foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
			{
				Log("light " + l.name + " type " + l.type + " shadows " + l.shadows + " mode " + l.lightmapBakeType + " cookie " + (l.cookie != null ? l.cookie.name : "none"));
				lights.Add(new KeyValuePair<Light, LightShadows>(l, l.shadows));
				l.shadows = LightShadows.None;
			}
			shot("c_no_shadows");
			foreach (KeyValuePair<Light, LightShadows> pair in lights) { pair.Key.shadows = pair.Value; }
			Shader lit = Shader.Find("Universal Render Pipeline/Lit");
			List<Material> originals = new List<Material>();
			foreach (MeshRenderer r in floor)
			{
				originals.Add(r.sharedMaterial);
				Material m = new Material(lit);
				m.SetTexture("_BaseMap", r.sharedMaterial.mainTexture);
				m.mainTextureScale = r.sharedMaterial.mainTextureScale;
				r.sharedMaterial = m;
			}
			shot("d_floor_urp_lit");
			for (int i = 0; i < floor.Count; i++) { UnityEngine.Object.DestroyImmediate(floor[i].sharedMaterial); floor[i].sharedMaterial = originals[i]; }
			List<Renderer> off = new List<Renderer>();
			foreach (LineRenderer l in UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
			{
				if (l.enabled)
				{
					Log("line on: '" + PathOf(l.transform) + "' points " + l.positionCount + " world " + l.useWorldSpace + " width " + l.widthMultiplier + " mat " + (l.sharedMaterial != null ? l.sharedMaterial.name + "/" + l.sharedMaterial.shader.name + " tex " + (l.sharedMaterial.mainTexture != null ? l.sharedMaterial.mainTexture.name : "none") : "null")
						+ (l.positionCount > 0 ? " p0 " + l.transform.TransformPoint(l.GetPosition(0)).ToString("F2") + " pN " + l.transform.TransformPoint(l.GetPosition(l.positionCount - 1)).ToString("F2") : ""));
					l.enabled = false;
					off.Add(l);
				}
			}
			shot("e_no_lines");
			foreach (Renderer r in off) { r.enabled = true; }
			off.Clear();
			Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
			foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
			{
				if (r.enabled && !GeometryUtility.TestPlanesAABB(planes, r.bounds))
				{
					r.enabled = false;
					off.Add(r);
				}
			}
			Log("outside-frustum renderers switched off: " + off.Count);
			shot("f_no_offscreen");
			foreach (Renderer r in off) { r.enabled = true; }
		}
		catch (Exception e)
		{
			Debug.LogError("[ParticleSnapshot] EXCEPTION: " + e);
		}
		finally
		{
			if (rig != null)
			{
				rig.Restore();
			}
			UnityEngine.Object.DestroyImmediate(cameraObject);
		}
		Log("DiagnoseBandVariants done");
	}

	/// <summary>Dumps the triangles (world positions, uv) of the sidewalk tiles around the player.</summary>
	public static void DiagnoseFloorMesh()
	{
		if (SceneManager.GetActiveScene().path != ScenePath)
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		}
		foreach (MeshRenderer r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
		{
			if (!(r.name == "veredaOK_2_1" || r.name == "veredaOK_1_1 1"))
			{
				continue;
			}
			Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
			Vector3[] v = mesh.vertices;
			Vector2[] uv = mesh.uv;
			int[] t = mesh.triangles;
			Log("MESH " + r.name + " path '" + AssetDatabase.GetAssetPath(mesh) + "' verts " + v.Length + " tris " + (t.Length / 3) + " submeshes " + mesh.subMeshCount + " scale " + r.transform.lossyScale.ToString("F3"));
			for (int i = 0; i < t.Length; i += 3)
			{
				string line = "  tri " + (i / 3) + ":";
				for (int k = 0; k < 3; k++)
				{
					int idx = t[i + k];
					line += " " + r.transform.TransformPoint(v[idx]).ToString("F2") + (uv.Length > idx ? " uv" + uv[idx].ToString("F2") : "");
				}
				Log(line);
			}
		}
		Log("DiagnoseFloorMesh done");
	}

	private static string PathOf(Transform t)
	{
		string path = t.name;
		while (t.parent != null)
		{
			t = t.parent;
			path = t.name + "/" + path;
		}
		return path;
	}

	private static Camera MakeCamera(GameObject cameraObject, Camera sceneCamera)
	{
		Camera camera = cameraObject.AddComponent<Camera>();
		if (sceneCamera != null)
		{
			camera.CopyFrom(sceneCamera);
		}
		camera.stereoTargetEye = StereoTargetEyeMask.None;
		camera.nearClipPlane = 0.01f;
		camera.enabled = false;
		camera.fieldOfView = VerticalFov;
		camera.aspect = (float)Width / Height;
		UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
		if (data != null && sceneCamera != null)
		{
			UniversalAdditionalCameraData source = sceneCamera.GetUniversalAdditionalCameraData();
			if (source != null)
			{
				data.renderPostProcessing = source.renderPostProcessing;
				data.antialiasing = source.antialiasing;
			}
		}
		return camera;
	}

	private static CustomGrabInteractable PickGrabbable(Vector3 eye)
	{
		CustomGrabInteractable[] all = UnityEngine.Object.FindObjectsByType<CustomGrabInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		foreach (string preferred in PreferredGrabbables)
		{
			foreach (CustomGrabInteractable g in all)
			{
				if (g.name == preferred)
				{
					return g;
				}
			}
		}
		CustomGrabInteractable best = null;
		float bestDistance = float.MaxValue;
		foreach (CustomGrabInteractable g in all)
		{
			float d = Vector3.Distance(eye, g.transform.position);
			if (d > 0.8f && d < bestDistance)
			{
				best = g;
				bestDistance = d;
			}
		}
		return best;
	}

	private static Vector3 Flat(Vector3 v)
	{
		v.y = 0f;
		return v.sqrMagnitude > 1e-6f ? v : Vector3.forward;
	}

	private static void Add(List<Texture2D> tiles, List<string> names, Texture2D tile, string name)
	{
		tiles.Add(tile);
		names.Add(name);
	}

	private static Texture2D Render(Camera camera)
	{
		camera.fieldOfView = VerticalFov;
		camera.aspect = (float)Width / Height;
		RenderTextureDescriptor desc = new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24);
		desc.msaaSamples = 4;
		desc.sRGB = true;
		RenderTexture target = new RenderTexture(desc);
		target.Create();
		RenderTexture resolved = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
		resolved.Create();
		try
		{
			camera.targetTexture = target;
			camera.Render();
			camera.targetTexture = null;
			Graphics.Blit(target, resolved);
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = resolved;
			Texture2D image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
			image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
			image.Apply();
			RenderTexture.active = previous;
			return image;
		}
		finally
		{
			camera.targetTexture = null;
			target.Release();
			UnityEngine.Object.DestroyImmediate(target);
			resolved.Release();
			UnityEngine.Object.DestroyImmediate(resolved);
		}
	}

	/// <summary>Screen rectangle of the bounds (pixels, origin bottom-left like Texture2D), with a small margin.</summary>
	private static RectInt CropRect(Camera camera, Bounds bounds)
	{
		float minX = float.MaxValue;
		float minY = float.MaxValue;
		float maxX = float.MinValue;
		float maxY = float.MinValue;
		for (int i = 0; i < 8; i++)
		{
			Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) != 0 ? 1f : -1f, (i & 2) != 0 ? 1f : -1f, (i & 4) != 0 ? 1f : -1f));
			Vector3 viewport = camera.WorldToViewportPoint(corner);
			minX = Mathf.Min(minX, viewport.x * Width);
			maxX = Mathf.Max(maxX, viewport.x * Width);
			minY = Mathf.Min(minY, viewport.y * Height);
			maxY = Mathf.Max(maxY, viewport.y * Height);
		}
		float padX = (maxX - minX) * 0.08f + 16f;
		float padY = (maxY - minY) * 0.08f + 16f;
		int x0 = Mathf.Clamp(Mathf.FloorToInt(minX - padX), 0, Width - 1);
		int y0 = Mathf.Clamp(Mathf.FloorToInt(minY - padY), 0, Height - 1);
		int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX + padX), x0 + 1, Width);
		int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY + padY), y0 + 1, Height);
		return new RectInt(x0, y0, x1 - x0, y1 - y0);
	}

	/// <summary>The crop, enlarged by an integer factor (nearest neighbour, up to 4x, at most ~<paramref name="maxSize"/> px).</summary>
	private static Texture2D Crop(Texture2D source, RectInt rect, int maxSize)
	{
		int scale = Mathf.Clamp(maxSize / Mathf.Max(rect.width, rect.height), 1, 4);
		Color32[] all = source.GetPixels32();
		Texture2D result = new Texture2D(rect.width * scale, rect.height * scale, TextureFormat.RGBA32, false);
		Color32[] pixels = new Color32[result.width * result.height];
		for (int y = 0; y < result.height; y++)
		{
			int sy = rect.y + y / scale;
			for (int x = 0; x < result.width; x++)
			{
				int sx = rect.x + x / scale;
				pixels[y * result.width + x] = all[sy * source.width + sx];
			}
		}
		result.SetPixels32(pixels);
		result.Apply();
		return result;
	}

	private static Texture2D ContactSheet(List<Texture2D> tiles, List<string> names)
	{
		const int cell = 420;
		int cols = Mathf.CeilToInt(Mathf.Sqrt(tiles.Count));
		int rows = Mathf.CeilToInt((float)tiles.Count / cols);
		Texture2D sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, false);
		Color32[] fill = new Color32[sheet.width * sheet.height];
		for (int i = 0; i < fill.Length; i++)
		{
			fill[i] = new Color32(40, 40, 40, 255);
		}
		sheet.SetPixels32(fill);
		for (int i = 0; i < tiles.Count; i++)
		{
			Texture2D t = tiles[i];
			float s = (float)(cell - 4) / Mathf.Max(t.width, t.height);
			int w = Mathf.Max(1, Mathf.RoundToInt(t.width * s));
			int h = Mathf.Max(1, Mathf.RoundToInt(t.height * s));
			int ox = (i % cols) * cell + (cell - w) / 2;
			int oy = (rows - 1 - i / cols) * cell + (cell - h) / 2;
			Color[] px = new Color[w * h];
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					px[y * w + x] = t.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
				}
			}
			sheet.SetPixels(ox, oy, w, h, px);
			Log("CONTACTSHEET row " + (i / cols) + " col " + (i % cols) + " = " + names[i]);
		}
		sheet.Apply();
		return sheet;
	}

	private static void Save(Texture2D image, string path)
	{
		File.WriteAllBytes(path, image.EncodeToPNG());
		Log("saved " + path + " (" + image.width + "x" + image.height + ")");
	}

	private static Camera FindSceneCamera()
	{
		Camera best = null;
		foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
		{
			foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
			{
				if (camera.CompareTag("MainCamera"))
				{
					return camera;
				}
				if (best == null)
				{
					best = camera;
				}
			}
		}
		return best;
	}

	/// <summary>Player eye: the rig camera's horizontal position, EyeHeight above the XR Origin (its floor).</summary>
	private static Vector3 FindEye(Camera sceneCamera)
	{
		if (sceneCamera == null)
		{
			return FallbackEyeFloor + Vector3.up * EyeHeight;
		}
		Transform origin = sceneCamera.transform;
		while (origin.parent != null && !origin.name.Contains("XR Origin"))
		{
			origin = origin.parent;
		}
		Vector3 eye = sceneCamera.transform.position;
		eye.y = origin.position.y + EyeHeight;
		return eye;
	}

	private static Bounds JointBounds(GameObject target)
	{
		bool any = false;
		Bounds bounds = new Bounds(target.transform.position, Vector3.zero);
		foreach (MeshRenderer meshRenderer in target.GetComponentsInChildren<MeshRenderer>(true))
		{
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
		return bounds;
	}

	private static string Arg(string name, string fallback)
	{
		string[] args = Environment.GetCommandLineArgs();
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
			{
				return args[i + 1];
			}
		}
		return fallback;
	}

	private static void Log(string message)
	{
		Debug.Log("[ParticleSnapshot] " + message);
	}
}

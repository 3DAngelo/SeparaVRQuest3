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
	// Beam shots: the hand swept this far (deg, to the right) while the lazy end still lags on the residuo; how far along
	// its flight to the hand the grabbed residuo is (0..1, before easing); how far above the horizon the "aim up" shot
	// points (deg); frames in the flow exposure.
	private const float LagYaw = 10f;
	private const float FlightFraction = 0.5f;
	private const float UpPitch = 15f;
	private const int ExposureFrames = 8;

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
		Transform movedGrab = null;
		Vector3 movedGrabPosition = Vector3.zero;
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
				// XRI uses) at aimPoint; the hand mesh moves rigidly with it. The rest of the rig (other hand, controllers,
				// pinch / poke visuals) is hidden: in edit mode it all sits at the rig's rest pose, i.e. inside the camera.
				rig = PoseRightHand(aimPoint, bounds.center);
				Vector3 origin = rig.AimOrigin;
				Vector3 localCenter = HandBeamFX.LocalCenter(grab);
				Vector3 center = grab.transform.TransformPoint(localCenter);
				float groundY = GroundY();
				Log("aim origin " + origin.ToString("F3") + " from '" + rig.Source + "', residuo centre " + center.ToString("F3") + " min y " + bounds.min.y.ToString("F3") + ", ground (XR Origin) y " + groundY.ToString("F3"));

				// Rule check, exactly as HandBeamFX calls it: pointing at the residuo (the XRI ray hits it).
				Vector3 ruleEnd;
				bool ruleResiduo = HandBeamFX.AimsAtSidewalk(origin, (center - origin).normalized, groundY, true, center, true, out ruleEnd);
				Log("rule: aim at residuo -> " + (ruleResiduo ? "BEAM ON" : "off"));

				// ---- 1a. choosing: the hand has just swept LagYaw degrees to the right, the lazy end still on the residuo ----
				Vector3 toResiduo = (center - origin).normalized;
				rig.Point(origin, origin + Quaternion.AngleAxis(LagYaw, Vector3.up) * toResiduo);
				ParticleSystem beam = FeedbackFX.CreateBeam(null);
				temporary.Add(beam.gameObject);
				SimulateBeam(beam, rig.AimOrigin, rig.AimDirection, center, 1f, 2f, true);

				ParticleSystem sparkles = FeedbackFX.CreateSparkles(grab.name);
				temporary.Add(sparkles.gameObject);
				FeedbackFX.FollowSparkles(sparkles, grab.GetComponentsInChildren<MeshRenderer>());
				sparkles.Simulate(2f, true, true);
				Log("beam particles " + beam.particleCount + ", sparkles " + sparkles.particleCount);

				Texture2D full = Render(camera);
				Save(full, Path.Combine(outDir, tag + "_player_beam_full.png"));
				Bounds view = bounds;
				view.Encapsulate(origin);
				view.Encapsulate(rig.HandBounds);
				view.Encapsulate(bounds.center + Vector3.up * 0.3f);
				Texture2D crop = Crop(full, CropRect(camera, view), 1200);
				Save(crop, Path.Combine(outDir, tag + "_player_beam_crop.png"));
				Add(tiles, names, crop, "choosing: curved beam to the residuo (hand swept " + LagYaw + " deg, end lagging)");
				Bounds near = rig.HandBounds;
				near.Encapsulate(origin + rig.AimDirection * 0.8f);
				Texture2D handCrop = Crop(full, CropRect(camera, near), 1200);
				Save(handCrop, Path.Combine(outDir, tag + "_beam_hand_crop.png"));
				Add(tiles, names, handCrop, "beam at the hand (player view)");
				UnityEngine.Object.DestroyImmediate(full);

				// ---- 1b. side view of the whole beam (the curve), and the flow toward the hand ----
				Vector3 mid = Vector3.Lerp(origin, center, 0.5f);
				Vector3 side = Vector3.Cross(Vector3.up, Flat(center - origin)).normalized;
				Bounds whole = new Bounds(origin, Vector3.zero);   // the beam only: aim origin to the residuo
				whole.Encapsulate(bounds);
				whole.Expand(0.12f);
				float sideDistance = Mathf.Max(1.5f, Vector3.Distance(origin, center) * 1.1f);   // far enough to fit it all
				camera.transform.position = mid + side * sideDistance + Vector3.up * 0.2f;
				camera.transform.LookAt(mid);
				Texture2D sideShot = Render(camera);
				RectInt sideRect = CropRect(camera, whole);
				Texture2D sideCrop = Crop(sideShot, sideRect, 1200);
				Save(sideCrop, Path.Combine(outDir, tag + "_beam_side.png"));
				Add(tiles, names, sideCrop, "choosing: side view (curve)");
				UnityEngine.Object.DestroyImmediate(sideShot);

				// Flow evidence, same side camera: two frames 0.08 s apart, and an "exposure" of 8 frames 0.02 s apart
				// where older frames are dimmer: each light leaves a fading trail behind it, bright head toward the hand.
				Texture2D flowA = Crop(Render(camera), sideRect, 1200);
				SimulateBeam(beam, rig.AimOrigin, rig.AimDirection, center, 1f, 0.08f, false);
				Texture2D flowB = Crop(Render(camera), sideRect, 1200);
				Save(flowA, Path.Combine(outDir, tag + "_flow_a.png"));
				Save(flowB, Path.Combine(outDir, tag + "_flow_b.png"));
				Add(tiles, names, flowA, "flow frame A");
				Add(tiles, names, flowB, "flow frame B (+0.08 s)");
				List<Texture2D> exposure = new List<Texture2D>();
				for (int k = 0; k < ExposureFrames; k++)
				{
					SimulateBeam(beam, rig.AimOrigin, rig.AimDirection, center, 1f, 0.02f, false);
					Texture2D frameShot = Render(camera);
					exposure.Add(Crop(frameShot, sideRect, 1200));
					UnityEngine.Object.DestroyImmediate(frameShot);
				}
				Texture2D trails = Exposure(exposure);
				Save(trails, Path.Combine(outDir, tag + "_flow_exposure.png"));
				Add(tiles, names, trails, "flow exposure (trails; heads point to the hand)");
				foreach (Texture2D e in exposure)
				{
					UnityEngine.Object.DestroyImmediate(e);
				}

				// ---- 1b'. spiral close-up: side camera about 1 m from the middle of the beam, two frames ----
				Vector3 closeAt = Vector3.Lerp(origin, center, 0.55f);
				camera.transform.position = closeAt + side * 1f + Vector3.up * 0.05f;
				camera.transform.LookAt(closeAt);
				Bounds closeView = new Bounds(closeAt, Vector3.one * 0.35f);
				for (int k = 0; k < 2; k++)
				{
					Texture2D spiralShot = Render(camera);
					Texture2D spiralCrop = Crop(spiralShot, CropRect(camera, closeView), 1200);
					Save(spiralCrop, Path.Combine(outDir, tag + "_spiral_close_" + (k == 0 ? "a" : "b") + ".png"));
					Add(tiles, names, spiralCrop, "spiral close-up, 1 m side" + (k == 0 ? "" : " (+0.05 s)"));
					UnityEngine.Object.DestroyImmediate(spiralShot);
					SimulateBeam(beam, rig.AimOrigin, rig.AimDirection, center, 1f, 0.05f, false);
				}

				// ---- 1c. pulling: the residuo grabbed and halfway on its flight to the hand, the beam shortened to it ----
				sparkles.gameObject.SetActive(false);
				rig.Point(origin, center);   // the hand still points where it grabbed
				Vector3 holdCenter = rig.AimOrigin + rig.AimDirection * 0.14f + Vector3.down * 0.03f;
				Vector3 flyingCenter = Vector3.Lerp(center, holdCenter, Mathf.SmoothStep(0f, 1f, FlightFraction));
				movedGrab = grab.transform;
				movedGrabPosition = grab.transform.position;
				grab.transform.position += flyingCenter - center;
				Vector3 tip = grab.transform.TransformPoint(localCenter);
				SimulateBeam(beam, rig.AimOrigin, rig.AimDirection, center, 1f, 2f, true);   // the stream as it was...
				SimulateBeam(beam, rig.AimOrigin, rig.AimDirection, tip, 1f, 0.25f, false);  // ...then 0.25 s of flight
				Log("pulling: tip " + tip.ToString("F3") + " (" + Vector3.Distance(rig.AimOrigin, tip).ToString("F2") + " m from the hand), particles " + beam.particleCount);
				camera.transform.position = eye;
				camera.transform.LookAt(Vector3.Lerp(eye, center, 0.75f));
				Texture2D pull = Render(camera);
				Texture2D pullCrop = Crop(pull, CropRect(camera, view), 1200);
				Save(pullCrop, Path.Combine(outDir, tag + "_pull_player.png"));
				Add(tiles, names, pullCrop, "pulling: mid-flight, beam ends at the residuo");
				UnityEngine.Object.DestroyImmediate(pull);
				camera.transform.position = mid + side * sideDistance + Vector3.up * 0.2f;
				camera.transform.LookAt(mid);
				Texture2D pullSide = Render(camera);
				Texture2D pullSideCrop = Crop(pullSide, sideRect, 1200);
				Save(pullSideCrop, Path.Combine(outDir, tag + "_pull_side.png"));
				Add(tiles, names, pullSideCrop, "pulling: side view");
				UnityEngine.Object.DestroyImmediate(pullSide);
				movedGrab.position = movedGrabPosition;
				movedGrab = null;

				// ---- 1d. pointing at the bare sidewalk (no residuo hit): rule on, the beam ends on the ground ----
				Vector3 groundAim = new Vector3(center.x, groundY, center.z) - side * 0.7f + Flat(center - origin).normalized * 0.4f;
				rig.Point(origin, groundAim);
				Vector3 groundEnd;
				Physics.SyncTransforms();
				float surfaceY = HandBeamFX.SurfaceY(rig.AimOrigin, rig.AimDirection, groundY);   // as HandBeamFX does
				Log("sidewalk surface under the aim: y " + surfaceY.ToString("F3") + " (XR Origin floor " + groundY.ToString("F3") + ")");
				bool ruleGround = HandBeamFX.AimsAtSidewalk(rig.AimOrigin, rig.AimDirection, surfaceY, false, Vector3.zero, false, out groundEnd);
				Log("rule: aim at bare sidewalk -> " + (ruleGround ? "BEAM ON" : "off") + ", end " + groundEnd.ToString("F2"));
				SimulateBeam(beam, rig.AimOrigin, rig.AimDirection, groundEnd, ruleGround ? 1f : 0f, 2f, true);
				camera.transform.position = eye;
				camera.transform.LookAt(Vector3.Lerp(eye, center, 0.75f));
				Texture2D bare = Render(camera);
				Bounds bareView = view;
				bareView.Encapsulate(groundEnd);
				Texture2D bareCrop = Crop(bare, CropRect(camera, bareView), 1200);
				Save(bareCrop, Path.Combine(outDir, tag + "_aim_sidewalk.png"));
				Add(tiles, names, bareCrop, "aim at bare sidewalk: rule " + (ruleGround ? "ON" : "off"));
				UnityEngine.Object.DestroyImmediate(bare);

				// ---- 1e. pointing above the horizon (buildings / sky): rule off, no beam ----
				Vector3 upAim = Quaternion.AngleAxis(-UpPitch, Vector3.Cross(Vector3.up, Flat(center - origin)).normalized) * Flat(center - origin).normalized;
				rig.Point(origin, origin + upAim);
				Vector3 upEnd;
				bool ruleUp = HandBeamFX.AimsAtSidewalk(rig.AimOrigin, rig.AimDirection, groundY, false, Vector3.zero, false, out upEnd);
				Log("rule: aim " + UpPitch + " deg above the horizon (dir " + rig.AimDirection.ToString("F2") + ") -> " + (ruleUp ? "BEAM ON" : "off"));
				SimulateBeam(beam, rig.AimOrigin, rig.AimDirection, ruleUp ? upEnd : rig.AimOrigin + rig.AimDirection * 3f, ruleUp ? 1f : 0f, 2f, true);
				if (!ruleUp)
				{
					beam.Clear(true);   // as HandBeamFX does once faded out
				}
				camera.transform.position = eye;
				camera.transform.LookAt(eye + Flat(center - eye).normalized * 3f + Vector3.up * 0.3f);
				Texture2D up = Render(camera);
				Save(up, Path.Combine(outDir, tag + "_aim_up_full.png"));
				Bounds upView = rig.HandBounds;
				upView.Encapsulate(rig.AimOrigin + rig.AimDirection * 2.5f);
				upView.Expand(0.3f);
				Texture2D upCrop = Crop(up, CropRect(camera, upView), 1200);
				Save(upCrop, Path.Combine(outDir, tag + "_aim_up.png"));
				Add(tiles, names, upCrop, "aim above horizon: rule " + (ruleUp ? "ON" : "off (no beam)"));
				UnityEngine.Object.DestroyImmediate(up);
				rig.HideHand();   // the later shots are about the object / containers
				sparkles.gameObject.SetActive(true);

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
			if (movedGrab != null)
			{
				movedGrab.position = movedGrabPosition;
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
		public Transform Aim;
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

		/// <summary>Turns the posed hand (rigidly, as PoseRightHand) so its Aim Pose is at aimPoint looking at target.</summary>
		public void Point(Vector3 aimPoint, Vector3 target)
		{
			Vector3 dir = (target - aimPoint).normalized;
			if (Hand == null || Aim == null)
			{
				AimOrigin = aimPoint;
				AimDirection = dir;
				HandBounds = new Bounds(aimPoint, Vector3.one * 0.15f);
				return;
			}
			Quaternion delta = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Inverse(Aim.rotation);
			Vector3 newPosition = aimPoint + delta * (Hand.position - Aim.position);
			Hand.SetPositionAndRotation(newPosition, delta * Hand.rotation);
			AimOrigin = Aim.position;
			AimDirection = Aim.forward;
			bool any = false;
			foreach (Renderer r in Shown)
			{
				if (r == null || !r.gameObject.activeInHierarchy)
				{
					continue;
				}
				if (!any)
				{
					HandBounds = r.bounds;
					any = true;
				}
				else
				{
					HandBounds.Encapsulate(r.bounds);
				}
			}
			if (!any)
			{
				HandBounds = new Bounds(AimOrigin, Vector3.one * 0.15f);
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
		pose.Aim = aim;
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

	/// <summary>Places the beam on its curve, advances it by <paramref name="seconds"/> (from scratch if restart), places it again.</summary>
	private static void SimulateBeam(ParticleSystem beam, Vector3 origin, Vector3 aim, Vector3 end, float visibility, float seconds, bool restart)
	{
		FeedbackFX.PlaceBeam(beam, origin, aim, end, visibility);   // puts the emitter at the tip first
		if (restart)
		{
			beam.Simulate(seconds, true, true);
		}
		else
		{
			// In small steps, re-placing the lights every step (as every frame at run time).
			float left = seconds;
			while (left > 1e-4f)
			{
				float step = Mathf.Min(0.02f, left);
				beam.Simulate(step, true, false);
				FeedbackFX.PlaceBeam(beam, origin, aim, end, visibility);
				left -= step;
			}
		}
		FeedbackFX.PlaceBeam(beam, origin, aim, end, visibility);
	}

	/// <summary>"Long exposure": per pixel maximum of the frames, the older ones dimmer (frame k of n weighted (k+1)/n).</summary>
	private static Texture2D Exposure(List<Texture2D> frames)
	{
		Texture2D first = frames[0];
		Color[] result = new Color[first.width * first.height];
		for (int k = 0; k < frames.Count; k++)
		{
			float w = (k + 1f) / frames.Count;
			Color[] px = frames[k].GetPixels();
			if (px.Length != result.Length)
			{
				continue;
			}
			for (int i = 0; i < px.Length; i++)
			{
				Color c = px[i] * w;
				Color r = result[i];
				result[i] = new Color(Mathf.Max(r.r, c.r), Mathf.Max(r.g, c.g), Mathf.Max(r.b, c.b), 1f);
			}
		}
		// The scene itself is in every frame: keep the latest frame where nothing moved (no dimmed background).
		Color[] last = frames[frames.Count - 1].GetPixels();
		for (int i = 0; i < result.Length && i < last.Length; i++)
		{
			result[i] = new Color(Mathf.Max(result[i].r, last[i].r), Mathf.Max(result[i].g, last[i].g), Mathf.Max(result[i].b, last[i].b), 1f);
		}
		Texture2D tex = new Texture2D(first.width, first.height, TextureFormat.RGBA32, false);
		tex.SetPixels(result);
		tex.Apply();
		return tex;
	}

	/// <summary>The floor height HandBeamFX uses: the XR Origin's origin, or 0.</summary>
	private static float GroundY()
	{
		Unity.XR.CoreUtils.XROrigin xr = UnityEngine.Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
		return xr != null && xr.Origin != null ? xr.Origin.transform.position.y : 0f;
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

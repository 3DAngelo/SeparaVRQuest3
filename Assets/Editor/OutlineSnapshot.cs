// Screenshot harness for the selection outline (HighlightPulse + Assets/Resources/ContainerOutline.shader).
//
// Opens Assets/Scenes/NuevoJuego.unity (never saves it), puts a temporary URP camera at the player's eye (or 1 m from a
// grabbable object) and renders each target with and without its outline into PNGs, at two pixel densities:
//  - "eyebuf":  1832x1920, vertical FOV 100 deg (about 14 px/deg at the centre, like the Quest 3 default eye buffer).
//  - "display": 1600x1600, vertical FOV 58.4 deg (25 px/deg at the centre, the Quest 3 display density).
// Plus nearest-neighbour enlarged crops around the object, with and without the outline, to judge the border.
// The outline is drawn through the real code path: HighlightPulse.DrawOutline (Graphics.RenderMesh) before Camera.Render.
// If nothing changes in the image (RenderMesh not picked up by the manual render), it falls back to temporary child
// renderers that use the very same HighlightPulse materials.
//
// Batch: Unity.exe -batchmode -projectPath <project> -executeMethod OutlineSnapshot.Run -outlineTag after
//        [-outlineOut <dir>] -logFile <log> -quit
// The pulse is pinned to its minimum (thinnest, worst case), or its maximum with -outlinePulseMax.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class OutlineSnapshot
{
	private const string ScenePath = "Assets/Scenes/NuevoJuego.unity";
	private const string DefaultOutDir = @"C:\Users\Admin\AppData\Local\Temp\claude\G--SeparaVRecover\15853079-1b61-4d7e-9168-99726887a113\scratchpad";
	private static readonly string[] GrabbableNames = { "CajaJugo", "Agua" };
	private const string ContainerName = "naranja";
	// Eye height above the XR Origin (standing player), and where the player starts if no rig camera is found.
	private const float EyeHeight = 1.6f;
	private static readonly Vector3 FallbackEyeFloor = new Vector3(-8.09f, 0f, 8.56f);
	// Grabbable: camera this far away horizontally / above the object (about 1 m in total), on the side of the player.
	private const float GrabDistanceFlat = 0.85f;
	private const float GrabDistanceUp = 0.5f;

	private struct View
	{
		public string Name;
		public int Width;
		public int Height;
		public float VerticalFov;

		public View(string name, int width, int height, float verticalFov)
		{
			Name = name;
			Width = width;
			Height = height;
			VerticalFov = verticalFov;
		}
	}

	private static readonly View[] Views =
	{
		new View("eyebuf", 1832, 1920, 100f),
		new View("display", 1600, 1600, 58.4f),
	};

	private static bool useProxies;

	[MenuItem("Tools/SeparaVR/Outline snapshot (PNG)")]
	private static void RunFromMenu()
	{
		if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
		{
			return;
		}
		Run();
	}

	public static void Run()
	{
		string tag = Arg("-outlineTag", "snap");
		string outDir = Arg("-outlineOut", DefaultOutDir);
		bool pulseMax = HasArg("-outlinePulseMax");
		Directory.CreateDirectory(outDir);
		useProxies = HasArg("-outlineProxy");

		if (SceneManager.GetActiveScene().path != ScenePath)
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		}
		Log("scene " + ScenePath + ", tag " + tag + ", out " + outDir + ", pulse " + (pulseMax ? "max" : "min"));

		Camera sceneCamera = FindSceneCamera();
		Vector3 eye = FindEye(sceneCamera);
		Log("eye " + eye.ToString("F3"));

		GameObject cameraObject = new GameObject("OutlineSnapshotCamera");
		cameraObject.hideFlags = HideFlags.DontSave;
		try
		{
			Camera camera = cameraObject.AddComponent<Camera>();
			if (sceneCamera != null)
			{
				camera.CopyFrom(sceneCamera);
			}
			camera.stereoTargetEye = StereoTargetEyeMask.None;
			camera.nearClipPlane = 0.01f;
			camera.enabled = false;
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

			foreach (string grabName in GrabbableNames)
			{
				GameObject target = FindObject(grabName);
				if (target == null)
				{
					Log("not found: " + grabName);
					continue;
				}
				Bounds bounds = JointBounds(target);
				Vector3 toEye = eye - bounds.center;
				toEye.y = 0f;
				if (toEye.sqrMagnitude < 1e-4f)
				{
					toEye = Vector3.back;
				}
				Vector3 position = bounds.center + toEye.normalized * GrabDistanceFlat + Vector3.up * GrabDistanceUp;
				Shoot(camera, target, position, bounds, CustomGrabInteractable.HoverOutlineColor, CustomGrabInteractable.HoverOutlineWidth, pulseMax, tag + "_grab_" + grabName, outDir);
			}

			GameObject container = FindObject(ContainerName);
			if (container == null)
			{
				Log("not found: " + ContainerName);
			}
			else
			{
				Bounds bounds = JointBounds(container);
				Log("container distance " + Vector3.Distance(eye, bounds.center).ToString("F2") + " m");
				Shoot(camera, container, eye, bounds, Container.OutlineOrange, Container.OutlineWidth, pulseMax, tag + "_container_" + ContainerName, outDir);
			}
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(cameraObject);
		}
		Log("done");
	}

	/// <summary>
	/// Every CustomGrabInteractable and every Container of the scene, eyebuf crop with outline only (pulse at minimum),
	/// files outline_all_NAME_INDEX.png plus outline_all_contactsheet.png. Colours/widths come from
	/// CustomGrabInteractable.HoverOutlineColor/HoverOutlineWidth and Container.OutlineLineColor/OutlineWidth.
	/// </summary>
	public static void RunAll()
	{
		string outDir = Arg("-outlineOut", DefaultOutDir);
		bool pulseMax = HasArg("-outlinePulseMax");
		Directory.CreateDirectory(outDir);
		useProxies = HasArg("-outlineProxy");
		if (SceneManager.GetActiveScene().path != ScenePath)
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
		}
		Camera sceneCamera = FindSceneCamera();
		Vector3 eye = FindEye(sceneCamera);
		Log("RunAll eye " + eye.ToString("F3") + ", out " + outDir);

		List<KeyValuePair<GameObject, bool>> targets = new List<KeyValuePair<GameObject, bool>>();
		List<GameObject> grabs = new List<GameObject>();
		foreach (CustomGrabInteractable g in UnityEngine.Object.FindObjectsByType<CustomGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			grabs.Add(g.gameObject);
		}
		grabs.Sort((a, b) => { int c = string.CompareOrdinal(a.name, b.name); return c != 0 ? c : a.GetInstanceID().CompareTo(b.GetInstanceID()); });
		foreach (GameObject g in grabs)
		{
			targets.Add(new KeyValuePair<GameObject, bool>(g, false));
		}
		List<GameObject> containers = new List<GameObject>();
		foreach (Container c in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			containers.Add(c.gameObject);
		}
		containers.Sort((a, b) => { int c = string.CompareOrdinal(a.name, b.name); return c != 0 ? c : a.GetInstanceID().CompareTo(b.GetInstanceID()); });
		foreach (GameObject c in containers)
		{
			targets.Add(new KeyValuePair<GameObject, bool>(c, true));
		}
		Log("RunAll targets: " + grabs.Count + " grabbables, " + containers.Count + " containers");

		View view = Views[0];
		GameObject cameraObject = new GameObject("OutlineSnapshotCamera");
		cameraObject.hideFlags = HideFlags.DontSave;
		List<Texture2D> tiles = new List<Texture2D>();
		List<string> names = new List<string>();
		try
		{
			Camera camera = cameraObject.AddComponent<Camera>();
			if (sceneCamera != null)
			{
				camera.CopyFrom(sceneCamera);
			}
			camera.stereoTargetEye = StereoTargetEyeMask.None;
			camera.nearClipPlane = 0.01f;
			camera.enabled = false;
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

			for (int index = 0; index < targets.Count; index++)
			{
				GameObject target = targets[index].Key;
				bool isContainer = targets[index].Value;
				string label = (isContainer ? "container_" : "grab_") + target.name + "_" + index;
				bool wasActive = target.activeSelf;
				target.SetActive(true);
				try
				{
					Bounds bounds = JointBounds(target);
					Vector3 toEye = eye - bounds.center;
					toEye.y = 0f;
					if (toEye.sqrMagnitude < 1e-4f)
					{
						toEye = Vector3.back;
					}
					toEye.Normalize();
					Vector3 position;
					if (isContainer)
					{
						float flat = Vector2.Distance(new Vector2(eye.x, eye.z), new Vector2(bounds.center.x, bounds.center.z));
						position = eye;
						camera.fieldOfView = view.VerticalFov;
						camera.aspect = (float)view.Width / view.Height;
						for (float d = Mathf.Max(flat, 0.5f); d <= 8f; d += 0.5f)
						{
							position = new Vector3(bounds.center.x, eye.y, bounds.center.z) + toEye * d;
							camera.transform.position = position;
							camera.transform.LookAt(bounds.center);
							if (AllCornersVisible(camera, bounds))
							{
								break;
							}
						}
					}
					else
					{
						float dist = Mathf.Max(0.7f, bounds.extents.magnitude * 2.2f);
						Vector3 dir = (toEye * Mathf.Cos(25f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(25f * Mathf.Deg2Rad)).normalized;
						position = bounds.center + dir * dist;
					}
					Color color = isContainer ? Container.OutlineLineColor : CustomGrabInteractable.HoverOutlineColor;
					float width = isContainer ? Container.OutlineWidth : CustomGrabInteractable.HoverOutlineWidth;
					camera.transform.position = position;
					camera.transform.LookAt(bounds.center);
					List<MeshRenderer> renderers = new List<MeshRenderer>(target.GetComponentsInChildren<MeshRenderer>());
					HighlightPulse pulse = new HighlightPulse("Snapshot " + target.name, color);
					pulse.Width = width;
					pulse.Begin();
					PinPulse(pulse, pulseMax);

					Texture2D plain = Render(camera, view, null, null);
					Texture2D outlined = Render(camera, view, pulse, renderers);
					int changed = CountChanged(plain, outlined);
					if (changed == 0 && !useProxies)
					{
						Log("RenderMesh not picked up by Camera.Render; switching to proxy renderers");
						useProxies = true;
						UnityEngine.Object.DestroyImmediate(outlined);
						outlined = Render(camera, view, pulse, renderers);
						changed = CountChanged(plain, outlined);
					}
					Log("ALL " + label + ": renderers " + renderers.Count + ", color " + color + ", width " + width + ", distance " + Vector3.Distance(position, bounds.center).ToString("F2") + " m, bounds size " + bounds.size.ToString("F3") + ", changed pixels " + changed + (useProxies ? " (proxies)" : " (RenderMesh)") + (changed == 0 ? " ZERO_OUTLINE" : ""));
					Texture2D crop = Crop(outlined, CropRect(camera, bounds, view));
					string safe = target.name;
					foreach (char ch in Path.GetInvalidFileNameChars())
					{
						safe = safe.Replace(ch, '_');
					}
					Save(crop, Path.Combine(outDir, "outline_all_" + safe + "_" + index + ".png"));
					tiles.Add(crop);
					names.Add(label);
					UnityEngine.Object.DestroyImmediate(plain);
					UnityEngine.Object.DestroyImmediate(outlined);
				}
				catch (Exception e)
				{
					Debug.LogError("[OutlineSnapshot] EXCEPTION for " + label + ": " + e);
				}
				finally
				{
					target.SetActive(wasActive);
				}
			}
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(cameraObject);
		}

		if (tiles.Count > 0)
		{
			const int cell = 384;
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
			Save(sheet, Path.Combine(outDir, "outline_all_contactsheet.png"));
		}
		Log("RunAll done");
	}

	private static bool AllCornersVisible(Camera camera, Bounds bounds)
	{
		for (int i = 0; i < 8; i++)
		{
			Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) != 0 ? 1f : -1f, (i & 2) != 0 ? 1f : -1f, (i & 4) != 0 ? 1f : -1f));
			Vector3 v = camera.WorldToViewportPoint(corner);
			if (v.z <= 0f || v.x < 0.02f || v.x > 0.98f || v.y < 0.02f || v.y > 0.98f)
			{
				return false;
			}
		}
		return true;
	}

	private static void Shoot(Camera camera, GameObject target, Vector3 position, Bounds bounds, Color color, float width, bool pulseMax, string baseName, string outDir)
	{
		bool wasActive = target.activeSelf;
		target.SetActive(true);
		try
		{
			camera.transform.position = position;
			camera.transform.LookAt(bounds.center);
			List<MeshRenderer> renderers = new List<MeshRenderer>(target.GetComponentsInChildren<MeshRenderer>());
			HighlightPulse pulse = new HighlightPulse("Snapshot " + target.name, color);
			pulse.Width = width;
			pulse.Begin();
			PinPulse(pulse, pulseMax);
			Log(baseName + ": " + renderers.Count + " renderers, width " + width + " m, distance " + Vector3.Distance(position, bounds.center).ToString("F2") + " m, MinPixels " + ReadField(pulse, "MinPixels"));

			foreach (View view in Views)
			{
				Texture2D plain = Render(camera, view, null, null);
				Texture2D outlined = Render(camera, view, pulse, renderers);
				int changed = CountChanged(plain, outlined);
				if (changed == 0 && !useProxies)
				{
					Log("RenderMesh not picked up by Camera.Render; switching to proxy renderers");
					useProxies = true;
					UnityEngine.Object.DestroyImmediate(outlined);
					outlined = Render(camera, view, pulse, renderers);
					changed = CountChanged(plain, outlined);
				}
				Log("OUTLINE " + baseName + "_" + view.Name + " changed pixels " + changed + (useProxies ? " (proxies)" : " (RenderMesh)"));

				string prefix = Path.Combine(outDir, "outline_" + baseName + "_" + view.Name);
				Save(outlined, prefix + ".png");
				RectInt crop = CropRect(camera, bounds, view);
				Save(Crop(outlined, crop), prefix + "_crop.png");
				Save(Crop(plain, crop), prefix + "_crop_nooutline.png");
				UnityEngine.Object.DestroyImmediate(plain);
				UnityEngine.Object.DestroyImmediate(outlined);
			}
		}
		finally
		{
			target.SetActive(wasActive);
		}
	}

	private static Texture2D Render(Camera camera, View view, HighlightPulse pulse, List<MeshRenderer> renderers)
	{
		camera.fieldOfView = view.VerticalFov;
		camera.aspect = (float)view.Width / view.Height;
		RenderTextureDescriptor desc = new RenderTextureDescriptor(view.Width, view.Height, RenderTextureFormat.ARGB32, 24);
		desc.msaaSamples = 4;
		desc.sRGB = true;
		RenderTexture target = new RenderTexture(desc);
		target.Create();
		RenderTexture resolved = new RenderTexture(view.Width, view.Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
		resolved.Create();
		List<GameObject> proxies = null;
		try
		{
			camera.targetTexture = target;
			if (pulse != null)
			{
				if (useProxies)
				{
					proxies = MakeProxies(pulse, renderers);
				}
				else
				{
					pulse.DrawOutline(renderers, renderers.Count > 0 ? renderers[0].gameObject.layer : 0);
				}
			}
			camera.Render();
			camera.targetTexture = null;
			Graphics.Blit(target, resolved);

			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = resolved;
			Texture2D image = new Texture2D(view.Width, view.Height, TextureFormat.RGBA32, false);
			image.ReadPixels(new Rect(0, 0, view.Width, view.Height), 0, 0);
			image.Apply();
			RenderTexture.active = previous;
			return image;
		}
		finally
		{
			camera.targetTexture = null;
			if (proxies != null)
			{
				foreach (GameObject proxy in proxies)
				{
					UnityEngine.Object.DestroyImmediate(proxy);
				}
			}
			target.Release();
			UnityEngine.Object.DestroyImmediate(target);
			resolved.Release();
			UnityEngine.Object.DestroyImmediate(resolved);
		}
	}

	/// <summary>
	/// Fallback: child renderers with the HighlightPulse materials (its private fields, so the very same shader and
	/// values): "maskMaterial" + "outlineMaterial" when the class has both, else "outlineMaterial" alone.
	/// </summary>
	private static List<GameObject> MakeProxies(HighlightPulse pulse, List<MeshRenderer> renderers)
	{
		// Let DrawOutline set the material values (colour, centre, widths), then draw them with ordinary renderers.
		pulse.DrawOutline(renderers, 0);
		List<Material> materials = new List<Material>();
		Material mask = ReadField(pulse, "maskMaterial") as Material;
		Material outline = ReadField(pulse, "outlineMaterial") as Material;
		if (mask != null)
		{
			materials.Add(mask);
		}
		if (outline != null)
		{
			materials.Add(outline);
		}
		List<GameObject> proxies = new List<GameObject>();
		foreach (MeshRenderer meshRenderer in renderers)
		{
			MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
			if (filter == null || filter.sharedMesh == null || !meshRenderer.enabled || !meshRenderer.gameObject.activeInHierarchy)
			{
				continue;
			}
			foreach (Material material in materials)
			{
				GameObject proxy = new GameObject("OutlineProxy");
				proxy.hideFlags = HideFlags.DontSave;
				proxy.layer = meshRenderer.gameObject.layer;
				proxy.transform.SetParent(meshRenderer.transform, false);
				proxy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
				MeshRenderer proxyRenderer = proxy.AddComponent<MeshRenderer>();
				Material[] shared = new Material[filter.sharedMesh.subMeshCount];
				for (int i = 0; i < shared.Length; i++)
				{
					shared[i] = material;
				}
				proxyRenderer.sharedMaterials = shared;
				proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
				proxyRenderer.receiveShadows = false;
				proxies.Add(proxy);
			}
		}
		return proxies;
	}

	/// <summary>Pins the pulse of <paramref name="pulse"/> to its minimum (or maximum) by moving its private start time.</summary>
	private static void PinPulse(HighlightPulse pulse, bool max)
	{
		FieldInfo field = typeof(HighlightPulse).GetField("startTime", BindingFlags.Instance | BindingFlags.NonPublic);
		if (field == null)
		{
			Log("HighlightPulse.startTime not found; pulse not pinned");
			return;
		}
		// Value = 0.5 + 0.5 sin((t - start) 2 pi f): -1 at (t - start) f = -1/4, +1 at +1/4.
		float quarter = 0.25f / HighlightPulse.PulseHz;
		field.SetValue(pulse, max ? Time.time - quarter : Time.time + quarter);
		Log("pulse value " + pulse.Value.ToString("F3"));
	}

	private static object ReadField(object instance, string name)
	{
		FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		return field != null ? field.GetValue(instance) : null;
	}

	private static int CountChanged(Texture2D a, Texture2D b)
	{
		Color32[] pa = a.GetPixels32();
		Color32[] pb = b.GetPixels32();
		int changed = 0;
		for (int i = 0; i < pa.Length; i++)
		{
			if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) > 24)
			{
				changed++;
			}
		}
		return changed;
	}

	/// <summary>Screen rectangle of the bounds (pixels, origin bottom-left like Texture2D), enlarged by half its size.</summary>
	private static RectInt CropRect(Camera camera, Bounds bounds, View view)
	{
		camera.fieldOfView = view.VerticalFov;
		camera.aspect = (float)view.Width / view.Height;
		float minX = float.MaxValue;
		float minY = float.MaxValue;
		float maxX = float.MinValue;
		float maxY = float.MinValue;
		for (int i = 0; i < 8; i++)
		{
			Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) != 0 ? 1f : -1f, (i & 2) != 0 ? 1f : -1f, (i & 4) != 0 ? 1f : -1f));
			Vector3 viewport = camera.WorldToViewportPoint(corner);
			minX = Mathf.Min(minX, viewport.x * view.Width);
			maxX = Mathf.Max(maxX, viewport.x * view.Width);
			minY = Mathf.Min(minY, viewport.y * view.Height);
			maxY = Mathf.Max(maxY, viewport.y * view.Height);
		}
		float padX = (maxX - minX) * 0.25f + 16f;
		float padY = (maxY - minY) * 0.25f + 16f;
		int x0 = Mathf.Clamp(Mathf.FloorToInt(minX - padX), 0, view.Width - 1);
		int y0 = Mathf.Clamp(Mathf.FloorToInt(minY - padY), 0, view.Height - 1);
		int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX + padX), x0 + 1, view.Width);
		int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY + padY), y0 + 1, view.Height);
		return new RectInt(x0, y0, x1 - x0, y1 - y0);
	}

	/// <summary>The crop, enlarged by an integer factor (nearest neighbour, up to 6x, at most ~1600 px) so pixels stay visible.</summary>
	private static Texture2D Crop(Texture2D source, RectInt rect)
	{
		int scale = Mathf.Clamp(1600 / Mathf.Max(rect.width, rect.height), 1, 6);
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

	private static GameObject FindObject(string name)
	{
		foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
		{
			foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
			{
				if (t.name == name)
				{
					return t.gameObject;
				}
			}
		}
		return null;
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

	private static bool HasArg(string name)
	{
		foreach (string arg in Environment.GetCommandLineArgs())
		{
			if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void Log(string message)
	{
		Debug.Log("[OutlineSnapshot] " + message);
	}
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Why light baking "does nothing" in the recovered scenes, and the tool that fixes it.
///
/// The scenes were recovered from a BUILD. In a build, static geometry has already been merged by static batching:
/// every static MeshFilter points to a huge "Combined Mesh (root: scene)" asset (vertices in world space) and its
/// MeshRenderer carries "Static Batch Info" (which sub-meshes of the combined mesh are its own). The lightmapper
/// skips renderers that are already part of a static batch, so a bake only processes the few objects that are not
/// (in NuevoJuego: 1 renderer out of 1022 marked Contribute GI) and produces an almost empty lightmap.
///
/// Menu Tools > SeparaVR > Light Bake:
///   1. Report                 - counts what would be converted. Changes nothing.
///   2. Un-batch static meshes - gives every such renderer its own mesh again (extracted from the combined mesh and
///                               moved back to the object's local space, lightmap UVs kept) and clears the static
///                               batch info. Static flags stay, so Play mode and builds batch again by themselves.
///   3. Regenerate lightmap UVs- optional, if the bake shows broken / overlapping lightmap UVs.
///   4. Moderate bake settings - optional, lowers the lightmap resolution of the scene's Lighting Settings.
/// </summary>
public static class LightBakeFix
{
	private const string Menu = "Tools/SeparaVR/Light Bake/";
	private const string OutputFolderParent = "Assets/Mesh";
	private const string OutputFolderName = "Unbatched";
	private const string Tag = "[LightBakeFix] ";

	// Lightmap texels per world unit proposed by menu item 4 (the scene asset currently asks for 100).
	private const float ProposedResolution = 12f;

	private class Source
	{
		public Mesh mesh;
		public List<Vector3> vertices = new List<Vector3>();
		public List<Vector3> normals = new List<Vector3>();
		public List<Vector4> tangents = new List<Vector4>();
		public List<Vector2> uv0 = new List<Vector2>();
		public List<Vector2> uv1 = new List<Vector2>();
		public List<Color32> colors = new List<Color32>();
	}

	private class Job
	{
		public MeshRenderer renderer;
		public MeshFilter filter;
		public int firstSubMesh;
		public int subMeshCount;
		public Mesh result;
		public bool uvGenerated;
	}

	// ---------------------------------------------------------------- 1. report

	[MenuItem(Menu + "1. Report (changes nothing)", priority = 1)]
	private static void Report()
	{
		Scene scene = SceneManager.GetActiveScene();
		List<Job> jobs = CollectJobs(scene, out int total, out int contributeGI, out int batchedGI);
		HashSet<Mesh> combined = new HashSet<Mesh>();
		foreach (Job job in jobs)
		{
			combined.Add(job.filter.sharedMesh);
		}
		Debug.Log(Tag + "Scene '" + scene.name + "': " + total + " MeshRenderers, " + contributeGI + " marked Contribute GI.\n" +
			jobs.Count + " MeshRenderers are leftovers of a static batch (" + batchedGI + " of them Contribute GI), pointing into " + combined.Count + " combined meshes.\n" +
			"The lightmapper skips those, so today a bake only handles " + (contributeGI - batchedGI) + " renderer(s).\n" +
			(jobs.Count > 0
				? "Run 'Tools > SeparaVR > Light Bake > 2. Un-batch static meshes' and then Generate Lighting."
				: "Nothing to convert: this scene is ready for baking as far as static batching is concerned."));
		if (Lightmapping.TryGetLightingSettings(out LightingSettings settings))
		{
			Debug.Log(Tag + "Lighting Settings '" + settings.name + "': resolution " + settings.lightmapResolution + " texels/unit, max lightmap size " +
				settings.lightmapMaxSize + ", lightmapper " + settings.lightmapper + ", baked GI " + settings.bakedGI + ".\n" +
				(settings.lightmapResolution > 30f ? "That resolution is very high for a whole city: expect dozens of lightmaps and a very long bake. See menu item 4." : ""));
		}
		else
		{
			Debug.LogWarning(Tag + "The scene has no Lighting Settings asset assigned (Window > Rendering > Lighting > Scene > New).");
		}
		foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			bool gi = (GameObjectUtility.GetStaticEditorFlags(terrain.gameObject) & StaticEditorFlags.ContributeGI) != 0;
			Debug.Log(Tag + "Terrain '" + terrain.name + "': Contribute GI = " + gi + (gi ? "" : " (it will not be lightmapped; tick Static > Contribute GI on it if you want it to)"), terrain);
		}
	}

	// ---------------------------------------------------------------- 2. un-batch

	[MenuItem(Menu + "2. Un-batch static meshes of the open scene", priority = 2)]
	private static void Unbatch()
	{
		if (EditorApplication.isPlayingOrWillChangePlaymode)
		{
			EditorUtility.DisplayDialog("Light Bake Fix", "Exit Play mode first.", "OK");
			return;
		}
		Scene scene = SceneManager.GetActiveScene();
		List<Job> jobs = CollectJobs(scene, out int total, out int contributeGI, out int batchedGI);
		if (jobs.Count == 0)
		{
			Debug.Log(Tag + "Scene '" + scene.name + "': no static-batch leftovers found. Nothing to do.");
			return;
		}
		if (!EditorUtility.DisplayDialog("Light Bake Fix",
			"Scene '" + scene.name + "': " + jobs.Count + " MeshRenderers still point into combined static-batch meshes, which the lightmapper skips.\n\n" +
			"This gives each of them its own mesh (saved under " + OutputFolderParent + "/" + OutputFolderName + ") and clears the static batch info. " +
			"The original combined meshes are not modified. The scene is NOT saved automatically: check it, then save it yourself (or close without saving to discard).",
			"Convert", "Cancel"))
		{
			return;
		}

		try
		{
			// Phase 1: build every mesh in memory. Nothing in the scene is touched until all of them succeeded.
			Dictionary<Mesh, Source> sources = new Dictionary<Mesh, Source>();
			Dictionary<Mesh, List<Mesh>> resultsBySource = new Dictionary<Mesh, List<Mesh>>();
			int uvKept = 0, uvGenerated = 0;
			for (int i = 0; i < jobs.Count; i++)
			{
				Job job = jobs[i];
				if (EditorUtility.DisplayCancelableProgressBar("Light Bake Fix", "Extracting meshes " + (i + 1) + " / " + jobs.Count, (float)i / jobs.Count))
				{
					Debug.LogWarning(Tag + "Cancelled. Nothing was changed.");
					return;
				}
				Mesh combined = job.filter.sharedMesh;
				if (!sources.TryGetValue(combined, out Source source))
				{
					source = ReadSource(combined);
					if (source == null)
					{
						Debug.LogError(Tag + "Could not read the vertex data of '" + combined.name + "'. Nothing was changed.", combined);
						return;
					}
					sources.Add(combined, source);
					resultsBySource.Add(combined, new List<Mesh>());
				}
				job.result = Extract(job, source);
				if (job.result == null)
				{
					Debug.LogError(Tag + "Could not extract the mesh of '" + job.renderer.name + "' (sub-mesh " + job.firstSubMesh + " of '" + combined.name + "'). Nothing was changed.", job.renderer);
					return;
				}
				if (job.uvGenerated) uvGenerated++; else uvKept++;
				resultsBySource[combined].Add(job.result);
			}

			// Phase 2: save the meshes (one asset file per combined mesh, the new meshes inside it).
			EditorUtility.DisplayProgressBar("Light Bake Fix", "Saving meshes", 0.95f);
			string folder = OutputFolderParent + "/" + OutputFolderName;
			if (!AssetDatabase.IsValidFolder(folder))
			{
				if (!AssetDatabase.IsValidFolder(OutputFolderParent))
				{
					AssetDatabase.CreateFolder("Assets", Path.GetFileName(OutputFolderParent));
				}
				AssetDatabase.CreateFolder(OutputFolderParent, OutputFolderName);
			}
			int fileIndex = 0;
			foreach (KeyValuePair<Mesh, List<Mesh>> pair in resultsBySource)
			{
				string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + scene.name + "_unbatched_" + fileIndex++ + ".asset");
				AssetDatabase.CreateAsset(pair.Value[0], path);
				for (int i = 1; i < pair.Value.Count; i++)
				{
					AssetDatabase.AddObjectToAsset(pair.Value[i], path);
				}
			}
			AssetDatabase.SaveAssets();

			// Phase 3: point the scene objects to their own meshes and clear the static batch info.
			Undo.SetCurrentGroupName("Un-batch static meshes");
			int group = Undo.GetCurrentGroup();
			foreach (Job job in jobs)
			{
				Undo.RecordObject(job.filter, "Un-batch static meshes");
				job.filter.sharedMesh = job.result;
				EditorUtility.SetDirty(job.filter);

				SerializedObject so = new SerializedObject(job.renderer);
				so.FindProperty("m_StaticBatchInfo.firstSubMesh").intValue = 0;
				so.FindProperty("m_StaticBatchInfo.subMeshCount").intValue = 0;
				SerializedProperty root = so.FindProperty("m_StaticBatchRoot");
				if (root != null)
				{
					root.objectReferenceValue = null;
				}
				so.ApplyModifiedProperties();
			}
			Undo.CollapseUndoOperations(group);
			EditorSceneManager.MarkSceneDirty(scene);

			Debug.Log(Tag + "Done. " + jobs.Count + " MeshRenderers of scene '" + scene.name + "' now have their own mesh (" + fileIndex + " asset files under " + folder + ").\n" +
				"Lightmap UVs: kept from the original for " + uvKept + ", generated for " + uvGenerated + " (they had none usable).\n" +
				"Static flags were left as they were, so Play mode and builds still static-batch these objects.\n" +
				"MeshColliders were not touched (they keep using the combined meshes, which is fine).\n" +
				"NEXT: look at the scene (it must look exactly as before), save it, then Window > Rendering > Lighting > Generate Lighting.\n" +
				"Before a long bake consider menu item 4 (the scene asks for " + CurrentResolutionText() + " texels per unit).\n" +
				"To go back: Edit > Undo, or close the scene without saving.");
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}
	}

	private static string CurrentResolutionText()
	{
		return Lightmapping.TryGetLightingSettings(out LightingSettings settings) ? settings.lightmapResolution.ToString("0.#") : "?";
	}

	/// <summary>MeshRenderers of the scene that carry static batch info and a mesh.</summary>
	private static List<Job> CollectJobs(Scene scene, out int total, out int contributeGI, out int batchedGI)
	{
		List<Job> jobs = new List<Job>();
		total = contributeGI = batchedGI = 0;
		foreach (GameObject rootObject in scene.GetRootGameObjects())
		{
			foreach (MeshRenderer renderer in rootObject.GetComponentsInChildren<MeshRenderer>(true))
			{
				total++;
				bool gi = (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.ContributeGI) != 0;
				if (gi) contributeGI++;
				MeshFilter filter = renderer.GetComponent<MeshFilter>();
				if (filter == null || filter.sharedMesh == null)
				{
					continue;
				}
				SerializedObject so = new SerializedObject(renderer);
				SerializedProperty first = so.FindProperty("m_StaticBatchInfo.firstSubMesh");
				SerializedProperty count = so.FindProperty("m_StaticBatchInfo.subMeshCount");
				if (first == null || count == null || count.intValue <= 0)
				{
					continue;
				}
				if (first.intValue + count.intValue > filter.sharedMesh.subMeshCount)
				{
					Debug.LogWarning(Tag + "'" + renderer.name + "' has static batch info that does not fit its mesh '" + filter.sharedMesh.name + "'; skipped.", renderer);
					continue;
				}
				if (gi) batchedGI++;
				jobs.Add(new Job { renderer = renderer, filter = filter, firstSubMesh = first.intValue, subMeshCount = count.intValue });
			}
		}
		return jobs;
	}

	private static Source ReadSource(Mesh mesh)
	{
		Source source = new Source { mesh = mesh };
		mesh.GetVertices(source.vertices);
		if (source.vertices.Count == 0 && mesh.vertexCount > 0 && !mesh.isReadable)
		{
			// The recovered combined meshes are flagged "not readable". Lift the flag on the loaded object only
			// (the asset file is not saved by this tool) so that the vertex data can be read.
			SerializedObject so = new SerializedObject(mesh);
			SerializedProperty readable = so.FindProperty("m_IsReadable");
			if (readable != null)
			{
				readable.boolValue = true;
				so.ApplyModifiedPropertiesWithoutUndo();
			}
			mesh.GetVertices(source.vertices);
		}
		if (source.vertices.Count == 0)
		{
			return null;
		}
		mesh.GetNormals(source.normals);
		mesh.GetTangents(source.tangents);
		mesh.GetUVs(0, source.uv0);
		mesh.GetUVs(1, source.uv1);
		mesh.GetColors(source.colors);
		int n = source.vertices.Count;
		if (source.normals.Count != n) source.normals = null;
		if (source.tangents.Count != n) source.tangents = null;
		if (source.uv0.Count != n) source.uv0 = null;
		if (source.uv1.Count != n) source.uv1 = null;
		if (source.colors.Count != n) source.colors = null;
		return source;
	}

	private static Mesh Extract(Job job, Source source)
	{
		Transform transform = job.renderer.transform;
		// The combined vertices are in the space of the static batch root; these scenes have none, i.e. world space.
		Transform batchRoot = new SerializedObject(job.renderer).FindProperty("m_StaticBatchRoot")?.objectReferenceValue as Transform;
		Matrix4x4 batchToWorld = batchRoot != null ? batchRoot.localToWorldMatrix : Matrix4x4.identity;
		Matrix4x4 toLocal = transform.worldToLocalMatrix * batchToWorld;
		Matrix4x4 normalToLocal = (batchToWorld.inverse * transform.localToWorldMatrix).transpose;   // inverse transpose of toLocal
		bool mirrored = transform.localToWorldMatrix.determinant < 0f;   // static batching flipped the winding of mirrored objects

		Dictionary<int, int> remap = new Dictionary<int, int>();
		List<Vector3> vertices = new List<Vector3>();
		List<Vector3> normals = source.normals != null ? new List<Vector3>() : null;
		List<Vector4> tangents = source.tangents != null ? new List<Vector4>() : null;
		List<Vector2> uv0 = source.uv0 != null ? new List<Vector2>() : null;
		List<Vector2> uv1 = source.uv1 != null ? new List<Vector2>() : null;
		List<Color32> colors = source.colors != null ? new List<Color32>() : null;
		List<int[]> subMeshIndices = new List<int[]>();
		List<MeshTopology> topologies = new List<MeshTopology>();

		for (int s = 0; s < job.subMeshCount; s++)
		{
			int subMesh = job.firstSubMesh + s;
			MeshTopology topology = source.mesh.GetTopology(subMesh);
			int[] indices = source.mesh.GetIndices(subMesh, true);
			if (indices == null || indices.Length == 0)
			{
				return null;
			}
			for (int i = 0; i < indices.Length; i++)
			{
				int old = indices[i];
				if (old < 0 || old >= source.vertices.Count)
				{
					return null;
				}
				if (!remap.TryGetValue(old, out int index))
				{
					index = vertices.Count;
					remap.Add(old, index);
					vertices.Add(toLocal.MultiplyPoint3x4(source.vertices[old]));
					if (normals != null) normals.Add(normalToLocal.MultiplyVector(source.normals[old]).normalized);
					if (tangents != null)
					{
						Vector4 t = source.tangents[old];
						Vector3 dir = toLocal.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
						tangents.Add(new Vector4(dir.x, dir.y, dir.z, mirrored ? -t.w : t.w));
					}
					if (uv0 != null) uv0.Add(source.uv0[old]);
					if (uv1 != null) uv1.Add(source.uv1[old]);
					if (colors != null) colors.Add(source.colors[old]);
				}
				indices[i] = index;
			}
			if (mirrored && topology == MeshTopology.Triangles)
			{
				for (int i = 0; i + 2 < indices.Length; i += 3)
				{
					int tmp = indices[i + 1];
					indices[i + 1] = indices[i + 2];
					indices[i + 2] = tmp;
				}
			}
			subMeshIndices.Add(indices);
			topologies.Add(topology);
		}

		Mesh mesh = new Mesh();
		mesh.name = job.renderer.name;
		mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
		mesh.SetVertices(vertices);
		if (normals != null) mesh.SetNormals(normals);
		if (tangents != null) mesh.SetTangents(tangents);
		if (uv0 != null) mesh.SetUVs(0, uv0);
		if (colors != null) mesh.SetColors(colors);
		mesh.subMeshCount = subMeshIndices.Count;
		for (int s = 0; s < subMeshIndices.Count; s++)
		{
			mesh.SetIndices(subMeshIndices[s], topologies[s], s, false);
		}
		mesh.RecalculateBounds();

		// Lightmap UVs. Static batching stored them already placed inside the old lightmap atlas (a small rectangle
		// somewhere in 0..1). Stretch that rectangle back to the full 0..1 square, keeping its proportions.
		if (uv1 != null && NormaliseLightmapUVs(uv1))
		{
			mesh.SetUVs(1, uv1);
		}
		else
		{
			Unwrapping.GenerateSecondaryUVSet(mesh);
			job.uvGenerated = true;
		}
		return mesh;
	}

	private static bool NormaliseLightmapUVs(List<Vector2> uv)
	{
		if (uv.Count == 0)
		{
			return false;
		}
		Vector2 min = uv[0], max = uv[0];
		for (int i = 1; i < uv.Count; i++)
		{
			min = Vector2.Min(min, uv[i]);
			max = Vector2.Max(max, uv[i]);
		}
		Vector2 size = max - min;
		float largest = Mathf.Max(size.x, size.y);
		if (float.IsNaN(largest) || largest < 1e-6f || Mathf.Min(size.x, size.y) < 1e-7f)
		{
			return false;   // all the UVs in one point or on one line: unusable
		}
		float scale = 1f / largest;
		for (int i = 0; i < uv.Count; i++)
		{
			uv[i] = (uv[i] - min) * scale;
		}
		return true;
	}

	// ---------------------------------------------------------------- 3. regenerate lightmap UVs

	[MenuItem(Menu + "3. Regenerate lightmap UVs of the un-batched meshes (optional)", priority = 3)]
	private static void RegenerateUVs()
	{
		if (EditorApplication.isPlayingOrWillChangePlaymode)
		{
			EditorUtility.DisplayDialog("Light Bake Fix", "Exit Play mode first.", "OK");
			return;
		}
		string folder = OutputFolderParent + "/" + OutputFolderName;
		if (!AssetDatabase.IsValidFolder(folder))
		{
			Debug.LogWarning(Tag + "Folder " + folder + " does not exist: run menu item 2 first.");
			return;
		}
		if (!EditorUtility.DisplayDialog("Light Bake Fix",
			"Replace the lightmap UVs (UV2) of every mesh under " + folder + " with freshly generated ones?\n\n" +
			"Only needed if a bake shows wrong shading that looks like overlapping or stretched lightmap UVs. It can take a few minutes.",
			"Regenerate", "Cancel"))
		{
			return;
		}
		try
		{
			string[] guids = AssetDatabase.FindAssets("t:Mesh", new[] { folder });
			HashSet<string> paths = new HashSet<string>();
			foreach (string guid in guids)
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			int done = 0, failed = 0, fileNumber = 0;
			foreach (string path in paths)
			{
				fileNumber++;
				Object[] objects = AssetDatabase.LoadAllAssetsAtPath(path);
				for (int i = 0; i < objects.Length; i++)
				{
					Mesh mesh = objects[i] as Mesh;
					if (mesh == null)
					{
						continue;
					}
					if (EditorUtility.DisplayCancelableProgressBar("Light Bake Fix", "File " + fileNumber + " / " + paths.Count + ": " + mesh.name, (float)i / objects.Length))
					{
						AssetDatabase.SaveAssets();
						Debug.LogWarning(Tag + "Cancelled after " + done + " meshes (those keep their new UVs).");
						return;
					}
					if (Unwrapping.GenerateSecondaryUVSet(mesh))
					{
						EditorUtility.SetDirty(mesh);
						done++;
					}
					else
					{
						failed++;
					}
				}
			}
			AssetDatabase.SaveAssets();
			Debug.Log(Tag + "Lightmap UVs regenerated for " + done + " meshes" + (failed > 0 ? " (" + failed + " could not be unwrapped and keep their previous UVs)" : "") + ". Bake again.");
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}
	}

	// ---------------------------------------------------------------- 4. bake settings

	[MenuItem(Menu + "4. Use a moderate lightmap resolution (optional)", priority = 4)]
	private static void ModerateSettings()
	{
		if (!Lightmapping.TryGetLightingSettings(out LightingSettings settings))
		{
			Debug.LogWarning(Tag + "The open scene has no Lighting Settings asset assigned (Window > Rendering > Lighting > Scene > New).");
			return;
		}
		if (!EditorUtility.DisplayDialog("Light Bake Fix",
			"Lighting Settings '" + settings.name + "': change Lightmap Resolution from " + settings.lightmapResolution.ToString("0.#") + " to " + ProposedResolution.ToString("0.#") +
			" texels per unit?\n\nNothing else is changed. You can set any other value later in Window > Rendering > Lighting.",
			"Change", "Cancel"))
		{
			return;
		}
		Undo.RecordObject(settings, "Lightmap resolution");
		float before = settings.lightmapResolution;
		settings.lightmapResolution = ProposedResolution;
		EditorUtility.SetDirty(settings);
		AssetDatabase.SaveAssetIfDirty(settings);
		Debug.Log(Tag + "Lighting Settings '" + settings.name + "': Lightmap Resolution " + before.ToString("0.#") + " -> " + ProposedResolution.ToString("0.#") +
			" texels per unit. Raise it later (20 - 40) only if the baked shadows look too blurry.", settings);
	}
}

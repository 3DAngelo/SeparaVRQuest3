// Suciedad de EntornoFix: pasa los materiales de Assets/Material/EntornoFix a "SeparaVR/Lit Dirty"
// (quita keywords/texturas que el shader recortado no usa, setea _DirtAmount y _CleanBrightness) y conecta CleanlinessController.
// La suciedad es procedural (sin textura); tamaño y color son #define en SeparaVR_LitDirtyInput.hlsl.
// Se puede volver a correr sobre materiales que ya usan el shader.
// En el editor _Cleanliness queda en 0 (sucio, como al empezar el juego) salvo con el menú de previsualización.
// Meta hornea siempre el albedo limpio, así que el valor no afecta al bake.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

[InitializeOnLoad]
public static class EntornoFixDirtSetup
{
    const string ShaderName = "SeparaVR/Lit Dirty";
    const string MatFolder = "Assets/Material/EntornoFix";
    const string RootName = "EntornoFix";
    const string Menu = "Tools/SeparaVR/EntornoFix/Suciedad/";
    static readonly string[] Passes = { "ShadowCaster", "DepthOnly", "MotionVectors", "XRMotionVectors" };

    static EntornoFixDirtSetup()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) SetEditorDefault();
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) SetEditorDefault(); };
    }

    // en el editor se ve como al empezar el juego (sucio)
    static void SetEditorDefault() { Shader.SetGlobalFloat("_Cleanliness", 0f); }

    // ---------------- materiales ----------------

    // keywords que el shader recortado todavía usa; el resto se quita
    static readonly HashSet<string> KeepKw = new HashSet<string> {
        "_ALPHATEST_ON", "_METALLICSPECGLOSSMAP", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A",
        "_SPECULARHIGHLIGHTS_OFF", "_ENVIRONMENTREFLECTIONS_OFF", "_RECEIVE_SHADOWS_OFF",
        "_SURFACE_TYPE_TRANSPARENT", "_ALPHAPREMULTIPLY_ON", "_ALPHAMODULATE_ON" };

    // texturas de Lit que ya no existen en el shader (se borran de las propiedades guardadas)
    static readonly Dictionary<string, string> RemovedTex = new Dictionary<string, string> {
        { "_BumpMap", "normal map" }, { "_ParallaxMap", "height map" }, { "_OcclusionMap", "occlusion map" },
        { "_EmissionMap", "emission map" }, { "_DetailMask", "detail mask" }, { "_DetailAlbedoMap", "detail albedo" },
        { "_DetailNormalMap", "detail normal" }, { "_SpecGlossMap", "specular map" }, { "_ClearCoatMap", "clear coat map" },
        { "_MainTex", "_MainTex (obsoleto)" }, { "_DirtMap", "dirt map (ahora procedural)" } };

    // floats que ya no existen en el shader
    static readonly HashSet<string> RemovedFloats = new HashSet<string> { "_DirtTiling" };

    [MenuItem(Menu + "Previsualizar (solo log)")]
    static void PreviewMats() { RunMats(false); }

    [MenuItem(Menu + "Aplicar shader de suciedad a materiales")]
    static void ApplyMats() { RunMats(true); }

    // cantidad de suciedad por material (por nombre)
    static float DirtFor(Material m, bool clip)
    {
        string n = m.name.ToLowerInvariant();
        if (clip) return 0.3f; // hojas / recortes
        if (n.Contains("baldosa") || n.Contains("cordon") || n.Contains("vereda") || n.Contains("concrete")) return 1f;
        if (n.Contains("casa") || n.Contains("edificio")) return 0.7f; // incluye edificioatras
        if (n.Contains("postebondi")) return 0.6f;
        return 0.8f;
    }

    // brillo del albedo limpio por material (1 = sin cambio). La vereda (BaldosaAlbedo) es muy oscura:
    // el sistema viejo la limpiaba con detalle x2 (~x2), así que limpio x2 para igualar a BaldosaDetail4.
    static float CleanBrightnessFor(Material m)
    {
        if (m.name == "M_BaldosaAlbedo") return 2f;
        return 1f;
    }

    static List<Material> LoadMats()
    {
        var list = new List<Material>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MatFolder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetDirectoryName(p).Replace('\\', '/') != MatFolder) continue; // solo directos
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) list.Add(m);
        }
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    // keywords guardadas (válidas + inválidas), sin depender del shader actual
    static HashSet<string> SavedKeywords(Material m)
    {
        var set = new HashSet<string>(m.shaderKeywords);
        var so = new SerializedObject(m);
        foreach (string arr in new[] { "m_ValidKeywords", "m_InvalidKeywords" })
        {
            var p = so.FindProperty(arr);
            if (p == null || !p.isArray) continue;
            for (int i = 0; i < p.arraySize; i++) set.Add(p.GetArrayElementAtIndex(i).stringValue);
        }
        return set;
    }

    // float guardado aunque el shader actual no tenga la propiedad
    static float SavedFloat(Material m, string name, float def)
    {
        var p = new SerializedObject(m).FindProperty("m_SavedProperties.m_Floats");
        if (p == null) return def;
        for (int i = 0; i < p.arraySize; i++)
        {
            var e = p.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("first").stringValue == name) return e.FindPropertyRelative("second").floatValue;
        }
        return def;
    }

    // borra de m_SavedProperties las texturas y floats que ya no usa el shader; devuelve lo que tenía asignado
    static List<string> StripRemovedTextures(Material m, bool apply)
    {
        var info = new List<string>();
        var so = new SerializedObject(m);
        var tex = so.FindProperty("m_SavedProperties.m_TexEnvs");
        if (tex == null) return info;
        for (int i = tex.arraySize - 1; i >= 0; i--)
        {
            var e = tex.GetArrayElementAtIndex(i);
            string name = e.FindPropertyRelative("first").stringValue;
            string label;
            if (!RemovedTex.TryGetValue(name, out label)) continue;
            var t = e.FindPropertyRelative("second.m_Texture");
            if (t != null && t.objectReferenceValue != null)
                info.Add("tenía " + label + " '" + t.objectReferenceValue.name + "', se quita");
            if (apply) tex.DeleteArrayElementAtIndex(i);
        }
        var floats = so.FindProperty("m_SavedProperties.m_Floats");
        if (floats != null && apply)
            for (int i = floats.arraySize - 1; i >= 0; i--)
                if (RemovedFloats.Contains(floats.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue))
                    floats.DeleteArrayElementAtIndex(i);
        if (apply)
        {
            var inv = so.FindProperty("m_InvalidKeywords");
            if (inv != null && inv.isArray) inv.ClearArray();
            so.ApplyModifiedProperties(); // registra Undo
        }
        return info;
    }

    static void RunMats(bool apply)
    {
        var sh = Shader.Find(ShaderName);
        if (sh == null) { Debug.LogError("[EntornoFix] No se encontró el shader '" + ShaderName + "' (¿compiló?)."); return; }
        var mats = LoadMats();
        if (mats.Count == 0) { Debug.LogError("[EntornoFix] No hay materiales en " + MatFolder); return; }

        var sb = new StringBuilder();
        sb.AppendLine("[EntornoFix] Suciedad materiales - " + (apply ? "APLICADO" : "PREVISUALIZACIÓN (sin cambios)") + " | " + mats.Count
            + " materiales | suciedad procedural (sin textura)");

        if (apply)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Shader suciedad EntornoFix");
        }
        int grp = Undo.GetCurrentGroup();

        foreach (var m in mats)
        {
            var kwBefore = SavedKeywords(m);
            bool clip = kwBefore.Contains("_ALPHATEST_ON") || SavedFloat(m, "_AlphaClip", 0f) > 0.5f;
            float oldDirt = m.shader == sh ? m.GetFloat("_DirtAmount") : -1f;
            float newDirt = DirtFor(m, clip);
            float oldBright = m.shader == sh ? m.GetFloat("_CleanBrightness") : -1f;
            float newBright = CleanBrightnessFor(m);
            var notes = new List<string>();

            // keywords finales: solo las que existen en el shader recortado
            var kwAfter = new HashSet<string>(kwBefore.Where(k => KeepKw.Contains(k)));
            var kwRemoved = kwBefore.Where(k => !KeepKw.Contains(k)).OrderBy(k => k).ToArray();
            if (kwRemoved.Length > 0) notes.Add("keywords quitadas: " + string.Join(", ", kwRemoved));
            if (kwBefore.Contains("_SPECULAR_SETUP"))
                notes.Add("AVISO: era workflow specular, pasa a metallic (_Metallic " + SavedFloat(m, "_Metallic", 0f) + ")");
            if (kwBefore.Contains("_EMISSION")) notes.Add("tenía emisión, se quita");
            notes.AddRange(StripRemovedTextures(m, false));

            string line = m.name + " | " + m.shader.name + " -> " + ShaderName + " | cola " + m.renderQueue
                + (clip ? " | alpha clip" : "") + " | _DirtAmount " + (oldDirt < 0 ? "(nuevo)" : oldDirt.ToString("0.##")) + " -> " + newDirt.ToString("0.##")
                + " | _CleanBrightness " + (oldBright < 0 ? "(nuevo)" : oldBright.ToString("0.##")) + " -> " + newBright.ToString("0.##");
            foreach (var n in notes) line += "\n    " + n;
            sb.AppendLine(line);
            if (!apply) continue;

            // guardar estado que el cambio de shader podría tocar
            int queue = m.renderQueue;
            string rtype = m.GetTag("RenderType", false, "");
            bool dsgi = m.doubleSidedGI;
            var passOn = Passes.Select(p => m.GetShaderPassEnabled(p)).ToArray();

            Undo.RecordObject(m, "Shader suciedad");
            if (m.shader != sh) m.shader = sh;

            if (m.GetTexture("_MetallicGlossMap") != null) kwAfter.Add("_METALLICSPECGLOSSMAP");
            else kwAfter.Remove("_METALLICSPECGLOSSMAP");
            m.shaderKeywords = kwAfter.ToArray();

            m.renderQueue = queue;
            if (rtype.Length > 0) m.SetOverrideTag("RenderType", rtype);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; // sin GI emisivo
            m.doubleSidedGI = dsgi;
            for (int i = 0; i < Passes.Length; i++) m.SetShaderPassEnabled(Passes[i], passOn[i]);

            m.SetFloat("_DirtAmount", newDirt);
            m.SetFloat("_CleanBrightness", newBright);
            EditorUtility.SetDirty(m);

            // al final: borrar texturas/floats viejos (incluye _DirtMap/_DirtTiling) y keywords inválidas (SerializedObject nuevo, con el shader ya cambiado)
            StripRemovedTextures(m, true);
        }

        if (apply)
        {
            Undo.CollapseUndoOperations(grp);
            AssetDatabase.SaveAssets();
        }
        Debug.Log(sb.ToString());
    }

    // ---------------- gameplay ----------------

    [MenuItem(Menu + "Conectar al gameplay")]
    static void Connect()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var gm = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gm == null) { Debug.LogError("[EntornoFix] No hay GameManager en la escena."); return; }

        var sb = new StringBuilder();
        sb.AppendLine("[EntornoFix] Conectar suciedad al gameplay");
        int steps = gm.evolveScores != null && gm.evolveScores.Length > 0 ? gm.evolveScores.Length : 5;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Conectar suciedad EntornoFix");
        int grp = Undo.GetCurrentGroup();

        GameObject target;
        var env = gm.enviroment;
        if (env != null && env.gameObject.activeInHierarchy)
        {
            // el entorno actual está activo: el controlador va ahí, sin tocar la referencia
            target = env.gameObject;
            sb.AppendLine("GameManager.enviroment ('" + env.name + "') está activo: se agrega el controlador ahí.");
        }
        else
        {
            if (env != null)
            {
                sb.AppendLine("GameManager.enviroment ('" + env.name + "') está INACTIVO: hoy CheckEvolve no encuentra ningún IEvolve.");
                bool blocked = false;
                foreach (var e in env.GetComponentsInChildren<IEvolve>(true))
                {
                    sb.AppendLine("  " + FullPath(e.transform) + " | " + e.GetType().Name + " | activo: " + e.gameObject.activeInHierarchy);
                    if (e.gameObject.activeInHierarchy && !(e is MaterialEvolve)) blocked = true;
                }
                if (blocked)
                {
                    Debug.LogError(sb + "Hay IEvolve activos (no MaterialEvolve) bajo el entorno actual: no se cambia la referencia.");
                    return;
                }
            }
            else sb.AppendLine("GameManager.enviroment es null.");

            target = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (target == null) { Debug.LogError(sb + "No se encontró '" + RootName + "' en la raíz de la escena."); return; }

            Undo.RecordObject(gm, "Conectar suciedad");
            gm.enviroment = target.transform;
            if (PrefabUtility.IsPartOfPrefabInstance(gm)) PrefabUtility.RecordPrefabInstancePropertyModifications(gm);
            sb.AppendLine("GameManager.enviroment -> '" + RootName + "'.");
        }

        var ctrl = target.GetComponent<CleanlinessController>();
        if (ctrl == null)
        {
            ctrl = Undo.AddComponent<CleanlinessController>(target);
            sb.AppendLine("CleanlinessController agregado a '" + target.name + "'.");
        }
        else sb.AppendLine("CleanlinessController ya existía en '" + target.name + "', se actualiza.");
        Undo.RecordObject(ctrl, "Conectar suciedad");
        ctrl.steps = steps;
        if (PrefabUtility.IsPartOfPrefabInstance(ctrl)) PrefabUtility.RecordPrefabInstancePropertyModifications(ctrl);
        sb.AppendLine("steps = " + steps + " (evolveScores.Length). groundY = " + ctrl.groundY + " (revisar altura del suelo).");

        // avisos
        foreach (var e in target.GetComponentsInChildren<IEvolve>(true))
            if (e != ctrl) sb.AppendLine("AVISO: otro IEvolve bajo '" + target.name + "': " + FullPath(e.transform) + " (" + e.GetType().Name + ") también recibirá Evolve().");
        var all = Object.FindObjectsByType<CleanlinessController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all.Length > 1) sb.AppendLine("AVISO: hay " + all.Length + " CleanlinessController en la escena; dejar solo uno.");
        foreach (var t in Object.FindObjectsByType<TestEvolve>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            sb.AppendLine("AVISO: TestEvolve en '" + FullPath(t.transform) + "' sigue apuntando a su 'entorno' anterior (no se modifica).");

        Undo.CollapseUndoOperations(grp);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log(sb.ToString());
    }

    static string FullPath(Transform t)
    {
        string p = t.name;
        for (t = t.parent; t != null; t = t.parent) p = t.name + "/" + p;
        return p;
    }

    // ---------------- previsualización ----------------

    [MenuItem(Menu + "Previsualizar limpieza en editor/0 (sucio)")]
    static void Prev0() { Preview(0f); }

    [MenuItem(Menu + "Previsualizar limpieza en editor/0.5")]
    static void Prev05() { Preview(0.5f); }

    [MenuItem(Menu + "Previsualizar limpieza en editor/1 (limpio)")]
    static void Prev1() { Preview(1f); }

    static void Preview(float v)
    {
        Shader.SetGlobalFloat("_Cleanliness", v);
        SceneView.RepaintAll();
        InternalEditorUtility.RepaintAllViews();
        Debug.Log("[EntornoFix] _Cleanliness = " + v + " (se vuelve a 0 al recompilar o salir de Play)");
    }
}

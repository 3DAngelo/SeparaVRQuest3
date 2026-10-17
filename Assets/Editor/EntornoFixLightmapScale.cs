// Ajusta "Scale In Lightmap" de cada renderer bajo EntornoFix según categoría (por nombre)
// y distancia horizontal (XZ) a la cámara de inicio del jugador. Los cambios quedan como overrides de prefab.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class EntornoFixLightmapScale
{
    const string RootName = "EntornoFix";
    const string ReportFile = "Logs/EntornoFixLightmapScale.txt"; // relativo a la carpeta del proyecto
    static readonly Vector3 FallbackCamPos = new Vector3(-8.09f, 0f, 8.56f);

    // ---- límites de bandas (m) ----
    const float CercaMax = 35f, OtraCuadraMax = 80f, MediaMax = 150f;

    // ---- escalas [cerca, otra cuadra, media, lejos] ----
    static readonly float[] EscSuelo = { 1.0f, 0.3f, 0.15f, 0.1f };
    static readonly float[] EscCasa = { 1.0f, 0.2f, 0.1f, 0.1f };
    static readonly float[] EscCasaEsquina = { 0.3f, 0.2f, 0.1f, 0.1f }; // esquina de la manzana propia 0.3; adyacentes como el resto de las casas
    static readonly float[] EscEdificio = { 0.2f, 0.2f, 0.1f, 0.1f };
    static readonly float[] EscArbol = { 0.3f, 0.1f, 0.1f, 0.1f };
    static readonly float[] EscProp = { 0.5f, 0.1f, 0.1f, 0.1f };
    static readonly float[] EscOtros = { 1.0f, 0.2f, 0.1f, 0.1f };
    const float EscPlano = 0.1f;

    static readonly string[] BandNames = { "CERCA", "OTRA_CUADRA", "MEDIA", "LEJOS" };
    static readonly string[] CatNames = { "Suelo", "Casa", "CasaEsquina", "Edificio", "Arbol", "Prop", "Plano", "Otros" };

    [MenuItem("Tools/SeparaVR/EntornoFix/Previsualizar escala lightmap (solo log)")]
    static void Preview() { Run(false); }

    [MenuItem("Tools/SeparaVR/EntornoFix/Aplicar escala lightmap")]
    static void Apply() { Run(true); }

    class Row
    {
        public Renderer r;
        public string name, cat;
        public int catIdx, band;
        public float dist, oldScale, newScale, area;
        public bool skipped;
        public string skipWhy = "";
    }

    static bool Starts(string n, string p) { return n.StartsWith(p, StringComparison.OrdinalIgnoreCase); }

    // devuelve índice de categoría para un nombre, o -1 si no hay coincidencia
    static int CategoryOf(string n)
    {
        if (string.Equals(n, "Plane001", StringComparison.OrdinalIgnoreCase)) return 6;
        if (Starts(n, "CasaEsquina")) return 2;
        if (Starts(n, "Edificio")) return 3;
        if (Starts(n, "CasaBaja") || Starts(n, "CasaMedia") || Starts(n, "CasaGrande")) return 1;
        if (Starts(n, "PosteBondi")) return 5;
        if (Starts(n, "Arbol") || Starts(n, "Willow") || Starts(n, "Tree")) return 4;
        if (Starts(n, "vereda") || Starts(n, "cordon") || Starts(n, "bajadas") || Starts(n, "canterito")) return 0;
        return -1;
    }

    static int BandOf(float d)
    {
        if (d <= CercaMax) return 0;
        if (d <= OtraCuadraMax) return 1;
        if (d <= MediaMax) return 2;
        return 3;
    }

    static float ScaleFor(int cat, int band)
    {
        switch (cat)
        {
            case 0: return EscSuelo[band];
            case 1: return EscCasa[band];
            case 2: return EscCasaEsquina[band];
            case 3: return EscEdificio[band];
            case 4: return EscArbol[band];
            case 5: return EscProp[band];
            case 6: return EscPlano;
            default: return EscOtros[band];
        }
    }

    static Vector3 FindCameraPos(out string source)
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (!go.activeInHierarchy || !Starts(go.name, "XR Origin")) continue;
            var cam = go.GetComponentInChildren<Camera>(true);
            source = cam != null ? "Camera hija de '" + go.name + "'" : "transform de '" + go.name + "'";
            return cam != null ? cam.transform.position : go.transform.position;
        }
        if (Camera.main != null) { source = "Camera.main"; return Camera.main.transform.position; }
        source = "valor por defecto";
        return FallbackCamPos;
    }

    static float DistXZ(Bounds b, Vector3 p)
    {
        float cx = Mathf.Clamp(p.x, b.min.x, b.max.x), cz = Mathf.Clamp(p.z, b.min.z, b.max.z);
        float dx = p.x - cx, dz = p.z - cz;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    static void Run(bool apply)
    {
        var scene = EditorSceneManager.GetActiveScene();
        GameObject root = null;
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == RootName) { root = go; break; }
        if (root == null) root = GameObject.Find(RootName);
        if (root == null) { Debug.LogError("[EntornoFix] No se encontró el objeto '" + RootName + "' en la escena."); return; }

        string src;
        Vector3 cam = FindCameraPos(out src);

        var rows = new List<Row>();
        var otros = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var row = new Row { r = r, name = r.gameObject.name };
            // categoría: nombre propio, luego ancestros hasta (sin incluir) la raíz
            int cat = -1;
            for (var t = r.transform; t != null && t != root.transform && cat < 0; t = t.parent)
                cat = CategoryOf(t.gameObject.name);
            if (cat < 0) { cat = 7; otros.Add(row.name); }
            row.catIdx = cat; row.cat = CatNames[cat];

            var b = r.bounds;
            row.dist = DistXZ(b, cam);
            row.band = BandOf(row.dist);
            row.newScale = ScaleFor(cat, row.band);
            var so = new SerializedObject(r);
            var p = so.FindProperty("m_ScaleInLightmap");
            row.oldScale = p != null ? p.floatValue : 1f;

            if ((GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) == 0)
            { row.skipped = true; row.skipWhy = "sin ContributeGI"; }
            else if (r.receiveGI == ReceiveGI.LightProbes)
            { row.skipped = true; row.skipWhy = "receiveGI=LightProbes"; }

            var s = b.size;
            row.area = s.x * s.z + s.y * Mathf.Max(s.x, s.z); // proxy de superficie
            rows.Add(row);
        }
        rows.Sort((a, c) => a.dist.CompareTo(c.dist));

        int changed = 0, unchanged = 0, skipped = 0;
        double areaBefore = 0, areaAfter = 0;
        var counts = new int[CatNames.Length, 4];
        foreach (var row in rows)
        {
            if (row.skipped) { skipped++; continue; }
            counts[row.catIdx, row.band]++;
            areaBefore += row.oldScale * row.oldScale * row.area;
            areaAfter += row.newScale * row.newScale * row.area;
            if (Mathf.Approximately(row.oldScale, row.newScale)) unchanged++; else changed++;
        }

        if (apply)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Escala lightmap EntornoFix");
            int grp = Undo.GetCurrentGroup();
            foreach (var row in rows)
            {
                if (row.skipped || Mathf.Approximately(row.oldScale, row.newScale)) continue;
                var so = new SerializedObject(row.r);
                var p = so.FindProperty("m_ScaleInLightmap");
                if (p == null) continue;
                p.floatValue = row.newScale;
                so.ApplyModifiedProperties(); // registra Undo
            }
            Undo.CollapseUndoOperations(grp);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        var sb = new StringBuilder();
        sb.AppendLine("[EntornoFix] Escala lightmap - " + (apply ? "APLICADO" : "PREVISUALIZACIÓN (sin cambios)"));
        sb.AppendLine("Cámara: " + src + " en " + cam.ToString("F2") + " (distancia en XZ, metros)");
        sb.AppendLine("Bandas: CERCA<=" + CercaMax + " OTRA_CUADRA<=" + OtraCuadraMax + " MEDIA<=" + MediaMax + " LEJOS>");
        sb.AppendLine("Renderers: " + rows.Count + " | cambiados: " + changed + " | sin cambio: " + unchanged + " | saltados: " + skipped);
        sb.AppendLine();
        sb.AppendLine("Categoría x banda (cantidad -> escala):");
        for (int c = 0; c < CatNames.Length; c++)
        {
            var line = new StringBuilder("  " + CatNames[c].PadRight(12));
            for (int bd = 0; bd < 4; bd++)
                line.Append(BandNames[bd] + ": " + counts[c, bd] + " -> " + F(ScaleFor(c, bd)) + "   ");
            sb.AppendLine(line.ToString());
        }
        sb.AppendLine();
        sb.AppendLine("Nombres en 'Otros': " + (otros.Count == 0 ? "(ninguno)" : string.Join(", ", new List<string>(otros).ToArray())));
        sb.AppendLine("Área lightmap estimada (sum escala^2 * (x*z + y*max(x,z))): antes " + areaBefore.ToString("F0", CultureInfo.InvariantCulture)
            + " -> después " + areaAfter.ToString("F0", CultureInfo.InvariantCulture)
            + " (ratio " + (areaBefore > 0 ? (areaAfter / areaBefore).ToString("0.000", CultureInfo.InvariantCulture) : "n/a") + ")");
        sb.AppendLine();
        sb.AppendLine("nombre | categoria | dist m | escala anterior -> nueva");
        foreach (var row in rows)
            sb.AppendLine(row.name + " | " + row.cat + " | " + row.dist.ToString("F1", CultureInfo.InvariantCulture) + " | "
                + F(row.oldScale) + " -> " + F(row.newScale) + (row.skipped ? "  [SALTADO: " + row.skipWhy + "]" : ""));

        string text = sb.ToString();
        Debug.Log(text);
        try
        {
            string projDir = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(projDir, ReportFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, Encoding.UTF8);
            Debug.Log("[EntornoFix] Reporte escrito en " + path);
        }
        catch (Exception e) { Debug.LogWarning("[EntornoFix] No se pudo escribir el reporte: " + e.Message); }
    }
}

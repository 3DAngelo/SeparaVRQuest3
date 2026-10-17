// Convierte los materiales embebidos de EntornoFix.fbx a URP Lit (Assets/Material/EntornoFix/)
// y remapea el FBX a ellos. Materiales con el mismo difuso se colapsan en uno solo.
// No toca Assets/Material/<name>.mat (solo los lee como fuente).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using RenderQueue = UnityEngine.Rendering.RenderQueue;

public static class EntornoFixMaterialConverter
{
    const string FbxPath = "Assets/Mesh/EntornoFix.fbx";
    const string ProjectMatFolder = "Assets/Material";
    const string DestFolder = "Assets/Material/EntornoFix";
    const string TexFolder = "Assets/Texture2D";
    const string ReportPath = DestFolder + "/_reporte_conversion.txt";
    const float MaxSmoothness = 0.5f;
    const float DefaultSmoothness = 0.15f;

    [MenuItem("Tools/SeparaVR/EntornoFix/Previsualizar conversión (solo log)")]
    static void Preview() { Run(false); }

    [MenuItem("Tools/SeparaVR/EntornoFix/Convertir materiales a URP Lit")]
    static void Convert() { Run(true); }

    // ---- datos planos de cada material ----
    class Slot
    {
        public Texture tex;
        public Vector2 scale = Vector2.one;
        public Vector2 offset = Vector2.zero;
    }

    class Spec
    {
        public string name, origen, shaderOrigen, destName = "-";
        public Slot baseMap = new Slot(), bump = new Slot(), detAlb = new Slot(), detNrm = new Slot(),
                    detMask = new Slot(), emiMap = new Slot();
        public Color baseColor = Color.white, emiColor = Color.black;
        public float bumpScale = 1f, detAlbScale = 1f, detNrmScale = 1f;
        public float smoothness = DefaultSmoothness, cutoff = 0.5f;
        public bool alphaClip, emission;
        public List<string> warnings = new List<string>();
    }

    // grupo = un material destino
    class Group
    {
        public string[] key;
        public string keyStr, destName;
        public List<Spec> members = new List<Spec>();
        public Spec Rep { get { return members[0]; } }
    }

    static void Run(bool convert)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
            throw new InvalidOperationException("[EntornoFix] No se encontró el shader 'Universal Render Pipeline/Lit'.");

        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException("[EntornoFix] No se encontró ModelImporter en " + FbxPath);

        // Nombres: embebidos + ya remapeados (para que la 2ª ejecución sea idempotente)
        var embedded = new Dictionary<string, Material>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
            if (o is Material m && !string.IsNullOrEmpty(m.name)) embedded[m.name] = m;
        var remapped = new Dictionary<string, Material>();
        foreach (var kv in importer.GetExternalObjectMap())
            if (kv.Key.type == typeof(Material) && kv.Value is Material rm) remapped[kv.Key.name] = rm;
        var names = new SortedSet<string>(embedded.Keys, StringComparer.Ordinal);
        foreach (var n in remapped.Keys) names.Add(n);

        if (names.Count == 0)
        {
            Debug.LogWarning("[EntornoFix] El FBX no tiene materiales.");
            return;
        }

        var texIndex = BuildTextureIndex();
        var specs = new List<Spec>();
        var temps = new List<Material>();
        try
        {
            // 1) Recolectar TODO antes de tocar nada (el remap invalida los embebidos)
            foreach (var n in names)
            {
                Material src = AssetDatabase.LoadAssetAtPath<Material>(ProjectMatFolder + "/" + n + ".mat");
                string origen = "proyecto";
                if (src == null && embedded.TryGetValue(n, out var emb) && AssetDatabase.GetAssetPath(emb) == FbxPath)
                {
                    src = emb;
                    origen = "embebido";
                }
                if (src == null && remapped.TryGetValue(n, out var prev) && prev != null)
                {
                    src = prev;
                    origen = "remapeado";
                }
                if (src == null)
                {
                    Debug.LogError("[EntornoFix] Sin fuente para '" + n + "', se omite.");
                    continue;
                }
                var clone = new Material(src) { hideFlags = HideFlags.HideAndDontSave }; // snapshot
                temps.Add(clone);
                specs.Add(BuildSpec(n, origen, clone, texIndex));
            }

            // 2) Agrupar por difuso
            var groups = BuildGroups(specs);

            var report = BuildReport(specs, groups, convert);
            Debug.Log(report);
            if (!convert) return;

            // 3) Crear/actualizar materiales destino (uno por grupo)
            EnsureFolder(DestFolder);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var g in groups)
                {
                    string destPath = DestFolder + "/" + g.destName + ".mat";
                    var dest = AssetDatabase.LoadAssetAtPath<Material>(destPath);
                    bool isNew = dest == null;
                    if (isNew) dest = new Material(lit);
                    Apply(g.Rep, dest, lit);
                    if (isNew) AssetDatabase.CreateAsset(dest, destPath);
                    else EditorUtility.SetDirty(dest);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();

            // 4) Informe
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReportPath),
                report, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ReportPath);

            // 5) Remap en el FBX y reimport único
            importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            int remaps = 0;
            foreach (var g in groups)
            {
                var dest = AssetDatabase.LoadAssetAtPath<Material>(DestFolder + "/" + g.destName + ".mat");
                if (dest == null)
                    throw new InvalidOperationException("[EntornoFix] No se pudo cargar el destino " + g.destName);
                foreach (var s in g.members)
                {
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), s.name), dest);
                    remaps++;
                }
            }
            importer.SaveAndReimport();
            Debug.Log("[EntornoFix] Conversión terminada: " + remaps + " materiales remapeados a " + groups.Count +
                      " destinos en " + FbxPath);
        }
        finally
        {
            foreach (var t in temps) if (t != null) UnityEngine.Object.DestroyImmediate(t);
        }
    }

    // ---- agrupado por difuso ----
    // alpha clip no forma parte de la clave: tronco y hojas con el mismo difuso van a un solo material cutout
    static readonly string[] KeyLabels = { "difuso", "tiling/offset", "detalle", "color" };

    static string TexId(Texture t)
    {
        if (t == null) return "";
        string p = AssetDatabase.GetAssetPath(t);
        return string.IsNullOrEmpty(p) ? "obj:" + t.name : AssetDatabase.AssetPathToGUID(p);
    }

    static string F4(float v) { return v.ToString("F4", CultureInfo.InvariantCulture); }

    static string[] MakeKey(Spec s)
    {
        if (s.baseMap.tex == null) // sin difuso: nunca se fusiona
            return new[] { "alone:" + s.name, "", "", "" };
        return new[]
        {
            TexId(s.baseMap.tex),
            F4(s.baseMap.scale.x) + "," + F4(s.baseMap.scale.y) + "," + F4(s.baseMap.offset.x) + "," + F4(s.baseMap.offset.y),
            TexId(s.detAlb.tex),
            F4(s.baseColor.r) + "," + F4(s.baseColor.g) + "," + F4(s.baseColor.b) + "," + F4(s.baseColor.a)
        };
    }

    static int CompareMembers(Spec a, Spec b)
    {
        int pa = a.origen == "proyecto" ? 0 : 1, pb = b.origen == "proyecto" ? 0 : 1;
        if (pa != pb) return pa.CompareTo(pb);
        return string.CompareOrdinal(a.name, b.name);
    }

    static string SafeName(string n)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return n;
    }

    static List<Group> BuildGroups(List<Spec> specs)
    {
        var byKey = new Dictionary<string, Group>();
        foreach (var s in specs)
        {
            var key = MakeKey(s);
            var ks = string.Join("|", key);
            if (!byKey.TryGetValue(ks, out var g))
            {
                g = new Group { key = key, keyStr = ks };
                byKey[ks] = g;
            }
            g.members.Add(s);
        }
        var groups = new List<Group>(byKey.Values);
        groups.Sort((a, b) => string.CompareOrdinal(a.keyStr, b.keyStr));
        foreach (var g in groups)
        {
            g.members.Sort(CompareMembers);
            // si algún miembro recorta alpha, el grupo entero es cutout
            var clipSrc = g.members.Find(m => m.alphaClip);
            if (clipSrc != null)
                foreach (var s in g.members) { s.alphaClip = true; s.cutoff = clipSrc.cutoff; }
        }

        // mismo difuso pero clave distinta: avisar qué difiere
        var byTex = new Dictionary<string, List<Group>>();
        foreach (var g in groups)
        {
            if (g.Rep.baseMap.tex == null) continue;
            if (!byTex.TryGetValue(g.key[0], out var l)) { l = new List<Group>(); byTex[g.key[0]] = l; }
            l.Add(g);
        }
        foreach (var kv in byTex)
        {
            var l = kv.Value;
            if (l.Count < 2) continue;
            var diffs = new List<string>();
            for (int i = 1; i < KeyLabels.Length; i++)
                for (int j = 1; j < l.Count; j++)
                    if (l[j].key[i] != l[0].key[i]) { diffs.Add(KeyLabels[i]); break; }
            string msg = "mismo difuso, no colapsado por: " + string.Join(", ", diffs);
            Debug.LogWarning("[EntornoFix] " + l[0].Rep.baseMap.tex.name + " -> " + msg);
            foreach (var g in l) foreach (var s in g.members) s.warnings.Add(msg);
        }

        // nombres destino, deterministas y únicos sin distinguir mayúsculas
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            string baseName = "M_" + SafeName(g.Rep.baseMap.tex != null ? g.Rep.baseMap.tex.name : g.Rep.name);
            string cand = baseName;
            for (int i = 1; used.Contains(cand); i++) cand = baseName + "_" + i;
            used.Add(cand);
            g.destName = cand;
            foreach (var s in g.members) s.destName = cand;
        }
        return groups;
    }

    // ---- lectura de la fuente ----
    static Spec BuildSpec(string name, string origen, Material src, Dictionary<string, string> texIndex)
    {
        string shaderName = src.shader != null ? src.shader.name : "null";
        var s = new Spec { name = name, origen = origen, shaderOrigen = shaderName };
        bool isMax = src.HasProperty("_BASE_COLOR_MAP") || src.HasProperty("_BASE_COLOR"); // PhysicalMaterial3DsMax

        ReadTex(src, s.baseMap, new[] { "_BaseMap", "_MainTex", "_BASE_COLOR_MAP" });
        ReadTex(src, s.bump, new[] { "_BumpMap", "_NormalMap", "_BUMP_MAP" });
        ReadTex(src, s.detAlb, new[] { "_DetailAlbedoMap" });
        ReadTex(src, s.detNrm, new[] { "_DetailNormalMap" });
        ReadTex(src, s.detMask, new[] { "_DetailMask" });
        ReadTex(src, s.emiMap, new[] { "_EmissionMap", "_EMISSION_COLOR_MAP" });

        s.baseColor = ReadColor(src, new[] { "_BaseColor", "_Color", "_BASE_COLOR" }, Color.white);
        // en el shader de 3ds Max el color solo se usa si no hay mapa
        if (isMax && s.baseMap.tex != null && !src.HasProperty("_BaseColor") && !src.HasProperty("_Color"))
            s.baseColor = Color.white;
        s.bumpScale = ReadFloat(src, new[] { "_BumpScale", "_BUMP_MAP_STRENGTH" }, 1f);
        s.detAlbScale = ReadFloat(src, new[] { "_DetailAlbedoMapScale" }, 1f);
        s.detNrmScale = ReadFloat(src, new[] { "_DetailNormalMapScale" }, 1f);

        // suavidad: nunca por encima de 0.5
        float sm;
        if (src.HasProperty("_Smoothness") || src.HasProperty("_Glossiness"))
            sm = ReadFloat(src, new[] { "_Smoothness", "_Glossiness" }, DefaultSmoothness);
        else if (src.HasProperty("_REFLECTIONS_ROUGHNESS"))
            sm = 1f - src.GetFloat("_REFLECTIONS_ROUGHNESS");
        else sm = DefaultSmoothness;
        s.smoothness = Mathf.Clamp(sm, 0f, MaxSmoothness);

        // emisión: mapa o color no negro Y (keyword _EMISSION, o _EMISSION_WEIGHT>0 en shader de 3ds Max)
        Color ec = ReadColor(src, new[] { "_EmissionColor", "_EMISSION_COLOR" }, Color.black);
        bool emiData = s.emiMap.tex != null || ec.maxColorComponent > 0.0001f;
        bool emiOn = src.HasProperty("_EMISSION_WEIGHT")
            ? src.GetFloat("_EMISSION_WEIGHT") > 0f
            : src.IsKeywordEnabled("_EMISSION");
        s.emission = emiData && emiOn;
        if (emiData && !emiOn) s.warnings.Add("emisión desactivada en origen (ignorada)");
        if (s.emission) s.emiColor = ec.maxColorComponent > 0.0001f ? ec : Color.white;
        else { s.emiMap = new Slot(); s.emiColor = Color.black; }

        // alpha clip: hojas, _AlphaClip, keyword, cola AlphaTest, shader Transparent o mapa de transparencia.
        // (_Cutoff>0 a secas NO se usa: Lit/SimpleLit lo traen siempre a 0.5)
        int q = src.renderQueue;
        bool srcClip = (src.HasProperty("_AlphaClip") && src.GetFloat("_AlphaClip") >= 0.5f)
                       || src.IsKeywordEnabled("_ALPHATEST_ON")
                       || (q >= (int)RenderQueue.AlphaTest && q < (int)RenderQueue.Transparent)
                       || shaderName.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0
                       || (src.HasProperty("_TRANSPARENCY_MAP") && src.GetTexture("_TRANSPARENCY_MAP") != null);
        s.alphaClip = name.IndexOf("Leaf", StringComparison.OrdinalIgnoreCase) >= 0 || srcClip;
        float cut = src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0.5f;
        s.cutoff = cut > 0f ? cut : 0.5f;

        // textura base ausente: buscar por nombre en Assets/Texture2D
        if (s.baseMap.tex == null)
        {
            if (texIndex.TryGetValue(name.ToLowerInvariant(), out var tp))
            {
                s.baseMap.tex = AssetDatabase.LoadAssetAtPath<Texture>(tp);
                s.warnings.Add("base map por nombre: " + tp);
            }
            else s.warnings.Add("SIN base map");
        }
        if (s.baseMap.tex != null && AssetDatabase.GetAssetPath(s.baseMap.tex) == FbxPath)
            s.warnings.Add("base map es subasset del FBX");
        return s;
    }

    static void ReadTex(Material m, Slot slot, string[] names)
    {
        string first = null;
        foreach (var n in names)
        {
            if (!m.HasProperty(n)) continue;
            if (first == null) first = n;
            var t = m.GetTexture(n);
            if (t == null) continue;
            slot.tex = t;
            slot.scale = m.GetTextureScale(n);
            slot.offset = m.GetTextureOffset(n);
            return;
        }
        if (first != null)
        {
            slot.scale = m.GetTextureScale(first);
            slot.offset = m.GetTextureOffset(first);
        }
    }

    static Color ReadColor(Material m, string[] names, Color def)
    {
        foreach (var n in names) if (m.HasProperty(n)) return m.GetColor(n);
        return def;
    }

    static float ReadFloat(Material m, string[] names, float def)
    {
        foreach (var n in names) if (m.HasProperty(n)) return m.GetFloat(n);
        return def;
    }

    // ---- escritura en el destino ----
    static void Apply(Spec s, Material d, Shader lit)
    {
        d.shader = lit;

        SetTex(d, "_BaseMap", s.baseMap);
        SetTex(d, "_BumpMap", s.bump);
        SetTex(d, "_DetailAlbedoMap", s.detAlb);
        SetTex(d, "_DetailNormalMap", s.detNrm);
        SetTex(d, "_DetailMask", s.detMask);
        SetTex(d, "_EmissionMap", s.emiMap);

        d.SetColor("_BaseColor", s.baseColor);
        d.SetFloat("_BumpScale", s.bumpScale);
        d.SetFloat("_DetailAlbedoMapScale", s.detAlbScale);
        d.SetFloat("_DetailNormalMapScale", s.detNrmScale);

        d.SetFloat("_WorkflowMode", 1f);
        d.SetFloat("_Metallic", 0f);
        d.SetFloat("_Smoothness", s.smoothness);
        d.SetTexture("_MetallicGlossMap", null);
        d.SetTexture("_SpecGlossMap", null);

        d.SetFloat("_Surface", 0f); // opaco
        d.SetFloat("_AlphaClip", s.alphaClip ? 1f : 0f);
        d.SetFloat("_Cutoff", s.cutoff);
        d.SetFloat("_Cull", s.alphaClip ? 0f : 2f);
        d.SetColor("_EmissionColor", s.emiColor);
        d.globalIlluminationFlags = s.emission ? MaterialGlobalIlluminationFlags.BakedEmissive
                                               : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        // keywords como los pondría el inspector de URP Lit
        BaseShaderGUI.SetMaterialKeywords(d,
            UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords, SetDetailKeywords);

        // forzar estado final
        SetKw(d, "_ALPHATEST_ON", s.alphaClip);
        d.renderQueue = s.alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
        if (!s.emission)
        {
            d.DisableKeyword("_EMISSION");
            d.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        d.enableInstancing = false;
    }

    static void SetTex(Material d, string prop, Slot slot)
    {
        d.SetTexture(prop, slot.tex);
        d.SetTextureScale(prop, slot.scale);
        d.SetTextureOffset(prop, slot.offset);
    }

    // equivalente a LitDetailGUI.SetMaterialKeywords (esa clase es internal)
    static void SetDetailKeywords(Material m)
    {
        bool has = m.GetTexture("_DetailAlbedoMap") != null || m.GetTexture("_DetailNormalMap") != null;
        bool scaled = m.GetFloat("_DetailAlbedoMapScale") != 1f;
        SetKw(m, "_DETAIL_MULX2", has && !scaled);
        SetKw(m, "_DETAIL_SCALED", has && scaled);
    }

    static void SetKw(Material m, string kw, bool on)
    {
        if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
    }

    // ---- utilidades ----
    static Dictionary<string, string> BuildTextureIndex()
    {
        var idx = new Dictionary<string, string>();
        foreach (var g in AssetDatabase.FindAssets("t:Texture", new[] { TexFolder }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var k = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            if (!idx.ContainsKey(k)) idx[k] = p;
        }
        return idx;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static string TexName(Slot s) { return s.tex != null ? s.tex.name : "-"; }

    static string BuildReport(List<Spec> specs, List<Group> groups, bool convert)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[EntornoFix] " + (convert ? "CONVERSIÓN" : "PREVISUALIZACIÓN") + " - " + FbxPath);
        sb.AppendLine("nombre | fuente | shader origen | base | normal | detalle | alpha clip | destino | avisos");
        int warns = 0, clips = 0, proj = 0, emb = 0;
        foreach (var s in specs)
        {
            string det = (s.detAlb.tex != null || s.detNrm.tex != null)
                ? TexName(s.detAlb) + "/" + TexName(s.detNrm) : "-";
            sb.AppendLine(s.name + " | " + s.origen + " | " + s.shaderOrigen + " | " + TexName(s.baseMap) + " | " +
                          TexName(s.bump) + " | " + det + " | " +
                          (s.alphaClip ? "si(" + s.cutoff.ToString("0.##", CultureInfo.InvariantCulture) + ")" : "no") +
                          " | " + s.destName + " | " + (s.warnings.Count > 0 ? string.Join("; ", s.warnings) : "-"));
            if (s.warnings.Count > 0) warns++;
            if (s.alphaClip) clips++;
            if (s.origen == "proyecto") proj++; else if (s.origen == "embebido") emb++;
        }
        sb.AppendLine("Grupos:");
        foreach (var g in groups)
        {
            var mem = new List<string>();
            foreach (var s in g.members) mem.Add(s.name);
            sb.AppendLine(DestFolder + "/" + g.destName + ".mat <- " + string.Join(", ", mem));
        }
        sb.AppendLine("Resumen: " + specs.Count + " materiales origen | " + groups.Count + " materiales destino | " +
                      proj + " desde proyecto | " + emb + " desde embebido | " + clips + " alpha clip | " +
                      warns + " con avisos");
        return sb.ToString();
    }
}

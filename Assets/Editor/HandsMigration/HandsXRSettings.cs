// Hand-tracking migration: enables the OpenXR features needed for hands on Standalone (Meta Link) and Android (Quest),
// and makes sure the Android target has the OpenXR loader. Only ever turns things ON; it never disables a feature or profile.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

public static class HandsXRSettings
{
    static readonly string[] Both =
    {
        "UnityEngine.XR.Hands.OpenXR.HandTracking",
        "UnityEngine.XR.Hands.OpenXR.MetaHandTrackingAim",
        "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile",
        "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchProControllerProfile",
    };
    static readonly string[] AndroidOnly = { "UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature" };

    public static XRGeneralSettingsPerBuildTarget PerTarget()
    {
        XRGeneralSettingsPerBuildTarget per;
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out per) || per == null)
            per = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>("Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
        return per;
    }

    public static void Report(StringBuilder sb)
    {
        var per = PerTarget();
        foreach (var g in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
        {
            var gs = per == null ? null : per.SettingsForBuildTarget(g);
            sb.AppendLine("XR[" + g + "] initOnStart=" + (gs != null && gs.InitManagerOnStart) + " loaders=" + (gs == null || gs.Manager == null ? "<none>" : string.Join(", ", gs.Manager.activeLoaders.Select(l => l == null ? "null" : l.name + ":" + l.GetType().Name))));
            var s = OpenXRSettings.GetSettingsForBuildTargetGroup(g);
            if (s == null) { sb.AppendLine("  OpenXRSettings: <none>"); continue; }
            foreach (var f in s.GetFeatures<OpenXRFeature>().OrderBy(f => f.GetType().FullName))
                sb.AppendLine("  " + (f.enabled ? "[ON ] " : "[off] ") + f.GetType().FullName);
        }
    }

    public static void Configure()
    {
        var sb = new StringBuilder();
        sb.AppendLine("######## BEFORE"); Report(sb);
        var per = PerTarget();
        var standalone = per.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        var android = per.SettingsForBuildTarget(BuildTargetGroup.Android);
        var loader = standalone.Manager.activeLoaders.FirstOrDefault(l => l is OpenXRLoader);
        sb.AppendLine("######## CHANGES");
        if (loader == null) sb.AppendLine("ERROR: Standalone has no OpenXR loader to reuse");
        else if (!android.Manager.activeLoaders.Contains(loader))
        {
            bool ok = android.Manager.TryAddLoader(loader);
            EditorUtility.SetDirty(android.Manager);
            sb.AppendLine("Android: added loader " + loader.name + " ok=" + ok);
        }
        foreach (var g in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
        {
            FeatureHelpers.RefreshFeatures(g);
            var s = OpenXRSettings.GetSettingsForBuildTargetGroup(g);
            var wanted = new List<string>(Both);
            if (g == BuildTargetGroup.Android) wanted.AddRange(AndroidOnly);
            foreach (var w in wanted)
            {
                var f = s.GetFeatures<OpenXRFeature>().FirstOrDefault(x => x.GetType().FullName == w);
                if (f == null) { sb.AppendLine("ERROR " + g + ": feature not found " + w); continue; }
                if (!f.enabled) { f.enabled = true; EditorUtility.SetDirty(f); sb.AppendLine(g + ": enabled " + w); }
            }
            EditorUtility.SetDirty(s);
        }
        EditorUtility.SetDirty(per);
        AssetDatabase.SaveAssets();
        sb.AppendLine("######## AFTER"); Report(sb);
        File.WriteAllText("G:/SeparaVRecover/_logs/hands_xrsettings.txt", sb.ToString());
        Debug.Log("HandsXRSettings done");
    }
}

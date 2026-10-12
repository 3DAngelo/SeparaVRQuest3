// Read-only inspection helper for the hand-tracking migration. Writes a text report; changes nothing.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HandsDump
{
    static StringBuilder sb;
    static void L(string s) { sb.AppendLine(s); }
    public static string PathOf(Transform t) { var p = t.name; while (t.parent != null) { t = t.parent; p = t.parent == null ? t.name + "/" + p : t.name + "/" + p; } return p; }
    public static readonly string[] Scenes = { "Assets/Scenes/Intro.unity", "Assets/Scenes/NuevoJuego.unity", "Assets/Scenes/Win.unity" };

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    static bool IsGameScript(Component c)
    {
        var mb = c as MonoBehaviour; if (mb == null) return false;
        var ms = MonoScript.FromMonoBehaviour(mb); if (ms == null) return false;
        return AssetDatabase.GetAssetPath(ms).StartsWith("Assets/Scripts/");
    }

    static string Describe(UnityEngine.Object o)
    {
        if (o == null) return "null";
        if (o is Component c) return c.GetType().Name + "@" + PathOf(c.transform);
        if (o is GameObject g) return "GO@" + PathOf(g.transform);
        var p = AssetDatabase.GetAssetPath(o);
        return o.GetType().Name + ":" + o.name + (string.IsNullOrEmpty(p) ? "" : " [" + p + "]");
    }

    static void DumpProps(UnityEngine.Object o, string indent, bool onlyRefs, int max = 400)
    {
        var so = new SerializedObject(o);
        var it = so.GetIterator();
        int n = 0;
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = true;
            if (it.propertyPath == "m_Script") continue;
            if (it.propertyType == SerializedPropertyType.ObjectReference)
            {
                bool broken = it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0;
                if (it.objectReferenceValue != null || broken)
                { L(indent + it.propertyPath + " -> " + (broken ? "MISSING(broken ref)" : Describe(it.objectReferenceValue))); n++; }
            }
            else if (!onlyRefs && !it.hasVisibleChildren)
            {
                string v = null;
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Integer: v = it.longValue.ToString(); break;
                    case SerializedPropertyType.Boolean: v = it.boolValue.ToString(); break;
                    case SerializedPropertyType.Float: v = it.doubleValue.ToString("0.####"); break;
                    case SerializedPropertyType.String: v = "\"" + it.stringValue + "\""; break;
                    case SerializedPropertyType.Enum: v = it.enumValueIndex >= 0 && it.enumValueIndex < it.enumDisplayNames.Length ? it.enumDisplayNames[it.enumValueIndex] : it.intValue.ToString(); break;
                    case SerializedPropertyType.LayerMask: v = "mask " + it.intValue; break;
                    case SerializedPropertyType.Vector3: v = it.vector3Value.ToString("0.###"); break;
                    case SerializedPropertyType.Color: v = it.colorValue.ToString(); break;
                }
                if (v != null) { L(indent + it.propertyPath + " = " + v); n++; }
            }
            if (it.propertyType == SerializedPropertyType.String) enter = false;
            if (n > max) { L(indent + "..."); break; }
        }
    }

    static void DumpTree(Transform t, int depth, int maxDepth)
    {
        var comps = t.GetComponents<Component>();
        var names = string.Join(", ", comps.Where(c => !(c is Transform)).Select(c => c == null ? "<MISSING>" : c.GetType().Name + ((c is Behaviour b && !b.enabled) ? "(off)" : "")));
        L(new string(' ', depth * 2) + (t.gameObject.activeSelf ? "" : "[inactive] ") + t.name + "  pos" + t.localPosition.ToString("0.###") + " rot" + t.localEulerAngles.ToString("0.#") + " scl" + t.localScale.ToString("0.###") + " layer=" + LayerMask.LayerToName(t.gameObject.layer) + " tag=" + t.gameObject.tag + "  {" + names + "}");
        if (depth >= maxDepth) { if (t.childCount > 0) L(new string(' ', depth * 2 + 2) + "... " + t.childCount + " children"); return; }
        foreach (Transform c in t) DumpTree(c, depth + 1, maxDepth);
    }

    public static Transform FindRig(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var r in scene.GetRootGameObjects())
            foreach (var o in r.GetComponentsInChildren<Unity.XR.CoreUtils.XROrigin>(true)) return o.transform;
        return null;
    }

    public static void Dump()
    {
        string outPath = Arg("-dumpOut", "G:/SeparaVRecover/_logs/hands_dump.txt");
        sb = new StringBuilder();
        foreach (var path in Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            L("################ SCENE " + path);
            int missing = 0, gos = 0;
            foreach (var r in scene.GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true)) { gos++; missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject); }
            L("GameObjects=" + gos + " missingScripts=" + missing);
            L("== ROOTS");
            foreach (var r in scene.GetRootGameObjects()) DumpTree(r.transform, 0, 1);
            var rig = FindRig(scene);
            L("== RIG TREE: " + (rig == null ? "NONE" : PathOf(rig)));
            if (rig != null)
            {
                DumpTree(rig, 0, 99);
                L("== RIG COMPONENT DETAILS (non-default interesting)");
                foreach (var c in rig.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is CanvasRenderer) continue;
                    var tn = c.GetType().Name;
                    bool full = IsGameScript(c) || c is Camera || tn.Contains("Interactor") || tn.Contains("XROrigin") || tn.Contains("TrackedPoseDriver") || tn.Contains("AudioListener") || tn.Contains("UniversalAdditionalCameraData") || tn.Contains("InputActionManager") || tn.Contains("ControllerInputActionManager") || tn.Contains("Canvas") || tn.Contains("Locomotion") || tn.Contains("Provider") || tn.Contains("CharacterController");
                    if (!full) continue;
                    L("  -- " + tn + " @ " + PathOf(c.transform));
                    DumpProps(c, "       ", false, 150);
                }
            }
            L("== GAME SCRIPT COMPONENTS (all props)");
            var typeCount = new SortedDictionary<string, int>();
            foreach (var r in scene.GetRootGameObjects())
                foreach (var c in r.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || !IsGameScript(c)) continue;
                    var tn = c.GetType().Name;
                    int k; typeCount.TryGetValue(tn, out k); typeCount[tn] = k + 1;
                    if (k >= 2 && (tn == "CustomGrabInteractable" || tn == "Residuo" || tn == "Container" || tn == "AbreContainer" || tn.EndsWith("Evolve"))) continue;
                    L("  -- " + tn + " @ " + PathOf(c.transform) + (((Behaviour)c).enabled ? "" : " (disabled)") + (c.gameObject.activeInHierarchy ? "" : " (inactive)"));
                    var others = c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name);
                    L("       [siblings: " + string.Join(", ", others) + "] layer=" + LayerMask.LayerToName(c.gameObject.layer));
                    DumpProps(c, "       ", false, 200);
                    var rb = c.GetComponent<Rigidbody>();
                    if (rb != null) L("       RB kinematic=" + rb.isKinematic + " gravity=" + rb.useGravity + " mass=" + rb.mass);
                    foreach (var col in c.GetComponentsInChildren<Collider>(true)) L("       COL " + col.GetType().Name + " @" + PathOf(col.transform) + " trigger=" + col.isTrigger + " enabled=" + col.enabled + " bounds=" + col.bounds.size.ToString("0.##"));
                }
            foreach (var kv in typeCount) L("  count " + kv.Key + " = " + kv.Value);
            L("== NON-RIG XRI / UI components");
            foreach (var r in scene.GetRootGameObjects())
                foreach (var c in r.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || IsGameScript(c)) continue;
                    if (rig != null && c.transform.IsChildOf(rig)) continue;
                    var tn = c.GetType().Name; var ns = c.GetType().Namespace ?? "";
                    bool show = ns.Contains("XR.Interaction") || c is Canvas || tn == "EventSystem" || tn.Contains("InputModule") || tn.Contains("Raycaster") || tn == "Button" || c is Camera || tn.Contains("InputActionManager");
                    if (!show) continue;
                    L("  -- " + tn + " @ " + PathOf(c.transform) + (c.gameObject.activeInHierarchy ? "" : " (inactive)"));
                    DumpProps(c, "       ", !(c is Canvas || tn == "Button" || tn.Contains("InputModule") || tn.Contains("Interactable") || tn.Contains("Raycaster") || tn.Contains("Poke") || tn.Contains("Manager")), 120);
                }
            L("== INBOUND REFERENCES INTO RIG (from outside rig)");
            if (rig != null)
                foreach (var r in scene.GetRootGameObjects())
                    foreach (var c in r.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || c.transform.IsChildOf(rig)) continue;
                        var so = new SerializedObject(c); var it = so.GetIterator();
                        while (it.Next(true))
                        {
                            if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                            if (it.propertyPath == "m_GameObject" || it.propertyPath == "m_Father") continue;
                            Transform tt = null;
                            if (it.objectReferenceValue is Component cc) tt = cc.transform; else if (it.objectReferenceValue is GameObject gg) tt = gg.transform;
                            if (tt != null && tt.IsChildOf(rig)) L("  " + c.GetType().Name + "@" + PathOf(c.transform) + " ." + it.propertyPath + " -> " + Describe(it.objectReferenceValue));
                        }
                    }
            L("== OUTBOUND REFERENCES FROM RIG to scene objects outside rig");
            if (rig != null)
                foreach (var c in rig.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var so = new SerializedObject(c); var it = so.GetIterator();
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                        if (it.propertyPath == "m_GameObject" || it.propertyPath == "m_Father") continue;
                        Transform tt = null;
                        if (it.objectReferenceValue is Component cc) tt = cc.transform; else if (it.objectReferenceValue is GameObject gg) tt = gg.transform;
                        if (tt != null && !tt.IsChildOf(rig)) L("  " + c.GetType().Name + "@" + PathOf(c.transform) + " ." + it.propertyPath + " -> " + Describe(it.objectReferenceValue));
                    }
                }
        }
        L("################ LAYERS");
        for (int i = 0; i < 32; i++) { var n = LayerMask.LayerToName(i); if (!string.IsNullOrEmpty(n)) L("  " + i + " = " + n); }
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log("HandsDump written to " + outPath);
    }
}

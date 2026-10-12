// Read-only: compares each scene's current rig against the stock Starter Assets rig prefab to reveal game-specific customisations,
// and prints the structure of the Hands rig prefab. Changes nothing (scenes are not saved).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HandsRigDiff
{
    public const string StockRig = "Assets/Samples/XR Interaction Toolkit/3.0.6/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    public const string HandsRig = "Assets/Samples/XR Interaction Toolkit/3.0.6/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";
    static StringBuilder sb;
    static void L(string s) { sb.AppendLine(s); }

    public static string Rel(Transform root, Transform t)
    {
        if (t == root) return "";
        var parts = new List<string>();
        while (t != null && t != root)
        {
            int idx = 0;
            if (t.parent != null) for (int i = 0; i < t.GetSiblingIndex(); i++) if (t.parent.GetChild(i).name == t.name) idx++;
            parts.Add(idx == 0 ? t.name : t.name + "#" + idx);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static Dictionary<string, Transform> Map(Transform root)
    {
        var d = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) d[Rel(root, t)] = t;
        return d;
    }

    static string Val(SerializedProperty p, Transform root)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue.ToString();
            case SerializedPropertyType.Boolean: return p.boolValue.ToString();
            case SerializedPropertyType.Float: return p.doubleValue.ToString("0.#####");
            case SerializedPropertyType.String: return "\"" + p.stringValue + "\"";
            case SerializedPropertyType.Enum: return p.intValue.ToString();
            case SerializedPropertyType.LayerMask: return "mask" + p.intValue;
            case SerializedPropertyType.ArraySize: return "size" + p.intValue;
            case SerializedPropertyType.Character: return p.intValue.ToString();
            case SerializedPropertyType.ObjectReference:
                var o = p.objectReferenceValue;
                if (o == null) return "null";
                Transform tt = null;
                if (o is Component c) tt = c.transform; else if (o is GameObject g) tt = g.transform;
                if (tt != null && tt.gameObject.scene.IsValid())
                    return tt.IsChildOf(root) ? "rig:" + Rel(root, tt) + ":" + o.GetType().Name : "scene:" + tt.name + ":" + o.GetType().Name;
                return "asset:" + o.name + ":" + o.GetType().Name;
            default: return null;
        }
    }

    static Dictionary<string, string> Props(UnityEngine.Object o, Transform root)
    {
        var d = new Dictionary<string, string>();
        var so = new SerializedObject(o); var it = so.GetIterator();
        bool enter = true;
        while (it.Next(enter))
        {
            enter = it.propertyType != SerializedPropertyType.String;
            var pp = it.propertyPath;
            if (pp.StartsWith("m_ObjectHideFlags") || pp.StartsWith("m_CorrespondingSourceObject") || pp.StartsWith("m_PrefabInstance") || pp.StartsWith("m_PrefabAsset") || pp == "m_GameObject" || pp.StartsWith("m_EditorHideFlags") || pp.StartsWith("m_EditorClassIdentifier") || pp == "m_Script" || pp == "m_Name" || pp.StartsWith("m_Father") || pp.StartsWith("m_Children") || pp.StartsWith("m_LocalEulerAnglesHint") || pp.StartsWith("m_Component") || pp.Contains("m_Id") || pp.StartsWith("m_RootOrder")) continue;
            var v = Val(it, root);
            if (v != null) d[pp] = v;
        }
        return d;
    }

    static void PrintTree(Transform t, int depth, string[] prune)
    {
        var comps = t.GetComponents<Component>();
        var names = string.Join(", ", comps.Where(c => !(c is Transform)).Select(c => c == null ? "<MISSING>" : c.GetType().Name + ((c is Behaviour b && !b.enabled) ? "(off)" : "")));
        L(new string(' ', depth * 2) + (t.gameObject.activeSelf ? "" : "[inactive] ") + t.name + "  pos" + t.localPosition.ToString("0.###") + " rot" + t.localEulerAngles.ToString("0.#") + "  {" + names + "}");
        if (prune.Any(p => t.name.Contains(p))) { if (t.childCount > 0) L(new string(' ', depth * 2 + 2) + "... (" + t.GetComponentsInChildren<Transform>(true).Length + " transforms pruned)"); return; }
        foreach (Transform c in t) PrintTree(c, depth + 1, prune);
    }

    public static void Run()
    {
        sb = new StringBuilder();
        var stockAsset = AssetDatabase.LoadAssetAtPath<GameObject>(StockRig);
        var handsAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HandsRig);
        L("stock prefab loaded=" + (stockAsset != null) + " hands prefab loaded=" + (handsAsset != null));
        foreach (var path in HandsDump.Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var old = HandsDump.FindRig(scene);
            L("################ DIFF old rig vs stock Starter Assets rig : " + path);
            var stock = ((GameObject)PrefabUtility.InstantiatePrefab(stockAsset, scene)).transform;
            var mo = Map(old); var ms = Map(stock);
            foreach (var k in mo.Keys.Where(k => !ms.ContainsKey(k)).OrderBy(k => k)) if (!mo.Keys.Any(o => k.StartsWith(o + "/") && !ms.ContainsKey(o))) L("  EXTRA in old (not in stock): " + k + "  {" + string.Join(", ", mo[k].GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name)) + "} subtree=" + mo[k].GetComponentsInChildren<Transform>(true).Length);
            foreach (var k in ms.Keys.Where(k => !mo.ContainsKey(k)).OrderBy(k => k)) if (!ms.Keys.Any(o => k.StartsWith(o + "/") && !mo.ContainsKey(o))) L("  ABSENT in old (stock has): " + k);
            foreach (var k in mo.Keys.OrderBy(k => k))
            {
                Transform a = mo[k], b;
                if (!ms.TryGetValue(k, out b)) continue;
                var pre = "  [" + (k == "" ? "<root>" : k) + "] ";
                if (a.gameObject.activeSelf != b.gameObject.activeSelf) L(pre + "activeSelf old=" + a.gameObject.activeSelf + " stock=" + b.gameObject.activeSelf);
                if (a.gameObject.layer != b.gameObject.layer) L(pre + "layer old=" + LayerMask.LayerToName(a.gameObject.layer) + " stock=" + LayerMask.LayerToName(b.gameObject.layer));
                if (a.gameObject.tag != b.gameObject.tag) L(pre + "tag old=" + a.gameObject.tag + " stock=" + b.gameObject.tag);
                if (k != "" && ((a.localPosition - b.localPosition).magnitude > 1e-4f || Quaternion.Angle(a.localRotation, b.localRotation) > 0.01f || (a.localScale - b.localScale).magnitude > 1e-4f))
                    L(pre + "TRS old=" + a.localPosition.ToString("0.####") + a.localEulerAngles.ToString("0.##") + a.localScale.ToString("0.###") + " stock=" + b.localPosition.ToString("0.####") + b.localEulerAngles.ToString("0.##") + b.localScale.ToString("0.###"));
                var ca = a.GetComponents<Component>().Where(c => c != null && !(c is Transform)).ToList();
                var cb = b.GetComponents<Component>().Where(c => c != null && !(c is Transform)).ToList();
                var used = new HashSet<Component>();
                foreach (var c in ca)
                {
                    var m = cb.FirstOrDefault(x => x.GetType() == c.GetType() && !used.Contains(x));
                    if (m == null) { L(pre + "EXTRA component in old: " + c.GetType().Name); continue; }
                    used.Add(m);
                    var pa = Props(c, old); var pb = Props(m, stock);
                    int n = 0;
                    foreach (var kv in pa)
                    {
                        string vb; if (!pb.TryGetValue(kv.Key, out vb)) vb = "<absent>";
                        if (vb == kv.Value) continue;
                        if (n++ < 25) L(pre + c.GetType().Name + "." + kv.Key + "  old=" + kv.Value + "  stock=" + vb);
                    }
                    foreach (var kv in pb) if (!pa.ContainsKey(kv.Key) && n++ < 25) L(pre + c.GetType().Name + "." + kv.Key + "  old=<absent>  stock=" + kv.Value);
                    if (n > 25) L(pre + c.GetType().Name + " ... " + (n - 25) + " more diffs");
                }
                foreach (var c in cb) if (!used.Contains(c)) L(pre + "ABSENT component in old: " + c.GetType().Name);
            }
            UnityEngine.Object.DestroyImmediate(stock.gameObject);
        }
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var hands = ((GameObject)PrefabUtility.InstantiatePrefab(handsAsset, scene)).transform;
            L("################ HANDS RIG PREFAB TREE");
            int miss = 0; foreach (var t in hands.GetComponentsInChildren<Transform>(true)) miss += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            L("missing scripts in hands rig instance = " + miss);
            PrintTree(hands, 0, new[] { "Callouts", "Controller Visual", "Interaction Visual", "Locomotion", "Hand Menu", "Affordances" });
            L("################ HANDS RIG : hand-side component values");
            foreach (var c in hands.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform) continue;
                var rel = Rel(hands, c.transform);
                bool isRoot = rel == "";
                bool handSide = rel.Contains("Right Hand") && !rel.Contains("Visual") && !rel.Contains("Affordances");
                var tn = c.GetType().Name;
                if (!(handSide || (isRoot && (tn == "XRInputModalityManager" || tn == "InputActionManager")) || tn == "XRInteractionGroup" || (rel.EndsWith("Right Controller/Near-Far Interactor") && tn.Contains("Caster")))) continue;
                if (tn.Contains("Renderer") || tn == "MeshFilter" || tn == "SortingGroup") continue;
                L("  -- " + tn + " @ " + rel + ((c is Behaviour b && !b.enabled) ? " (off)" : ""));
                foreach (var kv in Props(c, hands))
                {
                    var key = kv.Key;
                    if (key.Contains("m_InputAction.") || key.Contains("m_InputActionPerformed.") || key.Contains("m_InputActionValue.") || key.Contains("m_Action.") || key.Contains("Manual") || key.Contains("Haptic") || key.Contains("AudioClip") || key.Contains("m_PersistentCalls.m_Calls.Array.size") || kv.Value == "null") continue;
                    L("       " + key + " = " + kv.Value);
                }
            }
        }
        File.WriteAllText("G:/SeparaVRecover/_logs/hands_rigdiff.txt", sb.ToString());
        Debug.Log("HandsRigDiff written");
    }
}

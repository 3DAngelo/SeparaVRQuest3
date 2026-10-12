// Hand-tracking migration: replaces each scene's controller-only XR rig by an instance of the XRI "XR Origin Hands (XR Rig)" prefab
// (hands + controllers, switched automatically by XRInputModalityManager) and carries the game's customisations over.
// Idempotent: a scene whose rig already has hands is skipped.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public static class HandsMigrate
{
    static StringBuilder sb;
    static void L(string s) { sb.AppendLine(s); }

    public static void Run()
    {
        sb = new StringBuilder();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandsRigDiff.HandsRig);
        if (prefab == null) { Debug.LogError("HandsMigrate: hands rig prefab not found"); EditorApplication.Exit(3); return; }
        foreach (var path in HandsDump.Scenes) MigrateScene(path, prefab);
        File.WriteAllText("G:/SeparaVRecover/_logs/hands_migrate.txt", sb.ToString());
        Debug.Log("HandsMigrate done");
    }

    static void CopyProp(Component from, Component to, string prop)
    {
        var a = new SerializedObject(from); var b = new SerializedObject(to);
        var pa = a.FindProperty(prop);
        if (pa == null || b.FindProperty(prop) == null) { L("  WARN property not found: " + from.GetType().Name + "." + prop); return; }
        b.CopyFromSerializedProperty(pa);
        b.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetRef(Component c, string prop, Object value)
    {
        var so = new SerializedObject(c); var p = so.FindProperty(prop);
        if (p == null) { L("  WARN property not found: " + c.GetType().Name + "." + prop); return; }
        p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }

    static string ParentKey(string k) { int i = k.LastIndexOf('/'); return i < 0 ? "" : k.Substring(0, i); }

    static void MigrateScene(string path, GameObject prefab)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        L("################ " + path);
        var old = HandsDump.FindRig(scene);
        if (old == null) { L("  ERROR no XROrigin in scene; skipped"); return; }
        if (old.Find("Camera Offset/Left Hand") != null) { L("  already has a hands rig; skipped"); return; }

        var neu = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).transform;
        string oldName = old.name;
        int sibling = old.GetSiblingIndex();
        neu.SetParent(old.parent, false);
        neu.localPosition = old.localPosition; neu.localRotation = old.localRotation; neu.localScale = old.localScale;
        GameObjectUtility.SetStaticEditorFlags(neu.gameObject, GameObjectUtility.GetStaticEditorFlags(old.gameObject));
        L("  new rig placed at pos=" + neu.position.ToString("0.####") + " rot=" + neu.eulerAngles.ToString("0.##") + " scale=" + neu.localScale.ToString("0.###"));

        var mo = HandsRigDiff.Map(old); var mn = HandsRigDiff.Map(neu);
        var relOld = new Dictionary<Transform, string>(); foreach (var kv in mo) relOld[kv.Value] = kv.Key;

        // 1. objects present in both rigs: active state, layer, tag; components the game's rig has that the new one lacks
        foreach (var k in mo.Keys.OrderBy(x => x))
        {
            Transform a = mo[k], b;
            if (!mn.TryGetValue(k, out b)) continue;
            string nm = k == "" ? "<root>" : k;
            if (a.gameObject.activeSelf != b.gameObject.activeSelf) { b.gameObject.SetActive(a.gameObject.activeSelf); L("  activeSelf=" + a.gameObject.activeSelf + " : " + nm); }
            if (a.gameObject.layer != b.gameObject.layer) { b.gameObject.layer = a.gameObject.layer; L("  layer=" + LayerMask.LayerToName(a.gameObject.layer) + " : " + nm); }
            if (a.gameObject.tag != b.gameObject.tag) { b.gameObject.tag = a.gameObject.tag; L("  tag=" + a.gameObject.tag + " : " + nm); }
            foreach (var c in a.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                if (b.GetComponent(c.GetType()) != null) continue;
                UnityEditorInternal.ComponentUtility.CopyComponent(c);
                bool ok = UnityEditorInternal.ComponentUtility.PasteComponentAsNew(b.gameObject);
                L("  copied component " + c.GetType().Name + " -> " + nm + " ok=" + ok);
            }
        }

        // 2. explicit value carry-over (everything else in the old rig was identical to the stock Starter Assets rig)
        var oxo = old.GetComponent<Unity.XR.CoreUtils.XROrigin>(); var nxo = neu.GetComponent<Unity.XR.CoreUtils.XROrigin>();
        CopyProp(oxo, nxo, "m_RequestedTrackingOriginMode"); CopyProp(oxo, nxo, "m_CameraYOffset");
        var occ = old.GetComponent<CharacterController>(); var ncc = neu.GetComponent<CharacterController>();
        if (occ != null && ncc != null) { EditorUtility.CopySerialized(occ, ncc); L("  CharacterController copied (enabled=" + ncc.enabled + ")"); }
        var oOff = mo["Camera Offset"]; var nOff = mn["Camera Offset"];
        nOff.localPosition = oOff.localPosition; nOff.localRotation = oOff.localRotation; nOff.localScale = oOff.localScale;
        L("  Camera Offset localPos=" + nOff.localPosition.ToString("0.####"));
        var oCam = mo["Camera Offset/Main Camera"].GetComponent<Camera>(); var nCam = mn["Camera Offset/Main Camera"].GetComponent<Camera>();
        EditorUtility.CopySerialized(oCam, nCam); L("  Camera settings copied");
        var oAl = oCam.GetComponent<AudioListener>(); var nAl = nCam.GetComponent<AudioListener>();
        if (oAl != null && nAl != null) nAl.enabled = oAl.enabled;
        foreach (var side in new[] { "Left", "Right" })
        {
            string cp = "Camera Offset/" + side + " Controller";
            var om = mo[cp].GetComponent("ControllerInputActionManager"); var nm = mn[cp].GetComponent("ControllerInputActionManager");
            CopyProp(om, nm, "m_SmoothMotionEnabled"); CopyProp(om, nm, "m_NearFarEnableTeleportDuringNearInteraction");
            // the game's rig had no Teleport Interactor (and Locomotion is inactive): keep it that way
            Transform tp;
            if (!mo.ContainsKey(cp + "/Teleport Interactor") && mn.TryGetValue(cp + "/Teleport Interactor", out tp))
            {
                tp.gameObject.SetActive(false); SetRef(nm, "m_TeleportInteractor", null);
                L("  " + side + " Controller: Teleport Interactor deactivated and unassigned (absent in the game's rig)");
            }
        }

        // 3. objects the old rig has and the new one lacks: move them, as they are, under the same parent of the new rig
        var extras = mo.Keys.Where(k => k != "" && !mn.ContainsKey(k) && mn.ContainsKey(ParentKey(k))).OrderBy(k => k).ToList();
        foreach (var k in extras)
        {
            var t = mo[k];
            t.SetParent(mn[ParentKey(k)], false);
            L("  moved object: " + k + " {" + string.Join(", ", t.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name)) + "} subtree=" + t.GetComponentsInChildren<Transform>(true).Length);
        }
        foreach (var side in new[] { "Left", "Right" })
        {
            string cp = "Camera Offset/" + side + " Controller";
            var oldRay = new SerializedObject(mo[cp].GetComponent("ControllerInputActionManager")).FindProperty("m_RayInteractor").objectReferenceValue as Component;
            if (oldRay != null && oldRay.transform.IsChildOf(neu))
            {
                SetRef(mn[cp].GetComponent("ControllerInputActionManager"), "m_RayInteractor", oldRay);
                L("  " + side + " ControllerInputActionManager.m_RayInteractor -> " + HandsDump.PathOf(oldRay.transform));
            }
        }

        // 4. the hover-only ray that highlights containers: give each hand one too, driven by the hand's aim pose
        foreach (var side in new[] { "Left", "Right" })
        {
            var ray = mn["Camera Offset/" + side + " Controller"].GetComponentsInChildren<XRRayInteractor>(true).FirstOrDefault(r => r.name != "Teleport Interactor");
            if (ray == null) continue;
            int listeners = new SerializedObject(ray).FindProperty("m_HoverEntered.m_PersistentCalls.m_Calls").arraySize;
            if (listeners == 0) { L("  " + side + ": controller ray '" + ray.name + "' has no hover listeners here; no hand copy needed"); continue; }
            var aim = mn["Camera Offset/" + side + " Hand/Aim Pose"];
            var copy = Object.Instantiate(ray.gameObject, aim, false);
            copy.name = side + " Hand Container Ray";
            copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity; copy.transform.localScale = Vector3.one;
            L("  added " + HandsDump.PathOf(copy.transform) + " (copy of '" + ray.name + "', hoverListeners=" + listeners + ")");
        }

        // 5. rewire every reference that still points into the old rig
        int remapped = 0, failed = 0;
        foreach (var r in scene.GetRootGameObjects())
            foreach (var c in r.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c.transform.IsChildOf(old)) continue;
                var so = new SerializedObject(c); var it = so.GetIterator(); bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    if (it.propertyPath == "m_GameObject" || it.propertyPath.StartsWith("m_CorrespondingSourceObject") || it.propertyPath.StartsWith("m_PrefabInstance") || it.propertyPath.StartsWith("m_PrefabAsset")) continue;
                    var o = it.objectReferenceValue; Transform tt = null;
                    if (o is Component oc) tt = oc.transform; else if (o is GameObject og) tt = og.transform;
                    if (tt == null || !tt.IsChildOf(old)) continue;
                    Object target = null; Transform nt; string rel;
                    if (relOld.TryGetValue(tt, out rel) && mn.TryGetValue(rel, out nt))
                    {
                        if (o is GameObject) target = nt.gameObject;
                        else if (o is Transform) target = nt;
                        else
                        {
                            var same = tt.GetComponents(o.GetType()); int idx = System.Array.IndexOf(same, (Component)o);
                            var cand = nt.GetComponents(o.GetType());
                            if (idx >= 0 && idx < cand.Length) target = cand[idx];
                        }
                    }
                    string who = c.GetType().Name + "@" + HandsDump.PathOf(c.transform) + " ." + it.propertyPath;
                    if (target != null) { remapped++; L("  REWIRED " + who + " : " + o.GetType().Name + "@" + rel + " -> " + target.GetType().Name + "@" + HandsDump.PathOf(target is GameObject tg ? tg.transform : ((Component)target).transform)); }
                    else { failed++; L("  REWIRE-FAILED (set to null) " + who + " : was " + o.GetType().Name + "@" + (rel ?? tt.name)); }
                    it.objectReferenceValue = target; changed = true;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
        L("  references rewired=" + remapped + " failed=" + failed);

        // 6. remove the old rig, keep the name and the place in the hierarchy
        Object.DestroyImmediate(old.gameObject);
        neu.name = oldName;
        neu.SetSiblingIndex(sibling);
        L("  old rig removed; new rig named '" + neu.name + "'");
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        L("  saved=" + saved);
    }
}

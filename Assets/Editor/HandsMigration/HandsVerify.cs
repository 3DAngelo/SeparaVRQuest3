// Read-only verification report for the hand-tracking migration. Run before and after; the two outputs are diffed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Filtering;

public static class HandsVerify
{
    static StringBuilder sb;
    static void L(string s) { sb.AppendLine(s); }

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
        if (o is Component c) return c.GetType().Name + "@" + HandsDump.PathOf(c.transform);
        if (o is GameObject g) return "GO@" + HandsDump.PathOf(g.transform);
        return o.GetType().Name + ":" + o.name + " [" + AssetDatabase.GetAssetPath(o) + "]";
    }

    static IEnumerable<Component> All(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var r in scene.GetRootGameObjects())
            foreach (var c in r.GetComponentsInChildren<Component>(true)) yield return c;
    }

    public static void Run()
    {
        string outPath = Arg("-verifyOut", "G:/SeparaVRecover/_logs/hands_verify.txt");
        sb = new StringBuilder();
        HandsXRSettings.Report(sb);
        foreach (var path in HandsDump.Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var sn = Path.GetFileNameWithoutExtension(path);
            L("################ SCENE " + sn);
            int missing = 0, gos = 0, missingInRig = 0;
            var rig = HandsDump.FindRig(scene);
            foreach (var r in scene.GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                {
                    gos++; int m = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject); missing += m;
                    if (rig != null && t.IsChildOf(rig)) missingInRig += m;
                }
            L(sn + " | missingScripts=" + missing + " (inside rig=" + missingInRig + ")");
            var origins = All(scene).Where(c => c is Unity.XR.CoreUtils.XROrigin).ToList();
            L(sn + " | XROrigin count=" + origins.Count + " rig='" + (rig == null ? "NONE" : rig.name) + "' pos=" + (rig == null ? "" : rig.position.ToString("0.###") + " rot=" + rig.eulerAngles.ToString("0.##") + " layer=" + LayerMask.LayerToName(rig.gameObject.layer)));
            if (rig != null)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(rig.gameObject);
                L(sn + " | rig prefab source=" + (src == null ? "<none, unpacked>" : AssetDatabase.GetAssetPath(src)));
                L(sn + " | rig has hands=" + (rig.Find("Camera Offset/Left Hand") != null && rig.Find("Camera Offset/Right Hand") != null));
            }
            var cams = All(scene).OfType<Camera>().ToList();
            foreach (var cam in cams) L(sn + " | Camera " + HandsDump.PathOf(cam.transform) + " tag=" + cam.tag + " active=" + cam.gameObject.activeInHierarchy + " clear=" + cam.clearFlags + " near=" + cam.nearClipPlane + " far=" + cam.farClipPlane + " fov=" + cam.fieldOfView + " hdr=" + cam.allowHDR + " msaa=" + cam.allowMSAA + " comps={" + string.Join(",", cam.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name)) + "} children={" + string.Join(",", cam.transform.Cast<Transform>().Select(t => t.name)) + "}");
            L(sn + " | AudioListeners(active)=" + All(scene).OfType<AudioListener>().Count(a => a.isActiveAndEnabled));

            // broken references anywhere in the scene (fileID set but object gone)
            var broken = new List<string>();
            foreach (var c in All(scene))
            {
                if (c == null) continue;
                var so = new SerializedObject(c); var it = so.GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                        broken.Add(c.GetType().Name + "@" + HandsDump.PathOf(c.transform) + " ." + it.propertyPath);
            }
            L(sn + " | brokenRefs(all components)=" + broken.Count);
            foreach (var b in broken.Take(40)) L(sn + " | BROKEN " + b);

            // every object reference held by game scripts
            var lines = new List<string>();
            var typeCount = new SortedDictionary<string, int>();
            foreach (var c in All(scene))
            {
                if (c == null || !IsGameScript(c)) continue;
                int k; typeCount.TryGetValue(c.GetType().Name, out k); typeCount[c.GetType().Name] = k + 1;
                var so = new SerializedObject(c); var it = so.GetIterator(); bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = it.propertyType != SerializedPropertyType.String;
                    if (it.propertyPath == "m_Script" || it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    bool br = it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0;
                    lines.Add(sn + " | REF " + c.GetType().Name + "@" + HandsDump.PathOf(c.transform) + " ." + it.propertyPath + " -> " + (br ? "BROKEN" : Describe(it.objectReferenceValue)));
                }
            }
            lines.Sort(StringComparer.Ordinal);
            foreach (var l in lines) L(l);
            L(sn + " | gameScriptRefs total=" + lines.Count + " null=" + lines.Count(l => l.EndsWith("-> null")) + " broken=" + lines.Count(l => l.EndsWith("-> BROKEN")));
            foreach (var kv in typeCount) L(sn + " | count " + kv.Key + "=" + kv.Value);

            // persistent UnityEvent listeners outside the rig that target nothing
            foreach (var c in All(scene))
            {
                if (c == null) continue;
                var so = new SerializedObject(c); var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (!it.propertyPath.EndsWith("m_PersistentCalls.m_Calls") || !it.isArray || it.arraySize == 0) continue;
                    for (int i = 0; i < it.arraySize; i++)
                    {
                        var call = it.GetArrayElementAtIndex(i);
                        var tgt = call.FindPropertyRelative("m_Target").objectReferenceValue;
                        var arg = call.FindPropertyRelative("m_Arguments.m_ObjectArgument").objectReferenceValue;
                        L(sn + " | EVENT " + c.GetType().Name + "@" + HandsDump.PathOf(c.transform) + " ." + it.propertyPath.Replace(".m_PersistentCalls.m_Calls", "") + "[" + i + "] -> " + (tgt == null ? "NULL-TARGET" : tgt.GetType().Name + "@" + ((tgt is Component tc) ? HandsDump.PathOf(tc.transform) : tgt.name)) + "." + call.FindPropertyRelative("m_MethodName").stringValue + "(" + (arg == null ? "" : Describe(arg)) + ")");
                    }
                }
            }

            // interactors
            foreach (var x in All(scene).OfType<XRBaseInteractor>())
            {
                var s = sn + " | INTERACTOR " + x.GetType().Name + " @" + HandsDump.PathOf(x.transform) + " activeInHierarchy=" + x.gameObject.activeInHierarchy + " enabled=" + x.enabled + " layers=" + x.interactionLayers.value + " handed=" + x.handedness;
                if (x is NearFarInteractor nf)
                {
                    var so = new SerializedObject(nf);
                    var far = so.FindProperty("m_FarInteractionCaster").objectReferenceValue as CurveInteractionCaster;
                    var near = so.FindProperty("m_NearInteractionCaster").objectReferenceValue as SphereInteractionCaster;
                    var att = so.FindProperty("m_InteractionAttachController").objectReferenceValue as InteractionAttachController;
                    s += " farCasting=" + nf.enableFarCasting + " farCastOrigin=" + (far == null || far.castOrigin == null ? "NULL" : HandsDump.PathOf(far.castOrigin)) + " castDistance=" + (far == null ? 0 : far.castDistance) + " raycastMask=" + (far == null ? 0 : far.raycastMask.value)
                        + " nearCastOrigin=" + (near == null || near.castOrigin == null ? "NULL" : HandsDump.PathOf(near.castOrigin)) + " attachFollows=" + (att == null || att.transformToFollow == null ? "NULL" : HandsDump.PathOf(att.transformToFollow))
                        + " select=" + Ref(so, "m_SelectInput.m_InputActionReferencePerformed") + " selectValue=" + Ref(so, "m_SelectInput.m_InputActionReferenceValue") + " selectMode=" + so.FindProperty("m_SelectInput.m_InputSourceMode").intValue + " selectReader=" + Describe(so.FindProperty("m_SelectInput.m_ObjectReferenceObject").objectReferenceValue);
                }
                if (x is XRRayInteractor ray)
                {
                    var so = new SerializedObject(ray);
                    s += " maxDist=" + ray.maxRaycastDistance + " mask=" + ray.raycastMask.value + " select=" + Ref(so, "m_SelectInput.m_InputActionReferencePerformed") + " hoverListeners=" + so.FindProperty("m_HoverEntered.m_PersistentCalls.m_Calls").arraySize + " uiInteraction=" + ray.enableUIInteraction;
                }
                if (x is XRPokeInteractor poke)
                {
                    var so = new SerializedObject(poke);
                    s += " requirePokeFilter=" + poke.requirePokeFilter + " attach=" + Describe(so.FindProperty("m_AttachTransform").objectReferenceValue) + " tpd=" + (poke.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() != null);
                }
                L(s);
            }
            // input wiring inside the rig
            if (rig != null)
            {
                var iam = rig.GetComponent<InputActionManager>();
                var assets = iam == null ? new List<InputActionAsset>() : iam.actionAssets;
                L(sn + " | InputActionManager assets=" + string.Join(", ", assets.Select(a => a == null ? "null" : AssetDatabase.GetAssetPath(a))));
                int total = 0, nul = 0, foreign = 0; var foreignList = new List<string>();
                foreach (var c in rig.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var so = new SerializedObject(c); var it = so.GetIterator();
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var iar = it.objectReferenceValue as InputActionReference;
                        if (iar == null) continue;
                        total++;
                        if (iar.action == null) { nul++; foreignList.Add("UNRESOLVED " + c.GetType().Name + "@" + HandsDump.PathOf(c.transform) + "." + it.propertyPath); }
                        else if (!assets.Contains(iar.asset)) { foreign++; foreignList.Add("FOREIGN-ASSET " + c.GetType().Name + "@" + HandsDump.PathOf(c.transform) + "." + it.propertyPath + " -> " + AssetDatabase.GetAssetPath(iar.asset)); }
                    }
                }
                L(sn + " | rig InputActionReferences total=" + total + " unresolved=" + nul + " notInManagedAsset=" + foreign);
                foreach (var f in foreignList.Take(30)) L(sn + " |   " + f);
                var mm = rig.GetComponent<XRInputModalityManager>();
                if (mm != null) L(sn + " | ModalityManager leftHand=" + Describe(mm.leftHand) + " rightHand=" + Describe(mm.rightHand) + " leftController=" + Describe(mm.leftController) + " rightController=" + Describe(mm.rightController));
                foreach (var cm in rig.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "ControllerInputActionManager"))
                {
                    var so = new SerializedObject(cm);
                    L(sn + " | ControllerInputActionManager@" + cm.transform.name + " ray=" + Describe(so.FindProperty("m_RayInteractor").objectReferenceValue) + " nearFar=" + Describe(so.FindProperty("m_NearFarInteractor").objectReferenceValue) + " teleport=" + Describe(so.FindProperty("m_TeleportInteractor").objectReferenceValue) + " smoothMotion=" + so.FindProperty("m_SmoothMotionEnabled").boolValue);
                }
                foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
                    if (!t.gameObject.activeSelf) L(sn + " | rig inactive object: " + HandsRigDiff.Rel(rig, t));
                var shaders = new SortedDictionary<string, int>();
                foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        string key = m == null ? "<null material> on " + r.name : (m.shader == null ? "<null shader>" : m.shader.name + (m.shader.isSupported ? "" : " (UNSUPPORTED)")) ;
                        int k; shaders.TryGetValue(key, out k); shaders[key] = k + 1;
                    }
                foreach (var kv in shaders) L(sn + " | rig shader: " + kv.Key + " x" + kv.Value);
                var cc = rig.GetComponent<CharacterController>();
                var xo = rig.GetComponent<Unity.XR.CoreUtils.XROrigin>();
                L(sn + " | XROrigin mode=" + xo.RequestedTrackingOriginMode + " cameraYOffset=" + xo.CameraYOffset + " camera=" + Describe(xo.Camera) + " floorOffset=" + Describe(xo.CameraFloorOffsetObject) + " cameraOffsetLocalPos=" + (xo.CameraFloorOffsetObject == null ? "" : xo.CameraFloorOffsetObject.transform.localPosition.ToString("0.####")) + " | CharacterController enabled=" + (cc != null && cc.enabled) + " h=" + (cc == null ? 0 : cc.height) + " r=" + (cc == null ? 0 : cc.radius));
            }
            // grab interactables and poke targets
            foreach (var g in All(scene).OfType<XRGrabInteractable>())
            {
                var so = new SerializedObject(g);
                var rb = g.GetComponent<Rigidbody>();
                L(sn + " | GRAB " + g.GetType().Name + " @" + HandsDump.PathOf(g.transform) + " active=" + g.gameObject.activeInHierarchy + " layers=" + g.interactionLayers.value + " physLayer=" + LayerMask.LayerToName(g.gameObject.layer) + " farAttachMode=" + so.FindProperty("m_FarAttachMode").intValue + " movement=" + g.movementType + " throw=" + g.throwOnDetach + " colliders=" + g.GetComponentsInChildren<Collider>(true).Count(c => c.enabled) + " rb=" + (rb != null) + " residuo=" + (g.GetComponent("Residuo") != null) + " manager=" + Describe(so.FindProperty("m_InteractionManager").objectReferenceValue));
            }
            foreach (var pf in All(scene).OfType<XRPokeFilter>())
            {
                var it = pf.GetComponent<XRBaseInteractable>();
                var so = new SerializedObject(pf);
                L(sn + " | POKE-TARGET @" + HandsDump.PathOf(pf.transform) + " active=" + pf.gameObject.activeInHierarchy + " interactable=" + (it == null ? "NONE" : it.GetType().Name + " layers=" + it.interactionLayers.value) + " pokeCollider=" + Describe(so.FindProperty("m_PokeCollider").objectReferenceValue) + " filterInteractable=" + Describe(so.FindProperty("m_Interactable").objectReferenceValue) + " colliders=" + pf.GetComponentsInChildren<Collider>(true).Count(c => c.enabled) + " physLayer=" + LayerMask.LayerToName(pf.gameObject.layer));
            }
            foreach (var c in All(scene).Where(c => c != null && c.GetType().Name == "Container"))
                L(sn + " | CONTAINER @" + HandsDump.PathOf(c.transform) + " layers=" + ((XRBaseInteractable)c).interactionLayers.value + " physLayer=" + LayerMask.LayerToName(c.gameObject.layer));
            L(sn + " | XRInteractionManager count=" + All(scene).Count(c => c is UnityEngine.XR.Interaction.Toolkit.XRInteractionManager));
        }
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log("HandsVerify written to " + outPath);
    }

    static string Ref(SerializedObject so, string prop)
    {
        var p = so.FindProperty(prop);
        if (p == null) return "<noprop>";
        var o = p.objectReferenceValue;
        return o == null ? "null" : o.name + "[" + Path.GetFileName(AssetDatabase.GetAssetPath(o)) + "]";
    }
}

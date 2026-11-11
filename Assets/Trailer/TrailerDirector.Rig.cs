// Trailer capture tooling (not part of the game). The "fake player" rig: a head camera with human-like smoothing,
// breathing and sway, and two fake tracked hands (springy reach with overshoot, idle sway, finger curl) whose wrist
// poses go to TrailerHandProvider. Everything is changed at run time only; nothing is saved.
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SeparaTrailer
{
    [DefaultExecutionOrder(1000)]
    public partial class TrailerDirector
    {
        // ------------------------------------------------------------------ rig state

        private Camera cam;
        private XROrigin origin;
        private Transform space;          // tracking space (Camera Offset)

        // Head: base pose (before noise). Follow mode smooths toward targets; dolly mode writes the base pose directly.
        private Vector3 headPos;
        private Quaternion headRot = Quaternion.identity;
        private bool headFollow;
        private Vector3 headPosTarget;
        private Vector3 lookPoint;
        private Transform lookTransform;
        private Vector3 lookOffset;
        private float headSmooth = 0.45f;
        private float headPosSmooth = 0.6f;
        private Vector3 headPosVel;
        private float yawVel, pitchVel;
        private float headNoise = 1f;
        private float maxPitch;           // > 0: the head never looks further down than this (degrees), keeps the horizon in frame

        private class HandRig
        {
            public int Index;            // 0 left, 1 right
            public float Side;           // -1 left, +1 right
            public GameObject Root;      // "Left Hand" / "Right Hand"
            public NearFarInteractor Interactor;
            public Transform Aim;
            public Transform PinchPose;
            public bool Tracked;
            public bool Rest = true;     // follow the head-relative rest pose
            public Vector3 Target;       // world, when not resting
            public Quaternion TargetRot = Quaternion.identity;
            public Vector3 Pos;
            public Vector3 Vel;
            public Quaternion Rot = Quaternion.identity;
            public bool Initialised;
            public float Curl;
            public Vector3? HoldLocal;   // held in front of the body (head yaw frame), when set and not resting
            public Vector3? AimAt;       // world point the aim ray points to (null: along the fingers)
            public Transform AimAtTransform;
            public Vector3 RestLocal;
            public float Seed;
        }

        private readonly HandRig[] hands = new HandRig[2];

        // ------------------------------------------------------------------ setup

        private IEnumerator SetupRig()
        {
            // Let AlinearJugador / the screen fade finish their start-up work first.
            yield return new WaitForSeconds(3.5f);
            origin = FindFirstObjectByType<XROrigin>();
            cam = origin != null ? origin.Camera : Camera.main;
            space = origin != null && origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : cam.transform.parent;
            foreach (TrackedPoseDriver tpd in cam.GetComponents<TrackedPoseDriver>())
            {
                tpd.enabled = false;
            }
            headPos = cam.transform.position;
            headRot = cam.transform.rotation;
            headPosTarget = headPos;
            lookPoint = headPos + headRot * Vector3.forward * 10f;

            MuteMusic();
            HideDebug();

            for (int i = 0; i < 2; i++)
            {
                var h = new HandRig { Index = i, Side = i == 0 ? -1f : 1f, Seed = 17.3f * (i + 1) };
                h.RestLocal = new Vector3(h.Side * 0.19f, -0.25f, 0.36f);
                Transform t = origin != null ? origin.transform.Find("Camera Offset/" + (i == 0 ? "Left Hand" : "Right Hand")) : null;
                if (t != null)
                {
                    h.Root = t.gameObject;
                    h.Interactor = t.GetComponentInChildren<NearFarInteractor>(true);
                    Transform aim = t.Find("Aim Pose");
                    h.Aim = aim;
                    h.PinchPose = t.Find("Pinch Grab Pose");
                    foreach (TrackedPoseDriver tpd in t.GetComponentsInChildren<TrackedPoseDriver>(true))
                    {
                        if (tpd.transform == aim || tpd.transform == h.PinchPose)
                        {
                            tpd.enabled = false;
                        }
                    }
                }
                hands[i] = h;
            }
            Log("rig ready cam=" + V(cam.transform.position) + " space=" + (space != null ? space.name : "-") +
                " hands=" + (hands[0].Root != null) + "," + (hands[1].Root != null) + " aim=" + (hands[1].Aim != null));
        }

        private void MuteMusic()
        {
            // The trailer soundtrack is laid over in the edit: silence the game's music layers, keep SFX and ambience.
            int n = 0;
            foreach (AudioSource a in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool music = a.name.StartsWith("Music") || (a.transform.parent != null && a.transform.parent.name == "Music");
                if (!music && a.outputAudioMixerGroup != null)
                {
                    string g = a.outputAudioMixerGroup.name;
                    music = g == "Music" || g == "1A" || g == "1B" || g == "2" || g == "3" || g == "Intro" || g.StartsWith("Level");
                }
                if (music && !a.mute)
                {
                    a.mute = true;
                    n++;
                }
            }
            if (n > 0)
            {
                Log("muted music sources: " + n);
            }
        }

        private void HideDebug()
        {
            // Editor-only test helpers (keyboard evolve / victory / restart): off for the capture.
            foreach (MonoBehaviour b in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string n = b.GetType().Name;
                if (n == "TestEvolve" || n == "TestVictory" || n == "TestPerformance" || n == "LongTouchRestart")
                {
                    b.enabled = false;
                }
            }
        }

        // ------------------------------------------------------------------ head API

        /// <summary>Puts the head somewhere at once (between shots).</summary>
        private void HeadCut(Vector3 pos, Vector3 look)
        {
            headFollow = true;
            headPos = pos;
            headPosTarget = pos;
            headPosVel = Vector3.zero;
            lookTransform = null;
            lookPoint = look;
            headRot = Quaternion.LookRotation(look - pos, Vector3.up);
            yawVel = pitchVel = 0f;
            foreach (HandRig h in hands)
            {
                if (h != null)
                {
                    h.Initialised = false;
                }
            }
            ApplyHead(0f);
        }

        /// <summary>Human-like look: the head turns toward the point with eased acceleration and settling.</summary>
        private void LookAt(Vector3 point, float smooth = 0.45f)
        {
            headFollow = true;
            lookTransform = null;
            lookPoint = point;
            headSmooth = smooth;
        }

        private void LookAt(Transform t, Vector3 offset, float smooth = 0.45f)
        {
            headFollow = true;
            lookTransform = t;
            lookOffset = offset;
            if (t != null)
            {
                lookPoint = t.position + offset;
            }
            headSmooth = smooth;
        }

        private void MoveHead(Vector3 pos, float smooth = 0.6f)
        {
            headPosTarget = pos;
            headPosSmooth = smooth;
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            float s = t * t * t * (t * (t * 6f - 15f) + 10f);   // smootherstep
            return Mathf.Lerp(t, s, 0.65f);                     // keep some speed at the ends: never a dead stop
        }

        /// <summary>Wide shot camera move: position and look point eased from A to B.</summary>
        private IEnumerator Dolly(Vector3 p0, Vector3 l0, Vector3 p1, Vector3 l1, float duration)
        {
            headFollow = false;
            float t = 0f;
            while (t < duration)
            {
                float k = Ease(t / duration);
                headPos = Vector3.Lerp(p0, p1, k);
                Vector3 look = Vector3.Lerp(l0, l1, k);
                headRot = Quaternion.LookRotation(look - headPos, Vector3.up);
                t += Time.deltaTime;
                yield return null;
            }
            headPos = p1;
            headRot = Quaternion.LookRotation(l1 - p1, Vector3.up);
        }

        private void SetDollyPose(Vector3 p, Vector3 l)
        {
            headFollow = false;
            headPos = p;
            headRot = Quaternion.LookRotation(l - p, Vector3.up);
            ApplyHead(0f);
        }

        // ------------------------------------------------------------------ hands API

        // The game's hand look (XRI demo ghost hand: depth-only pass + Unity_Hand_Noise, dark 66% body) reads as a grey
        // smear on a flat 2D frame. Runtime material instances only (nothing saved): same style, a lighter, more opaque body.
        private static readonly Color HandBody = new Color(0.80f, 0.74f, 0.69f, 0.85f);
        private const float HandRimPower = 2.5f;
        private readonly HashSet<Renderer> styledHands = new HashSet<Renderer>();

        private void StyleHands()
        {
            foreach (HandRig h in hands)
            {
                if (h == null || h.Root == null)
                {
                    continue;
                }
                foreach (SkinnedMeshRenderer smr in h.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (styledHands.Contains(smr))
                    {
                        continue;
                    }
                    styledHands.Add(smr);
                    Material[] mats = smr.materials;
                    string names = "";
                    foreach (Material m in mats)
                    {
                        names += (m != null ? m.name + "/" + (m.shader != null ? m.shader.name : "-") : "-") + " ";
                        if (m != null && m.HasProperty("_MainColor") && m.HasProperty("_EdgeColor"))
                        {
                            // XRI demo hand (Unity_Hand_Noise): dark 66% body, thin rim, speckle noise.
                            m.SetColor("_MainColor", HandBody);
                            m.SetColor("_EdgeColor", new Color(1f, 1f, 1f, 1f));
                            m.SetFloat("_EdgeHighlightPower", HandRimPower);
                            m.SetFloat("_NoiseStrength", 0.12f);
                        }
                    }
                    smr.materials = mats;
                    Log("styled hand " + smr.name + ": " + names);
                }
            }
        }

        private void ShowHands(bool left, bool right)
        {
            TrailerHandProvider.Install();
            StyleHands();
            hands[0].Tracked = left;
            hands[1].Tracked = right;
            foreach (HandRig h in hands)
            {
                h.Initialised = false;
            }
        }

        private void HandRest(int i)
        {
            hands[i].Rest = true;
            hands[i].HoldLocal = null;
            hands[i].AimAt = null;
            hands[i].AimAtTransform = null;
        }

        private Vector3 Shoulder(HandRig h)
        {
            Vector3 right = headRot * Vector3.right;
            right.y = 0f;
            right.Normalize();
            return headPos + Vector3.down * 0.22f + right * (h.Side * 0.17f);
        }

        /// <summary>Extends the hand from the shoulder toward a world point, fingers pointing at it.</summary>
        private void HandReach(int i, Vector3 point, float reach, float palmRoll = 25f, float lift = 0f, float across = 0f)
        {
            HandRig h = hands[i];
            Vector3 shoulder = Shoulder(h);
            Vector3 dir = (point - shoulder).normalized;
            // lift > 0: the arm stays higher (closer to the camera's view) while the fingers still point at the target.
            Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
            Vector3 armDir = Vector3.Slerp(dir, flat, lift).normalized;
            h.Rest = false;
            h.HoldLocal = null;
            h.Target = shoulder + armDir * reach;
            // across > 0: fingers angled in across the view, so the camera sees the hand in profile rather than end-on.
            Vector3 rightFlat = Vector3.Cross(Vector3.up, flat);
            Vector3 fingers = (dir - rightFlat * (h.Side * across)).normalized;
            h.TargetRot = HandRotation(fingers, h.Side, palmRoll);
        }

        private static Quaternion HandRotation(Vector3 fingers, float side, float palmRoll)
        {
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(fingers, up)) > 0.95f)
            {
                up = Vector3.forward;
            }
            // Back of the hand up, rolled so the palm turns a little inward (thumb side up).
            return Quaternion.LookRotation(fingers, up) * Quaternion.AngleAxis(-side * palmRoll, Vector3.forward);
        }

        /// <summary>Holds the hand in front of the body, relative to where the head faces.</summary>
        private void HandHold(int i, Vector3 local)
        {
            hands[i].Rest = false;
            hands[i].HoldLocal = local;
        }

        private void SetFov(float fov)
        {
            if (cam != null)
            {
                cam.fieldOfView = fov;
            }
        }

        // Head yaw, plus part of its pitch: hands follow the body more than the eyes.
        private Quaternion BodyFrame(float pitchShare)
        {
            Vector3 e = headRot.eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, e.x) * pitchShare;
            return Quaternion.Euler(pitch, e.y, 0f);
        }

        private void HandAim(int i, Vector3 point)
        {
            hands[i].AimAt = point;
            hands[i].AimAtTransform = null;
        }

        private void HandAim(int i, Transform t)
        {
            hands[i].AimAtTransform = t;
            hands[i].AimAt = t != null ? t.position : (Vector3?)null;
        }

        private IEnumerator Curl(int i, float to, float duration)
        {
            float from = hands[i].Curl;
            float t = 0f;
            while (t < duration)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                hands[i].Curl = Mathf.Lerp(from, to, k);
                t += Time.deltaTime;
                yield return null;
            }
            hands[i].Curl = to;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (cam == null)
            {
                return;
            }
            float dt = Time.deltaTime;
            if (headFollow)
            {
                if (lookTransform != null)
                {
                    lookPoint = lookTransform.position + lookOffset;
                }
                headPos = Vector3.SmoothDamp(headPos, headPosTarget, ref headPosVel, headPosSmooth, 3f, dt);
                Quaternion want = Quaternion.LookRotation(lookPoint - headPos, Vector3.up);
                Vector3 e = headRot.eulerAngles;
                Vector3 w = want.eulerAngles;
                if (maxPitch > 0f && Mathf.DeltaAngle(0f, w.x) > maxPitch)
                {
                    w.x = maxPitch;
                }
                float yaw = Mathf.SmoothDampAngle(e.y, w.y, ref yawVel, headSmooth, 220f, dt);
                float pitch = Mathf.SmoothDampAngle(e.x, w.x, ref pitchVel, headSmooth * 1.1f, 160f, dt);
                headRot = Quaternion.Euler(pitch, yaw, 0f);
            }
            ApplyHead(Time.time);
            UpdateHands(dt);
        }

        private void ApplyHead(float time)
        {
            if (cam == null)
            {
                return;
            }
            float a = headNoise;
            // Breathing: a few millimetres up/down at ~0.22 Hz, plus slow Perlin sway of position and orientation.
            Vector3 bob = Vector3.up * (0.004f * Mathf.Sin(time * Mathf.PI * 2f * 0.22f));
            Vector3 sway = new Vector3(Mathf.PerlinNoise(time * 0.23f, 3.1f) - 0.5f, Mathf.PerlinNoise(time * 0.19f, 7.7f) - 0.5f, Mathf.PerlinNoise(time * 0.21f, 11.3f) - 0.5f) * 0.012f;
            float yawN = (Mathf.PerlinNoise(time * 0.31f, 21.7f) - 0.5f) * 1.1f;
            float pitchN = (Mathf.PerlinNoise(time * 0.27f, 31.9f) - 0.5f) * 0.9f + 0.15f * Mathf.Sin(time * Mathf.PI * 2f * 0.22f);
            float rollN = (Mathf.PerlinNoise(time * 0.17f, 41.3f) - 0.5f) * 0.8f;
            cam.transform.SetPositionAndRotation(headPos + (bob + sway) * a, headRot * Quaternion.Euler(pitchN * a, yawN * a, rollN * a));
        }

        private void UpdateHands(float dt)
        {
            if (hands[0] == null)
            {
                return;
            }
            float time = Time.time;
            Transform head = cam.transform;
            foreach (HandRig h in hands)
            {
                TrailerHandProvider.Tracked[h.Index] = h.Tracked;
                // The game's modality manager turns the hand objects off when tracking is lost and may not turn them back on.
                if (h.Tracked && h.Root != null && !h.Root.activeSelf)
                {
                    h.Root.SetActive(true);
                    Log("reactivated " + h.Root.name);
                }
                // After a reactivation the visual's tracking events may leave the hand mesh renderer disabled.
                if (h.Tracked)
                {
                    foreach (Renderer r in styledHands)
                    {
                        if (r != null && (!r.enabled || !r.gameObject.activeSelf) && r.name.EndsWith("Hand") && r.transform.IsChildOf(h.Root.transform))
                        {
                            r.enabled = true;
                            if (!r.gameObject.activeSelf)
                            {
                                r.gameObject.SetActive(true);
                            }
                            Log("enabled hand mesh " + r.name);
                        }
                    }
                }
                if (!h.Tracked)
                {
                    continue;
                }
                Vector3 target;
                Quaternion targetRot;
                if (h.Rest)
                {
                    Quaternion hr = BodyFrame(0.8f);
                    target = headPos + hr * h.RestLocal;
                    Vector3 fingers = hr * new Vector3(-h.Side * 0.3f, -0.35f, 1f).normalized;
                    targetRot = HandRotation(fingers, h.Side, 30f);
                }
                else if (h.HoldLocal.HasValue)
                {
                    Quaternion hr = BodyFrame(0.85f);
                    target = headPos + hr * h.HoldLocal.Value;
                    Vector3 fingers = hr * new Vector3(-h.Side * 0.35f, -0.1f, 1f).normalized;
                    targetRot = HandRotation(fingers, h.Side, 60f);
                }
                else
                {
                    target = h.Target;
                    targetRot = h.TargetRot;
                }
                if (!h.Initialised)
                {
                    h.Pos = target;
                    h.Vel = Vector3.zero;
                    h.Rot = targetRot;
                    h.Initialised = true;
                }
                // Underdamped spring: eased start, a little overshoot, settle.
                const float omega = 9f, zeta = 0.62f;
                if (dt > 0f)
                {
                    Vector3 acc = omega * omega * (target - h.Pos) - 2f * zeta * omega * h.Vel;
                    h.Vel += acc * dt;
                    h.Pos += h.Vel * dt;
                    h.Rot = Quaternion.Slerp(h.Rot, targetRot, 1f - Mathf.Exp(-7f * dt));
                }
                // Idle sway: a centimetre or so, a couple of degrees.
                Vector3 sway = new Vector3(Mathf.PerlinNoise(time * 0.45f, h.Seed) - 0.5f, Mathf.PerlinNoise(time * 0.4f, h.Seed + 5f) - 0.5f, Mathf.PerlinNoise(time * 0.35f, h.Seed + 9f) - 0.5f) * 0.022f;
                Quaternion swayRot = Quaternion.Euler((Mathf.PerlinNoise(time * 0.5f, h.Seed + 13f) - 0.5f) * 5f, (Mathf.PerlinNoise(time * 0.45f, h.Seed + 17f) - 0.5f) * 5f, (Mathf.PerlinNoise(time * 0.4f, h.Seed + 19f) - 0.5f) * 6f);
                Vector3 wp = h.Pos + sway;
                Quaternion wr = h.Rot * swayRot;

                TrailerHandProvider.WristPose[h.Index] = new Pose(space.InverseTransformPoint(wp), Quaternion.Inverse(space.rotation) * wr);
                TrailerHandProvider.Curl[h.Index] = h.Curl;

                // Aim pose (Meta style: from the hand, toward what the player points at) and pinch pose.
                Vector3 aimPos = wp + wr * new Vector3(0f, -0.01f, 0.07f);
                if (h.AimAtTransform != null)
                {
                    h.AimAt = h.AimAtTransform.position;
                }
                // Nothing to point at: aim a little above the horizon, so the game's "choosing" beam stays hidden.
                Vector3 aimDir = h.AimAt.HasValue ? (h.AimAt.Value - aimPos).normalized : Quaternion.Euler(-8f, headRot.eulerAngles.y + h.Side * 10f, 0f) * Vector3.forward;
                if (h.Aim != null)
                {
                    h.Aim.SetPositionAndRotation(aimPos, Quaternion.LookRotation(aimDir, Vector3.up));
                }
                if (h.PinchPose != null)
                {
                    h.PinchPose.SetPositionAndRotation(wp + wr * new Vector3(0f, -0.03f, 0.1f), Quaternion.LookRotation(aimDir, Vector3.up));
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Transform FindResiduo(string name)
        {
            foreach (Residuo r in FindObjectsByType<Residuo>(FindObjectsSortMode.None))
            {
                if (r.name == name)
                {
                    return r.transform;
                }
            }
            return null;
        }

        private static Container FindContainer(string name)
        {
            foreach (Container c in FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (c.name == name)
                {
                    return c;
                }
            }
            return null;
        }

        private static void EvolveAll()
        {
            if (!GameManager.HasInstance || GameManager.Instance.enviroment == null)
            {
                return;
            }
            foreach (IEvolve e in GameManager.Instance.enviroment.GetComponentsInChildren<IEvolve>())
            {
                e.Evolve();
            }
        }

        private IEnumerator Shot(string name, IEnumerator body)
        {
            Log("shot start " + name);
            ShotStarted?.Invoke(name);
            yield return body;
            ShotEnded?.Invoke(name);
            Log("shot end " + name);
        }

        private static bool Wants(string shot)
        {
            if (Group == "all")
            {
                return true;
            }
            foreach (string part in Group.Split(','))
            {
                string p = part.Trim();
                if (p.Length > 0 && shot.StartsWith(p))
                {
                    return true;
                }
            }
            return false;
        }

        private IEnumerator Capture(string path)
        {
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            yield return null;
        }
    }
}

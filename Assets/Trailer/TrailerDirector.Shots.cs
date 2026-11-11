// Trailer capture tooling (not part of the game). The shot list.
using System.Collections;
using System.IO;
using UnityEngine;

namespace SeparaTrailer
{
    public partial class TrailerDirector
    {
        public const string ScoutDir = "G:/SeparaVRecover/_trailer/scout";

        private static Vector3 Dir(float yaw, float pitch)
        {
            return Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }

        private IEnumerator RunShots()
        {
            yield return SetupRig();
            if (Group.StartsWith("scout"))
            {
                yield return Scout();
                yield break;
            }
            if (Group == "grab" || Group == "all" || Group.Contains("grab_"))
            {
                yield return GrabSession();
            }
            else
            {
                yield return WorldSession();
            }
        }

        private const float SceneryNear = 0.1f;

        private bool WantsScenery()
        {
            return WantsShot("scenery") || Group.Contains("scenery_");
        }

        private bool WantsShot(string name)
        {
            return Group == "grab" || Group == "world" || Group == "all" || Wants(name);
        }

        // ------------------------------------------------------------------ first person grabs

        private Vector3 eyeHome;
        private static readonly Vector3 AheadLook = new Vector3(-2.2f, 0.9f, 8.6f);

        private IEnumerator GrabSession()
        {
            eyeHome = cam.transform.position;
            headNoise = 1f;
            maxPitch = 26f;
            SetFov(62f);
            ShowHands(true, true);
            HandRest(0);
            HandRest(1);
            hands[0].Curl = 0.2f;
            hands[1].Curl = 0.2f;
            HeadCut(eyeHome, AheadLook);
            yield return new WaitForSeconds(1.5f);
            foreach (Residuo rr in FindObjectsByType<Residuo>(FindObjectsSortMode.None))
            {
                Log("residuo " + rr.name + " at " + V(rr.transform.position) + " d=" + Vector3.Distance(Flat(rr.transform.position), Flat(eyeHome)).ToString("0.0"));
            }

            string green = PickNearest("panial", "Sanguche");
            if (WantsShot("grab_green"))
            {
                yield return Shot("03_grab_green", Grab(0, green, "verde", false));
                yield return Settle();
            }
            if (WantsShot("grab_orange"))
            {
                yield return Shot("04_grab_orange", Grab(1, "Botella Marron", "naranja", false));
                yield return Settle();
            }
            if (WantsShot("grab_close"))
            {
                string bottle = FindResiduo("Agua") != null ? "Agua" : PickContaining("Botella", "Botella Marron");
                yield return Shot("04b_grab_close", GrabClose(1, bottle));
                yield return Settle();
            }
            if (WantsShot("grab_wrong"))
            {
                yield return Shot("05_grab_wrong", Grab(1, "LataRoja", "verde", true));
                yield return Settle();
            }
        }

        private string PickNearest(params string[] names)
        {
            string best = names[0];
            float bestD = float.MaxValue;
            foreach (string n in names)
            {
                Transform t = FindResiduo(n);
                if (t == null)
                {
                    continue;
                }
                float d = Vector3.Distance(Flat(t.position), Flat(eyeHome));
                if (d < bestD)
                {
                    bestD = d;
                    best = n;
                }
            }
            return best;
        }

        private string PickContaining(string part, string exclude)
        {
            string best = null;
            float bestD = float.MaxValue;
            foreach (Residuo rr in FindObjectsByType<Residuo>(FindObjectsSortMode.None))
            {
                if (!rr.name.Contains(part) || rr.name == exclude)
                {
                    continue;
                }
                float d = Vector3.Distance(Flat(rr.transform.position), Flat(eyeHome));
                if (d < bestD)
                {
                    bestD = d;
                    best = rr.name;
                }
            }
            return best ?? exclude;
        }

        private static readonly Vector3 HudPoint = new Vector3(10.4f, 5.1f, 8.4f);

        // Tight beat: the hand closes on the bottle, lifts it in front of the eyes, the HUD names it behind.
        private IEnumerator GrabClose(int hand, string residuoName)
        {
            HandRig h = hands[hand];
            Transform r = FindResiduo(residuoName);
            if (r == null)
            {
                Log("grab close: missing " + residuoName);
                yield break;
            }
            Vector3 rp = r.position;
            Log("grab close " + residuoName + " at " + V(rp));
            float fov = cam.fieldOfView;
            float pitchLimit = maxPitch;
            maxPitch = 0f;
            SetFov(44f);
            HeadCut(cam.transform.position, rp + Vector3.up * 0.12f);
            HandRest(hand);
            yield return new WaitForSeconds(0.35f);
            HandReach(hand, rp, 0.5f, 70f, 0.2f, 0.6f);
            HandAim(hand, r);
            yield return Curl(hand, 0f, 0.3f);
            yield return new WaitForSeconds(0.8f);
            yield return Curl(hand, 0.9f, 0.28f);
            float t = 0f;
            while (h.Interactor != null && !h.Interactor.hasSelection && t < 0.9f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Log("grab close: selected=" + (h.Interactor != null && h.Interactor.hasSelection));
            yield return new WaitForSeconds(0.5f);
            // Lift it up to look at it: the head rises toward the HUD, which now describes the object.
            HandHold(hand, new Vector3(h.Side * 0.05f, -0.07f, 0.36f));
            Vector3 ahead = headPos + Flat(HudPoint - headPos).normalized * 10f + Vector3.up * 0.6f;
            LookAt(ahead, 0.6f);
            yield return new WaitForSeconds(3.2f);
            maxPitch = pitchLimit;
            SetFov(fov);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        // Between grab clips: back to a neutral pose (not recorded).
        private IEnumerator Settle()
        {
            HandRest(0);
            HandRest(1);
            hands[0].Curl = 0.2f;
            hands[1].Curl = 0.2f;
            LookAt(AheadLook, 0.5f);
            yield return new WaitForSeconds(1.5f);
        }

        private IEnumerator Grab(int hand, string residuoName, string containerName, bool wrong)
        {
            HandRig h = hands[hand];
            Transform r = FindResiduo(residuoName);
            Container c = FindContainer(containerName);
            if (r == null || c == null)
            {
                Log("grab: missing " + residuoName + " / " + containerName);
                yield break;
            }
            Vector3 rp = r.position;
            Log("grab " + residuoName + " at " + V(rp) + " -> " + containerName + " hand " + hand);

            // 1. A breath looking ahead (HUD and containers in view), then the eyes find the residuo.
            yield return new WaitForSeconds(0.9f);
            LookAt(rp + Vector3.up * 0.25f, 0.42f);
            yield return new WaitForSeconds(0.45f);

            // 2. Reach, open hand pointing at it: the game's beam locks onto it.
            HandReach(hand, rp, 0.46f, 70f, 0.45f, 0.7f);
            HandAim(hand, r);
            yield return Curl(hand, 0f, 0.3f);
            yield return new WaitForSeconds(0.9f);
            if (Group.Contains("scout")) { LogHands("reach"); yield return Capture("G:/SeparaVRecover/_trailer/scout/chk_reach.png"); }

            // 3. Close the hand: the game's own close-to-grab picks it up and it flies to the palm.
            yield return Curl(hand, 0.9f, 0.25f);
            float t = 0f;
            while (h.Interactor != null && !h.Interactor.hasSelection && t < 0.9f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (h.Interactor != null && !h.Interactor.hasSelection)
            {
                Log("grab: no selection by the game, forcing SelectEnter");
                var gi = r.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable>();
                if (gi != null && h.Interactor.interactionManager != null)
                {
                    h.Interactor.interactionManager.SelectEnter((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)h.Interactor, gi);
                }
            }
            Log("grab: selected=" + (h.Interactor != null && h.Interactor.hasSelection));
            LookAt(r, Vector3.zero, 0.35f);
            HandHold(hand, new Vector3(h.Side * 0.08f, -0.14f, 0.40f));
            yield return new WaitForSeconds(0.9f);

            // 4. Turn to the container, arm aiming at it.
            Vector3 aim = c.AimPoint;
            LookAt(aim + Vector3.down * 0.6f, 0.5f);
            yield return new WaitForSeconds(0.35f);
            HandReach(hand, aim + Vector3.up * 1.4f, 0.48f, 40f);
            HandAim(hand, aim);
            yield return new WaitForSeconds(1.0f);

            // 5. Open the hand: released toward the container (or tossed back if it is the wrong one).
            yield return Curl(hand, 0f, 0.22f);
            t = 0f;
            while (h.Interactor != null && h.Interactor.hasSelection && t < 0.8f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (h.Interactor != null && h.Interactor.hasSelection)
            {
                Log("grab: still selected, forcing SelectExit");
                var gi = h.Interactor.firstInteractableSelected;
                if (gi != null)
                {
                    h.Interactor.interactionManager.SelectExit((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)h.Interactor, gi);
                }
            }
            if (r != null)
            {
                LookAt(r, Vector3.zero, 0.5f);
            }
            yield return new WaitForSeconds(0.35f);
            HandRest(hand);
            hands[hand].Curl = 0.2f;
            if (wrong)
            {
                yield return new WaitForSeconds(1.6f);
                LookAt(aim + Vector3.down * 0.4f, 0.6f);
                yield return new WaitForSeconds(1.4f);
            }
            else
            {
                yield return new WaitForSeconds(2.6f);
                LookAt(aim + Vector3.up * 0.3f, 0.6f);   // lid, fireworks
                yield return new WaitForSeconds(1.9f);
            }
        }

        // ------------------------------------------------------------------ wide shots

        private static readonly Vector3 PoseP = new Vector3(-20f, 15f, 8f);
        private static readonly Vector3 LookP = new Vector3(40f, 0f, 20f);

        private IEnumerator WorldSession()
        {
            eyeHome = cam.transform.position;
            ShowHands(false, false);
            maxPitch = 0f;
            headNoise = 0.45f;
            SetFov(58f);
            if (WantsShot("concept_a"))
            {
                yield return Wide("01_concept_a", new Vector3(-34f, 34f, -22f), new Vector3(35f, 0f, 22f), new Vector3(-30f, 26f, 26f), new Vector3(35f, 0f, 14f), 6f);
            }
            if (WantsShot("concept_b"))
            {
                yield return Wide("02_concept_b", new Vector3(-12.5f, 4.6f, 8.6f), new Vector3(6f, 0.8f, 8.6f), new Vector3(-10f, 3.2f, 8.6f), new Vector3(2f, 1.0f, 8.6f), 6f);
            }
            if (WantsShot("evolve"))
            {
                yield return Static("06_evolve_before", PoseP, LookP, 4f);
                var poses = new (Vector3 p0, Vector3 l0, Vector3 p1, Vector3 l1)[]
                {
                    (new Vector3(-20f, 15f, 8f), LookP, new Vector3(-17f, 13f, 9f), LookP),
                    (new Vector3(-16f, 9f, 8.5f), new Vector3(10f, 0f, 8.5f), new Vector3(-14f, 8f, 10f), new Vector3(10f, 0f, 9f)),
                    (new Vector3(46f, 3f, 21f), new Vector3(0f, 2f, 9f), new Vector3(43f, 3.4f, 15f), new Vector3(0f, 2f, 9f)),
                    (new Vector3(-30f, 60f, 20f), new Vector3(40f, 0f, 20f), new Vector3(-26f, 55f, 12f), new Vector3(40f, 0f, 20f)),
                    (new Vector3(-25f, 25f, -10f), LookP, new Vector3(-22f, 22f, -4f), LookP),
                };
                for (int i = 0; i < poses.Length; i++)
                {
                    var p = poses[i];
                    yield return Wide((7 + i).ToString("00") + "_evolve_" + (i + 1), p.p0, p.l0, p.p1, p.l1, 6.5f, 1.2f);
                }
                yield return Static("12_evolve_after", PoseP, LookP, 4f);
            }
            else if (WantsScenery() || WantsShot("win"))
            {
                for (int i = 0; i < 5; i++)
                {
                    EvolveAll();
                    yield return new WaitForSeconds(2.5f);
                }
                yield return new WaitForSeconds(1.5f);
            }
            if (WantsScenery())
            {
                // Distant buildings z-fight at near=0.01 / far=1000: use a larger near plane for the far scenery shots only
                // (runtime camera value, nothing saved). Restored afterwards so the hand shots keep the game's value.
                float gameNear = cam.nearClipPlane;
                cam.nearClipPlane = SceneryNear;
                if (Group == "scenery" || Group == "all" || Group.Contains("scenery_a"))
                {
                    yield return Wide("13_scenery_a", new Vector3(-15f, 2.1f, -15.6f), new Vector3(100f, 2f, -15.6f), new Vector3(2f, 2.3f, -15.0f), new Vector3(100f, 2.2f, -15.6f), 6f);
                }
                if (Group == "scenery" || Group == "all" || Group.Contains("scenery_b"))
                {
                    yield return Wide("14_scenery_b", new Vector3(46f, 3f, 20f), new Vector3(0f, 2f, 9f), new Vector3(41f, 3.6f, 13f), new Vector3(0f, 2.5f, 9f), 6f);
                }
                if (Group == "scenery" || Group == "all" || Group.Contains("scenery_c"))
                {
                    yield return Wide("15_scenery_c", new Vector3(1.3f, 2.1f, 95f), new Vector3(1.3f, 2f, 0f), new Vector3(1.3f, 2.3f, 80f), new Vector3(1.5f, 2.2f, 0f), 6f);
                }
                cam.nearClipPlane = gameNear;
            }
            if (WantsShot("win"))
            {
                SetFov(66f);
                headNoise = 1f;
                HeadCut(eyeHome, new Vector3(-2.2f, 1.6f, 8.6f));
                ShowHands(true, true);
                HandRest(0);
                HandRest(1);
                hands[0].Curl = 0.2f;
                hands[1].Curl = 0.2f;
                yield return new WaitForSeconds(0.8f);
                LogHands("win setup");
                yield return Shot("16_win", Win());
                LogHands("win end");
            }
        }

        private IEnumerator Wide(string name, Vector3 p0, Vector3 l0, Vector3 p1, Vector3 l1, float duration, float evolveAt = -1f)
        {
            SetDollyPose(p0, l0);
            yield return null;
            yield return null;
            yield return Shot(name, WideBody(p0, l0, p1, l1, duration, evolveAt));
        }

        private IEnumerator WideBody(Vector3 p0, Vector3 l0, Vector3 p1, Vector3 l1, float duration, float evolveAt)
        {
            if (evolveAt >= 0f)
            {
                StartCoroutine(EvolveLater(evolveAt));
            }
            yield return Dolly(p0, l0, p1, l1, duration);
        }

        private IEnumerator EvolveLater(float delay)
        {
            yield return new WaitForSeconds(delay);
            EvolveAll();
            Log("evolve");
        }

        private IEnumerator Static(string name, Vector3 p, Vector3 l, float duration)
        {
            float noise = headNoise;
            headNoise = 0f;
            SetDollyPose(p, l);
            yield return null;
            yield return Shot(name, Dolly(p, l, p, l, duration));
            headNoise = noise;
        }

        private void LogHands(string tag)
        {
            foreach (HandRig h in hands)
            {
                if (h == null || h.Root == null)
                {
                    continue;
                }
                int on = 0, all = 0;
                foreach (Renderer rr in h.Root.GetComponentsInChildren<Renderer>(true))
                {
                    all++;
                    if (rr.enabled && rr.gameObject.activeInHierarchy)
                    {
                        on++;
                        Log("   on " + PathOf(rr.transform) + " mat=" + (rr.sharedMaterial != null ? rr.sharedMaterial.name : "-") + " vis=" + rr.isVisible + " bounds=" + V(rr.bounds.center) + " size=" + V(rr.bounds.size) + " lossy=" + V(rr.transform.lossyScale));
                        if (rr is SkinnedMeshRenderer smr && smr.bones != null)
                        {
                            foreach (Transform b in smr.bones)
                            {
                                if (b != null && (b.name.Contains("Wrist") || b.name.Contains("IndexTip") || b.name.Contains("Palm")))
                                {
                                    Log("     bone " + b.name + " world=" + V(b.position) + " camLocal=" + V(cam.transform.InverseTransformPoint(b.position)) + " lossy=" + V(b.lossyScale));
                                }
                            }
                        }
                    }
                }
                Log(tag + " cam=" + V(cam.transform.position) + " space=" + V(space.position) + " spaceRot=" + V(space.eulerAngles) + " originY=" + (origin != null ? origin.transform.position.y.ToString("0.00") : "-") + " wristCamLocal=" + V(cam.transform.InverseTransformPoint(h.Pos)) + " rootLocal=" + V(h.Root.transform.localPosition) + " rootScale=" + V(h.Root.transform.lossyScale));
                Log(tag + " hand" + h.Index + " active=" + h.Root.gameObject.activeInHierarchy + " renderers " + on + "/" + all + " tracked=" + h.Tracked + " pos=" + V(h.Pos) + " root=" + V(h.Root.transform.position));
                foreach (Transform c in h.Root.transform)
                {
                    Log("   child " + c.name + " active=" + c.gameObject.activeSelf);
                }
            }
        }

        private IEnumerator Win()
        {
            Vector3 sky = new Vector3(78f, 26f, 16f);
            WinFireworks.Play();
            yield return new WaitForSeconds(0.6f);
            LookAt(sky, 0.8f);
            yield return new WaitForSeconds(1.6f);
            // Hands up in celebration, waving a little.
            float t = 0f;
            while (t < 4.6f)
            {
                float w = Mathf.Sin(t * 5.5f) * 0.06f;
                Vector3 fwd = Flat(sky - headPos).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                HandReach(0, headPos + fwd * 1f + Vector3.up * 0.55f - right * (0.45f + w), 0.55f, 10f);
                HandReach(1, headPos + fwd * 1f + Vector3.up * 0.55f + right * (0.45f - w), 0.55f, 10f);
                t += Time.deltaTime;
                yield return null;
            }
        }

        // ------------------------------------------------------------------ scouting

        private IEnumerator Scout()
        {
            Directory.CreateDirectory(ScoutDir);
            headNoise = 0f;
            Vector3 eye = cam.transform.position;
            if (Group == "scout" || Group == "scout_hands")
            {
                yield return ScoutHands(eye);
            }
            if (Group == "scout_hands")
            {
                yield break;
            }
            yield return ScoutPoses("dirty", eye);
            for (int i = 0; i < 5; i++)
            {
                EvolveAll();
                yield return new WaitForSeconds(2.5f);
            }
            yield return new WaitForSeconds(1.5f);
            yield return ScoutPoses("clean", eye);
        }

        private IEnumerator ScoutPoses(string tag, Vector3 eye)
        {
            int n = 0;
            // Player view sweep.
            for (int yaw = 0; yaw < 360; yaw += 45)
            {
                HeadCut(eye, eye + Dir(yaw, 5f) * 10f);
                yield return Capture(ScoutDir + "/" + tag + "_eye_y" + yaw + ".png");
                n++;
            }
            var poses = new (string name, Vector3 p, Vector3 l)[]
            {
                ("down", eye, new Vector3(-6.0f, 0.5f, 8.6f)),
                ("verde", eye, new Vector3(-2.23f, 1.2f, 12.77f)),
                ("naranja", eye, new Vector3(-2.23f, 1.2f, 4.27f)),
                ("hud", eye, new Vector3(10.4f, 4.5f, 8.4f)),
                ("behind", new Vector3(-14f, 4f, 8.5f), new Vector3(0f, 1f, 8.5f)),
                ("behind_hi", new Vector3(-16f, 9f, 8.5f), new Vector3(10f, 0f, 8.5f)),
                ("high_city", new Vector3(-20f, 15f, 8f), new Vector3(40f, 0f, 20f)),
                ("high_city2", new Vector3(-25f, 25f, -10f), new Vector3(40f, 0f, 20f)),
                ("plaza_back", new Vector3(45f, 3f, 19f), new Vector3(0f, 2f, 9f)),
                ("plaza_look", new Vector3(25f, 2.5f, 10f), new Vector3(40f, 2f, 19f)),
                ("aerial", new Vector3(35f, 120f, 20f), new Vector3(35.1f, 0f, 20.5f)),
                ("aerial_oblique", new Vector3(-30f, 60f, 20f), new Vector3(40f, 0f, 20f)),
                ("street_x", new Vector3(1.3f, 2f, -60f), new Vector3(1.3f, 2f, 40f)),
                ("street_x2", new Vector3(1.3f, 2f, 95f), new Vector3(1.3f, 2f, 0f)),
                ("street_z", new Vector3(-15f, 2f, -15.6f), new Vector3(100f, 2f, -15.6f)),
                ("street_z_hi", new Vector3(-15f, 8f, -18f), new Vector3(60f, 0f, -10f)),
                ("street_n", new Vector3(-10f, 2f, 54f), new Vector3(80f, 2f, 54f)),
                ("edificio", new Vector3(30f, 3f, -25f), new Vector3(48f, 8f, -40f)),
                ("cheta", new Vector3(30f, 3f, 55f), new Vector3(36f, 4f, 74f)),
                ("piedras", new Vector3(-8f, 2.5f, 15f), new Vector3(-18f, 3f, 25.6f)),
                ("fireworks", eye, new Vector3(78f, 40f, 16f)),
                ("fireworks_wide", new Vector3(-12f, 4f, 2f), new Vector3(78f, 30f, 16f)),
                ("crane", new Vector3(-4f, 6f, -2f), new Vector3(-8f, 1f, 8.5f)),
                ("player_front", new Vector3(-3f, 2.4f, 8.5f), new Vector3(-8f, 1.5f, 8.5f)),
            };
            foreach (var p in poses)
            {
                HeadCut(p.p, p.l);
                yield return Capture(ScoutDir + "/" + tag + "_" + p.name + ".png");
                n++;
            }
            Log("scout " + tag + " captured " + n);
        }

        private IEnumerator ScoutHands(Vector3 eye)
        {
            HeadCut(eye, eye + Dir(90f, 25f) * 10f);
            ShowHands(true, true);
            yield return new WaitForSeconds(1.5f);
            yield return Capture(ScoutDir + "/hands_rest.png");
            Transform r = FindResiduo("Botella Marron");
            Log("hands: subsystem=" + (TrailerHandProvider.Instance != null && TrailerHandProvider.Instance.running) +
                " leftActive=" + (hands[0].Root != null && hands[0].Root.activeInHierarchy) + " rightActive=" + (hands[1].Root != null && hands[1].Root.activeInHierarchy) +
                " botella=" + (r != null ? V(r.position) : "-"));
            if (r != null)
            {
                LookAt(r.position, 0.4f);
                HandReach(1, r.position, 0.5f);
                HandAim(1, r);
                yield return new WaitForSeconds(1.5f);
                yield return Capture(ScoutDir + "/hands_reach_open.png");
                yield return Curl(1, 0.85f, 0.3f);
                yield return new WaitForSeconds(1.2f);
                yield return Capture(ScoutDir + "/hands_reach_closed.png");
                Log("hands: after close selected=" + (hands[1].Interactor != null && hands[1].Interactor.hasSelection));
            }
            HandRest(1);
            yield return Curl(1, 0f, 0.3f);
            ShowHands(false, false);
            yield return new WaitForSeconds(0.5f);
        }
    }
}

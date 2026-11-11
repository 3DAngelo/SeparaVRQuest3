// Trailer capture tooling (not part of the game). Plays a scripted "fake player" in NuevoJuego for trailer clips:
// a smoothly moving head camera and two fake tracked hands (TrailerHandProvider) that grab residuos through the game's
// own hand grab code. Created only by the editor tool (TrailerRecorder) at runtime; nothing in the game references it.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Audio;

namespace SeparaTrailer
{
    public partial class TrailerDirector : MonoBehaviour
    {
        public const string OutDir = "G:/SeparaVRecover/_trailer/cmd";

        public static event Action<string> ShotStarted;
        public static event Action<string> ShotEnded;
        public static event Action Finished;

        /// <summary>Which shots to play: "all", "dump", or a comma separated list of shot names / group prefixes.</summary>
        public static string Group = "all";

        private readonly StringBuilder log = new StringBuilder();

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(OutDir + "/director.txt", DateTime.Now.ToString("HH:mm:ss") + " f" + Time.frameCount + " t" + Time.time.ToString("F2") + " " + msg + "\n");
            }
            catch (Exception)
            {
            }
        }

        private IEnumerator Start()
        {
            Log("director start group=" + Group);
            yield return null;
            if (Group == "dump")
            {
                yield return new WaitForSeconds(3f);
                Dump();
                Finish();
                yield break;
            }
            yield return RunShots();
            Finish();
        }

        private void Finish()
        {
            Log("finished");
            Finished?.Invoke();
        }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }

        private static string V(Vector3 v)
        {
            return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";
        }

        private void DumpTree(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            var comps = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform)
                {
                    continue;
                }
                string n = c.GetType().Name;
                if (c is Behaviour b && !b.enabled)
                {
                    n += "(off)";
                }
                comps.Add(n);
            }
            sb.Append(new string(' ', depth * 2)).Append(t.gameObject.activeInHierarchy ? "" : "[x] ").Append(t.name)
                .Append(" lp=").Append(V(t.localPosition)).Append(" wp=").Append(V(t.position)).Append(" fwd=").Append(V(t.forward))
                .Append(" {").Append(string.Join(",", comps)).Append("}\n");
            if (depth < maxDepth)
            {
                foreach (Transform child in t)
                {
                    DumpTree(sb, child, depth + 1, maxDepth);
                }
            }
        }

        private void Dump()
        {
            var sb = new StringBuilder();
            XROrigin origin = FindFirstObjectByType<XROrigin>();
            sb.Append("== XR Origin\n");
            if (origin != null)
            {
                DumpTree(sb, origin.transform, 0, 8);
            }
            sb.Append("== Roots\n");
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                DumpTree(sb, root.transform, 0, root.name.Contains("Origin") ? 0 : 1);
            }
            sb.Append("== Canvases\n");
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                sb.Append(PathOf(c.transform)).Append(" mode=").Append(c.renderMode).Append(" active=").Append(c.gameObject.activeInHierarchy)
                    .Append(" wp=").Append(V(c.transform.position)).Append(" scale=").Append(V(c.transform.lossyScale)).Append("\n");
                foreach (UnityEngine.UI.Text text in c.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    sb.Append("   text ").Append(text.name).Append(": ").Append(text.text.Replace("\n", " | ")).Append("\n");
                }
            }
            sb.Append("== Containers\n");
            foreach (Container c in FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                sb.Append(PathOf(c.transform)).Append(" id=").Append(c.IDcontainer).Append(" desc=").Append(c.descripcion).Append(" wp=").Append(V(c.transform.position))
                    .Append(" aim=").Append(V(c.AimPoint)).Append("\n");
            }
            sb.Append("== Residuos\n");
            foreach (Residuo r in FindObjectsByType<Residuo>(FindObjectsSortMode.None))
            {
                Renderer rend = r.GetComponentInChildren<Renderer>();
                sb.Append(PathOf(r.transform)).Append(" id=").Append(r.IDcontenedor).Append(" wp=").Append(V(r.transform.position))
                    .Append(" size=").Append(rend != null ? V(rend.bounds.size) : "-").Append("\n");
            }
            sb.Append("== Audio\n");
            foreach (AudioSource a in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                sb.Append(PathOf(a.transform)).Append(" clip=").Append(a.clip != null ? a.clip.name : "-").Append(" group=")
                    .Append(a.outputAudioMixerGroup != null ? a.outputAudioMixerGroup.name : "-").Append(" playing=").Append(a.isPlaying).Append(" vol=").Append(a.volume).Append("\n");
            }
            sb.Append("== Evolve\n");
            if (GameManager.HasInstance)
            {
                GameManager gm = GameManager.Instance;
                sb.Append("evolveScores=").Append(string.Join(",", gm.evolveScores)).Append(" env=").Append(gm.enviroment != null ? PathOf(gm.enviroment) : "-").Append("\n");
                if (gm.enviroment != null)
                {
                    foreach (IEvolve e in gm.enviroment.GetComponentsInChildren<IEvolve>())
                    {
                        sb.Append("  ").Append(e.GetType().Name).Append(" @ ").Append(PathOf(e.transform)).Append("\n");
                    }
                    Bounds b = new Bounds(gm.enviroment.position, Vector3.zero);
                    foreach (Renderer r in gm.enviroment.GetComponentsInChildren<Renderer>())
                    {
                        b.Encapsulate(r.bounds);
                    }
                    sb.Append("env bounds center=").Append(V(b.center)).Append(" size=").Append(V(b.size)).Append("\n");
                }
            }
            Camera cam = Camera.main;
            if (cam != null)
            {
                sb.Append("== Camera ").Append(PathOf(cam.transform)).Append(" wp=").Append(V(cam.transform.position)).Append(" fwd=").Append(V(cam.transform.forward))
                    .Append(" fov=").Append(cam.fieldOfView).Append(" near=").Append(cam.nearClipPlane).Append(" far=").Append(cam.farClipPlane).Append("\n");
            }
            sb.Append("== Subsystems hands\n");
            var hs = new List<UnityEngine.XR.Hands.XRHandSubsystem>();
            SubsystemManager.GetSubsystems(hs);
            foreach (var h in hs)
            {
                sb.Append(h.GetType().Name).Append(" running=").Append(h.running).Append("\n");
            }
            File.WriteAllText(OutDir + "/dump.txt", sb.ToString());
            Log("dump written");
        }
    }
}

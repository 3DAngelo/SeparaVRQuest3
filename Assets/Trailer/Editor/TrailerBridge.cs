// Trailer capture tooling (not part of the game). File-based command channel so an external agent can drive the
// open Editor without extra packages: drop a *.cmd file in G:/SeparaVRecover/_trailer/cmd and it is executed.
//   refresh                      -> AssetDatabase.Refresh (recompile)
//   exec Full.Type.Name.Method [arg] -> invoke a static method (no args or one string arg)
//   play / stop                  -> enter / exit play mode
// Results are appended to cmd/log.txt; cmd/alive.txt holds a heartbeat with the editor state.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SeparaTrailer.EditorTools
{
    [InitializeOnLoad]
    public static class TrailerBridge
    {
        public const string Root = "G:/SeparaVRecover/_trailer";
        public static readonly string CmdDir = Root + "/cmd";
        private static double nextPoll;
        private static double nextBeat;

        static TrailerBridge()
        {
            Directory.CreateDirectory(CmdDir);
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            Log("bridge loaded (domain reload) playing=" + EditorApplication.isPlaying);
        }

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(CmdDir + "/log.txt", DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\n");
            }
            catch (Exception)
            {
            }
        }

        private static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now > nextBeat)
            {
                nextBeat = now + 2.0;
                try
                {
                    File.WriteAllText(CmdDir + "/alive.txt", DateTime.Now.ToString("HH:mm:ss") + " playing=" + EditorApplication.isPlaying +
                        " compiling=" + EditorApplication.isCompiling + " updating=" + EditorApplication.isUpdating + " frame=" + Time.frameCount);
                }
                catch (Exception)
                {
                }
            }
            if (now < nextPoll)
            {
                return;
            }
            nextPoll = now + 0.5;
            string[] files;
            try
            {
                files = Directory.GetFiles(CmdDir, "*.cmd").OrderBy(f => f).ToArray();
            }
            catch (Exception)
            {
                return;
            }
            foreach (string file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file).Trim();
                    File.Delete(file);
                }
                catch (Exception)
                {
                    continue;
                }
                Log("> " + text);
                try
                {
                    Run(text);
                }
                catch (Exception e)
                {
                    Log("ERROR " + e);
                }
            }
        }

        private static void Run(string text)
        {
            string[] parts = text.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }
            switch (parts[0])
            {
                case "refresh":
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    Log("refreshed");
                    break;
                case "play":
                    EditorApplication.isPlaying = true;
                    break;
                case "stop":
                    EditorApplication.isPlaying = false;
                    break;
                case "exec":
                    Exec(parts[1], parts.Length > 2 ? parts[2] : null);
                    break;
                default:
                    Log("unknown command " + parts[0]);
                    break;
            }
        }

        private static void Exec(string fullName, string arg)
        {
            int dot = fullName.LastIndexOf('.');
            string typeName = fullName.Substring(0, dot);
            string methodName = fullName.Substring(dot + 1);
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
            if (type == null)
            {
                Log("type not found " + typeName);
                return;
            }
            MethodInfo method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == (arg == null ? 0 : 1));
            if (method == null)
            {
                Log("method not found " + fullName);
                return;
            }
            object result = method.Invoke(null, arg == null ? null : new object[] { arg });
            Log("ok " + fullName + (result != null ? " -> " + result : ""));
        }
    }
}

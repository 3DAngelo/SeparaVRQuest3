// Trailer capture tooling (not part of the game). Opens NuevoJuego, enters Play mode with XR start-up disabled (in
// memory only, restored on exit and never saved), adds a TrailerDirector and records one MP4 per shot with the Unity
// Recorder (Game view, 1920x1080, 30 fps constant, H.264, with audio).
// Entry points: menu Tools/Separa Trailer/..., or TrailerRecorder.Run("<group>") through the TrailerBridge / -executeMethod.
using System;
using System.IO;
using SeparaTrailer;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;

namespace SeparaTrailer.EditorTools
{
    [InitializeOnLoad]
    public static class TrailerRecorder
    {
        public const string ScenePath = "Assets/Scenes/NuevoJuego.unity";
        public const string ClipsDir = TrailerBridge.Root + "/clips";
        private const string KeyGroup = "SeparaTrailer.Group";
        private const string KeyXr = "SeparaTrailer.XrInitOriginal";
        private const string KeyRecord = "SeparaTrailer.Record";

        private static RecorderController controller;
        private static string currentShot;

        static TrailerRecorder()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // After the domain reload that follows entering Play mode, the director is created here.
            if (EditorApplication.isPlayingOrWillChangePlaymode && !string.IsNullOrEmpty(SessionState.GetString(KeyGroup, "")))
            {
                EditorApplication.delayCall += AttachDirector;
            }
        }

        [MenuItem("Tools/Separa Trailer/Record all")]
        public static void RecordAll()
        {
            Run("all");
        }

        [MenuItem("Tools/Separa Trailer/Dump scene info (no recording)")]
        public static void DumpOnly()
        {
            Run("dump", false);
        }

        public static void Run(string group)
        {
            Run(group, true);
        }

        /// <summary>Same as Run but without recording (the director just plays the shots).</summary>
        public static void Preview(string group)
        {
            Run(group, false);
        }

        public static void Run(string group, bool record)
        {
            if (EditorApplication.isPlaying)
            {
                TrailerBridge.Log("already playing");
                return;
            }
            Directory.CreateDirectory(ClipsDir);
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                // Never save the game scenes: discard instead of prompting.
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            XRGeneralSettings xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (xr != null)
            {
                if (!SessionState.GetBool(KeyXr + ".set", false))
                {
                    SessionState.SetBool(KeyXr, xr.InitManagerOnStart);
                    SessionState.SetBool(KeyXr + ".set", true);
                }
                xr.InitManagerOnStart = false;
            }
            SessionState.SetString(KeyGroup, group);
            SessionState.SetBool(KeyRecord, record);
            TrailerBridge.Log("run group=" + group + " record=" + record);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                AttachDirector();
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                StopShot();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.EraseString(KeyGroup);
                RestoreXr();
            }
        }

        private static void RestoreXr()
        {
            if (!SessionState.GetBool(KeyXr + ".set", false))
            {
                return;
            }
            XRGeneralSettings xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (xr != null)
            {
                xr.InitManagerOnStart = SessionState.GetBool(KeyXr, true);
            }
            SessionState.EraseBool(KeyXr + ".set");
            TrailerBridge.Log("xr init restored to " + (xr != null && xr.InitManagerOnStart));
        }

        private static void AttachDirector()
        {
            string group = SessionState.GetString(KeyGroup, "");
            if (!EditorApplication.isPlaying || string.IsNullOrEmpty(group) || UnityEngine.Object.FindFirstObjectByType<TrailerDirector>() != null)
            {
                return;
            }
            TrailerDirector.ShotStarted -= StartShot;
            TrailerDirector.ShotEnded -= EndShot;
            TrailerDirector.Finished -= OnFinished;
            if (SessionState.GetBool(KeyRecord, true))
            {
                TrailerDirector.ShotStarted += StartShot;
                TrailerDirector.ShotEnded += EndShot;
            }
            TrailerDirector.Finished += OnFinished;
            TrailerDirector.Group = group;
            new GameObject("TrailerDirector").AddComponent<TrailerDirector>();
            TrailerBridge.Log("director attached group=" + group);
        }

        private static void StartShot(string name)
        {
            StopShot();
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = name;
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movie.CaptureAudio = true;
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
            movie.OutputFile = ClipsDir + "/" + name;
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRatePlayback = FrameRatePlayback.Constant;
            settings.FrameRate = 30f;
            settings.CapFrameRate = true;
            controller = new RecorderController(settings);
            controller.PrepareRecording();
            bool ok = controller.StartRecording();
            currentShot = name;
            TrailerBridge.Log("rec start " + name + " ok=" + ok + " frame=" + Time.frameCount);
        }

        private static void EndShot(string name)
        {
            if (currentShot == name)
            {
                StopShot();
            }
        }

        private static void StopShot()
        {
            if (controller != null)
            {
                if (controller.IsRecording())
                {
                    controller.StopRecording();
                }
                TrailerBridge.Log("rec stop " + currentShot + " frame=" + Time.frameCount);
                controller = null;
                currentShot = null;
            }
        }

        private static void OnFinished()
        {
            StopShot();
            TrailerBridge.Log("director finished");
            EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
        }
    }
}

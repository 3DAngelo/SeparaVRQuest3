// Trailer capture tooling (not part of the game). A fake XR Hands provider: it reports two tracked hands whose wrist
// poses and finger curl are set by TrailerDirector, with finger poses blended from the XRI device-simulator hand
// expression captures (copied to Assets/Trailer/Resources as text). Because the hands look "really tracked", the game's
// own HandVisualizer, XRInputModalityManager, HandCloseGrab, HandHoldPose and HandBeamFX all run unchanged.
// It is only created when TrailerHandProvider.Install() is called (by the trailer director), never in the game.
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.ProviderImplementation;

namespace SeparaTrailer
{
    public class TrailerHandSubsystem : XRHandSubsystem
    {
        private XRHandProviderUtility.SubsystemUpdater updater;

        protected override void OnCreate()
        {
            base.OnCreate();
            updater = new XRHandProviderUtility.SubsystemUpdater(this);
        }

        protected override void OnStart()
        {
            base.OnStart();
            updater.Start();
        }

        protected override void OnStop()
        {
            updater.Stop();
            base.OnStop();
        }

        protected override void OnDestroy()
        {
            updater.Destroy();
            updater = null;
            base.OnDestroy();
        }
    }

    public class TrailerHandProvider : XRHandSubsystemProvider
    {
        public const string Id = "Separa-Trailer-Hands";
        public const int JointCount = 26;

        /// <summary>Desired wrist pose in tracking space, curl 0 (open) .. 1 (fist), tracked flag. Index 0 left, 1 right.</summary>
        public static readonly Pose[] WristPose = { Pose.identity, Pose.identity };
        public static readonly float[] Curl = { 0f, 0f };
        public static readonly bool[] Tracked = { false, false };

        public static TrailerHandSubsystem Instance { get; private set; }

        // Joint poses relative to the wrist, already rotated into the "hand frame" (+Z along the fingers, +Y back of the
        // hand), per capture and hand.
        private static Pose[][] open, grab, fist;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            Instance = null;
            XRHandSubsystemDescriptor.Register(new XRHandSubsystemDescriptor.Cinfo
            {
                id = Id,
                providerType = typeof(TrailerHandProvider),
                subsystemTypeOverride = typeof(TrailerHandSubsystem),
            });
        }

        public static bool Install()
        {
            if (Instance != null)
            {
                return true;
            }
            if (!LoadCaptures())
            {
                Debug.LogError("TrailerHandProvider: hand captures missing in Resources");
                return false;
            }
            var descriptors = new List<XRHandSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(descriptors);
            foreach (XRHandSubsystemDescriptor descriptor in descriptors)
            {
                if (descriptor.id == Id)
                {
                    Instance = descriptor.Create() as TrailerHandSubsystem;
                    break;
                }
            }
            if (Instance == null)
            {
                Debug.LogError("TrailerHandProvider: could not create the subsystem");
                return false;
            }
            Instance.Start();
            return true;
        }

        public static void Uninstall()
        {
            if (Instance != null)
            {
                Instance.Stop();
                Instance.Destroy();
                Instance = null;
            }
        }

        // ---------------------------------------------------------------- captures

        private static bool LoadCaptures()
        {
            if (open != null)
            {
                return true;
            }
            Pose[][] o = Parse("TrailerHand_Open");
            Pose[][] g = Parse("TrailerHand_Grab");
            Pose[][] f = Parse("TrailerHand_Fist");
            if (o == null || g == null || f == null)
            {
                return false;
            }
            open = o;
            grab = g;
            fist = f;
            return true;
        }

        private static readonly Regex PoseRegex = new Regex(
            @"position: \{x: ([-\deE.]+), y: ([-\deE.]+), z: ([-\deE.]+)\}\s*rotation: \{x: ([-\deE.]+), y: ([-\deE.]+), z: ([-\deE.]+), w: ([-\deE.]+)\}");

        private static float F(Match m, int i)
        {
            return float.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        }

        private static Pose[][] Parse(string resource)
        {
            TextAsset asset = Resources.Load<TextAsset>(resource);
            if (asset == null)
            {
                return null;
            }
            string[] halves = asset.text.Split(new[] { "m_RightCapturedPoses:" }, System.StringSplitOptions.None);
            if (halves.Length != 2)
            {
                return null;
            }
            var result = new Pose[2][];
            for (int h = 0; h < 2; h++)
            {
                MatchCollection matches = PoseRegex.Matches(halves[h]);
                if (matches.Count < JointCount)
                {
                    return null;
                }
                var raw = new Pose[JointCount];
                for (int i = 0; i < JointCount; i++)
                {
                    Match m = matches[i];
                    raw[i] = new Pose(new Vector3(F(m, 1), F(m, 2), F(m, 3)), new Quaternion(F(m, 4), F(m, 5), F(m, 6), F(m, 7)).normalized);
                }
                result[h] = ToHandFrame(raw, h == 0);
            }
            return result;
        }

        // Re-expresses the captured joints relative to a hand frame built from the joints themselves, so the director can
        // simply say "fingers this way, back of the hand that way".
        private static Pose[] ToHandFrame(Pose[] raw, bool left)
        {
            Vector3 wrist = raw[0].position;
            Vector3 fwd = (raw[12].position - wrist).normalized;            // middle proximal
            Vector3 side = raw[7].position - raw[22].position;              // index proximal - little proximal
            Vector3 up = Vector3.Cross(fwd, side).normalized * (left ? 1f : -1f);
            Quaternion frame = Quaternion.LookRotation(fwd, up);
            Quaternion inv = Quaternion.Inverse(frame);
            var rel = new Pose[JointCount];
            for (int i = 0; i < JointCount; i++)
            {
                rel[i] = new Pose(inv * (raw[i].position - wrist), inv * raw[i].rotation);
            }
            return rel;
        }

        // ---------------------------------------------------------------- provider

        public override void Start()
        {
        }

        public override void Stop()
        {
        }

        public override void Destroy()
        {
        }

        public override void GetHandLayout(NativeArray<bool> handJointsInLayout)
        {
            for (int i = 0; i < handJointsInLayout.Length; i++)
            {
                handJointsInLayout[i] = true;
            }
        }

        public override XRHandSubsystem.UpdateSuccessFlags TryUpdateHands(XRHandSubsystem.UpdateType updateType,
            ref Pose leftHandRootPose, NativeArray<XRHandJoint> leftHandJoints, ref Pose rightHandRootPose, NativeArray<XRHandJoint> rightHandJoints)
        {
            var flags = XRHandSubsystem.UpdateSuccessFlags.None;
            if (open == null)
            {
                return flags;
            }
            if (Tracked[0])
            {
                leftHandRootPose = WristPose[0];
                Fill(Handedness.Left, 0, leftHandJoints);
                flags |= XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose | XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints;
            }
            if (Tracked[1])
            {
                rightHandRootPose = WristPose[1];
                Fill(Handedness.Right, 1, rightHandJoints);
                flags |= XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose | XRHandSubsystem.UpdateSuccessFlags.RightHandJoints;
            }
            return flags;
        }

        private static void Fill(Handedness handedness, int h, NativeArray<XRHandJoint> joints)
        {
            float c = Mathf.Clamp01(Curl[h]);
            Pose[] a, b;
            float t;
            if (c <= 0.6f)
            {
                a = open[h];
                b = grab[h];
                t = c / 0.6f;
            }
            else
            {
                a = grab[h];
                b = fist[h];
                t = (c - 0.6f) / 0.4f;
            }
            Pose root = WristPose[h];
            int count = Mathf.Min(joints.Length, JointCount);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Vector3.Lerp(a[i].position, b[i].position, t);
                Quaternion r = Quaternion.Slerp(a[i].rotation, b[i].rotation, t);
                var pose = new Pose(root.position + root.rotation * p, root.rotation * r);
                joints[i] = XRHandProviderUtility.CreateJoint(handedness, XRHandJointTrackingState.Pose, XRHandJointIDUtility.FromIndex(i), pose);
            }
        }
    }
}

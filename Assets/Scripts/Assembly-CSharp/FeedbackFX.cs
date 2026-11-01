using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Particle feedback, all built in code at run time (no prefabs, no scene set-up):
///  - Hand beam: a curved "tractor beam" of lights flowing toward the hand: crisp over the sidewalk, a vortex on a target (see HandBeamFX).
///  - Sparkles: little sparks and lights rising about 20 cm from the pointed / held object (see CustomGrabInteractable); the win scene's hands use them too (see HandBeamFX).
///  - Fireworks: a burst in the container's colour when an object lands in the right container (GameManager /
///    TutorialIntro), and a small grey puff when an object is released over the wrong one.
///
/// Quest 3 budget: every system uses the same additive unlit shader (Assets/Resources/FxAdditive.shader, single pass
/// instanced ready) in two materials (shared particles, brighter sparkles) with small procedural
/// textures; no lights, no trails, no collision, no noise module; particle counts are capped (see the Max constants). Nothing here allocates per frame: systems are created once
/// and then only played / stopped / moved.
/// </summary>
public static class FeedbackFX
{
	// ---------------------------------------------------------------- shared look

	// Assets/Resources/FxAdditive.shader
	public const string ShaderResource = "FxAdditive";
	// Procedural glow atlas: two square tiles side by side, 0 = soft round dot, 1 = little four-pointed star.
	public const int AtlasTile = 64;
	// Overall brightness multiplier of the additive material.
	public const float Intensity = 1.5f;

	// ---------------------------------------------------------------- hand beam (HandBeamFX)
	// Particles only: a stream of little lights flowing FROM the tip TOWARD the hand. When it shows and where its tip is
	// are decided by HandBeamFX. Accessibility first (older players): it must answer the aim at once, and the tip must say
	// what is really being pointed at, so there is no build-up and no fade-in. Two looks:
	//  - Sidewalk (no target): a thin, crisp stream exactly on the curve, whole on its very first frame.
	//  - Target (a residuo is pointed at, or flying to the hand): the same stream grows into a wider, denser, golden
	//    three-strand vortex in BeamTargetBlendIn, so "I have a target" can be told apart at a glance.
	// No lights are emitted or aged: every frame BeamMotion's flow distance gives each one its place along the curve, so
	// the whole length is covered on any frame, whatever the length, and a change of length never makes them jump.

	// Shape: a quadratic Bezier from the hand to the end point (like the stock XRI line), whose control point is
	// origin + aim direction * length * BeamCurveRatio, with the sideways part of that direction mirrored across the straight
	// line hand - tip: the tip runs a little behind the aim, so moving the hand right would bow the beam right; mirrored, it
	// bows left, trailing the movement...
	public const float BeamCurveRatio = 0.5f;
	// ...lifted this fraction of the length, so the beam arcs gently UP between hand and tip (both ends stay exactly where
	// they are). Small, so it never reads as aiming at the sky.
	public const float BeamLift = 0.05f;
	// The belly (control point) follows the hand with this sharpness (1/s; 14 = about 70 ms): a little softness while the
	// hand moves. The tip itself has no lag at all: it is always the real end point.
	public const float BeamBellySharpness = 14f;
	// Samples along the curve used to measure its length.
	public const int BeamCurvePoints = 20;
	// Speed of the lights flowing from the tip to the hand (m/s along the curve).
	public const float BeamSpeed = 2.2f;
	// Speed of the lights in the target look (m/s): slow enough to follow them along the spiral. At 72 fps they move about
	// 1.1 cm per frame against 3.75 cm between lights of one strand, so they read as moving instead of flickering.
	public const float BeamTargetSpeed = 0.8f;
	// In the target look a light takes at least this long (s) from tip to hand, also on a short beam.
	public const float BeamTargetMinTravelTime = 0.5f;
	// The stream starts this far in front of the aim origin, so it does not cover the hand itself.
	public const float BeamStartOffset = 0.06f;
	// Longest beam (m); HandBeamFX does not show it toward anything farther.
	public const float BeamMaxLength = 5f;
	// Shorter than this (the residuo has arrived at the hand) nothing is drawn.
	public const float BeamMinLength = 0.1f;
	// Distance between lights along the curve (m), and how far each may stray from its slot (fraction of that distance).
	public const float BeamSpacing = 0.0125f;
	public const float BeamSpacingJitter = 0.5f;
	// One light in this many also exists in the sidewalk look, the rest only in the target look (sidewalk: 27 lights per metre, target: 80).
	public const int BeamSidewalkEvery = 3;
	// Lights per hand. They run on a closed loop of BeamMaxParticles * BeamSpacing = 5.5 m, so a beam 5 m long (slightly
	// more along its bow) is always covered. Drawn: about 80 (sidewalk) or 240 (target) on a usual 3 m beam.
	public const int BeamMaxParticles = 440;

	// Sidewalk look: size (m), colour, and how much of the streak it keeps (fraction: short, so the lights stay distinct).
	public const float BeamSidewalkSize = 0.02f;
	public static readonly Color BeamSidewalkColor = new Color(0.8f, 0.97f, 1f, 1f);
	public const float BeamSidewalkStreak = 0.5f;

	// Target look: size range (m) and colours (pale sky blue to warm gold; alpha = strength of the additive light).
	public const float BeamSizeMin = 0.02f;
	public const float BeamSizeMax = 0.038f;
	public static readonly Color BeamColorA = new Color(0.55f, 0.85f, 1f, 0.75f);
	public static readonly Color BeamColorB = new Color(1f, 0.85f, 0.5f, 0.75f);
	// Seconds from sidewalk look to target look when a target appears, and back when it is lost. The spiral emerges from
	// the central stream, already formed along its whole length, in this short time.
	public const float BeamTargetBlendIn = 0.1f;
	public const float BeamTargetBlendOut = 0.12f;
	// Vortex: the lights spiral around the curve on BeamSpiralStrands interleaved helices (evenly spaced phases plus a
	// little per-light jitter), so it reads as a swirling vortex, not a single corkscrew.
	public const int BeamSpiralStrands = 3;
	// Per-light phase jitter around its strand (radians).
	public const float BeamSpiralPhaseJitter = 0.6f;
	// Spiral radius (m) at the tip and at the hand (it tightens as the lights are pulled in); each light varies it by
	// +/- BeamSpiralRadiusJitter (fraction).
	public const float BeamSpiralRadiusTip = 0.018f;
	public const float BeamSpiralRadiusHand = 0.005f;
	public const float BeamSpiralRadiusJitter = 0.3f;
	// Turns per metre travelled (the pitch of the helices), plus a slow extra spin with time (rad/s).
	public const float BeamSpiralTurnsPerMeter = 3f;
	public const float BeamSpiralSpin = 4f;

	// Each light is a short streak along the flow: stretched by its velocity times this (seconds).
	public const float BeamStreak = 0.012f;
	// Fade of a light along its way: in over this many metres at the tip (just enough that it does not pop; the tip stays
	// bright and exact), out from BeamFlowFadeOutStart of the length before it reaches the hand. And its size at the hand
	// relative to the tip (they shrink a little while being pulled in).
	public const float BeamTipFade = 0.03f;
	public const float BeamFlowFadeOutStart = 0.93f;
	public const float BeamFlowSizeAtHand = 0.6f;
	// How fast the beam fades OUT when hidden (1/s; 6 = about 0.17 s), and the quicker fade when the residuo arrives at the
	// hand. It has no fade in: it is whole and full the first frame it is shown.
	public const float BeamFadeSpeed = 6f;
	public const float BeamArriveFadeSpeed = 12f;
	// Replace the stock XRI ray line (CurveVisualController + LineRenderer on the Near-Far Interactor) with this beam, so
	// there are not two lines on the same ray. It is restored if the beam cannot be made (no shader) or is removed.
	public const bool BeamReplacesStockLine = true;

	// ---------------------------------------------------------------- sparkles over the pointed / held object

	// Particles per second while pointed at, and (fewer, it is right in front of the face) while held.
	public const float SparkleRateHovered = 36f;
	public const float SparkleRateHeld = 14f;
	// Upward speed (m/s) and lifetime (s): from the top half of the object they rise roughly 0.18 .. 0.35 m, ~20 cm above it.
	public const float SparkleRiseMin = 0.22f;
	public const float SparkleRiseMax = 0.32f;
	public const float SparkleLifeMin = 0.8f;
	public const float SparkleLifeMax = 1.1f;
	// Small sideways drift (m/s), so they do not climb in straight lines.
	public const float SparkleDrift = 0.035f;
	public const float SparkleSizeMin = 0.028f;
	public const float SparkleSizeMax = 0.055f;
	// The sparkles use a brighter copy of the shared material (same shader and atlas, this intensity instead of Intensity).
	public const float SparkleIntensity = 2.1f;
	// Emission volume: an ellipsoid over the top half of the object (its bounds times this), never under SparkleMinRadius.
	public const float SparkleVolumeScale = 1.1f;
	public const float SparkleMinRadius = 0.03f;
	public const int SparkleMaxParticles = 64;
	// Radius (m) of the volume around the palm for the win scene's hand sparkles.
	public const float HandSparkleRadius = 0.06f;
	// Gold, white, light cyan and pink, picked at random per particle.
	public static readonly Color SparkleGold = new Color(1f, 0.82f, 0.35f, 1f);
	public static readonly Color SparkleWhite = new Color(1f, 1f, 0.95f, 1f);
	public static readonly Color SparkleCyan = new Color(0.5f, 0.9f, 1f, 1f);
	public static readonly Color SparklePink = new Color(1f, 0.55f, 0.85f, 1f);

	// ---------------------------------------------------------------- fireworks over the container (right deposit)

	// Three shells: offset from the top centre of the container (x = to the right as seen by the player, y = up, z =
	// toward the player), delay (s), and whether the shell uses the lighter tint of the colour.
	private static readonly Vector3[] ShellOffsets = { new Vector3(0f, 0.6f, 0f), new Vector3(-0.6f, 1.05f, 0.1f), new Vector3(0.55f, 0.95f, -0.1f) };
	private static readonly float[] ShellDelays = { 0f, 0.22f, 0.42f };
	private static readonly bool[] ShellLight = { false, true, false };
	// Sparks per shell, initial speed (m/s), lifetime (s), size (m).
	public const int ShellSparks = 48;
	public const float ShellSpeedMin = 1.8f;
	public const float ShellSpeedMax = 2.5f;
	public const float ShellLifeMin = 0.9f;
	public const float ShellLifeMax = 1.3f;
	public const float ShellSizeMin = 0.07f;
	public const float ShellSizeMax = 0.12f;
	// Air drag (1/s) and gravity factor: the sparks open fast, slow down and droop a little. Radius ~ speed / drag.
	public const float ShellDrag = 2.2f;
	public const float ShellGravity = 0.35f;
	// Streaks: stretched along the velocity by this factor (seconds), so they read as fireworks and shrink to dots.
	public const float ShellStreak = 0.07f;
	// Bright puff at the centre of each shell as it bursts.
	public const float FlashSize = 0.8f;
	public const float FlashLife = 0.22f;
	// Glitter that twinkles and falls slowly after the shells.
	public const int GlitterCount = 30;
	public const float GlitterDelay = 0.30f;
	public const float GlitterRadius = 0.6f;
	public const float GlitterLifeMin = 1.0f;
	public const float GlitterLifeMax = 1.6f;
	public const float GlitterSizeMin = 0.08f;
	public const float GlitterSizeMax = 0.13f;
	// How far the lighter tint moves the container colour toward white, and the glitter toward warm white.
	public const float LightTint = 0.45f;
	public const float GlitterTint = 0.6f;
	public static readonly Color GlitterWarmWhite = new Color(1f, 0.95f, 0.75f, 1f);
	// Fireworks that can be in the air at once (pooled); more requests while all are busy are skipped.
	public const int MaxFireworks = 4;

	// ---------------------------------------------------------------- wrong deposit: small subdued puff

	public const int PuffCount = 12;
	public static readonly Color PuffColor = new Color(0.55f, 0.6f, 0.7f, 0.45f);
	public const float PuffSpeedMin = 0.12f;
	public const float PuffSpeedMax = 0.3f;
	public const float PuffLifeMin = 0.45f;
	public const float PuffLifeMax = 0.75f;
	public const float PuffSizeMin = 0.035f;
	public const float PuffSizeMax = 0.07f;

	// ---------------------------------------------------------------- state

	private static Material material;
	private static Material sparkleMaterial;
	private static Texture2D atlas;
	private static bool materialFailed;

	private struct FireworksEntry
	{
		public ParticleSystem Root;
		public Color Color;
	}

	private static readonly List<FireworksEntry> fireworks = new List<FireworksEntry>();
	private static ParticleSystem puff;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		material = null;
		sparkleMaterial = null;
		atlas = null;
		materialFailed = false;
		fireworks.Clear();
		puff = null;
	}

	/// <summary>The shared additive material (null if the shader is missing / unsupported: then there are no effects).</summary>
	public static Material Material
	{
		get
		{
			if (material == null && !materialFailed)
			{
				// The shader lives in Assets/Resources so that it is always included in the build.
				Shader shader = Resources.Load<Shader>(ShaderResource);
				if (shader == null || !shader.isSupported)
				{
					materialFailed = true;
					#if UNITY_EDITOR
					Debug.LogWarning("FeedbackFX: shader 'Resources/" + ShaderResource + "' not found or not supported; no particle feedback.");
					#endif
					return null;
				}
				if (atlas == null)
				{
					atlas = CreateAtlas();
				}
				material = new Material(shader);
				material.name = "FeedbackFX additive";
				material.hideFlags = HideFlags.DontSave;
				material.mainTexture = atlas;
				material.SetFloat("_Intensity", Intensity);
			}
			return material;
		}
	}

	/// <summary>Brighter copy of <see cref="Material"/> for the sparkles (null without the shader).</summary>
	private static Material SparkleMaterial
	{
		get
		{
			if (sparkleMaterial == null && Material != null)
			{
				sparkleMaterial = new Material(material);
				sparkleMaterial.name = "FeedbackFX additive (sparkles)";
				sparkleMaterial.hideFlags = HideFlags.DontSave;
				sparkleMaterial.SetFloat("_Intensity", SparkleIntensity);
			}
			return sparkleMaterial;
		}
	}


	// ---------------------------------------------------------------- hand beam

	// Scratch buffer shared by every beam, and each light's fixed random numbers (main thread only; made once).
	private static ParticleSystem.Particle[] beamParticles;
	private static float[] beamRandom;
	// Lifetime given to each light when placed: long, since they are re-placed every frame and never die by age.
	private const float BeamLightLife = 1000f;

	/// <summary>
	/// The moment-to-moment state of one beam, stepped once per frame by its owner (HandBeamFX, and the screenshot harness,
	/// which steps this same logic frame by frame). No allocations.
	/// </summary>
	public sealed class BeamMotion
	{
		/// <summary>0 = hidden .. 1 = full. Jumps to 1 as soon as the beam is shown.</summary>
		public float Visibility { get; private set; }
		/// <summary>0 = sidewalk look .. 1 = target look.</summary>
		public float TargetBlend { get; private set; }
		/// <summary>Current control point of the curve (lags a little behind the hand).</summary>
		public Vector3 Belly;
		/// <summary>False until the first placement after being hidden (the belly then starts exactly where it should).</summary>
		public bool HasBelly;
		/// <summary>Metres the stream has flowed (wraps around).</summary>
		public float Flow { get; private set; }
		/// <summary>Extra rotation of the vortex (radians, wraps around).</summary>
		public float Spin { get; private set; }

		/// <summary>
		/// One frame. <paramref name="show"/>: wanted visible (full at once); otherwise it fades out at
		/// <paramref name="fadeOutSpeed"/>. <paramref name="hasTarget"/>: target look wanted; <paramref name="snapTarget"/>:
		/// at once (a residuo flying to the hand).
		/// </summary>
		public void Step(float dt, bool show, bool hasTarget, bool snapTarget, float fadeOutSpeed)
		{
			Visibility = show ? 1f : Mathf.MoveTowards(Visibility, 0f, fadeOutSpeed * dt);
			if (Visibility <= 0f)
			{
				Reset();
				return;
			}
			float goal = hasTarget ? 1f : 0f;
			float rate = hasTarget ? 1f / BeamTargetBlendIn : 1f / BeamTargetBlendOut;
			TargetBlend = snapTarget ? goal : Mathf.MoveTowards(TargetBlend, goal, rate * dt);
			Spin = Mathf.Repeat(Spin + BeamSpiralSpin * dt, 6.2831853f);
		}

		/// <summary>Moves the stream on by <paramref name="metres"/> along the curve (wraps around).</summary>
		public void AdvanceFlow(float metres)
		{
			Flow = Mathf.Repeat(Flow + metres, BeamMaxParticles * BeamSpacing);
		}

		/// <summary>Hidden, sidewalk look, belly forgotten.</summary>
		public void Reset()
		{
			Visibility = 0f;
			TargetBlend = 0f;
			HasBelly = false;
		}
	}

	/// <summary>
	/// A new (stopped) beam under <paramref name="parent"/>. It emits nothing: its lights live in world space and are all
	/// set, every frame, by <see cref="PlaceBeam"/>. Null without material.
	/// </summary>
	public static ParticleSystem CreateBeam(Transform parent)
	{
		ParticleSystem ps = NewSystem("Hand Beam FX", parent, BeamMaxParticles);
		if (ps == null)
		{
			return null;
		}
		ParticleSystem.MainModule main = ps.main;
		main.loop = true;
		main.duration = 1f;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.startSpeed = 0f;
		main.startLifetime = BeamLightLife;
		main.startSize = BeamSidewalkSize;
		main.startColor = BeamSidewalkColor;

		ParticleSystem.EmissionModule emission = ps.emission;
		emission.enabled = false;

		ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
		r.renderMode = ParticleSystemRenderMode.Stretch;
		r.velocityScale = BeamStreak;
		r.lengthScale = 1f;
		SetTile(ps, false);
		return ps;
	}

	/// <summary>Point of the quadratic Bezier p0 - p1 - p2 at t (0 = p0, 1 = p2).</summary>
	public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
	{
		float s = 1f - t;
		return s * s * p0 + 2f * s * t * p1 + t * t * p2;
	}

	/// <summary>Speed of the lights (m/s) for a look (0 = sidewalk .. 1 = target) on a curve of this length.</summary>
	public static float BeamFlowSpeed(float targetBlend, float length)
	{
		float targetSpeed = Mathf.Min(BeamTargetSpeed, length / BeamTargetMinTravelTime);
		return Mathf.Lerp(BeamSpeed, targetSpeed, Mathf.SmoothStep(0f, 1f, targetBlend));
	}

	/// <summary>
	/// The beam's control points: from BeamStartOffset in front of <paramref name="origin"/> (the hand's aim origin) to
	/// <paramref name="end"/>: control point along <paramref name="aimDirection"/> at BeamCurveRatio of the length, mirrored
	/// across the straight line p0 - p2 (the bow trails the hand), lifted up by BeamLift (a soft upward bow). Returns the
	/// straight distance p0 - p2.
	/// </summary>
	public static float BeamControlPoints(Vector3 origin, Vector3 aimDirection, Vector3 end, out Vector3 p0, out Vector3 p1)
	{
		Vector3 dir = aimDirection.sqrMagnitude > 1e-6f ? aimDirection.normalized : Vector3.forward;
		p0 = origin + dir * BeamStartOffset;
		float chord = Vector3.Distance(p0, end);
		Vector3 along = dir * (chord * BeamCurveRatio);
		if (chord > 1e-4f)
		{
			Vector3 chordDir = (end - p0) / chord;
			along = 2f * Vector3.Dot(along, chordDir) * chordDir - along;   // mirrored across the straight line
		}
		p1 = p0 + along + Vector3.up * (chord * BeamLift);
		return chord;
	}

	/// <summary>
	/// Puts a beam on its curve for this frame, as <paramref name="motion"/> says: its tip exactly on
	/// <paramref name="end"/>, its whole length covered with lights flowing from the tip to the hand. Used both by HandBeamFX
	/// and by the screenshot harness. The system must be playing (or simulated by hand); no allocations.
	/// </summary>
	public static void PlaceBeam(ParticleSystem beam, BeamMotion motion, Vector3 origin, Vector3 aimDirection, Vector3 end, float dt)
	{
		Vector3 p0;
		Vector3 ideal;
		float chord = BeamControlPoints(origin, aimDirection, end, out p0, out ideal);
		if (chord < BeamMinLength || motion.Visibility <= 0f)
		{
			if (beam.particleCount > 0)
			{
				beam.Clear(true);
			}
			return;
		}
		motion.Belly = motion.HasBelly ? Vector3.Lerp(motion.Belly, ideal, 1f - Mathf.Exp(-BeamBellySharpness * dt)) : ideal;
		motion.HasBelly = true;
		Vector3 p1 = motion.Belly;

		float length = 0f;
		Vector3 previous = p0;
		for (int i = 1; i < BeamCurvePoints; i++)
		{
			Vector3 point = Bezier(p0, p1, end, (float)i / (BeamCurvePoints - 1));
			length += Vector3.Distance(previous, point);
			previous = point;
		}
		float flowSpeed = BeamFlowSpeed(motion.TargetBlend, length);
		motion.AdvanceFlow(flowSpeed * dt);
		FlowBeam(beam, motion, p0, p1, end, length, flowSpeed);
	}

	/// <summary>
	/// Sets every light of the beam: each has a fixed slot on a closed loop, the loop slides by the flow distance, and the
	/// part of the loop shorter than the beam is what gets drawn (0 = at the tip .. length = at the hand). So it is
	/// complete on any frame and shortens with the curve (a residuo flying to the hand) without a jump.
	/// </summary>
	private static void FlowBeam(ParticleSystem beam, BeamMotion motion, Vector3 p0, Vector3 p1, Vector3 p2, float length, float flowSpeed)
	{
		if (beamParticles == null)
		{
			beamParticles = new ParticleSystem.Particle[BeamMaxParticles];   // once
			beamRandom = new float[BeamMaxParticles * 3];
			Random.State saved = Random.state;
			Random.InitState(7321);
			for (int i = 0; i < beamRandom.Length; i++)
			{
				beamRandom[i] = Random.value;
			}
			Random.state = saved;
		}
		float look = Mathf.SmoothStep(0f, 1f, motion.TargetBlend);   // radius, size, colour, streak of the target look
		float visibility = motion.Visibility;
		float loop = BeamMaxParticles * BeamSpacing;
		float invLength = 1f / Mathf.Max(0.01f, length);
		float swirlScale = Mathf.Clamp01(length / 0.5f);   // a very short beam (arriving) does not swirl wider than itself
		float turnRate = BeamSpiralTurnsPerMeter * 6.2831853f;   // radians per metre travelled
		float angularSpeed = flowSpeed * turnRate + BeamSpiralSpin;
		float streakSpeed = flowSpeed * Mathf.Lerp(BeamSidewalkStreak, 1f, look);
		Vector3 chordDir = (p2 - p0).sqrMagnitude > 1e-8f ? (p2 - p0).normalized : Vector3.forward;
		int count = 0;
		for (int i = 0; i < BeamMaxParticles; i++)
		{
			bool targetOnly = i % BeamSidewalkEvery != 0;
			if (targetOnly && look <= 0f)
			{
				continue;
			}
			float h1 = beamRandom[i * 3];
			float h2 = beamRandom[i * 3 + 1];
			float h3 = beamRandom[i * 3 + 2];
			float along = Mathf.Repeat(i * BeamSpacing + motion.Flow + (h3 - 0.5f) * BeamSpacingJitter * BeamSpacing, loop);
			if (along >= length)
			{
				continue;
			}
			float u = along * invLength;   // 0 = at the tip .. 1 = at the hand
			float t = 1f - u;              // Bezier parameter (0 = hand, 1 = tip)
			Vector3 position = Bezier(p0, p1, p2, t);
			Vector3 tangent = 2f * (1f - t) * (p1 - p0) + 2f * t * (p2 - p1);
			tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : chordDir;
			Vector3 velocity = -tangent * streakSpeed;   // toward the hand (also orients the streak)

			if (look > 0f)
			{
				Vector3 a = Vector3.Cross(tangent, Vector3.up);
				if (a.sqrMagnitude < 1e-4f)
				{
					a = Vector3.Cross(tangent, Vector3.right);
				}
				a.Normalize();
				Vector3 b = Vector3.Cross(tangent, a);
				float angle = (i % BeamSpiralStrands) * (6.2831853f / BeamSpiralStrands) + (h2 - 0.5f) * BeamSpiralPhaseJitter
					+ along * turnRate + motion.Spin;
				float radius = Mathf.Lerp(BeamSpiralRadiusTip, BeamSpiralRadiusHand, u) * (1f + (h1 - 0.5f) * 2f * BeamSpiralRadiusJitter) * swirlScale * look;
				float cos = Mathf.Cos(angle);
				float sin = Mathf.Sin(angle);
				position += (a * cos + b * sin) * radius;
				velocity += (b * cos - a * sin) * (radius * angularSpeed);   // around it: each streak follows its own helix
			}

			float fade = Mathf.Clamp01(along / BeamTipFade) * (1f - Mathf.SmoothStep(0f, 1f, (u - BeamFlowFadeOutStart) / (1f - BeamFlowFadeOutStart))) * visibility;
			if (targetOnly)
			{
				fade *= look;
			}
			Color color = Color.Lerp(BeamSidewalkColor, Color.Lerp(BeamColorA, BeamColorB, h2), look);
			color.a *= fade;

			ParticleSystem.Particle p = beamParticles[count];
			p.position = position;
			p.velocity = velocity;
			p.startColor = color;
			p.startSize = Mathf.Lerp(BeamSidewalkSize, Mathf.Lerp(BeamSizeMin, BeamSizeMax, h1), look) * Mathf.Lerp(1f, BeamFlowSizeAtHand, u);
			p.startLifetime = BeamLightLife;
			p.remainingLifetime = BeamLightLife;
			p.randomSeed = (uint)i + 1u;
			beamParticles[count] = p;
			count++;
		}
		beam.SetParticles(beamParticles, count);
	}

	// ---------------------------------------------------------------- sparkles

	/// <summary>A new (stopped) sparkle emitter, not parented (world space; move it with <see cref="FollowSparkles"/>).</summary>
	public static ParticleSystem CreateSparkles(string ownerName)
	{
		ParticleSystem ps = NewSystem("Sparkles FX (" + ownerName + ")", null, SparkleMaxParticles);
		if (ps == null)
		{
			return null;
		}
		ParticleSystem.MainModule main = ps.main;
		main.loop = true;
		main.duration = 1f;
		main.simulationSpace = ParticleSystemSimulationSpace.World;   // they stay behind as the object moves: a little trail
		main.startSpeed = 0f;
		main.startLifetime = new ParticleSystem.MinMaxCurve(SparkleLifeMin, SparkleLifeMax);
		main.startSize = new ParticleSystem.MinMaxCurve(SparkleSizeMin, SparkleSizeMax);
		Gradient palette = new Gradient();
		palette.SetKeys(
			new[] { new GradientColorKey(SparkleGold, 0f), new GradientColorKey(SparkleWhite, 0.3f), new GradientColorKey(SparkleCyan, 0.6f), new GradientColorKey(SparklePink, 1f) },
			new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
		ParticleSystem.MinMaxGradient startColor = new ParticleSystem.MinMaxGradient(palette);
		startColor.mode = ParticleSystemGradientMode.RandomColor;
		main.startColor = startColor;

		ParticleSystem.EmissionModule emission = ps.emission;
		emission.rateOverTime = SparkleRateHovered;

		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = true;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 0.5f;          // scaled to the object's bounds by FollowSparkles
		shape.radiusThickness = 1f;   // whole volume

		ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
		velocity.enabled = true;
		velocity.space = ParticleSystemSimulationSpace.World;
		velocity.x = new ParticleSystem.MinMaxCurve(-SparkleDrift, SparkleDrift);
		velocity.y = new ParticleSystem.MinMaxCurve(SparkleRiseMin, SparkleRiseMax);
		velocity.z = new ParticleSystem.MinMaxCurve(-SparkleDrift, SparkleDrift);

		SetAlphaOverLifetime(ps, 0f, 0.15f, 0.6f, 1f);
		// Twinkle: the size pulses a couple of times before vanishing.
		ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
		size.enabled = true;
		size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
			new Keyframe(0f, 0.2f), new Keyframe(0.12f, 1f), new Keyframe(0.3f, 0.45f), new Keyframe(0.48f, 1f),
			new Keyframe(0.66f, 0.4f), new Keyframe(0.82f, 0.8f), new Keyframe(1f, 0f)));

		SetTile(ps, true);   // dots and little stars
		ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = SparkleMaterial;   // a little brighter than the rest
		return ps;
	}

	/// <summary>Sets the sparkle rate (particles per second).</summary>
	public static void SetSparkleRate(ParticleSystem sparkles, float rate)
	{
		ParticleSystem.EmissionModule emission = sparkles.emission;
		emission.rateOverTime = rate;
	}

	/// <summary>Puts the emitter on the joint bounds of <paramref name="renderers"/> (call every frame while emitting).</summary>
	public static void FollowSparkles(ParticleSystem sparkles, IList<MeshRenderer> renderers)
	{
		bool any = false;
		Bounds bounds = new Bounds();
		for (int i = 0; i < renderers.Count; i++)
		{
			MeshRenderer meshRenderer = renderers[i];
			if (meshRenderer == null || !meshRenderer.enabled || !meshRenderer.gameObject.activeInHierarchy)
			{
				continue;
			}
			if (!any)
			{
				bounds = meshRenderer.bounds;
				any = true;
			}
			else
			{
				bounds.Encapsulate(meshRenderer.bounds);
			}
		}
		if (!any)
		{
			return;
		}
		Transform t = sparkles.transform;
		// Upper half of the object: they start around its top and rise above it (not hidden inside it).
		t.SetPositionAndRotation(bounds.center + Vector3.up * (bounds.extents.y * 0.5f), Quaternion.identity);
		Vector3 s = Vector3.Scale(bounds.size, new Vector3(1f, 0.5f, 1f)) * SparkleVolumeScale;
		float min = 2f * SparkleMinRadius;
		ParticleSystem.ShapeModule shape = sparkles.shape;
		shape.scale = new Vector3(Mathf.Max(s.x, min), Mathf.Max(s.y, min), Mathf.Max(s.z, min));
	}

	/// <summary>A new (stopped) sparkle emitter for a hand: the same look as the object's, from a small volume around the palm. Null without material.</summary>
	public static ParticleSystem CreateHandSparkles(string ownerName)
	{
		ParticleSystem ps = CreateSparkles(ownerName);
		if (ps != null)
		{
			ParticleSystem.ShapeModule shape = ps.shape;
			shape.scale = Vector3.one * (2f * HandSparkleRadius);   // (the shape's radius is 0.5)
		}
		return ps;
	}

	// ---------------------------------------------------------------- fireworks

	/// <summary>Top centre of the container (where the fireworks start), from its colliders or else its renderers.</summary>
	public static Vector3 FireworksPoint(Container container)
	{
		bool any = false;
		Bounds bounds = new Bounds(container.transform.position, Vector3.zero);
		foreach (Collider c in container.colliders)
		{
			if (c == null)
			{
				continue;
			}
			if (!any)
			{
				bounds = c.bounds;
				any = true;
			}
			else
			{
				bounds.Encapsulate(c.bounds);
			}
		}
		if (!any)
		{
			foreach (MeshRenderer r in container.GetComponentsInChildren<MeshRenderer>())
			{
				if (!any)
				{
					bounds = r.bounds;
					any = true;
				}
				else
				{
					bounds.Encapsulate(r.bounds);
				}
			}
		}
		return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
	}

	/// <summary>Fireworks in the container's colour over the container (an object landed in the right container).</summary>
	public static void PlayFireworks(Container container)
	{
		if (container == null)
		{
			return;
		}
		Camera head = Camera.main;
		PlayFireworks(FireworksPoint(container), container.HighlightColor, head != null ? head.transform.position : container.transform.position + container.transform.forward);
	}

	/// <summary>Fireworks of <paramref name="color"/> at <paramref name="position"/>, laid out facing <paramref name="viewer"/>.</summary>
	public static ParticleSystem PlayFireworks(Vector3 position, Color color, Vector3 viewer)
	{
		ParticleSystem root = GetFireworks(color);
		if (root == null)
		{
			return null;
		}
		Vector3 toViewer = viewer - position;
		toViewer.y = 0f;
		Quaternion facing = toViewer.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(-toViewer.normalized, Vector3.up) : Quaternion.identity;
		root.transform.SetPositionAndRotation(position, facing);
		root.Play(true);
		return root;
	}

	/// <summary>A free pooled fireworks of that colour (made on demand), or null when all are busy / no material.</summary>
	private static ParticleSystem GetFireworks(Color color)
	{
		for (int i = fireworks.Count - 1; i >= 0; i--)
		{
			if (fireworks[i].Root == null)
			{
				fireworks.RemoveAt(i);   // destroyed with its scene
			}
		}
		for (int i = 0; i < fireworks.Count; i++)
		{
			if (fireworks[i].Color == color && !fireworks[i].Root.IsAlive(true))
			{
				return fireworks[i].Root;
			}
		}
		if (fireworks.Count >= MaxFireworks)
		{
			return null;
		}
		ParticleSystem root = CreateFireworks(color);
		if (root != null)
		{
			FireworksEntry entry;
			entry.Root = root;
			entry.Color = color;
			fireworks.Add(entry);
		}
		return root;
	}

	/// <summary>
	/// A new (stopped) fireworks: an empty root with three shells (sparks + flash each) and a glitter cloud as children,
	/// each one delayed, so that root.Play(true) / root.Simulate(t, true, true) runs the whole show. +Z of the root points
	/// away from the player (x of ShellOffsets = to the right).
	/// </summary>
	public static ParticleSystem CreateFireworks(Color color)
	{
		ParticleSystem root = NewSystem("Fireworks FX", null, 1);
		if (root == null)
		{
			return null;
		}
		ParticleSystem.MainModule rootMain = root.main;
		rootMain.loop = false;
		rootMain.duration = 2.5f;
		ParticleSystem.EmissionModule rootEmission = root.emission;
		rootEmission.enabled = false;
		root.GetComponent<ParticleSystemRenderer>().enabled = false;

		Color light = Color.Lerp(color, Color.white, LightTint);
		for (int i = 0; i < ShellOffsets.Length; i++)
		{
			Color c = ShellLight[i] ? light : color;
			// ShellOffsets: z toward the player, the root's +Z points away.
			Vector3 offset = new Vector3(-ShellOffsets[i].x, ShellOffsets[i].y, -ShellOffsets[i].z);
			CreateShell(root.transform, offset, ShellDelays[i], c);
			CreateFlash(root.transform, offset, ShellDelays[i], Color.Lerp(c, Color.white, 0.5f));
		}
		CreateGlitter(root.transform, new Vector3(0f, 0.9f, 0f), GlitterDelay, Color.Lerp(color, GlitterWarmWhite, GlitterTint));
		return root;
	}

	private static void CreateShell(Transform parent, Vector3 offset, float delay, Color color)
	{
		ParticleSystem ps = NewSystem("Shell", parent, ShellSparks);
		ps.transform.localPosition = offset;
		ParticleSystem.MainModule main = ps.main;
		main.loop = false;
		main.duration = 1f;
		main.startDelay = delay;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.startSpeed = new ParticleSystem.MinMaxCurve(ShellSpeedMin, ShellSpeedMax);
		main.startLifetime = new ParticleSystem.MinMaxCurve(ShellLifeMin, ShellLifeMax);
		main.startSize = new ParticleSystem.MinMaxCurve(ShellSizeMin, ShellSizeMax);
		main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.45f));
		main.gravityModifier = ShellGravity;
		SetBurst(ps, ShellSparks);

		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = true;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 0.02f;
		shape.radiusThickness = 0f;   // on the surface: all of them fly outward

		ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
		limit.enabled = true;
		limit.limit = 100f;
		limit.drag = ShellDrag;
		limit.multiplyDragByParticleSize = false;
		limit.multiplyDragByParticleVelocity = false;

		SetAlphaOverLifetime(ps, 1f, 0f, 0.55f, 1f);
		ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
		size.enabled = true;
		size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));

		ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
		r.renderMode = ParticleSystemRenderMode.Stretch;
		r.velocityScale = ShellStreak;
		r.lengthScale = 1f;
		SetTile(ps, false);
	}

	private static void CreateFlash(Transform parent, Vector3 offset, float delay, Color color)
	{
		ParticleSystem ps = NewSystem("Flash", parent, 1);
		ps.transform.localPosition = offset;
		ParticleSystem.MainModule main = ps.main;
		main.loop = false;
		main.duration = 0.5f;
		main.startDelay = delay;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.startSpeed = 0f;
		main.startLifetime = FlashLife;
		main.startSize = FlashSize;
		color.a = 0.8f;
		main.startColor = color;
		SetBurst(ps, 1);
		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = false;
		SetAlphaOverLifetime(ps, 1f, 0f, 0.2f, 1f);
		ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
		size.enabled = true;
		size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1f)));
		SetTile(ps, false);
	}

	private static void CreateGlitter(Transform parent, Vector3 offset, float delay, Color color)
	{
		ParticleSystem ps = NewSystem("Glitter", parent, GlitterCount);
		ps.transform.localPosition = offset;
		ParticleSystem.MainModule main = ps.main;
		main.loop = false;
		main.duration = 1f;
		main.startDelay = delay;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
		main.startLifetime = new ParticleSystem.MinMaxCurve(GlitterLifeMin, GlitterLifeMax);
		main.startSize = new ParticleSystem.MinMaxCurve(GlitterSizeMin, GlitterSizeMax);
		main.startColor = new ParticleSystem.MinMaxGradient(color, SparkleWhite);
		main.gravityModifier = 0.06f;
		SetBurst(ps, GlitterCount);
		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = true;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = GlitterRadius;
		shape.radiusThickness = 1f;
		SetAlphaOverLifetime(ps, 0f, 0.1f, 0.65f, 1f);
		ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
		size.enabled = true;
		size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
			new Keyframe(0f, 0.3f), new Keyframe(0.1f, 1f), new Keyframe(0.25f, 0.35f), new Keyframe(0.4f, 1f),
			new Keyframe(0.55f, 0.35f), new Keyframe(0.72f, 0.85f), new Keyframe(1f, 0f)));
		SetTile(ps, true);
	}

	// ---------------------------------------------------------------- wrong deposit

	/// <summary>Small subdued grey puff (no celebration) where an object was released over the wrong container.</summary>
	public static void PlayWrongPuff(Vector3 position)
	{
		if (puff == null)
		{
			puff = CreatePuff();
			if (puff == null)
			{
				return;
			}
		}
		puff.transform.position = position;
		if (!puff.isPlaying)
		{
			puff.Play();
		}
		puff.Emit(PuffCount);
	}

	public static ParticleSystem CreatePuff()
	{
		ParticleSystem ps = NewSystem("Wrong Puff FX", null, PuffCount * 3);
		if (ps == null)
		{
			return null;
		}
		ParticleSystem.MainModule main = ps.main;
		main.loop = true;   // always playing, with no emission of its own: Emit() adds the puffs
		main.duration = 1f;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.startSpeed = new ParticleSystem.MinMaxCurve(PuffSpeedMin, PuffSpeedMax);
		main.startLifetime = new ParticleSystem.MinMaxCurve(PuffLifeMin, PuffLifeMax);
		main.startSize = new ParticleSystem.MinMaxCurve(PuffSizeMin, PuffSizeMax);
		main.startColor = PuffColor;
		main.gravityModifier = -0.05f;   // drifts up a little
		ParticleSystem.EmissionModule emission = ps.emission;
		emission.rateOverTime = 0f;
		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = true;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 0.05f;
		SetAlphaOverLifetime(ps, 1f, 0f, 0.3f, 1f);
		ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
		size.enabled = true;
		size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.3f)));
		SetTile(ps, false);
		return ps;
	}

	// ---------------------------------------------------------------- helpers

	/// <summary>A stopped, empty particle system with the shared material and cheap renderer settings.</summary>
	private static ParticleSystem NewSystem(string name, Transform parent, int maxParticles)
	{
		Material mat = Material;
		if (mat == null)
		{
			return null;
		}
		GameObject go = new GameObject(name);
		if (parent != null)
		{
			go.transform.SetParent(parent, false);
		}
		ParticleSystem ps = go.AddComponent<ParticleSystem>();
		ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // (duration can only be set while stopped)
		ParticleSystem.MainModule main = ps.main;
		main.playOnAwake = false;
		main.maxParticles = maxParticles;
		main.scalingMode = ParticleSystemScalingMode.Hierarchy;
		main.startRotation = 0f;
		ParticleSystem.EmissionModule emission = ps.emission;
		emission.enabled = true;
		emission.rateOverTime = 0f;
		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = false;

		ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
		r.sharedMaterial = mat;
		r.renderMode = ParticleSystemRenderMode.Billboard;
		r.sortMode = ParticleSystemSortMode.None;
		r.shadowCastingMode = ShadowCastingMode.Off;
		r.receiveShadows = false;
		r.lightProbeUsage = LightProbeUsage.Off;
		r.reflectionProbeUsage = ReflectionProbeUsage.Off;
		r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
		r.allowOcclusionWhenDynamic = false;
		return ps;
	}

	private static void SetBurst(ParticleSystem ps, int count)
	{
		ParticleSystem.EmissionModule emission = ps.emission;
		emission.rateOverTime = 0f;
		emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
	}

	/// <summary>White colour over lifetime with alpha: <paramref name="startAlpha"/> at 0, 1 from <paramref name="fadeInEnd"/> to <paramref name="fadeOutStart"/>, 0 at <paramref name="end"/>.</summary>
	private static void SetAlphaOverLifetime(ParticleSystem ps, float startAlpha, float fadeInEnd, float fadeOutStart, float end)
	{
		Gradient g = new Gradient();
		if (fadeInEnd <= 0f)
		{
			g.SetKeys(
				new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
				new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, end) });
		}
		else
		{
			g.SetKeys(
				new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
				new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(1f, fadeInEnd), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, end) });
		}
		ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
		col.enabled = true;
		col.color = new ParticleSystem.MinMaxGradient(g);
	}

	/// <summary>Which tile of the glow atlas: the round dot only, or (randomly per particle) dot or star.</summary>
	private static void SetTile(ParticleSystem ps, bool dotsAndStars)
	{
		ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
		sheet.enabled = true;
		sheet.mode = ParticleSystemAnimationMode.Grid;
		sheet.numTilesX = 2;
		sheet.numTilesY = 1;
		sheet.animation = ParticleSystemAnimationType.WholeSheet;
		sheet.cycleCount = 1;
		// frameOverTime is normalised over the whole sheet (0 = first tile, 0.5 = second); a constant per particle.
		sheet.frameOverTime = dotsAndStars ? new ParticleSystem.MinMaxCurve(0f, 0.999f) : new ParticleSystem.MinMaxCurve(0f);
	}

	/// <summary>The glow atlas: white rgb, shape in alpha. Tile 0 = soft round dot, tile 1 = four-pointed star.</summary>
	private static Texture2D CreateAtlas()
	{
		int w = AtlasTile * 2;
		int h = AtlasTile;
		Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
		tex.name = "FeedbackFX glow atlas";
		tex.hideFlags = HideFlags.DontSave;
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;
		Color32[] pixels = new Color32[w * h];
		for (int y = 0; y < h; y++)
		{
			float v = (y + 0.5f) / AtlasTile * 2f - 1f;
			for (int x = 0; x < w; x++)
			{
				int tile = x / AtlasTile;
				float u = ((x % AtlasTile) + 0.5f) / AtlasTile * 2f - 1f;
				float r = Mathf.Sqrt(u * u + v * v);
				float falloff = Mathf.Clamp01(1f - r);
				float a;
				if (tile == 0)
				{
					// Soft round glow with a brighter core.
					a = 0.55f * falloff * falloff + 0.6f * Mathf.Exp(-r * r * 22f);
				}
				else
				{
					// Bright core, four thin rays and a faint halo.
					float au = Mathf.Abs(u);
					float av = Mathf.Abs(v);
					float rays = Mathf.Exp(-au * 26f) * (1f - av) * (1f - av) + Mathf.Exp(-av * 26f) * (1f - au) * (1f - au);
					a = Mathf.Exp(-r * r * 40f) + 0.9f * rays + 0.25f * falloff * falloff;
				}
				// Exactly 0 at the tile border (no bleeding between tiles).
				a *= Mathf.Clamp01((1f - Mathf.Max(Mathf.Abs(u), Mathf.Abs(v))) * 8f);
				byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
				pixels[y * w + x] = new Color32(255, 255, 255, alpha);
			}
		}
		tex.SetPixels32(pixels);
		tex.Apply(true, true);
		return tex;
	}
}

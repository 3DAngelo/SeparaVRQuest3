using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Particle feedback, all built in code at run time (no prefabs, no scene set-up):
///  - Hand beam: a soft curved "tractor beam" from the hand to the residuo being chosen, lights flowing toward the hand (see HandBeamFX).
///  - Sparkles: little sparks and lights rising about 20 cm from the pointed / held object (see CustomGrabInteractable).
///  - Fireworks: a burst in the container's colour when an object lands in the right container (GameManager /
///    TutorialIntro), and a small grey puff when an object is released over the wrong one.
///
/// Quest 3 budget: every system uses the same additive unlit shader (Assets/Resources/FxAdditive.shader, single pass
/// instanced ready) in three materials (shared particles, brighter sparkles, beam core line) with small procedural
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
	// A "tractor beam": a soft, slightly curved stream of light from the hand to the residuo being chosen, with little
	// lights flowing FROM the tip TOWARD the hand. When it shows is decided by HandBeamFX (choosing a residuo on the
	// sidewalk, and while the grabbed residuo flies to the hand).

	// Shape, like the stock XRI ray line (CurveUtility.TryGenerateCubicBezierCurve): a quadratic Bezier from the hand to
	// the end point whose control point is origin + aim direction * length * BeamCurveRatio (XRI's default bend ratio is
	// 0.5). The end point lags behind the aim (HandBeamFX smooths it), so when the hand moves the line bends lazily.
	public const float BeamCurveRatio = 0.5f;
	// Extra soft sag of the control point, downward, as a fraction of the length: a gentle arc even with a still hand.
	public const float BeamSag = 0.07f;
	// Points of the core line along the curve.
	public const int BeamCurvePoints = 20;
	// Speed of the lights flowing from the tip to the hand (m/s along the curve).
	public const float BeamSpeed = 2.2f;
	// Particles per second per hand.
	public const float BeamRate = 90f;
	// The stream starts this far in front of the aim origin, so it does not cover the hand itself.
	public const float BeamStartOffset = 0.06f;
	// Longest beam (m); HandBeamFX does not show it toward anything farther.
	public const float BeamMaxLength = 5f;
	// Shorter than this (the residuo has arrived at the hand) nothing is drawn.
	public const float BeamMinLength = 0.1f;
	public const float BeamSizeMin = 0.018f;
	public const float BeamSizeMax = 0.034f;
	// The lights spiral gently around the curve: radius (m) and angular speed (rad/s) of the swirl.
	public const float BeamSwirlRadius = 0.007f;
	public const float BeamSwirlSpeed = 6f;
	// Pale sky blue to warm gold; alpha = how strong the stream is (it is additive, so it also dims it).
	public static readonly Color BeamColorA = new Color(0.55f, 0.85f, 1f, 0.75f);
	public static readonly Color BeamColorB = new Color(1f, 0.85f, 0.5f, 0.75f);
	// Each light is a short streak along the flow: stretched by its velocity times this (seconds).
	public const float BeamStreak = 0.025f;
	// Fade of a light along its way, as fractions of the length (0 = tip, 1 = hand): in over the first BeamFlowFadeIn at
	// the tip, out from BeamFlowFadeOutStart before it reaches the hand. And its size at the hand relative to the tip
	// (they shrink a little while being pulled in).
	public const float BeamFlowFadeIn = 0.06f;
	public const float BeamFlowFadeOutStart = 0.8f;
	public const float BeamFlowSizeAtHand = 0.6f;
	// Cap per hand: BeamRate * (BeamMaxLength / BeamSpeed) is about 205.
	public const int BeamMaxParticles = 224;

	// Soft continuous core of the beam: one additive LineRenderer (BeamCurvePoints points, camera facing) along the same
	// curve, under the flowing lights, so the beam reads as a stream of light rather than a dotted trail. Its own small
	// procedural texture gives a soft cross-section (bright thin centre, faint halo) and fades it at both ends.
	// Tractor look: thinner and warm at the hand, wider and brighter cyan at the tip (it pulls from there).
	// Width (m) at the hand and at the tip.
	public const float BeamCoreWidthStart = 0.006f;
	public const float BeamCoreWidthEnd = 0.011f;
	// Colours at the hand / at the tip (alpha = strength; additive, so this keeps it soft, not blinding).
	public static readonly Color BeamCoreColorA = new Color(1f, 0.86f, 0.5f, 0.5f);
	public static readonly Color BeamCoreColorB = new Color(0.55f, 0.9f, 1f, 0.95f);
	// Fractions of the length (0 = hand, 1 = tip) over which the core fades in from the hand, and from where it fades
	// out at the tip (short, so the tip stays bright).
	public const float BeamCoreFadeIn = 0.06f;
	public const float BeamCoreFadeOutStart = 0.94f;
	// How fast the beam fades in / out when it is shown / hidden (1/s; 6 = about 0.17 s), and the quicker fade when the
	// residuo arrives at the hand.
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
	private static Material coreMaterial;
	private static Texture2D atlas;
	private static Texture2D coreTexture;
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
		coreMaterial = null;
		atlas = null;
		coreTexture = null;
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

	/// <summary>Copy of <see cref="Material"/> with the beam core texture (null without the shader).</summary>
	private static Material CoreMaterial
	{
		get
		{
			if (coreMaterial == null && Material != null)
			{
				if (coreTexture == null)
				{
					coreTexture = CreateCoreTexture();
				}
				coreMaterial = new Material(material);
				coreMaterial.name = "FeedbackFX additive (beam core)";
				coreMaterial.hideFlags = HideFlags.DontSave;
				coreMaterial.mainTexture = coreTexture;
			}
			return coreMaterial;
		}
	}

	// ---------------------------------------------------------------- hand beam

	// Scratch buffers shared by every beam (main thread only; no per-frame allocations).
	private static readonly Vector3[] beamCurve = new Vector3[BeamCurvePoints];
	private static ParticleSystem.Particle[] beamParticles;

	/// <summary>
	/// A new (stopped) beam under <paramref name="parent"/>. Its particles live in world space and are placed on the curve
	/// every frame by <see cref="PlaceBeam"/> (new ones start at the tip, where the system sits). Null without material.
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
		main.startSpeed = 0f;                                         // moved along the curve by FlowBeam, not by the system
		main.startLifetime = BeamMaxLength / BeamSpeed + 0.2f;        // long enough for the longest beam; killed at the hand
		main.startSize = new ParticleSystem.MinMaxCurve(BeamSizeMin, BeamSizeMax);
		main.startColor = new ParticleSystem.MinMaxGradient(BeamColorA, BeamColorB);

		ParticleSystem.EmissionModule emission = ps.emission;
		emission.rateOverTime = BeamRate;

		ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
		r.renderMode = ParticleSystemRenderMode.Stretch;
		r.velocityScale = BeamStreak;
		r.lengthScale = 1f;
		SetTile(ps, false);

		// The continuous core: first child of the beam, world space points along the curve (set by PlaceBeam).
		GameObject coreObject = new GameObject("Beam Core");
		coreObject.transform.SetParent(ps.transform, false);
		LineRenderer core = coreObject.AddComponent<LineRenderer>();
		core.sharedMaterial = CoreMaterial;
		core.useWorldSpace = true;
		core.positionCount = BeamCurvePoints;
		core.alignment = LineAlignment.View;
		core.textureMode = LineTextureMode.Stretch;
		core.numCapVertices = 0;
		core.numCornerVertices = 0;
		core.startWidth = BeamCoreWidthStart;
		core.endWidth = BeamCoreWidthEnd;
		core.widthMultiplier = 0f;   // grown in by PlaceBeam
		core.startColor = BeamCoreColorA;
		core.endColor = BeamCoreColorB;
		core.shadowCastingMode = ShadowCastingMode.Off;
		core.receiveShadows = false;
		core.lightProbeUsage = LightProbeUsage.Off;
		core.reflectionProbeUsage = ReflectionProbeUsage.Off;
		core.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
		core.allowOcclusionWhenDynamic = false;
		core.enabled = false;
		return ps;
	}

	/// <summary>The continuous core line of a beam made by <see cref="CreateBeam"/> (cache it; null if missing).</summary>
	public static LineRenderer BeamCore(ParticleSystem beam)
	{
		return beam != null && beam.transform.childCount > 0 ? beam.transform.GetChild(0).GetComponent<LineRenderer>() : null;
	}

	/// <summary>Point of the quadratic Bezier p0 - p1 - p2 at t (0 = p0, 1 = p2).</summary>
	public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
	{
		float s = 1f - t;
		return s * s * p0 + 2f * s * t * p1 + t * t * p2;
	}

	/// <summary>
	/// The beam's control points: from BeamStartOffset in front of <paramref name="origin"/> (the hand's aim origin) to
	/// <paramref name="end"/>, bending like the XRI line: control point along <paramref name="aimDirection"/> at
	/// BeamCurveRatio of the length, sagged down by BeamSag. Returns the straight distance p0 - p2.
	/// </summary>
	public static float BeamControlPoints(Vector3 origin, Vector3 aimDirection, Vector3 end, out Vector3 p0, out Vector3 p1)
	{
		Vector3 dir = aimDirection.sqrMagnitude > 1e-6f ? aimDirection.normalized : Vector3.forward;
		p0 = origin + dir * BeamStartOffset;
		float chord = Vector3.Distance(p0, end);
		p1 = p0 + dir * (chord * BeamCurveRatio) + Vector3.down * (chord * BeamSag);
		return chord;
	}

	/// <summary>
	/// Puts a beam on its curve (see <see cref="BeamControlPoints"/>) with the given visibility (0 = hidden .. 1 = full):
	/// the core line along the curve, and every light flowing from the tip to the hand. Used both by HandBeamFX and by the
	/// screenshot harness. Playing / stopping the emission is up to the caller.
	/// </summary>
	public static void PlaceBeam(ParticleSystem beam, LineRenderer core, Vector3 origin, Vector3 aimDirection, Vector3 end, float visibility)
	{
		Vector3 p0;
		Vector3 p1;
		float chord = BeamControlPoints(origin, aimDirection, end, out p0, out p1);
		if (chord < BeamMinLength)
		{
			visibility = 0f;
		}
		float length = 0f;
		for (int i = 0; i < BeamCurvePoints; i++)
		{
			beamCurve[i] = Bezier(p0, p1, end, (float)i / (BeamCurvePoints - 1));
			if (i > 0)
			{
				length += Vector3.Distance(beamCurve[i - 1], beamCurve[i]);
			}
		}
		beam.transform.position = end;   // new lights are born at the tip
		if (core != null)
		{
			bool visible = visibility > 0.001f;
			if (core.enabled != visible)
			{
				core.enabled = visible;
			}
			if (visible)
			{
				core.widthMultiplier = visibility;
				core.SetPositions(beamCurve);
			}
		}
		FlowBeam(beam, p0, p1, end, length, visibility);
	}

	/// <summary>
	/// Places every light of the beam on the curve by its age: a light born at the tip has travelled age * BeamSpeed toward
	/// the hand; past the hand it is removed. So the stream follows the curve exactly, and shortens with it (a residuo
	/// flying to the hand). Swirl, colour and size come from each light's own random seed (nothing stored per light).
	/// </summary>
	private static void FlowBeam(ParticleSystem beam, Vector3 p0, Vector3 p1, Vector3 p2, float length, float visibility)
	{
		int max = beam.main.maxParticles;
		if (beamParticles == null || beamParticles.Length < max)
		{
			beamParticles = new ParticleSystem.Particle[max];   // once
		}
		int count = beam.GetParticles(beamParticles);
		if (count == 0)
		{
			return;
		}
		float invLength = 1f / Mathf.Max(0.01f, length);
		float swirlScale = Mathf.Clamp01(length / 0.5f);   // a very short beam (arriving) does not swirl wider than itself
		Vector3 chordDir = (p2 - p0).sqrMagnitude > 1e-8f ? (p2 - p0).normalized : Vector3.forward;
		for (int i = 0; i < count; i++)
		{
			ParticleSystem.Particle p = beamParticles[i];
			float age = p.startLifetime - p.remainingLifetime;
			float u = age * BeamSpeed * invLength;   // 0 = at the tip .. 1 = at the hand
			if (u >= 1f)
			{
				p.remainingLifetime = 0f;   // reached the hand
				p.startColor = new Color32(0, 0, 0, 0);
				p.position = p0;
				beamParticles[i] = p;
				continue;
			}
			float t = 1f - u;   // Bezier parameter (0 = hand, 1 = tip)
			Vector3 position = Bezier(p0, p1, p2, t);
			Vector3 tangent = 2f * (1f - t) * (p1 - p0) + 2f * t * (p2 - p1);
			tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : chordDir;
			Vector3 a = Vector3.Cross(tangent, Vector3.up);
			if (a.sqrMagnitude < 1e-4f)
			{
				a = Vector3.Cross(tangent, Vector3.right);
			}
			a.Normalize();
			Vector3 b = Vector3.Cross(tangent, a);

			uint seed = p.randomSeed;
			float h1 = (seed & 0xFFFF) / 65535f;
			float h2 = ((seed >> 16) & 0xFF) / 255f;
			float h3 = ((seed >> 24) & 0xFF) / 255f;
			float angle = h1 * 6.2831853f + age * BeamSwirlSpeed;
			float radius = BeamSwirlRadius * (0.4f + 0.6f * h2) * swirlScale;
			position += (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * radius;

			float fade = Mathf.SmoothStep(0f, 1f, u / BeamFlowFadeIn) * (1f - Mathf.SmoothStep(0f, 1f, (u - BeamFlowFadeOutStart) / (1f - BeamFlowFadeOutStart))) * visibility;
			Color color = Color.Lerp(BeamColorA, BeamColorB, h3);
			color.a *= fade;

			p.position = position;
			p.velocity = -tangent * BeamSpeed;   // toward the hand (also orients the streak)
			p.startColor = color;
			p.startSize = Mathf.Lerp(BeamSizeMin, BeamSizeMax, h1) * Mathf.Lerp(1f, BeamFlowSizeAtHand, u);
			beamParticles[i] = p;
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

	/// <summary>
	/// The beam core texture: white rgb, alpha = soft cross-section (v: bright thin centre, faint halo) times the fade along
	/// the line (u: 0 = hand .. 1 = tip; fades in from the hand over BeamCoreFadeIn, fades out from BeamCoreFadeOutStart to the tip).
	/// </summary>
	private static Texture2D CreateCoreTexture()
	{
		const int w = 64;
		const int h = 32;
		Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
		tex.name = "FeedbackFX beam core";
		tex.hideFlags = HideFlags.DontSave;
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;
		Color32[] pixels = new Color32[w * h];
		for (int x = 0; x < w; x++)
		{
			float u = (float)x / (w - 1);
			float along = Mathf.SmoothStep(0f, 1f, u / BeamCoreFadeIn) * (1f - Mathf.SmoothStep(0f, 1f, (u - BeamCoreFadeOutStart) / (1f - BeamCoreFadeOutStart)));
			for (int y = 0; y < h; y++)
			{
				float v = ((y + 0.5f) / h) * 2f - 1f;
				float edge = 1f - Mathf.Abs(v);
				float across = 0.85f * Mathf.Exp(-v * v * 20f) + 0.3f * edge * edge;
				byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(across * along) * 255f);
				pixels[y * w + x] = new Color32(255, 255, 255, alpha);
			}
		}
		tex.SetPixels32(pixels);
		tex.Apply(false, true);
		return tex;
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

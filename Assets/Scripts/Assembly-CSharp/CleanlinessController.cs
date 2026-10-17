// Controla la limpieza global del entorno (shader "SeparaVR/Lit Dirty").
// Cada Evolve() de GameManager avanza un paso: _Cleanliness va de startCleanliness a 1 con transición suave.
// Reemplaza a MaterialEvolve en EntornoFix (no instancia materiales, solo setea dos globales).
using System.Collections;
using UnityEngine;

public class CleanlinessController : IEvolve
{
    [Tooltip("Cantidad de Evolve() hasta quedar limpio")]
    public int steps = 5;
    [Tooltip("Usa evolveScores.Length de GameManager como cantidad de pasos")]
    public bool stepsFromGameManager = true;
    [Range(0f, 1f)] public float startCleanliness = 0f;
    [Tooltip("Duración de cada transición (s). 0 = instantáneo")]
    public float transitionSeconds = 2f;
    public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Altura del suelo en mundo: la suciedad se acumula cerca de esta Y")]
    public float groundY = 0f;

    static readonly int CleanlinessId = Shader.PropertyToID("_Cleanliness");
    static readonly int GroundYId = Shader.PropertyToID("_DirtGroundY");

    int step;
    float current, target;
    Coroutine anim;

    public float Cleanliness { get { return current; } }

    void Start()
    {
        if (stepsFromGameManager)
        {
            var gm = FindFirstObjectByType<GameManager>();
            if (gm != null && gm.evolveScores != null && gm.evolveScores.Length > 0)
                steps = gm.evolveScores.Length;
        }
        step = 0;
        target = startCleanliness;
        Apply(startCleanliness);
        Shader.SetGlobalFloat(GroundYId, groundY);
    }

    public override void Evolve()
    {
        if (step >= Mathf.Max(1, steps)) return;
        step++;
        target = Mathf.Lerp(startCleanliness, 1f, (float)step / Mathf.Max(1, steps));

        if (anim != null) { StopCoroutine(anim); anim = null; }
        if (!isActiveAndEnabled || transitionSeconds <= 0f) { Apply(target); return; }
        anim = StartCoroutine(Transition(current, target));
    }

    IEnumerator Transition(float from, float to)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / transitionSeconds;
            Apply(Mathf.LerpUnclamped(from, to, curve.Evaluate(Mathf.Clamp01(t))));
            yield return null;
        }
        Apply(to);
        anim = null;
    }

    void OnDisable()
    {
        // no dejar una transición a medias
        if (anim != null) { StopCoroutine(anim); anim = null; }
        Apply(target);
    }

    void Apply(float v)
    {
        current = Mathf.Clamp01(v);
        Shader.SetGlobalFloat(CleanlinessId, current);
    }
}

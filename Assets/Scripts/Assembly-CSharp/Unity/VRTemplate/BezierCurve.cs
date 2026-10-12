using UnityEngine;

namespace Unity.VRTemplate
{
    public class BezierCurve : MonoBehaviour
    {
        public enum UpdateType
        {
            UpdateAndBeforeRender = 0,
            Update = 1,
            BeforeRender = 2
        }

        const float k_ViewerScaleChangeThreshold = 0.1f;

        [SerializeField]
        [Tooltip("The time within the frame that the curve will be updated. If this Bezier Curve is attached to a transform that is updating before render, then enabling updates in Before Render will keep the line connected without delay.")]
        UpdateType m_UpdateTrackingType = UpdateType.Update;

        [SerializeField]
        [Tooltip("The transform that determines the position, handle rotation, and handle scale of the start point of the bezier curve.")]
        Transform m_StartPoint;

        [SerializeField]
        [Tooltip("The transform that determines the position, handle rotation, and handle scale of the end point of the bezier curve.")]
        Transform m_EndPoint;

        [SerializeField]
        [Tooltip("Controls the scale factor of the curve's start bezier handle.")]
        float m_CurveFactorStart = 1f;

        [SerializeField]
        [Tooltip("Controls the scale factor of the curve's end bezier handle.")]
        float m_CurveFactorEnd = 1f;

        [SerializeField]
        [Tooltip("Controls the number of segments used to draw the curve.")]
        int m_SegmentCount = 50;

        [SerializeField]
        [Tooltip("When enabled, the line color gradient will be animated so that an opaque part travels along the line.")]
        bool m_Animate;

        [SerializeField]
        [Tooltip("If animated, this controls the speed that the animation of the line.")]
        float m_AnimSpeed = 0.25f;

        [SerializeField]
        [Tooltip("If animated, this color will be the main opaque color of the gradient")]
        Color m_GradientKeyColor = new Color(0.1255f, 0.5882f, 0.9529f, 1f);

        [SerializeField]
        [Tooltip("The line renderer that will draw the curve. If not set it will find a line renderer on this GameObject.")]
        LineRenderer m_LineRenderer;

        Vector3[] m_ControlPoints = new Vector3[4];
        float m_Time;
        float m_LineWidth;
        // RECONSTRUCCION-DUDA: m_LastViewerScale is never written in the binary (always 0), so the low-segment branch in DrawCurve is effectively dead; kept as compiled.
        float m_LastViewerScale;
        Vector3 m_LastStartPosition;
        Vector3 m_LastEndPosition;

        void Awake()
        {
            if (m_LineRenderer == null)
                m_LineRenderer = GetComponent<LineRenderer>();

            m_LineWidth = m_LineRenderer.startWidth;
        }

        void OnEnable()
        {
            DrawCurve();
            Application.onBeforeRender += OnBeforeRender;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
        }

        void OnBeforeRender()
        {
            if (m_UpdateTrackingType == UpdateType.UpdateAndBeforeRender || m_UpdateTrackingType == UpdateType.BeforeRender)
                DrawCurve();
        }

        void Update()
        {
            if (m_UpdateTrackingType <= UpdateType.Update)
                DrawCurve();

            if (m_Animate)
                AnimateCurve();
        }

        [ContextMenu("Draw")]
        public void DrawCurve()
        {
            var startPosition = m_StartPoint.position;
            var endPosition = m_EndPoint.position;

            if ((m_LastStartPosition - startPosition).sqrMagnitude < 1e-10f &&
                (m_LastEndPosition - endPosition).sqrMagnitude < 1e-10f)
                return;

            m_ControlPoints[0] = startPosition;
            var distance = (endPosition - startPosition).magnitude;
            m_ControlPoints[1] = startPosition + m_StartPoint.right * (distance * m_CurveFactorStart);
            m_ControlPoints[2] = endPosition - m_EndPoint.right * (distance * m_CurveFactorEnd);
            m_ControlPoints[3] = endPosition;

            var segments = distance < m_LastViewerScale * 0.0125f ? 2 : m_SegmentCount;
            m_LineRenderer.positionCount = segments + 1;
            m_LineRenderer.SetPosition(0, m_ControlPoints[0]);
            for (var i = 1; i <= segments; i++)
            {
                var t = i / (float)segments;
                m_LineRenderer.SetPosition(i, CalculateCubicBezierPoint(t, m_ControlPoints[0], m_ControlPoints[1], m_ControlPoints[2], m_ControlPoints[3]));
            }

            m_LastStartPosition = startPosition;
            m_LastEndPosition = endPosition;
        }

        static Vector3 CalculateCubicBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            var u = 1f - t;
            var tt = t * t;
            var uu = u * u;
            var uuu = uu * u;
            var ttt = tt * t;

            var p = uuu * p0;
            p += 3f * uu * t * p1;
            p += 3f * u * tt * p2;
            p += ttt * p3;
            return p;
        }

        void AnimateCurve()
        {
            // RECONSTRUCCION-DUDA: alpha key values/order (0.25 at m_Time, 1 at 1) and the wrap at m_Time >= 1 are reconstructed from constants, not fully certain.
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(m_GradientKeyColor, 0f) },
                new[] { new GradientAlphaKey(0.25f, m_Time), new GradientAlphaKey(1f, 1f) });
            gradient.mode = GradientMode.Blend;
            m_LineRenderer.colorGradient = gradient;

            m_Time += Time.unscaledDeltaTime * m_AnimSpeed;
            if (m_Time >= 1f)
                m_Time = 0f;
        }
    }
}

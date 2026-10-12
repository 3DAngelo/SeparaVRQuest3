using UnityEngine;
using UnityEngine.Video;

namespace Unity.VRTemplate
{
    [RequireComponent(typeof(VideoPlayer))]
    public class VideoPlayerRenderTexture : MonoBehaviour
    {
        const string k_ShaderName = "Unlit/Texture";

        [SerializeField]
        [Tooltip("The target Renderer which will display the video.")]
        Renderer m_Renderer;

        [SerializeField]
        [Tooltip("The width of the RenderTexture which will be created.")]
        int m_RenderTextureWidth = 1920;

        [SerializeField]
        [Tooltip("The height of the RenderTexture which will be created.")]
        int m_RenderTextureHeight = 1080;

        [SerializeField]
        [Tooltip("The bit depth of the depth channel for the RenderTexture which will be created.")]
        int m_RenderTextureDepth;

        void Start()
        {
            var renderTexture = new RenderTexture(m_RenderTextureWidth, m_RenderTextureHeight, m_RenderTextureDepth);
            renderTexture.Create();

            var material = new Material(Shader.Find(k_ShaderName));
            material.mainTexture = renderTexture;

            var videoPlayer = GetComponent<VideoPlayer>();
            videoPlayer.targetTexture = renderTexture;

            m_Renderer.material = material;
        }
    }
}

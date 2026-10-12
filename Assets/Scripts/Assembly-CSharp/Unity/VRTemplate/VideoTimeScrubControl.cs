using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Unity.VRTemplate
{
    [RequireComponent(typeof(VideoPlayer))]
    public class VideoTimeScrubControl : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Video play/pause button GameObject")]
        GameObject m_ButtonPlayOrPause;

        [SerializeField]
        [Tooltip("Slider that controls the video")]
        Slider m_Slider;

        [SerializeField]
        [Tooltip("Play icon sprite")]
        Sprite m_IconPlay;

        [SerializeField]
        [Tooltip("Pause icon sprite")]
        Sprite m_IconPause;

        [SerializeField]
        [Tooltip("Play or pause button image.")]
        Image m_ButtonPlayOrPauseIcon;

        [SerializeField]
        [Tooltip("Text that displays the current time of the video.")]
        TextMeshProUGUI m_VideoTimeText;

        [SerializeField]
        [Tooltip("If checked, the slider will fade off after a few seconds. If unchecked, the slider will remain on.")]
        bool m_HideSliderAfterFewSeconds;

        bool m_IsDragging;
        bool m_VideoIsPlaying;
        bool m_VideoJumpPending;
        long m_LastFrameBeforeScrub;
        VideoPlayer m_VideoPlayer;

        void Start()
        {
            m_VideoPlayer = GetComponent<VideoPlayer>();

            if (m_VideoPlayer.playOnAwake)
            {
                VideoPlay();
            }
            else
            {
                m_VideoPlayer.playOnAwake = true;
                m_VideoPlayer.Play();
                VideoStop();
            }

            if (m_ButtonPlayOrPause != null)
                m_ButtonPlayOrPause.SetActive(false);
        }

        void OnEnable()
        {
            if (m_VideoPlayer != null)
            {
                m_VideoPlayer.frame = 0L;
                VideoPlay();
            }

            m_Slider.SetValueWithoutNotify(0f);
            m_Slider.onValueChanged.AddListener(OnSliderValueChange);
            m_Slider.gameObject.SetActive(true);

            if (m_HideSliderAfterFewSeconds)
                StartCoroutine(HideSliderAfterSeconds(1f));
        }

        void Update()
        {
            if (m_VideoJumpPending)
            {
                // Wait until the player has actually moved off the pre-scrub frame.
                if (m_LastFrameBeforeScrub == m_VideoPlayer.frame)
                    return;

                m_LastFrameBeforeScrub = long.MinValue;
                m_VideoJumpPending = false;
            }

            if (m_IsDragging)
                return;

            if (m_VideoPlayer.frameCount == 0UL)
                return;

            m_Slider.SetValueWithoutNotify((float)m_VideoPlayer.frame / (float)m_VideoPlayer.frameCount);
        }

        public void OnPointerDown()
        {
            m_VideoJumpPending = true;
            VideoStop();
            VideoJump();
        }

        public void OnRelease()
        {
            m_IsDragging = false;
            VideoPlay();
            VideoJump();
        }

        void OnSliderValueChange(float sliderValue)
        {
            UpdateVideoTimeText();
        }

        // RECONSTRUCCION-DUDA: coroutine body taken from ISIL only (not re-checked with direct disassembly).
        IEnumerator HideSliderAfterSeconds(float duration = 1f)
        {
            yield return new WaitForSeconds(duration);
            m_Slider.gameObject.SetActive(false);
        }

        public void OnDrag()
        {
            m_IsDragging = true;
            m_VideoJumpPending = true;
        }

        void VideoJump()
        {
            m_VideoJumpPending = true;
            var frame = (float)m_VideoPlayer.frameCount * m_Slider.value;
            m_LastFrameBeforeScrub = m_VideoPlayer.frame;
            m_VideoPlayer.frame = (long)frame;
        }

        public void PlayOrPauseVideo()
        {
            if (m_VideoIsPlaying)
                VideoStop();
            else
                VideoPlay();
        }

        void UpdateVideoTimeText()
        {
            if (m_VideoPlayer != null && m_VideoTimeText != null)
            {
                var current = TimeSpan.FromSeconds(m_VideoPlayer.time);
                var total = TimeSpan.FromSeconds(m_VideoPlayer.length);
                var currentText = string.Format("{0:D2}:{1:D2}", current.Minutes, current.Seconds);
                var totalText = string.Format("{0:D2}:{1:D2}", total.Minutes, total.Seconds);
                m_VideoTimeText.SetText(string.Concat(currentText, " / ", totalText));
            }
        }

        void VideoStop()
        {
            m_VideoIsPlaying = false;
            m_VideoPlayer.Pause();
            m_ButtonPlayOrPauseIcon.sprite = m_IconPlay;
            m_ButtonPlayOrPause.SetActive(true);
        }

        void VideoPlay()
        {
            m_VideoIsPlaying = true;
            m_VideoPlayer.Play();
            m_ButtonPlayOrPauseIcon.sprite = m_IconPause;
            m_ButtonPlayOrPause.SetActive(false);
        }
    }
}

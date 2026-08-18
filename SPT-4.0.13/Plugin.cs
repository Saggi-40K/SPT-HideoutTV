using BepInEx;
using BepInEx.Configuration;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;

namespace HideoutTV
{
    [BepInPlugin(
        "com.nathan.hideouttv",
        "Hideout TV",
        "1.0.0"
    )]
    public class Plugin : BaseUnityPlugin
    {
        private const string TvPath =
            "09_rest_space/level3/highlight_transform/TV_LCD_only_TV/Video Player";

        private ConfigEntry<KeyboardShortcut> toggleKey;
        private ConfigEntry<KeyboardShortcut> nextChannelKey;
        private ConfigEntry<KeyboardShortcut> previousChannelKey;
        private ConfigEntry<KeyboardShortcut> pausePlayKey;
        private ConfigEntry<float> masterVolume;
        private ConfigEntry<bool> distanceVolume;
        private ConfigEntry<float> minimumVolume;
        private ConfigEntry<float> minDistance;
        private ConfigEntry<float> maxDistance;

        private VideoPlayer tvPlayer;
        private AudioSource audioSource;
        private Transform playerTransform;

        private VideoClip originalClip;
        private VideoSource originalSource;
        private string originalUrl;
        private RenderTexture originalTexture;

        private bool customVideoPlaying = false;
        private string[] channels = new string[0];
        private int currentChannel = 0;
        private float channelIndicatorTimer = 0.0f;
        private const float ChannelIndicatorDuration = 2.0f;
        private string channelIndicatorText = "";
        private ConfigEntry<bool> rescanChannelsButton;
        private bool rescanRequested = false;


        private void Awake()
        {
            toggleKey = Config.Bind(
                "Controls",
                "Toggle TV",
                new KeyboardShortcut(KeyCode.F9),
                "Turn the custom Hideout TV on or off."
            );

            nextChannelKey = Config.Bind(
                "Controls",
                "Next Channel",
                new KeyboardShortcut(KeyCode.F10),
                "Switch to the next TV channel."
            );

            previousChannelKey = Config.Bind(
                "Controls",
                "Previous Channel",
                new KeyboardShortcut(KeyCode.F8),
                "Switch to the previous TV channel."
            );

            pausePlayKey = Config.Bind(
    "Controls",
    "Pause / Play",
    new KeyboardShortcut(KeyCode.F7),
    "Pause or resume the current custom TV channel."
);


            masterVolume = Config.Bind(
                "Audio",
                "Master Volume",
                0.30f,
                new ConfigDescription(
                    "Overall TV volume.",
                    new AcceptableValueRange<float>(
                        0.0f,
                        1.0f
                    )
                )
            );

            distanceVolume = Config.Bind(
                "Audio",
                "Distance Volume",
                true,
                "Enable distance-based TV volume."
            );

            minimumVolume = Config.Bind(
                "Audio",
                "Minimum Volume",
                0.10f,
                new ConfigDescription(
                    "Volume remaining after Max Distance is reached.",
                    new AcceptableValueRange<float>(
                        0.0f,
                        1.0f
                    )
                )
            );

            minDistance = Config.Bind(
                "Audio",
                "Min Distance",
                10.0f,
                new ConfigDescription(
                    "Distance where the TV begins getting quieter.",
                    new AcceptableValueRange<float>(
                        0.0f,
                        100.0f
                    )
                )
            );

            maxDistance = Config.Bind(
                "Audio",
                "Max Distance",
                25.0f,
                new ConfigDescription(
                    "Distance where Minimum Volume is reached.",
                    new AcceptableValueRange<float>(
                        1.0f,
                        200.0f
                    )
                )
            );

            rescanChannelsButton = Config.Bind(
    "Channels",
    "Rescan Channels",
    false,
    new ConfigDescription(
        "Rescan the Channels folder for MP4 files.",
        null,
        new ConfigurationManagerAttributes
        {
            HideSettingName = true,
            HideDefaultButton = true,
            CustomDrawer = DrawRescanButton
        }
    )
);

            Logger.LogInfo("Hideout TV loaded.");
        
        }

        private void DrawRescanButton(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Rescan Channels"))
            {
                rescanRequested = true;
            }
        }
        private void LoadChannels()
        {
            string pluginFolder =
                Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location
                );

            string channelsFolder =
                Path.Combine(
                    pluginFolder,
                    "Channels"
                );

            if (!Directory.Exists(channelsFolder))
            {
                Directory.CreateDirectory(
                    channelsFolder
                );

                Logger.LogInfo(
                    "Channels folder created."
                );

                channels = new string[0];

                return;
            }

            channels =
                Directory.GetFiles(
                    channelsFolder,
                    "*.mp4"
                );
        }

        private void Update()
        {
            if (rescanRequested)
            {
                rescanRequested = false;

                LoadChannels();

                channelIndicatorText =
                    "Channels rescanned - " +
                    channels.Length +
                    " channel(s) found";

                channelIndicatorTimer =
                    ChannelIndicatorDuration;
            }

            if (toggleKey.Value.IsDown())
            {
                if (tvPlayer == null)
                {
                    FindTV();
                }

                if (tvPlayer == null)
                {
                    Logger.LogError(
                        "Hideout TV could not be found."
                    );

                    return;
                }

                if (customVideoPlaying)
                {
                    RestoreOriginalTV();
                }
                else
                {
                    PlayCustomVideo();
                }
            }

            if (customVideoPlaying)
            {
                if (pausePlayKey.Value.IsDown())
                {
                    TogglePause();
                }

                if (nextChannelKey.Value.IsDown())
                {
                    ChangeChannel(1);
                }

                if (previousChannelKey.Value.IsDown())
                {
                    ChangeChannel(-1);
                }

                FindPlayer();
                UpdateDistanceVolume();
            }
        }

        private void UpdateDistanceVolume()
        {
            if (audioSource == null)
                return;

            float master =
                Mathf.Clamp01(
                    masterVolume.Value
                );

            if (!distanceVolume.Value)
            {
                audioSource.volume = master;
                return;
            }

            if (
                playerTransform == null ||
                tvPlayer == null
            )
            {
                audioSource.volume = master;
                return;
            }

            float distance =
                Vector3.Distance(
                    playerTransform.position,
                    tvPlayer.transform.position
                );

            float start =
                Mathf.Max(
                    0.0f,
                    minDistance.Value
                );

            float end =
                Mathf.Max(
                    start + 0.1f,
                    maxDistance.Value
                );

            float minimum =
                Mathf.Clamp01(
                    minimumVolume.Value
                );

            float distanceMultiplier;

            if (distance <= start)
            {
                distanceMultiplier = 1.0f;
            }
            else if (distance >= end)
            {
                distanceMultiplier = minimum;
            }
            else
            {
                float t =
                    Mathf.InverseLerp(
                        start,
                        end,
                        distance
                    );

                distanceMultiplier =
                    Mathf.Lerp(
                        1.0f,
                        minimum,
                        t
                    );
            }

            audioSource.volume =
                Mathf.Clamp01(
                    master *
                    distanceMultiplier
                );
        }

        private void TogglePause()
        {
            if (tvPlayer == null)
                return;

            if (tvPlayer.isPaused)
            {
                tvPlayer.Play();

                channelIndicatorText =
                    "Play";
            }
            else
            {
                tvPlayer.Pause();

                channelIndicatorText =
                    "Pause";
            }

            channelIndicatorTimer =
                ChannelIndicatorDuration;
        }

        private void ShowChannelIndicator()
        {
            if (
                channels == null ||
                channels.Length == 0 ||
                currentChannel < 0 ||
                currentChannel >= channels.Length
            )
                return;

            string channelName =
                Path.GetFileNameWithoutExtension(
                    channels[currentChannel]
                );

            channelIndicatorText =
                "CH " +
                (currentChannel + 1) +
                "  -  " +
                channelName;

            channelIndicatorTimer =
                ChannelIndicatorDuration;
        }

        private void OnGUI()
        {
            if (channelIndicatorTimer <= 0.0f)
                return;

            channelIndicatorTimer -=
                Time.unscaledDeltaTime;

            float alpha =
                Mathf.Clamp01(
                    channelIndicatorTimer / 0.5f
                );

            GUIStyle style =
                new GUIStyle(
                    GUI.skin.label
                );

            style.fontSize = 18;
            style.alignment =
                TextAnchor.MiddleCenter;

            style.normal.textColor =
                new Color(
                    1.0f,
                    1.0f,
                    1.0f,
                    alpha
                );

            Rect rect =
                new Rect(
                    (Screen.width - 400.0f) / 2.0f,
                    Screen.height * 0.72f,
                    400.0f,
                    30.0f
                );

            GUI.Label(
                rect,
                channelIndicatorText,
                style
            );
        }

        private void ChangeChannel(int direction)
        {
            if (channels == null || channels.Length == 0)
            {
                channelIndicatorText =
                    "No channels found";

                channelIndicatorTimer =
                    ChannelIndicatorDuration;

                return;
            }

            currentChannel += direction;

            if (currentChannel >= channels.Length)
            {
                currentChannel = 0;
            }

            if (currentChannel < 0)
            {
                currentChannel = channels.Length - 1;
            }

            tvPlayer.Stop();

            tvPlayer.url =
                channels[currentChannel];
            SetupAudio();
            tvPlayer.Play();

            ShowChannelIndicator();

            Logger.LogInfo(
                "Switched to channel " +
                (currentChannel + 1)
            );
        }

        private void AdjustTvInteractionCollider()
        {
            if (tvPlayer == null)
                return;

            GameObject tvObject =
                tvPlayer.transform.parent.gameObject;

            BoxCollider collider =
                tvObject.GetComponent<BoxCollider>();

            if (collider == null)
                return;

            collider.center =
                new Vector3(
                    collider.center.x,
                    collider.center.y,
                    -0.11f
                );

            Logger.LogInfo(
                "TV interaction collider repositioned."
            );
        }

        private void SetupAudio()
        {
            if (tvPlayer == null)
                return;

            audioSource =
                tvPlayer.gameObject.GetComponent<AudioSource>();

            if (audioSource == null)
            {
                audioSource =
                    tvPlayer.gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;

            // Force this AudioSource to behave as a 3D emitter.
            audioSource.spatialBlend = 1.0f;
            audioSource.spatialize = true;
            audioSource.spread = 0.0f;
            audioSource.dopplerLevel = 0.0f;

            // Temporary 3D distance settings for testing.
            audioSource.minDistance = 5.0f;
            audioSource.maxDistance = 100.0f;
            audioSource.rolloffMode =
                  AudioRolloffMode.Custom;
            AnimationCurve flatRolloff =
    new AnimationCurve();

            flatRolloff.AddKey(
                0.0f,
                1.0f
            );

            flatRolloff.AddKey(
                1.0f,
                1.0f
            );

            audioSource.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                flatRolloff
            );

            tvPlayer.audioOutputMode =
                VideoAudioOutputMode.AudioSource;

            tvPlayer.EnableAudioTrack(
                0,
                true
            );

            tvPlayer.SetTargetAudioSource(
                0,
                audioSource
            );

        }

        private void FindTV()
        {
            VideoPlayer[] players =
                Resources.FindObjectsOfTypeAll<VideoPlayer>();

            for (int i = 0; i < players.Length; i++)
            {
                VideoPlayer player =
                    players[i];

                if (player == null)
                    continue;

                string path =
                    GetPath(
                        player.gameObject
                    );

                if (path == TvPath)
                {
                    tvPlayer =
                        player;

                    Logger.LogInfo(
                        "Hideout TV found."
                    );

                    return;
                }
            }
        }




        private void FindPlayer()
        {
            if (playerTransform != null)
                return;

            Type localPlayerType =
                Type.GetType(
                    "EFT.LocalPlayer, Assembly-CSharp"
                );

            if (localPlayerType == null)
                return;

            UnityEngine.Object[] players =
                Resources.FindObjectsOfTypeAll(
                    localPlayerType
                );

            for (int i = 0; i < players.Length; i++)
            {
                Component player =
                    players[i] as Component;

                if (player == null)
                    continue;

                playerTransform =
                    player.transform;

                Logger.LogInfo(
                    "Local player found."
                );

                return;
            }
        }

        private void PlayCustomVideo()
        {
            LoadChannels();

            if (channels.Length == 0)
            {
                Logger.LogError(
                    "No MP4 files were found in the Channels folder."
                );

                return;
            }
            AdjustTvInteractionCollider();
            currentChannel = 0;

            originalSource = tvPlayer.source;
            originalClip = tvPlayer.clip;
            originalUrl = tvPlayer.url;
            originalTexture = tvPlayer.targetTexture;
            tvPlayer.Stop();

            tvPlayer.source = VideoSource.Url;
            tvPlayer.url = channels[currentChannel];
            tvPlayer.isLooping = true;
            SetupAudio();
            tvPlayer.Play();

            customVideoPlaying = true;

            Logger.LogInfo(
                "Custom Hideout TV video started."
            );
        }

        private void RestoreOriginalTV()
        {
            if (tvPlayer == null)
                return;

            tvPlayer.Stop();

            tvPlayer.source =
                originalSource;

            tvPlayer.clip =
                originalClip;

            tvPlayer.url =
                originalUrl ?? "";

            tvPlayer.targetTexture =
                originalTexture;

            tvPlayer.Play();

            customVideoPlaying =
                false;

            Logger.LogInfo(
                "Original Hideout TV restored."
            );
        }

        private string GetPath(GameObject obj)
        {
            string path =
                obj.name;

            Transform current =
                obj.transform.parent;

            while (current != null)
            {
                path =
                    current.name +
                    "/" +
                    path;

                current =
                    current.parent;
            }

            return path;
        }
    }
}
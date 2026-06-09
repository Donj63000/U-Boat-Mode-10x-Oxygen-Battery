using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.UI.Notifications;
using UBOAT.Game.UI.Resources;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LongSubmerged10x
{
    // DonJ : point d'entree du runtime UBOAT. Cette classe ne porte pas la logique gameplay elle-meme ;
    // elle charge les reglages, cree le menu F10, installe les hooks Harmony et lance une premiere passe runtime.
    public sealed class LongSubmergedRuntimePatchMod : IUserMod
    {
        private const string RuntimeVersion = "1.4.2";

        public string Name
        {
            get { return "Long Submerged 10x+ AirFix"; }
        }

        public void OnLoaded()
        {
            try
            {
                // DonJ : je charge les reglages PlayerPrefs et je cree le menu avant Harmony.
                // Si un hook Harmony casse apres une mise a jour UBOAT, le menu et le heartbeat batterie existent quand meme.
                LongSubmergedRuntimeSettings.Load();
                LongSubmergedMenuController.Ensure();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            // DonJ : je patche chaque hook un par un. Un patch rate ne doit jamais empecher la batterie,
            // l'oxygene, les torpilles ou le menu de continuer a fonctionner avec les autres hooks valides.
            LongSubmergedRuntimePatcher.PatchSafely(new Harmony("donj.longsubmerged10x.airfix"));

            try
            {
                // DonJ : premiere application directe. Elle couvre le cas ou des objets existent deja
                // avant que leurs hooks Awake/Start aient pu etre interceptes.
                LongSubmergedRuntimeApplier.ApplyAll("mod loaded");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            Debug.Log("[LongSubmerged10x] Runtime patch loaded v" + RuntimeVersion + ". F10 ouvre le menu Long Submerged.");
        }
    }

    // DonJ : liste centralisee des hooks Harmony du mod. Garder cette liste explicite rend le chargement
    // robuste : on voit exactement quelles zones du jeu sont touchees et on peut ignorer un hook incompatible.
    internal static class LongSubmergedRuntimePatcher
    {
        private static readonly Type[] PatchTypes = new Type[]
        {
            typeof(PlayerShipAwakePatch),
            typeof(PlayerShipOnAfterDeserializePatch),
            typeof(PlayerShipUpdatePatch),
            typeof(ResourceUpdateAmountBatteryPatch),
            typeof(PlayerShipValidateTargetVelocityPatch),
            typeof(PlayerShipSavesManagerOnLoadedPatch),
            typeof(PlayerShipCrewAddedPatch),
            typeof(PlayerShipCrewRemovedPatch),
            typeof(PlayerShipEngineAwakePatch),
            typeof(PlayerShipEngineOnAfterDeserializePatch),
            typeof(PlayerShipEngineSavesManagerOnLoadedPatch),
            typeof(AccumulatorsUpgradeStartPatch),
            typeof(DivingPlanesStationAwakePatch),
            typeof(DivingPlanesStationUpdateModifiersPatch),
            typeof(AirCompressorOnEnablePatch),
            typeof(AirCompressorEnergyUsageChangedPatch),
            typeof(GyrocompassApplyModifiersPatch),
            typeof(TrimPumpOnEnablePatch),
            typeof(VentilationOnEnablePatch),
            typeof(StoredTorpedoStartPatch),
            typeof(StoredTorpedoApplyWarmUpModifierPatch),
            typeof(TorpedoAwakePatch),
            typeof(TorpedoFixedUpdatePatch),
            typeof(TorpedoDetonatePatch),
            typeof(ResourceGuiGetTooltipContentsPatch),
            typeof(ResourceGuiUpdateDisplayedValuePatch),
            typeof(DepletingResourceNotificationDoUpdatePatch)
        };

        public static void PatchSafely(Harmony harmony)
        {
            if (harmony == null)
                return;

            foreach (Type patchType in PatchTypes)
            {
                try
                {
                    harmony.CreateClassProcessor(patchType).Patch();
                    Debug.Log("[LongSubmerged10x] Harmony patch active: " + patchType.Name + ".");
                }
                catch (Exception ex)
                {
                    // DonJ : une seule methode renommee dans UBOAT ne doit plus neutraliser tout le mod.
                    Debug.LogWarning("[LongSubmerged10x] Harmony patch skipped: " + patchType.Name + " -> " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }

    // DonJ : etat runtime sauvegarde par le menu F10. Les sliders restent entre 1 et 100 :
    // 1 = comportement proche vanilla, 100 = maximum du mod, avec batterie infinie au cran 100.
    internal static class LongSubmergedRuntimeSettings
    {
        private const string PrefPrefix = "LongSubmerged10x.";
        private const int RuntimeSettingsVersion = 7;
        public const float MinRuntimeFactor = 1f;
        public const float MaxRuntimeFactor = 100f;
        private const bool DefaultMegaBattery = true;
        private const bool DefaultMegaOxygen = true;
        private const bool DefaultSuperSpeed = true;
        private const bool DefaultMegaTorpedoes = true;
        private const float DefaultBatteryFactor = 100f;
        private const float DefaultOxygenFactor = 100f;
        private const float DefaultSpeedFactor = 3.5f;
        private const float DefaultTorpedoFactor = 10f;

        public static bool MegaBattery = DefaultMegaBattery;
        public static bool MegaOxygen = DefaultMegaOxygen;
        public static bool SuperSpeed = DefaultSuperSpeed;
        public static bool MegaTorpedoes = DefaultMegaTorpedoes;
        public static float BatteryFactor = DefaultBatteryFactor;
        public static float OxygenFactor = DefaultOxygenFactor;
        public static float SpeedFactor = DefaultSpeedFactor;
        public static float TorpedoFactor = DefaultTorpedoFactor;

        public static void Load()
        {
            if (PlayerPrefs.GetInt(PrefPrefix + "RuntimeSettingsVersion", 0) < RuntimeSettingsVersion)
            {
                // DonJ : quand je change le modele de reglages, je force les nouveaux defauts propres
                // pour eviter qu'une vieille sauvegarde PlayerPrefs garde un profil casse.
                ResetToDefaults();
                Save();
                Debug.Log("[LongSubmerged10x] Runtime settings migrated to defaults v" + RuntimeSettingsVersion + ".");
                return;
            }

            MegaBattery = ReadBool("MegaBattery", DefaultMegaBattery);
            MegaOxygen = ReadBool("MegaOxygen", DefaultMegaOxygen);
            SuperSpeed = ReadBool("SuperSpeed", DefaultSuperSpeed);
            MegaTorpedoes = ReadBool("MegaTorpedoes", DefaultMegaTorpedoes);
            BatteryFactor = ReadFactor("BatteryFactor", DefaultBatteryFactor);
            OxygenFactor = ReadFactor("OxygenFactor", DefaultOxygenFactor);
            SpeedFactor = ReadFactor("SpeedFactor", DefaultSpeedFactor);
            TorpedoFactor = ReadFactor("TorpedoFactor", DefaultTorpedoFactor);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt(PrefPrefix + "MegaBattery", MegaBattery ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "MegaOxygen", MegaOxygen ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "SuperSpeed", SuperSpeed ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "MegaTorpedoes", MegaTorpedoes ? 1 : 0);
            PlayerPrefs.SetFloat(PrefPrefix + "BatteryFactor", ClampFactor(BatteryFactor));
            PlayerPrefs.SetFloat(PrefPrefix + "OxygenFactor", ClampFactor(OxygenFactor));
            PlayerPrefs.SetFloat(PrefPrefix + "SpeedFactor", ClampFactor(SpeedFactor));
            PlayerPrefs.SetFloat(PrefPrefix + "TorpedoFactor", ClampFactor(TorpedoFactor));
            PlayerPrefs.SetInt(PrefPrefix + "RuntimeSettingsVersion", RuntimeSettingsVersion);
            PlayerPrefs.Save();
        }

        public static void ResetToDefaults()
        {
            MegaBattery = DefaultMegaBattery;
            MegaOxygen = DefaultMegaOxygen;
            SuperSpeed = DefaultSuperSpeed;
            MegaTorpedoes = DefaultMegaTorpedoes;
            BatteryFactor = DefaultBatteryFactor;
            OxygenFactor = DefaultOxygenFactor;
            SpeedFactor = DefaultSpeedFactor;
            TorpedoFactor = DefaultTorpedoFactor;
        }

        public static float ClampFactor(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return MinRuntimeFactor;

            return Mathf.Clamp(value, MinRuntimeFactor, MaxRuntimeFactor);
        }

        private static bool ReadBool(string key, bool fallback)
        {
            return PlayerPrefs.GetInt(PrefPrefix + key, fallback ? 1 : 0) != 0;
        }

        private static float ReadFactor(string key, float fallback)
        {
            return ClampFactor(PlayerPrefs.GetFloat(PrefPrefix + key, fallback));
        }
    }

    // DonJ : vrai menu Unity UI en ScreenSpaceOverlay. Je n'utilise plus l'ancien rendu IMGUI,
    // car UBOAT pouvait figer ou masquer ce rendu. F10 ouvre/ferme, Escape ferme, et les changements s'appliquent en jeu.
    internal sealed class LongSubmergedMenuController : MonoBehaviour
    {
        private const KeyCode MenuKey = KeyCode.F10;
        private const int CanvasSortingOrder = 32000;
        private const float BatteryMaintenanceIntervalSeconds = 0.20f;
        private static LongSubmergedMenuController instance;
        private static Font cachedFont;

        private GameObject panelObject;
        private Toggle megaBatteryToggle;
        private Toggle megaOxygenToggle;
        private Toggle superSpeedToggle;
        private Toggle megaTorpedoesToggle;
        private Slider batteryFactorSlider;
        private Slider oxygenFactorSlider;
        private Slider speedFactorSlider;
        private Slider torpedoFactorSlider;
        private Text batteryFactorValueText;
        private Text oxygenFactorValueText;
        private Text speedFactorValueText;
        private Text torpedoFactorValueText;
        private bool visible;
        private bool suppressToggleEvents;
        private float nextBatteryMaintenanceTime;
        private bool cursorCaptured;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLockState;

        public static void Ensure()
        {
            if (instance != null)
            {
                instance.EnsureUi();
                return;
            }

            instance = UnityEngine.Object.FindObjectOfType<LongSubmergedMenuController>();
            if (instance != null)
            {
                instance.EnsureUi();
                return;
            }

            GameObject go = new GameObject("LongSubmerged10x Runtime Menu");
            UnityEngine.Object.DontDestroyOnLoad(go);
            instance = go.AddComponent<LongSubmergedMenuController>();
        }

        private void Awake()
        {
            instance = this;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            EnsureUi();
        }

        private void OnDestroy()
        {
            RestoreCursorIfNeeded();

            if (instance == this)
                instance = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(MenuKey))
                SetVisible(!visible, "F10");

            if (visible && Input.GetKeyDown(KeyCode.Escape))
                SetVisible(false, "Escape");

            RunBatteryMaintenanceTick();
        }

        private void RunBatteryMaintenanceTick()
        {
            if (!LongSubmergedRuntimeSettings.MegaBattery)
                return;

            // DonJ : le heartbeat tourne meme menu ferme. UBOAT peut recalculer la batterie apres chargement,
            // changement d'equipement ou equipage ; je reapplique donc le mode nucleaire regulierement.
            float now = Time.unscaledTime;
            if (now < nextBatteryMaintenanceTime)
                return;

            nextBatteryMaintenanceTime = now + BatteryMaintenanceIntervalSeconds;
            LongSubmergedRuntimeApplier.MaintainBatteryRuntime("runtime heartbeat");
        }

        private void EnsureUi()
        {
            if (panelObject != null)
                return;

            try
            {
                // DonJ : Canvas overlay avec ordre tres haut pour passer au-dessus de l'UI du jeu.
                Canvas canvas = gameObject.GetComponent<Canvas>();
                if (canvas == null)
                    canvas = gameObject.AddComponent<Canvas>();

                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = CanvasSortingOrder;
                canvas.overrideSorting = true;

                CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
                if (scaler == null)
                    scaler = gameObject.AddComponent<CanvasScaler>();

                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                if (gameObject.GetComponent<GraphicRaycaster>() == null)
                    gameObject.AddComponent<GraphicRaycaster>();

                EnsureEventSystem();
                BuildPanel();
                RefreshControlState();
                panelObject.SetActive(false);
                Debug.Log("[LongSubmerged10x] Runtime Unity UI menu controller ready on F10.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void BuildPanel()
        {
            // DonJ : panneau compact de test runtime. Tous les controles modifient les valeurs sauvegardees
            // et rappellent ApplyAll pour voir le resultat directement dans la partie.
            panelObject = CreateUiObject("LongSubmerged10x Panel", transform);
            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.04f, 0.05f, 0.06f, 0.96f);

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(28f, -82f);
            panelRect.sizeDelta = new Vector2(470f, 512f);

            CreateText(panelObject.transform, "Title", "Long Submerged 10x+", 20, FontStyle.Bold, new Vector2(18f, -16f), new Vector2(410f, 30f));
            CreateText(panelObject.transform, "Hint", "F10 ferme. Les reglages sont sauvegardes et appliques en partie.", 13, FontStyle.Normal, new Vector2(18f, -48f), new Vector2(430f, 24f));

            megaBatteryToggle = CreateToggle(panelObject.transform, "Mega Batterie", new Vector2(20f, -82f));
            batteryFactorSlider = CreateFactorSlider(panelObject.transform, "Batterie", new Vector2(20f, -118f), out batteryFactorValueText);

            megaOxygenToggle = CreateToggle(panelObject.transform, "Mega Oxygene", new Vector2(20f, -158f));
            oxygenFactorSlider = CreateFactorSlider(panelObject.transform, "Oxygene", new Vector2(20f, -194f), out oxygenFactorValueText);

            superSpeedToggle = CreateToggle(panelObject.transform, "SuperVitesse", new Vector2(20f, -234f));
            speedFactorSlider = CreateFactorSlider(panelObject.transform, "Vitesses rapides", new Vector2(20f, -270f), out speedFactorValueText);

            megaTorpedoesToggle = CreateToggle(panelObject.transform, "Mega Torpilles", new Vector2(20f, -310f));
            torpedoFactorSlider = CreateFactorSlider(panelObject.transform, "Torpilles", new Vector2(20f, -346f), out torpedoFactorValueText);

            Button defaultsButton = CreateButton(panelObject.transform, "Par defaut", new Vector2(20f, -430f), new Vector2(140f, 38f));
            defaultsButton.onClick.AddListener(OnDefaultsClicked);

            Button refreshButton = CreateButton(panelObject.transform, "Reappliquer maintenant", new Vector2(176f, -430f), new Vector2(220f, 38f));
            refreshButton.onClick.AddListener(OnRefreshClicked);
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
                return;

            GameObject eventSystemObject = new GameObject("LongSubmerged10x EventSystem");
            UnityEngine.Object.DontDestroyOnLoad(eventSystemObject);
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
            Debug.Log("[LongSubmerged10x] Runtime menu created fallback EventSystem.");
        }

        private void SetVisible(bool value, string source)
        {
            EnsureUi();

            if (panelObject == null || visible == value)
                return;

            visible = value;
            panelObject.SetActive(visible);

            if (visible)
            {
                RefreshControlState();
                CaptureCursor();
            }
            else
            {
                RestoreCursorIfNeeded();
            }

            Debug.Log("[LongSubmerged10x] Runtime menu " + (visible ? "opened" : "closed") + " by " + source + ".");
        }

        private void OnToggleChanged(bool ignored)
        {
            if (suppressToggleEvents)
                return;

            // DonJ : un changement UI met a jour l'etat runtime, sauvegarde PlayerPrefs,
            // puis reapplique le mod sur les objets deja charges dans la scene.
            LongSubmergedRuntimeSettings.MegaBattery = megaBatteryToggle != null && megaBatteryToggle.isOn;
            LongSubmergedRuntimeSettings.MegaOxygen = megaOxygenToggle != null && megaOxygenToggle.isOn;
            LongSubmergedRuntimeSettings.SuperSpeed = superSpeedToggle != null && superSpeedToggle.isOn;
            LongSubmergedRuntimeSettings.MegaTorpedoes = megaTorpedoesToggle != null && megaTorpedoesToggle.isOn;
            LongSubmergedRuntimeSettings.BatteryFactor = ReadSliderFactor(batteryFactorSlider);
            LongSubmergedRuntimeSettings.OxygenFactor = ReadSliderFactor(oxygenFactorSlider);
            LongSubmergedRuntimeSettings.SpeedFactor = ReadSliderFactor(speedFactorSlider);
            LongSubmergedRuntimeSettings.TorpedoFactor = ReadSliderFactor(torpedoFactorSlider);
            LongSubmergedRuntimeSettings.Save();
            LongSubmergedRuntimeApplier.ApplyAll("unity ui toggle");
        }

        private void OnFactorSliderChanged(float ignored)
        {
            if (suppressToggleEvents)
                return;

            OnToggleChanged(false);
            RefreshFactorLabels();
        }

        private void OnDefaultsClicked()
        {
            LongSubmergedRuntimeSettings.ResetToDefaults();
            LongSubmergedRuntimeSettings.Save();
            RefreshControlState();
            LongSubmergedRuntimeApplier.ApplyAll("unity ui defaults");
        }

        private void OnRefreshClicked()
        {
            LongSubmergedRuntimeApplier.ApplyAll("unity ui refresh");
        }

        private void RefreshControlState()
        {
            suppressToggleEvents = true;

            if (megaBatteryToggle != null)
                megaBatteryToggle.isOn = LongSubmergedRuntimeSettings.MegaBattery;

            if (megaOxygenToggle != null)
                megaOxygenToggle.isOn = LongSubmergedRuntimeSettings.MegaOxygen;

            if (superSpeedToggle != null)
                superSpeedToggle.isOn = LongSubmergedRuntimeSettings.SuperSpeed;

            if (megaTorpedoesToggle != null)
                megaTorpedoesToggle.isOn = LongSubmergedRuntimeSettings.MegaTorpedoes;

            SetSliderValue(batteryFactorSlider, LongSubmergedRuntimeSettings.BatteryFactor);
            SetSliderValue(oxygenFactorSlider, LongSubmergedRuntimeSettings.OxygenFactor);
            SetSliderValue(speedFactorSlider, LongSubmergedRuntimeSettings.SpeedFactor);
            SetSliderValue(torpedoFactorSlider, LongSubmergedRuntimeSettings.TorpedoFactor);
            RefreshFactorLabels();

            suppressToggleEvents = false;
        }

        private void RefreshFactorLabels()
        {
            SetFactorLabel(batteryFactorValueText, batteryFactorSlider, "x", batteryFactorSlider != null && batteryFactorSlider.value >= LongSubmergedRuntimeSettings.MaxRuntimeFactor ? "inf" : null);
            SetFactorLabel(oxygenFactorValueText, oxygenFactorSlider, "x", oxygenFactorSlider != null && oxygenFactorSlider.value >= LongSubmergedRuntimeSettings.MaxRuntimeFactor ? "90j" : null);
            SetFactorLabel(speedFactorValueText, speedFactorSlider, "x", null);
            SetFactorLabel(torpedoFactorValueText, torpedoFactorSlider, "x", null);
        }

        private static void SetSliderValue(Slider slider, float value)
        {
            if (slider == null)
                return;

            slider.value = LongSubmergedRuntimeSettings.ClampFactor(value);
        }

        private static float ReadSliderFactor(Slider slider)
        {
            return slider == null ? LongSubmergedRuntimeSettings.MinRuntimeFactor : LongSubmergedRuntimeSettings.ClampFactor(slider.value);
        }

        private static void SetFactorLabel(Text text, Slider slider, string prefix, string suffixOverride)
        {
            if (text == null || slider == null)
                return;

            float value = LongSubmergedRuntimeSettings.ClampFactor(slider.value);
            text.text = suffixOverride == null ? prefix + value.ToString("0") : suffixOverride;
        }

        private void CaptureCursor()
        {
            if (cursorCaptured)
                return;

            previousCursorVisible = Cursor.visible;
            previousCursorLockState = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            cursorCaptured = true;
        }

        private void RestoreCursorIfNeeded()
        {
            if (!cursorCaptured)
                return;

            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLockState;
            cursorCaptured = false;
        }

        private Slider CreateFactorSlider(Transform parent, string label, Vector2 anchoredPosition, out Text valueText)
        {
            GameObject root = CreateUiObject(label + " Factor", parent);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = new Vector2(420f, 30f);

            Text labelText = CreateText(root.transform, "Label", label, 13, FontStyle.Bold, new Vector2(0f, -1f), new Vector2(116f, 24f));
            labelText.alignment = TextAnchor.MiddleLeft;

            valueText = CreateText(root.transform, "Value", "x1", 13, FontStyle.Bold, new Vector2(362f, -1f), new Vector2(58f, 24f));
            valueText.alignment = TextAnchor.MiddleRight;

            GameObject sliderObject = CreateUiObject("Slider", root.transform);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0.5f);
            sliderRect.anchorMax = new Vector2(0f, 0.5f);
            sliderRect.pivot = new Vector2(0f, 0.5f);
            sliderRect.anchoredPosition = new Vector2(124f, -3f);
            sliderRect.sizeDelta = new Vector2(230f, 18f);

            Slider slider = sliderObject.AddComponent<Slider>();
            slider.minValue = LongSubmergedRuntimeSettings.MinRuntimeFactor;
            slider.maxValue = LongSubmergedRuntimeSettings.MaxRuntimeFactor;
            slider.wholeNumbers = true;

            GameObject background = CreateUiObject("Background", sliderObject.transform);
            Image backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = new Color(0.12f, 0.13f, 0.15f, 1f);
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            backgroundRect.sizeDelta = new Vector2(0f, 6f);

            GameObject fillArea = CreateUiObject("Fill Area", sliderObject.transform);
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0f);
            fillAreaRect.anchorMax = new Vector2(1f, 1f);
            fillAreaRect.offsetMin = new Vector2(5f, 0f);
            fillAreaRect.offsetMax = new Vector2(-5f, 0f);

            GameObject fill = CreateUiObject("Fill", fillArea.transform);
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.18f, 0.85f, 0.52f, 1f);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(1f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(0f, 6f);

            GameObject handleArea = CreateUiObject("Handle Slide Area", sliderObject.transform);
            RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(5f, 0f);
            handleAreaRect.offsetMax = new Vector2(-5f, 0f);

            GameObject handle = CreateUiObject("Handle", handleArea.transform);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(0.92f, 0.95f, 0.98f, 1f);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(16f, 16f);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.onValueChanged.AddListener(OnFactorSliderChanged);

            return slider;
        }

        private Toggle CreateToggle(Transform parent, string label, Vector2 anchoredPosition)
        {
            GameObject root = CreateUiObject(label + " Toggle", parent);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = new Vector2(330f, 30f);

            Toggle toggle = root.AddComponent<Toggle>();

            GameObject box = CreateUiObject("Box", root.transform);
            Image boxImage = box.AddComponent<Image>();
            boxImage.color = new Color(0.16f, 0.18f, 0.2f, 1f);
            RectTransform boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.anchoredPosition = new Vector2(0f, 0f);
            boxRect.sizeDelta = new Vector2(24f, 24f);

            GameObject checkmark = CreateUiObject("Checkmark", box.transform);
            Image checkmarkImage = checkmark.AddComponent<Image>();
            checkmarkImage.color = new Color(0.18f, 0.85f, 0.52f, 1f);
            RectTransform checkRect = checkmark.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.pivot = new Vector2(0.5f, 0.5f);
            checkRect.anchoredPosition = Vector2.zero;
            checkRect.sizeDelta = new Vector2(14f, 14f);

            Text labelText = CreateText(root.transform, "Label", label, 16, FontStyle.Normal, new Vector2(34f, -2f), new Vector2(280f, 28f));
            labelText.alignment = TextAnchor.MiddleLeft;

            toggle.targetGraphic = boxImage;
            toggle.graphic = checkmarkImage;
            toggle.onValueChanged.AddListener(OnToggleChanged);

            return toggle;
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject buttonObject = CreateUiObject(label + " Button", parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.13f, 0.26f, 0.42f, 1f);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateText(buttonObject.transform, "Label", label, 15, FontStyle.Bold, Vector2.zero, size);
            text.alignment = TextAnchor.MiddleCenter;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;

            return button;
        }

        private static Text CreateText(Transform parent, string name, string value, int fontSize, FontStyle fontStyle, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject textObject = CreateUiObject(name, parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Text text = textObject.AddComponent<Text>();
            text.text = value;
            text.font = UiFont;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static Font UiFont
        {
            get
            {
                if (cachedFont == null)
                    cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

                return cachedFont;
            }
        }
    }

    // DonJ : coeur gameplay du mod. Cette classe applique les valeurs runtime sans reecrire les fichiers XLSX :
    // elle pose des modifiers sur les Parametres du jeu, garde la batterie pleine et ajuste torpilles/vitesse/oxygene.
    internal static class LongSubmergedRuntimeApplier
    {
        // DonJ : constantes du profil livre. Le joueur peut ensuite ajuster en F10 sans regenerer le mod.
        private const float OxygenVanillaRestoreFactor = 1800f;
        private const float BatteryCapacityDataFactor = 10f;
        private const float EnergyUsageDataFactor = 0.1f;
        private const float BatteryCapacityVanillaRestoreScale = 1f / 10f;
        private const float EnergyUsageVanillaRestoreScale = 1f / 0.1f;
        private const float TorpedoDamageScale = 10f;
        private const float TorpedoCrewDamageScale = 10f;
        private const float TorpedoExplosionRadiusScale = 10f;
        private const float TorpedoExplosionIntensityScale = 10f;
        private const bool PerfectTorpedoReliability = true;
        private const float TorpedoGuidanceLeadSeconds = 4f;
        private const float TorpedoGuidanceMinimumDetonationDistance = 20f;
        private const float TorpedoGuidanceMaximumDetonationDistance = 80f;
        private const float TorpedoGuidanceDetonationRadiusRatio = 0.75f;
        private const string RuntimeScaleModifierName = "LongSubmerged10x Runtime Toggle";
        private const string RuntimeBatteryGainModifierName = "LongSubmerged10x Battery Gain Runtime";
        private const string RuntimeNuclearBatteryCapacityModifierName = "LongSubmerged10x Nuclear Battery Capacity Runtime";
        private const float NuclearBatteryCapacityFloor = 1000000000f;

        private static readonly FieldInfo OxygenBreathModifierField =
            AccessTools.Field(typeof(PlayerShip), "oxygenBreathModifier");

        private static readonly FieldInfo ResourcePlayerShipField =
            AccessTools.Field(typeof(Resource), "playerShip");

        private static readonly FieldInfo AirCompressorEnergyModifierField =
            AccessTools.Field(typeof(AirCompressor), "energyModifier");

        private static readonly FieldInfo GyrocompassEnergyGainModifierField =
            AccessTools.Field(typeof(Gyrocompass), "energyGainModifier");

        private static readonly FieldInfo TrimPumpEnergyGainModifierField =
            AccessTools.Field(typeof(TrimPump), "energyGainModifier");

        private static readonly FieldInfo VentilationEnergyModifierField =
            AccessTools.Field(typeof(Ventilation), "energyModifier");

        private static readonly FieldInfo ResourceGuiResourceField =
            AccessTools.Field(typeof(ResourceGUI), "resource");

        private static readonly FieldInfo DepletingResourceNotificationResourceField =
            AccessTools.Field(typeof(DepletingResourceNotification), "resource");

        private static readonly FieldInfo TorpedoHomingTargetField =
            AccessTools.Field(typeof(Torpedo), "homingTarget");

        private static readonly FieldInfo TorpedoRotatedField =
            AccessTools.Field(typeof(Torpedo), "rotated");

        private static readonly FieldInfo TorpedoSumOfAnglesField =
            AccessTools.Field(typeof(Torpedo), "sumOfAngles");

        private static readonly FieldInfo TorpedoHitEntityField =
            AccessTools.Field(typeof(Torpedo), "hitEntity");

        private static readonly FieldInfo TorpedoPassedDistanceField =
            AccessTools.Field(typeof(Torpedo), "passedDistance");

        private static readonly FieldInfo TorpedoArmDistanceField =
            AccessTools.Field(typeof(Torpedo), "armDistance");

        private static readonly MethodInfo TorpedoDoExplosionHitMethod =
            AccessTools.Method(typeof(Torpedo), "DoExplosionHit");

        private static readonly MethodInfo TorpedoDetonateMethod =
            AccessTools.Method(typeof(Torpedo), "Detonate", new Type[] { typeof(bool) });

        private static readonly ConditionalWeakTable<Parameter, ParameterScalePatchData> ParameterScaleData =
            new ConditionalWeakTable<Parameter, ParameterScalePatchData>();

        // DonJ : ConditionalWeakTable evite de garder en memoire des objets Unity detruits.
        // Chaque Parameter recoit un seul modifier DonJ, ensuite je change juste sa valeur.
        private static readonly ConditionalWeakTable<Parameter, ParameterDeltaPatchData> BatteryGainDeltaData =
            new ConditionalWeakTable<Parameter, ParameterDeltaPatchData>();

        private static readonly ConditionalWeakTable<Parameter, ParameterDeltaPatchData> NuclearBatteryCapacityDeltaData =
            new ConditionalWeakTable<Parameter, ParameterDeltaPatchData>();

        private static readonly ConditionalWeakTable<Torpedo, TorpedoGuidancePatchData> TorpedoGuidanceData =
            new ConditionalWeakTable<Torpedo, TorpedoGuidancePatchData>();

        private static readonly HashSet<int> InfiniteBatteryLoggedShipIds = new HashSet<int>();

        private static readonly HashSet<int> BatteryGainRuntimeLoggedResourceIds = new HashSet<int>();

        private static readonly HashSet<int> NuclearBatteryCapacityLoggedResourceIds = new HashSet<int>();

        private static readonly HashSet<int> BatteryTooltipRuntimeLoggedResourceIds = new HashSet<int>();

        private static readonly Dictionary<Type, FieldInfo[]> GenericEnergyUsageFieldCache =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, FieldInfo[]> GenericParameterCollectionFieldCache =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, FieldInfo[]> GenericEnergyModifierFieldCache =
            new Dictionary<Type, FieldInfo[]>();

        public static void ApplyAll(string reason)
        {
            try
            {
                // DonJ : passe globale volontairement defensive. Elle resynchronise le menu,
                // le PlayerShip, les consommateurs batterie et toutes les torpilles visibles.
                LongSubmergedMenuController.Ensure();
                ApplyPlayerShip(UnityEngine.Object.FindObjectOfType<PlayerShip>(), reason);
                ApplyBatteryConsumers(reason);

                foreach (StoredTorpedo item in UnityEngine.Object.FindObjectsOfType<StoredTorpedo>())
                    ApplyStoredTorpedo(item, reason + ".StoredTorpedo");

                foreach (Torpedo item in UnityEngine.Object.FindObjectsOfType<Torpedo>())
                    ApplyLaunchedTorpedo(item, reason + ".Torpedo");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void MaintainBatteryRuntime(string reason)
        {
            try
            {
                // DonJ : tick leger appele toutes les 0.20s. Il ne rescane pas toute la scene,
                // il remet seulement la ressource batterie du sous-marin dans l'etat attendu.
                ApplyBatteryResource(UnityEngine.Object.FindObjectOfType<PlayerShip>(), reason);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyBatteryConsumers(string reason)
        {
            try
            {
                // DonJ : UBOAT disperse les consommations electriques entre plusieurs composants.
                // Je traite les types connus puis je lance un scan generique pour les champs renommes ou caches.
                foreach (AccumulatorsUpgrade item in UnityEngine.Object.FindObjectsOfType<AccumulatorsUpgrade>())
                    ApplyBatteryObject(item, reason + ".AccumulatorsUpgrade");

                foreach (PlayerShipEngine item in UnityEngine.Object.FindObjectsOfType<PlayerShipEngine>())
                    ApplyBatteryObject(item, reason + ".PlayerShipEngine");

                foreach (DivingPlanesStation item in UnityEngine.Object.FindObjectsOfType<DivingPlanesStation>())
                    ApplyBatteryObject(item, reason + ".DivingPlanesStation");

                foreach (AirCompressor item in UnityEngine.Object.FindObjectsOfType<AirCompressor>())
                    ApplyBatteryObject(item, reason + ".AirCompressor");

                foreach (Gyrocompass item in UnityEngine.Object.FindObjectsOfType<Gyrocompass>())
                    ApplyBatteryObject(item, reason + ".Gyrocompass");

                foreach (TrimPump item in UnityEngine.Object.FindObjectsOfType<TrimPump>())
                    ApplyBatteryObject(item, reason + ".TrimPump");

                foreach (Ventilation item in UnityEngine.Object.FindObjectsOfType<Ventilation>())
                    ApplyBatteryObject(item, reason + ".Ventilation");

                foreach (Equipment item in UnityEngine.Object.FindObjectsOfType<Equipment>())
                    ApplyBatteryEquipment(item, reason + ".Equipment");

                ApplyGenericBatteryConsumers(reason + ".Generic");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyPlayerShip(PlayerShip ship, string reason)
        {
            LongSubmergedMenuController.Ensure();

            if (ship == null)
                return;

            OxygenBreathRecalculator.Recalculate(ship, reason);
            ApplyBatteryResource(ship, reason);
            EngineFastSpeedPatcher.PatchPlayerShip(ship, reason);
        }

        public static void ApplyBatteryResource(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            ApplyBatteryRuntimeToResource(ship.Energy, reason);
        }

        public static void MaintainInfiniteBatteryCharge(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            ApplyBatteryRuntimeToResource(ship.Energy, reason);
        }

        public static bool TryUpdateBatteryResourceAmount(Resource resource, string reason)
        {
            try
            {
                if (!IsPlayerShipEnergyResource(resource))
                    return false;

                ApplyBatteryRuntimeToResource(resource, reason);

                // DonJ : en mode infini je bloque l'UpdateAmount vanilla ; le jeu ne peut plus vider la ressource.
                return IsInfiniteBatteryRuntimeActive();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        private static void ApplyBatteryRuntimeToResource(Resource energy, string reason)
        {
            if (energy == null)
                return;

            // DonJ : pipeline batterie unique. Capacite nucleaire, gain/drain et remplissage passent ici,
            // ce qui evite d'avoir plusieurs comportements batterie qui divergent.
            ApplyNuclearBatteryCapacityOverride(energy, reason);
            ApplyBatteryGainModifiers(energy, reason);

            if (IsInfiniteBatteryRuntimeActive())
                FillBatteryToCapacity(energy, reason);
            else
                ClampBatteryAmountToCapacity(energy);
        }

        private static void ApplyNuclearBatteryCapacityOverride(Resource energy, string reason)
        {
            if (energy == null || energy.Capacity == null)
                return;

            // DonJ : le cran Batterie 100 ne fait pas juste "moins consommer" ;
            // il ajoute une capacite enorme pour que l'UI et le gameplay voient une batterie nucleaire.
            float baseCapacity = energy.Capacity.GetValueExcludingModifier(RuntimeNuclearBatteryCapacityModifierName);
            float targetCapacity = baseCapacity;

            if (IsInfiniteBatteryRuntimeActive())
                targetCapacity = Math.Max(baseCapacity, NuclearBatteryCapacityFloor);

            float delta = targetCapacity - baseCapacity;
            SetDelta(
                energy.Capacity,
                NuclearBatteryCapacityDeltaData,
                RuntimeNuclearBatteryCapacityModifierName,
                delta
            );

            if (IsInfiniteBatteryRuntimeActive())
            {
                int resourceId = RuntimeHelpers.GetHashCode(energy);
                if (NuclearBatteryCapacityLoggedResourceIds.Add(resourceId))
                    Debug.Log("[LongSubmerged10x] Mega Batterie nuclear capacity active after " + reason + ".");
            }
        }

        private static void ClampBatteryAmountToCapacity(Resource energy)
        {
            if (energy == null)
                return;

            double capacity = GetResourceCapacity(energy);
            if (!IsUsableResourceValue(capacity) || capacity <= 0.0)
                return;

            if (energy.Amount > capacity)
                energy.Amount = capacity;
            else if (energy.Amount < 0.0)
                energy.Amount = 0.0;
        }

        public static bool TryMaintainBatteryResource(Resource resource, string reason)
        {
            try
            {
                if (!IsPlayerShipEnergyResource(resource))
                    return false;

                ApplyBatteryRuntimeToResource(resource, reason);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        public static Resource GetResourceFromGui(ResourceGUI gui)
        {
            try
            {
                return gui != null && ResourceGuiResourceField != null
                    ? ResourceGuiResourceField.GetValue(gui) as Resource
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public static Resource GetResourceFromDepletingNotification(DepletingResourceNotification notification)
        {
            try
            {
                return notification != null && DepletingResourceNotificationResourceField != null
                    ? DepletingResourceNotificationResourceField.GetValue(notification) as Resource
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool ShouldSuppressBatteryDepletionUi(Resource resource, string reason)
        {
            if (!IsInfiniteBatteryRuntimeActive())
                return false;

            if (!TryMaintainBatteryResource(resource, reason))
                return false;

            int resourceId = RuntimeHelpers.GetHashCode(resource);
            if (BatteryTooltipRuntimeLoggedResourceIds.Add(resourceId))
                Debug.Log("[LongSubmerged10x] Mega Batterie depletion UI guard active after " + reason + ".");

            return true;
        }

        public static string BuildInfiniteBatteryTooltip(Resource resource)
        {
            if (resource == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            resource.PrintInfo(builder, 1, 1f, "per min", string.Empty, false);
            builder.Append("<line-height=50%>\n<line-height=100%>");
            builder.AppendLine("Mega Batterie : batterie infinie active.");
            return builder.ToString();
        }

        private static void FillBatteryToCapacity(Resource energy, string reason)
        {
            if (energy == null)
                return;

            double capacity = GetResourceCapacity(energy);
            if (!IsUsableResourceValue(capacity) || capacity <= 0.0)
                return;

            if (Math.Abs(energy.Amount - capacity) > 0.0001)
            {
                // DonJ : je garde la batterie au maximum avec le setter Amount pour forcer aussi le refresh UI.
                energy.Amount = capacity;

                int resourceId = RuntimeHelpers.GetHashCode(energy);
                if (InfiniteBatteryLoggedShipIds.Add(resourceId))
                    Debug.Log("[LongSubmerged10x] Mega Batterie infinite hold active after " + reason + ".");
            }
        }

        private static void ApplyBatteryGainModifiers(Resource energy, string reason)
        {
            if (energy == null)
                return;

            float factor = GetEffectiveBatteryGainFactor();
            ApplyBatteryGainParameter(energy.Gain, factor);
            ApplyBatteryGainParameter(energy.GainSandboxTimeScale, factor);

            if (factor >= LongSubmergedRuntimeSettings.MaxRuntimeFactor - 0.0001f)
            {
                int resourceId = RuntimeHelpers.GetHashCode(energy);
                if (BatteryGainRuntimeLoggedResourceIds.Add(resourceId))
                    Debug.Log("[LongSubmerged10x] Mega Batterie infinite gain guard active after " + reason + ".");
            }
        }

        private static float GetEffectiveBatteryGainFactor()
        {
            if (!LongSubmergedRuntimeSettings.MegaBattery)
                return LongSubmergedRuntimeSettings.MinRuntimeFactor;

            return LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.BatteryFactor);
        }

        private static void ApplyBatteryGainParameter(Parameter parameter, float factor)
        {
            if (parameter == null)
                return;

            float baseValue = parameter.GetValueExcludingModifier(RuntimeBatteryGainModifierName);
            float desiredValue = baseValue;

            // DonJ : le slider Batterie regle les durees finies via la capacite.
            // Ici je coupe seulement le gain negatif au cran 100 pour eviter un double xN sur les autres valeurs.
            if (factor >= LongSubmergedRuntimeSettings.MaxRuntimeFactor - 0.0001f && baseValue < 0f)
                desiredValue = 0f;

            SetDelta(
                parameter,
                BatteryGainDeltaData,
                RuntimeBatteryGainModifierName,
                desiredValue - baseValue
            );
        }

        private static void SetDelta(
            Parameter parameter,
            ConditionalWeakTable<Parameter, ParameterDeltaPatchData> table,
            string modifierName,
            float delta
        )
        {
            if (parameter == null || table == null)
                return;

            ParameterDeltaPatchData data;
            if (!table.TryGetValue(parameter, out data))
            {
                data = new ParameterDeltaPatchData(parameter.AddDeltaModifier(modifierName, false));
                table.Add(parameter, data);
            }

            if (data.DeltaModifier == null)
                return;

            if (Math.Abs(data.DeltaModifier.Value - delta) > 0.000001f)
                data.DeltaModifier.Value = delta;
        }

        private static bool IsPlayerShipEnergyResource(Resource resource)
        {
            if (resource == null)
                return false;

            PlayerShip owner = null;
            if (ResourcePlayerShipField != null)
                owner = ResourcePlayerShipField.GetValue(resource) as PlayerShip;

            if (owner == null)
                owner = UnityEngine.Object.FindObjectOfType<PlayerShip>();

            // DonJ : securite anti-faux-positif. Si je trouve le PlayerShip, je n'accepte que sa vraie ressource Energy.
            // Le fallback par nom sert seulement quand UBOAT ne donne pas encore le lien owner.
            if (owner != null)
                return object.ReferenceEquals(owner.Energy, resource);

            return IsEnergyResourceName(resource.Name);
        }

        private static bool IsEnergyResourceName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && (name.Equals("Energy", StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("Battery", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Batterie", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static double GetResourceCapacity(Resource resource)
        {
            if (resource == null || resource.Capacity == null)
                return double.NaN;

            return resource.Capacity.Value;
        }

        private static bool IsUsableResourceValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static void RestoreVanillaOxygenIfNeeded(PlayerShip ship)
        {
            if (ship == null || OxygenBreathModifierField == null)
                return;

            Modifier oxygenModifier = OxygenBreathModifierField.GetValue(ship) as Modifier;
            if (oxygenModifier == null)
                return;

            // DonJ : je compense le XLSX pour que le slider 1 soit vanilla
            // et que le slider 100 garde le profil demande autour de 90 jours.
            oxygenModifier.Value *= OxygenVanillaRestoreFactor / GetEffectiveOxygenDataFactor();
        }

        public static void ApplyBatteryObject(object target, string reason)
        {
            if (target == null)
                return;

            Equipment equipment = target as Equipment;
            if (equipment != null)
                ApplyBatteryEquipment(equipment, reason);

            ApplyBatteryCapacityParameter(GetParameterField(target, "energyCapacityGain"));
            Parameter energyUsage = GetParameterField(target, "energyUsage");
            ApplyEnergyUsageParameter(energyUsage);
            ApplyDirectEnergyGainModifier(target, energyUsage, reason);
        }

        public static void ApplyBatteryEquipment(Equipment equipment, string reason)
        {
            if (equipment == null || equipment.Parameters == null)
                return;

            ApplyBatteryCapacityParameter(GetParameter(equipment.Parameters, "EnergyCapacityGain"));
            ApplyEnergyUsageParameter(GetParameter(equipment.Parameters, "EnergyUsage"));
        }

        public static void ApplyStoredTorpedo(StoredTorpedo storedTorpedo, string reason)
        {
            if (storedTorpedo == null)
                return;

            float reliabilityScale = IsMegaTorpedoRuntimeActive() && PerfectTorpedoReliability ? 0f : 1f;
            SetScale(storedTorpedo.DudChance, reliabilityScale);
        }

        public static void ApplyLaunchedTorpedo(Torpedo torpedo, string reason)
        {
            if (torpedo == null)
                return;

            if (torpedo.Parameters != null)
            {
                // DonJ : les torpilles sont reglees au runtime. A 1 elles redeviennent vanilla ;
                // a 10 elles utilisent le profil mega par defaut ; a 100 elles deviennent extremes.
                float torpedoFactor = GetEffectiveTorpedoFactor();
                float damageScale = torpedoFactor;
                float crewDamageScale = torpedoFactor;
                float radiusScale = torpedoFactor;
                float intensityScale = torpedoFactor;
                float reliabilityScale = IsMegaTorpedoRuntimeActive() && PerfectTorpedoReliability ? 0f : 1f;

                SetScale(GetParameter(torpedo.Parameters, "Damage"), damageScale);
                SetScale(GetParameter(torpedo.Parameters, "CrewDamage"), crewDamageScale);
                SetScale(GetParameter(torpedo.Parameters, "DamageRadius"), radiusScale);
                SetScale(GetParameter(torpedo.Parameters, "DamageEffectsRadius"), radiusScale);
                SetScale(GetParameter(torpedo.Parameters, "DamageEffectsIntensity"), intensityScale);
                SetScale(GetParameter(torpedo.Parameters, "MagneticExplosionOnArm"), reliabilityScale);
                SetScale(GetParameter(torpedo.Parameters, "MagneticExplosionAfterArm"), reliabilityScale);
                SetScale(GetParameter(torpedo.Parameters, "MagneticExplosionFail"), reliabilityScale);
            }

            if (IsMegaTorpedoRuntimeActive())
                ApplyLockedTargetGuidance(torpedo, reason);
            else
                RestoreLockedTargetGuidance(torpedo);
        }

        private static void ApplyLockedTargetGuidance(Torpedo torpedo, string reason)
        {
            Entity target = torpedo.TargetEntity;
            if (target == null)
            {
                RestoreLockedTargetGuidance(torpedo);
                return;
            }

            TorpedoGuidancePatchData data = GetTorpedoGuidanceData(torpedo);
            if (!data.HasOriginalValues)
            {
                data.OriginalGyroAngle = torpedo.GyroAngle;
                data.OriginalTargetPosition = torpedo.TargetPosition;
                data.OriginalTargetPositionForReports = torpedo.TargetPositionForReports;
                data.HasOriginalValues = true;
            }

            Vector3 targetPoint = PredictLockedTargetPoint(torpedo, target);
            if (!IsFinite(targetPoint))
                return;

            // DonJ : je transforme le tir verrouille en visee cartesienne dynamique.
            // L'objectif est qu'une torpille tiree sur une cible correctement verrouillee corrige son angle pendant le vol.
            torpedo.GyroAngle = float.NaN;
            torpedo.TargetPosition = targetPoint;
            torpedo.TargetPositionForReports = targetPoint;
            data.GuidanceApplied = true;

            ResetCartesianTurnLimiter(torpedo);
            ApplyHomingPropeller(torpedo, target);
            TryForceLockedTargetDetonation(torpedo, target);

            if (!data.GuidanceLogged)
            {
                Debug.Log("[LongSubmerged10x] Mega torpedo locked-target guidance active after " + reason + ".");
                data.GuidanceLogged = true;
            }
        }

        private static void RestoreLockedTargetGuidance(Torpedo torpedo)
        {
            TorpedoGuidancePatchData data;
            if (!TorpedoGuidanceData.TryGetValue(torpedo, out data) || !data.HasOriginalValues || !data.GuidanceApplied)
                return;

            try
            {
                torpedo.GyroAngle = data.OriginalGyroAngle;
                torpedo.TargetPosition = data.OriginalTargetPosition;
                torpedo.TargetPositionForReports = data.OriginalTargetPositionForReports;

                if (TorpedoHomingTargetField != null)
                    TorpedoHomingTargetField.SetValue(torpedo, null);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            data.GuidanceApplied = false;
        }

        private static Vector3 PredictLockedTargetPoint(Torpedo torpedo, Entity target)
        {
            Vector3 targetPoint = target.transform.position;
            Ship targetShip = target as Ship;
            if (targetShip != null && targetShip.RigidBody != null)
                targetPoint += targetShip.RigidBody.velocity * TorpedoGuidanceLeadSeconds;

            Vector3 torpedoPosition = torpedo.transform.position;
            targetPoint.y = torpedoPosition.y;
            return targetPoint;
        }

        private static void ResetCartesianTurnLimiter(Torpedo torpedo)
        {
            try
            {
                if (TorpedoRotatedField != null)
                    TorpedoRotatedField.SetValue(torpedo, false);

                if (TorpedoSumOfAnglesField != null)
                    TorpedoSumOfAnglesField.SetValue(torpedo, 0f);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void ApplyHomingPropeller(Torpedo torpedo, Entity target)
        {
            if (TorpedoHomingTargetField == null)
                return;

            Ship targetShip = target as Ship;
            if (targetShip == null)
                return;

            Propeller[] propellers = targetShip.Propellers;
            if (propellers == null || propellers.Length == 0)
                return;

            for (int i = 0; i < propellers.Length; i++)
            {
                if (propellers[i] == null)
                    continue;

                TorpedoHomingTargetField.SetValue(torpedo, propellers[i]);
                return;
            }
        }

        private static void TryForceLockedTargetDetonation(Torpedo torpedo, Entity target)
        {
            if (target == null || torpedo.Detonated || TorpedoDoExplosionHitMethod == null || TorpedoDetonateMethod == null)
                return;

            TorpedoGuidancePatchData data = GetTorpedoGuidanceData(torpedo);
            if (data.ForcingDetonation)
                return;

            if (!IsTorpedoArmedForAssist(torpedo))
                return;

            Vector3 torpedoPosition = torpedo.transform.position;
            Vector3 targetPosition = target.transform.position;
            Vector2 delta = new Vector2(torpedoPosition.x - targetPosition.x, torpedoPosition.z - targetPosition.z);
            float detonationDistance = GetAssistDetonationDistance(torpedo);

            if (delta.sqrMagnitude > detonationDistance * detonationDistance)
                return;

            try
            {
                data.ForcingDetonation = true;

                if (TorpedoHitEntityField != null)
                    TorpedoHitEntityField.SetValue(torpedo, target);

                TorpedoDoExplosionHitMethod.Invoke(torpedo, new object[] { target });
                TorpedoDetonateMethod.Invoke(torpedo, new object[] { true });
                Debug.Log("[LongSubmerged10x] Mega torpedo forced locked-target detonation inside " + detonationDistance.ToString("0.0") + "m.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                data.ForcingDetonation = false;
            }
        }

        private static bool IsTorpedoArmedForAssist(Torpedo torpedo)
        {
            if (TorpedoPassedDistanceField == null || TorpedoArmDistanceField == null)
                return true;

            try
            {
                float passedDistance = (float)TorpedoPassedDistanceField.GetValue(torpedo);
                Parameter armDistance = TorpedoArmDistanceField.GetValue(torpedo) as Parameter;
                return armDistance == null || passedDistance >= armDistance.Value;
            }
            catch
            {
                return true;
            }
        }

        private static float GetAssistDetonationDistance(Torpedo torpedo)
        {
            Parameter damageRadius = torpedo.Parameters == null ? null : GetParameter(torpedo.Parameters, "DamageRadius");
            float scaledDamageRadius = damageRadius == null ? 0f : damageRadius.Value * GetEffectiveTorpedoFactor();
            // DonJ : detonateur de secours proche cible. Il reste borne pour ne pas exploser trop loin,
            // mais suit le rayon mega afin de fiabiliser les impacts verrouilles.
            return Mathf.Clamp(
                scaledDamageRadius * TorpedoGuidanceDetonationRadiusRatio,
                TorpedoGuidanceMinimumDetonationDistance,
                TorpedoGuidanceMaximumDetonationDistance
            );
        }

        private static bool IsMegaTorpedoRuntimeActive()
        {
            return LongSubmergedRuntimeSettings.MegaTorpedoes && GetEffectiveTorpedoFactor() > 1.0001f;
        }

        private static float GetEffectiveTorpedoFactor()
        {
            return LongSubmergedRuntimeSettings.MegaTorpedoes
                ? LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.TorpedoFactor)
                : 1f;
        }

        private static float GetEffectiveOxygenDataFactor()
        {
            if (!LongSubmergedRuntimeSettings.MegaOxygen)
                return 1f;

            float factor = LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.OxygenFactor);
            if (factor <= LongSubmergedRuntimeSettings.MinRuntimeFactor)
                return 1f;

            float normalized = (factor - LongSubmergedRuntimeSettings.MinRuntimeFactor)
                / (LongSubmergedRuntimeSettings.MaxRuntimeFactor - LongSubmergedRuntimeSettings.MinRuntimeFactor);
            return 1f + normalized * (OxygenVanillaRestoreFactor - 1f);
        }

        private static float GetEffectiveBatteryCapacityScale()
        {
            if (!LongSubmergedRuntimeSettings.MegaBattery)
                return BatteryCapacityVanillaRestoreScale;

            float factor = LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.BatteryFactor);
            return factor / BatteryCapacityDataFactor;
        }

        private static float GetEffectiveBatteryEnergyUsageScale()
        {
            // DonJ : pour les valeurs finies, la duree est pilotee par la capacite :
            // 1 = vanilla, 4 = x4, 99 = x99. Je restaure donc le fallback XLSX x0.1 vers vanilla.
            // Au cran 100, je coupe explicitement les consommateurs electriques pour que l'UI et le jeu voient l'infini.
            if (IsInfiniteBatteryRuntimeActive())
                return 0f;

            return EnergyUsageVanillaRestoreScale;
        }

        private static bool IsInfiniteBatteryRuntimeActive()
        {
            return LongSubmergedRuntimeSettings.MegaBattery
                && LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.BatteryFactor) >= LongSubmergedRuntimeSettings.MaxRuntimeFactor;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static TorpedoGuidancePatchData GetTorpedoGuidanceData(Torpedo torpedo)
        {
            TorpedoGuidancePatchData data;
            if (!TorpedoGuidanceData.TryGetValue(torpedo, out data))
            {
                data = new TorpedoGuidancePatchData();
                TorpedoGuidanceData.Add(torpedo, data);
            }

            return data;
        }

        private static void ApplyBatteryCapacityParameter(Parameter parameter)
        {
            if (parameter == null)
                return;

            SetScale(
                parameter,
                GetEffectiveBatteryCapacityScale()
            );
        }

        private static void ApplyEnergyUsageParameter(Parameter parameter)
        {
            if (parameter == null)
                return;

            // DonJ : ne pas tester parameter.Value ici : en mode infini mon scale vaut 0.
            // Quand le joueur redescend le slider, je dois pouvoir restaurer le drain vanilla.
            float baseValue = parameter.GetValueExcludingModifier(RuntimeScaleModifierName);
            if (baseValue <= 0f)
                return;

            SetScale(
                parameter,
                GetEffectiveBatteryEnergyUsageScale()
            );
        }

        private static void ApplyDirectEnergyGainModifier(object target, Parameter energyUsage, string reason)
        {
            if (target == null || energyUsage == null)
                return;

            if (target is AirCompressor)
            {
                ApplyDirectEnergyGainModifierField(target, AirCompressorEnergyModifierField, energyUsage);
                return;
            }

            if (target is Gyrocompass)
            {
                ApplyDirectEnergyGainModifierField(target, GyrocompassEnergyGainModifierField, energyUsage);
                return;
            }

            if (target is TrimPump)
            {
                ApplyDirectEnergyGainModifierField(target, TrimPumpEnergyGainModifierField, energyUsage);
                return;
            }

            if (target is Ventilation)
                ApplyDirectEnergyGainModifierField(target, VentilationEnergyModifierField, energyUsage);
        }

        private static void ApplyDirectEnergyGainModifierField(object target, FieldInfo modifierField, Parameter energyUsage)
        {
            if (modifierField == null || energyUsage == null)
                return;

            Modifier modifier = modifierField.GetValue(target) as Modifier;
            if (modifier == null)
                return;

            float usage = energyUsage.Value;
            if (usage < 0f)
                return;

            float desiredGain = -usage;
            if (Math.Abs(modifier.Value - desiredGain) > 0.0001f)
                modifier.Value = desiredGain;
        }

        private static void ApplyGenericBatteryConsumers(string reason)
        {
            // DonJ : filet de securite. Si UBOAT renomme un composant electrique,
            // je cherche quand meme les champs Parameter nommes EnergyUsage dans tous les MonoBehaviour.
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null || behaviour is LongSubmergedMenuController)
                    continue;

                ApplyGenericBatteryConsumer(behaviour, reason);
            }
        }

        private static void ApplyGenericBatteryConsumer(object target, string reason)
        {
            if (target == null)
                return;

            Type type = target.GetType();

            foreach (FieldInfo field in GetGenericEnergyUsageFields(type))
            {
                Parameter energyUsage = GetParameterFromField(target, field);
                if (energyUsage == null)
                    continue;

                ApplyEnergyUsageParameter(energyUsage);
                ApplyDirectEnergyGainModifier(target, energyUsage, reason);
                ApplyGenericEnergyModifierFields(target, energyUsage);
            }

            foreach (FieldInfo field in GetGenericParameterCollectionFields(type))
            {
                ParameterCollection parameters = GetParameterCollectionFromField(target, field);
                if (parameters == null)
                    continue;

                ApplyBatteryCapacityParameter(GetParameter(parameters, "EnergyCapacityGain"));
                Parameter energyUsage = GetParameter(parameters, "EnergyUsage");
                ApplyEnergyUsageParameter(energyUsage);
                ApplyDirectEnergyGainModifier(target, energyUsage, reason);
                ApplyGenericEnergyModifierFields(target, energyUsage);
            }
        }

        private static FieldInfo[] GetGenericEnergyUsageFields(Type type)
        {
            FieldInfo[] cached;
            if (GenericEnergyUsageFieldCache.TryGetValue(type, out cached))
                return cached;

            List<FieldInfo> fields = new List<FieldInfo>();
            CollectFields(type, fields, typeof(Parameter), true);
            cached = fields.ToArray();
            GenericEnergyUsageFieldCache[type] = cached;
            return cached;
        }

        private static FieldInfo[] GetGenericParameterCollectionFields(Type type)
        {
            FieldInfo[] cached;
            if (GenericParameterCollectionFieldCache.TryGetValue(type, out cached))
                return cached;

            List<FieldInfo> fields = new List<FieldInfo>();
            CollectFields(type, fields, typeof(ParameterCollection), false);
            cached = fields.ToArray();
            GenericParameterCollectionFieldCache[type] = cached;
            return cached;
        }

        private static FieldInfo[] GetGenericEnergyModifierFields(Type type)
        {
            FieldInfo[] cached;
            if (GenericEnergyModifierFieldCache.TryGetValue(type, out cached))
                return cached;

            List<FieldInfo> fields = new List<FieldInfo>();
            CollectFields(type, fields, typeof(Modifier), false);
            cached = fields.ToArray();
            GenericEnergyModifierFieldCache[type] = cached;
            return cached;
        }

        private static void CollectFields(Type type, List<FieldInfo> fields, Type requiredFieldType, bool energyUsageNameOnly)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo[] declaredFields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in declaredFields)
                {
                    if (field == null || !requiredFieldType.IsAssignableFrom(field.FieldType))
                        continue;

                    if (energyUsageNameOnly && !IsEnergyUsageMemberName(field.Name))
                        continue;

                    if (!energyUsageNameOnly && requiredFieldType == typeof(Modifier) && !IsEnergyModifierMemberName(field.Name))
                        continue;

                    fields.Add(field);
                }
            }
        }

        private static bool IsEnergyUsageMemberName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("EnergyUsage", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsEnergyModifierMemberName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("Energy", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("Modifier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Parameter GetParameterFromField(object target, FieldInfo field)
        {
            try
            {
                return field == null ? null : field.GetValue(target) as Parameter;
            }
            catch
            {
                return null;
            }
        }

        private static ParameterCollection GetParameterCollectionFromField(object target, FieldInfo field)
        {
            try
            {
                return field == null ? null : field.GetValue(target) as ParameterCollection;
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyGenericEnergyModifierFields(object target, Parameter energyUsage)
        {
            if (target == null || energyUsage == null)
                return;

            float usage = energyUsage.Value;
            if (usage < 0f)
                return;

            float desiredGain = -usage;
            foreach (FieldInfo field in GetGenericEnergyModifierFields(target.GetType()))
            {
                try
                {
                    Modifier modifier = field.GetValue(target) as Modifier;
                    if (modifier != null && Math.Abs(modifier.Value - desiredGain) > 0.0001f)
                        modifier.Value = desiredGain;
                }
                catch
                {
                }
            }
        }

        private static Parameter GetParameter(ParameterCollection parameters, string key)
        {
            try
            {
                return parameters.GetParameter(key);
            }
            catch
            {
                return null;
            }
        }

        private static Parameter GetParameterField(object target, string fieldName)
        {
            try
            {
                FieldInfo field = AccessTools.Field(target.GetType(), fieldName);
                return field == null ? null : field.GetValue(target) as Parameter;
            }
            catch
            {
                return null;
            }
        }

        private static void SetScale(Parameter parameter, float scale)
        {
            if (parameter == null)
                return;

            ParameterScalePatchData data;
            if (!ParameterScaleData.TryGetValue(parameter, out data))
            {
                data = new ParameterScalePatchData(parameter.AddScaleModifier(RuntimeScaleModifierName, false));
                ParameterScaleData.Add(parameter, data);
            }

            if (data.ScaleModifier == null)
                return;

            if (Math.Abs(data.ScaleModifier.Value - scale) > 0.0001f)
                data.ScaleModifier.Value = scale;
        }
    }

    internal sealed class ParameterScalePatchData
    {
        public readonly Modifier ScaleModifier;

        public ParameterScalePatchData(Modifier scaleModifier)
        {
            ScaleModifier = scaleModifier;
        }
    }

    internal sealed class ParameterDeltaPatchData
    {
        public readonly Modifier DeltaModifier;

        public ParameterDeltaPatchData(Modifier deltaModifier)
        {
            DeltaModifier = deltaModifier;
        }
    }

    internal sealed class TorpedoGuidancePatchData
    {
        public bool HasOriginalValues;
        public bool GuidanceApplied;
        public bool GuidanceLogged;
        public bool ForcingDetonation;
        public float OriginalGyroAngle;
        public Vector3 OriginalTargetPosition;
        public Vector3 OriginalTargetPositionForReports;
    }

    // DonJ : recalcul de l'oxygene apres chargement/equipage. UBOAT garde parfois un modifier calcule
    // avant les Data Sheets du mod ; je force donc le recalcul puis j'applique le facteur F10.
    internal static class OxygenBreathRecalculator
    {
        private static readonly MethodInfo ValidateOxygenBreathModifierMethod =
            AccessTools.Method(typeof(PlayerShip), "ValidateOxygenBreathModifier");

        public static void Recalculate(PlayerShip ship, string reason)
        {
            if (ship == null || ValidateOxygenBreathModifierMethod == null)
                return;

            try
            {
                // DonJ : je force le jeu a reprendre ma valeur Oxygen Consumption Per Character du fichier General.xlsx.
                ValidateOxygenBreathModifierMethod.Invoke(ship, null);
                LongSubmergedRuntimeApplier.RestoreVanillaOxygenIfNeeded(ship);
                Debug.Log("[LongSubmerged10x] Oxygen breath modifier recalculated after " + reason + ".");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    // DonJ : SuperVitesse ne change pas toutes les allures. Je booste seulement les deux derniers crans avant,
    // le plafond du sous-marin joueur et le multiplicateur de propulseur quand ces crans rapides sont actifs.
    internal static class EngineFastSpeedPatcher
    {
        private const float FastSpeedFactor = 3.5f;
        private const float FastSpeedFuelFactor = 8f;
        private const float PlayerSubmarineMaxSpeed = 45f;
        private const int FastForwardGearCount = 2;
        private const string RuntimeVelocityModifierName = "LongSubmerged10x Player Speed Cap";

        private static readonly FieldInfo ForwardPresetsField =
            AccessTools.Field(typeof(PlayerShipEngine), "forwardPresets");

        private static readonly FieldInfo ExpectedVelocityPerGearField =
            AccessTools.Field(typeof(PlayerShipEngine), "expectedVelocityPerGear");

        private static readonly FieldInfo ExpectedVelocityPerGearUnderwaterField =
            AccessTools.Field(typeof(PlayerShipEngine), "expectedVelocityPerGearUnderwater");

        private static readonly Type EngineSpeedPresetType =
            typeof(PlayerShipEngine).GetNestedType("EngineSpeedPreset", BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo BasePowerField =
            EngineSpeedPresetType == null ? null : AccessTools.Field(EngineSpeedPresetType, "basePower");

        private static readonly FieldInfo FuelConsumptionField =
            EngineSpeedPresetType == null ? null : AccessTools.Field(EngineSpeedPresetType, "fuelConsumptionInLitersPerHour");

        private static readonly FieldInfo ShipPropellersField =
            AccessTools.Field(typeof(Ship), "propellers");

        private static readonly ConditionalWeakTable<PlayerShipEngine, EngineSpeedPatchData> OriginalData =
            new ConditionalWeakTable<PlayerShipEngine, EngineSpeedPatchData>();

        private static readonly ConditionalWeakTable<PlayerShip, ShipRuntimePatchData> ShipRuntimeData =
            new ConditionalWeakTable<PlayerShip, ShipRuntimePatchData>();

        private static readonly ConditionalWeakTable<Propeller, PropellerPatchData> PropellerRuntimeData =
            new ConditionalWeakTable<Propeller, PropellerPatchData>();

        private static readonly HashSet<int> WarnedEngines = new HashSet<int>();

        public static void PatchPlayerShip(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            try
            {
                PatchEngine(ship.DieselEngine, reason + ".DieselEngine");
                PatchEngine(ship.ElectricEngine, reason + ".ElectricEngine");
                PatchShipVelocityCap(ship, reason, true);
                ApplyPropellerSpeedMultiplier(ship, reason, true);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void UpdatePlayerShipRuntime(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            try
            {
                PatchShipVelocityCap(ship, reason, false);
                ApplyPropellerSpeedMultiplier(ship, reason, false);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void PatchEngine(PlayerShipEngine engine, string reason)
        {
            if (engine == null)
                return;

            try
            {
                // DonJ : les champs moteur sont prives dans UBOAT, donc je passe par reflection.
                // Si une version du jeu renomme un champ, je log une seule alerte et je laisse le moteur vanilla.
                if (!FieldsReady())
                {
                    WarnOnce(engine, "champs moteur introuvables, patch vitesse ignore.");
                    return;
                }

                Array forwardPresets = ForwardPresetsField.GetValue(engine) as Array;
                float[] expectedVelocityPerGear = ExpectedVelocityPerGearField.GetValue(engine) as float[];
                float[] expectedVelocityPerGearUnderwater =
                    ExpectedVelocityPerGearUnderwaterField.GetValue(engine) as float[];

                if (forwardPresets == null || forwardPresets.Length < FastForwardGearCount)
                {
                    WarnOnce(engine, "moins de " + FastForwardGearCount + " crans avant, patch vitesse ignore.");
                    return;
                }

                EngineSpeedPatchData data;
                if (!OriginalData.TryGetValue(engine, out data))
                {
                    data = EngineSpeedPatchData.Capture(
                        forwardPresets,
                        expectedVelocityPerGear,
                        expectedVelocityPerGearUnderwater,
                        BasePowerField,
                        FuelConsumptionField
                    );
                    OriginalData.Add(engine, data);
                }

                float speedFactor = GetEffectiveFastSpeedFactor();
                float fuelFactor = GetEffectiveFastFuelFactor(speedFactor);

                // DonJ : je garde une copie des valeurs originales, puis je recalcule depuis ces bases.
                // Comme ca le slider F10 peut monter/descendre sans empiler les multiplicateurs.
                ApplyTopGearBasePower(forwardPresets, data.ForwardBasePower, speedFactor);
                ApplyTopGearFuelConsumption(forwardPresets, data.ForwardFuelConsumption, fuelFactor);
                ApplyTopGearFloatArray(expectedVelocityPerGear, data.ExpectedVelocityPerGear, speedFactor);
                ApplyTopGearFloatArray(expectedVelocityPerGearUnderwater, data.ExpectedVelocityPerGearUnderwater, speedFactor);

                Debug.Log("[LongSubmerged10x] Fast speed patch applied after " + reason + ".");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static bool FieldsReady()
        {
            return ForwardPresetsField != null
                && ExpectedVelocityPerGearField != null
                && ExpectedVelocityPerGearUnderwaterField != null
                && BasePowerField != null
                && FuelConsumptionField != null;
        }

        private static void ApplyTopGearBasePower(Array forwardPresets, float[] originalBasePower, float speedFactor)
        {
            if (forwardPresets == null || originalBasePower == null)
                return;

            int patchCount = Math.Min(FastForwardGearCount, Math.Min(forwardPresets.Length, originalBasePower.Length));
            int firstPatchedGear = forwardPresets.Length - patchCount;

            for (int index = firstPatchedGear; index < forwardPresets.Length; index++)
            {
                object preset = forwardPresets.GetValue(index);
                if (preset == null)
                    continue;

                BasePowerField.SetValue(preset, originalBasePower[index] * speedFactor);
                forwardPresets.SetValue(preset, index);
            }
        }

        private static void ApplyTopGearFuelConsumption(Array forwardPresets, float[] originalFuelConsumption, float fuelFactor)
        {
            if (forwardPresets == null || originalFuelConsumption == null)
                return;

            int patchCount = Math.Min(FastForwardGearCount, Math.Min(forwardPresets.Length, originalFuelConsumption.Length));
            int firstPatchedGear = forwardPresets.Length - patchCount;

            for (int index = firstPatchedGear; index < forwardPresets.Length; index++)
            {
                object preset = forwardPresets.GetValue(index);
                if (preset == null)
                    continue;

                FuelConsumptionField.SetValue(preset, originalFuelConsumption[index] * fuelFactor);
                forwardPresets.SetValue(preset, index);
            }
        }

        private static void ApplyTopGearFloatArray(float[] target, float[] original, float speedFactor)
        {
            if (target == null || original == null)
                return;

            int patchCount = Math.Min(FastForwardGearCount, Math.Min(target.Length, original.Length));
            int firstPatchedGear = target.Length - patchCount;

            for (int index = firstPatchedGear; index < target.Length; index++)
                target[index] = original[index] * speedFactor;
        }

        private static void PatchShipVelocityCap(PlayerShip ship, string reason, bool verboseLog)
        {
            if (ship == null || ship.Blueprint == null || ship.Blueprint.Velocity == null)
                return;

            ShipRuntimePatchData data;
            if (!ShipRuntimeData.TryGetValue(ship, out data))
            {
                float originalVelocity = ship.Blueprint.Velocity;
                Modifier modifier = null;

                if (originalVelocity < PlayerSubmarineMaxSpeed)
                    modifier = ship.Blueprint.Velocity.AddDeltaModifier(RuntimeVelocityModifierName, false);

                data = new ShipRuntimePatchData(originalVelocity, modifier);
                ShipRuntimeData.Add(ship, data);
            }

            if (data.VelocityModifier == null)
                return;

            float effectiveSpeedFactor = GetEffectiveFastSpeedFactor();
            float desiredMaxSpeed = effectiveSpeedFactor <= 1.0001f
                ? data.OriginalVelocity
                : Math.Max(data.OriginalVelocity, PlayerSubmarineMaxSpeed * (effectiveSpeedFactor / FastSpeedFactor));
            float desiredDelta = desiredMaxSpeed - data.OriginalVelocity;
            if (desiredDelta < 0f)
                desiredDelta = 0f;

            if (Math.Abs(data.VelocityModifier.Value - desiredDelta) > 0.001f)
                data.VelocityModifier.Value = desiredDelta;

            if (verboseLog)
            {
                Debug.Log(
                    "[LongSubmerged10x] Player ship speed cap patched after "
                    + reason
                    + ": "
                    + data.OriginalVelocity
                    + " -> "
                    + desiredMaxSpeed
                    + " km/h."
                );
            }
        }

        private static void ApplyPropellerSpeedMultiplier(PlayerShip ship, string reason, bool verboseLog)
        {
            if (ship == null)
                return;

            Propeller[] propellers = ShipPropellersField == null
                ? ship.Propellers
                : ShipPropellersField.GetValue(ship) as Propeller[];

            if (propellers == null || propellers.Length == 0)
                return;

            bool fastForwardGear = IsActiveEngineInFastForwardGear(ship);
            float appliedFactor = fastForwardGear ? GetEffectiveFastSpeedFactor() : 1f;
            int changedCount = 0;

            foreach (Propeller propeller in propellers)
            {
                if (propeller == null)
                    continue;

                PropellerPatchData data;
                if (!PropellerRuntimeData.TryGetValue(propeller, out data))
                {
                    data = new PropellerPatchData(propeller.PowerMultiplier);
                    PropellerRuntimeData.Add(propeller, data);
                }

                float desiredMultiplier = data.OriginalPowerMultiplier * appliedFactor;

                if (Math.Abs(propeller.PowerMultiplier - desiredMultiplier) > 0.001f)
                {
                    propeller.PowerMultiplier = desiredMultiplier;
                    changedCount++;
                }
            }

            if (verboseLog && changedCount > 0)
            {
                Debug.Log(
                    "[LongSubmerged10x] Propeller multiplier "
                    + appliedFactor
                    + " applied after "
                    + reason
                    + "."
                );
            }
        }

        private static bool IsActiveEngineInFastForwardGear(PlayerShip ship)
        {
            PlayerShipEngine engine = ship.ActiveEngine;
            if (engine == null || engine.GearIndex <= 0 || ForwardPresetsField == null)
                return false;

            Array forwardPresets = ForwardPresetsField.GetValue(engine) as Array;
            if (forwardPresets == null || forwardPresets.Length < FastForwardGearCount)
                return false;

            int firstFastGearIndex = forwardPresets.Length - FastForwardGearCount + 1;
            return engine.GearIndex >= firstFastGearIndex;
        }

        private static float GetEffectiveFastSpeedFactor()
        {
            if (!LongSubmergedRuntimeSettings.SuperSpeed)
                return 1f;

            return LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.SpeedFactor);
        }

        private static float GetEffectiveFastFuelFactor(float speedFactor)
        {
            if (!LongSubmergedRuntimeSettings.SuperSpeed || speedFactor <= 1.0001f)
                return 1f;

            float referenceSpeedFactor = Math.Max(1.0001f, FastSpeedFactor);
            float normalized = (speedFactor - 1f) / (referenceSpeedFactor - 1f);
            return Math.Max(1f, 1f + normalized * (FastSpeedFuelFactor - 1f));
        }

        private static void WarnOnce(PlayerShipEngine engine, string message)
        {
            int instanceId = engine.GetInstanceID();
            if (WarnedEngines.Add(instanceId))
                Debug.LogWarning("[LongSubmerged10x] " + message);
        }
    }

    internal sealed class EngineSpeedPatchData
    {
        public readonly float[] ForwardBasePower;
        public readonly float[] ForwardFuelConsumption;
        public readonly float[] ExpectedVelocityPerGear;
        public readonly float[] ExpectedVelocityPerGearUnderwater;

        private EngineSpeedPatchData(
            float[] forwardBasePower,
            float[] forwardFuelConsumption,
            float[] expectedVelocityPerGear,
            float[] expectedVelocityPerGearUnderwater)
        {
            ForwardBasePower = forwardBasePower;
            ForwardFuelConsumption = forwardFuelConsumption;
            ExpectedVelocityPerGear = expectedVelocityPerGear;
            ExpectedVelocityPerGearUnderwater = expectedVelocityPerGearUnderwater;
        }

        public static EngineSpeedPatchData Capture(
            Array forwardPresets,
            float[] expectedVelocityPerGear,
            float[] expectedVelocityPerGearUnderwater,
            FieldInfo basePowerField,
            FieldInfo fuelConsumptionField)
        {
            float[] basePower = new float[forwardPresets.Length];
            float[] fuelConsumption = new float[forwardPresets.Length];

            for (int index = 0; index < forwardPresets.Length; index++)
            {
                object preset = forwardPresets.GetValue(index);
                if (preset == null)
                    continue;

                object rawValue = basePowerField.GetValue(preset);
                if (rawValue is float)
                    basePower[index] = (float)rawValue;

                object rawFuelConsumption = fuelConsumptionField.GetValue(preset);
                if (rawFuelConsumption is float)
                    fuelConsumption[index] = (float)rawFuelConsumption;
            }

            return new EngineSpeedPatchData(
                basePower,
                fuelConsumption,
                CloneFloatArray(expectedVelocityPerGear),
                CloneFloatArray(expectedVelocityPerGearUnderwater)
            );
        }

        private static float[] CloneFloatArray(float[] source)
        {
            if (source == null)
                return null;

            float[] clone = new float[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }
    }

    internal sealed class ShipRuntimePatchData
    {
        public readonly float OriginalVelocity;
        public readonly Modifier VelocityModifier;

        public ShipRuntimePatchData(float originalVelocity, Modifier velocityModifier)
        {
            OriginalVelocity = originalVelocity;
            VelocityModifier = velocityModifier;
        }
    }

    internal sealed class PropellerPatchData
    {
        public readonly float OriginalPowerMultiplier;

        public PropellerPatchData(float originalPowerMultiplier)
        {
            OriginalPowerMultiplier = originalPowerMultiplier;
        }
    }

    // DonJ : hooks Harmony courts et delegues. Chaque hook appelle une methode robuste du runtime,
    // ce qui limite le risque de casser UBOAT si un objet arrive partiellement initialise.
    [HarmonyPatch(typeof(PlayerShip), "Awake")]
    internal static class PlayerShipAwakePatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "PlayerShip.Awake");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "OnAfterDeserialize")]
    internal static class PlayerShipOnAfterDeserializePatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "PlayerShip.OnAfterDeserialize");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Update")]
    internal static class PlayerShipUpdatePatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryResource(__instance, "PlayerShip.Update");
        }
    }

    [HarmonyPatch(typeof(Resource), "UpdateAmount")]
    internal static class ResourceUpdateAmountBatteryPatch
    {
        private static bool Prefix(Resource __instance)
        {
            return !LongSubmergedRuntimeApplier.TryUpdateBatteryResourceAmount(__instance, "Resource.UpdateAmount");
        }
    }

    [HarmonyPatch(typeof(ResourceGUI), "GetTooltipContents")]
    internal static class ResourceGuiGetTooltipContentsPatch
    {
        private static bool Prefix(ResourceGUI __instance, ref string __result)
        {
            Resource resource = LongSubmergedRuntimeApplier.GetResourceFromGui(__instance);
            if (!LongSubmergedRuntimeApplier.ShouldSuppressBatteryDepletionUi(resource, "ResourceGUI.GetTooltipContents"))
                return true;

            __result = LongSubmergedRuntimeApplier.BuildInfiniteBatteryTooltip(resource);
            return false;
        }
    }

    [HarmonyPatch(typeof(ResourceGUI), "UpdateDisplayedValue")]
    internal static class ResourceGuiUpdateDisplayedValuePatch
    {
        private static void Prefix(ResourceGUI __instance)
        {
            LongSubmergedRuntimeApplier.TryMaintainBatteryResource(
                LongSubmergedRuntimeApplier.GetResourceFromGui(__instance),
                "ResourceGUI.UpdateDisplayedValue"
            );
        }
    }

    [HarmonyPatch(typeof(DepletingResourceNotification), "DoUpdate")]
    internal static class DepletingResourceNotificationDoUpdatePatch
    {
        private static bool Prefix(DepletingResourceNotification __instance, ref float __result)
        {
            Resource resource = LongSubmergedRuntimeApplier.GetResourceFromDepletingNotification(__instance);
            if (!LongSubmergedRuntimeApplier.ShouldSuppressBatteryDepletionUi(resource, "DepletingResourceNotification.DoUpdate"))
                return true;

            __result = 5f;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "ValidateTargetVelocity")]
    internal static class PlayerShipValidateTargetVelocityPatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            EngineFastSpeedPatcher.UpdatePlayerShipRuntime(__instance, "PlayerShip.ValidateTargetVelocity");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "SavesManagerOnLoaded")]
    internal static class PlayerShipSavesManagerOnLoadedPatch
    {
        private static void Postfix(PlayerShip __instance, Queue<Action> __0)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "SavesManagerOnLoaded");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Crew_Added")]
    internal static class PlayerShipCrewAddedPatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "Crew_Added");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Crew_Removed")]
    internal static class PlayerShipCrewRemovedPatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "Crew_Removed");
        }
    }

    [HarmonyPatch(typeof(PlayerShipEngine), "Awake")]
    internal static class PlayerShipEngineAwakePatch
    {
        private static void Postfix(PlayerShipEngine __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "PlayerShipEngine.Awake");
            EngineFastSpeedPatcher.PatchEngine(__instance, "PlayerShipEngine.Awake");
        }
    }

    [HarmonyPatch(typeof(PlayerShipEngine), "OnAfterDeserialize")]
    internal static class PlayerShipEngineOnAfterDeserializePatch
    {
        private static void Postfix(PlayerShipEngine __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "PlayerShipEngine.OnAfterDeserialize");
            EngineFastSpeedPatcher.PatchEngine(__instance, "PlayerShipEngine.OnAfterDeserialize");
        }
    }

    [HarmonyPatch(typeof(PlayerShipEngine), "SavesManagerOnLoaded")]
    internal static class PlayerShipEngineSavesManagerOnLoadedPatch
    {
        private static void Postfix(PlayerShipEngine __instance, Queue<Action> __0)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "PlayerShipEngine.SavesManagerOnLoaded");
            EngineFastSpeedPatcher.PatchEngine(__instance, "PlayerShipEngine.SavesManagerOnLoaded");
        }
    }

    [HarmonyPatch(typeof(AccumulatorsUpgrade), "Start")]
    internal static class AccumulatorsUpgradeStartPatch
    {
        private static void Postfix(AccumulatorsUpgrade __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "AccumulatorsUpgrade.Start");
        }
    }

    [HarmonyPatch(typeof(DivingPlanesStation), "Awake")]
    internal static class DivingPlanesStationAwakePatch
    {
        private static void Postfix(DivingPlanesStation __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "DivingPlanesStation.Awake");
        }
    }

    [HarmonyPatch(typeof(DivingPlanesStation), "UpdateModifiers")]
    internal static class DivingPlanesStationUpdateModifiersPatch
    {
        private static void Postfix(DivingPlanesStation __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "DivingPlanesStation.UpdateModifiers");
        }
    }

    [HarmonyPatch(typeof(AirCompressor), "OnEnable")]
    internal static class AirCompressorOnEnablePatch
    {
        private static void Postfix(AirCompressor __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "AirCompressor.OnEnable");
        }
    }

    [HarmonyPatch(typeof(AirCompressor), "EnergyUsage_Changed")]
    internal static class AirCompressorEnergyUsageChangedPatch
    {
        private static void Postfix(AirCompressor __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "AirCompressor.EnergyUsage_Changed");
        }
    }

    [HarmonyPatch(typeof(Gyrocompass), "ApplyModifiers")]
    internal static class GyrocompassApplyModifiersPatch
    {
        private static void Postfix(Gyrocompass __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "Gyrocompass.ApplyModifiers");
        }
    }

    [HarmonyPatch(typeof(TrimPump), "OnEnable")]
    internal static class TrimPumpOnEnablePatch
    {
        private static void Postfix(TrimPump __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "TrimPump.OnEnable");
        }
    }

    [HarmonyPatch(typeof(Ventilation), "OnEnable")]
    internal static class VentilationOnEnablePatch
    {
        private static void Postfix(Ventilation __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "Ventilation.OnEnable");
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), "Start")]
    internal static class StoredTorpedoStartPatch
    {
        private static void Postfix(StoredTorpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyStoredTorpedo(__instance, "StoredTorpedo.Start");
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), "ApplyWarmUpModifier")]
    internal static class StoredTorpedoApplyWarmUpModifierPatch
    {
        private static void Postfix(StoredTorpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyStoredTorpedo(__instance, "StoredTorpedo.ApplyWarmUpModifier");
        }
    }

    [HarmonyPatch(typeof(Torpedo), "Awake")]
    internal static class TorpedoAwakePatch
    {
        private static void Postfix(Torpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyLaunchedTorpedo(__instance, "Torpedo.Awake");
        }
    }

    [HarmonyPatch(typeof(Torpedo), "FixedUpdate")]
    internal static class TorpedoFixedUpdatePatch
    {
        private static void Prefix(Torpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyLaunchedTorpedo(__instance, "Torpedo.FixedUpdate");
        }
    }

    [HarmonyPatch(typeof(Torpedo), "Detonate")]
    internal static class TorpedoDetonatePatch
    {
        private static void Prefix(Torpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyLaunchedTorpedo(__instance, "Torpedo.Detonate");
        }
    }
}

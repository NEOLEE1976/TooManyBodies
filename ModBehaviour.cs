using Duckov.Scenes;
using Duckov.Modding;
//using HarmonyLib;
using ItemStatsSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TooManyBodies
{
    [System.Serializable]
    public class TooManyBodiesConfig
    {
        //这里是默认配置，懒得做安全所以不能删

        //搜索半径（米）
        public float searchRadius = 15f;

        //清理提示
        public bool showNotifications = true;

        //启用自动清理
        public bool enableAutoCleanup = false;

        //清理间隔时间（秒）
        public float cleanupInterval = 5f;

        //快捷键：开始清理
        public KeyCode triggerKey = KeyCode.B;

        //删除动画
        public bool playDestroyAnimation = true;

        //删除动画时长
        public float destroyDelay = 0.3f;


        public string configToken = "toomanybodies_v1";
    }
    internal class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        public static string MOD_NAME = "TooManyBodies";

        private TooManyBodiesConfig config = new TooManyBodiesConfig();
        private static string persistentConfigPath => Path.Combine(Application.streamingAssetsPath, "TooManyBodiesConfig.txt");

        private CharacterMainControl? characterControl;
        private float lastCleanupTime = -999f;
        private static FieldInfo? _inventoryRefField;

        void OnEnable()
        {
            LevelManager.OnAfterLevelInitialized += OnLevelInitialized;

            if (_inventoryRefField == null)
            {
                _inventoryRefField = typeof(InteractableLootbox)
                    .GetField("inventoryReference",
                        BindingFlags.NonPublic | BindingFlags.Instance);
            }

            LoadConfigFromFile();

            // 尝试加载 ModConfig 配置
            if (ModConfigAPI.IsAvailable())
            {
                SetupModConfig();
                LoadConfigFromModConfig();
            }

            ModManager.OnModActivated += OnModActivated;
        }

        void OnDisable()
        {
            LevelManager.OnAfterLevelInitialized -= OnLevelInitialized;
            ModManager.OnModActivated -= OnModActivated;
            ModConfigAPI.SafeRemoveOnOptionsChangedDelegate(OnModConfigOptionsChanged);
        }

        void OnLevelInitialized()
        {
            characterControl = LevelManager.Instance.MainCharacter;
            lastCleanupTime = Time.time;
        }

        void Update()
        {
            if (characterControl == null)
            {
                characterControl = LevelManager.Instance?.MainCharacter;
                return;
            }

            if (Input.GetKeyDown(config.triggerKey))
            {
                PerformCleanup();
            }

            // 自动清理
            if (config.enableAutoCleanup && Time.time - lastCleanupTime >= config.cleanupInterval)
            {
                PerformCleanup();
                lastCleanupTime = Time.time;
            }
        }

        void PerformCleanup()
        {
            if (characterControl == null)
                return;

            lastCleanupTime = Time.time;

            // 获取范围内的空盒子
            List<InteractableLootbox> boxesToClean = GetEmptyBoxesInRange(
                characterControl.transform.position,
                config.searchRadius
            );

            int destroyedCount = 0;
            foreach (var box in boxesToClean)
            {
                StartCoroutine(DestroyBoxWithAnimation(box));
                destroyedCount++;
            }

            if (config.showNotifications && destroyedCount > 0)
            {
                string message = $"清理完成！删除 {destroyedCount} 个空盒子";
                characterControl.PopText(message);
            }
        }
        private List<InteractableLootbox> GetEmptyBoxesInRange(Vector3 centerPos, float radius)
        {
            List<InteractableLootbox> result = new List<InteractableLootbox>();

            if (MultiSceneCore.Instance == null)
                return result;

            InteractableLootbox[] allBoxes = MultiSceneCore.Instance.gameObject
                .GetComponentsInChildren<InteractableLootbox>(includeInactive: false);

            if (allBoxes != null && allBoxes.Length > 0)
            {
                foreach (var box in allBoxes)
                {
                    if (box == null) continue;

                    if (Vector3.Distance(box.transform.position, centerPos) <= radius
                        && IsBoxEmpty(box))
                    {
                        result.Add(box);
                    }
                }
            }

            return result;
        }

        private List<InteractableLootbox> GetAllLootboxesInScene()
        {
            List<InteractableLootbox> result = new List<InteractableLootbox>();

            if (MultiSceneCore.Instance == null)
                return result;

            InteractableLootbox[] allBoxes = MultiSceneCore.Instance.gameObject
                .GetComponentsInChildren<InteractableLootbox>(includeInactive: false);

            if (allBoxes != null && allBoxes.Length > 0)
            {
                result.AddRange(allBoxes.Where(box => box != null));
            }

            return result;
        }

        private List<InteractableLootbox> FilterBoxesByDistance(
            List<InteractableLootbox> boxes,
            Vector3 centerPosition,
            float radius)
        {
            return boxes.Where(box =>
                box != null &&
                Vector3.Distance(box.transform.position, centerPosition) <= radius
            ).ToList();
        }
        private bool IsBoxEmpty(InteractableLootbox box)
        {
            if (box == null)
                return true;

            try
            {
                var inventoryRef = _inventoryRefField?.GetValue(box) as Inventory;

                if (inventoryRef == null)
                    return true;

                return inventoryRef.IsEmpty();
            }
            catch
            {
                return false;
            }
        }

        private IEnumerator DestroyBoxWithAnimation(InteractableLootbox boxComponent)
        {
            if (boxComponent == null)
                yield break;

            GameObject box = boxComponent.gameObject;

            if (config.playDestroyAnimation)
            {
                float timer = 0f;
                Transform boxTransform = box.transform;
                Vector3 originalScale = boxTransform.localScale;

                while (timer < config.destroyDelay)
                {
                    timer += Time.deltaTime;
                    float progress = timer / config.destroyDelay;

                    boxTransform.localScale = originalScale * (1f - progress * 0.5f);

                    yield return null;
                }
            }
            else
            {
                yield return new WaitForSeconds(config.destroyDelay);
            }

            GameObject.Destroy(box);
        }
        #region Config Management (配置管理)

        // 从本地文件加载配置
        private void LoadConfigFromFile()
        {
            try
            {
                if (File.Exists(persistentConfigPath))
                {
                    string json = File.ReadAllText(persistentConfigPath);
                    var loadedConfig = JsonUtility.FromJson<TooManyBodiesConfig>(json);
                    if (loadedConfig != null)
                    {
                        config = loadedConfig;
                        Debug.Log("TooManyBodies: Config loaded from file");
                    }
                }
                else
                {
                    Debug.Log("TooManyBodies: Config file not found, using default config");
                    SaveConfigToFile();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"TooManyBodies: Failed to load config from file: {e}");
            }
        }
        
        // 保存配置到本地文件
        private void SaveConfigToFile()
        {
            try
            {
                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(persistentConfigPath, json);
                Debug.Log("TooManyBodies: Config saved to file");
            }
            catch (Exception e)
            {
                Debug.LogError($"TooManyBodies: Failed to save config to file: {e}");
            }
        }
        private void OnModActivated(ModInfo info, Duckov.Modding.ModBehaviour behaviour)
        {
            if (info.name == ModConfigAPI.ModConfigName)
            {
                Debug.Log("TooManyBodies: ModConfig activated!");
                SetupModConfig();
                LoadConfigFromModConfig();
            }
        }

        private void SetupModConfig()
        {
            if (!ModConfigAPI.IsAvailable())
            {
                Debug.LogWarning("TooManyBodies: ModConfig not available");
                return;
            }

            Debug.Log("TooManyBodies: Setting up ModConfig configuration");

            ModConfigAPI.SafeAddOnOptionsChangedDelegate(OnModConfigOptionsChanged);

            // b本地化
            SystemLanguage[] chineseLanguages = {
                SystemLanguage.Chinese,
                SystemLanguage.ChineseSimplified,
                SystemLanguage.ChineseTraditional
            };
            bool isChinese = System.Globalization.CultureInfo.CurrentCulture.Name.StartsWith("zh");

            // 搜索半径
            ModConfigAPI.SafeAddInputWithSlider(
                MOD_NAME,
                "searchRadius",
                isChinese ? "搜索半径（米）" : "Search Radius (meters)",
                typeof(float),
                config.searchRadius,
                new Vector2(5f, 50f)
            );

            // 清理提示
            ModConfigAPI.SafeAddBoolDropdownList(
                MOD_NAME,
                "showNotifications",
                isChinese ? "显示清理提示" : "Show Cleanup Notifications",
                config.showNotifications
            );

            // 启用自动清理
            ModConfigAPI.SafeAddBoolDropdownList(
                MOD_NAME,
                "enableAutoCleanup",
                isChinese ? "启用自动清理" : "Enable Auto Cleanup",
                config.enableAutoCleanup
            );

            // 清理间隔
            ModConfigAPI.SafeAddInputWithSlider(
                MOD_NAME,
                "cleanupInterval",
                isChinese ? "清理间隔时间（秒）" : "Cleanup Interval (seconds)",
                typeof(float),
                config.cleanupInterval,
                new Vector2(1f, 30f)
            );

            // 快捷键
            ModConfigAPI.SafeAddInputWithSlider(
                MOD_NAME,
                "triggerKey",
                isChinese ? "快捷键（B为默认）" : "Trigger Key (B as default)",
                typeof(string),
                config.triggerKey.ToString(),
                null
            );

            // 删除动画
            ModConfigAPI.SafeAddBoolDropdownList(
                MOD_NAME,
                "playDestroyAnimation",
                isChinese ? "播放删除动画" : "Play Destroy Animation",
                config.playDestroyAnimation
            );

            // 删除动画时长
            ModConfigAPI.SafeAddInputWithSlider(
                MOD_NAME,
                "destroyDelay",
                isChinese ? "删除动画时长（秒）" : "Destroy Animation Duration (seconds)",
                typeof(float),
                config.destroyDelay,
                new Vector2(0.1f, 2f)
            );

            Debug.Log("TooManyBodies: ModConfig setup completed");
        }

        private void OnModConfigOptionsChanged(string key)
        {
            if (!key.StartsWith(MOD_NAME + "_"))
                return;

            Debug.Log($"TooManyBodies: Config changed - {key}");

            LoadConfigFromModConfig();

            SaveConfigToFile();
        }

        private void LoadConfigFromModConfig()
        {
            config.searchRadius = ModConfigAPI.SafeLoad<float>(MOD_NAME, "searchRadius", config.searchRadius);
            config.showNotifications = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "showNotifications", config.showNotifications);
            config.enableAutoCleanup = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "enableAutoCleanup", config.enableAutoCleanup);
            config.cleanupInterval = ModConfigAPI.SafeLoad<float>(MOD_NAME, "cleanupInterval", config.cleanupInterval);
            config.playDestroyAnimation = ModConfigAPI.SafeLoad<bool>(MOD_NAME, "playDestroyAnimation", config.playDestroyAnimation);
            config.destroyDelay = ModConfigAPI.SafeLoad<float>(MOD_NAME, "destroyDelay", config.destroyDelay);

            string triggerKeyStr = ModConfigAPI.SafeLoad<string>(MOD_NAME, "triggerKey", config.triggerKey.ToString());
            if (System.Enum.TryParse<KeyCode>(triggerKeyStr, out KeyCode parsedKey))
            {
                config.triggerKey = parsedKey;
            }
        }

        #endregion
    }
}
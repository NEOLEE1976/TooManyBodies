using Duckov.Scenes;
using HarmonyLib;
using ItemStatsSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TooManyBodies
{
    internal class ModBehaviour:Duckov.Modding.ModBehaviour
    {

        //搜索半径（米）
        [SerializeField]
        public float searchRadius = 15f;

        //清理提示
        [SerializeField]
        public bool showNotifications = true;
        
        //清理间隔时间（秒）
        [SerializeField]
        public float cleanupInterval = 5f;

        //快捷键：开始清理
        [SerializeField]
        public KeyCode triggerKey = KeyCode.B;

        //删除动画
        [SerializeField]
        public bool playDestroyAnimation = true;

        //删除动画时长
        [SerializeField]
        public float destroyDelay = 0.3f;



        private CharacterMainControl? characterControl;
        private float lastCleanupTime = -999f;
        private static FieldInfo _inventoryRefField;

        void OnEnable()
        {
            LevelManager.OnAfterLevelInitialized += OnLevelInitialized;

            if (_inventoryRefField == null)
            {
                _inventoryRefField = typeof(InteractableLootbox)
                    .GetField("inventoryReference",
                        BindingFlags.NonPublic | BindingFlags.Instance);
            }
        }

        void OnDisable()
        {
            LevelManager.OnAfterLevelInitialized -= OnLevelInitialized;
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

            if (Input.GetKeyDown(triggerKey))
            {
                PerformCleanup();
            }

            // 自动清理
            // if (Time.time - lastCleanupTime >= cleanupInterval)
            // {
            //     PerformCleanup();
            //     lastCleanupTime = Time.time;
            // }
        }

        void PerformCleanup()
        {
            if (characterControl == null)
                return;

            lastCleanupTime = Time.time;

            // 获取范围内的空盒子
            List<InteractableLootbox> boxesToClean = GetEmptyBoxesInRange(
                characterControl.transform.position,
                searchRadius
            );

            int destroyedCount = 0;
            foreach (var box in boxesToClean)
            {
                StartCoroutine(DestroyBoxWithAnimation(box));
                destroyedCount++;
            }

            if (showNotifications && destroyedCount > 0)
            {
                string message = $"清理完成！删除 {destroyedCount} 个空盒子";
                characterControl.PopText(message, 10f);
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
                var inventoryRef = (Inventory)_inventoryRefField?.GetValue(box);

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

            if (playDestroyAnimation)
            {
                float timer = 0f;
                Transform boxTransform = box.transform;
                Vector3 originalScale = boxTransform.localScale;

                while (timer < destroyDelay)
                {
                    timer += Time.deltaTime;
                    float progress = timer / destroyDelay;

                    boxTransform.localScale = originalScale * (1f - progress * 0.5f);

                    yield return null;
                }
            }
            else
            {
                yield return new WaitForSeconds(destroyDelay);
            }

            GameObject.Destroy(box);
        }
    }
}


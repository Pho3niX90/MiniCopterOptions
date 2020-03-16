using System;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins {
    [Info("Mini-Copter Options", "Pho3niX90", "1.0.5")]
    [Description("Provide a number of additional options for Mini-Copters, including storage and seats.")]
    class MiniCopterOptions : RustPlugin {
        #region Prefab Modifications

        private readonly string storagePrefab = "assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab";
        private readonly string storageLargePrefab = "assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab";

        void AddLargeStorageBox(MiniCopter copter) {
            //sides,negative left | up and down
            if (config.storageLargeContainers == 1) {
                AddStorageBox(copter, storageLargePrefab, new Vector3(0.0f, 0.05f, -1.05f));
            } else if (config.storageLargeContainers >= 2) {
                AddStorageBox(copter, storageLargePrefab, new Vector3(-0.5f, 0.05f, -1.05f));
                AddStorageBox(copter, storageLargePrefab, new Vector3(0.5f, 0.05f, -1.05f));
            }
        }

        void AddRearStorageBox(MiniCopter copter) {
            AddStorageBox(copter, storagePrefab, new Vector3(0, 0.75f, -1f));
        }

        void AddSideStorageBoxes(MiniCopter copter) {
            AddStorageBox(copter, storagePrefab, new Vector3(0.6f, 0.3f, -0.35f));
            AddStorageBox(copter, storagePrefab, new Vector3(-0.6f, 0.3f, -0.35f));
        }

        void AddStorageBox(MiniCopter copter, string prefab, Vector3 position) {
            BaseEntity box = GameManager.server.CreateEntity(prefab, copter.transform.position) as BaseEntity;

            box.SetParent(copter);
            box.Spawn();
            box.transform.localPosition = position;
            box.SendNetworkUpdateImmediate(true);
        }

        void RestoreMiniCopter(MiniCopter copter, bool removeStorage = false) {
            copter.fuelPerSec = copterDefaults.fuelPerSecond;
            copter.liftFraction = copterDefaults.liftFraction;
            copter.torqueScale = copterDefaults.torqueScale;

            if (removeStorage) {
                foreach (var child in copter.children.ToList()) {
                    if (child.name == storagePrefab || child.name == storageLargePrefab) {
                        copter.RemoveChild(child);
                        child.Kill();
                    }
                }
            }
        }

        void ModifyMiniCopter(MiniCopter copter, bool storage = false) {
            copter.fuelPerSec = config.fuelPerSec;
            copter.liftFraction = config.liftFraction;
            copter.torqueScale = new Vector3(config.torqueScalePitch, config.torqueScaleYaw, config.torqueScaleRoll);

            if (storage) AddLargeStorageBox(copter);

            if (storage)
                switch (config.storageContainers) {
                    case 1:
                        AddRearStorageBox(copter);
                        break;
                    case 2:
                        AddSideStorageBoxes(copter);
                        break;
                    case 3:
                        AddRearStorageBox(copter);
                        AddSideStorageBoxes(copter);
                        break;
                }
        }

        void StoreMiniCopterDefaults(MiniCopter copter) {
            copterDefaults = new MiniCopterDefaults {
                fuelPerSecond = copter.fuelPerSec,
                liftFraction = copter.liftFraction,
                torqueScale = copter.torqueScale
            };
        }

        #endregion
        #region Hooks

        void OnServerInitialized() {
            PrintWarning("Applying settings except storage modifications to existing MiniCopters.");
            foreach (var copter in UnityEngine.Object.FindObjectsOfType<MiniCopter>()) {
                // Nab the default values off the first minicopter.
                if (copterDefaults.Equals(default(MiniCopterDefaults))) {
                    StoreMiniCopterDefaults(copter);
                }

                ModifyMiniCopter(copter, config.reloadStorage);
            }
        }

        void OnEntitySpawned(BaseNetworkable entity) {
            if (entity == null || !(entity is MiniCopter) || !entity.ShortPrefabName.Equals("minicopter.entity")) return;
            var minicopter = entity as MiniCopter;

            // Only add storage on spawn so we don't stack or mess with
            // existing player storage containers. 
            ModifyMiniCopter(minicopter, true);
        }

        void Unload() {
            foreach (var copter in UnityEngine.Object.FindObjectsOfType<MiniCopter>()) {
                RestoreMiniCopter(copter, config.reloadStorage);
            }
        }

        #endregion
        #region Configuration

        private class MiniCopterOptionsConfig {
            // Populated with Rust defaults.
            public float fuelPerSec = 0.25f;
            public float liftFraction = 0.25f;
            public float torqueScalePitch = 400f;
            public float torqueScaleYaw = 400f;
            public float torqueScaleRoll = 200f;

            public int storageContainers = 0;
            public int storageLargeContainers = 0;
            public bool restoreDefaults = true;
            public bool reloadStorage = false;

            // Plugin reference
            private MiniCopterOptions plugin;

            public MiniCopterOptionsConfig(MiniCopterOptions plugin) {
                this.plugin = plugin;

                GetConfig(ref fuelPerSec, "Fuel per Second");
                GetConfig(ref liftFraction, "Lift Fraction");
                GetConfig(ref torqueScalePitch, "Pitch Torque Scale");
                GetConfig(ref torqueScaleYaw, "Yaw Torque Scale");
                GetConfig(ref torqueScaleRoll, "Roll Torque Scale");
                GetConfig(ref storageContainers, "Storage Containers");
                GetConfig(ref storageLargeContainers, "Large Storage Containers");
                GetConfig(ref restoreDefaults, "Restore Defaults");
                GetConfig(ref reloadStorage, "Reload Storage");

                plugin.SaveConfig();
            }

            private void GetConfig<T>(ref T variable, params string[] path) {
                if (path.Length == 0) return;

                if (plugin.Config.Get(path) == null) {
                    SetConfig(ref variable, path);
                    plugin.PrintWarning($"Added field to config: {string.Join("/", path)}");
                }

                variable = (T)Convert.ChangeType(plugin.Config.Get(path), typeof(T));
            }

            private void SetConfig<T>(ref T variable, params string[] path) => plugin.Config.Set(path.Concat(new object[] { variable }).ToArray());
        }

        protected override void LoadDefaultConfig() => PrintWarning("Generating new configuration file.");

        private MiniCopterOptionsConfig config;

        struct MiniCopterDefaults {
            public float fuelPerSecond;
            public float liftFraction;
            public Vector3 torqueScale;
        }

        MiniCopterDefaults copterDefaults;

        private void Init() {
            config = new MiniCopterOptionsConfig(this);

            if (config.storageContainers > 3) {
                PrintWarning($"Storage Containers configuration value {config.storageContainers} exceeds the maximum, setting to 3.");
                config.storageContainers = 3;
            } else if (config.storageContainers < 0) {
                PrintWarning($"Storage Containers cannot be a negative value, setting to 0.");
                config.storageContainers = 0;
            }

            if (config.storageLargeContainers > 2) {
                PrintWarning($"Large Storage Containers configuration value {config.storageLargeContainers} exceeds the maximum, setting to 2.");
                config.storageLargeContainers = 2;
            } else if (config.storageLargeContainers < 0) {
                PrintWarning($"Large Storage Containers cannot be a negative value, setting to 0.");
                config.storageLargeContainers = 0;
            }
        }

        #endregion
    }
}

using System;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins {
    [Info("Mini-Copter Options", "Pho3niX90", "1.1.8")]
    [Description("Provide a number of additional options for Mini-Copters, including storage and seats.")]
    class MiniCopterOptions : RustPlugin {
        static MiniCopterOptions _instance;
        RaycastHit raycastHit;
        #region Prefab Modifications

        private readonly string storagePrefab = "assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab";
        private readonly string storageLargePrefab = "assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab";
        private readonly string autoturretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private readonly string switchPrefab = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private readonly string searchLightPrefab = "assets/prefabs/deployable/search light/searchlight.deployed.prefab";
        private readonly string batteryPrefab = "assets/prefabs/deployable/playerioents/batteries/smallrechargablebattery.deployed.prefab";

        void Loaded() {
            _instance = this;
        }

        void OnEntityDismounted(BaseMountable entity, BasePlayer player) {
            if (config.flyHackPause > 0 && entity is MiniCopter)
                player.PauseFlyHackDetection(config.flyHackPause);
        }

        object CanMountEntity(BasePlayer player, BaseMountable entity) {
            if (!config.autoturretBattery) return null;
            MiniCopter ent = entity.GetParentEntity() as MiniCopter;
            if (ent != null) {
                IOEntity ioe = GetBatteryConnected(ent);
                if (ioe != null) {
                    SendReply(player, $"First disconnect battery input from {ioe.GetDisplayName()}");
                    return false;
                }
            }
            return null;
        }

        void AddLargeStorageBox(MiniCopter copter) {
            //sides,negative left | up and down | in and out
            if (config.storageLargeContainers == 1) {
                AddStorageBox(copter, storageLargePrefab, new Vector3(0.0f, 0.07f, -1.05f), Quaternion.Euler(0, 180f, 0));
            } else if (config.storageLargeContainers >= 2) {
                AddStorageBox(copter, storageLargePrefab, new Vector3(-0.48f, 0.07f, -1.05f), Quaternion.Euler(0, 180f, 0));
                AddStorageBox(copter, storageLargePrefab, new Vector3(0.48f, 0.07f, -1.05f), Quaternion.Euler(0, 180f, 0));
            }
        }

        void AddRearStorageBox(MiniCopter copter) {
            AddStorageBox(copter, storagePrefab, new Vector3(0, 0.75f, -1f));
        }

        void AddSideStorageBoxes(MiniCopter copter) {
            AddStorageBox(copter, storagePrefab, new Vector3(0.6f, 0.24f, -0.35f));
            if (!config.autoturretBattery) AddStorageBox(copter, storagePrefab, new Vector3(-0.6f, 0.24f, -0.35f));
        }

        void AddStorageBox(MiniCopter copter, string prefab, Vector3 position) {
            AddStorageBox(copter, prefab, position, new Quaternion());
        }

        void AddStorageBox(MiniCopter copter, string prefab, Vector3 position, Quaternion q) {

            StorageContainer box = GameManager.server.CreateEntity(prefab, copter.transform.position, q) as StorageContainer;

            if (prefab.Equals(storageLargePrefab) && config.largeStorageLockable) {
                box.isLockable = true;
                box.panelName = GetPanelName(config.largeStorageSize);
            }

            box.Spawn();
            box.SetParent(copter);
            box.transform.localPosition = position;

            if (prefab.Equals(storageLargePrefab) && config.largeStorageLockable) {
                box.inventory.capacity = config.largeStorageSize;
            }

            box.SendNetworkUpdateImmediate(true);
        }

        void AddSearchLight(MiniCopter copter) {
            SphereEntity sph = (SphereEntity)GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", copter.transform.position, new Quaternion(0, 0, 0, 0), true);
            sph.Spawn();
            sph.SetParent(copter);
            sph.transform.localPosition = new Vector3(0, 0.33f, 2.0f);
            sph.SendNetworkUpdate();

            SearchLight searchLight = GameManager.server.CreateEntity(searchLightPrefab, sph.transform.position) as SearchLight;
            searchLight.Spawn();
            searchLight.SetParent(sph);
            searchLight.transform.localPosition = new Vector3(0, 0, 0);
            searchLight.SendNetworkUpdate();

            sph.LerpRadiusTo(0.1f, 1f);
        }

        void AddTurret(MiniCopter copter) {
            AutoTurret aturret = GameManager.server.CreateEntity(autoturretPrefab, copter.transform.position) as AutoTurret;
            aturret.Spawn();
            DestroyGroundComp(aturret);
            aturret.pickup.enabled = false;
            aturret.sightRange = config.turretRange;
            aturret.SetParent(copter);
            aturret.transform.localPosition = new Vector3(0, 0, 2.47f);
            aturret.transform.localRotation = Quaternion.Euler(0, 0, 0);
            ProtoBuf.PlayerNameID pnid = new ProtoBuf.PlayerNameID();
            if (copter.OwnerID != 0) {
                pnid.userid = copter.OwnerID;
                pnid.username = BasePlayer.FindByID(copter.OwnerID)?.displayName;
                aturret.authorizedPlayers.Add(pnid);
            }
            aturret.SendNetworkUpdate();

            AddSwitch(aturret);
        }

        ElectricBattery AddBattery(MiniCopter copter) {
            ElectricBattery abat = GameManager.server.CreateEntity(batteryPrefab, copter.transform.position) as ElectricBattery;
            abat.maxOutput = 12;
            abat.Spawn();
            DestroyGroundComp(abat);
            abat.pickup.enabled = false;
            abat.SetParent(copter);
            abat.transform.localPosition = new Vector3(-0.7f, 0.2f, -0.2f);
            abat.transform.localRotation = Quaternion.Euler(0, 0, 0);
            abat.SendNetworkUpdate();
            return abat;
        }

        void AddSwitch(AutoTurret aturret) {
            ElectricBattery bat = null;
            if (config.autoturretBattery) {
                bat = AddBattery(aturret.GetParentEntity() as MiniCopter);
            }

            ElectricSwitch aSwitch = aturret.GetComponentInChildren<ElectricSwitch>();
            aSwitch = GameManager.server.CreateEntity(switchPrefab, aturret.transform.position)?.GetComponent<ElectricSwitch>();
            if (aSwitch == null) return;
            aSwitch.pickup.enabled = false;
            aSwitch.SetParent(aturret);
            aSwitch.transform.localPosition = new Vector3(0f, -0.65f, 0.325f);
            aSwitch.transform.localRotation = Quaternion.Euler(0, 0, 0);
            DestroyGroundComp(aSwitch);
            aSwitch.Spawn();
            aSwitch._limitedNetworking = false;
            if (!config.autoturretBattery) {
                RunWire(aSwitch, 0, aturret, 0, 12);
            } else if (bat != null) {
                RunWire(bat, 0, aSwitch, 0);
                RunWire(aSwitch, 0, aturret, 0);
            }
        }

        // https://umod.org/community/rust/12554-trouble-spawning-a-switch?page=1#post-5
        private void RunWire(IOEntity source, int s_slot, IOEntity destination, int d_slot, int power = 0) {
            destination.inputs[d_slot].connectedTo.Set(source);
            destination.inputs[d_slot].connectedToSlot = s_slot;
            destination.inputs[d_slot].connectedTo.Init();
            source.outputs[s_slot].connectedTo.Set(destination);
            source.outputs[s_slot].connectedToSlot = d_slot;
            source.outputs[s_slot].connectedTo.Init();
            source.MarkDirtyForceUpdateOutputs();
            if (power > 0) {
                destination.UpdateHasPower(power, 0);
                source.UpdateHasPower(power, 0);
            }
            source.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            destination.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
        }

        void DestroyGroundComp(BaseEntity ent) {
            UnityEngine.Object.Destroy(ent.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.Destroy(ent.GetComponent<GroundWatch>());
            //UnityEngine.Object.Destroy(ent.GetComponent<Rigidbody>());
        }

        IOEntity GetBatteryConnected(MiniCopter ent) {
            Puts("Search bat");
            ElectricBattery bat = ent.GetComponentInChildren<ElectricBattery>();
            if (bat != null) {
                Puts("Found bat");
                return bat.inputs[0].connectedTo.ioEnt;
            }
            return null;
        }

        private object OnSwitchToggle(ElectricSwitch eswitch, BasePlayer player) {
            if (config.autoturretBattery) return null;
            BaseEntity parent = eswitch.GetParentEntity();
            if (parent != null && parent.PrefabName.Equals(autoturretPrefab)) {
                AutoTurret turret = parent as AutoTurret;
                if (turret == null) return null;
                if (!eswitch.IsOn())
                    PowerTurretOn(turret);
                else
                    PowerTurretOff(turret);
            }
            return null;
        }

        public void PowerTurretOn(AutoTurret turret) {
            turret.SetFlag(BaseEntity.Flags.Reserved8, true);
            turret.InitiateStartup();
        }
        private void PowerTurretOff(AutoTurret turret) {
            turret.SetFlag(BaseEntity.Flags.Reserved8, false);
            turret.InitiateShutdown();
        }

        string GetPanelName(int capacity) {
            if (capacity <= 6) {
                return "smallstash";
            } else if (capacity > 6 && capacity <= 12) {
                return "smallwoodbox";
            } else if (capacity > 12 && capacity <= 30) {
                return "largewoodbox";
            } else {
                return "genericlarge";
            }
        }

        void RestoreMiniCopter(MiniCopter copter, bool removeStorage = false) {
            copter.fuelPerSec = copterDefaults.fuelPerSecond;
            copter.liftFraction = copterDefaults.liftFraction;
            copter.torqueScale = copterDefaults.torqueScale;
            if (removeStorage) {
                foreach (var child in copter.children) {
                    if (child.name == storagePrefab || child.name == storageLargePrefab || child.name == autoturretPrefab) {
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

            if (config.autoturret && copter.GetComponentInChildren<AutoTurret>() == null) {
                timer.Once(copter.isSpawned ? 0 : 0.2f, () => {
                    AddTurret(copter);
                });
            }
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

            foreach (var bat in copter.children) {
                Puts($"{bat.GetType().Name}");
            }
        }

        void StoreMiniCopterDefaults(MiniCopter copter) {
            if (copter.liftFraction == 0 || copter.torqueScale.x == 0 || copter.torqueScale.y == 0 || copter.torqueScale.z == 0) {
                copter.liftFraction = 0.25f;
                copter.torqueScale = new Vector3(400f, 400f, 200f);
            }
            Puts($"Defaults for copters saved as \nfuelPerSecond = {copter.fuelPerSec}\nliftFraction = {copter.liftFraction}\ntorqueScale = {copter.torqueScale}");
            copterDefaults = new MiniCopterDefaults {
                fuelPerSecond = copter.fuelPerSec,
                liftFraction = copter.liftFraction,
                torqueScale = copter.torqueScale
            };
        }

        #endregion

        #region Hooks

        void OnItemDeployed(Deployer deployer, BaseEntity entity) {
            if (entity?.GetParentEntity() != null && entity.GetParentEntity().ShortPrefabName.Equals("minicopter.entity")) {
                CodeLock cLock = entity.GetComponentInChildren<CodeLock>();
                cLock.transform.localPosition = new Vector3(0.0f, 0.3f, 0.298f);
                cLock.transform.localRotation = Quaternion.Euler(new Vector3(0, 90, 0));
                cLock.SendNetworkUpdateImmediate();
            }
        }

        void OnServerInitialized() {
            PrintWarning("Applying settings except storage modifications to existing MiniCopters.");
            foreach (var copter in UnityEngine.Object.FindObjectsOfType<MiniCopter>()) {
                // Nab the default values off the first minicopter.
                if (copterDefaults.Equals(default(MiniCopterDefaults))) {
                    StoreMiniCopterDefaults(copter);
                    break;
                }

                if (config.landOnCargo) copter.gameObject.AddComponent<MiniShipLandingGear>();

                ModifyMiniCopter(copter, config.reloadStorage);
            }
        }

        void OnEntitySpawned(MiniCopter mini) {

            // Only add storage on spawn so we don't stack or mess with
            // existing player storage containers. 
            ModifyMiniCopter(mini, true);
            if (config.landOnCargo) mini.gameObject.AddComponent<MiniShipLandingGear>();
            //if (config.addSearchLight) AddSearchLight(mini);
        }

        void OnEntityKill(BaseNetworkable entity) {
            if (!config.dropStorage || !entity.ShortPrefabName.Equals("minicopter.entity")) return;
            StorageContainer[] containers = entity.GetComponentsInChildren<StorageContainer>();
            foreach (StorageContainer container in containers) {
                container.DropItems();
            }

            AutoTurret[] turrets = entity.GetComponentsInChildren<AutoTurret>();
            foreach (AutoTurret turret in turrets) {
                turret.DropItems();
            }
        }

        void Unload() {
            foreach (var copter in UnityEngine.Object.FindObjectsOfType<MiniCopter>()) {
                RestoreMiniCopter(copter, config.reloadStorage);
                if (config.landOnCargo) UnityEngine.Object.Destroy(copter.GetComponent<MiniShipLandingGear>());
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
            public bool dropStorage = true;
            public bool largeStorageLockable = true;
            public int largeStorageSize = 42;
            public int flyHackPause = 4;
            public bool autoturret = true;
            public bool landOnCargo = true;
            public bool allowMiniPush = true;
            public bool autoturretBattery = true;
            //public bool addSearchLight = true;
            public float turretRange = 30f;

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
                GetConfig(ref dropStorage, "Drop Storage Loot On Death");
                GetConfig(ref largeStorageLockable, "Large Storage Lockable");
                GetConfig(ref largeStorageSize, "Large Storage Size (Max 42)");
                GetConfig(ref flyHackPause, "Seconds to pause flyhack when dismount from heli.");
                GetConfig(ref autoturret, "Add auto turret to heli");
                GetConfig(ref autoturretBattery, "Auto turret uses battery");
                //GetConfig(ref addSearchLight, "Add Searchlight to heli");
                GetConfig(ref landOnCargo, "Allow Minis to Land on Cargo");
                GetConfig(ref turretRange, "Mini Turret Range (Default 30)");
                GetConfig(ref allowMiniPush, "Allow minicopter push");

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

            if (config.largeStorageSize > 42) {
                PrintWarning($"Large Storage Containers Capacity configuration value {config.largeStorageSize} exceeds the maximum, setting to 42.");
                config.largeStorageSize = 42;
            } else if (config.largeStorageSize < 6) {
                PrintWarning($"Storage Containers Capacity cannot be a smaller than 6, setting to 6.");
            }
        }

        #endregion

        #region Chat Commands
        [ChatCommand("push")]
        private void PushMiniChatCommand(BasePlayer player, string command, string[] args) {
            if (!config.allowMiniPush) return;
            GetTargetEntity(player);
        }
        #endregion

        #region Helpers
        private void GetTargetEntity(BasePlayer player) {
            bool flag = Physics.Raycast(player.eyes.HeadRay(), out raycastHit, 3f, Rust.Layers.Solid);
            BaseEntity entity = raycastHit.GetEntity();

            if (!entity.ShortPrefabName.Equals("minicopter.entity")) {
                if (entity.GetParentEntity() != null && entity.GetParentEntity().ShortPrefabName.Equals("minicopter.entity")) {
                    PushMini(player, entity.GetParentEntity() as BaseVehicle);
                    return;
                }
            }

            if (entity != null && entity.ShortPrefabName.Equals("minicopter.entity")) {
                PushMini(player, entity as BaseVehicle);
                return;
            }
            SendReply(player, $"You have to look at a minicopter. Pushing {GetEnglishName(entity.ShortPrefabName)} not allowed");
        }
        private string GetEnglishName(string shortName) { return ItemManager.FindItemDefinition(shortName)?.displayName?.english ?? shortName; }
        void PushMini(BasePlayer player, BaseVehicle bVehicle) {
            //bVehicle.rigidBody.AddForceAtPosition(Vector3.up * 15f, raycastHit.point, ForceMode.VelocityChange);
            PrintToChat($"{bVehicle.rigidBody.name} {raycastHit.point}");
            bVehicle.rigidBody.AddForceAtPosition(Vector3.up * 2f, raycastHit.point, ForceMode.VelocityChange);
            bVehicle.rigidBody.AddForceAtPosition(Vector3.forward * 5.5f, raycastHit.point, ForceMode.VelocityChange);
            player.metabolism.calories.Subtract(2f);
            player.metabolism.SendChangesToClient();
            Effect.server.Run("assets/content/vehicles/boats/effects/small-boat-push-land.prefab", bVehicle.GetNetworkPosition());
        }
        #endregion

        #region Classes
        public class MiniShipLandingGear : MonoBehaviour {
            private MiniCopter miniCopter;
            private uint cargoId;
            private bool isDestroyed = false;

            void Awake() {
                miniCopter = gameObject.GetComponent<MiniCopter>();
            }

            //https://docs.unity3d.com/ScriptReference/Collider.OnTriggerEnter.html
            void OnTriggerEnter(Collider col) {
                if (cargoId > 0) {
                    CancelInvoke("Exit");
                    return;
                }
                //_instance.PrintToChat(col.gameObject.name);
                if (!col.gameObject.name.Equals("trigger")) return;

                CargoShip cargo = col.ToBaseEntity() as CargoShip;
                if (cargo == null) return;

                cargoId = cargo.net.ID;
                miniCopter.SetParent(cargo, true);
            }

            //https://docs.unity3d.com/ScriptReference/Collider.OnTriggerExit.html
            void OnTriggerExit(Collider col) {
                //_instance.PrintToChat("Exit");
                if (isDestroyed || cargoId == 0 || col.ToBaseEntity().net.ID != cargoId) return;
                Invoke("Exit", 1.5f);
            }

            void Exit() {
                cargoId = 0;
                if (isDestroyed || miniCopter == null || miniCopter.IsDestroyed || !miniCopter.IsMounted()) return;
                miniCopter.SetParent(null, true);
            }

            void OnDestroy() {
                isDestroyed = true;
                CancelInvoke("Exit");

                if (miniCopter == null || miniCopter.IsDestroyed) return;
                miniCopter.SetParent(null, true, true);
            }
        }
        #endregion

    }
}

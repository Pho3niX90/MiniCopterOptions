using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Mini-Copter Options", "Pho3niX90", "2.1.1")]
    [Description("Provide a number of additional options for Mini-Copters, including storage and seats.")]
    class MiniCopterOptions : RustPlugin
    {
        bool lastRanAtNight;
        #region Prefab Modifications

        private readonly string storagePrefab = "assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab";
        private readonly string storageLargePrefab = "assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab";
        private readonly string autoturretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private readonly string switchPrefab = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private readonly string searchLightPrefab = "assets/prefabs/deployable/search light/searchlight.deployed.prefab";
        private readonly string batteryPrefab = "assets/prefabs/deployable/playerioents/batteries/smallrechargablebattery.deployed.prefab";
        private readonly string flasherBluePrefab = "assets/prefabs/deployable/playerioents/lights/flasherlight/electric.flasherlight.deployed.prefab";
        private readonly string lockPrefab = "assets/prefabs/locks/keypad/lock.code.prefab";
        private readonly string spherePrefab = "assets/prefabs/visualization/sphere.prefab";

        private const string resizableLootPanelName = "generic_resizable";

        int setupTimeHooksAttempts;
        TOD_Sky time;
        float sunrise;
        float sunset;
        float lastNightCheck;

        bool IsNight() {
            if (time == null)
                return false;

            float hour = time.Cycle.Hour;
            return hour > sunset || hour < sunrise;
        }

        void OnHour() {
            float hour = time.Cycle.Hour;
            bool isNight = IsNight();

            //Puts($"OnHour: hour is now {hour}, and it is night {isNight}");
            if ((isNight == lastRanAtNight) || (lastNightCheck == hour))
                return;

            //Puts($"OnHour Called: Night:{isNight} LastRanAtNight:{lastRanAtNight}");
            lastNightCheck = hour;

            var minis = BaseNetworkable.serverEntities.OfType<MiniCopter>().ToArray();
            //Puts($"OnHour Called: Minis to modify {minis.Count}");\
            foreach (var mini in minis) {
                var tailLight = mini.GetComponentInChildren<FlasherLight>();
                if (tailLight != null) {
                    tailLight.SetFlag(IOEntity.Flag_HasPower, isNight);
                }
            }

            lastRanAtNight ^= true;
        }

        void SetupTimeHooks() {
            time = TOD_Sky.Instance;

            if (time == null) {
                if (setupTimeHooksAttempts++ >= 10) {
                    PrintError("Unable to detect time system. Tail light will not follow time of day.");;
                    return;
                }

                timer.Once(1, SetupTimeHooks);
                return;
            }

            sunrise = time.SunriseTime;
            sunset = time.SunsetTime;

            time.Components.Time.OnHour += OnHour;
        }

        StorageContainer[] GetStorage(MiniCopter copter) => copter.GetComponentsInChildren<StorageContainer>()
            .Where(x => x.name == storagePrefab || x.name == storageLargePrefab)
            .ToArray();

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
            if (!IsBatteryEnabled()) {
                AddStorageBox(copter, storagePrefab, new Vector3(-0.6f, 0.24f, -0.35f));
            }
        }

        void AddStorageBox(MiniCopter copter, string prefab, Vector3 position) {
            AddStorageBox(copter, prefab, position, Quaternion.identity);
        }

        void SetupStorage(StorageContainer box) {
            if (box.PrefabName.Equals(storageLargePrefab)) {
                box.isLockable = config.largeStorageLockable;
                box.inventory.capacity = config.largeStorageSize;
                box.panelName = resizableLootPanelName;
            }
        }

        void AddStorageBox(MiniCopter copter, string prefab, Vector3 position, Quaternion rotation) {
            StorageContainer box = GameManager.server.CreateEntity(prefab, position, rotation) as StorageContainer;

            box.Spawn();
            box.SetParent(copter);

            SetupStorage(box);
            box.SendNetworkUpdateImmediate();
        }

        void SetupInvincibility(BaseCombatEntity entity) {
            entity._maxHealth = 99999999f;
            entity._health = 99999999f;
            entity.SendNetworkUpdate();
        }

        void SetupTailLight(FlasherLight tailLight) {
            tailLight.pickup.enabled = false;
            DestroyGroundComp(tailLight);
            tailLight.SetFlag(IOEntity.Flag_HasPower, IsNight());
        }

        void AddTailLight(MiniCopter copter) {
            FlasherLight tailLight = GameManager.server.CreateEntity(flasherBluePrefab, new Vector3(0, 1.2f, -2.0f), Quaternion.Euler(33, 180, 0)) as FlasherLight;
            SetupTailLight(tailLight);
            tailLight.SetParent(copter);
            tailLight.Spawn();
            SetupInvincibility(tailLight);
        }

        void SetupSphereEntity(SphereEntity sphereEntity) {
            sphereEntity.EnableSaving(true);
            sphereEntity.EnableGlobalBroadcast(false);
        }

        void SetupSearchLight(SearchLight searchLight) {
            searchLight.pickup.enabled = false;
            DestroyMeshCollider(searchLight);
            DestroyGroundComp(searchLight);
        }

        void AddSearchLight(MiniCopter copter) {
            SphereEntity sphereEntity = GameManager.server.CreateEntity(spherePrefab, new Vector3(0, -100, 0), Quaternion.identity) as SphereEntity;
            SetupSphereEntity(sphereEntity);
            sphereEntity.SetParent(copter);
            sphereEntity.Spawn();

            SearchLight searchLight = GameManager.server.CreateEntity(searchLightPrefab, sphereEntity.transform.position) as SearchLight;
            SetupSearchLight(searchLight);
            searchLight.Spawn();
            SetupInvincibility(searchLight);
            searchLight.SetFlag(BaseEntity.Flags.Reserved5, true);
            searchLight.SetFlag(BaseEntity.Flags.Busy, true);
            searchLight.SetParent(sphereEntity);
            searchLight.transform.localPosition = Vector3.zero;
            searchLight.transform.localRotation = Quaternion.Euler(-20, 180, 180);

            sphereEntity.currentRadius = 0.1f;
            sphereEntity.lerpRadius = 0.1f;
            sphereEntity.UpdateScale();
            sphereEntity.SendNetworkUpdateImmediate();

            timer.Once(3f, () => {
                if (sphereEntity != null)
                    sphereEntity.transform.localPosition = new Vector3(0, 0.24f, 1.8f);
            });
        }

        void AddLock(BaseEntity entity) {
            CodeLock codeLock = GameManager.server.CreateEntity(lockPrefab) as CodeLock;

            codeLock.Spawn();
            codeLock.code = "789456789123";
            codeLock.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.Lock));
            codeLock.transform.localScale += new Vector3(-50, -50, -50);
            entity.SetSlot(BaseEntity.Slot.Lock, codeLock);
            codeLock.SetFlag(BaseEntity.Flags.Locked, true);
        }

        void SetupAutoTurret(AutoTurret turret) {
            turret.pickup.enabled = false;
            turret.sightRange = config.turretRange;
            DestroyMeshCollider(turret);
            DestroyGroundComp(turret);
        }

        void AddTurret(MiniCopter copter) {
            AutoTurret turret = GameManager.server.CreateEntity(autoturretPrefab, new Vector3(0, 0, 2.47f)) as AutoTurret;
            SetupAutoTurret(turret);
            turret.SetParent(copter);
            turret.Spawn();

            BasePlayer player = BasePlayer.FindByID(copter.OwnerID);
            if (player != null) {
                turret.authorizedPlayers.Add(new ProtoBuf.PlayerNameID() {
                    userid = player.userID,
                    username = player.displayName,
                });
                turret.SendNetworkUpdate();
            }

            AddSwitch(turret);
        }

        bool IsBatteryEnabled() => config.autoturretBattery && config.autoturret;

        void SetupBattery(ElectricBattery battery) {
            battery.maxOutput = 12;
            battery.pickup.enabled = false;
            DestroyGroundComp(battery);
        }

        ElectricBattery AddBattery(MiniCopter copter) {
            var batteryPosition = copter.transform.TransformPoint(new Vector3(-0.7f, 0.2f, -0.2f));
            var batteryRotation = copter.transform.rotation;

            ElectricBattery battery = GameManager.server.CreateEntity(batteryPrefab, batteryPosition, batteryRotation) as ElectricBattery;
            SetupBattery(battery);
            battery.Spawn();
            battery.SetParent(copter, worldPositionStays: true);
            SetupInvincibility(battery);

            return battery;
        }

        void SetupSwitch(ElectricSwitch electricSwitch) {
            electricSwitch.pickup.enabled = false;
            DestroyMeshCollider(electricSwitch);
            DestroyGroundComp(electricSwitch);
        }

        void AddSwitch(AutoTurret turret) {
            ElectricBattery battery = null;
            if (IsBatteryEnabled()) {
                battery = AddBattery(turret.GetParentEntity() as MiniCopter);
            }

            var switchPosition = turret.transform.TransformPoint(new Vector3(0f, -0.65f, 0.325f));
            var switchRotation = turret.transform.rotation;

            ElectricSwitch electricSwitch = GameManager.server.CreateEntity(switchPrefab, switchPosition, switchRotation) as ElectricSwitch;
            SetupSwitch(electricSwitch);
            electricSwitch.Spawn();
            SetupInvincibility(electricSwitch);

            // Spawning the switch at the desired world position and then parenting it, allows it to render correctly initially.
            electricSwitch.SetParent(turret, worldPositionStays: true);

            if (!IsBatteryEnabled()) {
                RunWire(electricSwitch, 0, turret, 0, 12);
            } else if (battery != null) {
                RunWire(battery, 0, electricSwitch, 0);
                RunWire(electricSwitch, 0, turret, 0);
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
            UnityEngine.Object.DestroyImmediate(ent.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(ent.GetComponent<GroundWatch>());
        }

        void DestroyMeshCollider(BaseEntity ent) {
            foreach (var mesh in ent.GetComponentsInChildren<MeshCollider>()) {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        IOEntity GetBatteryConnected(MiniCopter ent) {
            return ent.GetComponentInChildren<ElectricBattery>()?.inputs[0]?.connectedTo.ioEnt;
        }

        private void OnSwitchToggled(ElectricSwitch electricSwitch, BasePlayer player) {
            if (IsBatteryEnabled())
            {
                // Do nothing since the switch is supposed to be wired into the turret,
                // so the game should handle this automatically.
                return;
            }

            AutoTurret turret = electricSwitch.GetParentEntity() as AutoTurret;
            if (turret == null)
                return;

            var mini = turret.GetParentEntity() as MiniCopter;
            if (mini == null)
            {
                // Ignore if the turret isn't on a mini, to avoid plugin conflicts.
                return;
            }

            if (electricSwitch.IsOn()) {
                turret.SetFlag(IOEntity.Flag_HasPower, true);
                turret.InitiateStartup();
            } else {
                turret.SetFlag(IOEntity.Flag_HasPower, false);
                turret.InitiateShutdown();
            }
        }

        void RestoreMiniCopter(MiniCopter copter, bool removeStorage = false) {
            if (copterDefaults != null) {
                copter.fuelPerSec = copterDefaults.fuelPerSecond;
                copter.liftFraction = copterDefaults.liftFraction;
                copter.torqueScale = copterDefaults.torqueScale;
            }

            if (removeStorage) {
                foreach (var child in copter.children.FindAll(child => child.name == storagePrefab || child.name == storageLargePrefab || child.name == autoturretPrefab))
                    child.Kill();
            }
        }

        void ModifyMiniCopter(MiniCopter copter) {

            copter.fuelPerSec = config.fuelPerSec;
            copter.liftFraction = config.liftFraction;
            copter.torqueScale = new Vector3(config.torqueScalePitch, config.torqueScaleYaw, config.torqueScaleRoll);

            if (config.autoturret) {
                // Setup existing turret, or add a new one.
                var turret = copter.GetComponentInChildren<AutoTurret>();
                if (turret != null) {
                    SetupAutoTurret(turret);

                    // Setup existing switch, but don't add a new one since that may add a duplicate battery.
                    var turretSwitch = turret.GetComponentInChildren<ElectricSwitch>();
                    if (turretSwitch != null) {
                        SetupSwitch(turretSwitch);
                    }
                } else {
                    AddTurret(copter);
                }
            }

            var existingStorage = GetStorage(copter);
            if (existingStorage.Length > 0) {
                // Existing storage found, update its state and don't add any more storage.
                foreach (var storage in existingStorage) {
                    SetupStorage(storage);
                }
            } else {
                // Add storage since none was found.
                AddLargeStorageBox(copter);

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

            if (IsBatteryEnabled()) {
                // Setup battery if present, but don't add a new one since that's handled when adding a switch.
                var battery = copter.GetComponentInChildren<ElectricBattery>();
                if (battery != null) {
                    SetupBattery(battery);
                    SetupInvincibility(battery);
                }
            }

            if (config.addSearchLight) {
                // Setup existing search light, or add a new one.
                var searchLight = copter.GetComponentInChildren<SearchLight>();
                if (searchLight != null) {
                    SetupSearchLight(searchLight);
                    SetupInvincibility(searchLight);

                    var sphereEntity = searchLight.GetParentEntity() as SphereEntity;
                    if (sphereEntity != null)
                        SetupSphereEntity(sphereEntity);
                } else {
                    AddSearchLight(copter);
                }
            }

            if (config.lightTail) {
                // Setup existing tail light, or add a new one.
                var tailLight = copter.GetComponentInChildren<FlasherLight>();
                if (tailLight != null) {
                    SetupTailLight(tailLight);
                    SetupInvincibility(tailLight);
                } else {
                    AddTailLight(copter);
                }
            }
        }

        void StoreMiniCopterDefaults(MiniCopter copter) {
            if (copterDefaults != null)
                return;

            if (copter.liftFraction == 0 || copter.torqueScale.x == 0 || copter.torqueScale.y == 0 || copter.torqueScale.z == 0) {
                copter.liftFraction = 0.25f;
                copter.torqueScale = new Vector3(400f, 400f, 200f);
            }

            //Puts($"Defaults for copters saved as \nfuelPerSecond = {copter.fuelPerSec}\nliftFraction = {copter.liftFraction}\ntorqueScale = {copter.torqueScale}");
            copterDefaults = new MiniCopterDefaults {
                fuelPerSecond = copter.fuelPerSec,
                liftFraction = copter.liftFraction,
                torqueScale = copter.torqueScale
            };
        }

        #endregion

        #region Hooks

        void OnServerInitialized(bool init) {
            PrintWarning("Applying settings except storage modifications to existing MiniCopters.");

            if (config.lightTail) {
                SetupTimeHooks();
            }

            if (!config.addSearchLight)
                Unsubscribe(nameof(OnServerCommand));

            foreach (var copter in BaseNetworkable.serverEntities.OfType<MiniCopter>()) {
                OnEntitySpawned(copter);
            }

            Subscribe(nameof(OnEntitySpawned));
        }

        void Unload() {
            foreach (var copter in BaseNetworkable.serverEntities.OfType<MiniCopter>()) {
                if (config.restoreDefaults)
                    RestoreMiniCopter(copter, config.reloadStorage);

                if (config.landOnCargo)
                    UnityEngine.Object.Destroy(copter.GetComponent<MiniShipLandingGear>());
            }

            if (config.lightTail && time != null) {
                time.Components.Time.OnHour -= OnHour;
            }
        }

        void OnEntitySpawned(MiniCopter copter) {
            if (copter is ScrapTransportHelicopter)
                return;

            StoreMiniCopterDefaults(copter);

            // Only add storage on spawn so we don't stack or mess with
            // existing player storage containers.
            ModifyMiniCopter(copter);

            if (config.landOnCargo)
                copter.gameObject.AddComponent<MiniShipLandingGear>();
        }

        void OnEntityKill(BaseNetworkable entity) {
            if (!config.dropStorage || !(entity is MiniCopter))
                return;

            StorageContainer[] containers = entity.GetComponentsInChildren<StorageContainer>();
            foreach (StorageContainer container in containers) {
                container.DropItems();
            }

            AutoTurret[] turrets = entity.GetComponentsInChildren<AutoTurret>();
            foreach (AutoTurret turret in turrets) {
                turret.DropItems();
            }
        }

        object OnServerCommand(ConsoleSystem.Arg arg) {
            if (arg.Connection == null || arg.cmd.FullName != "inventory.lighttoggle")
                return null;

            var player = arg.Player();
            if (player == null)
                return null;

            var mini = player.GetMountedVehicle() as MiniCopter;
            if (mini == null)
                return null;

            foreach (var child in mini.children) {
                var sphere = child as SphereEntity;
                if ((object)sphere == null)
                    continue;

                foreach (var grandChild in sphere.children) {
                    var light = grandChild as SearchLight;
                    if ((object)light == null)
                        continue;

                    light.SetFlag(IOEntity.Flag_HasPower, !light.IsPowered());

                    // Prevent other lights from toggling.
                    return false;
                }
            }

            return null;
        }

        private void OnEntityDismounted(BaseNetworkable entity, BasePlayer player) {
            if (config.flyHackPause > 0 && entity.GetParentEntity() is MiniCopter)
                player.PauseFlyHackDetection(config.flyHackPause);
        }

        bool? CanMountEntity(BasePlayer player, BaseMountable entity) {
            if (!(entity is MiniCopter) && !(entity.GetParentEntity() is MiniCopter))
                return null;

            if (!IsBatteryEnabled())
                return null;

            MiniCopter ent = entity.GetParentEntity() as MiniCopter;
            if (ent != null) {
                IOEntity ioe = GetBatteryConnected(ent);
                if (ioe != null) {
                    SendReply(player, GetMsg("Err - Diconnect Battery"), ioe.GetDisplayName());
                    return false;
                }
            }
            return null;
        }

        void OnItemDeployed(Deployer deployer, StorageContainer container, BaseLock baseLock) {
            if (container == null || baseLock == null)
                return;

            var parent = container.GetParentEntity();
            if (parent == null || !(parent is MiniCopter) || parent is ScrapTransportHelicopter)
                return;

            if (container.PrefabName != storageLargePrefab)
                return;

            baseLock.transform.localPosition = new Vector3(0.0f, 0.3f, 0.298f);
            baseLock.transform.localRotation = Quaternion.Euler(new Vector3(0, 90, 0));
            baseLock.SendNetworkUpdateImmediate();
        }

        #endregion

        #region Configuration

        private class MiniCopterOptionsConfig
        {
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
            public int flyHackPause = 1;
            public bool autoturret = false;
            public bool landOnCargo = true;
            public bool autoturretBattery = true;
            public bool addSearchLight = true;
            public float turretRange = 30f;
            public bool lightTail = false;

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
                GetConfig(ref landOnCargo, "Allow Minis to Land on Cargo");
                GetConfig(ref turretRange, "Mini Turret Range (Default 30)");
                GetConfig(ref addSearchLight, "Light: Add Searchlight to heli");
                GetConfig(ref lightTail, "Light: Add Nightitme Tail Light");

                plugin.SaveConfig();
            }

            private void GetConfig<T>(ref T variable, params string[] path) {
                if (path.Length == 0)
                    return;

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

        class MiniCopterDefaults
        {
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

            Unsubscribe(nameof(OnEntitySpawned));
        }

        #endregion

        #region Helpers

        private string GetEnglishName(string shortName) { return ItemManager.FindItemDefinition(shortName)?.displayName?.english ?? shortName; }

        void PrintComponents(BaseEntity ent) {
            foreach (var sl in ent.GetComponents<Component>()) {
                Puts($"-P- {sl.GetType().Name} | {sl.name}");
                foreach (var s in sl.GetComponentsInChildren<Component>()) {
                    Puts($"-C- {s.GetType().Name} | {s.name}");
                }
            }
        }

        #endregion

        #region Classes

        public class MiniShipLandingGear : MonoBehaviour
        {
            private MiniCopter miniCopter;
            private bool pCargo;

            void Awake() {
                miniCopter = GetComponent<MiniCopter>();
            }

            void OnTriggerEnter(Collider collider) {
                if (!collider.isTrigger || !(collider.ToBaseEntity() is CargoShip) || pCargo)
                    return;

                ParentTo(miniCopter, collider.ToBaseEntity());
            }

            void OnTriggerExit(Collider collider) {
                if (!collider.isTrigger || !(collider.ToBaseEntity() is CargoShip) || !pCargo)
                    return;

                ParentTo(miniCopter, null);
            }

            void ParentTo(MiniCopter mini, BaseEntity parent) {
                mini.SetParent(parent, true);
                pCargo ^= true;
            }
        }

        #endregion

        #region Languages

        protected override void LoadDefaultMessages() {
            lang.RegisterMessages(new Dictionary<string, string> {
                ["Err - Diconnect Battery"] = "First disconnect battery input from {0}",
                ["Err - Can only push minicopter"] = "You have to look at a minicopter. Pushing {0} not allowed"
            }, this);
        }

        string GetMsg(string key, object userID = null) => lang.GetMessage(key, this, userID == null ? null : userID.ToString());

        #endregion

    }
}

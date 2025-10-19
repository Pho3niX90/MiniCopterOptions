using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Mini-Copter Options", "Pho3niX90", "2.5.2")]
    [Description("Provide a number of additional options for Mini-Copters, including storage and seats.")]
    internal class MiniCopterOptions : CovalencePlugin
    {
        #region Fields

        private readonly string minicopterPrefab = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
        private readonly string storagePrefab = "assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab";
        private readonly string storageLargePrefab = "assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab";
        private readonly string autoturretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private readonly string switchPrefab = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private readonly string searchLightPrefab = "assets/prefabs/deployable/search light/searchlight.deployed.prefab";
        private readonly string flasherBluePrefab = "assets/prefabs/deployable/playerioents/lights/flasherlight/electric.flasherlight.deployed.prefab";

        private const string resizableLootPanelName = "generic_resizable";
        private const int MinStorageCapacity = 6;
        private const int MaxStorageCapacity = 48;

        private static readonly Vector3 TurretPosition = new(0, 0, 2.47f);
        private static readonly Vector3 TurretSwitchPosition = new(0, 0.36f, 0.32f);

        private static readonly Vector3 SearchLightPosition = new(0, 0.24f, 1.8f);
        private static readonly Quaternion SearchLightRotation = Quaternion.Euler(-20, 180, 180);
        private static readonly Vector3 SearchLightTransformScale = Vector3.one * 0.1f;

        private static readonly Vector3 TailLightPosition = new(0, 1.2f, -2.0f);
        private static readonly Quaternion TailLightRotation = Quaternion.Euler(33, 180, 0);

        private readonly object False = false;

        private Configuration config;
        private MiniCopterDefaults copterDefaults;
        private bool lastRanAtNight;
        private int setupTimeHooksAttempts;
        private TOD_Sky time;
        private float sunrise;
        private float sunset;
        private float lastNightCheck;
        private float[] stageFuelPerSec = new float[1];
        private float[] stageLiftFraction = new float[1];

        #endregion

        #region Hooks

        private void Init()
        {
            if (config.storageContainers > 3)
            {
                PrintWarning($"Storage Containers configuration value {config.storageContainers} exceeds the maximum, setting to 3.");
                config.storageContainers = 3;
            }
            else if (config.storageContainers < 0)
            {
                PrintWarning($"Storage Containers cannot be a negative value, setting to 0.");
                config.storageContainers = 0;
            }

            if (config.storageLargeContainers > 2)
            {
                PrintWarning($"Large Storage Containers configuration value {config.storageLargeContainers} exceeds the maximum, setting to 2.");
                config.storageLargeContainers = 2;
            }
            else if (config.storageLargeContainers < 0)
            {
                PrintWarning($"Large Storage Containers cannot be a negative value, setting to 0.");
                config.storageLargeContainers = 0;
            }

            if (config.largeStorageSize > MaxStorageCapacity)
            {
                PrintWarning($"Large Storage Containers Capacity configuration value {config.largeStorageSize} exceeds the maximum, setting to {MaxStorageCapacity}.");
                config.largeStorageSize = MaxStorageCapacity;
            }
            else if (config.largeStorageSize < MinStorageCapacity)
            {
                PrintWarning($"Storage Containers Capacity cannot be a smaller than {MinStorageCapacity}, setting to {MinStorageCapacity}.");
            }

            Unsubscribe(nameof(OnEntitySpawned));

            if (!config.autoturret)
            {
                Unsubscribe(nameof(OnTurretTarget));
            }
        }

        private void OnServerInitialized()
        {
            StoreMiniCopterDefaults();

            if (config.lightTail)
            {
                SetupTimeHooks();
            }

            if (!config.addSearchLight)
            {
                Unsubscribe(nameof(OnServerCommand));
            }

            foreach (var copter in BaseNetworkable.serverEntities.OfType<Minicopter>())
            {
                foreach (var meshCollider in copter.GetComponentsInChildren<MeshCollider>())
                {
                    UnityEngine.Object.DestroyImmediate(meshCollider);
                }

                foreach (var groundWatch in copter.GetComponentsInChildren<GroundWatch>())
                {
                    UnityEngine.Object.DestroyImmediate(groundWatch);
                }

                OnEntitySpawned(copter);
            }

            Subscribe(nameof(OnEntitySpawned));
        }

        private void Unload()
        {
            if (config.lightTail && time != null)
            {
                time.Components.Time.OnHour -= OnHour;
            }

            // If the plugin is unloaded before OnServerInitialized() ran, don't revert minicopters to 0 values.
            if (copterDefaults != null)
            {
                foreach (var copter in BaseNetworkable.serverEntities.OfType<Minicopter>())
                {
                    if (config.restoreDefaults && CanModifyMiniCopter(copter))
                    {
                        RestoreMiniCopter(copter, config.reloadStorage);
                    }
                }
            }
        }

        private void OnEntitySpawned(Minicopter copter)
        {
            ScheduleModifyMiniCopter(copter);
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (!config.dropStorage || entity is not Minicopter)
                return;

            var containers = entity.GetComponentsInChildren<StorageContainer>();
            foreach (var container in containers)
            {
                container.DropItems();
            }

            var turrets = entity.GetComponentsInChildren<AutoTurret>();
            foreach (var turret in turrets)
            {
                turret.DropItems();
            }
        }

        private void OnSwitchToggled(ElectricSwitch electricSwitch, BasePlayer player)
        {
            var turret = electricSwitch.GetParentEntity() as AutoTurret;
            if (turret == null)
                return;

            var mini = turret.GetParentEntity() as Minicopter;
            if (mini == null)
            {
                // Ignore if the turret isn't on a mini, to avoid plugin conflicts.
                return;
            }

            if (electricSwitch.IsOn())
            {
                turret.SetFlag(IOEntity.Flag_HasPower, true);
                turret.InitiateStartup();
            }
            else
            {
                turret.SetFlag(IOEntity.Flag_HasPower, false);
                turret.InitiateShutdown();
            }
        }

        private object OnTurretTarget(AutoTurret turret, BaseCombatEntity target)
        {
            if (target == null)
                return null;

            var mini = turret.GetParentEntity() as Minicopter;
            if (mini == null)
                return null;

            if (!config.autoTurretTargetsAnimals && target is BaseAnimalNPC)
                return False;

            var basePlayer = target as BasePlayer;
            if (basePlayer != null)
            {
                if (!config.autoTurretTargetsNPCs && basePlayer.IsNpc)
                    return False;

                if (!config.autoTurretTargetsPlayers && basePlayer.userID.IsSteamId())
                    return False;

                if (basePlayer.InSafeZone() && (basePlayer.IsNpc || !basePlayer.IsHostile()))
                    return False;
            }

            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Connection == null || arg.cmd.FullName != "inventory.lighttoggle")
                return null;

            var player = arg.Player();
            if (player == null)
                return null;

            var mini = player.GetMountedVehicle() as Minicopter;
            if (mini == null)
                return null;

            if (!mini.IsDriver(player))
                return null;

            foreach (var child in mini.children)
            {
                var light = child as SearchLight;
                if (light == null)
                    continue;

                light.SetFlag(IOEntity.Flag_HasPower, !light.IsPowered());

                // Prevent other lights from toggling.
                return False;
            }

            return null;
        }

        private void OnEntityDismounted(BaseNetworkable entity, BasePlayer player)
        {
            if (config.flyHackPause > 0 && entity.GetParentEntity() is Minicopter)
            {
                player.PauseFlyHackDetection(config.flyHackPause);
            }
        }

        private void OnItemDeployed(Deployer deployer, StorageContainer container, BaseLock baseLock)
        {
            if (container == null || baseLock == null)
                return;

            var parent = container.GetParentEntity();
            if (parent == null || parent is not Minicopter)
                return;

            if (container.PrefabName != storageLargePrefab)
                return;

            baseLock.transform.localPosition = new Vector3(0.0f, 0.3f, 0.298f);
            baseLock.transform.localRotation = Quaternion.Euler(new Vector3(0, 90, 0));
            baseLock.SendNetworkUpdateImmediate();
        }

        // When another plugin wants to modify a specific property, adjust the proposed value, using the delta between
        // the vanilla default and the configured value.
        private void OnMinicopterFuelPerSecChange(Plugin plugin, Minicopter copter, float[] fuelPerSec)
        {
            if (plugin.Name == nameof(MiniCopterOptions)
                || copterDefaults == null
                || config.fuelPerSec < 0
                || !CanModifyMiniCopter(copter))
                return;

            // For example, vanilla is 0.5, configured is 0.1, so multiply by 0.2.
            fuelPerSec[0] *= config.fuelPerSec / copterDefaults.fuelPerSec;
        }

        private void OnMinicopterLiftFractionChange(Plugin plugin, Minicopter copter, float[] liftFraction)
        {
            if (plugin.Name == nameof(MiniCopterOptions)
                || copterDefaults == null
                || config.liftFraction < 0
                || !CanModifyMiniCopter(copter))
                return;

            // For example, vanilla is 0.25, configured is 0.5, so multiply by 2.
            liftFraction[0] *= config.liftFraction / copterDefaults.liftFraction;
        }

        #endregion

        #region Exposed Hooks

        private static class ExposedHooks
        {
            public static object OnMiniCopterOptions(Minicopter copter)
            {
                return Interface.CallHook("OnMiniCopterOptions", copter);
            }

            public static object OnMinicopterFuelPerSecChange(Plugin plugin, Minicopter copter, float[] fuelPerSec)
            {
                return Interface.CallHook("OnMinicopterFuelPerSecChange", plugin, copter, fuelPerSec);
            }

            public static object OnMinicopterLiftFractionChange(Plugin plugin, Minicopter copter, float[] liftFraction)
            {
                return Interface.CallHook("OnMinicopterLiftFractionChange", plugin, copter, liftFraction);
            }
        }

        #endregion

        #region Helpers

        private bool IsNight()
        {
            if (time == null)
                return false;

            var hour = time.Cycle.Hour;
            return hour > sunset || hour < sunrise;
        }

        private void OnHour()
        {
            var hour = time.Cycle.Hour;
            var isNight = IsNight();

            //Puts($"OnHour: hour is now {hour}, and it is night {isNight}");
            if ((isNight == lastRanAtNight) || (lastNightCheck == hour))
                return;

            //Puts($"OnHour Called: Night:{isNight} LastRanAtNight:{lastRanAtNight}");
            lastNightCheck = hour;

            var minis = BaseNetworkable.serverEntities.OfType<Minicopter>().ToArray();
            //Puts($"OnHour Called: Minis to modify {minis.Count}");\
            foreach (var mini in minis)
            {
                var tailLight = mini.GetComponentInChildren<FlasherLight>();
                if (tailLight != null)
                {
                    tailLight.SetFlag(IOEntity.Flag_HasPower, isNight);
                }
            }

            lastRanAtNight ^= true;
        }

        private void SetupTimeHooks()
        {
            time = TOD_Sky.Instance;

            if (time == null)
            {
                if (setupTimeHooksAttempts++ >= 10)
                {
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

        private StorageContainer[] GetStorage(Minicopter copter)
        {
            return copter.GetComponentsInChildren<StorageContainer>()
                .Where(x => x.name == storagePrefab || x.name == storageLargePrefab)
                .ToArray();
        }

        private void AddLargeStorageBox(Minicopter copter)
        {
            if (config.storageLargeContainers == 1)
            {
                AddStorageBox(copter, storageLargePrefab, new Vector3(0.0f, 0.07f, -1.05f), Quaternion.Euler(0, 180f, 0));
            }
            else if (config.storageLargeContainers >= 2)
            {
                AddStorageBox(copter, storageLargePrefab, new Vector3(-0.48f, 0.07f, -1.05f), Quaternion.Euler(0, 180f, 0));
                AddStorageBox(copter, storageLargePrefab, new Vector3(0.48f, 0.07f, -1.05f), Quaternion.Euler(0, 180f, 0));
            }

        }

        private void AddRearStorageBox(Minicopter copter)
        {
            AddStorageBox(copter, storagePrefab, new Vector3(0, 0.75f, -1f));
        }

        private void AddSideStorageBoxes(Minicopter copter)
        {
            AddStorageBox(copter, storagePrefab, new Vector3(0.6f, 0.24f, -0.35f));
            AddStorageBox(copter, storagePrefab, new Vector3(-0.6f, 0.24f, -0.35f));
        }

        private void AddStorageBox(Minicopter copter, string prefab, Vector3 position)
        {
            AddStorageBox(copter, prefab, position, Quaternion.identity);
        }

        private void SetupStorage(StorageContainer box)
        {
            if (box.PrefabName.Equals(storageLargePrefab))
            {
                box.isLockable = config.largeStorageLockable;
                box.inventory.capacity = config.largeStorageSize;
                box.panelName = resizableLootPanelName;
            }
        }

        private void AddStorageBox(Minicopter copter, string prefab, Vector3 position, Quaternion rotation)
        {
            var box = GameManager.server.CreateEntity(prefab, position, rotation) as StorageContainer;
            if (box == null)
                return;

            box.Spawn();
            box.SetParent(copter);

            SetupStorage(box);
            box.SendNetworkUpdateImmediate();
        }

        private void SetupInvincibility(BaseCombatEntity entity)
        {
            entity._maxHealth = 99999999f;
            entity._health = 99999999f;
            entity.SendNetworkUpdate();
        }

        private void SetupTailLight(FlasherLight tailLight)
        {
            tailLight.pickup.enabled = false;
            DestroyGroundComp(tailLight);
            tailLight.SetFlag(IOEntity.Flag_HasPower, IsNight());
        }

        private void AddTailLight(Minicopter copter)
        {
            var tailLight = GameManager.server.CreateEntity(flasherBluePrefab, TailLightPosition, TailLightRotation) as FlasherLight;
            if (tailLight == null)
                return;

            SetupTailLight(tailLight);
            tailLight.SetParent(copter);
            tailLight.Spawn();
            SetupInvincibility(tailLight);
        }

        private void SetupSearchLight(SearchLight searchLight)
        {
            searchLight.pickup.enabled = false;
            DestroyMeshCollider(searchLight);
            DestroyGroundComp(searchLight);
        }

        private void AddSearchLight(Minicopter copter)
        {
            var searchLight = GameManager.server.CreateEntity(searchLightPrefab, SearchLightPosition, SearchLightRotation) as SearchLight;
            if (searchLight == null)
                return;

            searchLight.transform.localScale = SearchLightTransformScale;
            searchLight.networkEntityScale = true;
            searchLight.SetParent(copter);
            SetupSearchLight(searchLight);
            searchLight.Spawn();

            SetupInvincibility(searchLight);
            searchLight.SetFlag(BaseEntity.Flags.Reserved5, true);
            searchLight.SetFlag(BaseEntity.Flags.Busy, true);
        }

        private void SetupAutoTurret(AutoTurret turret)
        {
            turret.pickup.enabled = false;
            turret.sightRange = config.turretRange;
            DestroyMeshCollider(turret);
            DestroyGroundComp(turret);
        }

        private void AddTurret(Minicopter copter)
        {
            var turret = GameManager.server.CreateEntity(autoturretPrefab, TurretPosition) as AutoTurret;
            if (turret == null)
                return;

            SetupAutoTurret(turret);
            turret.SetParent(copter);
            turret.Spawn();

            var player = BasePlayer.FindByID(copter.OwnerID);
            if (player != null)
            {
                turret.authorizedPlayers.Add(new ProtoBuf.PlayerNameID
                {
                    userid = player.userID,
                    username = player.displayName,
                });
                turret.SendNetworkUpdate();
            }

            AddSwitch(turret);
        }

        private void SetupSwitch(ElectricSwitch electricSwitch)
        {
            electricSwitch.pickup.enabled = false;
            DestroyMeshCollider(electricSwitch);
            DestroyGroundComp(electricSwitch);

            if (electricSwitch.HasParent())
            {
                var transform = electricSwitch.transform;
                if (transform.localPosition != TurretSwitchPosition)
                {
                    transform.localPosition = TurretSwitchPosition;
                    electricSwitch.InvalidateNetworkCache();
                    electricSwitch.SendNetworkUpdate_Position();
                }
            }
        }

        private void AddSwitch(AutoTurret turret)
        {
            var turretTransform = turret.transform;
            var switchPosition = turretTransform.TransformPoint(TurretSwitchPosition);
            var switchRotation = turretTransform.rotation;

            var electricSwitch = GameManager.server.CreateEntity(switchPrefab, switchPosition, switchRotation) as ElectricSwitch;
            if (electricSwitch != null)
            {
                SetupSwitch(electricSwitch);
                electricSwitch.Spawn();
                SetupInvincibility(electricSwitch);

                // Spawning the switch at the desired world position and then parenting it, allows it to render correctly initially.
                electricSwitch.SetParent(turret, worldPositionStays: true);
            }
        }

        private void DestroyGroundComp(BaseEntity ent)
        {
            UnityEngine.Object.DestroyImmediate(ent.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(ent.GetComponent<GroundWatch>());
        }

        private void DestroyMeshCollider(BaseEntity ent)
        {
            foreach (var mesh in ent.GetComponentsInChildren<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private void RestoreMiniCopter(Minicopter copter, bool removeStorage = false)
        {
            if (copterDefaults != null)
            {
                var proposedFuelPerSec = copterDefaults.fuelPerSec;
                if (config.fuelPerSec >= 0 && CanModifyFuelPerSec(copter, ref proposedFuelPerSec))
                {
                    copter.fuelPerSec = proposedFuelPerSec;
                }

                var proposedLiftFraction = copterDefaults.liftFraction;
                if (config.liftFraction >= 0 && CanModifyLiftFraction(copter, ref proposedLiftFraction))
                {
                    copter.liftFraction = proposedLiftFraction;
                }

                if (config.torqueScalePitch >= 0)
                {
                    copter.torqueScale.x = copterDefaults.torqueScale.x;
                }

                if (config.torqueScaleYaw >= 0)
                {
                    copter.torqueScale.y = copterDefaults.torqueScale.y;
                }

                if (config.torqueScaleRoll >= 0)
                {
                    copter.torqueScale.z = copterDefaults.torqueScale.z;
                }
            }

            if (removeStorage)
            {
                foreach (var child in copter.children.FindAll(child => child.name == storagePrefab || child.name == storageLargePrefab || child.name == autoturretPrefab))
                {
                    child.Kill();
                }
            }
        }

        private void ModifyMiniCopter(Minicopter copter)
        {
            if (copterDefaults != null)
            {
                var proposedFuelPerSec = config.fuelPerSec;
                if (config.fuelPerSec >= 0 && CanModifyFuelPerSec(copter, ref proposedFuelPerSec))
                {
                    copter.fuelPerSec = proposedFuelPerSec;
                }

                var proposedLiftFraction = config.liftFraction;
                if (config.liftFraction >= 0 && CanModifyLiftFraction(copter, ref proposedLiftFraction))
                {
                    copter.liftFraction = proposedLiftFraction;
                }

                if (config.torqueScalePitch >= 0)
                {
                    copter.torqueScale.x = config.torqueScalePitch;
                }

                if (config.torqueScaleYaw >= 0)
                {
                    copter.torqueScale.y = config.torqueScaleYaw;
                }

                if (config.torqueScaleRoll >= 0)
                {
                    copter.torqueScale.z = config.torqueScaleRoll;
                }
            }

            // Override the inertia tensor since if the server had rebooted while there were attachments, it would have been snapshotted to an unreasonable value.
            // This is the vanilla amount while the prevent building object is inactive.
            // To determine this value, simply deactivate the prevent building object, call rigidBody.ResetInertiaTensor(), then print the value.
            copter.rigidBody.inertiaTensor = new Vector3(407.1f, 279.6f, 173.2f);

            if (config.autoturret)
            {
                // Setup existing turret, or add a new one.
                var turret = copter.GetComponentInChildren<AutoTurret>();
                if (turret != null)
                {
                    SetupAutoTurret(turret);

                    // Setup existing switch, but don't add a new one.
                    var turretSwitch = turret.GetComponentInChildren<ElectricSwitch>();
                    if (turretSwitch != null)
                    {
                        SetupSwitch(turretSwitch);
                    }
                }
                else
                {
                    AddTurret(copter);
                }
            }

            var existingStorage = GetStorage(copter);
            if (existingStorage.Length > 0)
            {
                // Existing storage found, update its state and don't add any more storage.
                foreach (var storage in existingStorage)
                {
                    SetupStorage(storage);
                }
            }
            else
            {
                // Add storage since none was found.
                AddLargeStorageBox(copter);

                switch (config.storageContainers)
                {
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

            if (config.addSearchLight)
            {
                // Setup existing search light, or add a new one.
                var searchLight = copter.GetComponentInChildren<SearchLight>();
                if (searchLight != null)
                {
                    // Remove the legacy sphere used to resize the search light.
                    var sphereEntity = searchLight.GetParentEntity() as SphereEntity;
                    if (sphereEntity != null)
                    {
                        searchLight.SetParent(copter, worldPositionStays: true, sendImmediate: true);
                        sphereEntity.Kill();
                    }

                    if (!searchLight.networkEntityScale || searchLight.transform.localScale != SearchLightTransformScale)
                    {
                        searchLight.transform.localScale = SearchLightTransformScale;
                        searchLight.networkEntityScale = true;
                        searchLight.SendNetworkUpdate();
                    }

                    SetupSearchLight(searchLight);
                    SetupInvincibility(searchLight);
                }
                else
                {
                    AddSearchLight(copter);
                }
            }

            if (config.lightTail)
            {
                // Setup existing tail light, or add a new one.
                var tailLight = copter.GetComponentInChildren<FlasherLight>();
                if (tailLight != null)
                {
                    SetupTailLight(tailLight);
                    SetupInvincibility(tailLight);
                }
                else
                {
                    AddTailLight(copter);
                }
            }
        }

        private bool CanModifyMiniCopter(Minicopter copter)
        {
            if (ExposedHooks.OnMiniCopterOptions(copter) is false)
                return false;

            return true;
        }

        private bool CanModifyFuelPerSec(Minicopter copter, ref float proposedFuelPerSec)
        {
            stageFuelPerSec[0] = proposedFuelPerSec;

            if (ExposedHooks.OnMinicopterFuelPerSecChange(this, copter, stageFuelPerSec) is false)
                return false;

            proposedFuelPerSec = stageFuelPerSec[0];
            return true;
        }

        private bool CanModifyLiftFraction(Minicopter copter, ref float proposedLiftFraction)
        {
            stageLiftFraction[0] = proposedLiftFraction;

            if (ExposedHooks.OnMinicopterLiftFractionChange(this, copter, stageLiftFraction) is false)
                return false;

            proposedLiftFraction = stageLiftFraction[0];
            return true;
        }

        private void ScheduleModifyMiniCopter(Minicopter copter)
        {
            // Delay to allow plugins to detect the Mini and save its ID, so they can block modification via hooks.
            NextTick(() =>
            {
                if (copter == null || copter.IsDestroyed)
                    return;

                if (!CanModifyMiniCopter(copter))
                    return;

                ModifyMiniCopter(copter);
            });
        }

        private void StoreMiniCopterDefaults()
        {
            var copter = GameManager.server.FindPrefab(minicopterPrefab)?.GetComponent<Minicopter>();
            if (copter == null)
                return;

            //Puts($"Defaults for copters saved as \nfuelPerSecond = {copter.fuelPerSec}\nliftFraction = {copter.liftFraction}\ntorqueScale = {copter.torqueScale}");
            copterDefaults = new MiniCopterDefaults
            {
                fuelPerSec = copter.fuelPerSec,
                liftFraction = copter.liftFraction,
                torqueScale = copter.torqueScale
            };
        }

        #endregion

        #region Configuration

        private class MiniCopterDefaults
        {
            public float fuelPerSec;
            public float liftFraction;
            public Vector3 torqueScale;
        }

        private class Configuration : SerializableConfiguration
        {
            // Populated with Rust defaults.
            [JsonProperty("Fuel per Second")]
            public float fuelPerSec = 0.5f;

            [JsonProperty("Lift Fraction")]
            public float liftFraction = 0.25f;

            [JsonProperty("Pitch Torque Scale")]
            public float torqueScalePitch = 400f;

            [JsonProperty("Yaw Torque Scale")]
            public float torqueScaleYaw = 400f;

            [JsonProperty("Roll Torque Scale")]
            public float torqueScaleRoll = 200f;

            [JsonProperty("Storage Containers")]
            public int storageContainers = 0;

            [JsonProperty("Large Storage Containers")]
            public int storageLargeContainers = 0;

            [JsonProperty("Restore Defaults")]
            public bool restoreDefaults = true;

            [JsonProperty("Reload Storage")]
            public bool reloadStorage = false;

            [JsonProperty("Drop Storage Loot On Death")]
            public bool dropStorage = true;

            [JsonProperty("Large Storage Lockable")]
            public bool largeStorageLockable = true;

            [JsonProperty("Large Storage Size (Max 48)")]
            public int largeStorageSize = 48;

            [JsonProperty("Large Storage Size (Max 42)")]
            public int largeStorageSizeDeprecated {
                set => largeStorageSize = value;
            }

            [JsonProperty("Seconds to pause flyhack when dismount from heli.")]
            public int flyHackPause = 1;

            [JsonProperty("Add auto turret to heli")]
            public bool autoturret = false;

            [JsonProperty("Auto turret targets players")]
            public bool autoTurretTargetsPlayers = true;

            [JsonProperty("Auto turret targets NPCs")]
            public bool autoTurretTargetsNPCs = true;

            [JsonProperty("Auto turret targets animals")]
            public bool autoTurretTargetsAnimals = true;

            [JsonProperty("Mini Turret Range (Default 30)")]
            public float turretRange = 30f;

            [JsonProperty("Light: Add Searchlight to heli")]
            public bool addSearchLight = true;

            [JsonProperty("Light: Add Nightitme Tail Light")]
            public bool lightTail = false;
        }

        private Configuration GetDefaultConfig() => new();

        #region Configuration Helpers

        private class SerializableConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(SerializableConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                object currentRawValue;
                if (currentRaw.TryGetValue(key, out currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(config))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion

        #endregion
    }
}

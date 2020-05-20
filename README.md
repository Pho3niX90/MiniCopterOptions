Allows for the configuration of additional Mini-Copter options, including the addition of storage containers to the Mini-Copter.

* Increase the **Lift Fraction** to increase the take off speed.
* Increase the **Pitch/Roll/Yaw Torque Scale** in order to make the Mini-Copter response to controls faster.
* Setting **Fuel per Second** to 0 will make the Mini-Copter use no fuel, but still requires **1 fuel** to be placed into the fuel storage in order to run the Mini-Copter. This is a client side requirement.

Set **Restore Defaults** to true to restore the default movement and fuel consumption values on plugin unload.   Storage Containers will remain unless **Reload Storage** is set to true. **Warning:** Unloading or reloading the plugin with **Reload Storage** set to **true** will destroy any player items contained in the Mini-Copter storage stashes.

**Warning about high *Lift Fraction* values:** Setting a high lift fraction can increase the risk of FlyHack kicks when a player has crashed the Mini-Copter. **Seconds to pause flyhack when dismount from heli.:** should solve the issue. 
## Commands
`/push` Pushes the mini.

## Configuration

The default configuration mimics the default Rust values.

```json
{
  "Add auto turret to heli": true, // adds an autoturret to the mini
  "Allow minicopter push": true,  //if players should be allowed to push the mini with /push
  "Allow Minis to Land on Cargo": true,
  "Auto turret uses battery": true, // if true, the mini will have a small battery that will require charging to run the turret, if false, the turret will have an endless supply of power from the switch.
  "Drop Storage Loot On Death": true, // drops storage loot when heli is destroyed
  "Fuel per Second": 0.25,
  "Large Storage Containers": 2, //how many large containers should there be (max 2)
  "Large Storage Lockable": true, // if people can add locks to large containers
  "Large Storage Size (Max 42)": 42, // storage capacity of large containers, max 42, min 6
  "Lift Fraction": 0.25,
  "Pitch Torque Scale": 400.0,
  "Reload Storage": false,
  "Restore Defaults": true,
  "Roll Torque Scale": 200.0,
  "Seconds to pause flyhack when dismount from heli.": 4,
  "Storage Containers": 3, // how many small containers there should be, max 3
  "Yaw Torque Scale": 400.0
}
```

Original plugin dev: Diametric
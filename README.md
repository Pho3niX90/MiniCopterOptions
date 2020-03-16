Allows for the configuration of additional Mini-Copter options, including the addition of storage containers to the Mini-Copter. 

* Increase the **Lift Fraction** to increase the take off speed.
* Increase the **Pitch/Roll/Yaw Torque Scale** in order to make the Mini-Copter response to controls faster. 
* Setting **Fuel per Second** to 0 will make the Mini-Copter use no fuel, but still requires **1 fuel** to be placed into the fuel storage in order to run the Mini-Copter. This is a client side requirement. 

Set **Restore Defaults** to true to restore the default movement and fuel consumption values on plugin unload.   Storage Containers will remain unless **Reload Storage** is set to true. **Warning:** Unloading or reloading the plugin with **Reload Storage** set to **true** will destroy any player items contained in the Mini-Copter storage stashes. 

**Warning about high *Lift Fraction* values:** Setting a high lift fraction can increase the risk of FlyHack kicks when a player has crashed the Mini-Copter. 

## Configuration

The default configuration mimics the default Rust values. 

```json
{
  "Fuel per Second": 0.25,
  "Lift Fraction": 0.25,
  "Pitch Torque Scale": 400.0,
  "Reload Storage": false,
  "Restore Defaults": true,
  "Roll Torque Scale": 200.0,
  "Storage Containers": 0,
  "Yaw Torque Scale": 400.0
}
```
using PVZ_Randomizer.Template.Configuration;
using Reloaded.Mod.Interfaces.Structs;
using System.ComponentModel;

namespace PVZ_Randomizer.Configuration
{
    public class Config : Configurable<Config>
    {
        /*
            User Properties:
                - Please put all of your configurable properties here.
    
            By default, configuration saves as "Config.json" in mod user config folder.    
            Need more config files/classes? See Configuration.cs
    
            Available Attributes:
            - Category
            - DisplayName
            - Description
            - DefaultValue

            // Technically Supported but not Useful
            - Browsable
            - Localizable

            The `DefaultValue` attribute is used as part of the `Reset` button in Reloaded-Launcher.
        */
        [DisplayName("Always Allow Coin Drops")]
        [Category("Randomizer")]
        [Description("If true, lets zombies drop coins in all levels.")]
        [DefaultValue(true)]
        public bool AlwaysAllowCoinDrops { get; set; } = true;

        [DisplayName("Store is Always Unlocked")]
        [Category("Randomizer")]
        [Description("If true, the store is always available.")]
        [DefaultValue(true)]
        public bool StoreAlwaysAvailable { get; set; } = true;

        [DisplayName("All Store Items are Available")]
        [Category("Randomizer")]
        [Description("If true, all upgrade plants will always be available for purchase.")]
        [DefaultValue(true)]
        public bool StoreItemsAlwaysAvailable { get; set; } = true;

        [DisplayName("Randomize Seeds")]
        [Category("Seed Randomizer")]
        [Description("Randomize seed reward at the end of a level.")]
        [DefaultValue(true)]
        public bool RandomizeSeedAwards { get; set; } = true;

        [DisplayName("Drop Upgrade Plants")]
        [Category("Seed Randomizer")]
        [Description("If true, upgrade plants can be dropped at the end of a level.")]
        [DefaultValue(true)]
        public bool DropUpgradePlants { get; set; } = true;

        [DisplayName("Force Mandatory Plants")]
        [Category("Seed Randomizer")]
        [Description("Force unlocks mandatory plants if needed. (ex: being sent to pool or fog without lily pad unlocked)")]
        [DefaultValue(true)]
        public bool ForceMandatoryPlants { get; set; } = true;
    }

    /// <summary>
    /// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
    /// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
    /// </summary>
    public class ConfiguratorMixin : ConfiguratorMixinBase
    {
        // 
    }
}

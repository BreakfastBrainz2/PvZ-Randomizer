using Newtonsoft.Json;
using PVZ_Randomizer.Configuration;
using PVZ_Randomizer.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Structs;
using Reloaded.Hooks.Definitions.X86;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Runtime.InteropServices;
using static Reloaded.Hooks.Definitions.X86.FunctionAttribute;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace PVZ_Randomizer
{
    public unsafe class Mod : ModBase // <= Do not Remove.
    {
        private readonly IModLoader _modLoader;
        private readonly IReloadedHooks? _hooks;
        private readonly ILogger _logger;
        private readonly IMod _owner;
        private Config _configuration;
        private readonly IModConfig _modConfig;

        private static IHook<HasSeedType> s_hasSeedTypeHook;
        private static IHook<CoinGetFinalSeedPacketType> s_coinGetFinalSeedPacketTypeHook;
        private static IHook<BoardChooseSeedsOnLevel> s_boardChooseSeedsOnLevelHook;
        private static IHook<LawnAppGetAvailableSeeds> s_lawnAppGetAvailableSeedsHook;
        private static IHook<AwardScreenStartButtonPressed> s_awardScreenStartButtonPressedHook;
        private static IHook<GameSelectorAdventureClicked> s_gameSelectorAdventureClickedHook;

        private static bool s_finalSeedPacketRewarded = false;
        private static SeedType s_finalSeedPacketReward = SeedType.SEED_NONE;

        private static Mod s_mod;
        private static RandomizerSave s_save;
        private static Random s_random = new(DateTime.Now.Millisecond);

        private static readonly string s_saveName = "randomizer_save.json";

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

            s_mod = this;
            s_save = new RandomizerSave();

            s_save.CollectedSeeds.Add(SeedType.SEED_PEASHOOTER, true);

            for (SeedType i = SeedType.SEED_SUNFLOWER; i < SeedType.SEED_IMITATER; i++)
            {
                s_save.CollectedSeeds.TryAdd(i, false);
            }

            for (int i = 0; i < 49; i++)
            {
                s_save.UnbeatenLevels.Add(i + 1);
            }

            s_hasSeedTypeHook = _hooks!.CreateHook<HasSeedType>(HasSeedTypeImpl, 0x456FE0).Activate();
            s_coinGetFinalSeedPacketTypeHook = _hooks!.CreateHook<CoinGetFinalSeedPacketType>(CoinGetFinalSeedPackeTypeImpl, 0x434520).Activate();
            s_boardChooseSeedsOnLevelHook = _hooks!.CreateHook<BoardChooseSeedsOnLevel>(BoardChooseSeedsOnLevelImpl, 0x40E5D0).Activate();
            s_lawnAppGetAvailableSeedsHook = _hooks!.CreateHook<LawnAppGetAvailableSeeds>(LawnAppGetAvailableSeedsImpl, 0x456F90).Activate();
            s_awardScreenStartButtonPressedHook = _hooks!.CreateHook<AwardScreenStartButtonPressed>(AwardScreenStartButtonPressedImpl, 0x409530).Activate();
            s_gameSelectorAdventureClickedHook = _hooks!.CreateHook<GameSelectorAdventureClicked>(GameSelectorAdventureClickedImpl, 0x44F270).Activate();

            if(_configuration.AlwaysAllowCoinDrops)
            {
                // patch Board::CanDropLoot() to always return true
                byte[] asm = { 0xB0, 0x01, 0xC3 };
                _hooks!.CreateAsmHook(asm, 0x4200C5).Activate();
            }

            if(_configuration.StoreAlwaysAvailable)
            {
                // patch SeedChooserScreen::SeedChooserScreen to always enable the store button
                byte[] asm = {
                0x8B, 0x95, 0xAC, 0x00, 0x00, 0x00,
                0xC6, 0x82, 0xFD, 0x00, 0x00, 0x00, 0x00,
                0x8B, 0x85, 0xAC, 0x00, 0x00, 0x00,
                0xC6, 0x40, 0x1A, 0x00};
                _hooks!.CreateAsmHook(asm, 0x48EA14).Activate();
            }

            if(_configuration.StoreItemsAlwaysAvailable)
            {
                // patch StoreScreen::IsItemUnavailable to always return false
                byte[] asm = { 0xB0, 0x00, 0x5F, 0xC3 };
                _hooks!.CreateAsmHook(asm, 0x4956C3).Activate();
            }
        }

        public class RandomizerSave
        {
            public Dictionary<SeedType, bool> CollectedSeeds = new();
            public List<int> UnbeatenLevels = new();
        }

        #region Utility Methods

        private static void SaveProgress()
        {
            File.WriteAllText(s_saveName, JsonConvert.SerializeObject(s_save, Formatting.Indented));
        }

        private static void LoadProgress()
        {
            if(File.Exists(s_saveName))
            {
                s_save = JsonConvert.DeserializeObject<RandomizerSave>(File.ReadAllText(s_saveName))!;
            }
        }

        private static bool IsNightLevel(int level)
        {
            return level >= 11 && level <= 19 && level != 15;
        }

        private static bool IsPoolLevel(int level)
        {
            return level >= 21 && level <= 29 && level != 25;
        }

        private static bool IsFogLevel(int level)
        {
            return level >= 31 && level <= 39 && level != 35;
        }

        private static bool IsRoofLevel(int level)
        {
            return level >= 41 && level <= 49 && level != 45;
        }

        private static bool IsMinigameLevel(int level)
        {
            return level % 5 == 0;
        }

        private static bool IsUpgradePlant(SeedType type)
        {
            return type >= SeedType.SEED_GATLINGPEA && type < SeedType.SEED_IMITATER;
        }

        private static bool HasRequirementsForUpgradePlant(SeedType upgradeSeedType)
        {
            switch (upgradeSeedType)
            {
                case SeedType.SEED_GATLINGPEA:
                    return s_save.CollectedSeeds[SeedType.SEED_REPEATER] == true;
                case SeedType.SEED_TWINSUNFLOWER:
                    return s_save.CollectedSeeds[SeedType.SEED_SUNFLOWER] == true;
                case SeedType.SEED_GLOOMSHROOM:
                    return s_save.CollectedSeeds[SeedType.SEED_FUMESHROOM] == true;
                case SeedType.SEED_CATTAIL:
                    return s_save.CollectedSeeds[SeedType.SEED_LILYPAD] == true;
                case SeedType.SEED_WINTERMELON:
                    return s_save.CollectedSeeds[SeedType.SEED_MELONPULT] == true;
                case SeedType.SEED_GOLD_MAGNET:
                    return s_save.CollectedSeeds[SeedType.SEED_MAGNETSHROOM] == true;
                case SeedType.SEED_SPIKEROCK:
                    return s_save.CollectedSeeds[SeedType.SEED_SPIKEWEED] == true;
                case SeedType.SEED_COBCANNON:
                    return s_save.CollectedSeeds[SeedType.SEED_KERNELPULT] == true;
            }
            return false;
        }

        private static SeedType GetRandomSeedAward()
        {
            List<SeedType> lockedSeeds = s_save.CollectedSeeds.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();

            SeedType randomType = lockedSeeds[s_random.Next(lockedSeeds.Count - 1)];

            if (IsUpgradePlant(randomType))
            {
                if (!s_mod._configuration.DropUpgradePlants)
                {
                    while (true)
                    {
                        randomType = lockedSeeds[s_random.Next(lockedSeeds.Count - 1)];
                        if (!IsUpgradePlant(randomType))
                        {
                            return randomType;
                        }
                    }
                }
                else
                {
                    if (!HasRequirementsForUpgradePlant(randomType))
                    {
                        while (true)
                        {
                            randomType = lockedSeeds[s_random.Next(lockedSeeds.Count - 1)];
                            if (!IsUpgradePlant(randomType))
                            {
                                return randomType;
                            }
                            else if (HasRequirementsForUpgradePlant(randomType))
                            {
                                return randomType;
                            }
                        }
                    }
                }
            }

            return randomType;
        }

        #endregion

        #region GameSelector Hooks

        [Function(CallingConventions.Stdcall)]
        public delegate void GameSelectorAdventureClicked(GameSelector* self);

        public void GameSelectorAdventureClickedImpl(GameSelector* self)
        {
            if(self->App->PlayerInfo->Level == 1)
            {
                SaveProgress();
            }
            else
            {
                LoadProgress();
            }
            s_gameSelectorAdventureClickedHook.OriginalFunction(self);
        }

        #endregion

        #region LawnApp Hooks

        [Function(new[] { Register.edi, Register.esi }, Register.eax, StackCleanup.Caller)]
        public delegate bool HasSeedType(SeedType theSeedType, LawnApp* self);

        private static bool HasSeedTypeImpl(SeedType theSeedType, LawnApp* self)
        {
            if (theSeedType >= SeedType.SEED_IMITATER)
            {
                return s_hasSeedTypeHook.OriginalFunction(theSeedType, self);
            }
            if (IsUpgradePlant(theSeedType))
            {
                switch (theSeedType)
                {
                    case SeedType.SEED_GATLINGPEA:
                        {
                            bool owns = self->PlayerInfo->OwnsGatlingPea > 0 || s_save.CollectedSeeds[SeedType.SEED_GATLINGPEA];
                            if (owns)
                                s_save.CollectedSeeds[SeedType.SEED_GATLINGPEA] = true;
                            return owns;
                        }
                    case SeedType.SEED_TWINSUNFLOWER:
                        {
                            bool owns = self->PlayerInfo->OwnsTwinSunflower > 0 || s_save.CollectedSeeds[SeedType.SEED_TWINSUNFLOWER];
                            if (owns)
                                s_save.CollectedSeeds[SeedType.SEED_TWINSUNFLOWER] = true;
                            return owns;
                        }
                    case SeedType.SEED_GLOOMSHROOM:
                        {
                            bool owns = self->PlayerInfo->OwnsGloom > 0 || s_save.CollectedSeeds[SeedType.SEED_GLOOMSHROOM];
                            if (owns)
                                s_save.CollectedSeeds[SeedType.SEED_GLOOMSHROOM] = true;
                            return owns;
                        }
                    case SeedType.SEED_CATTAIL:
                        {
                            bool owns = self->PlayerInfo->OwnsCattail > 0 || s_save.CollectedSeeds[SeedType.SEED_CATTAIL];
                            if (owns)
                                s_save.CollectedSeeds[SeedType.SEED_CATTAIL] = true;
                            return owns;
                        }
                    case SeedType.SEED_WINTERMELON:
                        {
                            bool owns = self->PlayerInfo->OwnsWintermelon > 0 || s_save.CollectedSeeds[SeedType.SEED_WINTERMELON];
                            if (owns)
                                s_save.CollectedSeeds[SeedType.SEED_WINTERMELON] = true;
                            return owns;
                        }
                    case SeedType.SEED_GOLD_MAGNET:
                        {
                            bool owns = self->PlayerInfo->OwnsGoldMagnet > 0 || s_save.CollectedSeeds[SeedType.SEED_GOLD_MAGNET];
                            if (owns)
                                s_save.CollectedSeeds[SeedType.SEED_GOLD_MAGNET] = true;
                            return owns;
                        }
                    case SeedType.SEED_SPIKEROCK:
                        {
                            bool owns = self->PlayerInfo->OwnsSpikerock > 0 || s_save.CollectedSeeds[SeedType.SEED_SPIKEROCK];
                            if (owns)
                                s_save.CollectedSeeds[SeedType.SEED_SPIKEROCK] = true;
                            return owns;
                        }
                    case SeedType.SEED_COBCANNON:
                        {
                            bool owns = self->PlayerInfo->OwnsCobcannon > 0 || s_save.CollectedSeeds[SeedType.SEED_COBCANNON];
                            if (owns)
                                s_save.CollectedSeeds[SeedType.SEED_COBCANNON] = true;
                            return owns;
                        }
                }
            }
            return s_save.CollectedSeeds[theSeedType];
        }

        [Function(new[] { Register.eax }, Register.eax, StackCleanup.Caller)]
        public delegate int LawnAppGetAvailableSeeds(LawnApp* self);

        private static int LawnAppGetAvailableSeedsImpl(LawnApp* self)
        {
            return s_save.CollectedSeeds.Count(kv => kv.Value);
        }

        #endregion

        #region Board Hooks

        [Function(new[] { Register.edi }, Register.eax, StackCleanup.Caller)]
        public delegate bool BoardChooseSeedsOnLevel(Board* self);

        private static bool BoardChooseSeedsOnLevelImpl(Board* self)
        {
            if(self->Level == 1)
            {
                return false;
            }
            else
            {
                bool isMinigame = IsMinigameLevel(self->Level);
                return isMinigame ? false : true;
            }
        }

        #endregion

        #region AwardScreen Hooks

        [Function(CallingConventions.Stdcall)]
        public delegate void AwardScreenStartButtonPressed(AwardScreen* self);

        public static void AwardScreenStartButtonPressedImpl(AwardScreen* self)
        {
            if (self->AwardType == 0)
            {
                int beatenLevel = self->App->PlayerInfo->Level - 1;
                s_save.UnbeatenLevels.Remove(beatenLevel);

                int newLevel = s_save.UnbeatenLevels.Count > 0 ? s_save.UnbeatenLevels[s_random.Next(s_save.UnbeatenLevels.Count - 1)] : 50;
                self->App->PlayerInfo->Level = newLevel;

                s_finalSeedPacketRewarded = false;
                if (s_mod._configuration.ForceMandatoryPlants)
                {
                    if ((IsNightLevel(newLevel) || IsFogLevel(newLevel)) && !s_save.CollectedSeeds[SeedType.SEED_PUFFSHROOM])
                    {
                        s_save.CollectedSeeds[SeedType.SEED_PUFFSHROOM] = true;
                    }
                    if (IsPoolLevel(newLevel) && !s_save.CollectedSeeds[SeedType.SEED_LILYPAD])
                    {
                        s_save.CollectedSeeds[SeedType.SEED_LILYPAD] = true;
                    }
                    if (IsFogLevel(newLevel) && (!s_save.CollectedSeeds[SeedType.SEED_LILYPAD] || !s_save.CollectedSeeds[SeedType.SEED_SEASHROOM]))
                    {
                        s_save.CollectedSeeds[SeedType.SEED_LILYPAD] = true;
                        s_save.CollectedSeeds[SeedType.SEED_SEASHROOM] = true;
                    }
                    if (IsRoofLevel(newLevel) && !s_save.CollectedSeeds[SeedType.SEED_FLOWERPOT])
                    {
                        s_save.CollectedSeeds[SeedType.SEED_FLOWERPOT] = true;
                    }
                }
                SaveProgress();
            }
            s_awardScreenStartButtonPressedHook.OriginalFunction(self);
        }

        #endregion

        #region Coin Hooks

        [Function(CallingConventions.MicrosoftThiscall)]
        public delegate SeedType CoinGetFinalSeedPacketType(Coin* self);

        private static SeedType CoinGetFinalSeedPackeTypeImpl(Coin* self)
        {
            if (!s_finalSeedPacketRewarded)
            {
                SeedType type = s_mod._configuration.RandomizeSeedAwards ? GetRandomSeedAward() : s_coinGetFinalSeedPacketTypeHook.OriginalFunction(self);
                s_finalSeedPacketReward = type;
                s_finalSeedPacketRewarded = true;
                s_save.CollectedSeeds[type] = true;
                return type;
            }
            return s_finalSeedPacketReward;
        }

        #endregion

        #region PvZ Structure Definitions

        [StructLayout(LayoutKind.Explicit)]
        public struct Board
        {
            [FieldOffset(0x174)]
            public LawnApp* App;
            [FieldOffset(0x918)]
            public GameMode GameMode;
            [FieldOffset(0x5568)]
            public int Level;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct LawnApp
        {
            [FieldOffset(0x94C)]
            public PlayerInfo* PlayerInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct PlayerInfo
        {
            [FieldOffset(0x4C)]
            public int Level;
            [FieldOffset(0x54)]
            public int NumTimesFinishedAdventure;
            [FieldOffset(0x1E8)]
            public int OwnsGatlingPea;
            [FieldOffset(0x1EC)]
            public int OwnsTwinSunflower;
            [FieldOffset(0x1F0)]
            public int OwnsGloom;
            [FieldOffset(0x1F4)]
            public int OwnsCattail;
            [FieldOffset(0x1F8)]
            public int OwnsWintermelon;
            [FieldOffset(0x1FC)]
            public int OwnsGoldMagnet;
            [FieldOffset(0x200)]
            public int OwnsSpikerock;
            [FieldOffset(0x204)]
            public int OwnsCobcannon;
            [FieldOffset(0x208)]
            public int OwnsImitater;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct AwardScreen
        {
            [FieldOffset(0xB0)]
            public LawnApp* App;
            [FieldOffset(0xB8)]
            public int AwardType;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct GameSelector
        {
            [FieldOffset(0xA4)]
            public LawnApp* App;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct Coin
        {
            [FieldOffset(0x0)]
            public LawnApp* App;
            [FieldOffset(0x4)]
            public Board* Board;
            [FieldOffset(0x58)]
            public CoinType Type;
        }

        #endregion

        #region PvZ Enums

        public enum CoinType : int
        {
            COIN_NONE = 0x0,
            COIN_SILVER = 0x1,
            COIN_GOLD = 0x2,
            COIN_DIAMOND = 0x3,
            COIN_SUN = 0x4,
            COIN_SMALLSUN = 0x5,
            COIN_LARGESUN = 0x6,
            COIN_FINAL_SEED_PACKET = 0x7,
            COIN_TROPHY = 0x8,
            COIN_SHOVEL = 0x9,
            COIN_ALMANAC = 0xA,
            COIN_CARKEYS = 0xB,
            COIN_VASE = 0xC,
            COIN_WATERING_CAN = 0xD,
            COIN_TACO = 0xE,
            COIN_NOTE = 0xF,
            COIN_USABLE_SEED_PACKET = 0x10,
            COIN_PRESENT_PLANT = 0x11,
            COIN_AWARD_MONEY_BAG = 0x12,
            COIN_AWARD_PRESENT = 0x13,
            COIN_AWARD_BAG_DIAMOND = 0x14,
            COIN_AWARD_SILVER_SUNFLOWER = 0x15,
            COIN_AWARD_GOLD_SUNFLOWER = 0x16,
            COIN_CHOCOLATE = 0x17,
            COIN_AWARD_CHOCOLATE = 0x18,
            COIN_PRESENT_MINIGAMES = 0x19,
            COIN_PRESENT_PUZZLE_MODE = 0x1A,
        };

        public enum SeedType : int
        {
            SEED_PEASHOOTER = 0x0,
            SEED_SUNFLOWER = 0x1,
            SEED_CHERRYBOMB = 0x2,
            SEED_WALLNUT = 0x3,
            SEED_POTATOMINE = 0x4,
            SEED_SNOWPEA = 0x5,
            SEED_CHOMPER = 0x6,
            SEED_REPEATER = 0x7,
            SEED_PUFFSHROOM = 0x8,
            SEED_SUNSHROOM = 0x9,
            SEED_FUMESHROOM = 0xA,
            SEED_GRAVEBUSTER = 0xB,
            SEED_HYPNOSHROOM = 0xC,
            SEED_SCAREDYSHROOM = 0xD,
            SEED_ICESHROOM = 0xE,
            SEED_DOOMSHROOM = 0xF,
            SEED_LILYPAD = 0x10,
            SEED_SQUASH = 0x11,
            SEED_THREEPEATER = 0x12,
            SEED_TANGLEKELP = 0x13,
            SEED_JALAPENO = 0x14,
            SEED_SPIKEWEED = 0x15,
            SEED_TORCHWOOD = 0x16,
            SEED_TALLNUT = 0x17,
            SEED_SEASHROOM = 0x18,
            SEED_PLANTERN = 0x19,
            SEED_CACTUS = 0x1A,
            SEED_BLOVER = 0x1B,
            SEED_SPLITPEA = 0x1C,
            SEED_STARFRUIT = 0x1D,
            SEED_PUMPKINSHELL = 0x1E,
            SEED_MAGNETSHROOM = 0x1F,
            SEED_CABBAGEPULT = 0x20,
            SEED_FLOWERPOT = 0x21,
            SEED_KERNELPULT = 0x22,
            SEED_INSTANT_COFFEE = 0x23,
            SEED_GARLIC = 0x24,
            SEED_UMBRELLA = 0x25,
            SEED_MARIGOLD = 0x26,
            SEED_MELONPULT = 0x27,
            SEED_GATLINGPEA = 0x28,
            SEED_TWINSUNFLOWER = 0x29,
            SEED_GLOOMSHROOM = 0x2A,
            SEED_CATTAIL = 0x2B,
            SEED_WINTERMELON = 0x2C,
            SEED_GOLD_MAGNET = 0x2D,
            SEED_SPIKEROCK = 0x2E,
            SEED_COBCANNON = 0x2F,
            SEED_IMITATER = 0x30,
            SEED_EXPLODE_O_NUT = 0x31,
            SEED_GIANT_WALLNUT = 0x32,
            SEED_SPROUT = 0x33,
            SEED_LEFTPEATER = 0x34,
            NUM_SEED_TYPES = 0x35,
            SEED_BEGHOULED_BUTTON_SHUFFLE = 0x36,
            SEED_BEGHOULED_BUTTON_CRATER = 0x37,
            SEED_SLOT_MACHINE_SUN = 0x38,
            SEED_SLOT_MACHINE_DIAMOND = 0x39,
            SEED_ZOMBIQUARIUM_SNORKLE = 0x3A,
            SEED_ZOMBIQUARIUM_TROPHY = 0x3B,
            SEED_ZOMBIE_NORMAL = 0x3C,
            SEED_ZOMBIE_TRAFFIC_CONE = 0x3D,
            SEED_ZOMBIE_POLEVAULTER = 0x3E,
            SEED_ZOMBIE_PAIL = 0x3F,
            SEED_ZOMBIE_LADDER = 0x40,
            SEED_ZOMBIE_DIGGER = 0x41,
            SEED_ZOMBIE_BUNGEE = 0x42,
            SEED_ZOMBIE_FOOTBALL = 0x43,
            SEED_ZOMBIE_BALLOON = 0x44,
            SEED_ZOMBIE_SCREEN_DOOR = 0x45,
            SEED_ZOMBONI = 0x46,
            SEED_ZOMBIE_POGO = 0x47,
            SEED_ZOMBIE_DANCER = 0x48,
            SEED_ZOMBIE_GARGANTUAR = 0x49,
            SEED_ZOMBIE_IMP = 0x4A,
            NUM_SEEDS_IN_CHOOSER = 0x31,
            SEED_NONE = -1,
        };

        public enum GameMode : int
        {
            GAMEMODE_ADVENTURE = 0x0,
            GAMEMODE_SURVIVAL_NORMAL_STAGE_1 = 0x1,
            GAMEMODE_SURVIVAL_NORMAL_STAGE_2 = 0x2,
            GAMEMODE_SURVIVAL_NORMAL_STAGE_3 = 0x3,
            GAMEMODE_SURVIVAL_NORMAL_STAGE_4 = 0x4,
            GAMEMODE_SURVIVAL_NORMAL_STAGE_5 = 0x5,
            GAMEMODE_SURVIVAL_HARD_STAGE_1 = 0x6,
            GAMEMODE_SURVIVAL_HARD_STAGE_2 = 0x7,
            GAMEMODE_SURVIVAL_HARD_STAGE_3 = 0x8,
            GAMEMODE_SURVIVAL_HARD_STAGE_4 = 0x9,
            GAMEMODE_SURVIVAL_HARD_STAGE_5 = 0xA,
            GAMEMODE_SURVIVAL_ENDLESS_STAGE_1 = 0xB,
            GAMEMODE_SURVIVAL_ENDLESS_STAGE_2 = 0xC,
            GAMEMODE_SURVIVAL_ENDLESS_STAGE_3 = 0xD,
            GAMEMODE_SURVIVAL_ENDLESS_STAGE_4 = 0xE,
            GAMEMODE_SURVIVAL_ENDLESS_STAGE_5 = 0xF,
            GAMEMODE_CHALLENGE_WAR_AND_PEAS = 0x10,
            GAMEMODE_CHALLENGE_WALLNUT_BOWLING = 0x11,
            GAMEMODE_CHALLENGE_SLOT_MACHINE = 0x12,
            GAMEMODE_CHALLENGE_RAINING_SEEDS = 0x13,
            GAMEMODE_CHALLENGE_BEGHOULED = 0x14,
            GAMEMODE_CHALLENGE_INVISIGHOUL = 0x15,
            GAMEMODE_CHALLENGE_SEEING_STARS = 0x16,
            GAMEMODE_CHALLENGE_ZOMBIQUARIUM = 0x17,
            GAMEMODE_CHALLENGE_BEGHOULED_TWIST = 0x18,
            GAMEMODE_CHALLENGE_LITTLE_TROUBLE = 0x19,
            GAMEMODE_CHALLENGE_PORTAL_COMBAT = 0x1A,
            GAMEMODE_CHALLENGE_COLUMN = 0x1B,
            GAMEMODE_CHALLENGE_BOBSLED_BONANZA = 0x1C,
            GAMEMODE_CHALLENGE_SPEED = 0x1D,
            GAMEMODE_CHALLENGE_WHACK_A_ZOMBIE = 0x1E,
            GAMEMODE_CHALLENGE_LAST_STAND = 0x1F,
            GAMEMODE_CHALLENGE_WAR_AND_PEAS_2 = 0x20,
            GAMEMODE_CHALLENGE_WALLNUT_BOWLING_2 = 0x21,
            GAMEMODE_CHALLENGE_POGO_PARTY = 0x22,
            GAMEMODE_CHALLENGE_FINAL_BOSS = 0x23,
            GAMEMODE_CHALLENGE_ART_CHALLENGE_1 = 0x24,
            GAMEMODE_CHALLENGE_SUNNY_DAY = 0x25,
            GAMEMODE_CHALLENGE_RESODDED = 0x26,
            GAMEMODE_CHALLENGE_BIG_TIME = 0x27,
            GAMEMODE_CHALLENGE_ART_CHALLENGE_2 = 0x28,
            GAMEMODE_CHALLENGE_AIR_RAID = 0x29,
            GAMEMODE_CHALLENGE_ICE = 0x2A,
            GAMEMODE_CHALLENGE_ZEN_GARDEN = 0x2B,
            GAMEMODE_CHALLENGE_HIGH_GRAVITY = 0x2C,
            GAMEMODE_CHALLENGE_GRAVE_DANGER = 0x2D,
            GAMEMODE_CHALLENGE_SHOVEL = 0x2E,
            GAMEMODE_CHALLENGE_STORMY_NIGHT = 0x2F,
            GAMEMODE_CHALLENGE_BUNGEE_BLITZ = 0x30,
            GAMEMODE_CHALLENGE_SQUIRREL = 0x31,
            GAMEMODE_TREE_OF_WISDOM = 0x32,
            GAMEMODE_SCARY_POTTER_1 = 0x33,
            GAMEMODE_SCARY_POTTER_2 = 0x34,
            GAMEMODE_SCARY_POTTER_3 = 0x35,
            GAMEMODE_SCARY_POTTER_4 = 0x36,
            GAMEMODE_SCARY_POTTER_5 = 0x37,
            GAMEMODE_SCARY_POTTER_6 = 0x38,
            GAMEMODE_SCARY_POTTER_7 = 0x39,
            GAMEMODE_SCARY_POTTER_8 = 0x3A,
            GAMEMODE_SCARY_POTTER_9 = 0x3B,
            GAMEMODE_SCARY_POTTER_ENDLESS = 0x3C,
            GAMEMODE_PUZZLE_I_ZOMBIE_1 = 0x3D,
            GAMEMODE_PUZZLE_I_ZOMBIE_2 = 0x3E,
            GAMEMODE_PUZZLE_I_ZOMBIE_3 = 0x3F,
            GAMEMODE_PUZZLE_I_ZOMBIE_4 = 0x40,
            GAMEMODE_PUZZLE_I_ZOMBIE_5 = 0x41,
            GAMEMODE_PUZZLE_I_ZOMBIE_6 = 0x42,
            GAMEMODE_PUZZLE_I_ZOMBIE_7 = 0x43,
            GAMEMODE_PUZZLE_I_ZOMBIE_8 = 0x44,
            GAMEMODE_PUZZLE_I_ZOMBIE_9 = 0x45,
            GAMEMODE_PUZZLE_I_ZOMBIE_ENDLESS = 0x46,
            NUM_GAME_MODES = 0x47,
        };

        #endregion

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            // Apply settings from configuration.
            // ... your code here.
            _configuration = configuration;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}
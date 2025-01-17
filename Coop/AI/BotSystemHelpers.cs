﻿//using SIT.Core.Misc;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Reflection;

//namespace SIT.Tarkov.Core.AI
//{
//    public class BotSystemHelpers
//    {
//        public static Type BotControllerType { get; set; }
//        public static Type BotPresetType { get; set; }
//        public static Type BotScatteringType { get; set; }
//        public static Type BossSpawnRunnerType { get; set; }
//        public static Type ProfileCreatorType { get; set; }
//        public static Type BotCreatorType { get; set; }
//        public static Type RoleLimitDifficultyType { get; set; }
//        public static Type LocationBaseType { get; set; }

//        public static Dictionary<string, Type> TypeDictionary { get; } = new Dictionary<string, Type>();

//        public static Object BotControllerInstance { get; set; }
//        public static MethodInfo SetSettingsMethod { get; set; }
//        public static MethodInfo InitMethod { get; set; }
//        public static MethodInfo StopMethod { get; set; }
//        public static MethodInfo AddActivePlayerMethod { get; set; }

//        public static BepInEx.Logging.ManualLogSource Logger { get; set; }

//        static BotSystemHelpers()
//        {
//            Setup();
//        }

//        public static void Setup()
//        {
//            Logger = BepInEx.Logging.Logger.CreateLogSource("SIT.Core.BotSystemHelpers");
//            //Logger = PatchConstants.Logger;

//            if (BotControllerType == null)
//                //BotControllerType = PatchConstants.EftTypes.Single(x => ReflectionHelpers.GetMethodForType(x, "AddActivePLayer") != null);
//                BotControllerType = PatchConstants.EftTypes.Single(x =>
//                    x.GetMethod("SetSettings", BindingFlags.Public | BindingFlags.Instance) != null
//                    && x.GetMethod("AddActivePLayer", BindingFlags.Public | BindingFlags.Instance) != null
//                );

//            //Logger.LogInfo($"BotControllerType:{BotControllerType.Name}");

//            if (BotPresetType == null)
//                BotPresetType = PatchConstants.EftTypes.Single(x => x.IsClass
//                    && ReflectionHelpers.GetFieldFromType(x, "BotDifficulty") != null
//                    && ReflectionHelpers.GetFieldFromType(x, "Role") != null
//                    && ReflectionHelpers.GetFieldFromType(x, "UseThis") != null
//                    && ReflectionHelpers.GetFieldFromType(x, "VisibleAngle") != null
//                    );

//            //Logger.LogInfo($"BotPresetType:{BotPresetType.Name}");

//            if (BotScatteringType == null)
//                BotScatteringType = PatchConstants.EftTypes.Single(x => x.IsClass
//                    && ReflectionHelpers.GetFieldFromType(x, "PriorityScatter1meter") != null
//                    && ReflectionHelpers.GetFieldFromType(x, "PriorityScatter10meter") != null
//                    && ReflectionHelpers.GetFieldFromType(x, "PriorityScatter100meter") != null
//                    && ReflectionHelpers.GetMethodForType(x, "Check") != null
//                    );

//            //Logger.LogInfo($"BotScatteringType:{BotScatteringType.Name}");

//            if (BossSpawnRunnerType == null)
//                BossSpawnRunnerType = PatchConstants.EftTypes.Single(x => x.IsClass
//                    && ReflectionHelpers.GetPropertyFromType(x, "HaveSectants") != null
//                    && ReflectionHelpers.GetPropertyFromType(x, "BossSpawnWaves") != null
//                    && x.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance) != null
//                    );

//            //Logger.LogInfo($"BossSpawnRunnerType:{BossSpawnRunnerType.Name}");

//            //if (ProfileCreatorType == null)
//            //    ProfileCreatorType = typeof(BotPresetClass);
//            //    //ProfileCreatorType = PatchConstants.EftTypes.Last(x => x.IsClass
//            //    //    && x.GetMethod("GetNewProfile", BindingFlags.NonPublic | BindingFlags.Instance) != null
//            //    //    && x.GetMethod("GetNewProfile", BindingFlags.NonPublic | BindingFlags.Instance) != null
//            //    //    );

//            //Logger.LogInfo($"ProfileCreatorType:{ProfileCreatorType.Name}");

//            if (BotCreatorType == null)
//                BotCreatorType = PatchConstants.EftTypes.Single(x => x.IsClass
//                    && ReflectionHelpers.GetPropertyFromType(x, "StartProfilesLoaded") != null
//                    && x.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == "ActivateBot")
//                    && x.GetMethod("method_0", BindingFlags.NonPublic | BindingFlags.Instance) != null
//                    && x.GetMethod("method_1", BindingFlags.NonPublic | BindingFlags.Instance) != null
//                    && x.GetMethod("method_2", BindingFlags.NonPublic | BindingFlags.Instance) != null
//                    );

//            //Logger.LogInfo($"BotCreatorType:{BotCreatorType.Name}");

//            if (RoleLimitDifficultyType == null)
//                RoleLimitDifficultyType = PatchConstants.EftTypes.First(x => x.IsClass
//                    && ReflectionHelpers.GetFieldFromType(x, "Role") != null
//                    && ReflectionHelpers.GetFieldFromType(x, "Limit") != null
//                    && ReflectionHelpers.GetFieldFromType(x, "Difficulty") != null
//                    );

//            if (LocationBaseType == null)
//                LocationBaseType = PatchConstants.EftTypes.First(x => x.IsClass
//                    && ReflectionHelpers.GetFieldFromType(x, "OpenZones") != null
//                    && ReflectionHelpers.GetFieldFromType(x, "DisabledForScav") != null
//                    && ReflectionHelpers.GetFieldFromType(x, "DisabledScavExits") != null
//                    );

//            //Logger.LogInfo($"LocationBaseType:{LocationBaseType.Name}");

//            if (!TypeDictionary.ContainsKey("BotOwner"))
//            {
//                TypeDictionary.Add("BotOwner", typeof(EFT.BotOwner));
//                TypeDictionary.Add("BotBrain", ReflectionHelpers.GetPropertyFromType(TypeDictionary["BotOwner"], "Brain").PropertyType);
//                TypeDictionary.Add("BotBaseBrain", ReflectionHelpers.GetPropertyFromType(TypeDictionary["BotBrain"], "BaseBrain").PropertyType);
//                TypeDictionary.Add("BotAgent", ReflectionHelpers.GetPropertyFromType(TypeDictionary["BotBrain"], "Agent").PropertyType);
//            }

//            if (SetSettingsMethod == null)
//                SetSettingsMethod = ReflectionHelpers.GetMethodForType(BotControllerType, "SetSettings");

//            //Logger.LogInfo($"SetSettingsMethod:{SetSettingsMethod.Name}");

//            if (StopMethod == null)
//                StopMethod = ReflectionHelpers.GetMethodForType(BotControllerType, "Stop");

//            //Logger.LogInfo($"StopMethod:{StopMethod.Name}");

//            if (InitMethod == null)
//                InitMethod = ReflectionHelpers.GetMethodForType(BotControllerType, "Init");

//            //Logger.LogInfo($"InitMethod:{InitMethod.Name}");

//            if (AddActivePlayerMethod == null)
//                AddActivePlayerMethod = ReflectionHelpers.GetMethodForType(BotControllerType, "AddActivePLayer");

//            //Logger.LogInfo($"AddActivePlayerMethod:{AddActivePlayerMethod.Name}");
//        }

//        public static void AddActivePlayer(EFT.Player player)
//        {
//            if (BotControllerInstance == null)
//            {
//                PatchConstants.Logger.LogInfo("Can't AddActivePlayer when BotSystemInstance is NULL");
//                return;
//            }

//            //Logger.LogInfo($"AddActivePlayer:{player.Profile.AccountId}");

//            //AddActivePlayerMethod?.Invoke(BotControllerInstance, new object[] { player });
//        }

//        public static void SetSettings(int maxCount, Array botPresets, Array botScattering)
//        {
//            if (BotControllerInstance == null)
//            {
//                PatchConstants.Logger.LogInfo("Can't SetSettings when BotSystemInstance is NULL");
//                return;
//            }

//            SetSettingsMethod?.Invoke(BotControllerInstance
//                , new object[] { 0, botPresets, botScattering });
//        }

//        public static void SetSettingsNoBots()
//        {
//            if (BotControllerInstance == null)
//            {
//                PatchConstants.Logger.LogInfo("Can't SetSettings when BotSystemInstance is NULL");
//                return;
//            }

//            var botPresets = Array.CreateInstance(BotPresetType, 0);
//            var botScattering = Array.CreateInstance(BotScatteringType, 0);

//            SetSettingsMethod?.Invoke(BotControllerInstance
//                , new object[] { 0, botPresets, botScattering });
//        }

//        public static void Init(
//            object botGame
//            , object botCreator
//            , BotZone[] botZones
//            , object spawnSystem
//            , BotLocationModifier botLocationModifier
//            , bool botEnable
//            , bool freeForAll
//            , bool enableWaveControl
//            , bool online
//            , bool haveSectants
//            , object players
//            , string openZones)
//        {
//            if (BotControllerInstance == null)
//            {
//                PatchConstants.Logger.LogInfo("Can't Init when BotControllerInstance is NULL");
//                return;
//            }



//            InitMethod?.Invoke(BotControllerInstance
//                , new object[] {

//                    botGame
//                    , botCreator
//                    , botZones
//                    , spawnSystem
//                    , botLocationModifier
//                    , botEnable
//                    , freeForAll
//                    , enableWaveControl
//                    , online
//                    , haveSectants
//                    , players
//                    , openZones

//                });
//        }

//        public static void Stop()
//        {
//            if (BotControllerInstance == null)
//            {
//                PatchConstants.Logger.LogInfo("Can't Stop when BotSystemInstance is NULL");
//                return;
//            }

//            StopMethod?.Invoke(BotControllerInstance
//                , new object[] { });
//        }

//        //public static void SetBotBrain(EFT.Player player, object brain)
//        //{
//        //    var ai = ReflectionHelpers.GetFieldOrPropertyFromInstance<object>(player, "AIData");
//        //    if (ai != null)
//        //    {
//        //        var botOwner = ReflectionHelpers.GetFieldOrPropertyFromInstance<object>(ai, "BotOwner");
//        //        if (botOwner != null)
//        //        {
//        //            var botBrain = ReflectionHelpers.GetFieldOrPropertyFromInstance<object>(botOwner, "Brain");
//        //            if (botBrain != null)
//        //            {
//        //                PatchConstants.SetFieldOrPropertyFromInstance(player, "BaseBrain", brain);
//        //            }
//        //        }
//        //    }
//        //}
//    }
//}

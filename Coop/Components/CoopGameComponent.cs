﻿using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sirenix.Utilities;
using SIT.Coop.Core.Matchmaker;
using SIT.Coop.Core.Player;
using SIT.Core.Coop.Player;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SIT.Core.Coop
{
    /// <summary>
    /// Coop Game Component is the User 1-2-1 communication to the Server
    /// </summary>
    public class CoopGameComponent : MonoBehaviour, IFrameIndexer
    {
        #region Fields/Properties        
        public WorldInteractiveObject[] ListOfInteractiveObjects { get; set; }
        private Request RequestingObj { get; set; }
        public string ServerId { get; set; } = null;
        public ConcurrentDictionary<string, EFT.Player> Players { get; private set; } = new();
        public ConcurrentQueue<Dictionary<string, object>> QueuedPackets { get; } = new();

        BepInEx.Logging.ManualLogSource Logger { get; set; }

        public static Vector3? ClientSpawnLocation { get; set; }

        private long ReadFromServerLastActionsLastTime { get; set; } = -1;
        private long ApproximatePing { get; set; } = 1;

        public ConcurrentDictionary<string, ESpawnState> PlayersToSpawn { get; private set; } = new();
        public ConcurrentDictionary<string, Dictionary<string, object>> PlayersToSpawnPacket { get; private set; } = new();
        public ConcurrentDictionary<string, Profile> PlayersToSpawnProfiles { get; private set; } = new();
        public ConcurrentDictionary<string, Vector3> PlayersToSpawnPositions { get; private set; } = new();
        public ulong LocalIndex { get; set; }

        public double LocalTime => 0;

        public bool DEBUGSpawnDronesOnServer { get; set; }
        public bool DEBUGShowPlayerList { get; set; }

        #endregion

        #region Public Voids
        public static CoopGameComponent GetCoopGameComponent()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
                return null;

            var coopGC = gameWorld.GetComponent<CoopGameComponent>();
            return coopGC;
        }
        public static string GetServerId()
        {
            var coopGC = GetCoopGameComponent();
            if (coopGC == null)
                return null;

            return coopGC.ServerId;
        }
        #endregion

        #region Unity Component Methods

        void Awake()
        {

            // ----------------------------------------------------
            // Create a BepInEx Logger for CoopGameComponent
            Logger = BepInEx.Logging.Logger.CreateLogSource("CoopGameComponent");
        }

        void Start()
        {
            Logger.LogInfo("CoopGameComponent:Start");

            // ----------------------------------------------------
            // Always clear "Players" when creating a new CoopGameComponent
            Players = new ConcurrentDictionary<string, EFT.Player>();
            var ownPlayer = (EFT.LocalPlayer)Singleton<GameWorld>.Instance.RegisteredPlayers.First(x => x.IsYourPlayer);
            Players.TryAdd(ownPlayer.Profile.AccountId, ownPlayer);

            //StartCoroutine(ReadFromServerLastActions());
            StartCoroutine(ReadFromServerCharacters());
            ReadFromServerLastActionsTaskToken = new CancellationTokenSource();
            CancellationToken token = ReadFromServerLastActionsTaskToken.Token;
            Task.Run(() => { _ = ReadFromServerLastActions(token); });
            Task.Run(() => { _ = ProcessFromServerLastActions(token); });

            ListOfInteractiveObjects = FindObjectsOfType<WorldInteractiveObject>();
            PatchConstants.Logger.LogInfo($"Found {ListOfInteractiveObjects.Length} interactive objects");

            CoopPatches.EnableDisablePatches();
            //GCHelpers.EnableGC();

            DEBUGSpawnDronesOnServer = Plugin.Instance.Config.Bind<bool>
                ("Coop", "ShowDronesOnServer", false, new BepInEx.Configuration.ConfigDescription("Whether to spawn the client drones on the server -- for debugging")).Value;

            DEBUGShowPlayerList = Plugin.Instance.Config.Bind<bool>
               ("Coop", "ShowPlayerList", false, new BepInEx.Configuration.ConfigDescription("Whether to show the player list on the GUI -- for debugging")).Value;

            Player_Init_Patch.SendPlayerDataToServer((EFT.LocalPlayer)Singleton<GameWorld>.Instance.RegisteredPlayers.First(x => x.IsYourPlayer));
        }

        void OnDestroy()
        {
            CoopPatches.EnableDisablePatches();
            ReadFromServerLastActionsTaskToken.Cancel();

        }

        #endregion

        CancellationTokenSource ReadFromServerLastActionsTaskToken { get; set; }

        private IEnumerator ReadFromServerCharacters()
        {
            var waitEndOfFrame = new WaitForEndOfFrame();

            if (GetServerId() == null)
                yield return waitEndOfFrame;

            var waitSeconds = new WaitForSeconds(10f);

            Dictionary<string, object> d = new Dictionary<string, object>();
            d.Add("serverId", GetServerId());
            d.Add("pL", new List<string>());
            while (true)
            {
                yield return waitSeconds;

                if (Players == null)
                    continue;

                if (DEBUGSpawnDronesOnServer)
                {
                    //d["pL"] = Players.Keys.ToArray();
                    //if (Singleton<GameWorld>.Instance.RegisteredPlayers.Any())
                    //    ((string[])d["pL"]).AddRangeToArray(Singleton<GameWorld>.Instance.RegisteredPlayers.Select(x => x.Profile.AccountId).ToArray());
                }
                else
                {
                    //d["pL"] = PlayersToSpawn.Keys.ToArray();
                    //if (Players.Keys.Any())
                    //    ((string[])d["pL"]).AddRangeToArray(Players.Keys.ToArray());
                    //.AddRangeToArray(Singleton<GameWorld>.Instance.RegisteredPlayers.Select(x => x.Profile.AccountId)
                    //.ToArray());
                }

                var jsonDataToSend = d.ToJson();

                if (RequestingObj == null)
                    RequestingObj = Request.Instance;

                try
                {
                    m_CharactersJson = RequestingObj.PostJsonAsync<Dictionary<string, object>[]>("/coop/server/read/players", jsonDataToSend).Result;
                    if (m_CharactersJson == null)
                        continue;

                    if (!m_CharactersJson.Any())
                        continue;

                    //Logger.LogDebug($"CoopGameComponent.ReadFromServerCharacters:{actionsToValues.Length}");

                    var packets = m_CharactersJson
                         .Where(x => x != null);
                    if (packets == null)
                        continue;

                    foreach (var queuedPacket in packets)
                    {
                        if (queuedPacket != null && queuedPacket.Count > 0)
                        {
                            if (queuedPacket != null)
                            {
                                if (queuedPacket.ContainsKey("m"))
                                {
                                    var method = queuedPacket["m"].ToString();
                                    if (method != "PlayerSpawn")
                                        continue;

                                    string accountId = queuedPacket["accountId"].ToString();
                                    // TODO: Put this back in after testing in Creation functions
                                    if (!DEBUGSpawnDronesOnServer)
                                    {
                                        if (Players == null 
                                            || Players.ContainsKey(accountId) 
                                            || Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x=>x.Profile.AccountId == accountId))
                                        {
                                            Logger.LogDebug($"Ignoring call to Spawn player {accountId}. The player already exists in the game.");
                                            continue;
                                        }
                                    }
                                    if (PlayersToSpawn.ContainsKey(accountId))
                                        continue;

                                    if (!PlayersToSpawnPacket.ContainsKey(accountId))
                                        PlayersToSpawnPacket.TryAdd(accountId, queuedPacket);

                                    //Vector3 newPosition = Players.First().Value.Position;
                                    //if (queuedPacket.ContainsKey("sPx")
                                    //    && queuedPacket.ContainsKey("sPy")
                                    //    && queuedPacket.ContainsKey("sPz"))
                                    //{
                                    //    string npxString = queuedPacket["sPx"].ToString();
                                    //    newPosition.x = float.Parse(npxString);
                                    //    string npyString = queuedPacket["sPy"].ToString();
                                    //    newPosition.y = float.Parse(npyString);
                                    //    string npzString = queuedPacket["sPz"].ToString();
                                    //    newPosition.z = float.Parse(npzString) + 0.5f;

                                    //    if (!PlayersToSpawnPositions.ContainsKey(accountId))
                                    //        PlayersToSpawnPositions.TryAdd(accountId, newPosition);

                                        if (!PlayersToSpawn.ContainsKey(accountId))
                                            PlayersToSpawn.TryAdd(accountId, ESpawnState.None);
                                        //ProcessPlayerBotSpawn(queuedPacket, accountId, newPosition, false);
                                    //}


                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {

                    Logger.LogError(ex.ToString());

                }
                finally
                {

                }

                foreach (var p in PlayersToSpawn)
                {
                    // If not showing drones. Check whether the "Player" has been registered, if they have, then ignore the drone
                    if (!DEBUGSpawnDronesOnServer)
                    {
                        if (Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x => x.Profile.AccountId == p.Key))
                        {
                            if (PlayersToSpawn.ContainsKey(p.Key))
                                PlayersToSpawn[p.Key] = ESpawnState.Ignore;

                            continue;
                        }

                        if (Players.Any(x => x.Key == p.Key))
                        {
                            if (PlayersToSpawn.ContainsKey(p.Key))
                                PlayersToSpawn[p.Key] = ESpawnState.Ignore;

                            continue;
                        }
                    }


                    if (PlayersToSpawn[p.Key] == ESpawnState.Ignore)
                        continue;

                    if (PlayersToSpawn[p.Key] == ESpawnState.Spawned)
                        continue;

                    Vector3 newPosition = Vector3.zero;
                    if (PlayersToSpawnPacket[p.Key].ContainsKey("sPx")
                        && PlayersToSpawnPacket[p.Key].ContainsKey("sPy")
                        && PlayersToSpawnPacket[p.Key].ContainsKey("sPz"))
                    {
                        string npxString = PlayersToSpawnPacket[p.Key]["sPx"].ToString();
                        newPosition.x = float.Parse(npxString);
                        string npyString = PlayersToSpawnPacket[p.Key]["sPy"].ToString();
                        newPosition.y = float.Parse(npyString);
                        string npzString = PlayersToSpawnPacket[p.Key]["sPz"].ToString();
                        newPosition.z = float.Parse(npzString) + 0.5f;
                        ProcessPlayerBotSpawn(PlayersToSpawnPacket[p.Key], p.Key, newPosition, false);
                    }
                    else
                    {
                        Logger.LogError($"ReadFromServerCharacters::PlayersToSpawnPacket does not have positional data for {p.Key}");
                    }
                }

                //actionsToValuesJson = null;
                yield return waitEndOfFrame;
            }
        }

        private void ProcessPlayerBotSpawn(Dictionary<string, object> packet, string accountId, Vector3 newPosition, bool isBot)
        {
            Logger.LogDebug($"ProcessPlayerBotSpawn:{accountId}");

            // If not showing drones. Check whether the "Player" has been registered, if they have, then ignore the drone
            if (!DEBUGSpawnDronesOnServer)
            {
                if(Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x=>x.Profile.AccountId == accountId))
                {
                    if (PlayersToSpawn.ContainsKey(accountId))
                        PlayersToSpawn[accountId] = ESpawnState.Ignore;

                    return;
                }
            }

            // If CreatePhysicalOtherPlayerOrBot has been done before. Then ignore the Deserialization section and continue.
            if (PlayersToSpawn.ContainsKey(accountId)
                && PlayersToSpawn[accountId] != ESpawnState.Ignore
                && PlayersToSpawn[accountId] != ESpawnState.Loading
                && PlayersToSpawn[accountId] != ESpawnState.Spawned
                && PlayersToSpawnProfiles.ContainsKey(accountId)
                ) 
            {
                CreatePhysicalOtherPlayerOrBot(PlayersToSpawnProfiles[accountId], newPosition);
                return;
            }

            if (PlayersToSpawnProfiles.ContainsKey(accountId))
                return;

            Profile profile = MatchmakerAcceptPatches.Profile.Clone();
            profile.AccountId = accountId;

            try
            {
                //Logger.LogDebug("PlayerBotSpawn:: Adding " + accountId + " to spawner list");
                profile.Id = accountId;
                profile.Info.Nickname = "BSG Employee " + Players.Count;
                profile.Info.Side = isBot ? EPlayerSide.Savage : EPlayerSide.Usec;
                if (packet.ContainsKey("p.info"))
                {
                    //Logger.LogDebug("PlayerBotSpawn:: Converting Profile data");
                    profile.Info = packet["p.info"].ToString().ParseJsonTo<ProfileInfo>(Array.Empty<JsonConverter>());
                    //Logger.LogDebug("PlayerBotSpawn:: Converted Profile data:: Hello " + profile.Info.Nickname);
                }
                if (packet.ContainsKey("p.cust"))
                {
                    var parsedCust = packet["p.cust"].ToString().ParseJsonTo<Dictionary<EBodyModelPart, string>>(Array.Empty<JsonConverter>());
                    if (parsedCust != null && parsedCust.Any())
                    {
                        profile.Customization = new Customization(parsedCust);
                        //Logger.LogDebug("PlayerBotSpawn:: Set Profile Customization for " + profile.Info.Nickname);

                    }
                }
                if (packet.ContainsKey("p.equip"))
                {
                    var pEquip = packet["p.equip"].ToString();
                    var equipment = packet["p.equip"].ToString().SITParseJson<Equipment>();
                    profile.Inventory.Equipment = equipment;
                    //Logger.LogDebug("PlayerBotSpawn:: Set Equipment for " + profile.Info.Nickname);

                }
                if (packet.ContainsKey("isHost"))
                {
                }

                // Send to be loaded
                CreatePhysicalOtherPlayerOrBot(profile, newPosition);
                PlayersToSpawnProfiles.TryAdd(accountId, profile);
            }
            catch (Exception ex)
            {
                Logger.LogError($"PlayerBotSpawn::ERROR::" + ex.Message);
            }

        }

        private void CreatePhysicalOtherPlayerOrBot(Profile profile, Vector3 position)
        {
            try
            {
                if (Players == null)
                {
                    Logger.LogError("Players is NULL!");
                    return;
                }

                int playerId = Players.Count + Singleton<GameWorld>.Instance.RegisteredPlayers.Count + 1;
                if (profile == null)
                {
                    Logger.LogError("CreatePhysicalOtherPlayerOrBot Profile is NULL!");
                    return;
                }

                PlayersToSpawn.TryAdd(profile.AccountId, ESpawnState.None);
                if (PlayersToSpawn[profile.AccountId] == ESpawnState.None)
                {
                    PlayersToSpawn[profile.AccountId] = ESpawnState.Loading;
                    IEnumerable<ResourceKey> allPrefabPaths = profile.GetAllPrefabPaths();
                    if (allPrefabPaths.Count() == 0)
                    {
                        Logger.LogError($"CreatePhysicalOtherPlayerOrBot::{profile.Info.Nickname}::PrefabPaths are empty!");
                        PlayersToSpawn[profile.AccountId] = ESpawnState.Error;
                        return;
                    }

                    Singleton<PoolManager>.Instance.LoadBundlesAndCreatePools(PoolManager.PoolsCategory.Raid, PoolManager.AssemblyType.Local, allPrefabPaths.ToArray(), JobPriority.General)
                        .ContinueWith(delegate
                        {
                            PlayersToSpawn[profile.AccountId] = ESpawnState.Spawning;
                            Logger.LogDebug($"CreatePhysicalOtherPlayerOrBot::{profile.Info.Nickname}::Load Complete.");
                            return;
                        });

                    return;
                }

                // ------------------------------------------------------------------
                // Its loading on the previous pass, ignore this one until its finished
                if (PlayersToSpawn[profile.AccountId] == ESpawnState.Loading)
                {
                    return;
                }

                // ------------------------------------------------------------------
                // It has already spawned, we should never reach this point if Players check is working in previous step
                if (PlayersToSpawn[profile.AccountId] == ESpawnState.Spawned)
                {
                    Logger.LogDebug($"CreatePhysicalOtherPlayerOrBot::{profile.Info.Nickname}::Is already spawned");
                    return;
                }

                // ------------------------------------------------------------------
                // Create Local Player drone
                LocalPlayer localPlayer = LocalPlayer.Create(playerId
                    , position
                    , Quaternion.identity
                    ,
                    "Player",
                    ""
                    , EPointOfView.ThirdPerson
                    , profile
                    , aiControl: false
                    , EUpdateQueue.Update
                    , EFT.Player.EUpdateMode.Auto
                    , EFT.Player.EUpdateMode.Auto
                    , BackendConfigManager.Config.CharacterController.ClientPlayerMode
                    , () => Singleton<OriginalSettings>.Instance.Control.Settings.MouseSensitivity
                    , () => Singleton<OriginalSettings>.Instance.Control.Settings.MouseAimingSensitivity
                    , new CoopStatisticsManager()
                    , FilterCustomizationClass.Default
                    , null
                    , isYourPlayer: false).Result;

                if (localPlayer == null)
                    return;

                PlayersToSpawn[profile.AccountId] = ESpawnState.Spawned;

                // ----------------------------------------------------------------------------------------------------
                // Add the player to the custom Players list
                if (!Players.ContainsKey(profile.AccountId))
                    Players.TryAdd(profile.AccountId, localPlayer);

                if (!Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x => x.Profile.AccountId == profile.AccountId))
                    Singleton<GameWorld>.Instance.RegisteredPlayers.Add(localPlayer);

                // Create/Add PlayerReplicatedComponent to the LocalPlayer
                var prc = localPlayer.GetOrAddComponent<PlayerReplicatedComponent>();
                prc.IsClientDrone = true;

                // ----------------------------------------------------------------------------------------------------
                // Find the Original version of this Player/Bot and hide them. This is so the SERVER sees the same as CLIENTS.
                //
                //MakeOriginalPlayerInvisible(profile);
                //
                // ----------------------------------------------------------------------------------------------------
                SetWeaponInHandsOfNewPlayer(localPlayer);
                //Singleton<GameWorld>.Instance.RegisterPlayer(localPlayer);

            }
            catch (Exception ex)
            {
                Logger.LogError(ex.ToString());
            }

        }

        /// <summary>
        /// Doesn't seem to work :(
        /// </summary>
        /// <param name="profile"></param>
        //private void MakeOriginalPlayerInvisible(Profile profile)
        //{
        //    if (Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x => x.Profile.AccountId == profile.AccountId))
        //    {
        //        var originalPlayer = Singleton<GameWorld>.Instance.RegisteredPlayers.FirstOrDefault(x => x.Profile.AccountId == profile.AccountId);
        //        if (originalPlayer != null)
        //        {
        //            Logger.LogDebug($"Make {profile.AccountId} invisible?");
        //            originalPlayer.IsVisible = false;
        //        }
        //        else
        //        {
        //            Logger.LogDebug($"Unable to find {profile.AccountId} to make them invisible");
        //        }
        //    }
        //    else
        //    {
        //        Logger.LogDebug($"Unable to find {profile.AccountId} to make them invisible");
        //    }
        //}

        /// <summary>
        /// Attempts to set up the New Player with the current weapon after spawning
        /// </summary>
        /// <param name="person"></param>
        public void SetWeaponInHandsOfNewPlayer(EFT.Player person)
        {
            // Set first available item...
            //person.SetFirstAvailableItem((IResult) =>
            //{



            //});

            Logger.LogDebug($"SetWeaponInHandsOfNewPlayer: {person.Profile.AccountId}");

            var equipment = person.Profile.Inventory.Equipment;
            if (equipment == null)
            {
                Logger.LogError($"SetWeaponInHandsOfNewPlayer: {person.Profile.AccountId} has no Equipment!");
                return;
            }
            Item item = null;

            if (equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem != null)
                item = equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem;

            if (equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem != null)
                item = equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem;

            if (equipment.GetSlot(EquipmentSlot.Holster).ContainedItem != null)
                item = equipment.GetSlot(EquipmentSlot.Holster).ContainedItem;

            if (equipment.GetSlot(EquipmentSlot.Scabbard).ContainedItem != null)
                item = equipment.GetSlot(EquipmentSlot.Scabbard).ContainedItem;

            if (item == null)
            {
                Logger.LogError($"SetWeaponInHandsOfNewPlayer:Unable to find any weapon for {person.Profile.AccountId}");
                return;
            }

            Logger.LogDebug($"SetWeaponInHandsOfNewPlayer: {person.Profile.AccountId} {item.TemplateId}");

            person.SetItemInHands(item, (IResult) =>
            {

                if (IResult.Failed == true)
                {
                    Logger.LogError($"SetWeaponInHandsOfNewPlayer:Unable to set item {item} in hands for {person.Profile.AccountId}");
                }

            });
        }

        private ConcurrentQueue<string> m_ActionsToValuesJson { get; } = new ConcurrentQueue<string>();
        private List<string> m_ActionsToValuesJson2 { get; } = new List<string>();
        private Dictionary<string, object>[] m_CharactersJson;

        /// <summary>
        /// Gets the Last Actions Dictionary from the Server. This should not be used for things like Moves. Just other stuff.
        /// </summary>
        /// <returns></returns>
        //private IEnumerator ReadFromServerLastActions()
        private async Task ReadFromServerLastActions(CancellationToken cancellationToken = default(CancellationToken))
        {
            var fTimeToWaitInMS = 750;
            var jsonDataServerId = new Dictionary<string, object>
            {
                { "serverId", GetServerId() },
                { "t", ReadFromServerLastActionsLastTime }
            };
            while (true)
            {
                if(cancellationToken.IsCancellationRequested) 
                    return;

                //yield return waitSeconds;
                await Task.Delay(fTimeToWaitInMS);

                jsonDataServerId["t"] = ReadFromServerLastActionsLastTime;
                if (Players == null)
                {
                    PatchConstants.Logger.LogInfo("CoopGameComponent:No Players Found! Nothing to process!");
                    continue;
                }

                if (RequestingObj == null)
                    RequestingObj = Request.GetRequestInstance(true, Logger);

                m_ActionsToValuesJson.Enqueue(RequestingObj.GetJson($"/coop/server/read/lastActions/{GetServerId()}"));
                ApproximatePing = new DateTime(DateTime.Now.Ticks - ReadFromServerLastActionsLastTime).Millisecond - fTimeToWaitInMS;
                ReadFromServerLastActionsLastTime = DateTime.Now.Ticks;
            }
        }

        private async Task ProcessFromServerLastActions(CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                await Task.Delay(5);

                while(m_ActionsToValuesJson.TryDequeue(out var jsonData))
                {
                    await Task.Delay(1);
                    ReadFromServerLastActionsByAccountParseData(jsonData);
                }
            }
        }

        void LateUpdate()
        {
            if (m_ActionsToValuesJson2.Any())
            {
                for(var i = 0; i < m_ActionsToValuesJson2.Count; i++)
                {
                    var result = m_ActionsToValuesJson2[i];
                    ReadFromServerLastActionsParseData(result);
                    result = null;
                }
                m_ActionsToValuesJson2.Clear();
            }


            // TODO : Player
            foreach (var player in Players)
            {
                //if (LastPlayerStateSent < DateTime.Now.AddSeconds(-1))
                //{

                //    Dictionary<string, object> dictPlayerState = new Dictionary<string, object>();
                //    if (ReplicatedDirection.HasValue)
                //    {
                //        dictPlayerState.Add("dX", ReplicatedDirection.Value.x);
                //        dictPlayerState.Add("dY", ReplicatedDirection.Value.y);
                //    }
                //    dictPlayerState.Add("pX", player.Position.x);
                //    dictPlayerState.Add("pY", player.Position.y);
                //    dictPlayerState.Add("pZ", player.Position.z);
                //    dictPlayerState.Add("rX", player.Rotation.x);
                //    dictPlayerState.Add("rY", player.Rotation.y);
                //    dictPlayerState.Add("pose", player.MovementContext.PoseLevel);
                //    dictPlayerState.Add("spd", player.MovementContext.CharacterMovementSpeed);
                //    dictPlayerState.Add("spr", player.MovementContext.IsSprintEnabled);
                //    dictPlayerState.Add("m", "PlayerState");
                //    ServerCommunication.PostLocalPlayerData(player, dictPlayerState);

                //    LastPlayerStateSent = DateTime.Now;
                //}
            }
        }

        public void ReadFromServerLastActionsByAccountParseData(string actionsToValuesJson)
        {
            if (string.IsNullOrEmpty(actionsToValuesJson))
                return;

            if (actionsToValuesJson.StartsWith("["))
            {
                Logger.LogDebug("ReadFromServerLastActionsByAccountParseData: Has Array. This wont work!");
                return;
            }
            
            Dictionary<string, JObject> actionsToValues = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(actionsToValuesJson);
            if (actionsToValues == null)
            {
                return;
            }

            var packets = actionsToValues.Values
                 .Where(x => x != null)
                 .Where(x => x.Count > 0)
                 .Select(x => x.ToObject<Dictionary<string, object>>());

            foreach (var packets2 in packets.Select(x=>x.Values))
            {
                foreach (var packet in packets2)
                {
                    var json = packet.SITToJson();
                    try
                    {
                        m_ActionsToValuesJson2.Add(json);
                    }
                    catch (Exception)
                    { 
                    }
                }
            }
            packets = null;
            actionsToValues = null;
        }


        public void ReadFromServerLastActionsParseData(string actionsToValuesJson)
        {
            if (Singleton<GameWorld>.Instance == null)
                return;

            if (actionsToValuesJson == null)
            {
                PatchConstants.Logger.LogInfo("CoopGameComponent:No Data Returned from Last Actions!");
                return;
            }

            Dictionary<string, object> packet = JsonConvert.DeserializeObject<Dictionary<string, object>>(actionsToValuesJson);
            if (packet == null || packet.Count == 0)
                return;

            var accountId = packet["accountId"].ToString();
            //if (!Players.ContainsKey(accountId))
            //{
            //    Logger.LogInfo($"TODO: FIXME: Players does not contain {accountId}. Searching. This is SLOW. FIXME! Don't do this!");
            //    foreach (var p in FindObjectsOfType<LocalPlayer>())
            //    {
            //        if (!Players.ContainsKey(p.Profile.AccountId))
            //        {
            //            Players.TryAdd(p.Profile.AccountId, p);
            //            var nPRC = p.GetOrAddComponent<PlayerReplicatedComponent>();
            //            nPRC.player = p;
            //        }
            //    }
            //}



            try
            {
                foreach (var plyr in 
                    Players.ToArray()
                    .Where(x => x.Key == packet["accountId"].ToString())
                    )
                {
                    plyr.Value.TryGetComponent<PlayerReplicatedComponent>(out var prc);

                    if (prc == null)
                    {
                        Logger.LogError($"Player {accountId} doesn't have a PlayerReplicatedComponent!");
                        continue;
                    }

                    prc.HandlePacket(packet);
                }
            }
            catch (Exception) { }

            try
            {
                // Deal to all versions of this guy (this shouldnt happen but good for testing)
                foreach (var plyr in Singleton<GameWorld>.Instance.RegisteredPlayers.Where(x => x.Profile != null && x.Profile.AccountId == packet["accountId"].ToString()))
                {
                    if (!plyr.TryGetComponent<PlayerReplicatedComponent>(out var prc))
                    {

                        Logger.LogError($"Player {accountId} doesn't have a PlayerReplicatedComponent!");
                        continue;
                    }

                    prc.HandlePacket(packet);
                }
            }
            catch (Exception) { }


        }

        int GuiX = 10;
        int GuiWidth = 400;

        ConcurrentQueue<long> RTTQ = new ConcurrentQueue<long>();

        void OnGUI()
        {
            var rect = new Rect(GuiX, 5, GuiWidth, 100);

            rect.y = 5;
            GUI.Label(rect, $"SIT Coop: " + (MatchmakerAcceptPatches.IsClient ? "CLIENT" : "SERVER"));
            rect.y += 15;

            GUI.Label(rect, $"Ping:{(ApproximatePing >= 0 ? ApproximatePing : 0)}");
            rect.y += 15;
            if (Request.Instance != null)
            {
                if (RTTQ.Count > 350) 
                    RTTQ.TryDequeue(out _);

                RTTQ.Enqueue(ApproximatePing + Request.Instance.PostPing);
                var rtt = Math.Round(RTTQ.Average()); // ApproximatePing + Request.Instance.PostPing;

                GUI.Label(rect, $"RTT:{(rtt >= 0 ? rtt : 0)}");
                rect.y += 15;
            }

            if (!DEBUGShowPlayerList)
                return;

            if (PlayersToSpawn.Any(p => p.Value != ESpawnState.Spawned))
            {
                GUI.Label(rect, $"Spawning Players:");
                rect.y += 15;
                foreach (var p in PlayersToSpawn.Where(p => p.Value != ESpawnState.Spawned))
                {
                    GUI.Label(rect, $"{p.Key}:{p.Value}");
                    rect.y += 15;
                }
            }

            if (Singleton<GameWorld>.Instance != null)
            {
                var players = Singleton<GameWorld>.Instance.RegisteredPlayers.ToList();
                players.AddRange(Players.Values);

                rect.y += 15;
                GUI.Label(rect, $"Players [{players.Count}]:");
                rect.y += 15;
                foreach (var p in players)
                {
                    GUI.Label(rect, $"{p.Profile.Nickname}:{(p.IsAI ? "AI" : "Player")}:{(p.HealthController.IsAlive ? "Alive" : "Dead")}");
                    rect.y += 15;
                }

                players.Clear();
                players = null;
            }
        }

    }

    public enum ESpawnState
    {
        None = 0,
        Loading = 1,
        Spawning = 2,
        Spawned = 3,
        Ignore = 98,
        Error = 99,
    }
}
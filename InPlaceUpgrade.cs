//#define ExtractLayerMask

using Rewired;
using System;
using UnityEngine;
using RewiredConsts;
using Voxeland5;
using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using System.Linq;
using System.Diagnostics.Eventing.Reader;
using VLB;
using JetBrains.Annotations;
using BepInEx.Configuration;
using System.Diagnostics;
using UnityEngine.SceneManagement;

namespace InPlaceUpgrade
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class InPlaceUpgrade : BaseUnityPlugin
    {
        private const int TicksBetweenBuilds = 5;
        public const string pluginGuid = "inplaceupgrade.nhickling.co.uk";
        public const string pluginName = "InPlaceUpgrade";
        public const string pluginShort = "IPU";
        public const string pluginVersion = "0.1.0";

        public static ConfigEntry<bool> UseAggressiveBeltFinder;
        public static ConfigEntry<bool> EnableLogging;
        public static ConfigEntry<bool> EnableSameBeltTriggering;
        public static bool loggingEnabled;
        public static ConfigEntry<int> TraverseLimit;
        public static ConfigEntry<int> ReplaceCount;

        Stopwatch heavyLoggingTimer = Stopwatch.StartNew();

        private static ManualLogSource modLogger;
        private static GameObject hostObject;

        private int capturedBuildMask = 2097512;
        private Rewired.Player playerReference = null;
        private int ticksTillNextbuild = 2;
        private Queue<PendingBuildRequest> queuedBuildActions = new Queue<PendingBuildRequest>();
        private float raycastRange = 15.0f;

        private int notificationCycle = 50;
        private int notificationTimer = 50;

        public void Awake()
        {
            modLogger = Logger;
            modLogger.LogInfo($"Moved to persistant scene");

            gameObject.hideFlags = HideFlags.HideAndDontSave;

            modLogger.LogInfo($"Started DevBuild 4");

            UseAggressiveBeltFinder = ((BaseUnityPlugin)this).Config.Bind<bool>("Config", "Aggressive", false, new ConfigDescription("Search for belts indirectly.", (AcceptableValueBase)(object)new AcceptableValueRange<bool>(false, true), Array.Empty<object>()));
            EnableLogging = ((BaseUnityPlugin)this).Config.Bind<bool>("Config", "Logging", false, new ConfigDescription("Enable disagnostic logging. Can cause lag. (Value is ONLY loaded at startup, changes after WILL be ignored for performance reasons)", (AcceptableValueBase)(object)new AcceptableValueRange<bool>(false, true), Array.Empty<object>()));
            EnableSameBeltTriggering = ((BaseUnityPlugin)this).Config.Bind<bool>("Config", "EnableSameBeltTriggering", true, new ConfigDescription("Enable starting an in-place-upgrade of belts when looking at the same type of belt.", (AcceptableValueBase)(object)new AcceptableValueRange<bool>(false, true), Array.Empty<object>()));
            loggingEnabled = EnableLogging.Value;
            ReplaceCount = ((BaseUnityPlugin)this).Config.Bind<int>("Config", "ReplaceCount", 50, new ConfigDescription("How many belts is the mod allowed to replace at once. High values can cause a stutter when starting replacement.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(25, 100), Array.Empty<object>()));
            TraverseLimit = ((BaseUnityPlugin)this).Config.Bind<int>("Config", "TraverseLimit", 50, new ConfigDescription("How many belts is the mod allowed to scan at once. (Used when Aggresive ssearch is enabled)", (AcceptableValueBase)(object)new AcceptableValueRange<int>(25, 100), Array.Empty<object>()));
            modLogger.LogInfo($"Loaded settings from config. Aggressive:{UseAggressiveBeltFinder.Value}. Logging {EnableLogging.Value}. Depth {TraverseLimit.Value}. Count {ReplaceCount.Value}");
            
#if ExtractLayerMask
            Harmony harmony = new Harmony(pluginGuid);
            modLogger.LogInfo($"{pluginName}: Fetching patch references");
            MethodInfo original = AccessTools.Method(typeof(PlayerBuilder), "CheckTargetLogisticsObject");
            modLogger.LogInfo($"{pluginName}: Starting Patch");
            harmony.Patch(original, null, new HarmonyMethod(patch));
#endif
        }

#if ExtractLayerMask
        public static void CheckTargetLogisticsObject_Patch(PlayerBuilder __instance)
        {
            var maskValue = (LayerMask)Traverse.Create(__instance).Field("buildableMask").GetValue();

            modLogger.LogInfo($"{pluginName}: Layer masks intercepted: buildableMask {maskValue.value}");
        }
#endif

        public void FixedUpdate()
        {
            if(notificationTimer > 0)
            {
                --notificationTimer;
            }

            if (playerReference != null)
            {
                this.DoGameLoop();
            }
            else
            {
                // Try to attached to player input controller

                try
                {
                    if (ReInput.isReady)
                    {
                        if (playerReference == null)
                        {
                            playerReference = ReInput.players.GetPlayer(0);
                            TryDetailLog($"Attached to player controllers");
                        }
                    }
                }
                catch {
                    if (heavyLoggingTimer.ElapsedMilliseconds > 2000)
                    {
                        TryDetailLog($"Waiting for ReInput.");
                        heavyLoggingTimer.Restart();
                    }
                }
            }
        }

        private void DoGameLoop()
        {
            GridManager grid = null;
            Player techPlayer = null;
            try
            {
                grid = GridManager.instance;
                techPlayer = Player.instance;
            }
            catch (Exception)
            {
                if (heavyLoggingTimer.ElapsedMilliseconds > 2000)
                {
                    TryDetailLog($"Error getting manager instances");
                    heavyLoggingTimer.Restart();
                }
            }


            if (techPlayer == null || grid == null)
            {
                if (heavyLoggingTimer.ElapsedMilliseconds > 2000)
                {
                    TryDetailLog($"Waiting for manager instances.");
                    heavyLoggingTimer.Restart();
                }
            }
            else
            {
                if(this.queuedBuildActions.Count > 0)
                {
                    if(--ticksTillNextbuild == 0)
                    {
                        ticksTillNextbuild = TicksBetweenBuilds;
                        var action = this.queuedBuildActions.Peek();
                        TryDetailLog($" Sending build event");

                        if (grid.CheckBuildableIncludingVoxelsAt(action.BuildGrid))
                        {
                            TryDetailLog($" Sending build event");

                            action = this.queuedBuildActions.Dequeue();

                            NetworkMessageRelay.instance.SendNetworkAction((NetworkAction)action.BuildAction);
                        }
                        else
                        {
                            ticksTillNextbuild = 5;
                            TryShowNotification($"{pluginShort}: Rebuild is currently blocked, skipping for now");
                            --action.BuildAttempts;
                            if(action.BuildAttempts == 0)
                            {
                                this.queuedBuildActions.Dequeue();
                                this.queuedBuildActions.Enqueue(new PendingBuildRequest()
                                {
                                    BuildAction = action.BuildAction,
                                    BuildGrid = action.BuildGrid
                                });
                            }
                        }

                    }

                    if (playerReference.GetButton(109))
                    {
                        TryShowNotification($"{pluginShort}: Rebuild is currently busy!");
                        if (heavyLoggingTimer.ElapsedMilliseconds > 2000)
                        {
                            TryDetailLog($"Input recieved while busy.");
                            heavyLoggingTimer.Restart();
                        }
                    }
                }
                else if (playerReference.GetButton(109))
                {
                    TryDetailLog($" Input captured");
                    if (techPlayer != null)
                    {
                        var cameraDirection = techPlayer.cam.transform.forward;
                        var cameraOrigin = techPlayer.cam.transform.position;
                        RaycastHit rayHit;
                        if (Physics.Raycast(cameraOrigin, cameraDirection, out rayHit, this.raycastRange, capturedBuildMask, QueryTriggerInteraction.Ignore))
                        {
                            TryDetailLog($" Raycast hit!");
                            GenericMachineInstanceRef targetMachineRef;
                            var targetVoxel = rayHit.point.GetVoxel();
                            grid.TryGetMachine(in targetVoxel, out targetMachineRef); 
                            TryDetailLog($" Raycast found machine of type {targetMachineRef.typeIndex.ToString()}");

                            if (this.CanReplace(targetMachineRef))
                            {
                                TryDetailLog($" Machine is replacable.");

                                if (techPlayer.toolbar.selectedInfo.buildable)
                                {
                                    var playerSelected = techPlayer.toolbar.selectedBuildableInfo;                                    
                                    if (playerSelected.GetInstanceType() == targetMachineRef.typeIndex)
                                    {
                                        bool isDifferent = playerSelected.uniqueId != targetMachineRef.ResId;
                                        bool sameBeltOverride = EnableSameBeltTriggering.Value && targetMachineRef.typeIndex == MachineTypeEnum.Conveyor;
                                        TryDetailLog($" Matching item type, checking for item difference. {isDifferent}/{sameBeltOverride}");
                                        if (isDifferent || sameBeltOverride)
                                        {
                                            var selectedItem = techPlayer.toolbar.selectedInfo;
                                            var itemCount = techPlayer.inventory.GetResourceCount(selectedItem.uniqueId);
                                            var toReplace = GetReplacableItems(targetMachineRef, playerSelected);
                                            TryDetailLog($" Need to replace {toReplace.Count} items.");
                                            TryDetailLog($" Found {itemCount} items in inventory.");

                                            if(toReplace.Count == 0)
                                            {
                                                TryShowNotification($"{pluginShort}: Nothing to replace found.");
                                            } else if (toReplace.Count <= itemCount)
                                            {
                                                TryDetailLog($" Found enough items");
                                                TryShowNotification($"{pluginShort}: Replacing {toReplace.Count} items.");

                                                var toDestroy = new List<uint>();
                                                foreach (var replace in toReplace)
                                                {
                                                    toDestroy.Add(replace.instanceId);
                                                }

                                                List<PendingBuildRequest> rebuildAction = this.GetRebuildAction(targetMachineRef, playerSelected.uniqueId, toReplace);

                                                if(rebuildAction.Count > 1)
                                                {
                                                    if (toReplace.Count * 2 >= itemCount)
                                                    {
                                                        TryShowNotification($"{pluginShort}: Rebuild protection. More items needed. ({itemCount} of {toReplace.Count * 2})");
                                                        return;
                                                    }
                                                }

                                                TryDetailLog($" Sending deconstruction");
                                                // Prevent next update from pinging screen
                                                this.notificationTimer = notificationCycle;

                                                NetworkMessageRelay.instance.SendNetworkAction((NetworkAction)new EraseMachineAction()
                                                {
                                                    info = new EraseMachineInfo()
                                                    {
                                                        machineIDs = toDestroy.ToArray()
                                                    }
                                                });
                                                TryDetailLog($" Queing construction");

                                                foreach(var action in rebuildAction)
                                                {
                                                    this.queuedBuildActions.Enqueue(action);
                                                }
                                            }
                                            else
                                            {
                                                TryDetailLog($" Not enough items.");

                                                TryShowNotification($"{pluginShort}: Not enough items ({itemCount} of {toReplace.Count})");
                                            }
                                        }
                                        else
                                        {
                                            TryDetailLog($" Can't self replace.");

                                            TryShowNotification($"{pluginShort}: Can't replace with the same type.");
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            TryDetailLog($" Raycast did not hit anything");

                        }
                    }
                }
            }
        }

        private class ModSystemMessage : SystemMessageInfo
        {
            public ModSystemMessage(string message) : base(message)
            {
            }

            public override MessageType type => MessageType.Tutorial;
        }

        internal static void TryDetailLog(string log)
        {
            if (loggingEnabled) {
                modLogger.LogInfo($"{pluginName}: {log}");
            }
        }

        private void TryShowNotification(string message)
        {
            if (notificationTimer == 0)
            {
                var systemLog = UIManager.instance.systemLog;
                var systemMessageInfo = new ModSystemMessage(message);
                systemLog.FlashMessage((SystemMessageInfo)systemMessageInfo);
                notificationTimer = notificationCycle;
            }

        }
        private List<PendingBuildRequest> GetRebuildAction(GenericMachineInstanceRef sourceMachine, int selectedItem, List<GenericMachineInstanceRef> toReplace)
        {
            var result =  new List<PendingBuildRequest>();

            switch (sourceMachine.typeIndex)
            {
                case MachineTypeEnum.Inserter:
                case MachineTypeEnum.Smelter:
                case MachineTypeEnum.Drill:
                case MachineTypeEnum.Thresher:
                case MachineTypeEnum.Assembler:
                    this.AppendSimpleBuildAction(sourceMachine, selectedItem, ref result);
                    break;
                case MachineTypeEnum.Conveyor:
                    this.AppendConveyorBuildAction(sourceMachine.typeIndex, selectedItem, ref result, toReplace);
                    break;

            }

            return result;
        }

        private void AppendConveyorBuildAction(MachineTypeEnum machineType, int selectedItem, ref List<PendingBuildRequest> result, List<GenericMachineInstanceRef> toReplace)
        {
            TryDetailLog($" Building conveyor replaceaction");

            List<ConveyorBuildInfo.ChainData> newChain = new List<ConveyorBuildInfo.ChainData>();
            // Fetch conveyor cache
            var factoryReference = FactorySimManager.instance;

            ConveyorMachineList machineList = (ConveyorMachineList)factoryReference.machineManager.GetMachineList<ConveyorInstance, ConveyorDefinition>(MachineTypeEnum.Conveyor);
            TryDetailLog($" Got machineList: {machineList == null}");

            bool isreversed = false;
            foreach (var conveyorSegment in toReplace)
            {
                TryDetailLog($" Adding conveyor segment");

                // Convert from GenericRef to ConveyorInstance
                var conveyorInstance = machineList.myArray[conveyorSegment.index];
                isreversed = conveyorInstance.buildBackwards;
                var targetVoxel = conveyorSegment.MyGridInfo.Center.GetVoxel();

                if (conveyorInstance.beltShape == ConveyorInstance.BeltShape.Up || conveyorInstance.beltShape == ConveyorInstance.BeltShape.Down)
                {
                    targetVoxel.y -= 1;
                }

                newChain.Add(new ConveyorBuildInfo.ChainData()
                {
                    count = 1,
                    shape = conveyorInstance.beltShape,
                    rotation = conveyorInstance.gridInfo.yawRot,
                    start = targetVoxel
                });

                ConveyorBuildInfo cbi = new ConveyorBuildInfo();
                cbi.machineType = selectedItem;
                cbi.chainData = newChain;
                cbi.isReversed = isreversed;
                cbi.machineIds = new List<uint>();
                cbi.tick += 5;
                ConveyorBuildAction cba = new ConveyorBuildAction();
                cba.info = (ConveyorBuildInfo)cbi.Clone();
                cba.resourceCostAmount = cbi.chainData.Count;
                cba.resourceCostID = selectedItem;
                result.Add(new PendingBuildRequest()
                {
                    BuildAction = cba,
                    BuildGrid = conveyorSegment.MyGridInfo
                });
            }
            TryDetailLog($" Finalising action");

        }

        private void AppendSimpleBuildAction(GenericMachineInstanceRef machineRef, int selectedItem, ref List<PendingBuildRequest> result)
        {
            TryDetailLog($" Building simple replace action");

            // Fetch conveyor cache
            var factoryReference = FactorySimManager.instance;
            TryDetailLog($" Got factory reference: {factoryReference == null}");

            TryDetailLog($" Adding conveyor segment");

            SimpleBuildInfo sbi = new SimpleBuildInfo();
            sbi.machineType = selectedItem;
            sbi.rotation = machineRef.MyGridInfo.YawRotation;
            sbi.minGridPos = machineRef.MyGridInfo.minPos;
            sbi.tick += 5;
            SimpleBuildAction sba = new SimpleBuildAction();
            sba.info = (SimpleBuildInfo)sbi.Clone();
            sba.resourceCostAmount = 1;
            sba.resourceCostID = selectedItem;
            result.Add(new PendingBuildRequest()
            {
                BuildAction = sba,
                BuildGrid = machineRef.MyGridInfo
            });
            TryDetailLog($" Finalising action");
        }

        private bool CanReplace(GenericMachineInstanceRef foundMachine)
        {
            switch(foundMachine.typeIndex)
            {
                case MachineTypeEnum.Inserter:
                case MachineTypeEnum.Conveyor:
                case MachineTypeEnum.Drill:
                case MachineTypeEnum.Thresher:
                case MachineTypeEnum.Smelter:
                    return true;

            }
            return false;
        }

        private List<GenericMachineInstanceRef> GetReplacableItems(GenericMachineInstanceRef foundMachine, BuilderInfo inventoryRef)
        {
            List<GenericMachineInstanceRef> machinesToReplace = new List<GenericMachineInstanceRef>();
            switch (foundMachine.typeIndex)
            {

                case MachineTypeEnum.Inserter:
                case MachineTypeEnum.Assembler:
                case MachineTypeEnum.Drill:
                case MachineTypeEnum.Thresher:
                case MachineTypeEnum.Smelter:
                    machinesToReplace.Add(foundMachine);
                    break;

                case MachineTypeEnum.Conveyor:
                    try
                    { 
                        TryDetailLog($" Trying to cast");
                        ConveyorInstance convInst = MachineManager.instance.GetTyped<ConveyorInstance>(foundMachine);
                        TryDetailLog($" Traversing");

                        List<ConveyorInstance> linkedConveyors = TraverseConveyor(convInst);

                        TryDetailLog($" Found {linkedConveyors.Count} linked conveyors");

                        linkedConveyors = linkedConveyors.Where(linked => linked._myDef.uniqueId != inventoryRef.uniqueId).ToList();
                        TryDetailLog($" Found {linkedConveyors.Count} conveyors to replace");
                        

                        TryDetailLog($" Sorting by distance");
                        linkedConveyors = linkedConveyors.OrderBy(linked => (int)Vector3Int.Distance(linked.gridInfo.minPos, foundMachine.MyGridInfo.minPos)).ToList();
                        TryDetailLog($" Sorted");
                        foreach (var conv in linkedConveyors)
                        {
                            if (!machinesToReplace.Contains(conv.gridInfo.myRef))
                            {
                                if (machinesToReplace.Count < ReplaceCount.Value)
                                {
                                    machinesToReplace.Add(conv.gridInfo.myRef);
                                }
                                else
                                {
                                    TryDetailLog($"Maximum replace count hit. {ReplaceCount.Value}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TryDetailLog($" Failure to traverse! {ex.Message}");
                    }
                    TryDetailLog($" {machinesToReplace.Count} items found");
                    break;

            }

            return machinesToReplace;
        }

        private List<ConveyorInstance> TraverseConveyor(ConveyorInstance sourcePoint)
        {
            List<ConveyorInstance> foundConveyors = new List<ConveyorInstance>();
            Queue<ConveyorInstance> conveyorsToScan = new Queue<ConveyorInstance>();
            Action<ConveyorInstance> handleConveyor;

            if (UseAggressiveBeltFinder.Value) {
                handleConveyor = new Action<ConveyorInstance>((conv) =>
                {
                    if (foundConveyors.Any(traversed => traversed.gridInfo.myRef.instanceId == conv.gridInfo.myRef.instanceId))
                    {
                        return;
                    }

                    foundConveyors.Add(conv);

                    var foundNeighbours = conv.gridInfo.neighbors;
                    foreach (var neighbour in foundNeighbours)
                    {
                        if (neighbour.typeIndex == MachineTypeEnum.Conveyor)
                        {
                            conveyorsToScan.Enqueue(MachineManager.instance.GetTyped<ConveyorInstance>(neighbour));
                        }
                    }
                });
            }
            else {
                handleConveyor = new Action<ConveyorInstance>((conv) =>
                {
                    if (foundConveyors.Any(traversed => traversed.gridInfo.myRef.instanceId == conv.gridInfo.myRef.instanceId))
                    {
                        return;
                    }

                    foundConveyors.Add(conv);

                    //0.1.2a bypass
                    conv.RefreshShape(true);

                    if (conv.isHub)
                    {
                        TryDetailLog($" Found hub, adding {conv.numHubConns} cons");
                        for (int i = 0; i < conv.numHubConns; ++i)
                        {
                            conveyorsToScan.Enqueue(MachineManager.instance.GetTyped<ConveyorInstance>(conv.hubConns[i].machineRef));
                        }
                    }
                    else
                    {
                        if (conv.canUseOutputRef)
                        {
                            TryDetailLog($" adding output reference");
                            conveyorsToScan.Enqueue(MachineManager.instance.GetTyped<ConveyorInstance>(conv.mainOutputRef.machineRef));
                        }
                        else if (conv.canUseInputRef)
                        {
                            TryDetailLog($" adding input reference");
                            conveyorsToScan.Enqueue(MachineManager.instance.GetTyped<ConveyorInstance>(conv.mainInputRef.machineRef));
                        }
                    }
                });
            }
            // Scan forward, then clear to scan reverse
            handleConveyor(sourcePoint);
            int traverseLimit = TraverseLimit.Value;
            while (true)
            {
                TryDetailLog($" Traverse run state - CurCount:{foundConveyors.Count} Queue:{conveyorsToScan.Count} ");
                if (conveyorsToScan.Count()>0)
                {
                    handleConveyor(conveyorsToScan.Dequeue());
                }

                if(foundConveyors.Count() > traverseLimit)
                {
                    TryDetailLog($" Traverse limit hit. Found {foundConveyors.Count()} limit {traverseLimit}");
                    break;
                }

                if(conveyorsToScan.Count() == 0 )
                {
                    TryDetailLog($" no more belts. Found {foundConveyors.Count()}.");
                    break;
                }
            }

            return foundConveyors;
        }

        static void TryAdd(ref List<ConveyorInstance> traversedConveyors, ConveyorInstance instance)
        {
            if(!traversedConveyors.Contains(instance))
            {
                traversedConveyors.Add(instance);
            }
        }

        private class PendingBuildRequest
        {
            public BuildMachineAction BuildAction { get; set; }
            public GridInfo BuildGrid { get; set; }

            public int BuildAttempts { get; set; } = 5;
        }

    }
}

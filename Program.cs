using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        string version = "v0.2.2";
        
        // Keyword Management
        string overflowTag = "[Overflow]";
        string oresTag = "[Ores]";
        
        // Cycle Speed
        static int waitSeconds = 15;
        static int waitTicksStatic = (waitSeconds * 60) / 100;
        int waitTicks = 0;

        static int waitFlushSeconds = 60;
        static int waitFlushTicksStatic = (waitFlushSeconds * 60) / 100;
        int waitFlushTicks = 0;

        bool refineryFlushEnabled = false;

        class RefineryContainer
        {
            public IMyRefinery Refinery;
            public IMyInventory InventoryIn;
            public IMyInventory InventoryOut;
        }
        
        class CargoContainer
        {
            public IMyCargoContainer Container;
            public IMyInventory Inventory;
            public bool OresFlag = false;
            public bool OverflowFlag = false;
        }
        
        List<RefineryContainer> refineryContainers = new List<RefineryContainer>();
        
        List<IMyRefinery> managedRefineries = new List<IMyRefinery>();
        
        List<IMyCargoContainer> taggedCargos = new List<IMyCargoContainer>();
        
        List<CargoContainer> managedCargos = new List<CargoContainer>();
        
        IMyCargoContainer overflowCargo;
        IMyInventory overflowInventory;
        
        List<IMyInventory> oreInventories = new List<IMyInventory>();
        
        MyIni pbIni = new MyIni();
        string refineryOrder = "Iron,Nickel,Cobalt,Silicon,Magnesium,Gold,Silver,Platinum,Uranium";
        
        Dictionary<string, int> OrePriorityMap = new Dictionary<string, int>
        {
            { "Iron", 0 },
            { "Nickel", 0 },
            { "Cobalt", 0 },
            { "Silicon", 0 },
            { "Magnesium", 0 },
            { "Silver", 0 },
            { "Gold", 0 },
            { "Platinum", 0 },
            { "Uranium", 0 },
            { "Stone", 0 },
        };

        int pCount = 0;
        
        bool priorityOreFound = false;

        Dictionary<int, string> ReversedOrePriorityMap = new Dictionary<int, string>();

        public Program()
        {
           Runtime.UpdateFrequency = UpdateFrequency.Update100;

           MyIniParseResult result;
           if (!pbIni.TryParse(Me.CustomData, out result))
               throw new Exception(result.ToString());
           
           GridTerminalSystem.GetBlocksOfType<IMyRefinery>(managedRefineries, refinery => refinery.CubeGrid == Me.CubeGrid);

           GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(taggedCargos, container => container.CustomName.Contains(oresTag) || container.CustomName.Contains(overflowTag));
         
           foreach (var cargoContainer in taggedCargos)
           {
               var managedCargo = new CargoContainer
               {
                   Container = cargoContainer,
                   Inventory = cargoContainer.GetInventory(0),
               };
               
               if (cargoContainer.CustomName.Contains(oresTag))
                   managedCargo.OresFlag = true;
                
               if (cargoContainer.CustomName.Contains(overflowTag))
               {
                   managedCargo.OverflowFlag = true;
                   overflowCargo = managedCargo.Container;
                   overflowInventory = managedCargo.Inventory;
               }
               
               managedCargos.Add(managedCargo);

           }
           
           foreach (var managedCargo in managedCargos)
           {
               if (managedCargo.OresFlag)
                   oreInventories.Add(managedCargo.Inventory);
           }
           
           foreach (var refinery in managedRefineries)
           {
               var managedRefinery = new RefineryContainer
               {
                   Refinery = refinery,
                   InventoryIn = refinery.InputInventory,
                   InventoryOut = refinery.OutputInventory,
               };
               refineryContainers.Add(managedRefinery);
               
               // Boot Phase: Empty output of all refineries
               var items = new List<MyInventoryItem>();

               refinery.OutputInventory.GetItems(items);

               for (var i = items.Count - 1; i >= 0; i--)
               {
                   refinery.OutputInventory.TransferItemTo(overflowInventory, i, null, true, items[i].Amount);
               }
               
               items.Clear();
               
               refinery.InputInventory.GetItems(items);

               for (var i = items.Count - 1; i >= 0; i--)
               {
                   refinery.InputInventory.TransferItemTo(overflowInventory, i, null, true, items[i].Amount);
               }

               refinery.UseConveyorSystem = false;
           }

           string[] refineryOrderItems = refineryOrder.Split(',');
           
           OrePriorityMap["Iron"] = Array.IndexOf(refineryOrderItems, "Iron") + 1;
           OrePriorityMap["Nickel"] = Array.IndexOf(refineryOrderItems, "Nickel") + 1;
           OrePriorityMap["Cobalt"] = Array.IndexOf(refineryOrderItems, "Cobalt") + 1;
           OrePriorityMap["Silicon"] = Array.IndexOf(refineryOrderItems, "Silicon") + 1;
           OrePriorityMap["Magnesium"] = Array.IndexOf(refineryOrderItems, "Magnesium") + 1;
           OrePriorityMap["Silver"] = Array.IndexOf(refineryOrderItems, "Silver") + 1;
           OrePriorityMap["Gold"] = Array.IndexOf(refineryOrderItems, "Gold") + 1;
           OrePriorityMap["Platinum"] = Array.IndexOf(refineryOrderItems, "Platinum") + 1;
           OrePriorityMap["Uranium"] = Array.IndexOf(refineryOrderItems, "Uranium") + 1;
           OrePriorityMap["Stone"] = 0;
           
           ReversedOrePriorityMap = OrePriorityMap.ToDictionary(key => key.Value, key => key.Key);

        }

        public void Save()
        {
            
        }

        public void Main(string argument, UpdateType updateSource)
        {
            Echo($"Kippen Refinery Manager (KRM) {version}...");
            Echo($"Waiting {waitTicks * 100 / 60} more seconds...\n");

            string priority = ReversedOrePriorityMap[pCount];

            Echo($"Next in Queue: {priority}");

            if (waitTicks > 0) 
                waitTicks--;
           
            if (waitFlushTicks > 0)
                waitFlushTicks--;

            if (argument == "flush")
            {
                priority = ReversedOrePriorityMap[0];
                
                var items = new List<MyInventoryItem>();

                
                foreach (var refinery in managedRefineries)
                {
                    items.Clear();

                    refinery.OutputInventory.GetItems(items);

                    for (var i = items.Count - 1; i >= 0; i--)
                    {
                        refinery.OutputInventory.TransferItemTo(overflowInventory, i, null, true, items[i].Amount);
                    }
               
                    items.Clear();
               
                    refinery.InputInventory.GetItems(items);

                    for (var i = items.Count - 1; i >= 0; i--)
                    {
                        refinery.InputInventory.TransferItemTo(overflowInventory, i, null, true, items[i].Amount);
                    }
                }
               
                
            }

            if (waitFlushTicks > 0 && waitTicks > 0)
                return;

            if (waitFlushTicks == 0 && refineryFlushEnabled)
            {
                foreach (var refinery in managedRefineries)
                {
                    // Boot Phase: Empty output of all refineries
                    var items = new List<MyInventoryItem>();

                    refinery.OutputInventory.GetItems(items);

                    for (var i = items.Count - 1; i >= 0; i--)
                    {
                        refinery.OutputInventory.TransferItemTo(overflowInventory, i, null, true, items[i].Amount);
                    }
               
                    items.Clear();
               
                    refinery.InputInventory.GetItems(items);

                    for (var i = items.Count - 1; i >= 0; i--)
                    {
                        refinery.InputInventory.TransferItemTo(overflowInventory, i, null, true, items[i].Amount);
                    }
                }
            }

            var inventoryItems = new List<MyInventoryItem>();
            foreach (var inventory in oreInventories)
            {
                
                inventoryItems.Clear();
                
                inventory.GetItems(inventoryItems);

                for (var i = inventoryItems.Count - 1; i >= 0; i--)
                {
                    if (inventoryItems[i].Type.SubtypeId == priority)
                    {
                        priorityOreFound = true;
                        
                        int amountToTransfer = (int)inventoryItems[i].Amount / managedRefineries.Count;
                        if (amountToTransfer < managedRefineries.Count)
                            amountToTransfer = managedRefineries.Count;
                  
                        foreach (var refinery in refineryContainers)
                        {
                            
                            inventory.TransferItemTo(refinery.InventoryIn, i, null, true, amountToTransfer);
                        }
                    }
                }
            }

            foreach (var refinery in managedRefineries)
            {
                inventoryItems.Clear();

                refinery.InputInventory.GetItems(inventoryItems);

                for (var i = inventoryItems.Count - 1; i >= 0; i--)
                {
                    if (inventoryItems[i].Type.SubtypeId == priority)
                    {
                        priorityOreFound = true;
                    }
                }
            }

            if (!priorityOreFound)
            {
                if (pCount == 9)
                    pCount = 0;
                else
                    pCount++;
            }

            priorityOreFound = false;

            foreach (var refinery in managedRefineries)
            {
                var outputItems = new List<MyInventoryItem>();

                refinery.OutputInventory.GetItems(outputItems);

                for (var i = outputItems.Count - 1; i >= 0; i--)
                {
                    refinery.OutputInventory.TransferItemTo(overflowInventory, i, null, true, outputItems[i].Amount);
                }
               
            }
            
            waitTicks = waitTicksStatic;
            waitFlushTicks = waitFlushTicksStatic;
        }
    }
}
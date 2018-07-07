using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // In order to add a new utility class, right-click on your project, 
        // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
        // category under 'Visual C# Items' on the left hand side, and select
        // 'Utility Class' in the main area. Name it in the box below, and
        // press OK. This utility class will be merged in with your code when
        // deploying your final script.
        //
        // You can also simply create a new utility class manually, you don't
        // have to use the template if you don't want to. Just do so the first
        // time to see what a utility class looks like.
#region ProgrammableBlock
        static Action<string> Log;
#region BaseService
        public interface IShipService
        {
            void update(float dt);
            void update10();
            void update100();
        }

        public class BaseShipService : IShipService
        {
            virtual public void update(float dt) { }
            virtual public void update10() { }
            virtual public void update100() { }
        }
#endregion //BaseService

        #region ParkingService
        public class ParkingService : BaseShipService
        {
            float maxSensorDistance = 10f;
            IMyGridTerminalSystem GTS;
            string groupName;
            IMyLightingBlock lightingBlock = null;
            IMyCameraBlock cameraSensor = null;

            public ParkingService(IMyGridTerminalSystem GTS)
            {
                this.GTS = GTS;
                groupName = "[Parking Service]";
                init();
            }

            void init()
            {
                findRequiredBlocks();
                renameBlocks();
                cameraSensor.EnableRaycast = true;
                float.TryParse(cameraSensor.CustomData, out maxSensorDistance);
            }

            override public void update100()
            {
                //reinit
                init();
            }

            override public void update10()
            {
                if (cameraSensor != null)
                {
                    MyDetectedEntityInfo lastDetectedEntityInfo = cameraSensor.Raycast(maxSensorDistance);
                    Log(lastDetectedEntityInfo.Type.ToString());
                    if (lastDetectedEntityInfo.Type != MyDetectedEntityType.None)
                    {
                        lightingBlock.Enabled = true;
                    }
                    else
                    {
                        lightingBlock.Enabled = false;
                    }
                    Vector3 sensorPosition = cameraSensor.WorldMatrix.Translation;
                    float distance = float.PositiveInfinity;
                    if (lastDetectedEntityInfo.HitPosition != null)
                    {
                        distance = Vector3.Distance(sensorPosition, lastDetectedEntityInfo.HitPosition.Value);
                        changeLightColor(distance);
                    }

                    Log("Distance: " + distance);
                }
            }

            void findRequiredBlocks()
            {
                IMyBlockGroup groupBlocks = GTS.GetBlockGroupWithName(groupName);

                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                groupBlocks.GetBlocksOfType<IMyCameraBlock>(blocks);

                blocks.ForEach(b => cameraSensor = b as IMyCameraBlock);

                if(cameraSensor == null)
                {
                    Log("[Service Parking] Can' find a camera");
                }

                groupBlocks.GetBlocksOfType<IMyLightingBlock>(blocks);
                blocks.ForEach(b => lightingBlock = b as IMyLightingBlock);

                if (cameraSensor == null)
                {
                    Log("[Service Parking] Can' find a light block");
                }
            }

            void changeLightColor(float distance)
            {
                Vector3 redColor = new Vector3(1f, 0f, 0f);
                Vector3 greenColor = new Vector3(0f, 1f, 0f);
                Vector3 color = Vector3.Lerp(redColor, greenColor, distance / maxSensorDistance);
                lightingBlock.Color = new Color(color);
            }

            void renameBlocks()
            {
                if (cameraSensor != null)
                {
                    cameraSensor.CustomName = groupName + " Camera Ground Sensor";
                }

                if (lightingBlock != null)
                {
                    lightingBlock.CustomName = groupName + " Light Distance Meter";
                }
            }
        }
        #endregion //Parking Service

        #region FuelService
        public class FuelService : BaseShipService
        {
            IMyGridTerminalSystem GTS;
            Program PB;
            string groupName;
            List<IMyGasGenerator> gasGenerators = new List<IMyGasGenerator>();
            List<IMyReactor> reactors = new List<IMyReactor>();

            public FuelService(IMyGridTerminalSystem GTS, Program PB)
            {
                this.PB = PB;
                this.GTS = GTS;
                groupName = "[Fuel Service]";
                init();
            }

            void init()
            {
                findRequiredBlocks();
                renameBlocks();
            }

            override public void update100()
            {

                init();

                PB.sortOreOfType("Ice", gasGenerators);
                PB.sortOreOfType("Uranium", reactors);
            }

            void findRequiredBlocks()
            {
                IMyBlockGroup groupBlocks = GTS.GetBlockGroupWithName(groupName);

                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                groupBlocks.GetBlocksOfType(gasGenerators);
                groupBlocks.GetBlocksOfType(reactors);

                if (gasGenerators.Count == 0)
                {
                    Log("[Fuel Service] Can't find gas generators");
                }

                if (reactors.Count == 0)
                {
                    Log("[Fuel Service] Can't find reactors");
                }
            }

            void renameBlocks()
            {
                for (int i = 0; i < gasGenerators.Count; i++)
                {
                    IMyGasGenerator gasGenerator = gasGenerators[i];
                    gasGenerator.CustomName = groupName + " Gas Generator " + (i + 1);
                }

                for (int i = 0; i < reactors.Count; i++)
                {
                    IMyReactor reactor = reactors[i];
                    reactor.CustomName = groupName + " Reactor " + (i + 1);
                }
            }
        }
        #endregion //FuelService

        IMyGridTerminalSystem GTS;

        List<IShipService> shipServices = new List<IShipService>();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;
            GTS = GridTerminalSystem;
            Log = Echo;

            shipServices.Add(new ParkingService(GTS));
            shipServices.Add(new FuelService(GTS, this));
        }

        public void Save()
        {
            
        }
        
        public void Main(string argument, UpdateType updateSource)
        {
            foreach (IShipService service in shipServices)
            {
                if ((updateSource & UpdateType.Update1) != 0)
                {
                    service.update((float)Runtime.TimeSinceLastRun.TotalSeconds);
                }


                if ((updateSource & UpdateType.Update10) != 0)
                {
                    service.update10();
                }

                if ((updateSource & UpdateType.Update100) != 0)
                {
                    service.update100();
                }
            }




        }

#region Libraries

        VRage.MyFixedPoint getSumAmountItemsOfType<TBlockType>(string subtype, List<TBlockType> blocks) where TBlockType : class, IMyTerminalBlock
        {
            VRage.MyFixedPoint amount = 0;
            blocks.ForEach(block =>
            {
                List<IMyInventoryItem> items = new List<IMyInventoryItem>();
                items = block.GetInventory(0).GetItems();

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    string typeName = item.Content.SubtypeName;
                    if (typeName == subtype)
                    {
                        amount += item.Amount;
                    }
                    //Echo(typeName); 
                }
            });
            return amount;
        }

        VRage.MyFixedPoint getAmountItemsOfType(string subtype, IMyTerminalBlock block)
        {
            VRage.MyFixedPoint amount = 0;

            List<IMyInventoryItem> items = new List<IMyInventoryItem>();
            items = block.GetInventory(0).GetItems();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string typeName = item.Content.SubtypeName;
                if (typeName == subtype)
                {
                    amount += item.Amount;
                }
                //Echo(typeName); 
            }

            return amount;
        }

        int getItemIndex(string subtype, IMyTerminalBlock block)
        {
            List<IMyInventoryItem> items = new List<IMyInventoryItem>();
            items = block.GetInventory(0).GetItems();


            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string typeName = item.Content.SubtypeName;
                if (typeName == subtype)
                {
                    return i;
                }
                //Echo(typeName); 
            }

            return 0;
        }


        void sortOreOfType<TBlockType>(string typename, List<TBlockType> containers) where TBlockType : class, IMyTerminalBlock
        {
            int averageOreAmout = ((int)getSumAmountItemsOfType(typename, containers) / containers.Count);
            //Echo("" + averageOreAmout); 


            containers.Sort(delegate (TBlockType b1, TBlockType b2)
            {
                int result = (int)(getAmountItemsOfType(typename, b2) - getAmountItemsOfType(typename, b1));
                return result;
            });

            for (int i = 0; i < containers.Count - 1; i++)
            {
                IMyTerminalBlock containerSource = containers[i];
                IMyTerminalBlock containerDest = containers[i + 1];
                VRage.MyFixedPoint amountToMove = getAmountItemsOfType(typename, containerSource) - averageOreAmout;
                if (amountToMove > 0 && !containerSource.GetInventory(0).IsFull)
                {
                    int sourceItemIndex = getItemIndex(typename, containerSource);
                    int destItemIndex = getItemIndex(typename, containerDest);

                    containerSource.GetInventory(0).TransferItemTo(containerDest.GetInventory(0), sourceItemIndex,
                                destItemIndex, true, amountToMove);
                }
            }
        }

        static string getShortOreName(string name)
        {
            Dictionary<string, string> ores = new Dictionary<string, string>()
            {
                {"Iron", "Fe"},
                {"Nickel", "Ni"},
                {"Cobalt", "Co"},
                {"Magnesium", "Mg"},
                {"Silicon", "Si"},
                {"Silver", "Ag"},
                {"Gold", "Au"},
                {"Platinum", "Pt"},
                {"Uranium", "Ur"}
            };

            string ret = name;
            if (ores.ContainsKey(name))
                ret = ores[name];

            return ret;
        }


        void ApplyActionTo<TBlockType>(string action)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<TBlockType>(blocks, b => b.CubeGrid == Me.CubeGrid);
            blocks.ForEach(it =>
            {
                TBlockType block = (TBlockType)it;
                block.ApplyAction(action);
            });
        }

        void SetValueTo<TBlockType, T>(string propertyName, T value)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<TBlockType>(blocks, b => b.CubeGrid == Me.CubeGrid);
            blocks.ForEach(it =>
            {
                TBlockType block = (TBlockType)it;
                block.SetValue<T>(propertyName, value);
            });
        }

        void getInventoryItems<TBlockType>(Dictionary<string, float> cargoItems)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyInventoryItem> inventoryItems = new List<IMyInventoryItem>();

            List<IMyTerminalBlock> cargosList = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<TBlockType>(cargosList, b => b.CubeGrid == Me.CubeGrid);
            cargosList.ForEach(it =>
            {
                TBlockType cargo = (TBlockType)it;

                List<IMyInventoryItem> items = new List<IMyInventoryItem>();
                if (cargo.InventoryCount > 0)
                {
                    items = cargo.GetInventory(0).GetItems();

                    for (int j = 0; j < items.Count; ++j)
                    {
                        var item = items[j];
                        float amount = (float)item.Amount;
                        string typeName = item.Content.SubtypeName;
                        if (cargoItems.ContainsKey(typeName))
                        {
                            cargoItems[typeName] += amount;
                        }
                        else
                        {
                            cargoItems.Add(typeName, amount);
                        }
                    }
                }
            });
        }

        TBlockType GetBlockWithName<TBlockType>(string name)
        where TBlockType : class, IMyTerminalBlock
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.SearchBlocksOfName(name, blocks);
            foreach (var block in blocks)
            {
                if (block.CubeGrid == Me.CubeGrid)
                    return (TBlockType)block;
            }

            return null;
        }

        #endregion //Libraries
        #endregion
    }
}
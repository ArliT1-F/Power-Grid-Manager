// Minimal RimWorld contracts for the offline verification harness.
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorld.Planet
{
    public struct GlobalTargetInfo
    {
        public Thing Thing;
        public GlobalTargetInfo(Thing thing) { Thing = thing; }
    }
}

namespace RimWorld
{
    public class CompProperties_Power
    {
        public float PowerConsumption;
    }

    public class CompProperties_Battery
    {
        public float storedEnergyMax;
        public float efficiency = 0.5f;
    }

    public class CompProperties_Flickable
    {
        public string commandTexture = "";
    }

    public abstract class CompPower : ThingComp
    {
        public virtual PowerNet PowerNet { get; set; }
    }

    public class CompPowerTrader : CompPower
    {
        private PowerNet net;
        public bool PowerOn;
        public float PowerOutput;
        public CompProperties_Power Props = new CompProperties_Power();
        public override PowerNet PowerNet { get => net; set => net = value; }
    }

    public class CompPowerPlant : CompPowerTrader { }

    public class CompPowerBattery : CompPower
    {
        private PowerNet net;
        public float StoredEnergy;
        public bool StunnedByEMP;
        public CompProperties_Battery Props = new CompProperties_Battery();
        public float AmountCanAccept => Props.storedEnergyMax - StoredEnergy;
        public override PowerNet PowerNet { get => net; set => net = value; }
    }

    public class CompPowerTransmitter : CompPower { }

    public class CompFlickable : ThingComp
    {
        private bool switchOnInt = true;
        public CompProperties_Flickable Props = new CompProperties_Flickable();

        public bool SwitchIsOn
        {
            get => switchOnInt;
            set => switchOnInt = value;
        }
    }

    public class CompTempControl : ThingComp { }
    public class CompGlower : ThingComp { }
    public class CompMannable : ThingComp { }

    public class Building_ResearchBench : Building { }
    public class Building_TurretGun : Building { }

    public class Building_Bed : Building
    {
        public bool Medical => def != null && def.building != null && def.building.bed_defaultMedical;
    }

    public class PowerNet
    {
        public List<CompPowerTrader> powerComps = new List<CompPowerTrader>();
        public List<CompPowerBattery> batteryComps = new List<CompPowerBattery>();
        public List<CompPower> transmitters = new List<CompPower>();
        public List<CompPower> connectors = new List<CompPower>();

        public float CurrentEnergyGainRate()
        {
            float num = 0f;
            for (int i = 0; i < powerComps.Count; i++)
            {
                CompPowerTrader trader = powerComps[i];
                if (trader != null && trader.PowerOn) num += trader.PowerOutput * 1.6666667E-05f;
            }
            return num;
        }

        public float CurrentStoredEnergy()
        {
            float num = 0f;
            for (int i = 0; i < batteryComps.Count; i++)
            {
                CompPowerBattery battery = batteryComps[i];
                if (battery != null && !battery.StunnedByEMP) num += battery.StoredEnergy;
            }
            return num;
        }
    }

    public class PowerNetManager
    {
        public List<PowerNet> AllNetsListForReading = new List<PowerNet>();
    }

    public abstract class MainTabWindow : Window
    {
        public MainButtonDef def;

        public virtual Vector2 RequestedTabSize => new Vector2(1010f, 684f);

        public MainTabWindow()
        {
            layer = WindowLayer.GameUI;
            preventCameraMotion = false;
            doCloseButton = false;
            doCloseX = false;
        }

        public override Vector2 InitialSize => RequestedTabSize;
    }

    public class MainButtonDef : Def
    {
        public Type tabWindowClass;
        public Type workerClass;
        public int order;
        public string iconPath;
        public bool validWithoutMap;
        public bool closesWorldView;
        public bool buttonVisible = true;
    }
}

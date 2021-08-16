using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace HealParserApi.Models
{
    public class BaseEvent : TableEntity
    {
        // PKEY == Report Name
        // RowKey = Guid

        public DateTime TimeStampFromLog { get; set; }
        public string EventName { get; set; }

        public string SourceGuid { get; set; }

        public string SourceName { get; set; }

        public string SourceFlags { get; set; }

        public string SourceRaidFlags { get; set; }

        public string TargetUniqueId { get; set; }

        public string TargetName { get; set; }

        public string TargetFlags { get; set; }

        public string TargetRaidFlags { get; set; }

        public void ParseBase(string[] parts)
        {
            this.SourceGuid = parts[1];
            this.SourceName = parts[2].Replace("\"", string.Empty);
            this.SourceFlags = parts[3];
            this.SourceRaidFlags = parts[4];
            this.TargetUniqueId = parts[5];
            this.TargetName = parts[6].Replace("\"", string.Empty);
            this.TargetFlags = parts[7];
            this.TargetRaidFlags = parts[8];
        }
    }

    public class AbilityEvent : BaseEvent
    {
        public string SpellDataJson { get; set; }
        public SpellData SpellInfo { get; set; }
    }

    public class RangedSpellEvent : AbilityEvent
    {
        public int SpellId { get; set; }

        public string SpellName { get; set; }

        public int SpellSchool { get; set; }

    }


    public class AbsorbedSpellEvent : BaseEvent
    {
        public string ShieldCasterId { get; set; }

        public string ShieldCasterName { get; set; }

        public int ShieldSpellId { get; set; }

        public string ShieldSpellName { get; set; }

        public double AbsorbedAmount { get; set; }

        public int SpellAbsorbedId { get; set; }

        public string SpellAbsorbedName { get; set; }
    }

    public class HealingSpellEvent : BaseEvent
    {
        public int SpellId { get; set; }

        public string SpellName { get; set; }

        public string SpellDataJson { get; set; }

        public SpellData SpellInfo { get; set; }

        public bool IsHealOverTime { get; set; }
    }

    public class AuraSpellEvent : BaseEvent
    {
        public int SpellId { get; set; }

        public string SpellName { get; set; }
    }

    public class SpellDispelEvent : BaseEvent
    {
        public int SpellId { get; set; }

        public string SpellName { get; set; }

        public int SpellIdRemoved { get; set; }

        public string SpellRemovedName { get; set; }
    }

    public class HealRecord
    {

        public DateTime Timestamp { get; set; }

        public string PlayerId { get; set; }

        public string PlayerName { get; set; }

        public int SpellId { get; set; }

        public string SpellName { get; set; }

        public string TargetId { get; set; }

        public string TargetName { get; set; }

        public double AmountHealedOrPrevented { get; set; }

        public double AmountOverhealed { get; set; }

        public bool WasCritical { get; set; }

        public HealKind Kind { get; set; }

        public int SpellRemovedId { get; set; }

        public string SpellRemovedName { get; set; }
    }

    public enum HealKind
    {
        DirectHeal,
        HealOverTime,
        Prevention,
        Dispel
    }

    public class SpellData
    {
        public string InfoGuid { get; set; }

        public string OwnerGuid { get; set; }

        public double UnitCurrentHP { get; set; }

        public double UnitMaxHP { get; set; }

        public double UnitAttackPower { get; set; }

        public double UnitSpellPower { get; set; }

        public double UnitArmor { get; set; }

        public PowerType PowerKind { get; set; }

        public double CurrentPower { get; set; }

        public double MaxPower { get; set; }

        public double PowerCost { get; set; }

        public string UnitX { get; set; }

        public string UnitY { get; set; }

        public int UiMapId { get; set; }

        public string Facing { get; set; }

        public int UnitLevel { get; set; }

        public double FinalDamage { get; set; }

        public double RawDamage { get; set; }

        public int SpellSchool { get; set; }

        public double OverkillAmount { get; set; }

        public double ResistedAmount { get; set; }

        public double BlockedAmount { get; set; }

        public double Absorbed { get; set; }

        public bool WasCritical { get; set; }

        public bool WasGlancing { get; set; }

        public bool WasCrushing { get; set; }

        public double FinalHealAmount {get; set;} 

        public double Overheal { get; set; }
    }

    public enum PowerType
    {
        Mana = 0,
        Rage = 1,
        Focus = 2,
        Energy = 3,
        ComboPoints = 4,
        Runes = 5,
        SoulShards = 7
    }
}

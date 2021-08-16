using HealParserApi.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HealParserApi.Services
{
    public class Parser
    {
        public static string[] EventsWeListen = new[] { "SWING_DAMAGE_LANDED", "RANGE_DAMAGE", "SPELL_DAMAGE", "SPELL_HEAL", "SPELL_DISPEL", "SPELL_AURA_APPLIED", "SPELL_PERIODIC_HEAL", "SPELL_ABSORBED" };

        private Dictionary<string, string> pomDiction = new Dictionary<string, string>();
        public (List<BaseEvent> events, BaseReport report) ParseFile(string text, string reportName, string fileName, string uploader)
        {
            var lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            var events = new List<BaseEvent>();

            var eventNames = new List<string>();

            var report = new BaseReport();
            report.ReportName = reportName;
            report.RowKey = Guid.NewGuid().ToString();
            report.PartitionKey = uploader;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var eventName = line.Split(' ')[3].Split(',')[0].ToUpperInvariant();

                if (!eventNames.Contains(eventName))
                {
                    eventNames.Add(eventName);
                }

                if (!EventsWeListen.Contains(eventName)) continue;

                var firstChunk = line.Split(',')[0].Split(' ');
                var timestamp = $"{firstChunk[0]} {firstChunk[1]}";

                var ev = this.ParseEvent(timestamp, eventName, line);
                if (ev == null)
                {
                    continue;
                }

                ev.PartitionKey = report.RowKey;
                ev.RowKey = Guid.NewGuid().ToString();
                events.Add(ev);
            }

            return (events, report);
        }


        private BaseEvent ParseEvent(string timestamp, string eventName, string line)
        {
            var stampSplit = timestamp.Split(' ');
            var date = stampSplit[0];
            date += $"/{DateTime.UtcNow.Year}";
            timestamp = $"{date} {stampSplit[1]}";
            var parsedDate = DateTime.Parse(timestamp);

            var parts = line.Split(',');

            switch (eventName)
            {
                case "SWING_DAMAGE_LANDED":
                    {
                        return this.ParseSwingDamageEvent(eventName, parsedDate, parts);
                    }
                case "RANGE_DAMAGE":
                case "SPELL_DAMAGE":
                    {
                        return this.ParseRangedEvent(eventName, parsedDate, parts);
                    }
                case "SPELL_ABSORBED":
                    {
                        if (parts[9].StartsWith("Player") || parts[9].StartsWith("Creature"))
                        {
                            return null;
                        }
                        return this.ParseAbsorbedEvent(eventName, parsedDate, parts);
                    }
                case "SPELL_PERIODIC_HEAL":
                    {
                        return this.ParseHealingEvent(eventName, parsedDate, parts, true);
                    }
                case "SPELL_HEAL":
                    {
                        var healEv = this.ParseHealingEvent(eventName, parsedDate, parts);
                        if (healEv.SpellId == 33110) // Prayer of Mending's actual SpellID when healed
                        {
                            try
                            {
                                var realSource = this.pomDiction.First(kvp => kvp.Value == healEv.SourceName).Key;
                                healEv.SourceName = realSource;
                                this.pomDiction.Remove(realSource);
                            }
                            catch(Exception ex)
                            {
                                Console.WriteLine(ex);
                            }
                        }

                        return healEv;
                    }
                case "SPELL_DISPEL":
                    {
                        return this.ParseDispellEvent(eventName, parsedDate, parts);
                    }
                case "SPELL_AURA_APPLIED":
                    {

                        var auraEv = this.ParseAuraEvent(eventName, parsedDate, parts);
                        if (auraEv.SpellId == 41635) // PRAYER OF MENDING, as Aura applied
                        {
                            if (this.pomDiction.ContainsKey(auraEv.SourceName))
                            {
                                this.pomDiction.Remove(auraEv.SourceName);
                            }
                            this.pomDiction.Add(auraEv.SourceName, auraEv.TargetName);
                        }

                        return auraEv;
                    }
                default:
                    return null;
            }
        }

        private AbilityEvent ParseSwingDamageEvent(string eventName, DateTime parsedDate, string[] parts)
        {
            var ev = new AbilityEvent();
            ev.EventName = eventName;
            ev.TimeStampFromLog = parsedDate;
            ev.ParseBase(parts);
            ev.SpellInfo = this.ExtrapolateSpellData(9, parts);
            ev.SpellDataJson = JsonConvert.SerializeObject(ev.SpellInfo);
            return ev;
        }

        private RangedSpellEvent ParseRangedEvent(string eventName, DateTime parsedDate, string[] parts)
        {
            var ev = new RangedSpellEvent();
            ev.EventName = eventName;
            ev.TimeStampFromLog = parsedDate;
            ev.ParseBase(parts);
            ev.SpellName = parts[10].Replace("\"", string.Empty);
            ev.SpellId = Convert.ToInt32(parts[9]);
            //ev.SpellSchool = Convert.ToInt32(parts[11]);
            ev.SpellInfo = this.ExtrapolateSpellData(12, parts);
            ev.SpellDataJson = JsonConvert.SerializeObject(ev.SpellInfo);
            return ev;
        }

        private AbsorbedSpellEvent ParseAbsorbedEvent(string eventName, DateTime parsedDate, string[] parts)
        {
            var ev = new AbsorbedSpellEvent();
            ev.EventName = eventName;
            ev.TimeStampFromLog = parsedDate;
            ev.ParseBase(parts);
            // format a
            if (parts[9].StartsWith("Player"))
            {
                ev.ShieldCasterId = parts[9];
                ev.ShieldCasterName = parts[10];
                ev.ShieldSpellId = Convert.ToInt32(parts[13]);
                ev.ShieldSpellName = parts[14];
                //ev.AbsorbedAmount = Convert.ToDouble(parts[16]); // ignore, seems dupe
            }
            else
            {
                ev.SpellAbsorbedId = Convert.ToInt32(parts[9]);
                ev.SpellAbsorbedName = parts[10].Replace("\"", string.Empty);
                ev.ShieldCasterId = parts[12];
                ev.ShieldCasterName = parts[13].Replace("\"", string.Empty);
                ev.ShieldSpellId = Convert.ToInt32(parts[16]);
                ev.ShieldSpellName = parts[17];
                ev.AbsorbedAmount = Convert.ToDouble(parts[19]);
            }

            return ev;
        }

        private HealingSpellEvent ParseHealingEvent(string eventName, DateTime parsedDate, string[] parts, bool isHOT = false)
        {
            var ev = new HealingSpellEvent();
            ev.EventName = eventName;
            ev.TimeStampFromLog = parsedDate;
            ev.ParseBase(parts);
            ev.SpellName = parts[10].Replace("\"", string.Empty);
            ev.SpellId = Convert.ToInt32(parts[9]);
            ev.SpellInfo = this.ExtrapolateHealingData(12, parts);
            ev.SpellDataJson = JsonConvert.SerializeObject(ev.SpellInfo);
            ev.IsHealOverTime = isHOT;
            return ev;
        }

        private SpellDispelEvent ParseDispellEvent(string eventName, DateTime parsedDate, string[] parts)
        {
            var ev = new SpellDispelEvent();
            ev.EventName = eventName;
            ev.TimeStampFromLog = parsedDate;
            ev.ParseBase(parts);
            ev.SpellId = Convert.ToInt32(parts[9]);
            ev.SpellName = parts[10].Replace("\"", string.Empty);
            ev.SpellIdRemoved = Convert.ToInt32(parts[12]);
            ev.SpellRemovedName = parts[13];
            return ev;
        }

        private AuraSpellEvent ParseAuraEvent(string eventName, DateTime parsedDate, string[] parts)
        {
            var ev = new AuraSpellEvent();
            ev.EventName = eventName;
            ev.TimeStampFromLog = parsedDate;
            ev.ParseBase(parts);
            ev.SpellName = parts[10];
            ev.SpellId = Convert.ToInt32(parts[9]);
            return ev;
        }

        private SpellData ExtrapolateSpellData(int startingIx, string[] parts)
        {
            var data = new SpellData();
            this.SpellDataBasics(startingIx, parts, data);
            data.FinalDamage = Convert.ToDouble(parts[startingIx + 16]);
            data.RawDamage = Convert.ToDouble(parts[startingIx + 17]);
            data.OverkillAmount = Convert.ToDouble(parts[startingIx + 18]);
            data.SpellSchool = Convert.ToInt32(parts[startingIx + 19]);
            data.ResistedAmount = Convert.ToDouble(parts[startingIx + 20]);
            data.BlockedAmount = Convert.ToDouble(parts[startingIx + 21]);
            data.Absorbed = Convert.ToDouble(parts[startingIx + 22]);
            data.WasCritical = parts[startingIx + 22] != "nil";
            data.WasGlancing = parts[startingIx + 24] != "nil";
            data.WasCrushing = parts[startingIx + 25] != "nil";
            return data;
        }

        private SpellData ExtrapolateHealingData(int startingIx, string[] parts)
        {
            var data = new SpellData();
            this.SpellDataBasics(startingIx, parts, data);
            data.FinalHealAmount = Convert.ToDouble(parts[startingIx + 16]);
            data.Overheal = Convert.ToDouble(parts[startingIx + 18]);
            return data;
        }

        private void SpellDataBasics(int startingIx, string[] parts, SpellData data)
        {
            data.InfoGuid = parts[startingIx + 0];
            data.OwnerGuid = parts[startingIx + 1];
            data.UnitCurrentHP = Convert.ToDouble(parts[startingIx + 2]);
            data.UnitMaxHP = Convert.ToDouble(parts[startingIx + 3]);
            data.UnitAttackPower = Convert.ToDouble(parts[startingIx + 4]);
            data.UnitSpellPower = Convert.ToDouble(parts[startingIx + 5]);
            data.UnitArmor = Convert.ToDouble(parts[startingIx + 6]);
            data.PowerKind = (PowerType)Convert.ToInt32(parts[startingIx + 7]);
            data.CurrentPower = Convert.ToDouble(parts[startingIx + 8]);
            data.MaxPower = Convert.ToDouble(parts[startingIx + 9]);
            data.PowerCost = Convert.ToDouble(parts[startingIx + 10]);
            data.UnitX = parts[startingIx + 11];
            data.UnitY = parts[startingIx + 12];
            data.UiMapId = Convert.ToInt32(parts[startingIx + 13]);
            data.Facing = parts[startingIx + 14];
            data.UnitLevel = Convert.ToInt32(parts[startingIx + 15]);
        }
    }
}

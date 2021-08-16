using HealParserApi.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HealParserApi.Services
{
    public interface IParseReportService
    {
        Task<BaseReport> AddReport(BaseReport report, List<BaseEvent> events);

        Task<HealingReport> GetReport(string id, string uploader, bool includeAllRecords = false);

        Task<List<BaseReport>> GetReportsForUploader(string uploader);

        Task DeleteReport(string id);

        Task<List<HealRecord>> HealRecordsForReport(string id);
    }

    public class ParseReportService : IParseReportService
    {
        private readonly CloudTable eventsTable;
        private readonly CloudTable reportsTable;
        private readonly int[] hiPrioritySpellsToDispel;
        private readonly AsyncCache<RawReport> cache;
        private readonly ILogger<ParseReportService> logger;

        public ParseReportService(IConfiguration config, ILogger<ParseReportService> logger)
        {
            this.logger = logger;
            var cloud = CloudStorageAccount.Parse(config["cloudStorage"]);
            var tableClient = cloud.CreateCloudTableClient();
            
            this.eventsTable = tableClient.GetTableReference("events");
            this.eventsTable.CreateIfNotExists();
            this.reportsTable = tableClient.GetTableReference("reports");
            this.reportsTable.CreateIfNotExists();

            this.hiPrioritySpellsToDispel = config.GetSection("hiPriDispelIds").AsEnumerable().ToList().Where(c => c.Value != null).Select(c => Convert.ToInt32(c.Value)).ToArray();

            this.cache = new AsyncCache<RawReport>(expiration: TimeSpan.FromHours(config.GetValue<int>("reportCacheExpirationHours")));
        }

        public async Task<BaseReport> AddReport(BaseReport report, List<BaseEvent> events)
        {
            
            var reportOp = TableOperation.Insert(report);
            var eventBatch = new TableBatchOperation();
            var batchOperations = new List<TableBatchOperation>();
            var currentBatch = new TableBatchOperation();
            foreach(var ev in events)
            {
                if (currentBatch.Count > 99)
                {
                    batchOperations.Add(currentBatch);
                    currentBatch = new TableBatchOperation();
                }
                currentBatch.Add(TableOperation.InsertOrReplace(ev));
            }
            batchOperations.Add(currentBatch); // final batch

            var batchTasks = batchOperations.Select(bop => this.UploadBatchEvents(bop));
            var results = await Task.WhenAll(batchTasks);
            var reportResult = await this.reportsTable.ExecuteAsync(reportOp);

            var rawReport = new RawReport { Events = events, Id = report.RowKey, Name = report.ReportName };
            rawReport.Events = events;
            await this.cache.Set(report.RowKey, rawReport);
            return report;
        }

        public async Task DeleteReport(string id)
        {
            throw new NotImplementedException();
        }

        public async Task<HealingReport> GetReport(string id, string uploader, bool includeAllRecords = false)
        {
            var rawReport = await this.cache.Get(id);
            if (rawReport == null)
            {
                rawReport = await this.FetchRawReportFromStorage(id, uploader);
            }

            var events = rawReport.Events.OrderBy(e=>e.TimeStampFromLog).ToList();

            var damageDoneEvents = events.Where(ev => (ev.TargetUniqueId != null && ev.TargetUniqueId.StartsWith("Player") && (ev is AbilityEvent || ev is RangedSpellEvent)));
            var damageDoneSwing = damageDoneEvents.Where(ev => ev is AbilityEvent).Select(e => e as AbilityEvent);
            var damageDoneSpell = damageDoneEvents.Where(ev => ev is RangedSpellEvent).Select(e => e as RangedSpellEvent);

            var damageDone = damageDoneSpell.Sum(d => d.SpellInfo.FinalDamage) + damageDoneSwing.Sum(d => d.SpellInfo.FinalDamage);
            var numOfHealers = 2; // todo: input
            var dmgPerHealer = damageDone / numOfHealers;

            var sources = events.Select(ev => ev.SourceName).Distinct();

            var lb = new List<PlayerRow>();
            var healingRecords = new List<HealRecord>();

            foreach (var source in sources)
            {
                var entry = new PlayerRow();
                entry.Player = source;

                var sourceEvents = events.Where(ev => ev.SourceName == source);
                var healingEvents = sourceEvents.Where(ev => ev is HealingSpellEvent).Select(e => e as HealingSpellEvent);
                var absorbedEvents = events.Where(ev => ev is AbsorbedSpellEvent).Select(e => e as AbsorbedSpellEvent).Where(ae => ae.ShieldCasterName == source);
                var dispelEvents = sourceEvents.Where(ev => ev is SpellDispelEvent).Select(e => e as SpellDispelEvent);

                var healRecords = healingEvents.Select(h => this.ProduceHealingRecord(h))
                    .Concat(absorbedEvents.Select(a => this.ProduceHealingRecord(a)))
                    .Concat(dispelEvents.Select(d => this.ProduceHealingRecord(d)));

                if (includeAllRecords)
                {
                    entry.HealingRecords = healRecords.ToList();
                }

                entry.OverhealFromHots = healingEvents.Where(h => h.IsHealOverTime).Sum(h => h.SpellInfo.Overheal);

                var overhealCasts = healingEvents.Where(h => !h.IsHealOverTime).Sum(h => h.SpellInfo.Overheal);
                entry.Overhealing = entry.OverhealFromHots + overhealCasts;
                entry.RawHealing = healingEvents.Sum(h => h.SpellInfo.FinalHealAmount);
                entry.Prevented = absorbedEvents.Sum(a => a.AbsorbedAmount);
                entry.ActualHpHealed = entry.RawHealing - entry.Overhealing + entry.Prevented;
                entry.TotalHealingPoints = entry.ActualHpHealed / 1000;

                entry.TotalDispels = dispelEvents.Count();
                entry.TotalHiPriDispels = dispelEvents.Where(de => this.hiPrioritySpellsToDispel.Contains(de.SpellIdRemoved)).Count();
                entry.TotalDispelPoints = entry.TotalHiPriDispels * 10;

                entry.TotalScore = (entry.TotalHealingPoints + entry.TotalDispelPoints);

                var potential = entry.ActualHpHealed / dmgPerHealer;
                if (potential > 1) potential = 1;
                entry.ScoreAdjusted = potential * 100;

                if (entry.TotalScore > 0)
                {
                    lb.Add(entry);
                }
            };

            var report = new HealingReport
            {
                PlayerData = lb.OrderByDescending(r=>r.ScoreAdjusted).ToList(),
                Id = id,
                ReportName = rawReport.Name,
            };

            return report;
        }

        public async Task<List<HealRecord>> HealRecordsForReport(string id)
        {
            throw new NotImplementedException();
        }

        public async Task<List<BaseReport>> GetReportsForUploader(string uploader)
        {
            throw new NotImplementedException();
        }


        private async Task<TableBatchResult> UploadBatchEvents(TableBatchOperation op)
        {
            try
            {
                var result = await this.eventsTable.ExecuteBatchAsync(op);
                this.logger.LogTrace($"Executing batch upload for report {op.First().Entity.PartitionKey}");
                return result;
            }
            catch (StorageException storEx)
            {
                ;
                throw;
            }
        }

        private async Task<RawReport> FetchRawReportFromStorage(string id, string uploader)
        {
            this.logger.LogTrace($"Fetching all report events from source, reportID: {id}");
            var reportPkey = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, uploader);
            var reportRkey = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, id);
            var combined = TableQuery.CombineFilters(reportPkey, TableOperators.And, reportRkey);

            var reportEntity = this.reportsTable.ExecuteQuery(new TableQuery<BaseReport>().Where(combined)).FirstOrDefault();
            if (reportEntity == null)
            {
                return null;
            }

            var pkey = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, id);
            var query = new TableQuery().Where(pkey);
            var tOpts = new TableRequestOptions { TableQueryMaxItemCount = 10000 };
            var bareEvents = this.eventsTable.ExecuteQuery(query, tOpts).ToList();
            var events = bareEvents.Select(e => this.Saturate(e)).ToList();

            var report = new RawReport
            {
                Events = events,
                Name = reportEntity.ReportName,
                Id = id,
            };

            await this.cache.Set(id, report);

            return report;
        }


        private BaseEvent Saturate(DynamicTableEntity entity)
        {
            var eventType = entity.Properties["EventName"].StringValue;
            var ctx = new OperationContext();

            switch (eventType)
            {
                case "SWING_DAMAGE_LANDED":
                    {
                        var abEvent = EntityPropertyConverter.ConvertBack<AbilityEvent>(entity.Properties, ctx);
                        abEvent.SpellInfo = JsonConvert.DeserializeObject<SpellData>(abEvent.SpellDataJson);
                        return abEvent;
                    }
                case "RANGE_DAMAGE":
                case "SPELL_DAMAGE":
                    {
                        var dmgEvent = EntityPropertyConverter.ConvertBack<RangedSpellEvent>(entity.Properties, ctx);
                        dmgEvent.SpellInfo = JsonConvert.DeserializeObject<SpellData>(dmgEvent.SpellDataJson);
                        return dmgEvent;
                    }
                case "SPELL_ABSORBED":
                    {
                        return EntityPropertyConverter.ConvertBack<AbsorbedSpellEvent>(entity.Properties, ctx);
                    }
                case "SPELL_PERIODIC_HEAL":
                case "SPELL_HEAL":
                    {
                        var healEvent = EntityPropertyConverter.ConvertBack<HealingSpellEvent>(entity.Properties, ctx);
                        healEvent.SpellInfo = JsonConvert.DeserializeObject<SpellData>(healEvent.SpellDataJson);
                        return healEvent;
                    }
                case "SPELL_DISPEL":
                    {
                        return EntityPropertyConverter.ConvertBack<SpellDispelEvent>(entity.Properties, ctx);
                    }
                case "SPELL_AURA_APPLIED":
                    {
                        return EntityPropertyConverter.ConvertBack<AuraSpellEvent>(entity.Properties, ctx);
                    }
                default:
                    return null;
            }
        }


        private HealRecord ProduceHealingRecord(AbsorbedSpellEvent absEv)
        {
            return new HealRecord
            {
                Timestamp = absEv.TimeStampFromLog,
                PlayerId = absEv.SourceGuid,
                PlayerName = absEv.SourceName,
                AmountHealedOrPrevented = absEv.AbsorbedAmount,
                Kind = HealKind.Prevention,
                TargetId = absEv.TargetUniqueId,
                TargetName = absEv.TargetName,
                SpellId = absEv.ShieldSpellId,
                SpellName = absEv.ShieldSpellName,
            };
        }

        private HealRecord ProduceHealingRecord(HealingSpellEvent healEv)
        {
            return new HealRecord
            {
                Timestamp = healEv.TimeStampFromLog,
                PlayerId = healEv.SourceGuid,
                PlayerName = healEv.SourceName,
                AmountHealedOrPrevented = healEv.SpellInfo.FinalHealAmount,
                Kind = healEv.IsHealOverTime ? HealKind.HealOverTime : HealKind.DirectHeal,
                TargetId = healEv.TargetUniqueId,
                TargetName = healEv.TargetName,
                SpellId = healEv.SpellId,
                SpellName = healEv.SpellName,
                AmountOverhealed = healEv.SpellInfo.Overheal,
                WasCritical = healEv.SpellInfo.WasCritical,
            };
        }

        private HealRecord ProduceHealingRecord(SpellDispelEvent disEv)
        {
            return new HealRecord
            {
                Timestamp = disEv.TimeStampFromLog,
                PlayerId = disEv.SourceGuid,
                PlayerName = disEv.SourceName,
                TargetId = disEv.TargetUniqueId,
                TargetName = disEv.TargetName,
                SpellId = disEv.SpellId,
                SpellName = disEv.SpellName,
                SpellRemovedId = disEv.SpellIdRemoved,
                SpellRemovedName = disEv.SpellRemovedName,
                Kind = HealKind.Dispel,
            };
        }
    }
}

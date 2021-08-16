using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HealParserApi.Models
{

    public class HealingReport
    {
        public string Id { get; set; }

        public string ReportName { get; set; }

        public List<PlayerRow> PlayerData { get; set; }
    }

  
    public class PlayerRow
    {
        public string Player { get; set; }

        public double TotalScore { get; set; }

        public double ScoreAdjusted { get; set; }

        public double RawHealing { get; set; }

        public double ActualHpHealed { get; set; }

        public double Overhealing { get; set; }

        public double Prevented { get; set; }

        public double OverhealSubtraction { get; set; }

        public double OverhealFromHots { get; set; }

        public double TotalHealingPoints { get; set; }

        public int TotalDispels { get; set; }

        public int TotalHiPriDispels { get; set; }

        public int TotalDispelPoints { get; set; }

        public List<HealRecord> HealingRecords { get; set; }
    }
}

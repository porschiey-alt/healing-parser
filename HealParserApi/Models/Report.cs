using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HealParserApi.Models
{
    public class BaseReport : TableEntity
    {
        // PKEY = user
        // rkey = guid

        public string ReportName { get; set; }

        public string FileName { get; set; }

    }

    public class RawReport
    {
        public string Id { get; set; }

        public string Name { get; set; }
        public List<BaseEvent> Events { get; set; }
    }
}

using HealParserApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HealParserApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportController : ControllerBase
    {
        private readonly IParseReportService parseService;

        public ReportController(IParseReportService parseService)
        {
            this.parseService = parseService;
        }

        [HttpPost]
        [Route("upload")]
        [RequestSizeLimit(100000000)]
        public async Task<ActionResult> UploadLog()
        {
            // todo: author

            var logFile = this.Request.Form.Files.FirstOrDefault();
            if (logFile == null)
            {
                return this.BadRequest("No file was submitted or the file is invalid.");
            }

            var uploader = "SYSTEM";
            var stream = logFile.OpenReadStream();
            var reader = new StreamReader(stream);
            var fileText = reader.ReadToEnd();

            var parser = new Parser(); // new parser each time, they're statefull
            var (allEvents, report) = parser.ParseFile(fileText, logFile.Name, logFile.FileName, uploader);

            var savedReport = await this.parseService.AddReport(report, allEvents);
            var formulatedReport = await this.parseService.GetReport(savedReport.RowKey, uploader);
            return this.Created($"api/report/{savedReport.RowKey}", formulatedReport);
        }

        [HttpGet]
        [Route("{id}")]
        public async Task<ActionResult> FetchReport(string id, bool includeAllRecords = false)
        {
            // todo: permissions
            var uploader = "SYSTEM";
            var report = await this.parseService.GetReport(id, uploader, includeAllRecords);
            if (report == null)
            {
                return this.NotFound("No report was found for that id");
            }
            return this.Ok(report);
        }


    }
}

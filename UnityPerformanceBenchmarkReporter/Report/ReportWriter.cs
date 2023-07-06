using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityPerformanceBenchmarkReporter.Entities;

namespace UnityPerformanceBenchmarkReporter.Report
{
    public class ReportWriter
    {
        private readonly Regex illegalCharacterScrubberRegex = new Regex("[^0-9a-zA-Z]", RegexOptions.Compiled);
        private readonly string noMetadataString = "Metadata not available for any of the test runs.";
        private readonly char pathSeperator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? '\\' : '/';
        private readonly string perfBenchmarkReportName = "UnityPerformanceBenchmark";
        private readonly TestRunMetadataProcessor testRunMetadataProcessor;
        private PerformanceTestRunResult baselineResults;
        private List<string> distinctSampleGroupNames;

        private List<string> distinctTestNames;
        private string perfBenchmarkReportNameFormat;
        private PerformanceTestRunResult[] perfTestRunResults = { };
        private bool thisHasBenchmarkResults;
        private uint thisSigFig;
        private bool anyTestFailures;
        private readonly string nullString = "null";

        public ReportWriter(TestRunMetadataProcessor metadataProcessor)
        {
            testRunMetadataProcessor = metadataProcessor;
        }

        public void WriteReport(PerformanceTestRunResult[] results, uint sigFig = 2,
            string reportDirectoryPath = null, bool hasBenchmarkResults = false)
        {
            if (results != null && results.Length > 0)
            {
                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

                thisSigFig = sigFig;
                thisHasBenchmarkResults = hasBenchmarkResults;
                perfTestRunResults = results;
                baselineResults = results.FirstOrDefault(r => r.IsBaseline);
                SetDistinctTestNames();
                SetDistinctSampleGroupNames();

                var reportDirectory = EnsureBenchmarkDirectory(reportDirectoryPath);
                var benchmarkReportFile = GetBenchmarkReportFile(reportDirectory);
                using (var rw = new StreamWriter(benchmarkReportFile))
                {
                    System.Console.WriteLine($"Writing Report To: {reportDirectory.FullName}");
                    System.Console.WriteLine($"");
                    WriteHtmlReport(rw);
                }
            }
            else
            {
                throw new ArgumentNullException(nameof(results),
                    "PerformanceTestRun results list is empty. No report will be written.");
            }
        }

        private FileStream GetBenchmarkReportFile(DirectoryInfo benchmarkDirectory)
        {
            perfBenchmarkReportNameFormat = "{0}_{1:yyyy-MM-dd_hh-mm-ss-fff}.html";
            var htmlFileName = Path.Combine(benchmarkDirectory.FullName,
                string.Format(perfBenchmarkReportNameFormat, perfBenchmarkReportName, DateTime.Now));
            return TryCreateHtmlFile(htmlFileName);
        }

        private DirectoryInfo EnsureBenchmarkDirectory(string reportDirectoryPath)
        {
            var reportDirPath = !string.IsNullOrEmpty(reportDirectoryPath)
                ? reportDirectoryPath
                : Directory.GetCurrentDirectory();
            var benchmarkDirectory = Directory.CreateDirectory(Path.Combine(reportDirPath, perfBenchmarkReportName));
            return benchmarkDirectory;
        }

        private static FileStream TryCreateHtmlFile(string htmlFileName)
        {
            try
            {
                return File.Create(htmlFileName);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Exception thrown while trying to create report file at {0}:\r\n{1}",
                    htmlFileName, e.Message);
                throw;
            }
        }

        private string GetFullResourceName(string resourceName)
        {
            var assemblyNameParts = Assembly.GetExecutingAssembly().Location.Split(pathSeperator);
            var assemblyName = assemblyNameParts[assemblyNameParts.Length - 1].Split('.')[0];
            return string.Format("{0}.Report.{1}", assemblyName, resourceName);
        }

        private string ScrubStringForSafeForVariableUse(string source)
        {
            return illegalCharacterScrubberRegex.Replace(source, "_");
        }

        private void WriteHtmlReport(StreamWriter streamWriter)
        {
            if (streamWriter == null)
            {
                throw new ArgumentNullException(nameof(streamWriter));
            }

            streamWriter.WriteLine("<!doctype html>");
            streamWriter.WriteLine("<html>");
            WriteHeader(streamWriter);
            WriteBody(streamWriter);
            streamWriter.WriteLine("</html>");
        }

        private void WriteBody(StreamWriter streamWriter)
        {
            streamWriter.WriteLine("<body>");
            WriteLogoWithTitle(streamWriter);
            WriteTestConfig(streamWriter);
            WriteStatMethodTable(streamWriter);
            WriteTestTableWithVisualizations(streamWriter);
            if (anyTestFailures)
            {
                streamWriter.WriteLine("<script>toggleCanvasWithNoFailures();</script>");
            }
            streamWriter.WriteLine("</body>");
        }

        private void WriteTestConfig(StreamWriter streamWriter)
        {
            streamWriter.Write("<table class=\"testconfigtable\">");
            streamWriter.WriteLine("<tr><td class=\"flex\">");
            WriteShowTestConfigButton(streamWriter);
            streamWriter.WriteLine("</td></tr>");
            streamWriter.WriteLine("<tr><td>");
            WriteTestConfigTable(streamWriter);
            streamWriter.WriteLine("</td></tr>");
            streamWriter.Write("</table>");
        }

        private void WriteStatMethodTable(StreamWriter streamWriter)
        {
            streamWriter.WriteLine("<table class=\"statMethodTable\">");
            WriteShowFailedTestsCheckbox(streamWriter);
            WriteStatMethodButtons(streamWriter);
            streamWriter.WriteLine("</table>");
        }

        private static void WriteStatMethodButtons(StreamWriter streamWriter)
        {
            streamWriter.WriteLine(
                "<tr><td><div class=\"buttonheader\">Select statistical method</div><div class=\"buttondiv\"><button id=\"MinButton\" class=\"button\">Min</button></div>&nbsp<div class=\"buttondiv\"><button id=\"MaxButton\" class=\"button\">Max</button></div>&nbsp<div class=\"buttondiv\"><button id=\"MedianButton\" class=\"initialbutton\">Median</button></div>&nbsp<div class=\"buttondiv\"><button id=\"AverageButton\" class=\"button\">Average</button></div></td></tr>");
        }

        private void WriteShowFailedTestsCheckbox(StreamWriter streamWriter)
        {
            streamWriter.WriteLine("<tr><td><div class=\"showedfailedtests\">");
            if (thisHasBenchmarkResults)
            {
                streamWriter.WriteLine("<label id=\"hidefailed\" class=\"containerLabel\">Show failed tests only");

                //var regressed = perfTestRunResults.SelectMany(ptr => ptr.TestResults).SelectMany(t => t.SampleGroupResults).Any(a => a.Regressed);

                if (perfTestRunResults.SelectMany(ptr => ptr.TestResults).SelectMany(t => t.SampleGroupResults).Any(a => a.Regressed))
                {
                    streamWriter.WriteLine("<input type=\"checkbox\" onclick=\"toggleCanvasWithNoFailures()\" checked>");
                }
                else
                {
                    streamWriter.WriteLine("<input type=\"checkbox\" onclick=\"toggleCanvasWithNoFailures()\">");
                }

            }
            else
            {
                streamWriter.WriteLine(
                    "<label id=\"hidefailed\" class=\"disabledContainerLabel\">Show failed tests only");
                streamWriter.WriteLine(
                    "<span class=\"tooltiptext\">No failed tests to show because there are no baseline results.</span>");
                streamWriter.WriteLine("<input type=\"checkbox\" disabled>");
            }

            streamWriter.WriteLine("<span class=\"checkmark\"></span></label></div></td></tr>");
        }

        private void WriteShowTestConfigButton(StreamWriter rw)
        {
	        const string warningBase64 = "data:image/png;base64,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";
            rw.WriteLine(
                "<div class=\"toggleconfigwrapper\"><button id=\"toggleconfig\" class=\"button\" onclick=\"showTestConfiguration()\">{0}Show Test Configuration</button>{1}</div><a title=\"Help\" class=\"help\" href=\"https://github.com/Unity-Technologies/PerformanceBenchmarkReporter/wiki\" target=\"_blank\"><div class=\"helpwrapper\"></div></a>",
                testRunMetadataProcessor.TypeMetadata.Any(m => m.HasMismatches) ||
                testRunMetadataProcessor.TypeMetadata.All(m => m.ValidResultCount == 0)
                    ? $"<image class=\"warning\" src=\"{warningBase64}\" alt=\"Mismatched test configurations present.\"></img>"
                    : string.Empty,
                testRunMetadataProcessor.TypeMetadata.Any(m => m.HasMismatches)
                    ?
                    "<span class=\"configwarning\">Mismatched test configurations present</span>"
                    : testRunMetadataProcessor.TypeMetadata.All(m => m.ValidResultCount == 0)
                        ? string.Format("<span class=\"configwarning\"><div class=\"fieldname\">{0}</div></span>",
                            noMetadataString)
                        : string.Empty);
        }

        private void WriteJavaScript(StreamWriter rw)
        {
            rw.WriteLine("<script>");
            rw.WriteLine("var failColor = \"rgba(255, 99, 132,0.5)\";");
            rw.WriteLine("var passColor = \"rgba(54, 162, 235,0.5)\";");
            rw.WriteLine("var baselineColor = \"rgb(255, 159, 64)\";");
            WriteShowTestConfigurationOnClickEvent(rw);
            WriteToggleCanvasWithNoFailures(rw);
            WriteTestRunArray(rw);
            WriteValueArrays(rw);
            WriteSampleGroupChartDefinitions(rw);
            rw.WriteLine("window.onload = function() {");
            WriteSampleGroupChartConfigs(rw);
            WriteStatMethodButtonEventListeners(rw);
            rw.WriteLine("};");
            rw.WriteLine("</script>");
        }

        private void WriteSampleGroupChartConfigs(StreamWriter rw)
        {
            foreach (var distinctTestName in distinctTestNames)
            {
                var resultsForThisTest = GetResultsForThisTest(distinctTestName);
                foreach (var distinctSampleGroupName in distinctSampleGroupNames)
                {
                    if (!SampleGroupHasSamples(resultsForThisTest, distinctSampleGroupName))
                    {
                        continue;
                    }

                    WriteSampleGroupChartConfig(rw, resultsForThisTest, distinctSampleGroupName, distinctTestName);
                }
            }
        }

        private void WriteSampleGroupChartDefinitions(StreamWriter rw)
        {
            foreach (var distinctTestName in distinctTestNames)
            {
                var resultsForThisTest = GetResultsForThisTest(distinctTestName);
                foreach (var distinctSampleGroupName in distinctSampleGroupNames)
                {
                    WriteSampleGroupChartDefinition(rw, resultsForThisTest, distinctSampleGroupName, distinctTestName);
                }
            }
        }

        private void WriteSampleGroupChartDefinition(StreamWriter rw, List<TestResult> resultsForThisTest,
            string distinctSampleGroupName,
            string distinctTestName)
        {
            if (!SampleGroupHasSamples(resultsForThisTest, distinctSampleGroupName)) return;

            var canvasId = GetCanvasId(distinctTestName, distinctSampleGroupName);
            var format = string.Format("var {0}_data = {{", canvasId);
            rw.WriteLine(format);
            rw.WriteLine("	labels: testRuns,");
            rw.WriteLine("	datasets: [{");
            var resultColors = new StringBuilder();
            resultColors.Append("backgroundColor: [");

            foreach (var testResult in resultsForThisTest)
            {
                if (testResult.SampleGroupResults.Any(r =>
                    ScrubStringForSafeForVariableUse(r.SampleGroupName).Equals(distinctSampleGroupName)))
                {
                    var sampleGroupResult = testResult.SampleGroupResults.First(r =>
                        ScrubStringForSafeForVariableUse(r.SampleGroupName).Equals(distinctSampleGroupName));
                    resultColors.Append(sampleGroupResult.Regressed ? "failColor, " : "passColor, ");
                }
                else
                {
                    resultColors.Append("passColor, ");
                }
            }

            var sampleUnit = GetSampleUnit(resultsForThisTest, distinctSampleGroupName);

            // remove trailing comma
            resultColors.Length = resultColors.Length - 2;

            resultColors.Append("],");
            rw.WriteLine(resultColors.ToString());
            rw.WriteLine("borderWidth: 1,");
            rw.WriteLine("label: \"" + (sampleUnit.Equals("None") ? distinctSampleGroupName : sampleUnit) + "\",");
            rw.WriteLine("legend: {");
            rw.WriteLine("display: true,");
            rw.WriteLine("},");
            rw.WriteLine("data: {0}", string.Format("{0}_Aggregated_Values", canvasId));
            rw.WriteLine("	}");

            if (baselineResults != null)
            {
                rw.WriteLine("	,{");
                rw.WriteLine("borderColor: baselineColor,");
                rw.WriteLine("backgroundColor: baselineColor,");
                rw.WriteLine("borderWidth: 2,");
                rw.WriteLine("fill: false,");
                rw.WriteLine("pointStyle: 'line',");
                rw.WriteLine("label: \"" +
                             (sampleUnit.Equals("None")
                                 ? "Baseline " + distinctSampleGroupName
                                 : "Baseline " + sampleUnit) +
                             "\",");
                rw.WriteLine("legend: {");
                rw.WriteLine("display: true,");
                rw.WriteLine("},");
                rw.WriteLine("data: {0}", string.Format("{0}_Baseline_Values,", canvasId));
                rw.WriteLine("type: 'line'}");
            }

            rw.WriteLine("	]");
            rw.WriteLine("};");
        }

        private void WriteStatMethodButtonEventListeners(StreamWriter rw)
        {
            var statisticalMethods = new List<string> { "Min", "Max", "Median", "Average" };
            foreach (var thisStatMethod in statisticalMethods)
            {
                rw.WriteLine("	document.getElementById('{0}Button').addEventListener('click', function()",
                    thisStatMethod);
                rw.WriteLine("	{");
                foreach (var distinctTestName in distinctTestNames)
                {
                    var resultsForThisTest = GetResultsForThisTest(distinctTestName);
                    foreach (var distinctSampleGroupName in distinctSampleGroupNames)
                    {
                        if (!SampleGroupHasSamples(resultsForThisTest, distinctSampleGroupName)) continue;

                        var canvasId = GetCanvasId(distinctTestName, distinctSampleGroupName);
                        var sampleUnit = GetSampleUnit(resultsForThisTest, distinctSampleGroupName);

                        rw.WriteLine("window.{0}.options.scales.yAxes[0].scaleLabel.labelString = \"{1} {2}\";",
                            canvasId,
                            thisStatMethod, !sampleUnit.Equals("None") ? sampleUnit : distinctSampleGroupName);
                        rw.WriteLine("{0}_data.datasets[0].data = {0}_{1}_Values;", canvasId, thisStatMethod);
                        rw.WriteLine("window.{0}.update();", canvasId);
                    }

                    rw.WriteLine("var a = document.getElementById('{0}Button');", thisStatMethod);
                    rw.WriteLine("a.style.backgroundColor = \"#2196F3\";");
                    var count = 98;
                    foreach (var statMethod in statisticalMethods.Where(m => !m.Equals(thisStatMethod)))
                    {
                        var varName = Convert.ToChar(count);
                        rw.WriteLine("var {0} = document.getElementById('{1}Button');", varName, statMethod);
                        rw.WriteLine("{0}.style.backgroundColor = \"#3e6892\";", varName);
                        count++;
                    }
                }

                rw.WriteLine("	 });");
            }
        }

        private void WriteSampleGroupChartConfig(StreamWriter rw, List<TestResult> resultsForThisTest,
            string distinctSampleGroupName,
            string distinctTestName)
        {
            var aggregationType = GetAggregationType(resultsForThisTest, distinctSampleGroupName);
            var sampleUnit = GetSampleUnit(resultsForThisTest, distinctSampleGroupName);
            var threshold = GetThreshold(resultsForThisTest, distinctSampleGroupName);
            var sampleCount = GetSampleCount(resultsForThisTest, distinctSampleGroupName);
            var canvasId = GetCanvasId(distinctTestName, distinctSampleGroupName);

            rw.WriteLine("Chart.defaults.global.elements.rectangle.borderColor = \'#fff\';");
            rw.WriteLine("var ctx{0} = document.getElementById('{0}').getContext('2d');", canvasId);
            rw.WriteLine("window.{0} = new Chart(ctx{0}, {{", canvasId);
            rw.WriteLine("type: 'bar',");
            rw.WriteLine("data: {0}_data,", canvasId);
            rw.WriteLine("options: {");
            rw.WriteLine("tooltips:");
            rw.WriteLine("{");
            rw.WriteLine("mode: 'index',");
            rw.WriteLine("callbacks: {");
            rw.WriteLine("title: function(tooltipItems, data) {");
            rw.WriteLine("var color = {0}_data.datasets[0].backgroundColor[tooltipItems[0].index];", canvasId);
            rw.WriteLine(
                "return tooltipItems[0].xLabel + (color === failColor ? \" regressed\" : \" within threshold\");},");
            rw.WriteLine("beforeFooter: function(tooltipItems, data) {");
            rw.WriteLine("var std = {0}_Stdev_Values[tooltipItems[0].index];", canvasId);
            rw.WriteLine(
                "var footermsg = ['Threshold: {0}']; footermsg.push('Standard deviation: ' + std); footermsg.push('Sample count: {1}'); return footermsg;}},",
                threshold, sampleCount);
            rw.WriteLine("},");
            rw.WriteLine("footerFontStyle: 'normal'");
            rw.WriteLine("},");
            rw.WriteLine("legend: { display: true},");
            rw.WriteLine("maintainAspectRatio: false,");
            rw.WriteLine("scales: {");
            rw.WriteLine("	yAxes: [{");
            rw.WriteLine("display: true,");
            rw.WriteLine("scaleLabel:");
            rw.WriteLine("{");
            rw.WriteLine("    display: true,");
            rw.WriteLine("    labelString: \"{0} {1}\"", aggregationType,
                !sampleUnit.Equals("None") ? sampleUnit : distinctSampleGroupName);
            rw.WriteLine("},");
            rw.WriteLine("ticks: {");
            rw.WriteLine("suggestedMax: .001,");
            rw.WriteLine("suggestedMin: .0");
            rw.WriteLine("}");
            rw.WriteLine("	}],");
            rw.WriteLine("	xAxes: [{");
            rw.WriteLine("display: true,");
            rw.WriteLine("scaleLabel:");
            rw.WriteLine("{");
            rw.WriteLine("    display: true,");
            rw.WriteLine("    labelString: \"Result File / Execution Time\"");
            rw.WriteLine("}");
            rw.WriteLine("	}]");
            rw.WriteLine("},");
            rw.WriteLine("responsive: true,");
            rw.WriteLine("animation:");
            rw.WriteLine("{");
            rw.WriteLine("    duration: 0 // general animation time");
            rw.WriteLine("},");
            rw.WriteLine("hover:");
            rw.WriteLine("{");
            rw.WriteLine("    animationDuration: 0 // general animation time");
            rw.WriteLine("},");
            rw.WriteLine("responsiveAnimationDuration: 0,");
            rw.WriteLine("title: {");
            rw.WriteLine("	display: true,");
            rw.WriteLine("	text: \"{0}\"", distinctSampleGroupName);
            rw.WriteLine("}");
            rw.WriteLine("}");
            rw.WriteLine("	});");
            rw.WriteLine("	");
        }

        private void WriteShowTestConfigurationOnClickEvent(StreamWriter rw)
        {
            rw.WriteLine("function showTestConfiguration() {");
            rw.WriteLine("	var x = document.getElementById(\"testconfig\");");
            rw.WriteLine("	var t = document.getElementById(\"toggleconfig\");");
            rw.WriteLine("	var img = t.childNodes[0];");
            rw.WriteLine("	if (x.style.display === \"\" || x.style.display === \"none\") {");
            rw.WriteLine("x.style.display = \"block\";");
            rw.WriteLine(
                "	document.getElementById(\"toggleconfig\").innerHTML= (img.outerHTML || \"\") + \"Hide Test Configuration\";");
            rw.WriteLine("	} else {");
            rw.WriteLine("x.style.display = \"none\";");
            rw.WriteLine("	var img = t.childNodes[0];");
            rw.WriteLine(
                "	document.getElementById(\"toggleconfig\").innerHTML= (img.outerHTML || \"\") + \"Show Test Configuration\";");
            rw.WriteLine("	}");
            rw.WriteLine("}");
        }

        private void WriteToggleCanvasWithNoFailures(StreamWriter rw)
        {
            rw.WriteLine("function toggleCanvasWithNoFailures() {");
            rw.WriteLine("	var x = document.getElementsByClassName(\"nofailures\");");
            rw.WriteLine("              for(var i = 0; i < x.length; i++)");
            rw.WriteLine("              {");
            rw.WriteLine("	    if (x[i].style.display === \"none\") {");
            rw.WriteLine("    x[i].getAttribute('style');");
            rw.WriteLine("    x[i].removeAttribute('style');");
            rw.WriteLine("	    } else {");
            rw.WriteLine("    x[i].style.display = \"none\";");
            rw.WriteLine("	    }");
            rw.WriteLine("	}");
            rw.WriteLine("}");
        }

        private void WriteHeader(StreamWriter rw)
        {
            rw.WriteLine("<head>");
            rw.WriteLine("<meta charset=\"utf-8\"/>");
            rw.WriteLine("<title>Unity Performance Benchmark Report</title>");
            // rw.WriteLine("<script src=\"Chart.bundle.js\"></script>");
            // rw.WriteLine("<link rel=\"stylesheet\" href=\"styles.css\">");
            // embed css/js
            WriteChartScript(rw);
            WriteStylesheet(rw);
            rw.WriteLine("<style>");
            rw.WriteLine("canvas {");
            rw.WriteLine("-moz-user-select: none;");
            rw.WriteLine("-webkit-user-select: none;");
            rw.WriteLine("-ms-user-select: none;");
            rw.WriteLine("}");
            rw.WriteLine("</style>");
            WriteJavaScript(rw);
            rw.WriteLine("</head>");
        }

        private void WriteChartScript(StreamWriter rw)
        {
            rw.WriteLine("<script>");
            rw.Flush();
            var resourceName = GetFullResourceName("Chart.bundle.js");
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
	            resource.CopyTo(rw.BaseStream);
            }
            rw.WriteLine("</script>");
        }

        private void WriteStylesheet(StreamWriter rw)
        {
	        rw.WriteLine("<style>");
	        rw.Flush();
            var resourceName = GetFullResourceName("styles.css");
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
	            resource.CopyTo(rw.BaseStream);
            }
	        rw.WriteLine("</style>");
        }

        private void WriteLogoWithTitle(StreamWriter rw)
        {
	        var logoBase64 = GetEmbeddedResourceImageBase64(GetFullResourceName("logo.png"));
            rw.WriteLine("<table class=\"titletable\">");
            rw.WriteLine(
                $"<tr><td class=\"logocell\"><img src=\"data:image/png;base64,{logoBase64}\" alt=\"Unity\" class=\"logo\"></td></tr>");
            rw.WriteLine(
                "<tr><td class=\"titlecell\"><div class=\"title\"><h1>Performance Benchmark Report</h1></div></td></tr>");
            rw.WriteLine("</table>");
        }

        private string GetEmbeddedResourceImageBase64(string resourceName)
        {
	        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
	        using var ms = new MemoryStream();
	        resource.CopyTo(ms);
	        return Convert.ToBase64String(ms.ToArray());
        }

        private void WriteTestTableWithVisualizations(StreamWriter rw)
        {
            rw.WriteLine("<table class=\"visualizationTable\">");
            foreach (var distinctTestName in distinctTestNames)
            {
                WriteResultForThisTest(rw, distinctTestName);
            }

            rw.WriteLine("</table>");
        }

        private void WriteResultForThisTest(StreamWriter rw, string distinctTestName)
        {
            var resultsForThisTest = GetResultsForThisTest(distinctTestName);
            var noTestRegressions = IsNoTestFailures(resultsForThisTest);
            anyTestFailures = anyTestFailures || !noTestRegressions;
            rw.WriteLine("<tr {0}>", noTestRegressions ? "class=\"nofailures\"" : string.Empty);
            rw.WriteLine(
                "<td class=\"testnamecell\"><div class=\"testname {0}\"><p><h5>Test Name:</h5></p><p><h3>{1}</h3></p></div></td></tr>",
                noTestRegressions ? "nofailures" : string.Empty,
                distinctTestName);
            rw.WriteLine(noTestRegressions ? "<tr class=\"nofailures\"><td></td></tr>" : "<tr><td></td></tr>");

            foreach (var distinctSampleGroupName in distinctSampleGroupNames)
            {
                WriteResultForThisSampleGroup(rw, distinctTestName, resultsForThisTest, distinctSampleGroupName);
            }
        }

        private void WriteResultForThisSampleGroup(StreamWriter rw, string distinctTestName,
            List<TestResult> resultsForThisTest,
            string distinctSampleGroupName)
        {
            if (!SampleGroupHasSamples(resultsForThisTest, distinctSampleGroupName))
            {
                return;
            }

            var canvasId = GetCanvasId(distinctTestName, distinctSampleGroupName);
            var noSampleGroupRegressions = !SampleGroupHasRegressions(resultsForThisTest, distinctSampleGroupName);
            rw.WriteLine(
                noSampleGroupRegressions
                    ? "<tr class=\"nofailures\"><td class=\"chartcell nofailures\"><div id=\"container\" class=\"container nofailures\">"
                    : "<tr><td class=\"chartcell\"><div id=\"container\" class=\"container\">");
            rw.WriteLine(
                noSampleGroupRegressions
                    ? "<canvas class=\"nofailures canvas\" id=\"{0}\"></canvas>"
                    : "<canvas class=\"canvas\" id=\"{0}\"></canvas>", canvasId);

            rw.WriteLine("</div></td></tr>");
        }

        private bool IsNoTestFailures(List<TestResult> resultsForThisTest)
        {
            var noTestFailures = true;
            foreach (var distinctSampleGroupName2 in distinctSampleGroupNames)
            {
                if (!SampleGroupHasSamples(resultsForThisTest, distinctSampleGroupName2)) continue;
                noTestFailures = noTestFailures &&
                                 !SampleGroupHasRegressions(resultsForThisTest, distinctSampleGroupName2);
            }

            return noTestFailures;
        }

        private void WriteTestRunArray(StreamWriter rw)
        {
            var runsString = new StringBuilder();
            runsString.Append("var testRuns = [");

            // Write remaining values
            foreach (var performanceTestRunResult in perfTestRunResults)
            {
                runsString.Append(
                    string.Format("['{0}','{1}','{2}'], ",
                        performanceTestRunResult.ResultName,
                        string.Format("{0:MM/dd/yyyy}", performanceTestRunResult.StartTime),
                        string.Format("{0:T}", performanceTestRunResult.StartTime)));
            }

            // Remove trailing comma and space
            runsString.Length = runsString.Length - 2;
            runsString.Append("];");
            rw.WriteLine(runsString.ToString());
        }

        private void WriteValueArrays(StreamWriter rw)
        {
            foreach (var distinctTestName in distinctTestNames)
            {
                var resultsForThisTest = GetResultsForThisTest(distinctTestName);
                foreach (var distinctSampleGroupName in distinctSampleGroupNames)
                {
                    if (!SampleGroupHasSamples(resultsForThisTest, distinctSampleGroupName)) continue;

                    var aggregatedValuesArrayName =
                        string.Format("{0}_{1}_Aggregated_Values", distinctTestName, distinctSampleGroupName);
                    var medianValuesArrayName =
                        string.Format("{0}_{1}_Median_Values", distinctTestName, distinctSampleGroupName);
                    var minValuesArrayName =
                        string.Format("{0}_{1}_Min_Values", distinctTestName, distinctSampleGroupName);
                    var maxValuesArrayName =
                        string.Format("{0}_{1}_Max_Values", distinctTestName, distinctSampleGroupName);
                    var avgValuesArrayName =
                        string.Format("{0}_{1}_Average_Values", distinctTestName, distinctSampleGroupName);
                    var stdevValuesArrayName =
                        string.Format("{0}_{1}_Stdev_Values", distinctTestName, distinctSampleGroupName);

                    var baselineValuesArrayName =
                        string.Format("{0}_{1}_Baseline_Values", distinctTestName, distinctSampleGroupName);

                    var aggregatedValuesArrayString = new StringBuilder();
                    aggregatedValuesArrayString.Append(string.Format("var {0} = [", aggregatedValuesArrayName));

                    var medianValuesArrayString = new StringBuilder();
                    medianValuesArrayString.Append(string.Format("var {0} = [", medianValuesArrayName));

                    var minValuesArrayString = new StringBuilder();
                    minValuesArrayString.Append(string.Format("var {0} = [", minValuesArrayName));

                    var maxValuesArrayString = new StringBuilder();
                    maxValuesArrayString.Append(string.Format("var {0} = [", maxValuesArrayName));

                    var avgValuesArrayString = new StringBuilder();
                    avgValuesArrayString.Append(string.Format("var {0} = [", avgValuesArrayName));

                    var stdevValuesArrayString = new StringBuilder();
                    stdevValuesArrayString.Append(string.Format("var {0} = [", stdevValuesArrayName));

                    var baselineValuesArrayString = new StringBuilder();
                    baselineValuesArrayString.Append(string.Format("var {0} = [", baselineValuesArrayName));

                    var benchmarkResults = perfTestRunResults[0];
                    foreach (var performanceTestRunResult in perfTestRunResults)
                    {
                        var aggregatedDefaultValue = nullString;
                        var medianDefaultValue = nullString;
                        var minDefaultValue = nullString;
                        var maxDefaultValue = nullString;
                        var avgDefaultValue = nullString;
                        var stdevDefaultValue = nullString;
                        var baselineDefaultValue = nullString;

                        if (performanceTestRunResult.TestResults.Any(r =>
                            ScrubStringForSafeForVariableUse(r.TestName).Equals(distinctTestName)))
                        {
                            var testResult =
                                performanceTestRunResult.TestResults.First(r =>
                                    ScrubStringForSafeForVariableUse(r.TestName).Equals(distinctTestName));
                            var sgResult =
                                testResult.SampleGroupResults.FirstOrDefault(r =>
                                    ScrubStringForSafeForVariableUse(r.SampleGroupName)
                                        .Equals(distinctSampleGroupName));
                            aggregatedValuesArrayString.Append(string.Format("'{0}', ",
                                sgResult != null ? sgResult.AggregatedValue.ToString("F" + thisSigFig) : aggregatedDefaultValue));
                            medianValuesArrayString.Append(string.Format("'{0}', ",
                                sgResult != null ? sgResult.Median.ToString("F" + thisSigFig) : medianDefaultValue));
                            minValuesArrayString.Append(string.Format("'{0}', ",
                                sgResult != null ? sgResult.Min.ToString("F" + thisSigFig) : minDefaultValue));
                            maxValuesArrayString.Append(string.Format("'{0}' ,",
                                sgResult != null ? sgResult.Max.ToString("F" + thisSigFig) : maxDefaultValue));
                            avgValuesArrayString.Append(string.Format("'{0}' ,",
                                sgResult != null ? sgResult.Average.ToString("F" + thisSigFig) : avgDefaultValue));
                            stdevValuesArrayString.Append(string.Format("'{0}' ,",
                                sgResult != null ? sgResult.StandardDeviation.ToString("F" + thisSigFig) : stdevDefaultValue));

                            if (benchmarkResults.TestResults
                                .Any(r => ScrubStringForSafeForVariableUse(r.TestName)
                                    .Equals(distinctTestName)))
                            {
                                var resultMatch = benchmarkResults.TestResults
                                    .First(r => ScrubStringForSafeForVariableUse(r.TestName)
                                        .Equals(distinctTestName));
                                var benchmarkSampleGroup = resultMatch.SampleGroupResults.FirstOrDefault(r =>
                                    ScrubStringForSafeForVariableUse(r.SampleGroupName)
                                        .Equals(distinctSampleGroupName));

                                var value = thisHasBenchmarkResults && benchmarkSampleGroup != null
                                    ? benchmarkSampleGroup.BaselineValue.ToString("F" + thisSigFig)
                                    : baselineDefaultValue;

                                baselineValuesArrayString.Append(string.Format("'{0}' ,",
                                    value));
                            }
                            else
                            {
                                baselineValuesArrayString.Append(string.Format("'{0}' ,", baselineDefaultValue));
                            }
                        }
                        else
                        {
                            aggregatedValuesArrayString.Append(string.Format("'{0}', ", aggregatedDefaultValue));
                            medianValuesArrayString.Append(string.Format("'{0}', ", medianDefaultValue));
                            minValuesArrayString.Append(string.Format("'{0}', ", minDefaultValue));
                            maxValuesArrayString.Append(string.Format("'{0}' ,", maxDefaultValue));
                            avgValuesArrayString.Append(string.Format("'{0}' ,", avgDefaultValue));
                            stdevValuesArrayString.Append(string.Format("'{0}' ,", stdevDefaultValue));
                            baselineValuesArrayString.Append(string.Format("'{0}' ,", baselineDefaultValue));
                        }
                    }

                    // Remove trailing commas from string builder
                    aggregatedValuesArrayString.Length = aggregatedValuesArrayString.Length - 2;
                    medianValuesArrayString.Length = medianValuesArrayString.Length - 2;
                    minValuesArrayString.Length = minValuesArrayString.Length - 2;
                    maxValuesArrayString.Length = maxValuesArrayString.Length - 2;
                    avgValuesArrayString.Length = avgValuesArrayString.Length - 2;
                    stdevValuesArrayString.Length = stdevValuesArrayString.Length - 2;
                    baselineValuesArrayString.Length = baselineValuesArrayString.Length - 2;

                    aggregatedValuesArrayString.Append("];");
                    medianValuesArrayString.Append("];");
                    minValuesArrayString.Append("];");
                    maxValuesArrayString.Append("];");
                    avgValuesArrayString.Append("];");
                    stdevValuesArrayString.Append("];");
                    baselineValuesArrayString.Append("];");

                    rw.WriteLine(aggregatedValuesArrayString.ToString());
                    rw.WriteLine(medianValuesArrayString.ToString());
                    rw.WriteLine(minValuesArrayString.ToString());
                    rw.WriteLine(maxValuesArrayString.ToString());
                    rw.WriteLine(avgValuesArrayString.ToString());
                    rw.WriteLine(stdevValuesArrayString.ToString());
                    rw.WriteLine(baselineValuesArrayString.ToString());
                }
            }
        }

        private void WriteTestConfigTable(StreamWriter rw)
        {
            rw.WriteLine("<div id=\"testconfig\" class=\"testconfig\">");
            WriteClassNameWithFields(rw);
            rw.WriteLine("</div></div>");
        }

        private void WriteClassNameWithFields(StreamWriter rw)
        {
            var typeMetadatas = testRunMetadataProcessor.TypeMetadata;
            foreach (var typeMetadata in typeMetadatas)
            {
                rw.WriteLine("<div><hr/></div><div class=\"{0}\">{1}</div><div><hr/></div>",
                    typeMetadata.HasMismatches
                        ? "typenamewarning"
                        : "typename", typeMetadata.TypeName);

                rw.WriteLine("<div class=\"systeminfo\"><pre>");

                var sb = new StringBuilder();

                if (typeMetadata.FieldGroups.Any())
                {
                    foreach (var fieldGroup in typeMetadata.FieldGroups)
                    {
                        if (fieldGroup.HasMismatches)
                        {
                            sb.Append("<div class=\"fieldgroupwarning\">");
                            sb.Append(string.Format("<div class=\"fieldnamewarning\">{0}</div>", fieldGroup.FieldName));
                            sb.Append("<div class=\"fieldvaluewarning\">");
                            sb.Append("<table class=\"warningtable\">");
                            sb.Append("<tr><th>Value</th><th>Result File</th><th>Path</th></tr>");

                            for (var i = 0; i < fieldGroup.Values.Length; i++)
                            {
                                sb.Append(string.Format(
                                    "<tr><td {0} title={4}>{1}</td><td {0}>{2}</td><td {0}>{3}</td></tr>",
                                    i == 0 || !fieldGroup.Values[i].IsMismatched
                                        ? "class=\"targetvalue\""
                                        : string.Empty, fieldGroup.Values[i].Value,
                                    fieldGroup.Values[i].ResultFileName, fieldGroup.Values[i].ResultFileDirectory,
                                    i == 0 ? "\"Configuration used for comparison\"" :
                                    !fieldGroup.Values[i].IsMismatched ? "\"Matching configuration\"" :
                                    "\"Mismatched configuration\""));
                            }

                            sb.Append("</table>");
                            sb.Append("</div></div>");
                        }
                        else
                        {
                            sb.Append("<div class=\"fieldgroup\">");
                            sb.Append(string.Format("<div class=\"fieldname\">{0}</div>", fieldGroup.FieldName));
                            sb.Append(string.Format("<div class=\"fieldvalue\">{0}</div>",
                                fieldGroup.Values.First().Value));
                            sb.Append("</div>");
                        }
                    }
                }
                else
                {
                    sb.Append(string.Format("<div class=\"fieldname\">{0}</div>", noMetadataString));
                }

                rw.WriteLine(sb.ToString());
                rw.WriteLine("</pre></div>");
            }
        }

        private List<TestResult> GetResultsForThisTest(string distinctTestName)
        {
            var resultsForThisTest = new List<TestResult>();
            foreach (var perfTestRunResult in perfTestRunResults)
            {
                if (perfTestRunResult.TestResults.Any(r =>
                    ScrubStringForSafeForVariableUse(r.TestName).Equals(distinctTestName)))
                {
                    resultsForThisTest.Add(
                        perfTestRunResult.TestResults.First(r =>
                            ScrubStringForSafeForVariableUse(r.TestName).Equals(distinctTestName)));
                }
            }

            return resultsForThisTest;
        }

        private string GetSampleUnit(List<TestResult> resultsForThisTest, string sampleGroupName)
        {
            var sampleUnit = "";
            if (resultsForThisTest.First().SampleGroupResults
                .Any(sg => ScrubStringForSafeForVariableUse(sg.SampleGroupName) == sampleGroupName))
            {
                sampleUnit = resultsForThisTest.First().SampleGroupResults
                    .First(sg => ScrubStringForSafeForVariableUse(sg.SampleGroupName) == sampleGroupName).SampleUnit;
            }

            return sampleUnit;
        }

        private double GetThreshold(List<TestResult> resultsForThisTest, string sampleGroupName)
        {
            var threshold = 0.0;
            if (resultsForThisTest.First().SampleGroupResults
                .Any(sg => ScrubStringForSafeForVariableUse(sg.SampleGroupName) == sampleGroupName))
            {
                threshold = resultsForThisTest.First().SampleGroupResults
                    .First(sg => ScrubStringForSafeForVariableUse(sg.SampleGroupName) == sampleGroupName).Threshold;
            }

            return threshold;
        }

        private int GetSampleCount(List<TestResult> resultsForThisTest, string sampleGroupName)
        {
            var sampleCount = 0;
            if (resultsForThisTest.First().SampleGroupResults
                .Any(sg => ScrubStringForSafeForVariableUse(sg.SampleGroupName) == sampleGroupName))
            {
                sampleCount = resultsForThisTest.First().SampleGroupResults
                    .First(sg => ScrubStringForSafeForVariableUse(sg.SampleGroupName) == sampleGroupName).SampleCount;
            }

            return sampleCount;
        }

        private string GetAggregationType(List<TestResult> resultsForThisTest, string sampleGroupName)
        {
            var aggregationType = "";
            if (resultsForThisTest.First().SampleGroupResults
                .Any(sg => ScrubStringForSafeForVariableUse(sg.SampleGroupName) == sampleGroupName))
            {
                aggregationType = resultsForThisTest.First().SampleGroupResults
                    .First(sg => ScrubStringForSafeForVariableUse(sg.SampleGroupName) == sampleGroupName)
                    .AggregationType;
            }

            return aggregationType;
        }

        private bool SampleGroupHasSamples(IEnumerable<TestResult> resultsForThisTest, string distinctSampleGroupName)
        {
            var sampleGroupHasSamples = resultsForThisTest.SelectMany(r => r.SampleGroupResults).Any(sg =>
                ScrubStringForSafeForVariableUse(sg.SampleGroupName) == distinctSampleGroupName);
            return sampleGroupHasSamples;
        }

        private bool SampleGroupHasRegressions(IEnumerable<TestResult> resultsForThisTest,
            string distinctSampleGroupName)
        {
            var failureInSampleGroup = resultsForThisTest.SelectMany(r => r.SampleGroupResults)
                .Where(sg => ScrubStringForSafeForVariableUse(sg.SampleGroupName) == distinctSampleGroupName)
                .Any(r => r.Regressed);

            return failureInSampleGroup;
        }

        private string GetCanvasId(string distinctTestName, string distinctSgName)
        {
            return string.Format("{0}_{1}", distinctTestName, distinctSgName);
        }

        private void SetDistinctSampleGroupNames()
        {
            var sgNames = new List<string>();

            foreach (var performanceTestRunResult in perfTestRunResults)
            {
                foreach (var testResult in performanceTestRunResult.TestResults)
                {
                    sgNames.AddRange(testResult.SampleGroupResults.Select(r => r.SampleGroupName));
                }
            }

            var tempDistinctSgNames = sgNames.Distinct().ToArray();
            for (var i = 0; i < tempDistinctSgNames.Length; i++)
            {
                tempDistinctSgNames[i] = ScrubStringForSafeForVariableUse(tempDistinctSgNames[i]);
            }

            distinctSampleGroupNames = tempDistinctSgNames.ToList();
            distinctSampleGroupNames.Sort();
        }

        private void SetDistinctTestNames()
        {
            var testNames = new List<string>();

            foreach (var performanceTestRunResult in perfTestRunResults)
            {
                testNames.AddRange(performanceTestRunResult.TestResults.Select(tr => tr.TestName));
            }

            var tempDistinctTestNames = testNames.Distinct().ToArray();
            for (var i = 0; i < tempDistinctTestNames.Length; i++)
            {
                tempDistinctTestNames[i] = ScrubStringForSafeForVariableUse(tempDistinctTestNames[i]);
            }

            distinctTestNames = tempDistinctTestNames.ToList();
            distinctTestNames.Sort();
        }
    }
}
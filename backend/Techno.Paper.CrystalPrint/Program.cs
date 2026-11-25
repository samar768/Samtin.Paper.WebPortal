using System;
using System.Data;
using System.IO;
using System.Text;
using System.Xml;
using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.Shared;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--worker")
            return RunWorker();
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Expected input XML and output PDF paths.");
            return 2;
        }

        return Export(args[0], args[1]);
    }

    private static int RunWorker()
    {
        Console.InputEncoding = new UTF8Encoding(false);
        string input;
        while ((input = Console.ReadLine()) != null)
        {
            var output = Console.ReadLine();
            if (output == null) return 2;
            if (Export(input, output) != 0) return 1;
            Console.WriteLine("OK");
            Console.Out.Flush();
        }
        return 0;
    }

    private static int Export(string input, string output)
    {
        try
        {
            using (var data = new DataSet())
            {
                using (var reader = XmlReader.Create(input, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 4_000_000
                }))
                    data.ReadXml(reader, XmlReadMode.ReadSchema);

                using (var report = new ReportDocument())
                {
                    report.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SaleOrder.rpt"),
                        OpenReportMethod.OpenReportByTempCopy);
                    report.SetDataSource(data);
                    report.ExportToDisk(ExportFormatType.PortableDocFormat, output);
                    report.Close();
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().Name);
            return 1;
        }
    }
}

using NUnit.Framework;
using OpenXMLXLSXImporter;
using OpenXMLXLSXImporter.Builders;
using OpenXMLXLSXImporter.CellData;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SSUT
{
    public class Tests
    {
        public const string SHEET1 = "Sheet1";
        public const string SHEET2 = "Sheet2";
        public const string TEST_FILE = "tesxtcel.xlsx";
        Stream stream = null;
        IExcelImporter importer = null;

        [SetUp]
        public void Setup()
        {
            stream = File.OpenRead(TEST_FILE);
            importer = new ExcelImporter(stream);
        }
        [TearDown]
        public void TearDown()
        {
            importer?.Dispose();
            stream?.Close();
            stream?.Dispose();
        }

        private class TitleColumnTest : ISheetProperties
        {
            private ISpreadSheetInstructionKey title;

            public string Sheet => SHEET1;

            public void LoadConfig(ISpreadSheetInstructionBuilder builder)
            {
                title = builder.LoadSingleCell(1, "A");
            }

            public async Task ResultsProcessed(ISpreadSheetQueryResults query)
            {
                ICellData d = await (await query.GetResults(title)).FirstOrDefault();

                Assert.IsTrue(d.Content() == "A Header");
            }
        }

        [Test]
        public void LoadTitleCellTest()
        {
            TitleColumnTest titleColumnTest = new TitleColumnTest();
            importer.Process(titleColumnTest).GetAwaiter().GetResult();
        }
    }
}
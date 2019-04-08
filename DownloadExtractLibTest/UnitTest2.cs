using System.IO;
using DownloadExtractLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DownloadExtractLibTest
{
    [TestClass]
    public class UnitTest2
    {
        const string FOLDER = @"C:\temp\stage";

        [TestMethod]
        public void TestMethod1()
        {
            // Arrange
            var stage = new DownloadParseManager(FOLDER);
            stage.DownloadPage("https://www.oreilly.com/library/view/mastering-akka/9781786465023/ch03s03.html");

            var f = File.Exists(FOLDER + @"\ch03s03.html");
            Assert.IsTrue(f);
        }
    }
}

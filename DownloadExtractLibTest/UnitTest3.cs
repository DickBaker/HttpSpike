using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DownloadExtractLibTest
{
    [TestClass]
    public class UnitTest3
    {
        [TestMethod]
        public void TestMethod1()
        {
            var myUri = new Uri("http://msdn.microsoft.com/abc/def/ghi.html");
            Console.WriteLine(myUri.AbsolutePath);
        }
    }
}

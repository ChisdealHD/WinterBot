using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace WinterBotTests
{
    [TestClass]
    public class HelperTests
    {
        [TestMethod]
        public void StandardUrls()
        {
            string googleStr = "one two three http://www.google.com/one/two/three four five one two three http://google.com/one/two/three four five www.google.com/one/two/three and google.com/one/two/three?v=four";
            
            Url[] googleUrls = googleStr.FindUrls();
            Assert.AreEqual(4, googleUrls.Length);

            // http://www.google.com/one/two/three
            Url google = googleUrls[0];
            Assert.AreEqual("com", google.Extension);
            Assert.AreEqual("google.com", google.Domain);
            Assert.AreEqual("www.google.com/one/two/three", google.FullUrl);
            Assert.AreEqual(google.FullUrl, google.ToString());


            // http://google.com/one/two/three
            google = googleUrls[1];
            Assert.AreEqual("com", google.Extension);
            Assert.AreEqual("google.com", google.Domain);
            Assert.AreEqual("google.com/one/two/three", google.FullUrl);
            Assert.AreEqual(google.FullUrl, google.ToString());

            // www.google.com/one/two/three
            google = googleUrls[2];
            Assert.AreEqual("com", google.Extension);
            Assert.AreEqual("google.com", google.Domain);
            Assert.AreEqual("www.google.com/one/two/three", google.FullUrl);
            Assert.AreEqual(google.FullUrl, google.ToString());

            // google.com/one/two/three?v=four
            google = googleUrls[3];
            Assert.AreEqual("com", google.Extension);
            Assert.AreEqual("google.com", google.Domain);
            Assert.AreEqual("google.com/one/two/three?v=four", google.FullUrl);
            Assert.AreEqual(google.FullUrl, google.ToString());
        }

        [TestMethod]
        public void IsRegex()
        {
            Assert.IsFalse("soundcloud.com/search?q=Phizax%2C%20Carlem%20Shake".IsRegex());
            Assert.IsTrue("teamliquid.net/.*(userfiles)|(profile)|(image)".IsRegex());
        }
    }
}
